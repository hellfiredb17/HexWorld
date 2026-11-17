/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Net.Sockets;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	public enum SendFailureReason { None, NotConnected, QueueFull, Cancelled, TransportError }
	public readonly record struct SendResult(bool Success, SendFailureReason FailureReason, string? Error = null, bool TransportWritten = true)
	{
		public static SendResult Sent => new(true, SendFailureReason.None);
		public static SendResult Superseded => new(true, SendFailureReason.None, null, false);
	}

	/// <summary>
	/// Represents an active network connection to a client, managing communication, session state, and message handling
	/// for a single player in the server.
	/// </summary>
	/// <remarks>A ClientConnection handles the lifecycle of a client's connection, including receiving and
	/// processing packets, sending responses, and managing player session state. It is responsible for reading incoming
	/// data, dispatching messages based on packet type, and coordinating session assignment or reconnection. Each instance
	/// is associated with a single client and is not intended to be reused for multiple clients. Thread safety is ensured
	/// for outgoing messages via internal locking. Instances should be properly disconnected using DisconnectAsync to
	/// release network resources and update session state.</remarks>
	public class ClientConnection
	{
		//---- Events
		//-----------
		public event Action<int, PacketType, byte[]>? PacketReceived;
		public event Action<int>? PlayerConnected;
		public event Action<int>? PlayerReconnected;
		public event Action<int, ConnectionCloseReason>? ConnectionLost;

		//---- Variables
		//--------------
		private readonly ITransportConnection _connection;
		private readonly object _outboundLock = new();
		private readonly LinkedList<OutboundSendRequest> _outboundQueue = new();
		private readonly SemaphoreSlim _outboundSignal = new(0);
		private readonly CancellationTokenSource _outboundCts = new();
		private readonly Task _writerTask;
		private readonly Queue<long> _packetTimestamps = new();
		private readonly object _packetRateLock = new();
		private readonly ConnectionManager _server;
		private readonly ILogger _logger;
		private CancellationTokenSource? _heartbeatWatchdogCts;
		private int _heartbeatWatchdogVersion;
		private int _disconnectStarted;
		private int _invalidPacketCount;
		[AllowNull] private PlayerSession _session;

		/// <summary>
		/// Initializes a new instance of the <see cref="ClientConnection"/> class.
		/// </summary>		
		public ClientConnection(TcpClient client, ConnectionManager server, ILogger? logger = null)
			: this(new TcpTransportConnection(client), server, logger)
		{
		}

		public ClientConnection(ITransportConnection connection, ConnectionManager server, ILogger? logger = null)
		{
			_connection = connection ?? throw new ArgumentNullException(nameof(connection));
			_server = server;
			_logger = logger ?? NullLogger.Instance;
			_writerTask = Task.Run(RunWriterAsync);
		}

		//---- Public
		//-----------
		/// <summary>
		/// Asynchronously sends the specified data packet over the network stream.
		/// </summary>
		/// <remarks>This method is thread-safe and ensures that only one send operation occurs at a time. If an error
		/// occurs during the send operation, the exception is logged to the console, but not rethrown.</remarks>
		/// <param name="packet">The byte array containing the data to send. Cannot be null.</param>
		/// <returns>A task that represents the asynchronous send operation.</returns>
		public Task<SendResult> SendAsync(byte[] packet, CancellationToken token = default)
		{
			ReadNetworkPacketResult parsed = SignalShared.ReadNetworkDatagram(packet);
			if (!parsed.Success) throw new ArgumentException("A valid framed HawkNet packet is required.", nameof(packet));
			return SendAsync(packet, PacketDeliveryPolicy.GetDefault(parsed.PacketType), token);
		}

		public Task<SendResult> SendAsync(byte[] packet, DeliveryOptions delivery, CancellationToken token = default)
		{
			if (packet is null) throw new ArgumentNullException(nameof(packet));
			if (token.IsCancellationRequested) return Task.FromResult(new SendResult(false, SendFailureReason.Cancelled));
			var request = new OutboundSendRequest(packet, delivery, token);
			bool queueFull = false;
			lock (_outboundLock)
			{
				if (Volatile.Read(ref _disconnectStarted) == 1 || !_connection.IsConnected)
					return Task.FromResult(new SendResult(false, SendFailureReason.NotConnected));
				if (_connection is IUdpDeliveryMetricsSource && delivery.Mode == UdpDeliveryMode.UnreliableSequenced)
				{
					for (LinkedListNode<OutboundSendRequest>? node = _outboundQueue.Last; node is not null; node = node.Previous)
					{
						if (node.Value.Delivery.Mode != UdpDeliveryMode.UnreliableSequenced || node.Value.Delivery.Channel != delivery.Channel) continue;
						OutboundSendRequest superseded = node.Value;
						_outboundQueue.Remove(node);
						superseded.Completion.TrySetResult(SendResult.Superseded);
						(_connection as IUdpDeliveryMetricsRecorder)?.RecordSupersededSend();
						break;
					}
				}
				if (_outboundQueue.Count >= _server.OutboundQueueCapacity)
					queueFull = true;
				else
					_outboundQueue.AddLast(request);
			}
			if (queueFull)
			{
				_ = DisconnectAsync(ConnectionCloseReason.OutboundQueueFull);
				return Task.FromResult(new SendResult(false, SendFailureReason.QueueFull, "Outbound queue is full."));
			}
			_outboundSignal.Release();
			return request.Completion.Task;
		}

		private async Task RunWriterAsync()
		{
			try
			{
				while (!_outboundCts.IsCancellationRequested)
				{
					await _outboundSignal.WaitAsync(_outboundCts.Token);
					OutboundSendRequest? request;
					lock (_outboundLock)
						if (_outboundQueue.First is null) request = null;
						else { request = _outboundQueue.First.Value; _outboundQueue.RemoveFirst(); }
					if (request is null) continue;
					if (request.Token.IsCancellationRequested)
					{
						request.Completion.TrySetResult(new SendResult(false, SendFailureReason.Cancelled));
						continue;
					}
					try
					{
						await _connection.SendAsync(request.Packet, request.Delivery, request.Token);
						request.Completion.TrySetResult(SendResult.Sent);
					}
					catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
					{
						request.Completion.TrySetResult(new SendResult(false, SendFailureReason.Cancelled));
					}
					catch (Exception exception)
					{
						request.Completion.TrySetResult(new SendResult(false, SendFailureReason.TransportError, exception.Message));
						_logger.Exception(exception, "Outbound transport write failed.", nameof(ClientConnection));
						_ = DisconnectAsync(ConnectionCloseReason.SendFailed);
						return;
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Expected during disconnect.
			}
			finally
			{
				lock (_outboundLock)
				{
					while (_outboundQueue.Count > 0)
					{
						OutboundSendRequest request = _outboundQueue.First!.Value;
						_outboundQueue.RemoveFirst();
						request.Completion.TrySetResult(new SendResult(false, SendFailureReason.NotConnected));
					}
				}
			}
		}

		/// <summary>
		/// Asynchronously disconnects the client and releases associated network resources.
		/// </summary>
		/// <remarks>If a session is active, the method updates the session state to reflect the disconnection and
		/// records the time of last activity. The session object is retained for potential reconnection scenarios.</remarks>
		/// <returns>A task that represents the asynchronous disconnect operation.</returns>
		public async Task DisconnectAsync(ConnectionCloseReason reason = ConnectionCloseReason.ClientDisconnected)
		{
			if(Interlocked.Exchange(ref _disconnectStarted, 1) == 1)
			{
				_logger.DebugLog(
					$"Disconnect already started for player {_session?.PlayerId.ToString() ?? "unassigned"}. Ignoring reason {reason}.",
					nameof(ClientConnection));
				return;
			}

			_logger.Warning(
				$"DisconnectAsync starting for player {_session?.PlayerId.ToString() ?? "unassigned"} reason={reason}.",
				nameof(ClientConnection));
			CancelHeartbeatWatchdog();
			_outboundCts.Cancel();

			try
			{
				await _connection.DisconnectAsync();
			}
			catch(Exception ex)
			{
				_logger.Exception(ex, "Error on shutdown.", nameof(ClientConnection));
			}
			try { await _writerTask; }
			catch (OperationCanceledException) { }
			catch (Exception ex) { _logger.Exception(ex, "Outbound writer shutdown failed.", nameof(ClientConnection)); }

			if(_session is not null
				&& _server.TryMarkConnectionLost(_session, this, reason, out int playerId))
			{
				_logger.Log($"Player {playerId} disconnected. Reason: {reason}.", nameof(ClientConnection));

				// send event
				ConnectionLost?.Invoke(playerId, reason);
			}			
		}

		/// <summary>
		/// Asynchronously handles communication with the connected client, processing incoming packets until the client
		/// disconnects or an error occurs.
		/// </summary>
		/// <remarks>This method continuously reads and processes packets from the client while the connection remains
		/// active. If the client disconnects or an exception is thrown, the connection is closed and resources are released.
		/// Any exceptions encountered during processing are logged, and the client is disconnected in all cases.</remarks>
		/// <returns>A task that represents the asynchronous operation of handling the client connection.</returns>
		public async Task HandleClientAsync()
		{
			var closeReason = ConnectionCloseReason.ClientDisconnected;
			using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(_server.ShutdownToken);
			handshakeCts.CancelAfter(_server.HandshakeTimeout);
			try
			{
				while(_connection.IsConnected)
				{
					CancellationToken readToken = _session is null ? handshakeCts.Token : CancellationToken.None;
					var result = await _connection.ReadPacketAsync(readToken);
					if(!result.Success)
					{
						closeReason = GetCloseReasonFromReadFailure(result.ErrorMsg);
						LogReadFailure(result.ErrorMsg, closeReason);
						break;
					}			

					_server.PacketCounters.RecordInbound(result.PacketType, GetPacketByteCount(result.Payload));
					if(IsPacketRateLimited())
					{
						closeReason = ConnectionCloseReason.PacketRateLimitExceeded;
						_logger.Warning(
							$"Player {_session?.PlayerId.ToString() ?? "unassigned"} exceeded packet rate limit " +
							$"{_server.MaxPacketsPerWindow}/{_server.PacketRateWindow.TotalSeconds:0.#}s.",
							nameof(ClientConnection));
						break;
					}

					await ProcessPacket(result.PacketType, result.Payload);
				}
			}
			catch(OperationCanceledException) when (_session is null && handshakeCts.IsCancellationRequested)
			{
				closeReason = ConnectionCloseReason.HandshakeTimeout;
				_logger.Warning("Unauthenticated connection exceeded the handshake deadline.", nameof(ClientConnection));
			}
			catch(IOException ex)
			{
				_logger.DebugLog($"Client connection closed during read: {ex.Message}", nameof(ClientConnection));
				closeReason = ConnectionCloseReason.ClientDisconnected;
			}
			catch(SocketException ex)
			{
				_logger.DebugLog($"Client socket closed during read: {ex.Message}", nameof(ClientConnection));
				closeReason = ConnectionCloseReason.ClientDisconnected;
			}
			catch(ObjectDisposedException ex)
			{
				_logger.DebugLog($"Client transport disposed during read: {ex.Message}", nameof(ClientConnection));
				closeReason = ConnectionCloseReason.ClientDisconnected;
			}
			catch(Exception ex)
			{
				_logger.Exception(ex, "Error handling client.", nameof(ClientConnection));
				closeReason = ConnectionCloseReason.ProtocolError;
			}
			finally
			{
				_logger.Warning(
					$"HandleClientAsync exiting for player {_session?.PlayerId.ToString() ?? "unassigned"} closeReason={closeReason} connected={_connection.IsConnected}.",
					nameof(ClientConnection));
				await DisconnectAsync(closeReason);
			}
		}

		//---- Private
		//------------
		/// <summary>
		/// Processes an incoming network packet based on its type and payload, updating session state or broadcasting
		/// messages as appropriate.
		/// </summary>
		/// <remarks>If the session is not yet established for the connection, a new player session is created and
		/// assigned. For reconnect requests, the payload must contain a valid player ID as a 4-byte integer. Chat messages
		/// are broadcast to all connected clients. The method updates the session's last active time for relevant packet
		/// types.</remarks>
		/// <param name="packetType">The type of packet received, indicating the action to perform (such as heartbeat, player input, reconnect request,
		/// or chat message).</param>
		/// <param name="payload">The raw byte array containing the packet's data. The expected format and content depend on the specified packet
		/// type.</param>
		/// <returns>A task that represents the asynchronous operation of processing the packet.</returns>
		private async Task ProcessPacket(PacketType packetType, byte[] payload)
		{
			if (payload.Length > _server.MaxPayloadBytes(packetType))
			{
				await HandleInvalidPacketAsync($"Payload for {packetType} exceeds {_server.MaxPayloadBytes(packetType)} bytes.");
				return;
			}

			if (_session is null && packetType is not PacketType.HandshakeRequest and not PacketType.ReconnectRequest)
			{
				_logger.Warning($"Packet {packetType} received before handshake.", nameof(ClientConnection));
				await HandleInvalidPacketAsync($"Packet {packetType} received before handshake.");
				return;
			}
			if (_session is not null && packetType is PacketType.HandshakeRequest or PacketType.ReconnectRequest)
			{
				await HandleInvalidPacketAsync($"Authentication packet {packetType} received after session establishment.");
				return;
			}
			if (packetType == PacketType.HandshakeRequest && payload.Length != 0)
			{
				await HandleInvalidPacketAsync("Handshake request payload must be empty.");
				return;
			}

			switch(packetType)
			{
				case PacketType.HeartBeat:
					if(_session is not null)
					{
						if(!MarkActivity())
						{
							await DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
						}
						return;
					}
					break;
				case PacketType.PlayerInput:
					if (_session is not null)
					{
						if(!MarkActivity())
						{
							await DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
							return;
						}
					}					
					break;
				case PacketType.ReconnectRequest:
					// Client provides player id and reconnect token to reconnect.
					if(payload.Length != ConnectionManager.ReconnectPayloadSize)
					{
						await DisconnectAsync(ConnectionCloseReason.InvalidReconnectPayload);
						return;
					}

					if(payload.Length == ConnectionManager.ReconnectPayloadSize)
					{
						int oldId = BitConverter.ToInt32(payload, 0);
						byte[] reconnectToken = new byte[ConnectionManager.ReconnectTokenSize];
						Array.Copy(payload, sizeof(int), reconnectToken, 0, reconnectToken.Length);
						if(_server.TryReconnectSession(oldId, reconnectToken, this, out var existingSession))
						{
							_session = existingSession;
							ResetHeartbeatWatchdog();
							if(!_server.IsCurrentConnection(existingSession, this))
							{
								await DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
								return;
							}

							_logger.Log($"Player {oldId} reconnected.", nameof(ClientConnection));

							// send connection event
							PlayerReconnected?.Invoke(oldId);
							return;
						}
					}
					await DisconnectAsync(ConnectionCloseReason.InvalidReconnectToken);
					return;
				case PacketType.HandshakeRequest:
					if(_session is null)
					{
						_logger.Log("Handshake request received for new session.", nameof(ClientConnection));
						if(!_server.TryCreateSession(this, out var newSession))
						{
							_logger.Warning("Handshake rejected because max players was reached.", nameof(ClientConnection));
							await DisconnectAsync(ConnectionCloseReason.MaxPlayersReached);
							return;
						}

						_session = newSession;
						ResetHeartbeatWatchdog();
						_logger.Log($"New player assigned {_session.PlayerId}.", nameof(ClientConnection));

						PlayerConnected?.Invoke(_session.PlayerId);
						return;
					}

					if(!MarkActivity())
					{
						await DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
						return;
					}
					break;
				case PacketType.ChatMessage:
					if (_session is not null)
					{
						if(!MarkActivity())
						{
							await DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
							return;
						}
					}

					break;
				case PacketType.DisplayNameUpdate:
					if(_session is not null)
					{
						if(!MarkActivity())
						{
							await DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
							return;
						}
					}

					break;
				case PacketType.ChatLobbyReadyRequest:
					if(_session is not null)
					{
						_logger.Log($"Chat lobby ready request received from player {_session.PlayerId}.", nameof(ClientConnection));
						if(!MarkActivity())
						{
							await DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
							return;
						}
					}

					break;
				case PacketType.MatchCommandRequest:
					if(_session is not null)
					{
						_logger.Log($"Match command request received from player {_session.PlayerId}.", nameof(ClientConnection));
						if(!MarkActivity())
						{
							await DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
							return;
						}
					}

					break;
				case PacketType.GameStateMessage:
					if(_session is not null)
					{
						_logger.Log($"Game-state message received from player {_session.PlayerId} payloadBytes={payload.Length}.", nameof(ClientConnection));
						if(!MarkActivity())
						{
							await DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
							return;
						}
					}

					break;
				default:
					_logger.Warning($"Unsupported packet type {packetType}.", nameof(ClientConnection));
					await HandleInvalidPacketAsync($"Unsupported packet type {packetType}.");
					return;
			}

			if(_session is null)
			{
				_logger.Warning($"Packet {packetType} received before handshake.", nameof(ClientConnection));
				await HandleInvalidPacketAsync($"Packet {packetType} received before handshake.");
				return;
			}

			// send event
			PacketReceived?.Invoke(_session.PlayerId, packetType, payload);
		}

		private async Task HandleInvalidPacketAsync(string reason)
		{
			int invalidPacketCount = Interlocked.Increment(ref _invalidPacketCount);
			_logger.Warning(
				$"{reason} Invalid packet count {invalidPacketCount}/{_server.MaxInvalidPacketsPerConnection}.",
				nameof(ClientConnection));

			if(invalidPacketCount >= _server.MaxInvalidPacketsPerConnection)
			{
				await DisconnectAsync(ConnectionCloseReason.TooManyInvalidPackets);
			}
		}

		private bool MarkActivity()
		{
			if(_session is null)
			{
				return false;
			}

			if(!_server.TryMarkSessionActivity(_session, this))
			{
				return false;
			}

			ResetHeartbeatWatchdog();
			return true;
		}

		private static ConnectionCloseReason GetCloseReasonFromReadFailure(string? errorMessage)
		{
			if (errorMessage?.Contains("Reliable UDP delivery has failed", StringComparison.OrdinalIgnoreCase) == true)
			{
				return ConnectionCloseReason.ReliableDeliveryFailed;
			}
			if(!string.IsNullOrWhiteSpace(errorMessage)
				&& errorMessage.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
			{
				return ConnectionCloseReason.ClientDisconnected;
			}

			return ConnectionCloseReason.ProtocolError;
		}

		private void LogReadFailure(string? errorMessage, ConnectionCloseReason closeReason)
		{
			if(closeReason == ConnectionCloseReason.ProtocolError)
			{
				_server.Diagnostics?.RecordMalformedPacketRead();
				_logger.Warning(
					$"Malformed packet or protocol read failure. Closing connection. Error: {errorMessage}",
					nameof(ClientConnection));
				return;
			}

			_logger.DebugLog(errorMessage ?? "Client disconnected.", nameof(ClientConnection));
		}

		private bool IsPacketRateLimited()
		{
			long now = Stopwatch.GetTimestamp();
			long oldestAllowed = now - (long)(_server.PacketRateWindow.TotalSeconds * Stopwatch.Frequency);

			lock(_packetRateLock)
			{
				while(_packetTimestamps.Count > 0 && _packetTimestamps.Peek() <= oldestAllowed)
				{
					_packetTimestamps.Dequeue();
				}

				if(_packetTimestamps.Count >= _server.MaxPacketsPerWindow)
				{
					return true;
				}

				_packetTimestamps.Enqueue(now);
				return false;
			}
		}

		private static int GetPacketByteCount(byte[] payload)
		{
			return SignalShared.LengthPrefixSize + SignalShared.HeaderSize + payload.Length;
		}

		private void ResetHeartbeatWatchdog()
		{
			if(_session is null)
			{
				return;
			}

			int version = Interlocked.Increment(ref _heartbeatWatchdogVersion);
			var watchdogCts = new CancellationTokenSource();
			var previousCts = Interlocked.Exchange(ref _heartbeatWatchdogCts, watchdogCts);
			previousCts?.Cancel();
			previousCts?.Dispose();

			_ = RunHeartbeatDeadlineAsync(_session, version, watchdogCts.Token);
		}

		private async Task RunHeartbeatDeadlineAsync(PlayerSession session, int version, CancellationToken token)
		{
			try
			{
				await Task.Delay(_server.HeartbeatTimeout, token);
			}
			catch(OperationCanceledException)
			{
				return;
			}

			if(token.IsCancellationRequested || version != Volatile.Read(ref _heartbeatWatchdogVersion))
			{
				return;
			}

			if(!_server.IsCurrentConnection(session, this))
			{
				return;
			}

			_logger.Warning($"Player {session.PlayerId} heartbeat timed out.", nameof(ClientConnection));
			await DisconnectAsync(ConnectionCloseReason.HeartbeatTimeout);
		}

		private void CancelHeartbeatWatchdog()
		{
			Interlocked.Increment(ref _heartbeatWatchdogVersion);
			var watchdogCts = Interlocked.Exchange(ref _heartbeatWatchdogCts, null);
			watchdogCts?.Cancel();
			watchdogCts?.Dispose();
		}

		private sealed class OutboundSendRequest
		{
			public OutboundSendRequest(byte[] packet, DeliveryOptions delivery, CancellationToken token)
			{
				Packet = packet;
				Delivery = delivery;
				Token = token;
				Completion = new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			}
			public byte[] Packet { get; }
			public DeliveryOptions Delivery { get; }
			public CancellationToken Token { get; }
			public TaskCompletionSource<SendResult> Completion { get; }
		}

	} // end class
} // end namespace
