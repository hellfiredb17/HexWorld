using System.Net;
using System.Text.Json;
using HawkNet.Core;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.Game.GameState.Lobby;
using HawkNet.Game.GameState.Match;
using Xunit;

namespace HawkNet.Tests
{
	public class ServerModuleConfigTests
	{
		[Fact]
		public async Task DefaultConfig_BootsAndEntersChatLobby()
		{
			string configPath = CreateTempConfig(GetAvailablePort());
			var host = HawkNetServer.Create(configPath);

			try
			{
				await host.BeginAsync();

				Assert.Equal("chat_lobby", host.ActiveGameStateId);
				Assert.IsType<ChatLobbyState>(host.ActiveGameState);
			}
			finally
			{
				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Theory]
		[InlineData(new[] { "player_identity" }, new string[0], "", "core")]
		[InlineData(new[] { "core", "missing" }, new string[0], "", "Unknown service module 'missing'")]
		[InlineData(new[] { "core", "core" }, new string[0], "", "Duplicate service module 'core'")]
		[InlineData(new[] { "core", "chat_service" }, new string[0], "", "requires service module 'player_identity'")]
		[InlineData(new[] { "core", "chat_lobby" }, new string[0], "", "game-state module")]
		[InlineData(new[] { "core" }, new[] { "missing" }, "", "Unknown game-state module 'missing'")]
		[InlineData(new[] { "core" }, new[] { "chat_lobby", "chat_lobby" }, "", "Duplicate game-state module 'chat_lobby'")]
		[InlineData(new[] { "core" }, new[] { "chat_lobby" }, "chat_lobby", "requires module 'player_identity'")]
		[InlineData(new[] { "core" }, new[] { "chat_service" }, "", "service module")]
		[InlineData(new[] { "core" }, new string[0], "chat_lobby", "not provided")]
		public void Create_InvalidModuleConfig_FailsClearly(
			string[] serviceModules,
			string[] gameStateModules,
			string initialStateId,
			string expectedMessage)
		{
			string configPath = CreateTempConfig(
				GetAvailablePort(),
				serviceModules,
				gameStateModules,
				initialStateId);

			try
			{
				var exception = Assert.Throws<InvalidOperationException>(() => HawkNetServer.Create(configPath));

				Assert.Contains(expectedMessage, exception.Message);
			}
			finally
			{
				File.Delete(configPath);
			}
		}

		[Fact]
		public void Create_CustomServiceModuleFromConfig_RegistersService()
		{
			string configPath = CreateTempConfig(
				GetAvailablePort(),
				["core", "custom_service"],
				[],
				string.Empty);
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.RegisterServiceModule(
					"custom_service",
					() => new CustomServiceModule(),
					[typeof(CustomService)],
					"core");
			});

			try
			{
				var service = host.Services.Get<CustomService>();

				Assert.Equal("custom-service", service.Name);
			}
			finally
			{
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task BeginAsync_CustomGameStateModuleFromConfig_RegistersState()
		{
			string configPath = CreateTempConfig(
				GetAvailablePort(),
				["core"],
				["custom_state"],
				"custom_state");
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.RegisterGameStateModule(
					"custom_state",
					() => new CustomGameStateModule(),
					["custom_state"],
					"core");
			});

			try
			{
				await host.BeginAsync();

				Assert.Equal("custom_state", host.ActiveGameStateId);
				Assert.IsType<CustomState>(host.ActiveGameState);
			}
			finally
			{
				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task BeginAsync_BuiltInMatchModuleFromConfig_RegistersMatchState()
		{
			string configPath = CreateTempConfig(
				GetAvailablePort(),
				gameStateModules: ["match"],
				initialStateId: "match");
			var host = HawkNetServer.Create(configPath);

			try
			{
				await host.BeginAsync();

				var state = Assert.IsType<MatchState>(host.ActiveGameState);
				Assert.Equal("match", host.ActiveGameStateId);
				Assert.Equal("base", state.VariantId);
			}
			finally
			{
				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task ReplaceService_FlowsIntoChatLobbyPayload()
		{
			string configPath = CreateTempConfig(GetAvailablePort());
			var host = HawkNetServer.Create(configPath, builder =>
			{
				builder.ReplaceService<IPlayerIdentityService>(new CustomIdentityService());
			});

			try
			{
				await host.BeginAsync();
				var state = Assert.IsType<ChatLobbyState>(host.ActiveGameState);

				byte[] payloadBytes = state.CreatePayloadForPlayer(12, host.Services.Get<IPayloadCodec>());
				var payload = host.Services.Get<IPayloadCodec>().Decode<ChatLobbyPayload>(payloadBytes);

				var player = Assert.Single(payload.Players);
				Assert.Equal(12, player.PlayerId);
				Assert.Equal("Custom 12", player.DisplayName);
			}
			finally
			{
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

		private static string CreateTempConfig(
			int port,
			string[]? serviceModules = null,
			string[]? gameStateModules = null,
			string initialStateId = "chat_lobby")
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-module-config-{Guid.NewGuid():N}.json");
			var config = new GameHostConfig
			{
				Server = new ServerSettings
				{
					Host = IPAddress.Loopback.ToString(),
					Port = port,
					LogLevel = "Error"
				},
				GameMode = new GameModeSettings
				{
					InitialStateId = initialStateId,
					States = string.IsNullOrWhiteSpace(initialStateId)
						? []
						:
						[
							new GameModeStateSettings
							{
								Id = initialStateId
							}
						]
				},
				Modules = new ModuleSettings
				{
					Services = serviceModules ??
					[
						"core",
						"player_identity",
						"diagnostics",
						"chat_service"
					],
					GameStates = gameStateModules ??
					[
						"chat_lobby"
					]
				}
			};

			File.WriteAllText(path, JsonSerializer.Serialize(config));
			return path;
		}

		private sealed record CustomService(string Name);

		private sealed class CustomServiceModule : IHawkNetServerModule
		{
			public void Configure(IHawkNetServerBuilder builder)
			{
				builder.AddService(new CustomService("custom-service"));
			}
		}

		private sealed class CustomGameStateModule : IHawkNetServerModule
		{
			public void Configure(IHawkNetServerBuilder builder)
			{
				builder.AddGameState("custom_state", context => new CustomState(context.Events));
			}
		}

		private sealed class CustomState : GameState
		{
			public CustomState(IEventDispatcher eventDispatcher)
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
		}

		private sealed class CustomIdentityService : IPlayerIdentityService
		{
			public PlayerIdentity GetOrCreateIdentity(int playerId)
			{
				var now = DateTime.UtcNow;
				return new PlayerIdentity(playerId, GetDisplayName(playerId), now, now);
			}

			public string GetDisplayName(int playerId)
			{
				return $"Custom {playerId}";
			}

			public bool TrySetDisplayName(int playerId, string displayName, out PlayerIdentity identity)
			{
				identity = GetOrCreateIdentity(playerId);
				return true;
			}

			public bool TryGetIdentity(int playerId, out PlayerIdentity identity)
			{
				identity = GetOrCreateIdentity(playerId);
				return true;
			}
		}
	}
}
