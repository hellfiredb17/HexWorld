/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	public class ChatService : IDisposable
	{
		//---- Variables
		//--------------
		private readonly object _historyLock = new();
		private readonly IEventDispatcher _eventDispatcher;
		private readonly IPlayerIdentityService _playerIdentityService;
		private readonly IPayloadCodec _payloadCodec;
		private readonly ILogger _logger;
		private readonly ServerDiagnostics? _diagnostics;
		private readonly List<IDisposable> _subscriptions = new();
		private readonly ChatEntry[] _history;
		private readonly int _maxMessageLength;
		private readonly int _maxMessageBytes;
		private readonly int _maxMessagesPerWindow;
		private readonly TimeSpan _messageRateWindow;
		private readonly ConcurrentDictionary<int, Queue<DateTime>> _messageTimestampsByPlayer = new();
		private int _historyStart;
		private int _historyCount;

		//---- Ctor
		//---------
		public ChatService(
			IEventDispatcher eventDispatcher,
			ChatSettings settings,
			IPlayerIdentityService? playerIdentityService = null,
			IPayloadCodec? payloadCodec = null,
			ILogger? logger = null,
			ServerDiagnostics? diagnostics = null)
		{
			_eventDispatcher = eventDispatcher;
			_playerIdentityService = playerIdentityService ?? new DefaultPlayerIdentityService();
			_payloadCodec = payloadCodec ?? new JsonPayloadCodec();
			_logger = logger ?? NullLogger.Instance;
			_diagnostics = diagnostics;
			int maxHistoryMessages = Math.Max(1, settings.MaxHistoryMessages);
			_maxMessageLength = Math.Max(1, settings.MaxMessageLength);
			_maxMessageBytes = Math.Max(1, settings.MaxMessageBytes);
			_maxMessagesPerWindow = Math.Max(1, settings.MaxMessagesPerWindow);
			_messageRateWindow = TimeSpan.FromSeconds(Math.Max(1, settings.MessageRateWindowSeconds));
			_history = new ChatEntry[maxHistoryMessages];
			_subscriptions.Add(_eventDispatcher.Subscribe<ChatMessageReceivedEvent>(OnChatMessageReceived));
			_subscriptions.Add(_eventDispatcher.Subscribe<PlayerConnectedEvent>(OnPlayerConnected));
			_subscriptions.Add(_eventDispatcher.Subscribe<PlayerReconnectedEvent>(OnPlayerReconnected));
			_subscriptions.Add(_eventDispatcher.Subscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected));
			_subscriptions.Add(_eventDispatcher.Subscribe<PlayerDisplayNameChangedEvent>(OnPlayerDisplayNameChanged));
		}

		//---- Public
		//-----------
		public IReadOnlyList<ChatEntry> GetHistory()
		{
			lock(_historyLock)
			{
				var entries = new List<ChatEntry>(_historyCount);
				for(int i = 0; i < _historyCount; i++)
				{
					entries.Add(_history[(_historyStart + i) % _history.Length]);
				}

				return entries;
			}
		}

		public async Task SendHistoryToPlayerAsync(int playerId, CancellationToken token = default)
		{
			byte[] payload = _payloadCodec.Encode(GetHistory());
			await _eventDispatcher.DispatchAsync(
				new SendPlayerMessageEvent(playerId, PacketType.ChatMessage, payload),
				token);
		}

		public Task SendSystemMessageAsync(
			string message,
			string displayName = "Server",
			CancellationToken token = default)
		{
			string normalizedMessage = (message ?? string.Empty).Trim();
			if(string.IsNullOrWhiteSpace(normalizedMessage))
			{
				throw new ArgumentException("Chat message cannot be empty.", nameof(message));
			}

			if(normalizedMessage.Length > _maxMessageLength)
			{
				normalizedMessage = normalizedMessage[.._maxMessageLength];
			}

			byte[] messageBytes = Encoding.UTF8.GetBytes(normalizedMessage);
			if(messageBytes.Length > _maxMessageBytes)
			{
				throw new InvalidOperationException($"Chat message exceeds {_maxMessageBytes} bytes.");
			}

			var entry = new ChatEntry(
				0,
				string.IsNullOrWhiteSpace(displayName) ? "Server" : displayName.Trim(),
				DateTime.UtcNow,
				normalizedMessage);

			return LogAndBroadcastAsync(entry, token);
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
		private async Task OnChatMessageReceived(ChatMessageReceivedEvent gameEvent, CancellationToken token)
		{
			long started = Stopwatch.GetTimestamp();
			if(gameEvent.Payload.Length > _maxMessageBytes)
			{
				_logger.Warning(
					$"Rejected chat message from Player {gameEvent.PlayerId}: payload size {gameEvent.Payload.Length} exceeds {_maxMessageBytes} bytes.",
					nameof(ChatService));
				_diagnostics?.RecordChatRejected(
					ChatDiagnosticsRejectionReason.ByteLimit,
					gameEvent.Payload.Length,
					HawkNet.Core.TimeCompatibility.GetElapsedTime(started));
				return;
			}

			string message = Encoding.UTF8.GetString(gameEvent.Payload).Trim();
			if(string.IsNullOrWhiteSpace(message))
			{
				_diagnostics?.RecordChatRejected(
					ChatDiagnosticsRejectionReason.Empty,
					gameEvent.Payload.Length,
					HawkNet.Core.TimeCompatibility.GetElapsedTime(started));
				return;
			}

			if(IsRateLimited(gameEvent.PlayerId))
			{
				_logger.Warning(
					$"Rejected chat message from Player {gameEvent.PlayerId}: rate limit {_maxMessagesPerWindow} messages per {_messageRateWindow.TotalSeconds:0}s exceeded.",
					nameof(ChatService));
				_diagnostics?.RecordChatRejected(
					ChatDiagnosticsRejectionReason.RateLimit,
					gameEvent.Payload.Length,
					HawkNet.Core.TimeCompatibility.GetElapsedTime(started));
				return;
			}

			if(message.Length > _maxMessageLength)
			{
				message = message[.._maxMessageLength];
			}

			var entry = new ChatEntry(
				gameEvent.PlayerId,
				_playerIdentityService.GetDisplayName(gameEvent.PlayerId),
				DateTime.UtcNow,
				message);

			await LogAndBroadcastAsync(entry, token);
			_diagnostics?.RecordChatAccepted(gameEvent.Payload.Length, HawkNet.Core.TimeCompatibility.GetElapsedTime(started));
		}

		private Task OnPlayerConnected(PlayerConnectedEvent gameEvent, CancellationToken token)
		{
			return SendHistoryToPlayerAsync(gameEvent.PlayerId, token);
		}

		private Task OnPlayerReconnected(PlayerReconnectedEvent gameEvent, CancellationToken token)
		{
			return SendHistoryToPlayerAsync(gameEvent.PlayerId, token);
		}

		private Task OnPlayerDisconnected(PlayerDisconnectedEvent gameEvent, CancellationToken token)
		{
			_messageTimestampsByPlayer.TryRemove(gameEvent.PlayerId, out _);
			return Task.CompletedTask;
		}

		private async Task OnPlayerDisplayNameChanged(PlayerDisplayNameChangedEvent gameEvent, CancellationToken token)
		{
			if(!ReplaceHistoryDisplayName(gameEvent.Identity.PlayerId, gameEvent.Identity.DisplayName))
			{
				return;
			}

			byte[] payload = _payloadCodec.Encode(GetHistory());
			await _eventDispatcher.DispatchAsync(
				new BroadcastMessageEvent(PacketType.ChatMessage, payload),
				token);
		}

		private void AddHistory(ChatEntry entry)
		{
			lock(_historyLock)
			{
				if(_historyCount < _history.Length)
				{
					_history[(_historyStart + _historyCount) % _history.Length] = entry;
					_historyCount++;
					return;
				}

				_history[_historyStart] = entry;
				_historyStart = (_historyStart + 1) % _history.Length;
			}
		}

		private bool ReplaceHistoryDisplayName(int playerId, string displayName)
		{
			bool changed = false;
			lock(_historyLock)
			{
				for(int i = 0; i < _historyCount; i++)
				{
					int index = (_historyStart + i) % _history.Length;
					ChatEntry entry = _history[index];
					if(entry.PlayerId != playerId || string.Equals(entry.DisplayName, displayName, StringComparison.Ordinal))
					{
						continue;
					}

					_history[index] = entry with { DisplayName = displayName };
					changed = true;
				}
			}

			return changed;
		}

		private async Task LogAndBroadcastAsync(ChatEntry entry, CancellationToken token)
		{
			AddHistory(entry);
			_logger.DebugLog($"Chat message logged for Player {entry.PlayerId}.", nameof(ChatService));
			await _eventDispatcher.DispatchAsync(new ChatMessageLoggedEvent(entry), token);
			byte[] payload = _payloadCodec.Encode(entry);
			await _eventDispatcher.DispatchAsync(
				new BroadcastMessageEvent(PacketType.ChatMessage, payload),
				token);
		}

		private bool IsRateLimited(int playerId)
		{
			var now = DateTime.UtcNow;
			var windowStart = now - _messageRateWindow;
			var timestamps = _messageTimestampsByPlayer.GetOrAdd(playerId, _ => new Queue<DateTime>());
			lock(timestamps)
			{
				while(timestamps.Count > 0 && timestamps.Peek() <= windowStart)
				{
					timestamps.Dequeue();
				}

				if(timestamps.Count >= _maxMessagesPerWindow)
				{
					return true;
				}

				timestamps.Enqueue(now);
				return false;
			}
		}

		private sealed class DefaultPlayerIdentityService : IPlayerIdentityService
		{
			public PlayerIdentity GetOrCreateIdentity(int playerId)
			{
				var now = DateTime.UtcNow;
				return new PlayerIdentity(playerId, GetDisplayName(playerId), now, now);
			}

			public string GetDisplayName(int playerId)
			{
				return $"Player {playerId}";
			}

			public bool TrySetDisplayName(int playerId, string displayName, out PlayerIdentity identity)
			{
				identity = default;
				return false;
			}

			public bool TryGetIdentity(int playerId, out PlayerIdentity identity)
			{
				identity = GetOrCreateIdentity(playerId);
				return true;
			}
		}
	}
}
