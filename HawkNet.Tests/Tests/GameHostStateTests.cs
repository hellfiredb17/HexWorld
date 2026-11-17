using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HawkNet.Core;
using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class GameHostStateTests
	{
		[Fact]
		public async Task BeginAsync_EntersConfiguredActiveState()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			TestState? state = null;
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.AddGameState("test", context =>
				{
					state = new TestState(context.Events);
					return state;
				});
			});

			try
			{
				await host.BeginAsync();

				Assert.NotNull(state);
				Assert.True(state.EnterCalled);
				Assert.Same(state, host.ActiveGameState);
				Assert.Equal("test", host.ActiveGameStateId);
				Assert.IsType<JsonPayloadCodec>(host.Services.Get<IPayloadCodec>());
				Assert.Same(host.Config, host.Services.Get<IConfigService>().Root);
			}
			finally
			{
				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public void Create_AllowsCustomServerModuleToRegisterService()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.AddModule(new TestServerModule());
				builder.AddGameState("test", context => new TestState(context.Events));
			});

			try
			{
				var service = host.Services.Get<TestServerService>();

				Assert.Equal("custom-server", service.Name);
			}
			finally
			{
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task UpdateLoop_UpdatesActiveState()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port, tickRateHz: 20);
			TestState? state = null;
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.AddGameState("test", context =>
				{
					state = new TestState(context.Events);
					return state;
				});
			});

			try
			{
				await host.BeginAsync();

				await WaitForAsync(
					() => state?.UpdateCount > 0,
					TimeSpan.FromSeconds(2));

				Assert.Equal(0.05f, state!.LastDeltaTime);
			}
			finally
			{
				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task StopAsync_ExitsAndDisposesActiveState()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			TestState? state = null;
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.AddGameState("test", context =>
				{
					state = new TestState(context.Events);
					return state;
				});
			});

			await host.BeginAsync();
			var activeState = state!;

			await host.StopAsync();
			File.Delete(configPath);

			Assert.True(activeState.ExitCalled);
			Assert.True(activeState.DisposeCalled);
			Assert.Null(host.ActiveGameState);
		}

		[Fact]
		public async Task TransitionRequest_TransitionsStateAndDispatchesCompletedEvent()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port, transitionToSecond: true);
			TestState? state = null;
			TestState? secondState = null;
			GameStateTransitionCompletedEvent? completedTransition = null;
			GameStateTransitionFailedEvent? failedTransition = null;
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.Events.Subscribe<GameStateTransitionCompletedEvent>((gameEvent, _) =>
				{
					completedTransition = gameEvent;
					return Task.CompletedTask;
				});
				builder.Events.Subscribe<GameStateTransitionFailedEvent>((gameEvent, _) =>
				{
					failedTransition = gameEvent;
					return Task.CompletedTask;
				});
				builder.AddGameState("test", context =>
				{
					state = new TestState(context.Events);
					return state;
				});
				builder.AddGameState("second", context =>
				{
					secondState = new TestState(context.Events);
					return secondState;
				});
			});

			try
			{
				await host.BeginAsync();
				var firstState = state!;

				await host.RequestGameStateTransitionAsync("second", [4, 5, 6]);

				Assert.True(firstState.ExitCalled);
				Assert.True(firstState.DisposeCalled);
				Assert.NotNull(secondState);
				Assert.True(secondState.EnterCalled);
				Assert.Same(secondState, host.ActiveGameState);
				Assert.Equal("second", host.ActiveGameStateId);
				Assert.NotNull(completedTransition);
				Assert.Equal("test", completedTransition.Value.FromStateId);
				Assert.Equal("second", completedTransition.Value.ToStateId);
				Assert.Equal([4, 5, 6], secondState.EnterPayload);
				Assert.Null(failedTransition);
			}
			finally
			{
				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task TransitionRequest_MissingTransitionDispatchesFailedEventAndKeepsActiveState()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			TestState? state = null;
			GameStateTransitionCompletedEvent? completedTransition = null;
			GameStateTransitionFailedEvent? failedTransition = null;
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.Events.Subscribe<GameStateTransitionCompletedEvent>((gameEvent, _) =>
				{
					completedTransition = gameEvent;
					return Task.CompletedTask;
				});
				builder.Events.Subscribe<GameStateTransitionFailedEvent>((gameEvent, _) =>
				{
					failedTransition = gameEvent;
					return Task.CompletedTask;
				});
				builder.AddGameState("test", context =>
				{
					state = new TestState(context.Events);
					return state;
				});
			});

			try
			{
				await host.BeginAsync();
				var activeState = state!;

				await host.RequestGameStateTransitionAsync("missing");

				Assert.Same(activeState, host.ActiveGameState);
				Assert.Equal("test", host.ActiveGameStateId);
				Assert.False(activeState.ExitCalled);
				Assert.Null(completedTransition);
				Assert.NotNull(failedTransition);
				Assert.Equal("missing", failedTransition.Value.ToStateId);
				Assert.Equal("test", failedTransition.Value.FromStateId);
			}
			finally
			{
				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task TransitionRequest_SendsGameStateChangePayloadToConnectedPlayers()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port, transitionToSecond: true);
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.AddGameState("test", context => new TestState(context.Events));
				builder.AddGameState("second", context => new PayloadState(context.Events));
			});

			using var client = new TcpClient();
			try
			{
				await host.BeginAsync();
				await client.ConnectAsync(IPAddress.Loopback, port);
				var stream = client.GetStream();
				await SendPacketAsync(stream, PacketType.HandshakeRequest, Array.Empty<byte>());
				var handshakePacket = await ReadPacketAsync(stream);
				var handshake = HandshakeProtocol.DecodeResponse(handshakePacket.Payload);
				Assert.Equal("test", handshake.Runtime.InitialGameStateId);

				await host.RequestGameStateTransitionAsync("second");

				var stateChangePacket = await ReadPacketOfTypeAsync(stream, PacketType.GameStateChange);
				Assert.Equal(PacketType.GameStateChange, stateChangePacket.PacketType);
				var message = GameStateProtocol.DecodeChange(stateChangePacket.Payload);
				Assert.Equal("second", message.GameStateId);
				var payload = new JsonPayloadCodec().Decode<TestStatePayload>(message.GameStatePayload);
				Assert.Equal(handshake.PlayerId, payload.PlayerId);
				Assert.Equal("payload for player", payload.Value);
			}
			finally
			{
				client.Close();
				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		private static int GetAvailablePort()
		{
			var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}

		private static string CreateTempConfig(int port, int tickRateHz = 30, bool transitionToSecond = false)
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-state-test-{Guid.NewGuid():N}.json");
			var stateSettings = new List<GameModeStateSettings>
			{
				new GameModeStateSettings
				{
					Id = "test",
					Transitions = transitionToSecond
						? ["second"]
						: []
				}
			};

			if(transitionToSecond)
			{
				stateSettings.Add(new GameModeStateSettings
				{
					Id = "second"
				});
			}

			var config = new GameHostConfig
			{
				Server = new ServerSettings
				{
					Host = IPAddress.Loopback.ToString(),
					Port = port
				},
				Networking = new NetworkingSettings
				{
					Protocol = TransportProtocol.Tcp
				},
				Simulation = new SimulationSettings
				{
					TickRateHz = tickRateHz,
					MaxCatchUpTicks = 5
				},
				GameMode = new GameModeSettings
				{
					InitialStateId = "test",
					States = stateSettings.ToArray()
				}
			};

			File.WriteAllText(path, JsonSerializer.Serialize(config));
			return path;
		}

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

		private static async Task SendPacketAsync(NetworkStream stream, PacketType packetType, byte[] payload)
		{
			byte[] packet = SignalShared.BuildPacket(packetType, payload);
			await stream.WriteAsync(packet, 0, packet.Length);
		}

		private static async Task<ReadNetworkPacketResult> ReadPacketAsync(NetworkStream stream)
		{
			var result = await SignalShared.ReadNetworkStream(stream).WaitAsync(TimeSpan.FromSeconds(2));
			Assert.True(result.Success, result.ErrorMsg);
			return result;
		}

		private static async Task<ReadNetworkPacketResult> ReadPacketOfTypeAsync(NetworkStream stream, PacketType packetType)
		{
			var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
			while(DateTime.UtcNow < deadline)
			{
				var packet = await ReadPacketAsync(stream);
				if(packet.PacketType == packetType)
				{
					return packet;
				}
			}

			throw new TimeoutException($"Timed out waiting for packet type {packetType}.");
		}

		private sealed class TestState : GameState
		{
			public bool EnterCalled { get; private set; }
			public bool ExitCalled { get; private set; }
			public bool DisposeCalled { get; private set; }
			public int UpdateCount { get; private set; }
			public float LastDeltaTime { get; private set; }
			public byte[] EnterPayload { get; private set; } = Array.Empty<byte>();

			public TestState(IEventDispatcher eventDispatcher)
				: base(eventDispatcher)
			{
			}

			public override void Enter(byte[] transitionPayload)
			{
				EnterCalled = true;
				EnterPayload = transitionPayload;
			}

			public override void Update(float deltaTime)
			{
				LastDeltaTime = deltaTime;
				UpdateCount++;
			}

			public override void Exit()
			{
				ExitCalled = true;
			}

			public override void Dispose()
			{
				DisposeCalled = true;
				base.Dispose();
			}
		}

		private sealed class PayloadState : GameState, IGameStatePayloadProvider
		{
			public PayloadState(IEventDispatcher eventDispatcher)
				: base(eventDispatcher)
			{
			}

			public override void Enter(byte[] transitionPayload)
			{
			}

			public override void Update(float deltaTime)
			{
			}

			public override void Exit()
			{
			}

			public byte[] CreatePayloadForPlayer(int playerId, IPayloadCodec codec)
			{
				return codec.Encode(new TestStatePayload
				{
					PlayerId = playerId,
					Value = "payload for player"
				});
			}
		}

		private sealed record TestStatePayload
		{
			public int PlayerId { get; init; }
			public string Value { get; init; } = string.Empty;
		}

		private sealed class TestServerModule : IHawkNetServerModule
		{
			public void Configure(IHawkNetServerBuilder builder)
			{
				builder.AddService(new TestServerService("custom-server"));
			}
		}

		private sealed record TestServerService(string Name);
	}
}
