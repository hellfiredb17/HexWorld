/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	/// <summary>
	/// Represents a session for a player, including identification, last activity time, and connection information.
	/// </summary>
	/// <remarks>A PlayerSession tracks the state of an individual player within the system. The session includes
	/// the player's unique identifier, the timestamp of their most recent activity, and the current client connection if
	/// the player is connected. If the player is disconnected, the Connection field will be null.</remarks>
	public class PlayerSession
	{
		public int PlayerId { get; internal init; }
		public DateTime LastActive { get; internal set; }
		public byte[] ReconnectToken { get; internal init; } = Array.Empty<byte>();
		public ClientConnection? Connection { get; internal set; } // current connection, null if disconnected
	}

	public readonly record struct PlayerSessionSnapshot(
		int PlayerId,
		DateTime LastActive,
		bool IsConnected);

	/// <summary>
	/// Manages player connections, sessions, and connection heartbeat timeouts.
	/// </summary>
	public class ConnectionManager
	{
		//---- Constants
		//--------------
		public const int ReconnectTokenSize = 32;
		public const int ReconnectPayloadSize = sizeof(int) + ReconnectTokenSize;

		//---- Events
		//-----------
		public event Action<int, PacketType, byte[]>? PacketReceived;
		public event Action<int>? PlayerConnected;
		public event Action<int>? PlayerReconnected;
		public event Action<int, ConnectionCloseReason>? ConnectionLost; // lost connection but yet to time out
		public event Action<int, ConnectionCloseReason>? Disconnection; // lost connection and timed out - removed

		//---- Variables
		//--------------
		private readonly ConcurrentDictionary<int, PlayerSession> _sessions = new();
		private readonly object _sessionsLock = new();
		private readonly IpNetwork[] _allowedIPs;
		private readonly GameHostConfig _config;
		private readonly TransportProtocol _protocol;
		private readonly IServerTransport _transport;
		private readonly CancellationTokenSource _cancellationTokenSource = new();
		private readonly ILogger _logger;
		private readonly PacketCounters _packetCounters = new();
		private readonly ServerDiagnostics? _diagnostics;
		private Task? _acceptLoopTask;
		private Task? _sessionTimeoutTask;
		private int _nextPlayerId;
		private int _activeConnections;
		private int _nextConnectionTaskId;
		private readonly ConcurrentDictionary<int, Task> _connectionTasks = new();
		private bool _isStarted;

		//---- Properties
		//---------------
		public int SessionCount => _sessions.Count;
		public int ConnectedPlayerCount
		{
			get
			{
				lock(_sessionsLock)
				{
					return _sessions.Values.Count(session => session.Connection is not null);
				}
			}
		}
		public PacketCounters PacketCounters => _packetCounters;
		public IPEndPoint? ListenEndPoint => _transport.LocalEndPoint;
		public UdpDeliveryMetricsSnapshot UdpDeliveryMetrics =>
			(_transport as IUdpDeliveryMetricsSource)?.GetUdpDeliveryMetrics() ?? UdpDeliveryMetricsSnapshot.Empty;
		internal int MaxInvalidPacketsPerConnection => Math.Max(1, _config.Security.MaxInvalidPacketsPerConnection);
		internal int MaxPacketsPerWindow => Math.Max(1, _config.Security.MaxPacketsPerWindow);
		internal TimeSpan PacketRateWindow => TimeSpan.FromSeconds(Math.Max(1, _config.Security.PacketRateWindowSeconds));
		internal TimeSpan HeartbeatTimeout => GetHeartbeatTimeout();
		internal TimeSpan HandshakeTimeout => TimeSpan.FromSeconds(_config.Security.HandshakeTimeoutSeconds);
		internal int OutboundQueueCapacity => _config.Networking.OutboundQueueCapacity;
		internal int MaxPayloadBytes(PacketType packetType) => packetType switch
		{
			PacketType.ChatMessage => _config.Chat.MaxMessageBytes,
			PacketType.ChatLobbyReadyRequest => _config.ChatLobby.MaxReadyPayloadBytes,
			PacketType.MatchCommandRequest => _config.Security.MaxCommandPayloadBytes,
			PacketType.GameStateMessage => _config.Security.MaxGameStateMessageBytes,
			_ => SignalShared.MaxPayloadLength
		};
		internal ServerDiagnostics? Diagnostics => _diagnostics;
		internal CancellationToken ShutdownToken => _cancellationTokenSource.Token;

		//---- Ctor
		//---------
		/// <summary>
		/// Initializes a new instance of the ConnectionManager class from server configuration.
		/// </summary>
		public ConnectionManager(GameHostConfig config, ILogger? logger = null, ServerDiagnostics? diagnostics = null, IServerTransportFactory? transportFactory = null)
		{
			_config = config;
			_logger = logger ?? NullLogger.Instance;
			_diagnostics = diagnostics;
			_allowedIPs = (config.Server.AllowedIPs ?? Array.Empty<string>())
				.Where(ip => !string.IsNullOrWhiteSpace(ip))
				.Select(ip => IpNetwork.TryParse(ip, out IpNetwork network) ? network : default)
				.ToArray();
			_protocol = config.Networking.Protocol;
			if(_config.Server.UseTls)
			{
				_logger.Warning(
					"TLS is configured but is not supported yet. HawkNet server v1 only supports plain TCP.",
					nameof(ConnectionManager));
				throw new NotSupportedException("TLS is configured, but HawkNet server v1 does not support TLS yet.");
			}

			transportFactory ??= new BuiltInServerTransportFactory();
			if (!transportFactory.Supports(_protocol))
			{
				_logger.Warning($"{_protocol} transport is not supported by the built-in server.", nameof(ConnectionManager));
				throw new NotSupportedException($"Transport '{_protocol}' is not implemented.");
			}

			_transport = transportFactory.Create(config);
			_nextPlayerId = config.Server.NextPlayerIdStart - 1;
		}

		/// <summary>
		/// Initializes a new instance of the ConnectionManager class that listens for TCP connections on the specified IP
		/// address and port.
		/// </summary>
		public ConnectionManager(IPAddress ipAddress, int port)
			: this(new GameHostConfig
			{
				Server = new ServerSettings
				{
					Host = ipAddress.ToString(),
					Port = port
				},
				Networking = new NetworkingSettings
				{
					Protocol = TransportProtocol.Tcp
				}
			})
		{
		}

		//---- Public
		//-----------
		/// <summary>
		/// Starts the server and begins listening for incoming TCP client connections asynchronously.
		/// </summary>
		/// <remarks>This method initiates the server's listening loop and processes client connections in the
		/// background. The method does not block the calling thread. To stop the server, cancel the associated cancellation
		/// token. Exceptions that occur during the listener loop are logged to the console.</remarks>
		/// <returns>A task that represents the asynchronous operation of starting the server and handling incoming connections.</returns>
		public async Task StartAsync()
		{
			if (_isStarted)
			{
				return;
			}

			await _transport.StartAsync(_cancellationTokenSource.Token);
			_isStarted = true;
			_logger.Log($"Listening on {_protocol} {_transport.IPAddress}:{_transport.Port}.", nameof(ConnectionManager));
			_acceptLoopTask = Task.Run(AcceptLoopAsync);
			_sessionTimeoutTask = Task.Run(SessionTimeoutCheckLoopAsync); // start session cleanup loop

			return;
		}

		/// <summary>
		/// Stops the server asynchronously and disconnects all active sessions.
		/// </summary>
		/// <remarks>This method cancels any ongoing operations, stops the server listener, and disconnects all active
		/// client sessions. It is safe to call multiple times; subsequent calls have no effect if the server is
		/// already stopped.</remarks>
		/// <returns>A task that represents the asynchronous stop operation.</returns>
		public async Task StopAsync()
		{
			if (!_isStarted)
			{
				return;
			}

			_cancellationTokenSource.Cancel();
			await _transport.StopAsync();

			foreach(var (_, connection) in GetConnectedConnections())
			{
				await connection.DisconnectAsync(ConnectionCloseReason.ServerShutdown);
			}

			await AwaitBackgroundTaskAsync(_acceptLoopTask);
			await AwaitBackgroundTaskAsync(_sessionTimeoutTask);
			await Task.WhenAll(_connectionTasks.Values.ToArray());

			_isStarted = false;
			_logger.Log("Stopped.", nameof(ConnectionManager));
		}

		public bool TryBuildReconnectPayload(int playerId, [NotNullWhen(true)] out byte[]? payload)
		{
			lock(_sessionsLock)
			{
				if(_sessions.TryGetValue(playerId, out var session))
				{
					payload = BuildReconnectPayload(session);
					return true;
				}
			}

			payload = null;
			return false;
		}

		public bool TryBuildReconnectToken(int playerId, [NotNullWhen(true)] out byte[]? reconnectToken)
		{
			lock(_sessionsLock)
			{
				if(_sessions.TryGetValue(playerId, out var session))
				{
					reconnectToken = session.ReconnectToken.ToArray();
					return true;
				}
			}

			reconnectToken = null;
			return false;
		}

		public bool TryGetSessionSnapshot(int playerId, out PlayerSessionSnapshot snapshot)
		{
			lock(_sessionsLock)
			{
				if(_sessions.TryGetValue(playerId, out var session))
				{
					snapshot = new PlayerSessionSnapshot(
						session.PlayerId,
						session.LastActive,
						session.Connection is not null);
					return true;
				}
			}

			snapshot = default;
			return false;
		}

		public bool IsPlayerConnected(int playerId)
		{
			lock(_sessionsLock)
			{
				return _sessions.TryGetValue(playerId, out var session) && session.Connection is not null;
			}
		}

		public IReadOnlyList<PlayerSessionSnapshot> GetSessionSnapshots()
		{
			lock (_sessionsLock)
			{
				return _sessions.Values.Select(session => new PlayerSessionSnapshot(
					session.PlayerId, session.LastActive, session.Connection is not null))
					.OrderBy(session => session.PlayerId).ToArray();
			}
		}

		public Task DisconnectPlayerAsync(int playerId, ConnectionCloseReason reason)
		{
			return TryGetConnectedConnection(playerId, out ClientConnection? connection)
				? connection.DisconnectAsync(reason)
				: Task.CompletedTask;
		}

		//---- Private
		//------------
		private async Task AcceptLoopAsync()
		{
			var token = _cancellationTokenSource.Token;

			try
			{
				while(!token.IsCancellationRequested)
				{
					var connection = await _transport.AcceptConnectionAsync(token);
					if(!CanAcceptConnection(connection) || !TryReserveConnectionSlot())
					{
						await connection.DisconnectAsync();
						continue;
					}

					int taskId = Interlocked.Increment(ref _nextConnectionTaskId);
					Task task = HandleNewConnection(connection);
					_connectionTasks[taskId] = task;
					_ = task.ContinueWith(
						completedTask => _connectionTasks.TryRemove(taskId, out Task? removedTask),
						CancellationToken.None,
						TaskContinuationOptions.ExecuteSynchronously,
						TaskScheduler.Default);
				}
			}
			catch(OperationCanceledException)
			{
				_logger.DebugLog("Listener loop cancelled.", nameof(ConnectionManager));
			}
			catch(ObjectDisposedException) when (token.IsCancellationRequested)
			{
				_logger.DebugLog("Listener loop stopped.", nameof(ConnectionManager));
			}
			catch(SocketException) when (token.IsCancellationRequested)
			{
				_logger.DebugLog("Listener loop stopped.", nameof(ConnectionManager));
			}
			catch(Exception ex)
			{
				_logger.Exception(ex, "Error in listener loop.", nameof(ConnectionManager));
			}
		}

		/// <summary>
		/// Handles a new incoming TCP client connection asynchronously.
		/// </summary>
		/// <param name="tcpClient">The TCP client representing the new connection to handle. Cannot be null.</param>
		/// <returns>A task that represents the asynchronous operation of handling the client connection.</returns>
		private async Task HandleNewConnection(ITransportConnection transportConnection)
		{
			try
			{
				var connection = new ClientConnection(transportConnection, this, _logger);
				connection.PlayerConnected += OnPlayerConnected;
				connection.PlayerReconnected += OnPlayerReconnected;
				connection.PacketReceived += OnPacketReceived;
				connection.ConnectionLost += OnConnectionLost;

				await connection.HandleClientAsync();
			}
			finally
			{
				Interlocked.Decrement(ref _activeConnections);
			}
		}

		/// <summary>
		/// Performs an asynchronous check to determine whether the current session has timed out.
		/// </summary>
		/// <returns>A task that represents the asynchronous operation.</returns>
		private async Task SessionTimeoutCheckLoopAsync()
		{
			var token = _cancellationTokenSource.Token;

			try
			{
				while(!token.IsCancellationRequested)
				{
					var now = DateTime.UtcNow;
					foreach(var session in _sessions.Values.ToArray())
					{
						if(TryRemoveTimedOutSession(session.PlayerId, now))
						{
							_logger.Log($"Session for Player {session.PlayerId} timed out.", nameof(ConnectionManager));
							OnDisconnection(session.PlayerId, ConnectionCloseReason.SessionTimeout);
						}
					}
					await Task.Delay(GetSessionCheckInterval(), token);
				}
			}
			catch(OperationCanceledException)
			{
				_logger.DebugLog("Session timeout loop cancelled.", nameof(ConnectionManager));
			}
		}

		private static async Task AwaitBackgroundTaskAsync(Task? task)
		{
			if (task is null)
			{
				return;
			}

			try
			{
				await task;
			}
			catch(OperationCanceledException)
			{
				// Expected during shutdown.
			}
		}

		private bool CanAcceptConnection(ITransportConnection connection)
		{
			if(_allowedIPs.Length == 0)
			{
				return true;
			}

			if(connection.RemoteEndPoint is not IPEndPoint remoteEndPoint)
			{
				return false;
			}

			if(_allowedIPs.Any(network => network.Contains(remoteEndPoint.Address)))
			{
				return true;
			}

			_logger.Warning($"Connection rejected: remote IP {remoteEndPoint.Address} is not allowed.", nameof(ConnectionManager));
			return false;
		}

		private bool TryReserveConnectionSlot()
		{
			while (true)
			{
				int current = Volatile.Read(ref _activeConnections);
				if (current >= _config.Server.MaxConcurrentConnections)
				{
					_logger.Warning("Connection rejected: max concurrent connections reached.", nameof(ConnectionManager));
					return false;
				}
				if (Interlocked.CompareExchange(ref _activeConnections, current + 1, current) == current) return true;
			}
		}

		private TimeSpan GetSessionCheckInterval()
		{
			int intervalSeconds = Math.Max(1, _config.Server.HeartbeatIntervalSeconds);
			return TimeSpan.FromSeconds(intervalSeconds);
		}

		private TimeSpan GetHeartbeatTimeout()
		{
			int timeoutSeconds = _config.Server.HeartbeatTimeoutSeconds > 0
				? _config.Server.HeartbeatTimeoutSeconds
				: Math.Max(1, _config.Server.HeartbeatIntervalSeconds * 2);
			return TimeSpan.FromSeconds(timeoutSeconds);
		}

		internal bool TryReconnectSession(int playerId, byte[] reconnectToken, ClientConnection connection, [NotNullWhen(true)] out PlayerSession? session)
		{
			ClientConnection? previousConnection = null;
			lock(_sessionsLock)
			{
				if(!_sessions.TryGetValue(playerId, out var existingSession))
				{
					session = null;
					return false;
				}

				if(!CryptographicOperations.FixedTimeEquals(existingSession.ReconnectToken, reconnectToken))
				{
					session = null;
					_logger.Warning($"Reconnect rejected for Player {playerId}: invalid reconnect token.", nameof(ConnectionManager));
					return false;
				}

				previousConnection = existingSession.Connection;
				session = existingSession;
				session.Connection = connection;
				session.LastActive = DateTime.UtcNow;
			}

			if(previousConnection is not null && !ReferenceEquals(previousConnection, connection))
			{
				_ = previousConnection.DisconnectAsync(ConnectionCloseReason.ClientDisconnected);
			}

			return true;
		}

		internal bool TryCreateSession(ClientConnection connection, [NotNullWhen(true)] out PlayerSession? session)
		{
			lock(_sessionsLock)
			{
				if(_sessions.Count >= _config.Server.MaxPlayers)
				{
					session = null;
					_logger.Warning("New player rejected: max players reached.", nameof(ConnectionManager));
					return false;
				}

				int newId = Interlocked.Increment(ref _nextPlayerId);
				session = new PlayerSession
				{
					PlayerId = newId,
					Connection = connection,
					LastActive = DateTime.UtcNow,
					ReconnectToken = HawkNet.Core.RandomCompatibility.GetBytes(ReconnectTokenSize),
				};
				_sessions[newId] = session;
				return true;
			}
		}

		internal void RegisterSession(PlayerSession session)
		{
			lock(_sessionsLock)
			{
				_sessions[session.PlayerId] = session;
			}
		}

		internal bool IsCurrentConnection(PlayerSession session, ClientConnection connection)
		{
			lock(_sessionsLock)
			{
				return _sessions.TryGetValue(session.PlayerId, out var currentSession)
					&& ReferenceEquals(currentSession, session)
					&& ReferenceEquals(currentSession.Connection, connection);
			}
		}

		internal bool TryGetConnectedConnection(int playerId, [NotNullWhen(true)] out ClientConnection? connection)
		{
			lock(_sessionsLock)
			{
				if(_sessions.TryGetValue(playerId, out var session) && session.Connection is not null)
				{
					connection = session.Connection;
					return true;
				}
			}

			connection = null;
			return false;
		}

		internal IReadOnlyCollection<(int PlayerId, ClientConnection Connection)> GetConnectedConnections()
		{
			lock(_sessionsLock)
			{
				return _sessions.Values
					.Where(session => session.Connection is not null)
					.Select(session => (session.PlayerId, session.Connection!))
					.ToArray();
			}
		}

		internal IReadOnlyCollection<int> GetConnectedPlayerIds()
		{
			lock(_sessionsLock)
			{
				return _sessions.Values
					.Where(session => session.Connection is not null)
					.Select(session => session.PlayerId)
					.ToArray();
			}
		}

		internal bool TryMarkSessionActivity(PlayerSession session, ClientConnection connection)
		{
			lock(_sessionsLock)
			{
				if(_sessions.TryGetValue(session.PlayerId, out var currentSession)
					&& ReferenceEquals(currentSession, session)
					&& ReferenceEquals(currentSession.Connection, connection))
				{
					currentSession.LastActive = DateTime.UtcNow;
					return true;
				}
			}

			return false;
		}

		internal bool TryMarkConnectionLost(PlayerSession session, ClientConnection connection, ConnectionCloseReason reason, out int playerId)
		{
			lock(_sessionsLock)
			{
				if(_sessions.TryGetValue(session.PlayerId, out var currentSession)
					&& ReferenceEquals(currentSession, session)
					&& ReferenceEquals(currentSession.Connection, connection))
				{
					currentSession.Connection = null;
					currentSession.LastActive = DateTime.UtcNow;
					playerId = currentSession.PlayerId;
					return true;
				}
			}

			playerId = default;
			return false;
		}

		private bool TryRemoveTimedOutSession(int playerId, DateTime now)
		{
			lock(_sessionsLock)
			{
				if(!_sessions.TryGetValue(playerId, out var session))
				{
					return false;
				}

				if(session.Connection is not null
					|| (now - session.LastActive).TotalSeconds <= _config.Server.SessionTimeoutSeconds)
				{
					return false;
				}

				return _sessions.TryRemove(playerId, out _);
			}
		}

		private static byte[] BuildReconnectPayload(PlayerSession session)
		{
			byte[] payload = new byte[ReconnectPayloadSize];
			Array.Copy(BitConverter.GetBytes(session.PlayerId), 0, payload, 0, sizeof(int));
			Array.Copy(session.ReconnectToken, 0, payload, sizeof(int), ReconnectTokenSize);
			return payload;
		}

		//---- Connection Callbacks
		//-------------------------
		private void OnPlayerConnected(int id)
		{
			PlayerConnected?.Invoke(id);
		}

		private void OnPlayerReconnected(int id)
		{
			PlayerReconnected?.Invoke(id);
		}

		private void OnPacketReceived(int playerId, PacketType packetType, byte[] payload)
		{
			// send event
			PacketReceived?.Invoke(playerId, packetType, payload);
		}

		private void OnConnectionLost(int id, ConnectionCloseReason reason)
		{
			_diagnostics?.RecordDisconnect(reason);

			// send event
			ConnectionLost?.Invoke(id, reason);
		}

		private void OnDisconnection(int id, ConnectionCloseReason reason)
		{
			_diagnostics?.RecordDisconnect(reason);

			// send event
			Disconnection?.Invoke(id, reason);

			_logger.Log($"Player {id} disconnected and removed. Reason: {reason}.", nameof(ConnectionManager));
		}

	} // end class 
} // end namespace
