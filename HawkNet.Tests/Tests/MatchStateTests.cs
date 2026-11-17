using HawkNet.Core;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.Game.GameState.Match;
using Xunit;

namespace HawkNet.Tests
{
	public class MatchStateTests
	{
		[Fact]
		public async Task Enter_DecodesLobbyReadyPayloadAndInitializesBaseMatch()
		{
			var harness = CreateHarness(
				new MatchSettings
				{
					Phases =
					[
						new MatchPhaseSettings
						{
							PhaseId = "pre_match",
							Seconds = -1f
						}
					]
				},
				connectedPlayers: [1, 2]);
			using var state = harness.State;
			byte[] readyPayload = [7, 8, 9];
			var lobbyPayload = new ChatLobbyReadyStateResponse
			{
				Players =
				[
					new ChatLobbyReadyPlayer
					{
						PlayerId = 1,
						DisplayName = "Nova",
						IsReady = true,
						ReadyPayload = readyPayload
					}
				],
				ReadyPlayerCount = 1,
				RequiredReadyPlayerCount = 1,
				ReadyPercentage = 100f
			};

			state.Enter(harness.Codec.Encode(lobbyPayload));
			await WaitForAsync(
				() => state.ActivePhaseId == "pre_match",
				TimeSpan.FromSeconds(2));

			Assert.True(state.IsActive);
			Assert.Equal("pre_match", state.ActivePhaseId);
			var player = Assert.Single(state.Players, player => player.PlayerId == 1);
			Assert.Equal("Nova", player.DisplayName);
			Assert.True(player.IsConnected);
			Assert.Equal(readyPayload, player.ReadyPayload);
			Assert.Contains(state.Players, player => player.PlayerId == 2 && player.IsConnected);
			Assert.Equal(TimeSpan.Zero, harness.UpdateLoop.GetTrackTime("match"));
			Assert.Equal(TimeSpan.Zero, harness.UpdateLoop.GetTrackTime("match_phase"));
		}

		[Fact]
		public async Task Enter_RunsConfiguredZeroDurationLifecycleInOrder()
		{
			var match = new RecordingMatch();
			var harness = CreateHarness(
				new MatchSettings
				{
					Phases =
					[
						new MatchPhaseSettings { PhaseId = "pre_match", Seconds = 0f },
						new MatchPhaseSettings { PhaseId = "match", Seconds = 0f },
						new MatchPhaseSettings { PhaseId = "post_match", Seconds = 0f }
					]
				},
				match: match);
			using var state = harness.State;

			state.Enter(Array.Empty<byte>());
			await WaitForAsync(
				() => state.IsComplete,
				TimeSpan.FromSeconds(2));

			Assert.True(state.IsComplete);
			Assert.Equal(
				["initialize", "subscribe", "pre_match", "begin_match", "end_match", "post_match"],
				match.Calls);
		}

		[Fact]
		public async Task GameMatchBase_TracksLateJoinsDisconnectsAndChat()
		{
			var match = new RecordingMatch();
			var harness = CreateHarness(
				new MatchSettings
				{
					Phases =
					[
						new MatchPhaseSettings
						{
							PhaseId = "match",
							Seconds = -1f
						}
					]
				},
				match: match,
				connectedPlayers: [1]);
			using var state = harness.State;
			state.Enter(Array.Empty<byte>());
			await WaitForAsync(
				() => state.ActivePhaseId == "match",
				TimeSpan.FromSeconds(2));

			await harness.Dispatcher.DispatchAsync(new PlayerConnectedEvent(2));
			await harness.Dispatcher.DispatchAsync(new ChatMessageLoggedEvent(new ChatEntry(2, "Pilot", DateTime.UtcNow, "hello")));
			await harness.Dispatcher.DispatchAsync(new MatchCommandReceivedEvent(2, "move", [4, 5, 6]));
			await harness.Dispatcher.DispatchAsync(new PlayerConnectionLostEvent(1, ConnectionCloseReason.ClientDisconnected));

			Assert.Contains(state.Players, player => player.PlayerId == 2 && player.IsConnected);
			Assert.Contains(state.Players, player => player.PlayerId == 1 && !player.IsConnected);
			Assert.Equal(2, match.ChatMessageCount);
			Assert.Equal(1, match.MatchCommandCount);
			Assert.Contains(match.Calls, call => call == "command:2:move");
			Assert.Contains(match.Calls, call => call == "joined:2");
			Assert.Contains(match.Calls, call => call == "disconnected:1");
		}

		[Fact]
		public async Task CreatePayloadForPlayer_ReturnsCurrentMatchSnapshot()
		{
			var harness = CreateHarness(
				new MatchSettings
				{
					VariantId = "base",
					Phases =
					[
						new MatchPhaseSettings
						{
							PhaseId = "match",
							Seconds = -1f
						}
					]
				},
				connectedPlayers: [5]);
			using var state = harness.State;
			state.Enter(Array.Empty<byte>());
			await WaitForAsync(
				() => state.ActivePhaseId == "match",
				TimeSpan.FromSeconds(2));

			var payload = harness.Codec.Decode<MatchPayload>(
				state.CreatePayloadForPlayer(5, harness.Codec));

			Assert.Equal("base", payload.VariantId);
			Assert.Equal("match", payload.PhaseId);
			Assert.Equal(-1f, payload.PhaseSeconds);
			Assert.Equal(DateTime.MaxValue, payload.PhaseEndsAtUtc);
			Assert.Contains(payload.Players, player => player.PlayerId == 5);
		}

		[Fact]
		public async Task CompletedMatch_RequestsConfiguredTransitionState()
		{
			var harness = CreateHarness(new MatchSettings
			{
				CompletedTransitionStateId = "chat_lobby",
				Phases =
				[
					new MatchPhaseSettings
					{
						PhaseId = "match",
						Seconds = 0f
					}
				]
			});
			GameStateTransitionRequestedEvent? transition = null;
			harness.Dispatcher.Subscribe<GameStateTransitionRequestedEvent>((gameEvent, _) =>
			{
				transition = gameEvent;
				return Task.CompletedTask;
			});
			using var state = harness.State;

			state.Enter(Array.Empty<byte>());
			await WaitForAsync(
				() => state.IsComplete && transition is not null,
				TimeSpan.FromSeconds(2));

			Assert.True(state.IsComplete);
			Assert.NotNull(transition);
			Assert.Equal("chat_lobby", transition.Value.ToStateId);
		}

		[Fact]
		public async Task InfinitePhase_WaitsUntilManualCompletion()
		{
			var harness = CreateHarness(new MatchSettings
			{
				Phases =
				[
					new MatchPhaseSettings
					{
						PhaseId = "pre_match",
						Seconds = -1f
					},
					new MatchPhaseSettings
					{
						PhaseId = "match",
						Seconds = -1f
					}
				]
			});
			using var state = harness.State;

			state.Enter(Array.Empty<byte>());
			await WaitForAsync(
				() => state.ActivePhaseId == "pre_match",
				TimeSpan.FromSeconds(2));
			await Task.Delay(50);

			Assert.Equal("pre_match", state.ActivePhaseId);

			await state.CompleteCurrentPhaseAsync();
			await WaitForAsync(
				() => state.ActivePhaseId == "match",
				TimeSpan.FromSeconds(2));

			Assert.Equal("match", state.ActivePhaseId);
			Assert.False(state.IsComplete);
		}

		[Fact]
		public async Task MatchState_DispatchesPhaseStartedAndEndedEvents()
		{
			var harness = CreateHarness(new MatchSettings
			{
				Phases =
				[
					new MatchPhaseSettings
					{
						PhaseId = "match",
						Seconds = 0f
					}
				]
			});
			MatchPhaseChangedEvent? phaseChanged = null;
			MatchStartedEvent? started = null;
			MatchEndedEvent? ended = null;
			harness.Dispatcher.Subscribe<MatchPhaseChangedEvent>((gameEvent, _) =>
			{
				phaseChanged = gameEvent;
				return Task.CompletedTask;
			});
			harness.Dispatcher.Subscribe<MatchStartedEvent>((gameEvent, _) =>
			{
				started = gameEvent;
				return Task.CompletedTask;
			});
			harness.Dispatcher.Subscribe<MatchEndedEvent>((gameEvent, _) =>
			{
				ended = gameEvent;
				return Task.CompletedTask;
			});
			using var state = harness.State;

			state.Enter(Array.Empty<byte>());
			await WaitForAsync(
				() => ended is not null,
				TimeSpan.FromSeconds(2));

			Assert.NotNull(phaseChanged);
			Assert.Equal("match", phaseChanged.Value.PhaseId);
			Assert.NotNull(started);
			Assert.Equal("match", started.Value.Payload.PhaseId);
			Assert.NotNull(ended);
			Assert.Equal("match", ended.Value.Payload.PhaseId);
		}

		[Fact]
		public async Task EnterPhase_SendsAdminChatMessage()
		{
			var harness = CreateHarness(new MatchSettings
			{
				Phases =
				[
					new MatchPhaseSettings
					{
						PhaseId = "match",
						Seconds = -1f
					}
				]
			});
			using var state = harness.State;

			state.Enter(Array.Empty<byte>());
			await WaitForAsync(
				() => harness.ChatService.GetHistory().Any(entry => entry.Message == "Phase changed to match."),
				TimeSpan.FromSeconds(2));

			var entry = Assert.Single(
				harness.ChatService.GetHistory(),
				entry => entry.Message == "Phase changed to match.");
			Assert.Equal(0, entry.PlayerId);
			Assert.Equal("Admin", entry.DisplayName);
		}

		private static MatchHarness CreateHarness(
			MatchSettings settings,
			IGameMatch? match = null,
			int[]? connectedPlayers = null)
		{
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			var services = new ServiceRegistry();
			var config = new ConfigService(new GameHostConfig
			{
				Match = settings
			});
			var identityService = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());
			foreach(int playerId in connectedPlayers ?? Array.Empty<int>())
			{
				identityService.GetOrCreateIdentity(playerId);
			}

			var chatService = new ChatService(dispatcher, new ChatSettings(), identityService, codec);
			var updateLoop = new UpdateLoop();
			services.Register(updateLoop);
			var context = new GameMatchContext(
				dispatcher,
				NullLogger.Instance,
				config,
				services,
				chatService,
				identityService,
				codec,
				() => connectedPlayers ?? Array.Empty<int>(),
				(_, _, _) => Task.CompletedTask);
			var state = new MatchState(
				dispatcher,
				match ?? new GameMatchBase(),
				context,
				settings,
				codec);

			return new MatchHarness(dispatcher, codec, updateLoop, chatService, state);
		}

		private sealed record MatchHarness(
			EventDispatcher Dispatcher,
			IPayloadCodec Codec,
			UpdateLoop UpdateLoop,
			ChatService ChatService,
			MatchState State);

		private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
		{
			var start = DateTime.UtcNow;
			while(DateTime.UtcNow - start < timeout)
			{
				if(condition())
				{
					return;
				}

				await Task.Delay(10);
			}

			throw new TimeoutException("Timed out waiting for condition.");
		}

		private sealed class RecordingMatch : GameMatchBase
		{
			public List<string> Calls { get; } = new();
			public int ChatMessageCount { get; private set; }
			public int MatchCommandCount { get; private set; }

			public override async Task InitializeAsync(IGameMatchContext context, MatchPayload payload, CancellationToken token = default)
			{
				Calls.Add("initialize");
				await base.InitializeAsync(context, payload, token);
			}

			public override async Task SubscribeAsync(CancellationToken token = default)
			{
				Calls.Add("subscribe");
				await base.SubscribeAsync(token);
			}

			public override Task PreMatchAsync(CancellationToken token = default)
			{
				Calls.Add("pre_match");
				return Task.CompletedTask;
			}

			public override Task BeginMatchAsync(CancellationToken token = default)
			{
				Calls.Add("begin_match");
				return Task.CompletedTask;
			}

			public override Task EndMatchAsync(CancellationToken token = default)
			{
				Calls.Add("end_match");
				return Task.CompletedTask;
			}

			public override Task PostMatchAsync(CancellationToken token = default)
			{
				Calls.Add("post_match");
				return Task.CompletedTask;
			}

			protected override Task OnPlayerJoinedAsync(MatchPlayer player, CancellationToken token)
			{
				Calls.Add($"joined:{player.PlayerId}");
				return Task.CompletedTask;
			}

			protected override Task OnPlayerDisconnectedAsync(MatchPlayer player, ConnectionCloseReason reason, CancellationToken token)
			{
				Calls.Add($"disconnected:{player.PlayerId}");
				return Task.CompletedTask;
			}

			protected override Task OnChatMessageAsync(ChatEntry entry, CancellationToken token)
			{
				ChatMessageCount++;
				return Task.CompletedTask;
			}

			protected override Task OnMatchCommandAsync(int playerId, string commandId, byte[] payload, CancellationToken token)
			{
				MatchCommandCount++;
				Calls.Add($"command:{playerId}:{commandId}");
				return Task.CompletedTask;
			}
		}
	}
}
