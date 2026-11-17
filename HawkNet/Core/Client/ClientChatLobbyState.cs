/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public sealed class ClientChatLobbyState : ClientGameState<ChatLobbyPayload>
	{
		private readonly ClientChatService _chatService;
		private readonly ClientPlayerService _playerService;
		private readonly GameClient _client;
		private readonly IPayloadCodec _payloadCodec;
		private readonly List<ChatLobbyPlayer> _players = new();
		private readonly List<ChatLobbyReadyPlayer> _readyPlayers = new();
		private readonly List<ChatEntry> _initialChatHistory = new();
		private ChatLobbyCountdownResponse? _countdown;
		private bool _isActive;

		public ClientChatLobbyState(
			IEventDispatcher eventDispatcher,
			GameClient client,
			ClientChatService chatService,
			ClientPlayerService playerService,
			IPayloadCodec payloadCodec)
			: base(eventDispatcher, payloadCodec)
		{
			_client = client;
			_chatService = chatService;
			_playerService = playerService;
			_payloadCodec = payloadCodec;
		}

		public bool IsActive => _isActive;
		public float LastDeltaTime { get; private set; }
		public int UpdateCount { get; private set; }
		public int ChatMessageCount { get; private set; }
		public ChatEntry? LastChatEntry { get; private set; }
		public ClientPlayerData? CurrentPlayer => _playerService.CurrentPlayer;
		public IReadOnlyList<ChatEntry> ChatHistory => _chatService.GetHistory();
		public IReadOnlyList<ChatLobbyPlayer> Players => _players.ToArray();
		public IReadOnlyList<ChatLobbyReadyPlayer> ReadyPlayers => _readyPlayers.ToArray();
		public IReadOnlyList<ChatEntry> InitialChatHistory => _initialChatHistory.ToArray();
		public int ReadyPlayerCount { get; private set; }
		public int RequiredReadyPlayerCount { get; private set; }
		public float ReadyPercentage { get; private set; }
		public ChatLobbyCountdownStatus CountdownStatus => _countdown?.Status ?? ChatLobbyCountdownStatus.None;
		public DateTime CountdownStartedAtUtc => _countdown?.StartedAtUtc ?? default;
		public DateTime CountdownEndsAtUtc => _countdown?.EndsAtUtc ?? default;
		public float CountdownRemainingSeconds
		{
			get
			{
				if(_countdown is not { Status: ChatLobbyCountdownStatus.Started } countdown)
				{
					return _countdown?.RemainingSeconds ?? 0f;
				}

				if(countdown.EndsAtUtc == default)
				{
					return countdown.RemainingSeconds;
				}

				return Math.Max(0f, (float)(countdown.EndsAtUtc - DateTime.UtcNow).TotalSeconds);
			}
		}
		public bool IsLocalPlayerReady => CurrentPlayer is { } player
			&& _readyPlayers.Any(readyPlayer => readyPlayer.PlayerId == player.PlayerId && readyPlayer.IsReady);

		protected override void Enter(ChatLobbyPayload? payload)
		{
			_isActive = true;
			_players.Clear();
			_initialChatHistory.Clear();
			if(payload is not null)
			{
				_players.AddRange(payload.Players);
				_initialChatHistory.AddRange(payload.ChatHistory);
				ApplyReadyState(payload.ReadyState);
				_countdown = payload.Countdown;
			}

			Subscribe<ClientChatMessageReceivedEvent>(OnChatMessageReceived);
			Subscribe<ClientChatHistoryReceivedEvent>(OnChatHistoryReceived);
			Subscribe<ClientChatLobbyReadyStateReceivedEvent>(OnReadyStateReceived);
			Subscribe<ClientChatLobbyCountdownReceivedEvent>(OnCountdownReceived);
			Subscribe<ClientChatLobbyPlayerStateReceivedEvent>(OnPlayerStateReceived);
		}

		public override void Update(float deltaTime)
		{
			LastDeltaTime = deltaTime;
			UpdateCount++;
		}

		public override void Exit()
		{
			_isActive = false;
			UnsubscribeAll();
			LastChatEntry = null;
			ChatMessageCount = 0;
			_players.Clear();
			_readyPlayers.Clear();
			_initialChatHistory.Clear();
			_countdown = null;
			ReadyPlayerCount = 0;
			RequiredReadyPlayerCount = 0;
			ReadyPercentage = 0f;
		}

		public Task SendChatMessageAsync(string message, CancellationToken token = default)
		{
			return _chatService.SendMessageAsync(message, token);
		}

		public Task ChangeDisplayNameAsync(string displayName, CancellationToken token = default)
		{
			return _playerService.ChangeDisplayNameAsync(displayName, token);
		}

		public Task SetReadyAsync(bool isReady, byte[] payload, CancellationToken token = default)
		{
			var request = new ChatLobbyReadyRequest
			{
				IsReady = isReady,
				Payload = payload?.ToArray() ?? Array.Empty<byte>()
			};

			return _client.SendPacketAsync(
				PacketType.ChatLobbyReadyRequest,
				_payloadCodec.Encode(request),
				token);
		}

		private Task OnChatMessageReceived(ClientChatMessageReceivedEvent gameEvent, CancellationToken token)
		{
			LastChatEntry = gameEvent.Entry;
			ChatMessageCount++;
			return Task.CompletedTask;
		}

		private Task OnChatHistoryReceived(ClientChatHistoryReceivedEvent gameEvent, CancellationToken token)
		{
			return Task.CompletedTask;
		}

		private Task OnReadyStateReceived(ClientChatLobbyReadyStateReceivedEvent gameEvent, CancellationToken token)
		{
			ApplyReadyState(gameEvent.ReadyState);
			return Task.CompletedTask;
		}

		private Task OnCountdownReceived(ClientChatLobbyCountdownReceivedEvent gameEvent, CancellationToken token)
		{
			_countdown = gameEvent.Countdown;
			return Task.CompletedTask;
		}

		private Task OnPlayerStateReceived(ClientChatLobbyPlayerStateReceivedEvent gameEvent, CancellationToken token)
		{
			_players.Clear();
			_players.AddRange(gameEvent.PlayerState.Players);
			return Task.CompletedTask;
		}

		private void ApplyReadyState(ChatLobbyReadyStateResponse readyState)
		{
			_readyPlayers.Clear();
			_readyPlayers.AddRange(readyState.Players);
			ReadyPlayerCount = readyState.ReadyPlayerCount;
			RequiredReadyPlayerCount = readyState.RequiredReadyPlayerCount;
			ReadyPercentage = readyState.ReadyPercentage;
		}
	}
}
