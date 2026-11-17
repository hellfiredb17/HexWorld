using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public interface IHawkNetClientBuilder
	{
		GameClientConfig Config { get; }
		IEventDispatcher Events { get; }
		IServiceRegistry Services { get; }

		IHawkNetClientBuilder AddService<TService>(TService service)
			where TService : class;

		IHawkNetClientBuilder ReplaceService<TService>(TService service)
			where TService : class;

		IHawkNetClientBuilder RegisterServiceModule(
			string id,
			Func<IHawkNetClientModule> factory,
			Type[] serviceTypes,
			params string[] dependencies);

		IHawkNetClientBuilder RegisterServiceModule(
			string id,
			string version,
			Func<IHawkNetClientModule> factory,
			Type[] serviceTypes,
			params string[] dependencies);

		IHawkNetClientBuilder RegisterGameStateModule(
			string id,
			Func<IHawkNetClientModule> factory,
			string[] gameStateIds,
			params string[] dependencies);

		IHawkNetClientBuilder RegisterGameStateModule(
			string id,
			string version,
			Func<IHawkNetClientModule> factory,
			string[] gameStateIds,
			params string[] dependencies);

		IHawkNetClientBuilder AddModule(IHawkNetClientModule module);
		IHawkNetClientBuilder AddGameState(string id, Func<IHawkNetClientContext, IClientGameState> factory);
	}

	public sealed record HawkNetClientServiceModuleDescriptor(
		string Id,
		string Version,
		Func<IHawkNetClientModule> Factory,
		Type[] ServiceTypes,
		string[] Dependencies);

	public sealed record HawkNetClientGameStateModuleDescriptor(
		string Id,
		string Version,
		Func<IHawkNetClientModule> Factory,
		string[] GameStateIds,
		string[] Dependencies);

	public interface IHawkNetClientModule
	{
		void Configure(IHawkNetClientBuilder builder);
	}

	public interface IHawkNetClientContext
	{
		IEventDispatcher Events { get; }
		IServiceRegistry Services { get; }
		GameClient Client { get; }
		ClientChatService ChatService { get; }
		ClientPlayerService PlayerService { get; }
		ClientGameStateManager GameStateManager { get; }
	}

	public sealed class HawkNetClient : IAsyncDisposable
	{
		private readonly ServiceRegistry _services = new();
		private readonly List<IService> _startupServices = new();
		private readonly ILogger _logger;
		private readonly IPayloadCodec _payloadCodec;
		private readonly HawkNetClientContext _context;
		private readonly HawkNetClientBuilder _builder;
		private int _startedServiceCount;
		private int _registeredGameStateCount;
		private bool _servicesInitialized;
		private bool _disposed;

		public EventDispatcher EventDispatcher { get; }
		public GameClient GameClient { get; }
		public ClientNetworkMessageHandler NetworkMessageHandler { get; }
		public ClientChatService ChatService => _services.Get<ClientChatService>();
		public ClientPlayerService PlayerService => _services.Get<ClientPlayerService>();
		public ClientGameStateManager GameStateManager { get; }
		public IServiceRegistry Services => _services;
		public IEventDispatcher Events => EventDispatcher;
		public ServiceState State { get; private set; } = ServiceState.NotInitialized;

		private HawkNetClient(GameClientConfig config, Action<IHawkNetClientBuilder>? configure)
		{
			_logger = new NetLogger();
			EventDispatcher = new EventDispatcher(_logger);
			_payloadCodec = new JsonPayloadCodec();
			GameClient = new GameClient(config, _payloadCodec);
			NetworkMessageHandler = new ClientNetworkMessageHandler(GameClient, EventDispatcher, _payloadCodec);
			GameStateManager = new ClientGameStateManager(EventDispatcher);
			_context = new HawkNetClientContext(
				EventDispatcher,
				_services,
				GameClient);

			_builder = new HawkNetClientBuilder(config, EventDispatcher, _services, _startupServices);
			_builder.RegisterServiceModule(
				"core",
				() => new ClientCoreModule(
					EventDispatcher,
					_logger,
					_payloadCodec,
					GameClient,
					NetworkMessageHandler,
					GameStateManager),
				[
					typeof(IEventDispatcher),
					typeof(EventDispatcher),
					typeof(ILogger),
					typeof(IPayloadCodec),
					typeof(GameClient),
					typeof(IClientTransport),
					typeof(ClientNetworkMessageHandler),
					typeof(ClientGameStateManager)
				]);
			AddDefaults(_builder);
			configure?.Invoke(_builder);
			_builder.EnableCoreModule();
			GameClient.HandshakeResponseReceived += OnHandshakeResponseReceived;
		}

		public static HawkNetClient Create(Action<IHawkNetClientBuilder> configure)
		{
			return Create("Config/game-client.json", configure);
		}

		public static HawkNetClient Create(string configPath, Action<IHawkNetClientBuilder>? configure = null)
		{
			return new HawkNetClient(GameClientConfig.Load(configPath), configure);
		}

		public static HawkNetClient Create(GameClientConfig config, Action<IHawkNetClientBuilder>? configure = null)
		{
			return new HawkNetClient(config, configure);
		}

		public async Task ConnectAsync(CancellationToken token = default)
		{
			HawkNet.Core.Guard.NotDisposed(_disposed, this);
			try
			{
				await InitializeServicesAsync(token);
				await GameClient.ConnectAsync(token);
				State = ServiceState.Ready;
			}
			catch(Exception ex)
			{
				await DispatchConnectionFailureAsync(ex, token);
				throw;
			}
		}

		public async Task ReconnectAsync(CancellationToken token = default)
		{
			HawkNet.Core.Guard.NotDisposed(_disposed, this);
			try
			{
				await InitializeServicesAsync(token);
				await GameClient.ReconnectAsync(token);
				State = ServiceState.Ready;
			}
			catch(Exception ex)
			{
				await DispatchConnectionFailureAsync(ex, token);
				throw;
			}
		}

		public async Task DisconnectAsync(CancellationToken token = default)
		{
			if(_disposed)
			{
				return;
			}

			await GameClient.DisconnectAsync(token);
			State = ServiceState.NotInitialized;
		}

		public async Task ShutdownAsync(CancellationToken token = default)
		{
			if(_disposed)
			{
				return;
			}

			await GameClient.DisconnectAsync(token);
			await ShutdownServicesAsync(token);
			State = ServiceState.NotInitialized;
		}

		public async ValueTask DisposeAsync()
		{
			if(_disposed)
			{
				return;
			}

			GameClient.HandshakeResponseReceived -= OnHandshakeResponseReceived;
			await ShutdownAsync();
			GameClient.Dispose();
			_disposed = true;
		}

		private void RegisterPendingGameStates()
		{
			foreach(var registration in _builder.GameStates.Skip(_registeredGameStateCount))
			{
				GameStateManager.RegisterState(
					registration.Id,
					() => registration.Factory(_context));
				_registeredGameStateCount++;
			}
		}

		private async Task InitializeServicesAsync(CancellationToken token)
		{
			if(_servicesInitialized && _startedServiceCount == _startupServices.Count)
			{
				return;
			}

			State = ServiceState.Initializing;
			for(int index = _startedServiceCount; index < _startupServices.Count; index++)
			{
				var service = _startupServices[index];
				service.RegisterServices();
				await service.CreateAsync(token);
				await service.InitializeAsync(token);
				_startedServiceCount++;
			}

			_servicesInitialized = true;
		}

		private async Task ShutdownServicesAsync(CancellationToken token)
		{
			if(!_servicesInitialized)
			{
				return;
			}

			foreach(var service in _startupServices.AsEnumerable().Reverse())
			{
				await service.ShutdownAsync(token);
				service.UnregisterServices();
			}

			_startedServiceCount = 0;
			_servicesInitialized = false;
		}

		private static void AddDefaults(IHawkNetClientBuilder builder)
		{
			builder
				.RegisterServiceModule(
					"player_identity",
					() => new ClientPlayerIdentityModule(),
					[
						typeof(ClientPlayerService)
					],
					"core")
				.RegisterServiceModule(
					"diagnostics",
					() => new ClientDiagnosticsModule(),
					[
						typeof(ClientDiagnostics)
					],
					"core")
				.RegisterServiceModule(
					"chat_service",
					() => new ClientChatServiceModule(),
					[
						typeof(ClientChatService)
					],
					"core",
					"player_identity")
				.RegisterGameStateModule(
					"chat_lobby",
					() => new ClientChatLobbyModule(),
					[
						"chat_lobby"
					],
					"core",
					"player_identity",
					"chat_service")
				.RegisterGameStateModule(
					"match",
					() => new ClientMatchModule(),
					[
						"match"
					],
					"core",
					"player_identity",
					"chat_service");
		}

		private async Task OnHandshakeResponseReceived(HandshakeResponse response, CancellationToken token)
		{
			try
			{
				_builder.EnableRuntimeModules(response.Runtime);
				RegisterPendingGameStates();
				await InitializeServicesAsync(token);
			}
			catch(HawkNetClientCompatibilityException ex)
			{
				await EventDispatcher.DispatchAsync(
					new ClientCompatibilityFailedEvent(
						ex.Reason,
						ex.Message,
						ex.ModuleId,
						ex.DependencyId,
						ex.RequestedVersion,
						ex.SupportedVersion,
						ex.Manifest),
					token);
				throw;
			}
		}

		private async Task DispatchConnectionFailureAsync(Exception exception, CancellationToken token)
		{
			if(exception is HawkNetClientCompatibilityException)
			{
				return;
			}

			if(GameClient.State == GameClientState.Faulted)
			{
				await EventDispatcher.DispatchAsync(
					new ClientFaultedEvent(exception.Message, exception),
					token);
			}

			await EventDispatcher.DispatchAsync(
				new ClientHandshakeFailedEvent(exception.Message, exception),
				token);
		}
	}

	internal sealed class HawkNetClientBuilder : IHawkNetClientBuilder
	{
		private readonly List<ClientGameStateRegistration> _gameStates = new();
		private readonly Dictionary<string, HawkNetClientServiceModuleDescriptor> _serviceModules = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HawkNetClientGameStateModuleDescriptor> _gameStateModules = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _enabledServiceModules = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _enabledGameStateModules = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<IService> _startupServices;

		public HawkNetClientBuilder(
			GameClientConfig config,
			IEventDispatcher events,
			IServiceRegistry services,
			List<IService> startupServices)
		{
			Config = config;
			Events = events;
			Services = services;
			_startupServices = startupServices;
		}

		public GameClientConfig Config { get; }
		public IEventDispatcher Events { get; }
		public IServiceRegistry Services { get; }
		public IReadOnlyList<ClientGameStateRegistration> GameStates => _gameStates;
		public IReadOnlyCollection<string> EnabledServiceModules => _enabledServiceModules;
		public IReadOnlyCollection<string> EnabledGameStateModules => _enabledGameStateModules;

		public IHawkNetClientBuilder AddService<TService>(TService service)
			where TService : class
		{
			Services.Register(service);
			if(service is IService lifecycleService)
			{
				_startupServices.Add(lifecycleService);
			}

			return this;
		}

		public IHawkNetClientBuilder ReplaceService<TService>(TService service)
			where TService : class
		{
			Services.Replace(service);
			return this;
		}

		public IHawkNetClientBuilder RegisterServiceModule(
			string id,
			Func<IHawkNetClientModule> factory,
			Type[] serviceTypes,
			params string[] dependencies)
		{
			return RegisterServiceModule(id, HandshakeProtocol.CurrentModuleVersion, factory, serviceTypes, dependencies);
		}

		public IHawkNetClientBuilder RegisterServiceModule(
			string id,
			string version,
			Func<IHawkNetClientModule> factory,
			Type[] serviceTypes,
			params string[] dependencies)
		{
			string normalizedId = NormalizeModuleId(id);
			string normalizedVersion = NormalizeModuleVersion(version);
			HawkNet.Core.Guard.NotNull(factory);
			HawkNet.Core.Guard.NotNull(serviceTypes);
			HawkNet.Core.Guard.NotNull(dependencies);
			if(!_serviceModules.TryAdd(
				normalizedId,
				new HawkNetClientServiceModuleDescriptor(
					normalizedId,
					normalizedVersion,
					factory,
					serviceTypes.ToArray(),
					NormalizeModuleIds(dependencies))))
			{
				throw new InvalidOperationException($"Client service module '{normalizedId}' is already registered.");
			}

			return this;
		}

		public IHawkNetClientBuilder RegisterGameStateModule(
			string id,
			Func<IHawkNetClientModule> factory,
			string[] gameStateIds,
			params string[] dependencies)
		{
			return RegisterGameStateModule(id, HandshakeProtocol.CurrentModuleVersion, factory, gameStateIds, dependencies);
		}

		public IHawkNetClientBuilder RegisterGameStateModule(
			string id,
			string version,
			Func<IHawkNetClientModule> factory,
			string[] gameStateIds,
			params string[] dependencies)
		{
			string normalizedId = NormalizeModuleId(id);
			string normalizedVersion = NormalizeModuleVersion(version);
			HawkNet.Core.Guard.NotNull(factory);
			HawkNet.Core.Guard.NotNull(gameStateIds);
			HawkNet.Core.Guard.NotNull(dependencies);
			if(!_gameStateModules.TryAdd(
				normalizedId,
				new HawkNetClientGameStateModuleDescriptor(
					normalizedId,
					normalizedVersion,
					factory,
					NormalizeGameStateIds(gameStateIds),
					NormalizeModuleIds(dependencies))))
			{
				throw new InvalidOperationException($"Client game-state module '{normalizedId}' is already registered.");
			}

			return this;
		}

		public IHawkNetClientBuilder AddModule(IHawkNetClientModule module)
		{
			HawkNet.Core.Guard.NotNull(module);
			module.Configure(this);
			return this;
		}

		public IHawkNetClientBuilder AddGameState(string id, Func<IHawkNetClientContext, IClientGameState> factory)
		{
			if(string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException("Client game state id cannot be empty.", nameof(id));
			}

			HawkNet.Core.Guard.NotNull(factory);
			_gameStates.Add(new ClientGameStateRegistration(id.Trim(), factory));
			return this;
		}

		public void EnableCoreModule()
		{
			if(!_serviceModules.TryGetValue("core", out var descriptor))
			{
				throw new InvalidOperationException("The required client service module 'core' is not registered.");
			}

			descriptor.Factory().Configure(this);
			_enabledServiceModules.Add("core");
		}

		public void EnableRuntimeModules(ClientRuntimeManifest manifest)
		{
			HawkNet.Core.Guard.NotNull(manifest);
			ValidateRuntimeManifestVersion(manifest);
			RuntimeModuleRequirement[] serviceModules = NormalizeModuleRequirements(manifest.ServiceModules, manifest.Services);
			RuntimeModuleRequirement[] gameStateModules = NormalizeModuleRequirements(manifest.GameStateModules, manifest.GameStates);
			ValidateRuntimeModules(manifest, serviceModules, gameStateModules, manifest.InitialGameStateId);

			foreach(string moduleId in serviceModules.Select(module => module.Id))
			{
				if(_enabledServiceModules.Contains(moduleId))
				{
					continue;
				}

				_serviceModules[moduleId].Factory().Configure(this);
				_enabledServiceModules.Add(moduleId);
			}

			foreach(string moduleId in gameStateModules.Select(module => module.Id))
			{
				if(_enabledGameStateModules.Contains(moduleId))
				{
					continue;
				}

				_gameStateModules[moduleId].Factory().Configure(this);
				_enabledGameStateModules.Add(moduleId);
			}
		}

		private static void ValidateRuntimeManifestVersion(ClientRuntimeManifest manifest)
		{
			if(manifest.ManifestVersion != HandshakeProtocol.CurrentRuntimeManifestVersion)
			{
				throw new HawkNetClientCompatibilityException(
					ClientCompatibilityFailureReason.UnsupportedManifestVersion,
					$"Unsupported client runtime manifest version '{manifest.ManifestVersion}'. Expected '{HandshakeProtocol.CurrentRuntimeManifestVersion}'.",
					manifest);
			}

			if(!string.Equals(manifest.ProtocolVersion, HandshakeProtocol.CurrentProtocolVersion, StringComparison.Ordinal))
			{
				throw new HawkNetClientCompatibilityException(
					ClientCompatibilityFailureReason.UnsupportedProtocolVersion,
					$"Unsupported HawkNet protocol version '{manifest.ProtocolVersion}'. Expected '{HandshakeProtocol.CurrentProtocolVersion}'.",
					manifest);
			}
		}

		private void ValidateRuntimeModules(
			ClientRuntimeManifest manifest,
			RuntimeModuleRequirement[] serviceModules,
			RuntimeModuleRequirement[] gameStateModules,
			string initialGameStateId)
		{
			EnsureNoDuplicates(
				manifest,
				serviceModules.Select(module => module.Id),
				"client service",
				ClientCompatibilityFailureReason.DuplicateServiceModule);
			EnsureNoDuplicates(
				manifest,
				gameStateModules.Select(module => module.Id),
				"client game-state",
				ClientCompatibilityFailureReason.DuplicateGameStateModule);

			var enabledServiceSet = serviceModules.Select(module => module.Id)
				.Concat(_enabledServiceModules)
				.Append("core")
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var enabledGameStateSet = gameStateModules.Select(module => module.Id)
				.Concat(_enabledGameStateModules)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			foreach(RuntimeModuleRequirement module in serviceModules)
			{
				string moduleId = module.Id;
				if(string.Equals(moduleId, "core", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if(!_serviceModules.TryGetValue(moduleId, out var descriptor))
				{
					if(_gameStateModules.ContainsKey(moduleId))
					{
						throw new HawkNetClientCompatibilityException(
							ClientCompatibilityFailureReason.GameStateModuleListedAsService,
							$"Module '{moduleId}' is a client game-state module and cannot be listed as a client service module.",
							manifest,
							moduleId);
					}

					throw new HawkNetClientCompatibilityException(
						ClientCompatibilityFailureReason.UnknownServiceModule,
						$"Unknown client service module '{moduleId}'.",
						manifest,
						moduleId);
				}

				if(!IsModuleVersionSupported(module.Version, descriptor.Version))
				{
					throw new HawkNetClientCompatibilityException(
						ClientCompatibilityFailureReason.UnsupportedServiceModuleVersion,
						$"Client service module '{moduleId}' version '{module.Version}' is not supported. Supported version is '{descriptor.Version}'.",
						manifest,
						moduleId,
						requestedVersion: module.Version,
						supportedVersion: descriptor.Version);
				}

				foreach(string dependency in descriptor.Dependencies)
				{
					if(!enabledServiceSet.Contains(dependency))
					{
						throw new HawkNetClientCompatibilityException(
							ClientCompatibilityFailureReason.MissingModuleDependency,
							$"Client service module '{moduleId}' requires service module '{dependency}'.",
							manifest,
							moduleId,
							dependency);
					}
				}
			}

			foreach(RuntimeModuleRequirement module in gameStateModules)
			{
				string moduleId = module.Id;
				if(!_gameStateModules.TryGetValue(moduleId, out var descriptor))
				{
					if(_serviceModules.ContainsKey(moduleId))
					{
						throw new HawkNetClientCompatibilityException(
							ClientCompatibilityFailureReason.ServiceModuleListedAsGameState,
							$"Module '{moduleId}' is a client service module and cannot be listed as a client game-state module.",
							manifest,
							moduleId);
					}

					throw new HawkNetClientCompatibilityException(
						ClientCompatibilityFailureReason.UnknownGameStateModule,
						$"Unknown client game-state module '{moduleId}'.",
						manifest,
						moduleId);
				}

				if(!IsModuleVersionSupported(module.Version, descriptor.Version))
				{
					throw new HawkNetClientCompatibilityException(
						ClientCompatibilityFailureReason.UnsupportedGameStateModuleVersion,
						$"Client game-state module '{moduleId}' version '{module.Version}' is not supported. Supported version is '{descriptor.Version}'.",
						manifest,
						moduleId,
						requestedVersion: module.Version,
						supportedVersion: descriptor.Version);
				}

				foreach(string dependency in descriptor.Dependencies)
				{
					if(!enabledServiceSet.Contains(dependency) && !enabledGameStateSet.Contains(dependency))
					{
						throw new HawkNetClientCompatibilityException(
							ClientCompatibilityFailureReason.MissingModuleDependency,
							$"Client game-state module '{moduleId}' requires module '{dependency}'.",
							manifest,
							moduleId,
							dependency);
					}
				}
			}

			if(!string.IsNullOrWhiteSpace(initialGameStateId))
			{
				var providedGameStateIds = gameStateModules.Select(module => module.Id)
					.SelectMany(moduleId => _gameStateModules[moduleId].GameStateIds)
					.Concat(_gameStates.Select(registration => registration.Id))
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				if(!providedGameStateIds.Contains(initialGameStateId.Trim()))
				{
					throw new HawkNetClientCompatibilityException(
						ClientCompatibilityFailureReason.InitialGameStateNotAvailable,
						$"Initial client game state '{initialGameStateId}' is not provided by any enabled client game-state module.",
						manifest,
						initialGameStateId);
				}
			}
		}

		private static void EnsureNoDuplicates(
			ClientRuntimeManifest manifest,
			IEnumerable<string> moduleIds,
			string moduleType,
			ClientCompatibilityFailureReason reason)
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach(string moduleId in moduleIds)
			{
				if(!seen.Add(moduleId))
				{
					throw new HawkNetClientCompatibilityException(
						reason,
						$"Duplicate {moduleType} module '{moduleId}' is enabled.",
						manifest,
						moduleId);
				}
			}
		}

		private static RuntimeModuleRequirement[] NormalizeModuleRequirements(
			IEnumerable<RuntimeModuleRequirement> moduleRequirements,
			IEnumerable<string> legacyModuleIds)
		{
			var requirements = moduleRequirements.ToArray();
			if(requirements.Length > 0)
			{
				return requirements
					.Select(requirement => new RuntimeModuleRequirement
					{
						Id = NormalizeModuleId(requirement.Id),
						Version = NormalizeModuleVersion(requirement.Version),
						Required = requirement.Required
					})
					.ToArray();
			}

			return legacyModuleIds
				.Select(id => new RuntimeModuleRequirement
				{
					Id = NormalizeModuleId(id),
					Version = HandshakeProtocol.CurrentModuleVersion,
					Required = true
				})
				.ToArray();
		}

		private static bool IsModuleVersionSupported(string requestedVersion, string supportedVersion)
		{
			return string.Equals(requestedVersion, supportedVersion, StringComparison.Ordinal);
		}

		private static string NormalizeModuleId(string id)
		{
			if(string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException("Module id cannot be empty.", nameof(id));
			}

			return id.Trim();
		}

		private static string NormalizeModuleVersion(string version)
		{
			if(string.IsNullOrWhiteSpace(version))
			{
				throw new ArgumentException("Module version cannot be empty.", nameof(version));
			}

			return version.Trim();
		}

		private static string[] NormalizeModuleIds(IEnumerable<string> ids)
		{
			return ids.Select(NormalizeModuleId).ToArray();
		}

		private static string[] NormalizeGameStateIds(IEnumerable<string> ids)
		{
			return ids
				.Select(id =>
				{
					if(string.IsNullOrWhiteSpace(id))
					{
						throw new ArgumentException("Game state id cannot be empty.", nameof(ids));
					}

					return id.Trim();
				})
				.ToArray();
		}
	}

	internal sealed class HawkNetClientContext : IHawkNetClientContext
	{
		public HawkNetClientContext(
			IEventDispatcher events,
			IServiceRegistry services,
			GameClient client)
		{
			Events = events;
			Services = services;
			Client = client;
		}

		public IEventDispatcher Events { get; }
		public IServiceRegistry Services { get; }
		public GameClient Client { get; }
		public ClientChatService ChatService => Services.Get<ClientChatService>();
		public ClientPlayerService PlayerService => Services.Get<ClientPlayerService>();
		public ClientGameStateManager GameStateManager => Services.Get<ClientGameStateManager>();
	}

	internal readonly record struct ClientGameStateRegistration(
		string Id,
		Func<IHawkNetClientContext, IClientGameState> Factory);

	internal sealed class ClientCoreModule : IHawkNetClientModule
	{
		private readonly EventDispatcher _eventDispatcher;
		private readonly ILogger _logger;
		private readonly IPayloadCodec _payloadCodec;
		private readonly GameClient _gameClient;
		private readonly ClientNetworkMessageHandler _networkMessageHandler;
		private readonly ClientGameStateManager _gameStateManager;

		public ClientCoreModule(
			EventDispatcher eventDispatcher,
			ILogger logger,
			IPayloadCodec payloadCodec,
			GameClient gameClient,
			ClientNetworkMessageHandler networkMessageHandler,
			ClientGameStateManager gameStateManager)
		{
			_eventDispatcher = eventDispatcher;
			_logger = logger;
			_payloadCodec = payloadCodec;
			_gameClient = gameClient;
			_networkMessageHandler = networkMessageHandler;
			_gameStateManager = gameStateManager;
		}

		public void Configure(IHawkNetClientBuilder builder)
		{
			builder
				.AddService<IEventDispatcher>(_eventDispatcher)
				.AddService(_eventDispatcher)
				.AddService<ILogger>(_logger)
				.AddService<IPayloadCodec>(_payloadCodec)
				.AddService(_gameClient)
				.AddService<IClientTransport>(_gameClient.Transport)
				.AddService(_networkMessageHandler)
				.AddService(_gameStateManager);
		}
	}

	public sealed class ClientDiagnostics : IService
	{
		private ServiceState _state = ServiceState.NotInitialized;

		public ServiceState State => _state;

		public Task CreateAsync(CancellationToken token = default)
		{
			_state = ServiceState.Initializing;
			return Task.CompletedTask;
		}

		public Task InitializeAsync(CancellationToken token = default)
		{
			_state = ServiceState.Ready;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken token = default)
		{
			_state = ServiceState.NotInitialized;
			return Task.CompletedTask;
		}

		public void RegisterServices()
		{
		}

		public void UnregisterServices()
		{
		}
	}

	internal sealed class ClientPlayerIdentityModule : IHawkNetClientModule
	{
		public void Configure(IHawkNetClientBuilder builder)
		{
			builder.AddService(new ClientPlayerService(builder.Services.Get<IEventDispatcher>()));
		}
	}

	internal sealed class ClientDiagnosticsModule : IHawkNetClientModule
	{
		public void Configure(IHawkNetClientBuilder builder)
		{
			builder.AddService(new ClientDiagnostics());
		}
	}

	internal sealed class ClientChatServiceModule : IHawkNetClientModule
	{
		public void Configure(IHawkNetClientBuilder builder)
		{
			builder.AddService(new ClientChatService(
				builder.Services.Get<GameClient>(),
				builder.Services.Get<IEventDispatcher>(),
				builder.Services.Get<IPayloadCodec>()));
		}
	}

	internal sealed class ClientChatLobbyModule : IHawkNetClientModule
	{
		public void Configure(IHawkNetClientBuilder builder)
		{
			builder.AddGameState("chat_lobby", context =>
				new ClientChatLobbyState(
					context.Events,
					context.Client,
					context.ChatService,
					context.PlayerService,
					context.Services.Get<IPayloadCodec>()));
		}
	}

	internal sealed class ClientMatchModule : IHawkNetClientModule
	{
		public void Configure(IHawkNetClientBuilder builder)
		{
			builder.AddService(new ClientMatchService(
				builder.Services.Get<GameClient>(),
				builder.Services.Get<IPayloadCodec>()));

			builder.AddGameState("match", context =>
				new ClientMatchState(
					context.Events,
					context.Services.Get<IPayloadCodec>()));
		}
	}
}
