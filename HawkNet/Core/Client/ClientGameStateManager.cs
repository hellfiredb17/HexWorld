/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

namespace HawkNet.Core.Client
{
	public sealed class ClientGameStateManager : IService, IDisposable
	{
		private readonly Dictionary<string, Func<IClientGameState>> _factories = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<IDisposable> _subscriptions = new();
		private readonly IEventDispatcher _eventDispatcher;
		private ServiceState _state = ServiceState.NotInitialized;
		private IClientGameState? _activeState;
		private string? _activeStateId;
		private bool _disposed;

		public ClientGameStateManager(IEventDispatcher eventDispatcher)
		{
			_eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
		}

		public ServiceState State => _state;
		public IClientGameState? ActiveState => _activeState;
		public string? ActiveStateId => _activeStateId;

		public void RegisterState(string id, Func<IClientGameState> factory)
		{
			if(string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException("Client game state id cannot be empty.", nameof(id));
			}

			HawkNet.Core.Guard.NotNull(factory);

			if(!_factories.TryAdd(id.Trim(), factory))
			{
				throw new InvalidOperationException($"Client game state factory '{id}' is already registered.");
			}
		}

		public bool TryEnterState(string stateId, byte[] payload)
		{
			if(string.IsNullOrWhiteSpace(stateId))
			{
				return false;
			}

			if(!_factories.TryGetValue(stateId.Trim(), out var factory))
			{
				return false;
			}

			ExitAndDisposeActiveState();
			IClientGameState nextState = factory();
			nextState.Enter(payload ?? Array.Empty<byte>());
			_activeState = nextState;
			_activeStateId = stateId.Trim();
			return true;
		}

		public void Update(float deltaTime)
		{
			_activeState?.Update(deltaTime);
		}

		public Task CreateAsync(CancellationToken token = default)
		{
			_state = ServiceState.Initializing;
			return Task.CompletedTask;
		}

		public Task InitializeAsync(CancellationToken token = default)
		{
			_subscriptions.Add(_eventDispatcher.Subscribe<ClientConnectedEvent>(OnConnected));
			_subscriptions.Add(_eventDispatcher.Subscribe<ClientReconnectedEvent>(OnReconnected));
			_subscriptions.Add(_eventDispatcher.Subscribe<ClientGameStateChangeReceivedEvent>(OnGameStateChangeReceived));
			_state = ServiceState.Ready;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken token = default)
		{
			Dispose();
			return Task.CompletedTask;
		}

		public void RegisterServices()
		{
		}

		public void UnregisterServices()
		{
			Dispose();
		}

		public void Dispose()
		{
			if(_disposed)
			{
				return;
			}

			foreach(var subscription in _subscriptions)
			{
				subscription.Dispose();
			}

			_subscriptions.Clear();
			ExitAndDisposeActiveState();
			_state = ServiceState.NotInitialized;
			_disposed = true;
		}

		private Task OnConnected(ClientConnectedEvent gameEvent, CancellationToken token)
		{
			return EnterStateFromEventAsync(gameEvent.GameStateId, gameEvent.GameStatePayload, token);
		}

		private Task OnReconnected(ClientReconnectedEvent gameEvent, CancellationToken token)
		{
			return EnterStateFromEventAsync(gameEvent.GameStateId, gameEvent.GameStatePayload, token);
		}

		private Task OnGameStateChangeReceived(ClientGameStateChangeReceivedEvent gameEvent, CancellationToken token)
		{
			return EnterStateFromEventAsync(gameEvent.GameStateId, gameEvent.GameStatePayload, token);
		}

		private async Task EnterStateFromEventAsync(string gameStateId, byte[] payload, CancellationToken token)
		{
			try
			{
				if(TryEnterState(gameStateId, payload))
				{
					return;
				}

				await _eventDispatcher.DispatchAsync(
					new ClientStateEntryFailedEvent(
						gameStateId,
						$"Client game state '{gameStateId}' is not registered.",
					null),
					token);
			}
			catch(OperationCanceledException) when (token.IsCancellationRequested)
			{
				// Expected while the client is shutting down or changing connections.
			}
			catch(Exception ex)
			{
				await _eventDispatcher.DispatchAsync(
					new ClientStateEntryFailedEvent(
						gameStateId,
						$"Client game state '{gameStateId}' failed to enter.",
						ex),
					token);
			}
		}

		private void ExitAndDisposeActiveState()
		{
			if(_activeState is null)
			{
				_activeStateId = null;
				return;
			}

			_activeState.Exit();
			_activeState.Dispose();
			_activeState = null;
			_activeStateId = null;
		}
	}
}
