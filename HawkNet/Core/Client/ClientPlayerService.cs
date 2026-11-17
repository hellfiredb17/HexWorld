/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

namespace HawkNet.Core.Client
{
	public readonly record struct ClientPlayerData(
		int PlayerId,
		string DisplayName);

	public sealed class ClientPlayerService : IService, IDisposable
	{
		//---- Variables
		//--------------
		private readonly object _lock = new();
		private readonly IEventDispatcher _eventDispatcher;
		private readonly List<IDisposable> _subscriptions = new();
		private ClientPlayerData? _currentPlayer;
		private bool _disposed;
		private ServiceState _state = ServiceState.NotInitialized;

		//---- Properties
		//---------------
		public ClientPlayerData? CurrentPlayer
		{
			get
			{
				lock(_lock)
				{
					return _currentPlayer;
				}
			}
		}

		public ServiceState State => _state;

		//---- Ctor
		//---------
		public ClientPlayerService(IEventDispatcher eventDispatcher)
		{
			_eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
			_subscriptions.Add(_eventDispatcher.Subscribe<ClientConnectedEvent>(OnConnected));
			_subscriptions.Add(_eventDispatcher.Subscribe<ClientReconnectedEvent>(OnReconnected));
		}

		//---- Public
		//-----------
		public async Task ChangeDisplayNameAsync(string displayName, CancellationToken token = default)
		{
			string normalizedName = NormalizeDisplayName(displayName);
			ClientPlayerData player;

			lock(_lock)
			{
				if(_currentPlayer is not ClientPlayerData currentPlayer)
				{
					throw new InvalidOperationException("Cannot change display name before the local player is known.");
				}

				player = currentPlayer with { DisplayName = normalizedName };
				_currentPlayer = player;
			}

			await _eventDispatcher.DispatchAsync(
				new ClientDisplayNameChangedEvent(player.PlayerId, player.DisplayName),
				token);
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
			_state = ServiceState.NotInitialized;
			_disposed = true;
		}

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

		//---- Private
		//------------
		private Task OnConnected(ClientConnectedEvent gameEvent, CancellationToken token)
		{
			SetPlayerIfNeeded(gameEvent.PlayerId);
			return Task.CompletedTask;
		}

		private Task OnReconnected(ClientReconnectedEvent gameEvent, CancellationToken token)
		{
			SetPlayerIfNeeded(gameEvent.PlayerId);
			return Task.CompletedTask;
		}

		private void SetPlayerIfNeeded(int playerId)
		{
			lock(_lock)
			{
				if(_currentPlayer is { } currentPlayer && currentPlayer.PlayerId == playerId)
				{
					return;
				}

				_currentPlayer = new ClientPlayerData(playerId, $"Player {playerId}");
			}
		}

		private static string NormalizeDisplayName(string displayName)
		{
			if(string.IsNullOrWhiteSpace(displayName))
			{
				throw new ArgumentException("Display name cannot be empty.", nameof(displayName));
			}

			return displayName.Trim();
		}
	}
}
