/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 * Created: 10/31/2025 12:57:25 AM
 * Description: [Short description]
 */

using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	public enum GameHostLifecycleState { Created, Starting, Running, Stopping, Stopped, Faulted }

	public class GameHost : IGameHost
	{
		//---- Variables
		//--------------
		private readonly GameHostConfig _config;
		private readonly ILogger _logger;
		private readonly ConfigService _configService;
		private readonly EventDispatcher _eventDispatcher;
		private readonly InboundMessageRouter _inboundMessageRouter;
		private readonly List<IDisposable> _eventSubscriptions = new();
		private readonly UpdateLoop _updateLoop;
		private readonly IPayloadCodec _payloadCodec;
		private readonly GameStateManager _gameStateManager;
		private readonly ServiceRegistry _services = new();
		private readonly HawkNetServerContext _context;
		private readonly IReadOnlyList<ServerGameStateRegistration> _gameStateRegistrations;
		private readonly string[] _clientServiceModules;
		private readonly string[] _clientGameStateModules;
		private readonly RuntimeModuleRequirement[] _clientServiceModuleRequirements;
		private readonly RuntimeModuleRequirement[] _clientGameStateModuleRequirements;
		private readonly ServerExecutionScheduler _executionScheduler;
		private readonly object _lifecycleLock = new();
		private GameHostLifecycleState _lifecycleState = GameHostLifecycleState.Created;
		private ConnectionManager? _connectionManager;
		private MessageDispatcher? _messageDispatcher;

		//---- Properties
		//---------------
		/// <summary>
		/// The loaded configuration instance.
		/// </summary>
		public GameHostConfig Config => _config;
		public UpdateLoop UpdateLoop => _updateLoop;
		public ServerDiagnostics? Diagnostics => _services.TryGet<ServerDiagnostics>(out var value) ? value : null;
		public IGameState? ActiveGameState => _gameStateManager.ActiveGameState;
		public string? ActiveGameStateId => _gameStateManager.ActiveStateId;
		protected IEventDispatcher EventDispatcher => _eventDispatcher;
		protected IPlayerIdentityService PlayerIdentityService => _services.Get<IPlayerIdentityService>();
		protected ILogger Logger => _logger;
		public IServiceRegistry Services => _services;
		public IEventDispatcher Events => _eventDispatcher;
		public IServerExecutionScheduler ExecutionScheduler => _executionScheduler;
		public GameHostLifecycleState LifecycleState { get { lock (_lifecycleLock) return _lifecycleState; } }
		public ServerMonitorSnapshot GetMonitorSnapshot() => new()
		{
			LifecycleState = LifecycleState,
			ListenEndPoint = _connectionManager?.ListenEndPoint,
			ActiveStateId = ActiveGameStateId ?? string.Empty,
			Sessions = _connectionManager?.GetSessionSnapshots() ?? Array.Empty<PlayerSessionSnapshot>(),
			Inbox = _executionScheduler.Metrics,
			Diagnostics = Diagnostics?.GetSnapshot(),
			UdpDelivery = _connectionManager?.UdpDeliveryMetrics ?? UdpDeliveryMetricsSnapshot.Empty,
			CapturedAtUtc = DateTime.UtcNow
		};

		//---- Constructor
		//----------------
		/// <summary>
		/// Loads server configuration from the specified JSON file path.
		/// </summary>
		/// <param name="configPath">Path to JSON config. Defaults to "Config/game-server.json".</param>
		public GameHost(string configPath = "Config/game-server.json", Action<IHawkNetServerBuilder>? configure = null)
			: this(ServerConfiguration.Load(configPath), configure)
		{
		}

		public GameHost(GameHostConfig config, Action<IHawkNetServerBuilder>? configure = null)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
			ServerConfiguration.Validate(_config).ThrowIfInvalid();
			_configService = new ConfigService(_config);
			_logger = CreateLogger(_config);

			// create event dispatcher and subscribe
			_eventDispatcher = new EventDispatcher(_logger);
			_updateLoop = new UpdateLoop();
			_executionScheduler = new ServerExecutionScheduler(_config.Simulation.InboundQueueCapacity);
			_payloadCodec = new JsonPayloadCodec();
			_gameStateManager = new GameStateManager(_config.GameMode, _logger);
			_context = new HawkNetServerContext(
				_eventDispatcher,
				_logger,
				_services,
				GetConnectedPlayerIds);
			_services.Register<IServerExecutionScheduler>(_executionScheduler);
			_services.Register(_executionScheduler);
			var builder = new HawkNetServerBuilder(_config, _eventDispatcher, _services, _configService);
			HawkNetServer.AddDefaults(builder);
			builder.RegisterServiceModule(
				"core",
				() => new ServerCoreModule(
					_configService,
					_eventDispatcher,
					_updateLoop,
					_payloadCodec,
					_gameStateManager,
					builder.MatchVariants,
					_logger),
				[
					typeof(IConfigService),
					typeof(ConfigService),
					typeof(IEventDispatcher),
					typeof(EventDispatcher),
					typeof(IPayloadCodec),
					typeof(ILogger),
					typeof(UpdateLoop),
					typeof(GameStateManager)
				]);
			configure?.Invoke(builder);
			builder.EnableConfiguredModules();
			_clientServiceModuleRequirements = builder.EnabledServiceModuleRequirements
				.Where(module => !string.Equals(module.Id, "core", StringComparison.OrdinalIgnoreCase))
				.ToArray();
			_clientGameStateModuleRequirements = builder.EnabledGameStateModuleRequirements.ToArray();
			_clientServiceModules = _clientServiceModuleRequirements
				.Select(module => module.Id)
				.Where(moduleId => !string.Equals(moduleId, "core", StringComparison.OrdinalIgnoreCase))
				.ToArray();
			_clientGameStateModules = _clientGameStateModuleRequirements
				.Select(module => module.Id)
				.ToArray();
			_inboundMessageRouter = new InboundMessageRouter(
				_eventDispatcher,
				_logger,
				_services.TryGet<ServerDiagnostics>(out var startupDiagnostics) ? startupDiagnostics : null,
				_payloadCodec);
			_gameStateRegistrations = builder.GameStates.ToArray();
			_eventSubscriptions.Add(_eventDispatcher.Subscribe<SendPlayerMessageEvent>(OnSendPlayerMessageEvent));
			_eventSubscriptions.Add(_eventDispatcher.Subscribe<BroadcastMessageEvent>(OnBroadcast));
			_eventSubscriptions.Add(_eventDispatcher.Subscribe<GameStateTransitionRequestedEvent>(OnGameStateTransitionRequested));
		}

		//---- IGameHost Interface
		//------------------------
		public async Task BeginAsync()
		{
			lock (_lifecycleLock)
			{
				if (_lifecycleState != GameHostLifecycleState.Created)
					throw new InvalidOperationException($"A GameHost is one-shot and cannot start from state {_lifecycleState}.");
				_lifecycleState = GameHostLifecycleState.Starting;
			}
			try
			{
			// use configured port/host
			_logger.Log($"Starting on {_config.Server.Host}:{_config.Server.Port} using {_config.Networking.Protocol}.", nameof(GameHost));
			_logger.Log($"MaxPlayers: {_config.Server.MaxPlayers}, SessionTimeout: {_config.Server.SessionTimeoutSeconds}s.", nameof(GameHost));

			// Create server and hook into callbacks
			_services.TryGet<ServerDiagnostics>(out var diagnostics);
			_services.TryGet<IServerTransportFactory>(out var transportFactory);
			_connectionManager = new ConnectionManager(_config, _logger, diagnostics, transportFactory);
			_services.Replace(_connectionManager);
			_messageDispatcher = new MessageDispatcher(_connectionManager, _logger, diagnostics);
			RegisterConfiguredGameStates();

			// subscribe to callbacks
			_connectionManager.PlayerConnected += OnConnection;
			_connectionManager.PlayerReconnected += OnReconnection;
			_connectionManager.PacketReceived += OnPacket;
			_connectionManager.ConnectionLost += OnConnecitonLost;
			_connectionManager.Disconnection += OnDisconnection;

			_gameStateManager.Start();

			// start server
			await _connectionManager.StartAsync();
			if(diagnostics is not null)
			{
				await diagnostics.StartAsync();
			}
			StartSimulationLoop();
			lock (_lifecycleLock) _lifecycleState = GameHostLifecycleState.Running;
			_logger.Log("Started.", nameof(GameHost));
			}
			catch
			{
				lock (_lifecycleLock) _lifecycleState = GameHostLifecycleState.Faulted;
				throw;
			}
		}

		public async Task StopAsync()
		{
			if(_connectionManager is null)
			{
				return;
			}
			lock (_lifecycleLock)
			{
				if (_lifecycleState is GameHostLifecycleState.Stopping or GameHostLifecycleState.Stopped) return;
				_lifecycleState = GameHostLifecycleState.Stopping;
			}

			_connectionManager.PlayerConnected -= OnConnection;
			_connectionManager.PlayerReconnected -= OnReconnection;
			_connectionManager.PacketReceived -= OnPacket;
			_connectionManager.ConnectionLost -= OnConnecitonLost;
			_connectionManager.Disconnection -= OnDisconnection;
			foreach(var subscription in _eventSubscriptions)
			{
				subscription.Dispose();
			}
			_eventSubscriptions.Clear();
			if(_services.TryGet<ChatService>(out var chatService))
			{
				chatService.Dispose();
			}

			if(_services.TryGet<PlayerIdentityService>(out var playerIdentityService))
			{
				playerIdentityService.Dispose();
			}

			await _updateLoop.StopUpdateLoopAsync();
			_updateLoop.Update -= OnUpdate;
			_gameStateManager.Stop();
			await _connectionManager.StopAsync();
			if(_services.TryGet<ServerDiagnostics>(out var diagnostics))
			{
				await diagnostics.StopAsync();
			}
			_logger.Log("Stopped.", nameof(GameHost));
			lock (_lifecycleLock) _lifecycleState = GameHostLifecycleState.Stopped;
		}

		//---- Public
		//-----------
		public Task RequestGameStateTransitionAsync(
			string toStateId,
			byte[]? payload = null,
			CancellationToken token = default)
		{
			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!_executionScheduler.TryEnqueue(async schedulerToken =>
			{
				try
				{
					await _eventDispatcher.DispatchAsync(new GameStateTransitionRequestedEvent(toStateId, payload ?? Array.Empty<byte>()), schedulerToken);
					completion.TrySetResult(true);
				}
				catch (Exception exception) { completion.TrySetException(exception); }
			}))
				throw new InvalidOperationException("The server command queue is full.");
			if (token.CanBeCanceled) token.Register(() => completion.TrySetCanceled(token));
			return completion.Task;
		}

		//---- Protected
		//--------------
		protected bool TryTransitionGameState(string toStateId, byte[]? payload = null)
		{
			return _gameStateManager.TryTransitionTo(toStateId, payload);
		}

		protected IEnumerable<int> GetConnectedPlayerIds()
		{
			return _connectionManager?.GetConnectedPlayerIds() ?? Enumerable.Empty<int>();
		}

		//---- Private
		//------------
		private void RegisterConfiguredGameStates()
		{
			foreach(var registration in _gameStateRegistrations)
			{
				_gameStateManager.RegisterState(
					registration.Id,
					() => registration.Factory(_context));
			}
		}

		private static ILogger CreateLogger(GameHostConfig config)
		{
			HawkNetLogLevel minimumLevel = HawkNetLogLevel.Information;
			if(Enum.TryParse(config.Server.LogLevel, ignoreCase: true, out HawkNetLogLevel configuredLevel))
			{
				minimumLevel = configuredLevel;
			}

			if(config.Server.EnableVerboseLogging && minimumLevel > HawkNetLogLevel.Debug)
			{
				minimumLevel = HawkNetLogLevel.Debug;
			}

			return new NetLogger(minimumLevel);
		}

		private void StartSimulationLoop()
		{
			int tickRateHz = Math.Max(1, _config.Simulation.TickRateHz);
			int maxCatchUpTicks = Math.Max(1, _config.Simulation.MaxCatchUpTicks);
			float fixedDelta = 1f / tickRateHz;

			_updateLoop.Update += OnUpdate;
			_updateLoop.StartUpdateLoop(fixedDelta, maxCatchUpTicks);
			_logger.Log($"Simulation TickRate: {tickRateHz}Hz, FixedDelta: {fixedDelta:0.0000}s, MaxCatchUpTicks: {maxCatchUpTicks}.", nameof(GameHost));
		}

		private void OnUpdate(float deltaTime)
		{
			_executionScheduler.Drain(
				_config.Simulation.MaxCommandsPerTick,
				HandleInboundCommandAsync);
			_gameStateManager.Update(deltaTime);
		}

		private Task HandleInboundCommandAsync(InboundServerCommand command, CancellationToken token)
		{
			if (command.StateEpoch != _gameStateManager.Epoch) return Task.CompletedTask;
			LogInboundPacket(command.PlayerId, command.PacketType, command.Payload);
			return _inboundMessageRouter.RouteAsync(command.PlayerId, command.PacketType, command.Payload, token);
		}

		//---- Server Callbacks
		//---------------------
		private void OnConnection(int playerId)
		{
			if (!_executionScheduler.TryEnqueue(_ => HandleConnectionAsync(playerId, false)))
			{
				_logger.Warning($"Server inbox full; connection event for Player {playerId} was rejected.", nameof(GameHost));
				_ = _connectionManager?.DisconnectPlayerAsync(playerId, ConnectionCloseReason.ServerInboxFull);
			}
		}

		private async Task HandleConnectionAsync(int playerId, bool reconnect)
		{
			if(_connectionManager is null || _messageDispatcher is null)
			{
				return;
			}

			// send confirm
			if(_connectionManager.TryBuildReconnectToken(playerId, out var reconnectToken))
			{
				var response = BuildHandshakeResponse(playerId, reconnectToken);
				_logger.Log(
					$"Sending handshake response player={playerId} initialState='{response.Runtime.InitialGameStateId}' payloadBytes={response.Runtime.InitialGameStatePayload.Length}.",
					nameof(GameHost));
				await _messageDispatcher.SendToPlayerAsync(
					playerId,
					PacketType.HandshakeResponse,
					HandshakeProtocol.EncodeResponse(response, _payloadCodec));
			}

			if (reconnect)
				await _eventDispatcher.DispatchAsync(new PlayerReconnectedEvent(playerId));
			else
				await _eventDispatcher.DispatchAsync(new PlayerConnectedEvent(playerId));
		}

		private void OnReconnection(int playerId)
		{
			if (!_executionScheduler.TryEnqueue(_ => HandleConnectionAsync(playerId, true)))
			{
				_logger.Warning($"Server inbox full; reconnect event for Player {playerId} was rejected.", nameof(GameHost));
				_ = _connectionManager?.DisconnectPlayerAsync(playerId, ConnectionCloseReason.ServerInboxFull);
			}
		}

		private void OnPacket(int playerId, PacketType packetType, byte[] payload)
		{
			var command = new InboundServerCommand(
				_executionScheduler.NextSequence(),
				_executionScheduler.NextConnectionSequence(playerId),
				_gameStateManager.Epoch,
				playerId,
				packetType,
				payload.ToArray(),
				DateTime.UtcNow);
			if (!_executionScheduler.TryEnqueue(command))
			{
				_logger.Warning($"Server inbox full; packet from Player {playerId} was rejected.", nameof(GameHost));
				_ = _connectionManager?.DisconnectPlayerAsync(playerId, ConnectionCloseReason.ServerInboxFull);
			}
		}

		private HandshakeResponse BuildHandshakeResponse(int playerId, byte[] reconnectToken)
		{
			return new HandshakeResponse
			{
				PlayerId = playerId,
				ReconnectToken = reconnectToken,
				Runtime = new ClientRuntimeManifest
				{
					Services = _clientServiceModules,
					GameStates = _clientGameStateModules,
					ServiceModules = _clientServiceModuleRequirements,
					GameStateModules = _clientGameStateModuleRequirements,
					InitialGameStateId = _gameStateManager.ActiveStateId ?? string.Empty,
					InitialGameStatePayload = CreateActiveGameStatePayloadForPlayer(playerId)
				}
			};
		}

		private byte[] CreateActiveGameStatePayloadForPlayer(int playerId)
		{
			byte[] payload = _gameStateManager.ActiveGameState is IGameStatePayloadProvider provider
				? provider.CreatePayloadForPlayer(playerId, _payloadCodec)
				: Array.Empty<byte>();
			_logger.DebugLog(
				$"Created active state payload player={playerId} state='{_gameStateManager.ActiveStateId ?? string.Empty}' payloadBytes={payload.Length}.",
				nameof(GameHost));
			return payload;
		}

		private void OnConnecitonLost(int playerId, ConnectionCloseReason reason)
		{
			_logger.Warning(
				$"Connection lost player={playerId} reason={reason} activeState='{_gameStateManager.ActiveStateId ?? string.Empty}' sessionTimeoutSeconds={_config.Server.SessionTimeoutSeconds}.",
				nameof(GameHost));
			_executionScheduler.TryEnqueue(token => _eventDispatcher.DispatchAsync(new PlayerConnectionLostEvent(playerId, reason), token));
		}

		private void OnDisconnection(int playerId, ConnectionCloseReason reason)
		{
			_logger.Warning(
				$"Disconnected player={playerId} reason={reason} activeState='{_gameStateManager.ActiveStateId ?? string.Empty}' sessionTimeoutSeconds={_config.Server.SessionTimeoutSeconds}.",
				nameof(GameHost));
			_executionScheduler.TryEnqueue(token => _eventDispatcher.DispatchAsync(new PlayerDisconnectedEvent(playerId, reason), token));
		}

		//--- EventDispatcher
		//-------------------
		private Task OnSendPlayerMessageEvent(SendPlayerMessageEvent gameEvent, CancellationToken token)
		{
			if(_messageDispatcher is null)
			{
				return Task.CompletedTask;
			}

			return _messageDispatcher.SendToPlayerAsync(gameEvent.PlayerId, gameEvent.PacketType, gameEvent.Payload,
				gameEvent.Delivery ?? PacketDeliveryPolicy.GetDefault(gameEvent.PacketType), token);
		}

		private Task OnBroadcast(BroadcastMessageEvent gameEvent, CancellationToken token)
		{
			if(_messageDispatcher is null)
			{
				return Task.CompletedTask;
			}

			return _messageDispatcher.BroadcastAsync(gameEvent.PacketType, gameEvent.Payload,
				gameEvent.Delivery ?? PacketDeliveryPolicy.GetDefault(gameEvent.PacketType), token);
		}

		private async Task OnGameStateTransitionRequested(GameStateTransitionRequestedEvent gameEvent, CancellationToken token)
		{
			string? fromStateId = _gameStateManager.ActiveStateId;
			_logger.Log(
				$"Game-state transition requested from='{fromStateId ?? string.Empty}' to='{gameEvent.ToStateId}' payloadBytes={gameEvent.Payload.Length}.",
				nameof(GameHost));
			bool transitioned = TryTransitionGameState(gameEvent.ToStateId, gameEvent.Payload);
			if(transitioned)
			{
				_logger.Log(
					$"Game-state transition succeeded from='{fromStateId ?? string.Empty}' to='{_gameStateManager.ActiveStateId ?? string.Empty}'.",
					nameof(GameHost));
				await _eventDispatcher.DispatchAsync(
					new GameStateTransitionCompletedEvent(
						fromStateId ?? string.Empty,
						_gameStateManager.ActiveStateId ?? string.Empty),
					token);
				await SendActiveGameStateChangeToConnectedPlayersAsync(token);
				return;
			}

			_logger.Warning(
				$"Game-state transition failed from='{fromStateId ?? string.Empty}' to='{gameEvent.ToStateId}'.",
				nameof(GameHost));
			await _eventDispatcher.DispatchAsync(
				new GameStateTransitionFailedEvent(
					gameEvent.ToStateId,
					fromStateId,
					"Transition request was rejected by the game state manager."),
				token);
		}

		private async Task SendActiveGameStateChangeToConnectedPlayersAsync(CancellationToken token)
		{
			if(_messageDispatcher is null)
			{
				return;
			}

			string stateId = _gameStateManager.ActiveStateId ?? string.Empty;
			int[] playerIds = GetConnectedPlayerIds().ToArray();
			_logger.Log(
				$"Sending active game-state change state='{stateId}' playerCount={playerIds.Length}.",
				nameof(GameHost));
			foreach(int playerId in playerIds)
			{
				var message = new GameStateChangeResponse
				{
					GameStateId = stateId,
					GameStatePayload = CreateActiveGameStatePayloadForPlayer(playerId)
				};
				_logger.Log(
					$"Sending GameStateChange player={playerId} state='{stateId}' payloadBytes={message.GameStatePayload.Length}.",
					nameof(GameHost));
				await _messageDispatcher.SendToPlayerAsync(
					playerId,
					PacketType.GameStateChange,
					GameStateProtocol.EncodeChange(message, _payloadCodec));
			}
		}

		private void LogInboundPacket(int playerId, PacketType packetType, byte[] payload)
		{
			if(packetType == PacketType.HeartBeat)
			{
				_logger.DebugLog(
					$"Inbound packet player={playerId} type={packetType} payloadBytes={payload.Length} activeState='{_gameStateManager.ActiveStateId ?? string.Empty}'.",
					nameof(GameHost));
				return;
			}

			if(packetType != PacketType.HandshakeRequest
				&& packetType != PacketType.ChatLobbyReadyRequest
				&& packetType != PacketType.GameStateMessage
				&& packetType != PacketType.MatchCommandRequest)
			{
				return;
			}

			string messageDetail = string.Empty;
			if(packetType == PacketType.GameStateMessage)
			{
				try
				{
					GameStateMessage message = GameStateMessageProtocol.Decode(payload, _payloadCodec);
					messageDetail = $" messageId='{message.MessageId}' innerPayloadBytes={message.Payload.Length}";
				}
				catch(Exception ex)
				{
					messageDetail = $" messageDecodeError='{ex.Message}'";
				}
			}

			_logger.Log(
				$"Inbound packet player={playerId} type={packetType} payloadBytes={payload.Length} activeState='{_gameStateManager.ActiveStateId ?? string.Empty}'{messageDetail}.",
				nameof(GameHost));
		}

	} // end class
} // end namespace
