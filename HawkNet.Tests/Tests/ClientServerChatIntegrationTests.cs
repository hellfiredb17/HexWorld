using System.Net;
using System.Text.Json;
using HawkNet.Core;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	public class ClientServerChatIntegrationTests
	{
		[Fact]
		public async Task ClientConnectsAndReceivesChatLobbyHandshake()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			var host = HawkNetServer.Create(configPath);
			ClientBundle? client = null;

			try
			{
				await host.BeginAsync();
				client = CreateClient(port);

				await client.ConnectAsync();

				Assert.NotNull(client.GameClient.PlayerId);
				Assert.Equal("chat_lobby", client.GameClient.CurrentGameStateId);
				await client.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
				var lobby = Assert.IsType<ClientChatLobbyState>(client.GameStateManager.ActiveState);
				var player = Assert.Single(lobby.Players);
				Assert.Equal(client.GameClient.PlayerId, player.PlayerId);
			}
			finally
			{
				if(client is not null)
				{
					await client.DisposeAsync();
				}

				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task ClientChatMessage_IsBroadcastBackThroughClientChatService()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			var host = HawkNetServer.Create(configPath);
			ClientBundle? client = null;

			try
			{
				await host.BeginAsync();
				client = CreateClient(port);
				await client.ConnectAsync();
				await client.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

				await client.ChatService.SendMessageAsync("hello world");

				var message = await client.NextMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));
				Assert.Equal(client.GameClient.PlayerId, message.Entry.PlayerId);
				Assert.Equal($"Player {client.GameClient.PlayerId}", message.Entry.DisplayName);
				Assert.Equal("hello world", message.Entry.Message);
				Assert.Contains(message.Entry, client.ChatService.GetHistory());
			}
			finally
			{
				if(client is not null)
				{
					await client.DisposeAsync();
				}

				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task DisplayNameChange_AffectsLaterChatBroadcast()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			var host = HawkNetServer.Create(configPath);
			ClientBundle? client = null;

			try
			{
				await host.BeginAsync();
				client = CreateClient(port);
				await client.ConnectAsync();
				await client.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

				await client.PlayerService.ChangeDisplayNameAsync("Nova");
				await client.ChatService.SendMessageAsync("named hello");

				var message = await client.NextMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));
				Assert.Equal("Nova", message.Entry.DisplayName);
				Assert.Equal("named hello", message.Entry.Message);
			}
			finally
			{
				if(client is not null)
				{
					await client.DisposeAsync();
				}

				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task DisplayNameChange_ReplacesExistingChatHistoryNamesForClients()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			var host = HawkNetServer.Create(configPath);
			ClientBundle? client = null;

			try
			{
				await host.BeginAsync();
				client = CreateClient(port);
				await client.ConnectAsync();
				await client.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

				await client.ChatService.SendMessageAsync("before rename");
				await client.NextMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));
				client.ResetHistoryWaiter();
				await client.PlayerService.ChangeDisplayNameAsync("Nova");

				var history = await client.NextHistory.Task.WaitAsync(TimeSpan.FromSeconds(2));
				var entry = Assert.Single(history.History);
				Assert.Equal("Nova", entry.DisplayName);
				Assert.Equal("before rename", entry.Message);
				Assert.Equal("Nova", Assert.Single(client.ChatService.GetHistory()).DisplayName);
			}
			finally
			{
				if(client is not null)
				{
					await client.DisposeAsync();
				}

				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task NewClientReceivesChatHistory()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			var host = HawkNetServer.Create(configPath);
			ClientBundle? firstClient = null;
			ClientBundle? secondClient = null;

			try
			{
				await host.BeginAsync();
				firstClient = CreateClient(port);
				await firstClient.ConnectAsync();
				await firstClient.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

				await firstClient.ChatService.SendMessageAsync("history item");
				await firstClient.NextMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));

				secondClient = CreateClient(port);
				await secondClient.ConnectAsync();
				await secondClient.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

				var history = await secondClient.NextHistory.Task.WaitAsync(TimeSpan.FromSeconds(2));
				var entry = Assert.Single(history.History);
				Assert.Equal("history item", entry.Message);
				Assert.Equal(firstClient.GameClient.PlayerId, entry.PlayerId);
			}
			finally
			{
				if(secondClient is not null)
				{
					await secondClient.DisposeAsync();
				}

				if(firstClient is not null)
				{
					await firstClient.DisposeAsync();
				}

				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task ClientReadyRequest_IsBroadcastBackThroughChatLobbyState()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			var host = HawkNetServer.Create(configPath);
			ClientBundle? client = null;

			try
			{
				await host.BeginAsync();
				client = CreateClient(port);
				await client.ConnectAsync();
				await client.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
				var lobby = Assert.IsType<ClientChatLobbyState>(client.GameStateManager.ActiveState);
				byte[] readyPayload = [4, 5, 6];

				await client.WaitForOptionalReadyStateAsync();
				client.ResetReadyStateWaiter();
				await lobby.SetReadyAsync(true, readyPayload);

				var readyState = await client.NextReadyState.Task.WaitAsync(TimeSpan.FromSeconds(2));
				var readyPlayer = Assert.Single(readyState.ReadyState.Players, player => player.PlayerId == client.GameClient.PlayerId);
				Assert.True(readyPlayer.IsReady);
				Assert.Equal(readyPayload, readyPlayer.ReadyPayload);
				Assert.True(lobby.IsLocalPlayerReady);
				Assert.Equal(1, lobby.ReadyPlayerCount);
			}
			finally
			{
				if(client is not null)
				{
					await client.DisposeAsync();
				}

				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task ClientLobbyPlayers_UpdateWhenAnotherClientJoins()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			var host = HawkNetServer.Create(configPath);
			ClientBundle? firstClient = null;
			ClientBundle? secondClient = null;

			try
			{
				await host.BeginAsync();
				firstClient = CreateClient(port);
				await firstClient.ConnectAsync();
				await firstClient.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
				var firstLobby = Assert.IsType<ClientChatLobbyState>(firstClient.GameStateManager.ActiveState);

				firstClient.ResetPlayerStateWaiter();
				secondClient = CreateClient(port);
				await secondClient.ConnectAsync();
				await secondClient.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

				await firstClient.NextPlayerState.Task.WaitAsync(TimeSpan.FromSeconds(2));
				Assert.Contains(firstLobby.Players, player => player.PlayerId == firstClient.GameClient.PlayerId);
				Assert.Contains(firstLobby.Players, player => player.PlayerId == secondClient.GameClient.PlayerId);
			}
			finally
			{
				if(secondClient is not null)
				{
					await secondClient.DisposeAsync();
				}

				if(firstClient is not null)
				{
					await firstClient.DisposeAsync();
				}

				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task ReadyLobby_TransitionsClientThroughMatchPhasesAndBackToLobby()
		{
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port, enableMatchFlow: true);
			var host = HawkNetServer.Create(configPath);
			ClientBundle? client = null;

			try
			{
				await host.BeginAsync();
				client = CreateClient(port);
				await client.ConnectAsync();
				await client.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
				var lobby = Assert.IsType<ClientChatLobbyState>(client.GameStateManager.ActiveState);
				byte[] readyPayload = [8, 6, 7, 5];

				await client.WaitForOptionalReadyStateAsync();
				await lobby.SetReadyAsync(true, readyPayload);

				await WaitForAsync(
					() => client.GameStateManager.ActiveState is ClientMatchState,
					TimeSpan.FromSeconds(3));
				var phaseChange = await client.NextMatchPhaseChange.Task.WaitAsync(TimeSpan.FromSeconds(2));
				var match = Assert.IsType<ClientMatchState>(client.GameStateManager.ActiveState);
				Assert.Equal("match", client.GameStateManager.ActiveStateId);
				Assert.Equal("base", match.VariantId);
				Assert.Equal("pre_match", phaseChange.PhaseChange.PhaseId);
				Assert.Contains(
					match.Players,
					player => player.PlayerId == client.GameClient.PlayerId
						&& player.IsConnected
						&& player.ReadyPayload.SequenceEqual(readyPayload));

				await WaitForAsync(
					() => client.GameStateManager.ActiveState is ClientChatLobbyState,
					TimeSpan.FromSeconds(3));
				Assert.Equal("chat_lobby", client.GameStateManager.ActiveStateId);
				Assert.IsType<ClientChatLobbyState>(client.GameStateManager.ActiveState);
				Assert.Equal("chat_lobby", host.ActiveGameStateId);
			}
			finally
			{
				if(client is not null)
				{
					await client.DisposeAsync();
				}

				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		private static ClientBundle CreateClient(int port)
		{
			var client = HawkNetClient.Create(new GameClientConfig
			{
				Server = new GameClientServerSettings
				{
					Host = IPAddress.Loopback.ToString(),
					Port = port
				},
				Networking = new GameClientNetworkingSettings
				{
					HeartbeatIntervalSeconds = 1
				}
			});
			return new ClientBundle(client);
		}

		private static int GetAvailablePort()
		{
			var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}

		private static string CreateTempConfig(int port, bool enableMatchFlow = false)
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-chat-integration-{Guid.NewGuid():N}.json");
			var config = new GameHostConfig
			{
				Server = new ServerSettings
				{
					Host = IPAddress.Loopback.ToString(),
					Port = port,
					HeartbeatIntervalSeconds = 1,
					HeartbeatTimeoutSeconds = 5,
					SessionTimeoutSeconds = 5,
					LogLevel = "Error"
				},
				Networking = new NetworkingSettings
				{
					Protocol = TransportProtocol.Tcp
				},
				Simulation = new SimulationSettings
				{
					TickRateHz = 10,
					MaxCatchUpTicks = 2
				},
				Chat = new ChatSettings
				{
					MaxHistoryMessages = 10,
					MaxMessageLength = 512,
					MaxMessageBytes = 2048,
					MaxMessagesPerWindow = 10,
					MessageRateWindowSeconds = 10
				},
				PlayerIdentity = new PlayerIdentitySettings
				{
					MaxDisplayNameLength = 24,
					DefaultDisplayNameFormat = "Player {0}"
				},
				ChatLobby = new ChatLobbySettings
				{
					ReadyThresholdPercent = 100f,
					TransitionCountdownSeconds = enableMatchFlow ? 0.05f : 5f,
					ReadyTransitionStateId = enableMatchFlow ? "match" : string.Empty,
					MaxReadyPayloadBytes = 4096
				},
				Match = new MatchSettings
				{
					VariantId = "base",
					Phases = enableMatchFlow
						?
						[
							new MatchPhaseSettings
							{
								PhaseId = "pre_match",
								Seconds = 0.05f
							},
							new MatchPhaseSettings
							{
								PhaseId = "match",
								Seconds = 0.05f
							},
							new MatchPhaseSettings
							{
								PhaseId = "post_match",
								Seconds = 0.05f
							}
						]
						: new MatchSettings().Phases,
					CompletedTransitionStateId = enableMatchFlow ? "chat_lobby" : string.Empty
				},
				GameMode = new GameModeSettings
				{
					InitialStateId = "chat_lobby",
					States =
						enableMatchFlow
							?
							[
								new GameModeStateSettings
								{
									Id = "chat_lobby",
									Transitions = ["match"]
								},
								new GameModeStateSettings
								{
									Id = "match",
									Transitions = ["chat_lobby"]
								}
							]
							:
							[
								new GameModeStateSettings
								{
									Id = "chat_lobby"
								}
							]
				},
				Security = new SecuritySettings
				{
					MaxInvalidPacketsPerConnection = 5,
					MaxPacketsPerWindow = 300,
					PacketRateWindowSeconds = 10
				},
				Modules = new ModuleSettings
				{
					Services =
					[
						"core",
						"player_identity",
						"diagnostics",
						"chat_service"
					],
					GameStates = enableMatchFlow
						? ["chat_lobby", "match"]
						: ["chat_lobby"]
				}
			};

			File.WriteAllText(path, JsonSerializer.Serialize(config));
			return path;
		}

		private sealed class ClientBundle : IAsyncDisposable
		{
			private readonly HawkNetClient _client;

			public GameClient GameClient { get; }
			public ClientChatService ChatService => _client.ChatService;
			public ClientPlayerService PlayerService => _client.PlayerService;
			public ClientGameStateManager GameStateManager => _client.GameStateManager;
			public TaskCompletionSource<ClientConnectedEvent> Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
			public TaskCompletionSource<ClientChatMessageReceivedEvent> NextMessage { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
			public TaskCompletionSource<ClientChatHistoryReceivedEvent> NextHistory { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
			public TaskCompletionSource<ClientChatLobbyReadyStateReceivedEvent> NextReadyState { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
			public TaskCompletionSource<ClientChatLobbyPlayerStateReceivedEvent> NextPlayerState { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
			public TaskCompletionSource<ClientMatchPhaseChangeReceivedEvent> NextMatchPhaseChange { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

			public ClientBundle(HawkNetClient client)
			{
				_client = client;
				GameClient = client.GameClient;
				client.Events.Subscribe<ClientConnectedEvent>((gameEvent, _) =>
				{
					Connected.TrySetResult(gameEvent);
					return Task.CompletedTask;
				});
				client.Events.Subscribe<ClientChatMessageReceivedEvent>((gameEvent, _) =>
				{
					NextMessage.TrySetResult(gameEvent);
					return Task.CompletedTask;
				});
				client.Events.Subscribe<ClientChatHistoryReceivedEvent>((gameEvent, _) =>
				{
					NextHistory.TrySetResult(gameEvent);
					return Task.CompletedTask;
				});
				client.Events.Subscribe<ClientChatLobbyReadyStateReceivedEvent>((gameEvent, _) =>
				{
					NextReadyState.TrySetResult(gameEvent);
					return Task.CompletedTask;
				});
				client.Events.Subscribe<ClientChatLobbyPlayerStateReceivedEvent>((gameEvent, _) =>
				{
					NextPlayerState.TrySetResult(gameEvent);
					return Task.CompletedTask;
				});
				client.Events.Subscribe<ClientMatchPhaseChangeReceivedEvent>((gameEvent, _) =>
				{
					NextMatchPhaseChange.TrySetResult(gameEvent);
					return Task.CompletedTask;
				});
			}

			public Task ConnectAsync()
			{
				return _client.ConnectAsync();
			}

			public void ResetReadyStateWaiter()
			{
				NextReadyState = new TaskCompletionSource<ClientChatLobbyReadyStateReceivedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
			}

			public void ResetHistoryWaiter()
			{
				NextHistory = new TaskCompletionSource<ClientChatHistoryReceivedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
			}

			public void ResetPlayerStateWaiter()
			{
				NextPlayerState = new TaskCompletionSource<ClientChatLobbyPlayerStateReceivedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
			}

			public async Task WaitForOptionalReadyStateAsync()
			{
				try
				{
					await NextReadyState.Task.WaitAsync(TimeSpan.FromMilliseconds(250));
				}
				catch(TimeoutException)
				{
				}
			}

			public async ValueTask DisposeAsync()
			{
				await _client.DisposeAsync();
			}
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
	}
}
