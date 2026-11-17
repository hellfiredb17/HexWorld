/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Server;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public enum GameClientState
	{
		Disconnected,
		Connecting,
		Connected,
		Reconnecting,
		Faulted
	}

	/// <summary>
	/// Client for a HawkNet game server. Owns the TCP connection, receive loop, reconnect token, and heartbeat.
	/// </summary>
	public class GameClient : IService, IDisposable
	{
		//---- Events
		//-----------
		public event Action<PacketType, byte[]>? PacketReceived;
		public event Action? Connected;
		public event Action? Reconnected;
		public event Action? Disconnected;
		public event Func<HandshakeResponse, CancellationToken, Task>? HandshakeResponseReceived;

		//---- Variables
		//--------------
		private readonly GameClientConfig _config;
		private readonly IPayloadCodec _payloadCodec;
		private readonly IClientTransport _transport;
		private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
		private readonly SemaphoreSlim _sendLock = new(1, 1);
		private CancellationTokenSource? _connectionCts;
		private Task? _receiveTask;
		private Task? _heartbeatTask;
		private TaskCompletionSource<bool>? _sessionReadyTcs;
		private byte[] _reconnectToken = Array.Empty<byte>();
		private int _disconnectNotified;
		private bool _disposed;
		private ServiceState _serviceState = ServiceState.NotInitialized;

		//---- Properties
		//---------------
		public GameClientState State { get; private set; } = GameClientState.Disconnected;
		ServiceState IService.State => _serviceState;
		public bool IsConnected => State == GameClientState.Connected && _transport.IsConnected;
		public int? PlayerId { get; private set; }
		public bool HasReconnectToken => _reconnectToken.Length == ConnectionManager.ReconnectTokenSize;
		public string CurrentGameStateId { get; private set; } = string.Empty;
		public byte[] CurrentGameStatePayload { get; private set; } = Array.Empty<byte>();
		public ClientRuntimeManifest CurrentRuntimeManifest { get; private set; } = new();
		public IClientTransport Transport => _transport;
		public UdpDeliveryMetricsSnapshot UdpDeliveryMetrics =>
			(_transport as IUdpDeliveryMetricsSource)?.GetUdpDeliveryMetrics() ?? UdpDeliveryMetricsSnapshot.Empty;

		//---- Constructor
		//----------------
		public GameClient(string host, int port)
			: this(new GameClientConfig
			{
				Server = new GameClientServerSettings
				{
					Host = host,
					Port = port
				}
			})
		{
		}

		public GameClient(
			GameClientConfig config,
			IPayloadCodec? payloadCodec = null,
			IClientTransport? transport = null)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			IReadOnlyList<string> udpErrors = UdpDeliveryConfiguration.Validate(config.Networking.UdpDelivery);
			if (udpErrors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, udpErrors), nameof(config));
			_payloadCodec = payloadCodec ?? new JsonPayloadCodec();
			_transport = transport ?? config.Networking.Protocol switch
			{
				TransportProtocol.Tcp => new TcpClientTransport(config),
				TransportProtocol.Udp => new UdpClientTransport(config),
				_ => throw new NotSupportedException($"Client transport '{config.Networking.Protocol}' is not supported.")
			};
		}

		//---- Public
		//-----------
		public Task StartAsync(CancellationToken token = default)
		{
			return ConnectAsync(token);
		}

		public Task StopAsync(CancellationToken token = default)
		{
			return DisconnectAsync(token);
		}

		public Task ConnectAsync(CancellationToken token = default)
		{
			return ConnectInternalAsync(reconnect: false, token);
		}

		public async Task ReconnectAsync(CancellationToken token = default)
		{
			if(!_config.Networking.ReconnectEnabled)
			{
				throw new InvalidOperationException("Reconnect is disabled by client config.");
			}

			if(PlayerId is null || !HasReconnectToken)
			{
				throw new InvalidOperationException("Cannot reconnect before the server assigns a player id and reconnect token.");
			}

			await DisconnectAsync(token, notify: State == GameClientState.Connected);
			await ConnectInternalAsync(reconnect: true, token);
		}

		public async Task DisconnectAsync(CancellationToken token = default)
		{
			await DisconnectAsync(token, notify: true);
		}

		public async Task SendPacketAsync(PacketType packetType, byte[] payload, CancellationToken token = default)
		{
			await SendPacketAsync(packetType, payload, PacketDeliveryPolicy.GetDefault(packetType), token);
		}

		public async Task SendPacketAsync(PacketType packetType, byte[] payload, DeliveryOptions delivery, CancellationToken token = default)
		{
			if(payload is null)
			{
				throw new ArgumentNullException(nameof(payload));
			}

			await SendRawAsync(SignalShared.BuildPacket(packetType, payload), delivery, token);
		}

		public async Task SendHeaderOnlyPacketAsync(PacketType packetType, CancellationToken token = default)
		{
			await SendHeaderOnlyPacketAsync(packetType, PacketDeliveryPolicy.GetDefault(packetType), token);
		}

		public async Task SendHeaderOnlyPacketAsync(PacketType packetType, DeliveryOptions delivery, CancellationToken token = default)
		{
			await SendRawAsync(SignalShared.BuildHeaderOnlyPacket(packetType), delivery, token);
		}

		public void Dispose()
		{
			if(_disposed)
			{
				return;
			}

			_disposed = true;
			_connectionCts?.Cancel();
			_ = _transport.DisposeAsync();
			_connectionCts?.Dispose();
			_lifecycleLock.Dispose();
			_sendLock.Dispose();
		}

		public Task CreateAsync(CancellationToken token = default)
		{
			_serviceState = ServiceState.Initializing;
			return Task.CompletedTask;
		}

		public Task InitializeAsync(CancellationToken token = default)
		{
			_serviceState = ServiceState.Ready;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken token = default)
		{
			_serviceState = ServiceState.NotInitialized;
			return DisconnectAsync(token);
		}

		public void RegisterServices()
		{
		}

		public void UnregisterServices()
		{
		}

		//---- Private
		//------------
		private async Task ConnectInternalAsync(bool reconnect, CancellationToken token)
		{
			HawkNet.Core.Guard.NotDisposed(_disposed, this);

			TaskCompletionSource<bool> sessionReadyTcs;
			await _lifecycleLock.WaitAsync(token);
			try
			{
				if(State is GameClientState.Connecting or GameClientState.Reconnecting or GameClientState.Connected)
				{
					throw new InvalidOperationException($"Cannot connect while client state is {State}.");
				}

				State = reconnect ? GameClientState.Reconnecting : GameClientState.Connecting;
				Interlocked.Exchange(ref _disconnectNotified, 0);

				_connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
				sessionReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				_sessionReadyTcs = sessionReadyTcs;

				await _transport.ConnectAsync(token);
				_receiveTask = Task.Run(() => ReceiveLoopAsync(_connectionCts.Token), CancellationToken.None);

				if(reconnect)
				{
					await SendReconnectRequestAsync(token);
				}
				else
				{
					await SendHeaderOnlyPacketAsync(PacketType.HandshakeRequest, token);
				}
			}
			catch
			{
				State = GameClientState.Faulted;
				await CloseTransportAsync();
				throw;
			}
			finally
			{
				_lifecycleLock.Release();
			}

			await TaskCompatibility.WaitAsync(sessionReadyTcs.Task, token);
		}

		private async Task DisconnectAsync(
			CancellationToken token,
			bool notify,
			bool awaitReceiveTask = true,
			bool awaitHeartbeatTask = true)
		{
			if(_disposed)
			{
				return;
			}

			Task? receiveTask;
			Task? heartbeatTask;

			await _lifecycleLock.WaitAsync(token);
			try
			{
				if(State == GameClientState.Disconnected && !_transport.IsConnected)
				{
					return;
				}

				State = GameClientState.Disconnected;
				_connectionCts?.Cancel();
				await CloseTransportAsync();
				receiveTask = awaitReceiveTask ? _receiveTask : null;
				heartbeatTask = awaitHeartbeatTask ? _heartbeatTask : null;
				_receiveTask = null;
				_heartbeatTask = null;
				_sessionReadyTcs?.TrySetCanceled(token);
				_sessionReadyTcs = null;
			}
			finally
			{
				_lifecycleLock.Release();
			}

			await AwaitBackgroundTaskAsync(receiveTask);
			await AwaitBackgroundTaskAsync(heartbeatTask);

			if(notify && Interlocked.Exchange(ref _disconnectNotified, 1) == 0)
			{
				Disconnected?.Invoke();
			}
		}

		private async Task ReceiveLoopAsync(CancellationToken token)
		{
			try
			{
				while(!token.IsCancellationRequested && _transport.IsConnected)
				{
					ReadNetworkPacketResult result = await _transport.ReadPacketAsync(token);
					if(!result.Success)
					{
						break;
					}

					if(result.PacketType == PacketType.HandshakeResponse)
					{
						await HandleConnectionConfirmationAsync(result.Payload, token);
						continue;
					}

					PacketReceived?.Invoke(result.PacketType, result.Payload);
				}
			}
			catch(OperationCanceledException)
			{
				// Expected during disconnect.
			}
			catch(IOException)
			{
				// Stream closed.
			}
			catch(ObjectDisposedException)
			{
				// Stream closed.
			}
			catch(Exception)
			{
				State = GameClientState.Faulted;
			}
			finally
			{
				if(!token.IsCancellationRequested)
				{
					await DisconnectAsync(CancellationToken.None, notify: true, awaitReceiveTask: false);
				}
			}
		}

		private async Task HandleConnectionConfirmationAsync(byte[] payload, CancellationToken token)
		{
			HandshakeResponse response;
			try
			{
				response = HandshakeProtocol.DecodeResponse(payload, _payloadCodec);
			}
			catch(Exception ex)
			{
				State = GameClientState.Faulted;
				_sessionReadyTcs?.TrySetException(new InvalidOperationException("Server sent an invalid handshake response payload.", ex));
				return;
			}

			if(response.ReconnectToken.Length != ConnectionManager.ReconnectTokenSize)
			{
				State = GameClientState.Faulted;
				_sessionReadyTcs?.TrySetException(new InvalidOperationException("Server sent an invalid reconnect token in the handshake response."));
				return;
			}

			bool wasReconnecting = State == GameClientState.Reconnecting;
			PlayerId = response.PlayerId;
			_reconnectToken = response.ReconnectToken.ToArray();
			CurrentRuntimeManifest = response.Runtime;
			CurrentGameStateId = response.Runtime.InitialGameStateId;
			CurrentGameStatePayload = response.Runtime.InitialGameStatePayload.ToArray();
			try
			{
				await NotifyHandshakeResponseReceivedAsync(response, token);
			}
			catch(Exception ex)
			{
				State = GameClientState.Faulted;
				_sessionReadyTcs?.TrySetException(ex);
				await CloseTransportAsync();
				return;
			}

			State = GameClientState.Connected;
			StartHeartbeatLoop();

			if(wasReconnecting)
			{
				Reconnected?.Invoke();
			}
			else
			{
				Connected?.Invoke();
			}

			_sessionReadyTcs?.TrySetResult(true);
		}

		private async Task NotifyHandshakeResponseReceivedAsync(HandshakeResponse response, CancellationToken token)
		{
			var handlers = HandshakeResponseReceived;
			if(handlers is null)
			{
				return;
			}

			foreach(Func<HandshakeResponse, CancellationToken, Task> handler in handlers.GetInvocationList())
			{
				await handler(response, token);
			}
		}

		private void StartHeartbeatLoop()
		{
			var connectionCts = _connectionCts;
			if(connectionCts is null || _heartbeatTask is { IsCompleted: false })
			{
				return;
			}

			_heartbeatTask = Task.Run(() => HeartbeatLoopAsync(connectionCts.Token), CancellationToken.None);
		}

		private async Task HeartbeatLoopAsync(CancellationToken token)
		{
			try
			{
				while(!token.IsCancellationRequested)
				{
					await Task.Delay(GetHeartbeatInterval(), token);
					if(IsConnected)
					{
						await SendHeaderOnlyPacketAsync(PacketType.HeartBeat, token);
					}
				}
			}
			catch(OperationCanceledException)
			{
				// Expected during disconnect.
			}
			catch(IOException)
			{
				await DisconnectAsync(CancellationToken.None, notify: true, awaitHeartbeatTask: false);
			}
			catch(ObjectDisposedException)
			{
				await DisconnectAsync(CancellationToken.None, notify: true, awaitHeartbeatTask: false);
			}
		}

		private async Task SendReconnectRequestAsync(CancellationToken token)
		{
			if(PlayerId is null || !HasReconnectToken)
			{
				throw new InvalidOperationException("Reconnect token is not available.");
			}

			byte[] payload = new byte[ConnectionManager.ReconnectPayloadSize];
			Array.Copy(BitConverter.GetBytes(PlayerId.Value), 0, payload, 0, sizeof(int));
			Array.Copy(_reconnectToken, 0, payload, sizeof(int), _reconnectToken.Length);
			await SendPacketAsync(PacketType.ReconnectRequest, payload, token);
		}

		private async Task SendRawAsync(byte[] packet, DeliveryOptions delivery, CancellationToken token)
		{
			if(!_transport.IsConnected)
			{
				throw new InvalidOperationException("Not connected to server.");
			}

			await _sendLock.WaitAsync(token);
			try
			{
				await _transport.SendAsync(packet, delivery, token);
			}
			finally
			{
				_sendLock.Release();
			}
		}

		private async Task CloseTransportAsync()
		{
			try
			{
				await _transport.DisconnectAsync();
			}
			finally
			{
				_connectionCts?.Dispose();
				_connectionCts = null;
			}
		}

		private TimeSpan GetHeartbeatInterval()
		{
			return TimeSpan.FromSeconds(Math.Max(1, _config.Networking.HeartbeatIntervalSeconds));
		}

		private static async Task AwaitBackgroundTaskAsync(Task? task)
		{
			if(task is null)
			{
				return;
			}

			try
			{
				await task;
			}
			catch(OperationCanceledException)
			{
				// Expected during disconnect.
			}
			catch(IOException)
			{
				// Expected when a socket is closed.
			}
			catch(ObjectDisposedException)
			{
				// Expected when a socket is closed.
			}
		}
	}
}
