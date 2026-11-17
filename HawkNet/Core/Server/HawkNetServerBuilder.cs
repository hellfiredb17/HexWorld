using HawkNet.Game.GameState.Lobby;
using HawkNet.Game.GameState.Match;
using HawkNet.Core.Shared;
using System.Text.Json;

namespace HawkNet.Core.Server
{
	public interface IHawkNetServerBuilder
	{
		GameHostConfig Config { get; }
		IEventDispatcher Events { get; }
		IServiceRegistry Services { get; }

		IHawkNetServerBuilder AddService<TService>(TService service)
			where TService : class;

		IHawkNetServerBuilder ReplaceService<TService>(TService service)
			where TService : class;

		IHawkNetServerBuilder RegisterServiceModule(
			string id,
			Func<IHawkNetServerModule> factory,
			Type[] serviceTypes,
			params string[] dependencies);

		IHawkNetServerBuilder RegisterServiceModule(
			string id,
			string version,
			Func<IHawkNetServerModule> factory,
			Type[] serviceTypes,
			params string[] dependencies);

		IHawkNetServerBuilder RegisterGameStateModule(
			string id,
			Func<IHawkNetServerModule> factory,
			string[] gameStateIds,
			params string[] dependencies);

		IHawkNetServerBuilder RegisterGameStateModule(
			string id,
			string version,
			Func<IHawkNetServerModule> factory,
			string[] gameStateIds,
			params string[] dependencies);

		IHawkNetServerBuilder AddModule(IHawkNetServerModule module);
		IHawkNetServerBuilder AddGameState(string id, Func<IHawkNetServerContext, IGameState> factory);
		IHawkNetServerBuilder RegisterMatchVariant(string id, Func<IGameMatchContext, IGameMatch> factory);
		TOptions GetOptions<TOptions>(string sectionId, TOptions defaults) where TOptions : class => defaults;
	}

	public sealed record HawkNetServerServiceModuleDescriptor(
		string Id,
		string Version,
		Func<IHawkNetServerModule> Factory,
		Type[] ServiceTypes,
		string[] Dependencies);

	public sealed record HawkNetServerGameStateModuleDescriptor(
		string Id,
		string Version,
		Func<IHawkNetServerModule> Factory,
		string[] GameStateIds,
		string[] Dependencies);

	public interface IHawkNetServerModule
	{
		void Configure(IHawkNetServerBuilder builder);
	}

	public interface IHawkNetServerContext
	{
		IEventDispatcher Events { get; }
		ILogger Logger { get; }
		IConfigService Config { get; }
		ServerDiagnostics? Diagnostics { get; }
		IPlayerIdentityService? PlayerIdentityService { get; }
		ChatService? ChatService { get; }
		IServiceRegistry Services { get; }
		T GetRequired<T>() where T : class => Services.Get<T>();
		bool TryGet<T>(out T service) where T : class => Services.TryGet(out service);

		IEnumerable<int> GetConnectedPlayerIds();
	}

	internal sealed class HawkNetServerBuilder : IHawkNetServerBuilder
	{
		private readonly List<ServerGameStateRegistration> _gameStates = new();
		private readonly Dictionary<string, HawkNetServerServiceModuleDescriptor> _serviceModules = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HawkNetServerGameStateModuleDescriptor> _gameStateModules = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _enabledServiceModules = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _enabledGameStateModules = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<string> _enabledServiceModuleOrder = new();
		private readonly List<string> _enabledGameStateModuleOrder = new();
		private readonly HashSet<Type> _explicitServiceReplacements = new();
		private readonly MatchVariantRegistry _matchVariants = new();
		private readonly ConfigService _configService;

		public HawkNetServerBuilder(
			GameHostConfig config,
			IEventDispatcher events,
			IServiceRegistry services,
			ConfigService configService)
		{
			Config = config;
			Events = events;
			Services = services;
			_configService = configService;
		}

		public GameHostConfig Config { get; }
		public IEventDispatcher Events { get; }
		public IServiceRegistry Services { get; }
		public IReadOnlyList<ServerGameStateRegistration> GameStates => _gameStates;
		public IReadOnlyCollection<string> EnabledServiceModules => _enabledServiceModules;
		public IReadOnlyCollection<string> EnabledGameStateModules => _enabledGameStateModules;
		public MatchVariantRegistry MatchVariants => _matchVariants;
		public IReadOnlyList<RuntimeModuleRequirement> EnabledServiceModuleRequirements =>
			_enabledServiceModuleOrder
				.Select(moduleId => new RuntimeModuleRequirement
				{
					Id = moduleId,
					Version = _serviceModules[moduleId].Version,
					Required = true
				})
				.ToArray();
		public IReadOnlyList<RuntimeModuleRequirement> EnabledGameStateModuleRequirements =>
			_enabledGameStateModuleOrder
				.Select(moduleId => new RuntimeModuleRequirement
				{
					Id = moduleId,
					Version = _gameStateModules[moduleId].Version,
					Required = true
				})
				.ToArray();

		public IHawkNetServerBuilder AddService<TService>(TService service)
			where TService : class
		{
			if(_explicitServiceReplacements.Contains(typeof(TService)))
			{
				return this;
			}

			Services.Register(service);
			return this;
		}

		public IHawkNetServerBuilder ReplaceService<TService>(TService service)
			where TService : class
		{
			_explicitServiceReplacements.Add(typeof(TService));
			Services.Replace(service);
			return this;
		}

		public IHawkNetServerBuilder RegisterServiceModule(
			string id,
			Func<IHawkNetServerModule> factory,
			Type[] serviceTypes,
			params string[] dependencies)
		{
			return RegisterServiceModule(id, HandshakeProtocol.CurrentModuleVersion, factory, serviceTypes, dependencies);
		}

		public IHawkNetServerBuilder RegisterServiceModule(
			string id,
			string version,
			Func<IHawkNetServerModule> factory,
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
				new HawkNetServerServiceModuleDescriptor(
					normalizedId,
					normalizedVersion,
					factory,
					serviceTypes.ToArray(),
					NormalizeModuleIds(dependencies))))
			{
				throw new InvalidOperationException($"Service module '{normalizedId}' is already registered.");
			}

			return this;
		}

		public IHawkNetServerBuilder RegisterGameStateModule(
			string id,
			Func<IHawkNetServerModule> factory,
			string[] gameStateIds,
			params string[] dependencies)
		{
			return RegisterGameStateModule(id, HandshakeProtocol.CurrentModuleVersion, factory, gameStateIds, dependencies);
		}

		public IHawkNetServerBuilder RegisterGameStateModule(
			string id,
			string version,
			Func<IHawkNetServerModule> factory,
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
				new HawkNetServerGameStateModuleDescriptor(
					normalizedId,
					normalizedVersion,
					factory,
					NormalizeGameStateIds(gameStateIds),
					NormalizeModuleIds(dependencies))))
			{
				throw new InvalidOperationException($"Game-state module '{normalizedId}' is already registered.");
			}

			return this;
		}

		public IHawkNetServerBuilder AddModule(IHawkNetServerModule module)
		{
			HawkNet.Core.Guard.NotNull(module);
			module.Configure(this);
			return this;
		}

		public IHawkNetServerBuilder AddGameState(string id, Func<IHawkNetServerContext, IGameState> factory)
		{
			if(string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException("Game state id cannot be empty.", nameof(id));
			}

			HawkNet.Core.Guard.NotNull(factory);
			_gameStates.Add(new ServerGameStateRegistration(id.Trim(), factory));
			return this;
		}

		public IHawkNetServerBuilder RegisterMatchVariant(string id, Func<IGameMatchContext, IGameMatch> factory)
		{
			_matchVariants.Register(id, factory);
			return this;
		}

		public TOptions GetOptions<TOptions>(string sectionId, TOptions defaults) where TOptions : class
		{
			if (string.IsNullOrWhiteSpace(sectionId)) throw new ArgumentException("Options section id cannot be empty.", nameof(sectionId));
			TOptions options = defaults;
			if (Config.ModuleOptions.TryGetValue(sectionId.Trim(), out JsonElement element))
			{
				options = element.Deserialize<TOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
					?? throw new InvalidOperationException($"Module options '{sectionId}' could not be deserialized as {typeof(TOptions).Name}.");
			}
			_configService.Register(options);
			Services.Replace(options);
			return options;
		}

		public void EnableConfiguredModules()
		{
			string[] serviceModuleIds = NormalizeModuleIds(Config.Modules.Services);
			string[] gameStateModuleIds = NormalizeModuleIds(Config.Modules.GameStates);
			ValidateConfiguredModules(serviceModuleIds, gameStateModuleIds);

			foreach(string moduleId in ResolveServiceModuleOrder(serviceModuleIds))
			{
				_serviceModules[moduleId].Factory().Configure(this);
				_enabledServiceModules.Add(moduleId);
				_enabledServiceModuleOrder.Add(moduleId);
			}

			foreach(string moduleId in ResolveGameStateModuleOrder(gameStateModuleIds))
			{
				_gameStateModules[moduleId].Factory().Configure(this);
				_enabledGameStateModules.Add(moduleId);
				_enabledGameStateModuleOrder.Add(moduleId);
			}
		}

		private IReadOnlyList<string> ResolveServiceModuleOrder(string[] configured)
		{
			return TopologicalSort(configured, id => _serviceModules[id].Dependencies
				.Where(dependency => _serviceModules.ContainsKey(dependency)));
		}

		private IReadOnlyList<string> ResolveGameStateModuleOrder(string[] configured)
		{
			return TopologicalSort(configured, id => _gameStateModules[id].Dependencies
				.Where(dependency => _gameStateModules.ContainsKey(dependency)));
		}

		private static IReadOnlyList<string> TopologicalSort(
			IReadOnlyList<string> configured,
			Func<string, IEnumerable<string>> dependencies)
		{
			var configuredSet = configured.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var result = new List<string>();

			void Visit(string id)
			{
				if (visited.Contains(id)) return;
				if (!visiting.Add(id))
					throw new InvalidOperationException($"Module dependency cycle detected at '{id}'.");
				foreach (string dependency in dependencies(id).Where(configuredSet.Contains)) Visit(dependency);
				visiting.Remove(id);
				visited.Add(id);
				result.Add(id);
			}

			foreach (string id in configured) Visit(id);
			return result;
		}

		private void ValidateConfiguredModules(string[] serviceModuleIds, string[] gameStateModuleIds)
		{
			if(serviceModuleIds.Length == 0)
			{
				throw new InvalidOperationException("At least one service module must be enabled. The 'core' service module is required.");
			}

			EnsureNoDuplicates(serviceModuleIds, "service");
			EnsureNoDuplicates(gameStateModuleIds, "game-state");

			if(!serviceModuleIds.Contains("core", StringComparer.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("The required service module 'core' is not enabled.");
			}

			var enabledServiceSet = serviceModuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var enabledGameStateSet = gameStateModuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

			foreach(string moduleId in serviceModuleIds)
			{
				if(!_serviceModules.TryGetValue(moduleId, out var descriptor))
				{
					if(_gameStateModules.ContainsKey(moduleId))
					{
						throw new InvalidOperationException($"Module '{moduleId}' is a game-state module and cannot be listed under Modules.Services.");
					}

					throw new InvalidOperationException($"Unknown service module '{moduleId}'.");
				}

				foreach(string dependency in descriptor.Dependencies)
				{
					if(!enabledServiceSet.Contains(dependency))
					{
						throw new InvalidOperationException($"Service module '{moduleId}' requires service module '{dependency}'.");
					}
				}
			}

			foreach(string moduleId in gameStateModuleIds)
			{
				if(!_gameStateModules.TryGetValue(moduleId, out var descriptor))
				{
					if(_serviceModules.ContainsKey(moduleId))
					{
						throw new InvalidOperationException($"Module '{moduleId}' is a service module and cannot be listed under Modules.GameStates.");
					}

					throw new InvalidOperationException($"Unknown game-state module '{moduleId}'.");
				}

				foreach(string dependency in descriptor.Dependencies)
				{
					if(!enabledServiceSet.Contains(dependency) && !enabledGameStateSet.Contains(dependency))
					{
						throw new InvalidOperationException($"Game-state module '{moduleId}' requires module '{dependency}'.");
					}
				}
			}

			string initialStateId = Config.GameMode.InitialStateId.Trim();
			var providedGameStateIds = gameStateModuleIds
				.SelectMany(moduleId => _gameStateModules[moduleId].GameStateIds)
				.Concat(_gameStates.Select(registration => registration.Id))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			if(!string.IsNullOrWhiteSpace(initialStateId) && !providedGameStateIds.Contains(initialStateId))
			{
				throw new InvalidOperationException($"GameMode initial state '{initialStateId}' is not provided by any enabled game-state module.");
			}
		}

		private static void EnsureNoDuplicates(string[] moduleIds, string moduleType)
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach(string moduleId in moduleIds)
			{
				if(!seen.Add(moduleId))
				{
					throw new InvalidOperationException($"Duplicate {moduleType} module '{moduleId}' is enabled.");
				}
			}
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
			return ids
				.Select(NormalizeModuleId)
				.ToArray();
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

	internal sealed class HawkNetServerContext : IHawkNetServerContext
	{
		private readonly Func<IEnumerable<int>> _connectedPlayers;

		public HawkNetServerContext(
			IEventDispatcher events,
			ILogger logger,
			IServiceRegistry services,
			Func<IEnumerable<int>> connectedPlayers)
		{
			Events = events;
			Logger = logger;
			Services = services;
			_connectedPlayers = connectedPlayers;
		}

		public IEventDispatcher Events { get; }
		public ILogger Logger { get; }
		public IConfigService Config => Services.Get<IConfigService>();
		public ServerDiagnostics? Diagnostics => Services.TryGet<ServerDiagnostics>(out var value) ? value : null;
		public IPlayerIdentityService? PlayerIdentityService => Services.TryGet<IPlayerIdentityService>(out var value) ? value : null;
		public ChatService? ChatService => Services.TryGet<ChatService>(out var value) ? value : null;
		public IServiceRegistry Services { get; }
		public T GetRequired<T>() where T : class => Services.Get<T>();
		public bool TryGet<T>(out T service) where T : class => Services.TryGet(out service);

		public IEnumerable<int> GetConnectedPlayerIds()
		{
			return _connectedPlayers();
		}
	}

	internal readonly record struct ServerGameStateRegistration(
		string Id,
		Func<IHawkNetServerContext, IGameState> Factory);

	public static class HawkNetServer
	{
		public static GameHost Create(Action<IHawkNetServerBuilder> configure)
		{
			return Create("Config/game-server.json", configure);
		}

		public static GameHost Create(string configPath, Action<IHawkNetServerBuilder>? configure = null)
		{
			return new GameHost(configPath, configure);
		}

		public static GameHost Create(GameHostConfig config, Action<IHawkNetServerBuilder>? configure = null)
		{
			return new GameHost(config, configure);
		}

		internal static void AddDefaults(IHawkNetServerBuilder builder)
		{
			builder
				.RegisterMatchVariant("base", _ => new GameMatchBase())
				.RegisterServiceModule(
					"player_identity",
					() => new ServerPlayerIdentityModule(),
					[
						typeof(IPlayerIdentityService),
						typeof(PlayerIdentityService)
					],
					"core")
				.RegisterServiceModule(
					"diagnostics",
					() => new ServerDiagnosticsModule(),
					[
						typeof(ServerDiagnostics)
					],
					"core")
				.RegisterServiceModule(
					"chat_service",
					() => new ServerChatServiceModule(),
					[
						typeof(ChatService)
					],
					"core",
					"player_identity")
				.RegisterGameStateModule(
					"chat_lobby",
					() => new ServerChatLobbyModule(),
					[
						"chat_lobby"
					],
					"core",
					"player_identity",
					"chat_service")
				.RegisterGameStateModule(
					"match",
					() => new ServerMatchModule(),
					[
						"match"
					],
					"core");
		}
	}

	internal sealed class ServerCoreModule : IHawkNetServerModule
	{
		private readonly ConfigService _configService;
		private readonly EventDispatcher _eventDispatcher;
		private readonly UpdateLoop _updateLoop;
		private readonly IPayloadCodec _payloadCodec;
		private readonly GameStateManager _gameStateManager;
		private readonly MatchVariantRegistry _matchVariants;
		private readonly ILogger _logger;

		public ServerCoreModule(
			ConfigService configService,
			EventDispatcher eventDispatcher,
			UpdateLoop updateLoop,
			IPayloadCodec payloadCodec,
			GameStateManager gameStateManager,
			MatchVariantRegistry matchVariants,
			ILogger logger)
		{
			_configService = configService;
			_eventDispatcher = eventDispatcher;
			_updateLoop = updateLoop;
			_payloadCodec = payloadCodec;
			_gameStateManager = gameStateManager;
			_matchVariants = matchVariants;
			_logger = logger;
		}

		public void Configure(IHawkNetServerBuilder builder)
		{
			builder
				.AddService<IEventDispatcher>(_eventDispatcher)
				.AddService(_eventDispatcher)
				.AddService<IConfigService>(_configService)
				.AddService(_configService)
				.AddService(_updateLoop)
				.AddService<IPayloadCodec>(_payloadCodec)
				.AddService(_gameStateManager)
				.AddService(_matchVariants)
				.AddService<IServerTransportFactory>(new BuiltInServerTransportFactory())
				.AddService<ILogger>(_logger);
		}
	}

	internal sealed class ServerPlayerIdentityModule : IHawkNetServerModule
	{
		public void Configure(IHawkNetServerBuilder builder)
		{
			var playerIdentityService = new PlayerIdentityService(
				builder.Services.Get<IEventDispatcher>(),
				builder.Services.Get<IConfigService>().Get<PlayerIdentitySettings>(),
				builder.Services.Get<ILogger>());

			builder
				.AddService<IPlayerIdentityService>(playerIdentityService)
				.AddService(playerIdentityService);
		}
	}

	internal sealed class ServerDiagnosticsModule : IHawkNetServerModule
	{
		public void Configure(IHawkNetServerBuilder builder)
		{
			var diagnostics = new ServerDiagnostics(
				builder.Services.Get<IConfigService>().Get<DiagnosticsSettings>(),
				builder.Services.Get<ILogger>(),
				() => builder.Services.TryGet<ConnectionManager>(out var connectionManager) ? connectionManager.SessionCount : 0,
				() => builder.Services.TryGet<ConnectionManager>(out var connectionManager) ? connectionManager.ConnectedPlayerCount : 0,
				() => builder.Services.TryGet<ConnectionManager>(out var connectionManager) ? connectionManager.PacketCounters.GetSnapshot() : default,
				() => builder.Services.TryGet<ConnectionManager>(out var connectionManager) ? connectionManager.PacketCounters.GetSnapshotsByType() : Array.Empty<PacketTypeCounterSnapshot>(),
				() => builder.Services.Get<UpdateLoop>().Metrics,
				() => builder.Services.TryGet<ConnectionManager>(out var connectionManager) ? connectionManager.UdpDeliveryMetrics : UdpDeliveryMetricsSnapshot.Empty);

			builder.AddService(diagnostics);
		}
	}

	internal sealed class ServerChatServiceModule : IHawkNetServerModule
	{
		public void Configure(IHawkNetServerBuilder builder)
		{
			builder.Services.TryGet<ServerDiagnostics>(out var diagnostics);
			var chatService = new ChatService(
				builder.Services.Get<IEventDispatcher>(),
				builder.Services.Get<IConfigService>().Get<ChatSettings>(),
				builder.Services.Get<IPlayerIdentityService>(),
				builder.Services.Get<IPayloadCodec>(),
				builder.Services.Get<ILogger>(),
				diagnostics);

			builder.AddService(chatService);
		}
	}

	internal sealed class ServerChatLobbyModule : IHawkNetServerModule
	{
		public void Configure(IHawkNetServerBuilder builder)
		{
			builder.AddGameState("chat_lobby", context =>
				new ChatLobbyState(
					context.Events,
					context.GetConnectedPlayerIds,
					context.PlayerIdentityService ?? throw new InvalidOperationException("chat_lobby requires player_identity."),
					context.ChatService ?? throw new InvalidOperationException("chat_lobby requires chat_service."),
					context.Config.Get<ChatLobbySettings>(),
					builder.Services.Get<IPayloadCodec>(),
					context.Logger));
		}
	}

	internal sealed class ServerMatchModule : IHawkNetServerModule
	{
		public void Configure(IHawkNetServerBuilder builder)
		{
			builder.AddGameState("match", context =>
			{
				var payloadCodec = context.Services.Get<IPayloadCodec>();
				var matchSettings = context.Config.Get<MatchSettings>();
				var matchContext = new GameMatchContext(
					context.Events,
					context.Logger,
					context.Config,
					context.Services,
					context.ChatService,
					context.PlayerIdentityService,
					payloadCodec,
					context.GetConnectedPlayerIds,
					(toStateId, payload, token) => context.Events.DispatchAsync(
						new GameStateTransitionRequestedEvent(toStateId, payload),
						token));
				var match = context.Services.Get<MatchVariantRegistry>().Create(
					matchSettings.VariantId,
					matchContext);

				return new MatchState(
					context.Events,
					match,
					matchContext,
					matchSettings,
					payloadCodec,
					context.Logger);
			});
		}
	}
}
