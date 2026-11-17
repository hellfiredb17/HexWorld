using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Server.Services;
using HawkNet.Desktop.Services;
using Xunit;

namespace HawkNet.Tests
{
	public class DesktopServerLauncherTests
	{
		[Fact]
		public async Task StartAsync_WritesRuntimeConfigAndPreventsSecondStart()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-desktop-runtime-{Guid.NewGuid():N}.json");
			var launcher = new DesktopServerLauncher(path);
			var config = CreateConfig(GetAvailablePort());

			try
			{
				await launcher.StartAsync(config);

				Assert.True(File.Exists(path));
				var options = new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				};
				options.Converters.Add(new JsonStringEnumConverter());
				var written = JsonSerializer.Deserialize<GameHostConfig>(File.ReadAllText(path), options);
				Assert.NotNull(written);
				Assert.Equal(config.Server.Port, written.Server.Port);

				var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => launcher.StartAsync(config));
				Assert.Contains("already running", exception.Message);
			}
			finally
			{
				if (launcher.ActiveHost is not null)
				{
					await launcher.ActiveHost.StopAsync();
				}

				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public async Task StopAsync_StopsActiveHostAndAllowsRestart()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-desktop-runtime-{Guid.NewGuid():N}.json");
			var launcher = new DesktopServerLauncher(path);
			var config = CreateConfig(GetAvailablePort());

			try
			{
				await launcher.StartAsync(config);
				Assert.NotNull(launcher.ActiveHost);

				await launcher.StopAsync();

				Assert.Null(launcher.ActiveHost);

				config = CreateConfig(GetAvailablePort());
				await launcher.StartAsync(config);
				Assert.NotNull(launcher.ActiveHost);
			}
			finally
			{
				if (launcher.ActiveHost is not null)
				{
					await launcher.ActiveHost.StopAsync();
				}

				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public async Task StartAsync_FailsBeforeWritingConfigWhenPortIsInUse()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-desktop-runtime-{Guid.NewGuid():N}.json");
			var launcher = new DesktopServerLauncher(path);
			var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;

			try
			{
				var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => launcher.StartAsync(CreateConfig(port)));

				Assert.Contains("already in use", exception.Message);
				Assert.False(File.Exists(path));
			}
			finally
			{
				listener.Stop();

				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public async Task ChatLobbyRuntime_LoadsSnapshotFromRunningHost()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-desktop-runtime-{Guid.NewGuid():N}.json");
			var launcher = new DesktopServerLauncher(path);
			var config = CreateConfig(GetAvailablePort());

			try
			{
				await launcher.StartAsync(config);
				using var runtime = new DesktopChatLobbyRuntime(launcher.ActiveHost, RunInlineAsync);

				ChatLobbySnapshot snapshot = await runtime.LoadSnapshotAsync();

				Assert.Equal(config.Server.MaxPlayers, snapshot.MaxPlayers);
				Assert.Empty(snapshot.Players);
				Assert.Empty(snapshot.ChatHistory);
			}
			finally
			{
				if (launcher.ActiveHost is not null)
				{
					await launcher.ActiveHost.StopAsync();
				}

				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public async Task ChatLobbyRuntime_SendMessageAsync_RecordsServerChatMessage()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-desktop-runtime-{Guid.NewGuid():N}.json");
			var launcher = new DesktopServerLauncher(path);
			var config = CreateConfig(GetAvailablePort());

			try
			{
				await launcher.StartAsync(config);
				using var runtime = new DesktopChatLobbyRuntime(launcher.ActiveHost, RunInlineAsync);

				await runtime.SendMessageAsync("admin hello");
				ChatLobbySnapshot snapshot = await runtime.LoadSnapshotAsync();

				var message = Assert.Single(snapshot.ChatHistory);
				Assert.Equal(0, message.PlayerId);
				Assert.Equal("Admin", message.DisplayName);
				Assert.Equal("admin hello", message.Message);
			}
			finally
			{
				if (launcher.ActiveHost is not null)
				{
					await launcher.ActiveHost.StopAsync();
				}

				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public async Task ChatLobbyRuntime_StartMatchAsync_RequestsRealServerTransition()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-desktop-runtime-{Guid.NewGuid():N}.json");
			var launcher = new DesktopServerLauncher(path);
			var config = CreateConfig(GetAvailablePort(), includeMatch: true);

			try
			{
				await launcher.StartAsync(config);
				using var runtime = new DesktopChatLobbyRuntime(launcher.ActiveHost, RunInlineAsync);

				await runtime.StartMatchAsync();

				Assert.Equal("match", launcher.ActiveHost?.ActiveGameStateId);
			}
			finally
			{
				if (launcher.ActiveHost is not null)
				{
					await launcher.ActiveHost.StopAsync();
				}

				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public async Task ChatLobbyReadyCountdown_RequestsRealServerTransition()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-desktop-runtime-{Guid.NewGuid():N}.json");
			var launcher = new DesktopServerLauncher(path);
			var config = CreateConfig(GetAvailablePort(), includeMatch: true) with
			{
				ChatLobby = new ChatLobbySettings
				{
					ReadyThresholdPercent = 100f,
					TransitionCountdownSeconds = 0.1f,
					ReadyTransitionStateId = "match"
				},
				Simulation = new SimulationSettings
				{
					TickRateHz = 30,
					MaxCatchUpTicks = 5
				}
			};
			var codec = new JsonPayloadCodec();

			try
			{
				await launcher.StartAsync(config);
				using var client = new TcpClient();
				await client.ConnectAsync(IPAddress.Loopback, config.Server.Port);
				NetworkStream stream = client.GetStream();
				await SendPacketAsync(stream, PacketType.HandshakeRequest, Array.Empty<byte>());
				ReadNetworkPacketResult handshakePacket = await ReadPacketOfTypeAsync(stream, PacketType.HandshakeResponse);
				HandshakeResponse handshake = HandshakeProtocol.DecodeResponse(handshakePacket.Payload, codec);

				await SendPacketAsync(
					stream,
					PacketType.ChatLobbyReadyRequest,
					codec.Encode(new ChatLobbyReadyRequest
					{
						IsReady = true
					}));

				await WaitForAsync(
					() => string.Equals(launcher.ActiveHost?.ActiveGameStateId, "match", StringComparison.OrdinalIgnoreCase),
					TimeSpan.FromSeconds(2));

				Assert.Equal("match", launcher.ActiveHost?.ActiveGameStateId);
				Assert.True(handshake.PlayerId >= config.Server.NextPlayerIdStart);
			}
			finally
			{
				if (launcher.ActiveHost is not null)
				{
					await launcher.ActiveHost.StopAsync();
				}

				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public async Task ServerMatchRuntime_LoadsSnapshotAndStopsMatchThroughRealTransition()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-desktop-runtime-{Guid.NewGuid():N}.json");
			var launcher = new DesktopServerLauncher(path);
			var config = CreateConfig(GetAvailablePort(), includeMatch: true);

			try
			{
				await launcher.StartAsync(config);
				using(var lobbyRuntime = new DesktopChatLobbyRuntime(launcher.ActiveHost, RunInlineAsync))
				{
					await lobbyRuntime.StartMatchAsync();
				}

				using var matchRuntime = new DesktopServerMatchRuntime(launcher.ActiveHost, RunInlineAsync);
				ServerMatchSnapshot snapshot = await matchRuntime.LoadSnapshotAsync();

				Assert.Equal("match", launcher.ActiveHost?.ActiveGameStateId);
				Assert.Equal("base", snapshot.Payload.VariantId);
				Assert.Equal(config.Server.MaxPlayers, snapshot.MaxPlayers);

				await matchRuntime.StopMatchAsync();

				Assert.Equal("chat_lobby", launcher.ActiveHost?.ActiveGameStateId);
			}
			finally
			{
				if (launcher.ActiveHost is not null)
				{
					await launcher.ActiveHost.StopAsync();
				}

				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		private static GameHostConfig CreateConfig(int port, bool includeMatch = false)
		{
			return new GameHostConfig
			{
				Server = new ServerSettings
				{
					Host = IPAddress.Loopback.ToString(),
					Port = port,
					MaxPlayers = 8,
					LogLevel = "Error"
				},
				GameMode = includeMatch
					? new GameModeSettings
					{
						InitialStateId = "chat_lobby",
						States =
						[
							new GameModeStateSettings
							{
								Id = "chat_lobby",
								Transitions = [ "match" ]
							},
							new GameModeStateSettings
							{
								Id = "match",
								Transitions = [ "chat_lobby" ]
							}
						]
					}
					: new GameModeSettings(),
				Modules = includeMatch
					? new ModuleSettings
					{
						Services = [ "core", "player_identity", "diagnostics", "chat_service" ],
						GameStates = [ "chat_lobby", "match" ]
					}
					: new ModuleSettings()
			};
		}

		private static Task RunInlineAsync(Action action)
		{
			action();
			return Task.CompletedTask;
		}

		private static int GetAvailablePort()
		{
			var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}

		private static async Task SendPacketAsync(NetworkStream stream, PacketType packetType, byte[] payload)
		{
			byte[] packet = SignalShared.BuildPacket(packetType, payload);
			await stream.WriteAsync(packet, 0, packet.Length);
		}

		private static async Task<ReadNetworkPacketResult> ReadPacketOfTypeAsync(NetworkStream stream, PacketType packetType)
		{
			DateTime deadline = DateTime.UtcNow.AddSeconds(2);
			while (DateTime.UtcNow < deadline)
			{
				ReadNetworkPacketResult packet = await SignalShared.ReadNetworkStream(stream).WaitAsync(TimeSpan.FromSeconds(2));
				Assert.True(packet.Success, packet.ErrorMsg);
				if (packet.PacketType == packetType)
				{
					return packet;
				}
			}

			throw new TimeoutException($"Timed out waiting for packet type {packetType}.");
		}

		private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
		{
			DateTime deadline = DateTime.UtcNow.Add(timeout);
			while (DateTime.UtcNow < deadline)
			{
				if (condition())
				{
					return;
				}

				await Task.Delay(10);
			}

			throw new TimeoutException("Timed out waiting for condition.");
		}

	}
}
