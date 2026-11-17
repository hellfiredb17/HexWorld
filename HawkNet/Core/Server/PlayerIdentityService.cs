/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using System.Collections.Concurrent;
using System.Text;
using HawkNet.Core;

namespace HawkNet.Core.Server
{
	public readonly record struct PlayerIdentity(
		int PlayerId,
		string DisplayName,
		DateTime CreatedAtUtc,
		DateTime UpdatedAtUtc);

	public interface IPlayerIdentityService
	{
		PlayerIdentity GetOrCreateIdentity(int playerId);
		string GetDisplayName(int playerId);
		bool TrySetDisplayName(int playerId, string displayName, out PlayerIdentity identity);
		bool TryGetIdentity(int playerId, out PlayerIdentity identity);
	}

	public class PlayerIdentityService : IPlayerIdentityService, IDisposable
	{
		//---- Variables
		//--------------
		private readonly ConcurrentDictionary<int, PlayerIdentity> _identities = new();
		private readonly IEventDispatcher _eventDispatcher;
		private readonly List<IDisposable> _subscriptions = new();
		private readonly PlayerIdentitySettings _settings;
		private readonly ILogger _logger;

		//---- Ctor
		//---------
		public PlayerIdentityService(
			IEventDispatcher eventDispatcher,
			PlayerIdentitySettings settings,
			ILogger? logger = null)
		{
			_eventDispatcher = eventDispatcher;
			_settings = settings;
			_logger = logger ?? NullLogger.Instance;
			_subscriptions.Add(_eventDispatcher.Subscribe<PlayerConnectedEvent>(OnPlayerConnected));
			_subscriptions.Add(_eventDispatcher.Subscribe<PlayerReconnectedEvent>(OnPlayerReconnected));
			_subscriptions.Add(_eventDispatcher.Subscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected));
			_subscriptions.Add(_eventDispatcher.Subscribe<DisplayNameUpdateReceivedEvent>(OnDisplayNameUpdateReceived));
		}

		//---- Public
		//-----------
		public PlayerIdentity GetOrCreateIdentity(int playerId)
		{
			return _identities.GetOrAdd(playerId, CreateDefaultIdentity);
		}

		public string GetDisplayName(int playerId)
		{
			return GetOrCreateIdentity(playerId).DisplayName;
		}

		public bool TrySetDisplayName(int playerId, string displayName, out PlayerIdentity identity)
		{
			if(!_identities.TryGetValue(playerId, out var currentIdentity))
			{
				identity = default;
				return false;
			}

			string normalizedDisplayName = NormalizeDisplayName(displayName);
			if(string.IsNullOrWhiteSpace(normalizedDisplayName))
			{
				identity = default;
				return false;
			}

			var now = DateTime.UtcNow;
			identity = currentIdentity with
			{
				DisplayName = normalizedDisplayName,
				UpdatedAtUtc = now
			};

			if(!_identities.TryUpdate(playerId, identity, currentIdentity))
			{
				identity = default;
				return false;
			}

			_logger.Log($"Player {playerId} display name set to '{identity.DisplayName}'.", nameof(PlayerIdentityService));
			_ = _eventDispatcher.DispatchAsync(new PlayerDisplayNameChangedEvent(identity));
			return true;
		}

		public bool TryGetIdentity(int playerId, out PlayerIdentity identity)
		{
			return _identities.TryGetValue(playerId, out identity);
		}

		public void Dispose()
		{
			foreach(var subscription in _subscriptions)
			{
				subscription.Dispose();
			}

			_subscriptions.Clear();
		}

		//---- Private
		//------------
		private Task OnPlayerConnected(PlayerConnectedEvent gameEvent, CancellationToken token)
		{
			GetOrCreateIdentity(gameEvent.PlayerId);
			return Task.CompletedTask;
		}

		private Task OnPlayerReconnected(PlayerReconnectedEvent gameEvent, CancellationToken token)
		{
			GetOrCreateIdentity(gameEvent.PlayerId);
			return Task.CompletedTask;
		}

		private Task OnPlayerDisconnected(PlayerDisconnectedEvent gameEvent, CancellationToken token)
		{
			_identities.TryRemove(gameEvent.PlayerId, out _);
			return Task.CompletedTask;
		}

		private Task OnDisplayNameUpdateReceived(DisplayNameUpdateReceivedEvent gameEvent, CancellationToken token)
		{
			string displayName = Encoding.UTF8.GetString(gameEvent.Payload);
			if(!TrySetDisplayName(gameEvent.PlayerId, displayName, out _))
			{
				_logger.Warning($"Rejected display name update for Player {gameEvent.PlayerId}.", nameof(PlayerIdentityService));
			}

			return Task.CompletedTask;
		}

		private PlayerIdentity CreateDefaultIdentity(int playerId)
		{
			var now = DateTime.UtcNow;
			return new PlayerIdentity(playerId, BuildDefaultDisplayName(playerId), now, now);
		}

		private string BuildDefaultDisplayName(int playerId)
		{
			string format = string.IsNullOrWhiteSpace(_settings.DefaultDisplayNameFormat)
				? "Player {0}"
				: _settings.DefaultDisplayNameFormat;
			return NormalizeDisplayName(string.Format(format, playerId));
		}

		private string NormalizeDisplayName(string displayName)
		{
			string normalizedDisplayName = displayName.Trim();
			int maxLength = Math.Max(1, _settings.MaxDisplayNameLength);
			if(normalizedDisplayName.Length > maxLength)
			{
				normalizedDisplayName = normalizedDisplayName[..maxLength];
			}

			return normalizedDisplayName;
		}
	}
}
