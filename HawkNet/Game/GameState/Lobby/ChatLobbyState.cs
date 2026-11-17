/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;

namespace HawkNet.Game.GameState.Lobby
{
	/// <summary>
	/// Minimal lobby state that tracks connected players while chat is handled by ChatService.
	/// </summary>
	public class ChatLobbyState : Core.Server.GameState, IGameStatePayloadProvider
	{
		//---- Variables
		//--------------
		private readonly HashSet<int> _connectedPlayers = new();
		private readonly Dictionary<int, ChatLobbyReadyPlayer> _readyPlayers = new();
		private readonly Func<IEnumerable<int>> _getConnectedPlayerIds;
		private readonly IPlayerIdentityService _playerIdentityService;
		private readonly ChatService? _chatService;
		private readonly ChatLobbySettings _settings;
		private readonly IPayloadCodec _payloadCodec;
		private readonly ILogger _logger;
		private float _countdownRemainingSeconds;
		private DateTime _countdownStartedAtUtc;
		private DateTime _countdownEndsAtUtc;
		private bool _isCountdownActive;
		private bool _transitionRequested;
		private bool _isActive;

		//---- Properties
		//---------------
		public IReadOnlyCollection<int> ConnectedPlayers => _connectedPlayers;
		public bool IsActive => _isActive;
		public float LastDeltaTime { get; private set; }
		public int UpdateCount { get; private set; }
		public ChatEntry? LastChatEntry { get; private set; }
		public int ChatMessageCount { get; private set; }
		public IReadOnlyCollection<ChatLobbyReadyPlayer> ReadyPlayers => _readyPlayers.Values.ToArray();
		public bool IsCountdownActive => _isCountdownActive;
		public float CountdownRemainingSeconds => _countdownRemainingSeconds;

		//---- Ctor
		//---------
		public ChatLobbyState(
			IEventDispatcher eventDispatcher,
			Func<IEnumerable<int>>? getConnectedPlayerIds = null,
			IPlayerIdentityService? playerIdentityService = null,
			ChatService? chatService = null,
			ChatLobbySettings? settings = null,
			IPayloadCodec? payloadCodec = null,
			ILogger? logger = null)
			: base(eventDispatcher)
		{
			_getConnectedPlayerIds = getConnectedPlayerIds ?? Enumerable.Empty<int>;
			_playerIdentityService = playerIdentityService ?? new DefaultLobbyPlayerIdentityService();
			_chatService = chatService;
			_settings = settings ?? new ChatLobbySettings();
			_payloadCodec = payloadCodec ?? new JsonPayloadCodec();
			_logger = logger ?? NullLogger.Instance;
		}

		//---- Public
		//-----------
		public override void Enter(byte[] transitionPayload)
		{
			_isActive = true;
			foreach(int playerId in _getConnectedPlayerIds())
			{
				_connectedPlayers.Add(playerId);
			}

			Subscribe<PlayerConnectedEvent>(OnPlayerConnected);
			Subscribe<PlayerReconnectedEvent>(OnPlayerReconnected);
			Subscribe<PlayerConnectionLostEvent>(OnPlayerConnectionLost);
			Subscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected);
			Subscribe<PlayerDisplayNameChangedEvent>(OnPlayerDisplayNameChanged);
			Subscribe<ChatMessageLoggedEvent>(OnChatMessageLogged);
			Subscribe<ChatLobbyReadyRequestReceivedEvent>(OnReadyRequestReceived);
			_logger.Log("Entered.", nameof(ChatLobbyState));
		}

		public override void Update(float deltaTime)
		{
			LastDeltaTime = deltaTime;
			UpdateCount++;
			UpdateCountdown(deltaTime);
		}

		public override void Exit()
		{
			_isActive = false;
			UnsubscribeAll();
			_connectedPlayers.Clear();
			_readyPlayers.Clear();
			_isCountdownActive = false;
			_transitionRequested = false;
			_countdownRemainingSeconds = 0f;
			_countdownStartedAtUtc = default;
			_countdownEndsAtUtc = default;
			LastChatEntry = null;
			ChatMessageCount = 0;
			_logger.Log("Exited.", nameof(ChatLobbyState));
		}

		public byte[] CreatePayloadForPlayer(int playerId, IPayloadCodec codec)
		{
			HawkNet.Core.Guard.NotNull(codec);

			var payload = new ChatLobbyPayload
			{
				Players = CreatePlayerStateMessage(playerId).Players,
				ChatHistory = _chatService?.GetHistory() ?? Array.Empty<ChatEntry>(),
				ReadyState = CreateReadyStateMessage(playerId),
				Countdown = _isCountdownActive
					? new ChatLobbyCountdownResponse
					{
						Status = ChatLobbyCountdownStatus.Started,
						RemainingSeconds = GetCountdownRemainingSeconds(DateTime.UtcNow),
						StartedAtUtc = _countdownStartedAtUtc,
						EndsAtUtc = _countdownEndsAtUtc
					}
					: null
			};

			return codec.Encode(payload);
		}

		//---- Private
		//------------
		private Task OnPlayerConnected(PlayerConnectedEvent gameEvent, CancellationToken token)
		{
			_connectedPlayers.Add(gameEvent.PlayerId);
			_ = BroadcastPlayerStateAsync(token);
			_ = BroadcastReadyStateAsync(token);
			EvaluateReadyThreshold(token);
			return Task.CompletedTask;
		}

		private Task OnPlayerReconnected(PlayerReconnectedEvent gameEvent, CancellationToken token)
		{
			_connectedPlayers.Add(gameEvent.PlayerId);
			_ = BroadcastPlayerStateAsync(token);
			_ = BroadcastReadyStateAsync(token);
			EvaluateReadyThreshold(token);
			return Task.CompletedTask;
		}

		private Task OnPlayerConnectionLost(PlayerConnectionLostEvent gameEvent, CancellationToken token)
		{
			_connectedPlayers.Remove(gameEvent.PlayerId);
			_readyPlayers.Remove(gameEvent.PlayerId);
			_ = BroadcastPlayerStateAsync(token);
			_ = BroadcastReadyStateAsync(token);
			EvaluateReadyThreshold(token);
			return Task.CompletedTask;
		}

		private Task OnPlayerDisconnected(PlayerDisconnectedEvent gameEvent, CancellationToken token)
		{
			_connectedPlayers.Remove(gameEvent.PlayerId);
			_readyPlayers.Remove(gameEvent.PlayerId);
			_ = BroadcastPlayerStateAsync(token);
			_ = BroadcastReadyStateAsync(token);
			EvaluateReadyThreshold(token);
			return Task.CompletedTask;
		}

		private Task OnPlayerDisplayNameChanged(PlayerDisplayNameChangedEvent gameEvent, CancellationToken token)
		{
			if(_readyPlayers.TryGetValue(gameEvent.Identity.PlayerId, out var readyPlayer))
			{
				_readyPlayers[gameEvent.Identity.PlayerId] = readyPlayer with
				{
					DisplayName = gameEvent.Identity.DisplayName
				};
			}

			_ = BroadcastPlayerStateAsync(token);
			_ = BroadcastReadyStateAsync(token);
			return Task.CompletedTask;
		}

		private Task OnChatMessageLogged(ChatMessageLoggedEvent gameEvent, CancellationToken token)
		{
			LastChatEntry = gameEvent.Entry;
			ChatMessageCount++;
			return Task.CompletedTask;
		}

		private async Task OnReadyRequestReceived(ChatLobbyReadyRequestReceivedEvent gameEvent, CancellationToken token)
		{
			if(!_connectedPlayers.Contains(gameEvent.PlayerId))
			{
				foreach(int playerId in _getConnectedPlayerIds())
				{
					_connectedPlayers.Add(playerId);
				}

				if(!_connectedPlayers.Contains(gameEvent.PlayerId))
				{
					return;
				}
			}

			ChatLobbyReadyRequest request;
			try
			{
				request = _payloadCodec.Decode<ChatLobbyReadyRequest>(gameEvent.Payload);
			}
			catch(Exception ex)
			{
				_logger.Warning($"Invalid ready request from player {gameEvent.PlayerId}: {ex.Message}", nameof(ChatLobbyState));
				return;
			}

			if(request.IsReady)
			{
				if(request.Payload.Length > Math.Max(0, _settings.MaxReadyPayloadBytes))
				{
					_logger.Warning(
						$"Ready request from player {gameEvent.PlayerId} exceeded MaxReadyPayloadBytes ({request.Payload.Length}/{_settings.MaxReadyPayloadBytes}).",
						nameof(ChatLobbyState));
					return;
				}

				_readyPlayers[gameEvent.PlayerId] = new ChatLobbyReadyPlayer
				{
					PlayerId = gameEvent.PlayerId,
					DisplayName = _playerIdentityService.GetDisplayName(gameEvent.PlayerId),
					IsReady = true,
					ReadyPayload = request.Payload.ToArray()
				};
			}
			else
			{
				_readyPlayers.Remove(gameEvent.PlayerId);
			}

			await BroadcastReadyStateAsync(token);
			EvaluateReadyThreshold(token);
		}

		private void UpdateCountdown(float deltaTime)
		{
			if(!_isCountdownActive || _transitionRequested)
			{
				return;
			}

			_countdownRemainingSeconds = Math.Max(0f, _countdownRemainingSeconds - Math.Max(0f, deltaTime));
			if(_countdownRemainingSeconds > 0f)
			{
				return;
			}

			_isCountdownActive = false;
			_transitionRequested = true;
			_ = BroadcastCountdownAsync(ChatLobbyCountdownStatus.Completed, CancellationToken.None);
			string readyTransitionStateId = GetReadyTransitionStateId();
			if(!string.IsNullOrWhiteSpace(readyTransitionStateId))
			{
				_logger.Log($"Countdown completed. Requesting transition to '{readyTransitionStateId}'.", nameof(ChatLobbyState));
				Task transitionTask = RequestTransitionAsync(
					readyTransitionStateId,
					CreateReadyTransitionPayload(),
					CancellationToken.None);
				_ = transitionTask.ContinueWith(
					task =>
					{
						if(task.IsFaulted && task.Exception is not null)
						{
							_logger.Exception(task.Exception, $"Countdown transition to '{readyTransitionStateId}' failed.", nameof(ChatLobbyState));
						}
						else if(task.IsCanceled)
						{
							_logger.Warning($"Countdown transition to '{readyTransitionStateId}' was canceled.", nameof(ChatLobbyState));
						}
					},
					TaskScheduler.Default);
			}
		}

		private void EvaluateReadyThreshold(CancellationToken token)
		{
			if(_transitionRequested)
			{
				return;
			}

			bool thresholdReached = IsReadyThresholdReached();
			if(thresholdReached && !_isCountdownActive)
			{
				_isCountdownActive = true;
				_countdownStartedAtUtc = DateTime.UtcNow;
				_countdownRemainingSeconds = Math.Max(0f, _settings.TransitionCountdownSeconds);
				_countdownEndsAtUtc = _countdownStartedAtUtc.AddSeconds(_countdownRemainingSeconds);
				_logger.Log(
					$"Ready threshold reached. Countdown started seconds={_countdownRemainingSeconds:0.###} connected={_connectedPlayers.Count} ready={GetReadyPlayerCount()} required={GetRequiredReadyPlayerCount()}.",
					nameof(ChatLobbyState));
				_ = BroadcastCountdownAsync(ChatLobbyCountdownStatus.Started, token);
				if(_countdownRemainingSeconds <= 0f)
				{
					UpdateCountdown(0f);
				}
				return;
			}

			if(!thresholdReached && _isCountdownActive)
			{
				_isCountdownActive = false;
				_countdownRemainingSeconds = 0f;
				_countdownStartedAtUtc = default;
				_countdownEndsAtUtc = default;
				_logger.Log(
					$"Ready threshold dropped. Countdown canceled connected={_connectedPlayers.Count} ready={GetReadyPlayerCount()} required={GetRequiredReadyPlayerCount()}.",
					nameof(ChatLobbyState));
				_ = BroadcastCountdownAsync(ChatLobbyCountdownStatus.Canceled, token);
			}
		}

		private bool IsReadyThresholdReached()
		{
			int connectedCount = _connectedPlayers.Count;
			if(connectedCount == 0)
			{
				return false;
			}

			return GetReadyPlayerCount() >= GetRequiredReadyPlayerCount();
		}

		private int GetReadyPlayerCount()
		{
			return _readyPlayers.Keys.Count(_connectedPlayers.Contains);
		}

		private int GetRequiredReadyPlayerCount()
		{
			return GetRequiredReadyPlayerCount(_connectedPlayers.Count);
		}

		private int GetRequiredReadyPlayerCount(int connectedCount)
		{
			if(connectedCount == 0)
			{
				return 0;
			}

			float threshold = Math.Clamp(_settings.ReadyThresholdPercent, 0f, 100f);
			int required = (int)Math.Ceiling(connectedCount * (threshold / 100f));
			return Math.Clamp(required, 1, connectedCount);
		}

		private ChatLobbyReadyStateResponse CreateReadyStateMessage(int? extraPlayerId = null)
		{
			var connectedPlayers = extraPlayerId is int playerId
				? _connectedPlayers.Append(playerId).Distinct().ToArray()
				: _connectedPlayers.ToArray();
			var players = connectedPlayers
				.OrderBy(id => id)
				.Select(id =>
				{
					if(TryGetReadyPlayer(id, out var readyPlayer))
					{
						return readyPlayer!;
					}

					return new ChatLobbyReadyPlayer
					{
						PlayerId = id,
						DisplayName = _playerIdentityService.GetDisplayName(id),
						IsReady = false,
						ReadyPayload = Array.Empty<byte>()
					};
				})
				.ToArray();

			return new ChatLobbyReadyStateResponse
			{
				Players = players,
				ReadyPlayerCount = GetReadyPlayerCount(),
				RequiredReadyPlayerCount = GetRequiredReadyPlayerCount(connectedPlayers.Length),
				ReadyPercentage = connectedPlayers.Length == 0
					? 0f
					: (GetReadyPlayerCount() / (float)connectedPlayers.Length) * 100f
			};
		}

		private ChatLobbyPlayerStateResponse CreatePlayerStateMessage(int? extraPlayerId = null)
		{
			var connectedPlayers = extraPlayerId is int playerId
				? _connectedPlayers.Append(playerId).Distinct().ToArray()
				: _connectedPlayers.ToArray();

			return new ChatLobbyPlayerStateResponse
			{
				Players = connectedPlayers
					.OrderBy(id => id)
					.Select(id =>
					{
						TryGetReadyPlayer(id, out var readyPlayer);
						return new ChatLobbyPlayer
						{
							PlayerId = id,
							DisplayName = _playerIdentityService.GetDisplayName(id),
							IsReady = readyPlayer is { IsReady: true },
							ReadyPayload = readyPlayer is null ? Array.Empty<byte>() : readyPlayer.ReadyPayload
						};
					})
					.ToArray()
			};
		}

		private Task BroadcastReadyStateAsync(CancellationToken token)
		{
			return BroadcastAsync(
				PacketType.ChatLobbyReadyState,
				_payloadCodec.Encode(CreateReadyStateMessage()),
				token);
		}

		private Task BroadcastPlayerStateAsync(CancellationToken token)
		{
			return BroadcastAsync(
				PacketType.ChatLobbyPlayerState,
				_payloadCodec.Encode(CreatePlayerStateMessage()),
				token);
		}

		private Task BroadcastCountdownAsync(ChatLobbyCountdownStatus status, CancellationToken token)
		{
			return BroadcastAsync(
				PacketType.ChatLobbyCountdown,
				_payloadCodec.Encode(new ChatLobbyCountdownResponse
				{
					Status = status,
					RemainingSeconds = status == ChatLobbyCountdownStatus.Started
						? GetCountdownRemainingSeconds(DateTime.UtcNow)
						: _countdownRemainingSeconds,
					StartedAtUtc = _countdownStartedAtUtc,
					EndsAtUtc = _countdownEndsAtUtc
				}),
				token);
		}

		private float GetCountdownRemainingSeconds(DateTime nowUtc)
		{
			if(!_isCountdownActive || _countdownEndsAtUtc == default)
			{
				return 0f;
			}

			return Math.Max(0f, _countdownRemainingSeconds);
		}

		private bool TryGetReadyPlayer(int playerId, out ChatLobbyReadyPlayer? readyPlayer)
		{
			if(_readyPlayers.TryGetValue(playerId, out var player))
			{
				readyPlayer = player;
				return true;
			}

			readyPlayer = null;
			return false;
		}

		private byte[] CreateReadyTransitionPayload()
		{
			return _payloadCodec.Encode(CreateReadyStateMessage());
		}

		private string GetReadyTransitionStateId()
		{
			return string.IsNullOrWhiteSpace(_settings.ReadyTransitionStateId)
				? string.Empty
				: _settings.ReadyTransitionStateId.Trim();
		}

		private sealed class DefaultLobbyPlayerIdentityService : IPlayerIdentityService
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
