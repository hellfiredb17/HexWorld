using System.Net;
using System.Text.Json;
using HawkNet.Core;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	[Collection(ServicesCollection.Name)]
	public class HawkNetClientTests
	{
		[Fact]
		public async Task ConnectAsync_InitializesServicesConnectsAndEntersChatLobby()
		{
			await Services.ShutdownAsync();
			int port = GetAvailablePort();
			string configPath = CreateTempConfig(port);
			var host = HawkNetServer.Create(configPath);
			HawkNetClient? client = null;

			try
			{
				await host.BeginAsync();
				client = HawkNetClient.Create(new GameClientConfig
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

				await client.ConnectAsync();

				Assert.Equal(ServiceState.Ready, client.State);
				Assert.Same(client.GameClient, client.Services.Get<GameClient>());
				Assert.IsType<JsonPayloadCodec>(client.Services.Get<IPayloadCodec>());
				Assert.IsType<TcpClientTransport>(client.Services.Get<IClientTransport>());
				Assert.Same(client.ChatService, client.Services.Get<ClientChatService>());
				Assert.Same(client.PlayerService, client.Services.Get<ClientPlayerService>());
				Assert.Same(client.GameStateManager, client.Services.Get<ClientGameStateManager>());
				Assert.Equal("chat_lobby", client.GameStateManager.ActiveStateId);
				var lobby = Assert.IsType<ClientChatLobbyState>(client.GameStateManager.ActiveState);
				Assert.True(lobby.IsActive);
				Assert.NotNull(lobby.CurrentPlayer);
				var player = Assert.Single(lobby.Players);
				Assert.Equal(client.GameClient.PlayerId, player.PlayerId);
				Assert.Equal($"Player {client.GameClient.PlayerId}", player.DisplayName);
				Assert.Empty(lobby.InitialChatHistory);
			}
			finally
			{
				if(client is not null)
				{
					await client.DisposeAsync();
				}
				else
				{
					await Services.ShutdownAsync();
				}

				await host.StopAsync();
				File.Delete(configPath);
			}
		}

		[Fact]
		public async Task Create_AllowsCustomClientModuleToRegisterService()
		{
			await using var client = HawkNetClient.Create(new GameClientConfig(), builder =>
			{
				builder.AddModule(new TestClientModule());
			});

			var service = client.Services.Get<TestClientService>();

			Assert.Equal("custom-client", service.Name);
		}

		[Fact]
		public async Task ConnectAsync_FailsClearlyWhenServerRequestsUnknownServiceModule()
		{
			int port = GetAvailablePort();
			using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
			listener.Start();
			Task serverTask = SendSingleHandshakeAsync(listener, new ClientRuntimeManifest
			{
				Services = ["missing_service"]
			});

			await using var client = CreateClient(port);
			var compatibilityFailure = CaptureCompatibilityFailure(client);

			var error = await Assert.ThrowsAsync<HawkNetClientCompatibilityException>(() => client.ConnectAsync());
			var gameEvent = await compatibilityFailure.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(ClientCompatibilityFailureReason.UnknownServiceModule, error.Reason);
			Assert.Equal("missing_service", error.ModuleId);
			Assert.Null(error.DependencyId);
			Assert.Equal(error.Reason, gameEvent.Reason);
			Assert.Equal(error.ModuleId, gameEvent.ModuleId);
			Assert.Contains("Unknown client service module 'missing_service'", error.Message);
			Assert.Same(error.Manifest, gameEvent.Manifest);
			await serverTask;
		}

		[Fact]
		public async Task ConnectAsync_FailsClearlyWhenServerRequestsUnknownGameStateModule()
		{
			int port = GetAvailablePort();
			using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
			listener.Start();
			Task serverTask = SendSingleHandshakeAsync(listener, new ClientRuntimeManifest
			{
				GameStates = ["missing_state"],
				InitialGameStateId = "missing_state"
			});

			await using var client = CreateClient(port);
			var compatibilityFailure = CaptureCompatibilityFailure(client);

			var error = await Assert.ThrowsAsync<HawkNetClientCompatibilityException>(() => client.ConnectAsync());
			var gameEvent = await compatibilityFailure.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(ClientCompatibilityFailureReason.UnknownGameStateModule, error.Reason);
			Assert.Equal("missing_state", error.ModuleId);
			Assert.Equal(error.Reason, gameEvent.Reason);
			Assert.Equal(error.ModuleId, gameEvent.ModuleId);
			Assert.Contains("Unknown client game-state module 'missing_state'", error.Message);
			await serverTask;
		}

		[Fact]
		public async Task ConnectAsync_FailsClearlyWhenClientServiceDependencyIsMissing()
		{
			int port = GetAvailablePort();
			using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
			listener.Start();
			Task serverTask = SendSingleHandshakeAsync(listener, new ClientRuntimeManifest
			{
				Services = ["needs_missing"]
			});

			await using var client = CreateClient(port, builder =>
			{
				builder.RegisterServiceModule(
					"needs_missing",
					() => new TestClientModule(),
					[
						typeof(TestClientService)
					],
					"missing_service");
			});

			var compatibilityFailure = CaptureCompatibilityFailure(client);
			var error = await Assert.ThrowsAsync<HawkNetClientCompatibilityException>(() => client.ConnectAsync());
			var gameEvent = await compatibilityFailure.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(ClientCompatibilityFailureReason.MissingModuleDependency, error.Reason);
			Assert.Equal("needs_missing", error.ModuleId);
			Assert.Equal("missing_service", error.DependencyId);
			Assert.Equal(error.Reason, gameEvent.Reason);
			Assert.Equal(error.ModuleId, gameEvent.ModuleId);
			Assert.Equal(error.DependencyId, gameEvent.DependencyId);
			Assert.Contains("Client service module 'needs_missing' requires service module 'missing_service'", error.Message);
			await serverTask;
		}

		[Fact]
		public async Task ConnectAsync_FailsClearlyWhenManifestVersionIsUnsupported()
		{
			int port = GetAvailablePort();
			using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
			listener.Start();
			Task serverTask = SendSingleHandshakeAsync(listener, new ClientRuntimeManifest
			{
				ManifestVersion = HandshakeProtocol.CurrentRuntimeManifestVersion + 1
			});

			await using var client = CreateClient(port);
			var compatibilityFailure = CaptureCompatibilityFailure(client);

			var error = await Assert.ThrowsAsync<HawkNetClientCompatibilityException>(() => client.ConnectAsync());
			var gameEvent = await compatibilityFailure.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(ClientCompatibilityFailureReason.UnsupportedManifestVersion, error.Reason);
			Assert.Equal(error.Reason, gameEvent.Reason);
			Assert.Contains("Unsupported client runtime manifest version", error.Message);
			await serverTask;
		}

		[Fact]
		public async Task ConnectAsync_FailsClearlyWhenProtocolVersionIsUnsupported()
		{
			int port = GetAvailablePort();
			using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
			listener.Start();
			Task serverTask = SendSingleHandshakeAsync(listener, new ClientRuntimeManifest
			{
				ProtocolVersion = "999"
			});

			await using var client = CreateClient(port);
			var compatibilityFailure = CaptureCompatibilityFailure(client);

			var error = await Assert.ThrowsAsync<HawkNetClientCompatibilityException>(() => client.ConnectAsync());
			var gameEvent = await compatibilityFailure.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(ClientCompatibilityFailureReason.UnsupportedProtocolVersion, error.Reason);
			Assert.Equal(error.Reason, gameEvent.Reason);
			Assert.Contains("Unsupported HawkNet protocol version", error.Message);
			await serverTask;
		}

		[Fact]
		public async Task ConnectAsync_FailsClearlyWhenServiceModuleVersionIsUnsupported()
		{
			int port = GetAvailablePort();
			using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
			listener.Start();
			Task serverTask = SendSingleHandshakeAsync(listener, new ClientRuntimeManifest
			{
				ServiceModules =
				[
					new RuntimeModuleRequirement
					{
						Id = "chat_service",
						Version = "999"
					}
				]
			});

			await using var client = CreateClient(port);
			var compatibilityFailure = CaptureCompatibilityFailure(client);

			var error = await Assert.ThrowsAsync<HawkNetClientCompatibilityException>(() => client.ConnectAsync());
			var gameEvent = await compatibilityFailure.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(ClientCompatibilityFailureReason.UnsupportedServiceModuleVersion, error.Reason);
			Assert.Equal("chat_service", error.ModuleId);
			Assert.Equal("999", error.RequestedVersion);
			Assert.Equal(HandshakeProtocol.CurrentModuleVersion, error.SupportedVersion);
			Assert.Equal(error.Reason, gameEvent.Reason);
			Assert.Equal(error.RequestedVersion, gameEvent.RequestedVersion);
			Assert.Equal(error.SupportedVersion, gameEvent.SupportedVersion);
			await serverTask;
		}

		[Fact]
		public async Task ConnectAsync_FailsClearlyWhenGameStateModuleVersionIsUnsupported()
		{
			int port = GetAvailablePort();
			using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
			listener.Start();
			Task serverTask = SendSingleHandshakeAsync(listener, new ClientRuntimeManifest
			{
				ServiceModules =
				[
					new RuntimeModuleRequirement
					{
						Id = "player_identity"
					},
					new RuntimeModuleRequirement
					{
						Id = "chat_service"
					}
				],
				GameStateModules =
				[
					new RuntimeModuleRequirement
					{
						Id = "chat_lobby",
						Version = "999"
					}
				],
				InitialGameStateId = "chat_lobby"
			});

			await using var client = CreateClient(port);
			var compatibilityFailure = CaptureCompatibilityFailure(client);

			var error = await Assert.ThrowsAsync<HawkNetClientCompatibilityException>(() => client.ConnectAsync());
			var gameEvent = await compatibilityFailure.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(ClientCompatibilityFailureReason.UnsupportedGameStateModuleVersion, error.Reason);
			Assert.Equal("chat_lobby", error.ModuleId);
			Assert.Equal("999", error.RequestedVersion);
			Assert.Equal(HandshakeProtocol.CurrentModuleVersion, error.SupportedVersion);
			Assert.Equal(error.Reason, gameEvent.Reason);
			Assert.Equal(error.RequestedVersion, gameEvent.RequestedVersion);
			Assert.Equal(error.SupportedVersion, gameEvent.SupportedVersion);
			await serverTask;
		}

		private static int GetAvailablePort()
		{
			var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}

		private static HawkNetClient CreateClient(int port, Action<IHawkNetClientBuilder>? configure = null)
		{
			return HawkNetClient.Create(new GameClientConfig
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
			}, configure);
		}

		private static TaskCompletionSource<ClientCompatibilityFailedEvent> CaptureCompatibilityFailure(HawkNetClient client)
		{
			var source = new TaskCompletionSource<ClientCompatibilityFailedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
			client.Events.Subscribe<ClientCompatibilityFailedEvent>((gameEvent, _) =>
			{
				source.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});

			return source;
		}

		private static async Task SendSingleHandshakeAsync(
			System.Net.Sockets.TcpListener listener,
			ClientRuntimeManifest manifest)
		{
			using var tcpClient = await listener.AcceptTcpClientAsync();
			var stream = tcpClient.GetStream();
			var request = await SignalShared.ReadNetworkStream(stream).WaitAsync(TimeSpan.FromSeconds(2));
			Assert.True(request.Success, request.ErrorMsg);
			Assert.Equal(PacketType.HandshakeRequest, request.PacketType);

			byte[] payload = HandshakeProtocol.EncodeResponse(new HandshakeResponse
			{
				PlayerId = 12,
				ReconnectToken = CreateReconnectToken(5),
				Runtime = manifest
			});
			byte[] packet = SignalShared.BuildPacket(PacketType.HandshakeResponse, payload);
			await stream.WriteAsync(packet, 0, packet.Length);
		}

		private static byte[] CreateReconnectToken(byte seed)
		{
			return Enumerable.Range(0, ConnectionManager.ReconnectTokenSize)
				.Select(index => (byte)(seed + index))
				.ToArray();
		}

		private static string CreateTempConfig(int port)
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-client-app-{Guid.NewGuid():N}.json");
			var config = new GameHostConfig
			{
				Server = new ServerSettings
				{
					Host = IPAddress.Loopback.ToString(),
					Port = port,
					LogLevel = "Error",
					HeartbeatIntervalSeconds = 1,
					HeartbeatTimeoutSeconds = 5
				},
				Networking = new NetworkingSettings
				{
					Protocol = TransportProtocol.Tcp
				},
				GameMode = new GameModeSettings
				{
					InitialStateId = "chat_lobby",
					States =
					[
						new GameModeStateSettings
						{
							Id = "chat_lobby"
						}
					]
				}
			};

			File.WriteAllText(path, JsonSerializer.Serialize(config));
			return path;
		}

		private sealed class TestClientModule : IHawkNetClientModule
		{
			public void Configure(IHawkNetClientBuilder builder)
			{
				builder.AddService(new TestClientService("custom-client"));
			}
		}

		private sealed record TestClientService(string Name);
	}
}
