using HawkNet.Core;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.Game.GameState.Lobby;
using System.Text;
using Xunit;

namespace HawkNet.Tests
{
	public class ChatLobbyStateTests
	{
		[Fact]
		public async Task Enter_SubscribesToConnectionEvents()
		{
			var dispatcher = new EventDispatcher();
			using var state = new ChatLobbyState(dispatcher);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(new PlayerConnectedEvent(1));
			await dispatcher.DispatchAsync(new PlayerConnectedEvent(2));

			Assert.True(state.IsActive);
			Assert.Equal(new[] { 1, 2 }, state.ConnectedPlayers.OrderBy(id => id));
		}

		[Fact]
		public void Enter_LoadsCurrentConnectedPlayers()
		{
			var dispatcher = new EventDispatcher();
			using var state = new ChatLobbyState(dispatcher, () => new[] { 3, 5 });

			state.Enter(Array.Empty<byte>());

			Assert.Equal(new[] { 3, 5 }, state.ConnectedPlayers.OrderBy(id => id));
		}

		[Fact]
		public async Task Reconnect_AddsPlayerBackToLobby()
		{
			var dispatcher = new EventDispatcher();
			using var state = new ChatLobbyState(dispatcher);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(new PlayerConnectionLostEvent(4, ConnectionCloseReason.ClientDisconnected));
			await dispatcher.DispatchAsync(new PlayerReconnectedEvent(4));

			Assert.Contains(4, state.ConnectedPlayers);
		}

		[Fact]
		public async Task ConnectionLostAndDisconnected_RemovePlayer()
		{
			var dispatcher = new EventDispatcher();
			using var state = new ChatLobbyState(dispatcher);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(new PlayerConnectedEvent(7));
			await dispatcher.DispatchAsync(new PlayerConnectionLostEvent(7, ConnectionCloseReason.ClientDisconnected));

			Assert.DoesNotContain(7, state.ConnectedPlayers);

			await dispatcher.DispatchAsync(new PlayerConnectedEvent(7));
			await dispatcher.DispatchAsync(new PlayerDisconnectedEvent(7, ConnectionCloseReason.SessionTimeout));

			Assert.DoesNotContain(7, state.ConnectedPlayers);
		}

		[Fact]
		public void Update_TracksTickInformation()
		{
			var dispatcher = new EventDispatcher();
			using var state = new ChatLobbyState(dispatcher);

			state.Update(0.033f);

			Assert.Equal(1, state.UpdateCount);
			Assert.Equal(0.033f, state.LastDeltaTime);
		}

		[Fact]
		public async Task Exit_UnsubscribesAndClearsState()
		{
			var dispatcher = new EventDispatcher();
			using var state = new ChatLobbyState(dispatcher);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(new PlayerConnectedEvent(9));
			state.Exit();
			await dispatcher.DispatchAsync(new PlayerConnectedEvent(10));

			Assert.False(state.IsActive);
			Assert.Empty(state.ConnectedPlayers);
		}

		[Fact]
		public async Task ChatServiceLoggedMessages_AreHandledByLobby()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(dispatcher, new ChatSettings());
			using var state = new ChatLobbyState(dispatcher);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(2, Encoding.UTF8.GetBytes("hello lobby")));

			Assert.Equal(1, state.ChatMessageCount);
			Assert.NotNull(state.LastChatEntry);
			Assert.Equal(2, state.LastChatEntry.Value.PlayerId);
			Assert.Equal("hello lobby", state.LastChatEntry.Value.Message);
		}

		[Fact]
		public async Task CreatePayloadForPlayer_IncludesPlayersAndChatHistory()
		{
			var dispatcher = new EventDispatcher();
			var identityService = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());
			using var chatService = new ChatService(dispatcher, new ChatSettings(), identityService);
			using var state = new ChatLobbyState(
				dispatcher,
				() => new[] { 2, 1 },
				identityService,
				chatService);
			var codec = new JsonPayloadCodec();

			state.Enter(Array.Empty<byte>());
			identityService.GetOrCreateIdentity(1);
			identityService.TrySetDisplayName(1, "Nova", out _);
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("hello lobby")));

			byte[] payloadBytes = state.CreatePayloadForPlayer(1, codec);
			var payload = codec.Decode<ChatLobbyPayload>(payloadBytes);

			Assert.Equal(new[] { 1, 2 }, payload.Players.Select(player => player.PlayerId));
			Assert.Contains(payload.Players, player => player.PlayerId == 1 && player.DisplayName == "Nova");
			var entry = Assert.Single(payload.ChatHistory);
			Assert.Equal("hello lobby", entry.Message);
		}

		[Fact]
		public async Task ReadyRequest_StoresOpaquePayloadAndBroadcastsReadyState()
		{
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			BroadcastMessageEvent? broadcast = null;
			dispatcher.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
			{
				if(gameEvent.PacketType == PacketType.ChatLobbyReadyState)
				{
					broadcast = gameEvent;
				}

				return Task.CompletedTask;
			});
			using var state = new ChatLobbyState(
				dispatcher,
				() => new[] { 1 },
				payloadCodec: codec);
			byte[] readyPayload = [9, 8, 7];

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(new ChatLobbyReadyRequestReceivedEvent(
				1,
				codec.Encode(new ChatLobbyReadyRequest
				{
					IsReady = true,
					Payload = readyPayload
				})));

			var readyPlayer = Assert.Single(state.ReadyPlayers);
			Assert.Equal(1, readyPlayer.PlayerId);
			Assert.True(readyPlayer.IsReady);
			Assert.Equal(readyPayload, readyPlayer.ReadyPayload);
			Assert.NotNull(broadcast);
			var message = codec.Decode<ChatLobbyReadyStateResponse>(broadcast.Value.Payload);
			Assert.Equal(1, message.ReadyPlayerCount);
			Assert.Equal(1, message.RequiredReadyPlayerCount);
			Assert.Equal(100f, message.ReadyPercentage);
		}

		[Fact]
		public async Task PlayerConnectionChanges_BroadcastPlayerState()
		{
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			BroadcastMessageEvent? broadcast = null;
			dispatcher.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
			{
				if(gameEvent.PacketType == PacketType.ChatLobbyPlayerState)
				{
					broadcast = gameEvent;
				}

				return Task.CompletedTask;
			});
			using var state = new ChatLobbyState(
				dispatcher,
				payloadCodec: codec);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(new PlayerConnectedEvent(7));

			Assert.NotNull(broadcast);
			var message = codec.Decode<ChatLobbyPlayerStateResponse>(broadcast.Value.Payload);
			var player = Assert.Single(message.Players);
			Assert.Equal(7, player.PlayerId);
			Assert.Equal("Player 7", player.DisplayName);
		}

		[Fact]
		public async Task ReadyThreshold_UsesRoundUpAndStartsCountdown()
		{
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			var countdowns = new List<ChatLobbyCountdownResponse>();
			dispatcher.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
			{
				if(gameEvent.PacketType == PacketType.ChatLobbyCountdown)
				{
					countdowns.Add(codec.Decode<ChatLobbyCountdownResponse>(gameEvent.Payload));
				}

				return Task.CompletedTask;
			});
			using var state = new ChatLobbyState(
				dispatcher,
				() => new[] { 1, 2, 3 },
				settings: new ChatLobbySettings
				{
					ReadyThresholdPercent = 75f,
					TransitionCountdownSeconds = 10f
				},
				payloadCodec: codec);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(CreateReadyEvent(codec, 1, true));
			await dispatcher.DispatchAsync(CreateReadyEvent(codec, 2, true));

			Assert.False(state.IsCountdownActive);

			await dispatcher.DispatchAsync(CreateReadyEvent(codec, 3, true));

			Assert.True(state.IsCountdownActive);
			Assert.Equal(10f, state.CountdownRemainingSeconds);
			var started = Assert.Single(countdowns, countdown => countdown.Status == ChatLobbyCountdownStatus.Started);
			Assert.NotEqual(default, started.StartedAtUtc);
			Assert.NotEqual(default, started.EndsAtUtc);
			Assert.True(started.EndsAtUtc > started.StartedAtUtc);
			int countdownCount = countdowns.Count;
			state.Update(1f);
			Assert.Equal(countdownCount, countdowns.Count);
		}

		[Fact]
		public async Task Countdown_CancelsWhenReadinessDropsBelowThreshold()
		{
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			var countdowns = new List<ChatLobbyCountdownResponse>();
			dispatcher.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
			{
				if(gameEvent.PacketType == PacketType.ChatLobbyCountdown)
				{
					countdowns.Add(codec.Decode<ChatLobbyCountdownResponse>(gameEvent.Payload));
				}

				return Task.CompletedTask;
			});
			using var state = new ChatLobbyState(
				dispatcher,
				() => new[] { 1, 2 },
				settings: new ChatLobbySettings
				{
					ReadyThresholdPercent = 100f,
					TransitionCountdownSeconds = 5f
				},
				payloadCodec: codec);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(CreateReadyEvent(codec, 1, true));
			await dispatcher.DispatchAsync(CreateReadyEvent(codec, 2, true));
			await dispatcher.DispatchAsync(CreateReadyEvent(codec, 2, false));

			Assert.False(state.IsCountdownActive);
			Assert.Contains(countdowns, countdown => countdown.Status == ChatLobbyCountdownStatus.Canceled);
		}

		[Fact]
		public async Task CountdownCompletion_RequestsConfiguredTransition()
		{
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			GameStateTransitionRequestedEvent? transition = null;
			dispatcher.Subscribe<GameStateTransitionRequestedEvent>((gameEvent, _) =>
			{
				transition = gameEvent;
				return Task.CompletedTask;
			});
			using var state = new ChatLobbyState(
				dispatcher,
				() => new[] { 1 },
				settings: new ChatLobbySettings
				{
					ReadyThresholdPercent = 100f,
					TransitionCountdownSeconds = 0f,
					ReadyTransitionStateId = "match"
				},
				payloadCodec: codec);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(CreateReadyEvent(codec, 1, true));
			await Task.Delay(25);

			Assert.NotNull(transition);
			Assert.Equal("match", transition.Value.ToStateId);
			var transitionPayload = codec.Decode<ChatLobbyReadyStateResponse>(transition.Value.Payload);
			Assert.Equal(1, transitionPayload.ReadyPlayerCount);
		}

		[Fact]
		public async Task CountdownCompletion_UsesAccumulatedDeltaTime()
		{
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			GameStateTransitionRequestedEvent? transition = null;
			dispatcher.Subscribe<GameStateTransitionRequestedEvent>((gameEvent, _) =>
			{
				transition = gameEvent;
				return Task.CompletedTask;
			});
			using var state = new ChatLobbyState(
				dispatcher,
				() => new[] { 1 },
				settings: new ChatLobbySettings
				{
					ReadyThresholdPercent = 100f,
					TransitionCountdownSeconds = 1f,
					ReadyTransitionStateId = "match"
				},
				payloadCodec: codec);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(CreateReadyEvent(codec, 1, true));
			state.Update(0.4f);

			Assert.Null(transition);

			state.Update(0.6f);
			await Task.Delay(25);

			Assert.NotNull(transition);
			Assert.Equal("match", transition.Value.ToStateId);
		}

		[Fact]
		public async Task InvalidReadyRequest_DoesNotCrashOrBroadcast()
		{
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			bool broadcasted = false;
			dispatcher.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
			{
				if(gameEvent.PacketType == PacketType.ChatLobbyReadyState)
				{
					broadcasted = true;
				}

				return Task.CompletedTask;
			});
			using var state = new ChatLobbyState(
				dispatcher,
				() => new[] { 1 },
				payloadCodec: codec);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(new ChatLobbyReadyRequestReceivedEvent(1, [1, 2, 3]));

			Assert.Empty(state.ReadyPlayers);
			Assert.False(broadcasted);
		}

		[Fact]
		public async Task OversizedReadyPayload_DoesNotStoreOrBroadcast()
		{
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			bool broadcasted = false;
			dispatcher.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
			{
				if(gameEvent.PacketType == PacketType.ChatLobbyReadyState)
				{
					broadcasted = true;
				}

				return Task.CompletedTask;
			});
			using var state = new ChatLobbyState(
				dispatcher,
				() => new[] { 1 },
				settings: new ChatLobbySettings
				{
					MaxReadyPayloadBytes = 2
				},
				payloadCodec: codec);

			state.Enter(Array.Empty<byte>());
			await dispatcher.DispatchAsync(new ChatLobbyReadyRequestReceivedEvent(
				1,
				codec.Encode(new ChatLobbyReadyRequest
				{
					IsReady = true,
					Payload = [1, 2, 3]
				})));

			Assert.Empty(state.ReadyPlayers);
			Assert.False(broadcasted);
		}

		private static ChatLobbyReadyRequestReceivedEvent CreateReadyEvent(
			IPayloadCodec codec,
			int playerId,
			bool isReady)
		{
			return new ChatLobbyReadyRequestReceivedEvent(
				playerId,
				codec.Encode(new ChatLobbyReadyRequest
				{
					IsReady = isReady,
					Payload = isReady ? [1, 2, 3] : []
				}));
		}
	}
}
