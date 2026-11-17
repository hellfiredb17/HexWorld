using System.Text;
using ClanIronSim;
using ClanIronSim.Commands;
using ClanIronSim.HawkNet;
using ClanIronSim.SimMath;
using HawkNet.Core;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	public class ClanIronSimHawkNetAdapterTests
	{
		[Fact]
		public void HawkNetProject_DoesNotReferenceClanIronSim()
		{
			string project = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "HawkNet", "HawkNet.csproj"));

			Assert.DoesNotContain("ClanIronSim", project, StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public void ClanIronSimMatchModule_RegistersClanIronVariant()
		{
			var registry = new MatchVariantRegistry();
			var builder = new TestServerBuilder(registry);

			new ClanIronSimMatchModule().Configure(builder);

			IGameMatch match = registry.Create("clan_iron", new TestMatchContext([]));
			Assert.IsType<ClanIronSimMatch>(match);
		}

		[Fact]
		public async Task ClanIronSimMatch_InitializesWorldAndSpawnsConnectedPlayers()
		{
			var context = new TestMatchContext([
				new MatchPlayer
				{
					PlayerId = 7,
					DisplayName = "Scout",
					IsConnected = true
				},
				new MatchPlayer
				{
					PlayerId = 8,
					DisplayName = "Offline",
					IsConnected = false
				}
			]);
			var match = new ClanIronSimMatch();

			await match.InitializeAsync(context, new MatchPayload
			{
				Players = context.InitialPlayers
			});

			Assert.NotNull(match.Overlord);
			Assert.True(match.Overlord!.TryGetPlayerNavigator(7, out var navigator));
			Assert.Equal("player_7_nav", navigator!.Id);
			Assert.Equal(new SimVector2(2.5f, 2.5f), navigator.CurrentWorldPosition);
			Assert.False(match.Overlord.TryGetPlayerNavigator(8, out _));
			Assert.True(match.Overlord.Players.TryGetProfile(7, out var profile));
			Assert.Equal("Scout", profile!.DisplayName);
			Assert.False(match.Overlord.Players.TryGetProfile(8, out _));
		}

		[Fact]
		public async Task ClanIronSimMatch_ProcessesMoveMessageOnUpdateAndEmitsSimActionMessage()
		{
			var context = new TestMatchContext([
				new MatchPlayer
				{
					PlayerId = 7,
					DisplayName = "Scout",
					IsConnected = true
				}
			]);
			var match = new ClanIronSimMatch();
			await match.InitializeAsync(context, new MatchPayload
			{
				Players = context.InitialPlayers
			});
			await match.SubscribeAsync();
			var command = new SimCommand
			{
				Type = SimCommandType.PlayerMoveToWorldPosition,
				PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
				{
					PlayerId = 999,
					WorldPosition = new SimVector2(3.5f, 2.5f)
				}
			};

			await context.Events.DispatchAsync(new GameStateMessageReceivedEvent(
				7,
				ClanIronSimHawkNetMessageIds.SimCommand,
				Encoding.UTF8.GetBytes(SimJson.ToJson(command))));
			await match.UpdateAsync(0.1f);

			BroadcastMessageEvent broadcast = Assert.Single(context.Broadcasts);
			Assert.Equal(PacketType.GameStateMessage, broadcast.PacketType);
			Assert.Equal(DeliveryOptions.ReliableGameplay, broadcast.Delivery);
			GameStateMessage message = GameStateMessageProtocol.Decode(broadcast.Payload, context.PayloadCodec);
			Assert.Equal(ClanIronSimHawkNetMessageIds.SimAction, message.MessageId);
			SimAction action = SimJson.FromJson<SimAction>(Encoding.UTF8.GetString(message.Payload));
			Assert.Equal(SimActionType.Move, action.Type);
			Assert.NotNull(action.Move);
			Assert.Equal(MoveStatus.Starting, action.Move.Status);
			Assert.Equal(7, action.Move.PlayerId);
			Assert.Equal("player_7_nav", action.Move.MoverId);

			await match.UnsubscribeAsync();
		}

		[Fact]
		public async Task ClanIronSimMatch_ReconnectKeepsSimPlayerNavigatorForMovement()
		{
			var context = new TestMatchContext([
				new MatchPlayer
				{
					PlayerId = 7,
					DisplayName = "Scout",
					IsConnected = true
				}
			]);
			var match = new ClanIronSimMatch();
			await match.InitializeAsync(context, new MatchPayload
			{
				Players = context.InitialPlayers
			});
			await match.SubscribeAsync();

			await context.Events.DispatchAsync(new PlayerConnectionLostEvent(7, ConnectionCloseReason.ClientDisconnected));
			Assert.True(match.Overlord!.TryGetPlayerNavigator(7, out _));

			await context.Events.DispatchAsync(new PlayerReconnectedEvent(7));
			var command = new SimCommand
			{
				Type = SimCommandType.PlayerMoveToWorldPosition,
				PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
				{
					WorldPosition = new SimVector2(3.5f, 2.5f)
				}
			};

			await context.Events.DispatchAsync(new GameStateMessageReceivedEvent(
				7,
				ClanIronSimHawkNetMessageIds.SimCommand,
				Encoding.UTF8.GetBytes(SimJson.ToJson(command))));
			await match.UpdateAsync(0.1f);

			BroadcastMessageEvent broadcast = Assert.Single(context.Broadcasts);
			GameStateMessage message = GameStateMessageProtocol.Decode(broadcast.Payload, context.PayloadCodec);
			SimAction action = SimJson.FromJson<SimAction>(Encoding.UTF8.GetString(message.Payload));
			Assert.Equal(MoveStatus.Starting, action.Move!.Status);
			Assert.Equal(7, action.Move.PlayerId);
			Assert.Equal("player_7_nav", action.Move.MoverId);

			await match.UnsubscribeAsync();
		}

		[Fact]
		public async Task ClanIronSimMatch_ProcessesSimRequestAndSendsResponseToPlayer()
		{
			var context = new TestMatchContext([
				new MatchPlayer
				{
					PlayerId = 7,
					DisplayName = "Scout",
					IsConnected = true
				}
			]);
			var match = new ClanIronSimMatch();
			await match.InitializeAsync(context, new MatchPayload
			{
				Players = context.InitialPlayers
			});
			await match.SubscribeAsync();
			var request = new SimRequest
			{
				Type = SimRequestType.RequestRegionState,
				RequestRegionState = new RequestRegionStatePayload
				{
					RegionId = "rect island"
				}
			};

			await context.Events.DispatchAsync(new GameStateMessageReceivedEvent(
				7,
				ClanIronSimHawkNetMessageIds.SimRequest,
				Encoding.UTF8.GetBytes(SimJson.ToJson(request))));

			SendPlayerMessageEvent send = Assert.Single(context.Sends);
			Assert.Equal(7, send.PlayerId);
			Assert.Equal(PacketType.GameStateMessage, send.PacketType);
			Assert.Equal(DeliveryOptions.ReliableGameplay, send.Delivery);
			GameStateMessage message = GameStateMessageProtocol.Decode(send.Payload, context.PayloadCodec);
			Assert.Equal(ClanIronSimHawkNetMessageIds.SimResponse, message.MessageId);
			SimResponse response = SimJson.FromJson<SimResponse>(Encoding.UTF8.GetString(message.Payload));
			Assert.Equal(SimResponseType.RegionState, response.Type);
			Assert.NotNull(response.RegionState);
			Assert.Equal("rect island", response.RegionState.RegionId);
			SimInteractableState interactable = Assert.Single(response.RegionState.Interactables);
			Assert.Equal("player_7_nav", interactable.Id);
			Assert.Equal(2, interactable.Location.TileGridX);
			Assert.Equal(2, interactable.Location.TileGridZ);
			Assert.Equal(new SimVector2(2.5f, 2.5f), interactable.CurrentWorldPosition);

			await match.UnsubscribeAsync();
		}

		private static string GetRepositoryRoot()
		{
			return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
		}

		private sealed class TestMatchContext : IGameMatchContext
		{
			private readonly MatchPlayer[] _players;

			public TestMatchContext(MatchPlayer[] players)
			{
				_players = players;
				Events.Subscribe<SendPlayerMessageEvent>((gameEvent, _) =>
				{
					Sends.Add(gameEvent);
					return Task.CompletedTask;
				});
				Events.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
				{
					Broadcasts.Add(gameEvent);
					return Task.CompletedTask;
				});
			}

			public MatchPlayer[] InitialPlayers => _players;
			public IEventDispatcher Events { get; } = new EventDispatcher();
			public ILogger Logger { get; } = NullLogger.Instance;
			public IConfigService Config { get; } = new ConfigService(new GameHostConfig());
			public IServiceRegistry Services { get; } = new ServiceRegistry();
			public ChatService ChatService => null!;
			public IPlayerIdentityService PlayerIdentityService { get; } = new TestPlayerIdentityService();
			public IPayloadCodec PayloadCodec { get; } = new JsonPayloadCodec();
			public List<SendPlayerMessageEvent> Sends { get; } = new();
			public List<BroadcastMessageEvent> Broadcasts { get; } = new();

			public IEnumerable<int> GetConnectedPlayerIds()
			{
				return _players.Where(player => player.IsConnected).Select(player => player.PlayerId);
			}

			public Task RequestTransitionAsync(string toStateId, byte[] payload, CancellationToken token = default)
			{
				return Task.CompletedTask;
			}

			public Task CompleteCurrentPhaseAsync(byte[]? payload = null, CancellationToken token = default)
			{
				return Task.CompletedTask;
			}
		}

		private sealed class TestPlayerIdentityService : IPlayerIdentityService
		{
			public PlayerIdentity GetOrCreateIdentity(int playerId)
			{
				DateTime now = DateTime.UtcNow;
				return new PlayerIdentity(playerId, GetDisplayName(playerId), now, now);
			}

			public string GetDisplayName(int playerId)
			{
				return $"Player {playerId}";
			}

			public bool TrySetDisplayName(int playerId, string displayName, out PlayerIdentity identity)
			{
				identity = GetOrCreateIdentity(playerId) with { DisplayName = displayName };
				return true;
			}

			public bool TryGetIdentity(int playerId, out PlayerIdentity identity)
			{
				identity = GetOrCreateIdentity(playerId);
				return true;
			}
		}

		private sealed class TestServerBuilder : IHawkNetServerBuilder
		{
			private readonly MatchVariantRegistry _registry;

			public TestServerBuilder(MatchVariantRegistry registry)
			{
				_registry = registry;
			}

			public GameHostConfig Config { get; } = new();
			public IEventDispatcher Events { get; } = new EventDispatcher();
			public IServiceRegistry Services { get; } = new ServiceRegistry();

			public IHawkNetServerBuilder AddService<TService>(TService service)
				where TService : class
			{
				Services.Register(service);
				return this;
			}

			public IHawkNetServerBuilder ReplaceService<TService>(TService service)
				where TService : class
			{
				Services.Replace(service);
				return this;
			}

			public IHawkNetServerBuilder RegisterServiceModule(string id, Func<IHawkNetServerModule> factory, Type[] serviceTypes, params string[] dependencies)
			{
				return this;
			}

			public IHawkNetServerBuilder RegisterServiceModule(string id, string version, Func<IHawkNetServerModule> factory, Type[] serviceTypes, params string[] dependencies)
			{
				return this;
			}

			public IHawkNetServerBuilder RegisterGameStateModule(string id, Func<IHawkNetServerModule> factory, string[] gameStateIds, params string[] dependencies)
			{
				return this;
			}

			public IHawkNetServerBuilder RegisterGameStateModule(string id, string version, Func<IHawkNetServerModule> factory, string[] gameStateIds, params string[] dependencies)
			{
				return this;
			}

			public IHawkNetServerBuilder AddModule(IHawkNetServerModule module)
			{
				module.Configure(this);
				return this;
			}

			public IHawkNetServerBuilder AddGameState(string id, Func<IHawkNetServerContext, IGameState> factory)
			{
				return this;
			}

			public IHawkNetServerBuilder RegisterMatchVariant(string id, Func<IGameMatchContext, IGameMatch> factory)
			{
				_registry.Register(id, factory);
				return this;
			}
		}
	}
}
