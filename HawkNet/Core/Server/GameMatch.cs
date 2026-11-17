using HawkNet.Core;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	public interface IGameMatchContext
	{
		IEventDispatcher Events { get; }
		ILogger Logger { get; }
		IConfigService Config { get; }
		IServiceRegistry Services { get; }
		ChatService? ChatService { get; }
		IPlayerIdentityService? PlayerIdentityService { get; }
		IPayloadCodec PayloadCodec { get; }
		T GetRequired<T>() where T : class => Services.Get<T>();
		bool TryGet<T>(out T service) where T : class => Services.TryGet(out service);

		IEnumerable<int> GetConnectedPlayerIds();
		Task RequestTransitionAsync(string toStateId, byte[] payload, CancellationToken token = default);
		Task CompleteCurrentPhaseAsync(byte[]? payload = null, CancellationToken token = default);
	}

	public interface IGameMatch : IDisposable
	{
		IReadOnlyCollection<MatchPlayer> Players { get; }

		Task InitializeAsync(IGameMatchContext context, MatchPayload payload, CancellationToken token = default);
		Task SubscribeAsync(CancellationToken token = default);
		Task UnsubscribeAsync(CancellationToken token = default);
		Task PreMatchAsync(CancellationToken token = default);
		Task BeginMatchAsync(CancellationToken token = default);
		Task EndMatchAsync(CancellationToken token = default);
		Task PostMatchAsync(CancellationToken token = default);
		Task EnterPhaseAsync(MatchPhaseSettings phase, CancellationToken token = default);
		Task UpdateAsync(float deltaTime, CancellationToken token = default);
	}

	public sealed class MatchVariantRegistry
	{
		private readonly Dictionary<string, Func<IGameMatchContext, IGameMatch>> _factories = new(StringComparer.OrdinalIgnoreCase);

		public void Register(string id, Func<IGameMatchContext, IGameMatch> factory)
		{
			if(string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException("Match variant id cannot be empty.", nameof(id));
			}

			HawkNet.Core.Guard.NotNull(factory);
			string normalizedId = id.Trim();
			if(!_factories.TryAdd(normalizedId, factory))
			{
				throw new InvalidOperationException($"Match variant '{normalizedId}' is already registered.");
			}
		}

		public IGameMatch Create(string id, IGameMatchContext context)
		{
			if(string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException("Match variant id cannot be empty.", nameof(id));
			}

			if(!_factories.TryGetValue(id.Trim(), out var factory))
			{
				throw new InvalidOperationException($"Match variant '{id}' is not registered.");
			}

			return factory(context);
		}
	}

	public class GameMatchBase : IGameMatch
	{
		private readonly List<IDisposable> _subscriptions = new();
		private readonly Dictionary<int, MatchPlayer> _players = new();
		private bool _disposed;

		protected IGameMatchContext Context { get; private set; } = null!;

		public IReadOnlyCollection<MatchPlayer> Players => _players.Values
			.OrderBy(player => player.PlayerId)
			.ToArray();

		public virtual Task InitializeAsync(IGameMatchContext context, MatchPayload payload, CancellationToken token = default)
		{
			Context = context ?? throw new ArgumentNullException(nameof(context));
			_players.Clear();
			if(payload is not null)
			{
				foreach(var player in payload.Players)
				{
					_players[player.PlayerId] = player;
				}
			}

			foreach(int playerId in context.GetConnectedPlayerIds())
			{
				AddOrReconnectPlayer(playerId);
			}

			return Task.CompletedTask;
		}

		public virtual Task SubscribeAsync(CancellationToken token = default)
		{
			_subscriptions.Add(Context.Events.Subscribe<PlayerConnectedEvent>(OnPlayerConnected));
			_subscriptions.Add(Context.Events.Subscribe<PlayerReconnectedEvent>(OnPlayerReconnected));
			_subscriptions.Add(Context.Events.Subscribe<PlayerConnectionLostEvent>(OnPlayerConnectionLost));
			_subscriptions.Add(Context.Events.Subscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected));
			_subscriptions.Add(Context.Events.Subscribe<PlayerDisplayNameChangedEvent>(OnPlayerDisplayNameChanged));
			_subscriptions.Add(Context.Events.Subscribe<ChatMessageLoggedEvent>(OnChatMessageLogged));
			_subscriptions.Add(Context.Events.Subscribe<MatchCommandReceivedEvent>(OnMatchCommandReceived));
			return Task.CompletedTask;
		}

		public virtual Task UnsubscribeAsync(CancellationToken token = default)
		{
			foreach(var subscription in _subscriptions.ToArray())
			{
				subscription.Dispose();
			}

			_subscriptions.Clear();
			return Task.CompletedTask;
		}

		public virtual Task PreMatchAsync(CancellationToken token = default)
		{
			return Task.CompletedTask;
		}

		public virtual Task BeginMatchAsync(CancellationToken token = default)
		{
			return Task.CompletedTask;
		}

		public virtual Task EndMatchAsync(CancellationToken token = default)
		{
			return Task.CompletedTask;
		}

		public virtual Task PostMatchAsync(CancellationToken token = default)
		{
			return Task.CompletedTask;
		}

		public virtual Task EnterPhaseAsync(MatchPhaseSettings phase, CancellationToken token = default)
		{
			return Task.CompletedTask;
		}

		public virtual Task UpdateAsync(float deltaTime, CancellationToken token = default)
		{
			return Task.CompletedTask;
		}

		protected virtual Task OnPlayerJoinedAsync(MatchPlayer player, CancellationToken token)
		{
			return Context.Events.DispatchAsync(new MatchPlayerJoinedEvent(player), token);
		}

		protected virtual Task OnPlayerDisconnectedAsync(MatchPlayer player, ConnectionCloseReason reason, CancellationToken token)
		{
			return Context.Events.DispatchAsync(new MatchPlayerDisconnectedEvent(player, reason), token);
		}

		protected virtual Task OnChatMessageAsync(ChatEntry entry, CancellationToken token)
		{
			return Task.CompletedTask;
		}

		protected virtual Task OnMatchCommandAsync(int playerId, string commandId, byte[] payload, CancellationToken token)
		{
			return Task.CompletedTask;
		}

		protected IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
			where TEvent : IGameEvent
		{
			var subscription = Context.Events.Subscribe(handler);
			_subscriptions.Add(subscription);
			return subscription;
		}

		private Task OnPlayerConnected(PlayerConnectedEvent gameEvent, CancellationToken token)
		{
			var player = AddOrReconnectPlayer(gameEvent.PlayerId);
			return OnPlayerJoinedAsync(player, token);
		}

		private Task OnPlayerReconnected(PlayerReconnectedEvent gameEvent, CancellationToken token)
		{
			var player = AddOrReconnectPlayer(gameEvent.PlayerId);
			return OnPlayerJoinedAsync(player, token);
		}

		private Task OnPlayerConnectionLost(PlayerConnectionLostEvent gameEvent, CancellationToken token)
		{
			var player = MarkPlayerDisconnected(gameEvent.PlayerId);
			return OnPlayerDisconnectedAsync(player, gameEvent.Reason, token);
		}

		private Task OnPlayerDisconnected(PlayerDisconnectedEvent gameEvent, CancellationToken token)
		{
			var player = MarkPlayerDisconnected(gameEvent.PlayerId);
			return OnPlayerDisconnectedAsync(player, gameEvent.Reason, token);
		}

		private Task OnPlayerDisplayNameChanged(PlayerDisplayNameChangedEvent gameEvent, CancellationToken token)
		{
			if(_players.TryGetValue(gameEvent.Identity.PlayerId, out var player))
			{
				_players[gameEvent.Identity.PlayerId] = player with
				{
					DisplayName = gameEvent.Identity.DisplayName
				};
			}

			return Task.CompletedTask;
		}

		private Task OnChatMessageLogged(ChatMessageLoggedEvent gameEvent, CancellationToken token)
		{
			return OnChatMessageAsync(gameEvent.Entry, token);
		}

		private Task OnMatchCommandReceived(MatchCommandReceivedEvent gameEvent, CancellationToken token)
		{
			return OnMatchCommandAsync(gameEvent.PlayerId, gameEvent.CommandId, gameEvent.Payload, token);
		}

		private MatchPlayer AddOrReconnectPlayer(int playerId)
		{
			if(_players.TryGetValue(playerId, out var existingPlayer))
			{
				var reconnectedPlayer = existingPlayer with
				{
					DisplayName = string.IsNullOrWhiteSpace(existingPlayer.DisplayName)
						? GetDisplayName(playerId)
						: existingPlayer.DisplayName,
					IsConnected = true
				};
				_players[playerId] = reconnectedPlayer;
				return reconnectedPlayer;
			}

			var player = new MatchPlayer
			{
				PlayerId = playerId,
				DisplayName = GetDisplayName(playerId),
				IsConnected = true,
				ReadyPayload = []
			};
			_players[playerId] = player;
			return player;
		}

		private MatchPlayer MarkPlayerDisconnected(int playerId)
		{
			if(_players.TryGetValue(playerId, out var existingPlayer))
			{
				var disconnectedPlayer = existingPlayer with
				{
					IsConnected = false
				};
				_players[playerId] = disconnectedPlayer;
				return disconnectedPlayer;
			}

			var player = new MatchPlayer
			{
				PlayerId = playerId,
				DisplayName = GetDisplayName(playerId),
				IsConnected = false,
				ReadyPayload = []
			};
			_players[playerId] = player;
			return player;
		}

		private string GetDisplayName(int playerId) =>
			Context.PlayerIdentityService?.GetDisplayName(playerId) ?? $"Player {playerId}";

		public virtual void Dispose()
		{
			if(_disposed)
			{
				return;
			}

			_disposed = true;
			foreach(var subscription in _subscriptions.ToArray())
			{
				subscription.Dispose();
			}

			_subscriptions.Clear();
			GC.SuppressFinalize(this);
		}
	}

	internal sealed class GameMatchContext : IGameMatchContext
	{
		private readonly Func<IEnumerable<int>> _getConnectedPlayerIds;
		private readonly Func<string, byte[], CancellationToken, Task> _requestTransition;
		private Func<byte[]?, CancellationToken, Task> _completeCurrentPhase;

		public GameMatchContext(
			IEventDispatcher events,
			ILogger logger,
			IConfigService config,
			IServiceRegistry services,
			ChatService? chatService,
			IPlayerIdentityService? playerIdentityService,
			IPayloadCodec payloadCodec,
			Func<IEnumerable<int>> getConnectedPlayerIds,
			Func<string, byte[], CancellationToken, Task> requestTransition)
		{
			Events = events;
			Logger = logger;
			Config = config;
			Services = services;
			ChatService = chatService;
			PlayerIdentityService = playerIdentityService;
			PayloadCodec = payloadCodec;
			_getConnectedPlayerIds = getConnectedPlayerIds;
			_requestTransition = requestTransition;
			_completeCurrentPhase = (_, _) => Task.CompletedTask;
		}

		public IEventDispatcher Events { get; }
		public ILogger Logger { get; }
		public IConfigService Config { get; }
		public IServiceRegistry Services { get; }
		public ChatService? ChatService { get; }
		public IPlayerIdentityService? PlayerIdentityService { get; }
		public IPayloadCodec PayloadCodec { get; }
		public T GetRequired<T>() where T : class => Services.Get<T>();
		public bool TryGet<T>(out T service) where T : class => Services.TryGet(out service);

		public IEnumerable<int> GetConnectedPlayerIds()
		{
			return _getConnectedPlayerIds();
		}

		public Task RequestTransitionAsync(string toStateId, byte[] payload, CancellationToken token = default)
		{
			return _requestTransition(toStateId, payload, token);
		}

		public Task CompleteCurrentPhaseAsync(byte[]? payload = null, CancellationToken token = default)
		{
			return _completeCurrentPhase(payload, token);
		}

		internal void SetCompleteCurrentPhaseHandler(Func<byte[]?, CancellationToken, Task> completeCurrentPhase)
		{
			_completeCurrentPhase = completeCurrentPhase ?? throw new ArgumentNullException(nameof(completeCurrentPhase));
		}
	}
}
