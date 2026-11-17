using System.Text.Json;
using HawkNet.Core.Server;
using HawkNet.CoreUx.Server.Services;
using HawkNet.CoreUx.Server.ViewModels;
using Xunit;

namespace HawkNet.Tests
{
	public class ServerSettingsViewModelTests
	{
		[Fact]
		public void ToConfig_MapsEditableSettingsAndPreservesUnexposedSettings()
		{
			var source = new GameHostConfig
			{
				Server = new ServerSettings
				{
					DataPath = "SavedData",
					CertificatePath = "cert.pem"
				},
				Match = new MatchSettings
				{
					VariantId = "arena"
				}
			};
			var viewModel = new ServerSettingsViewModel(source, new MockServerLauncher())
			{
				Host = "127.0.0.1",
				Port = 8888,
				MaxPlayers = 12,
				AllowedIPs = "127.0.0.1\n::1",
				Protocol = TransportProtocol.Udp,
				UdpReceiveQueueCapacity = 128,
				UdpMaxDatagramBytes = 1100,
				UdpReliableWindowSize = 24,
				UdpReorderCapacity = 20,
				UdpInitialRetransmitMilliseconds = 300,
				UdpMinimumRetransmitMilliseconds = 120,
				UdpMaximumRetransmitMilliseconds = 1800,
				UdpMaximumRetries = 8,
				UdpAcknowledgementDelayMilliseconds = 25,
				TickRateHz = 60,
				ServiceModules = "core, player_identity\nchat_service",
				GameStateModules = "chat_lobby"
			};

			GameHostConfig mapped = viewModel.ToConfig();

			Assert.Equal("127.0.0.1", mapped.Server.Host);
			Assert.Equal(8888, mapped.Server.Port);
			Assert.Equal(12, mapped.Server.MaxPlayers);
			Assert.Equal(["127.0.0.1", "::1"], mapped.Server.AllowedIPs);
			Assert.Equal(TransportProtocol.Udp, mapped.Networking.Protocol);
			Assert.Equal(128, mapped.Networking.UdpReceiveQueueCapacity);
			Assert.Equal(1100, mapped.Networking.UdpDelivery.MaxDatagramBytes);
			Assert.Equal(24, mapped.Networking.UdpDelivery.ReliableWindowSize);
			Assert.Equal(20, mapped.Networking.UdpDelivery.ReorderCapacity);
			Assert.Equal(300, mapped.Networking.UdpDelivery.InitialRetransmitMilliseconds);
			Assert.Equal(120, mapped.Networking.UdpDelivery.MinimumRetransmitMilliseconds);
			Assert.Equal(1800, mapped.Networking.UdpDelivery.MaximumRetransmitMilliseconds);
			Assert.Equal(8, mapped.Networking.UdpDelivery.MaximumRetries);
			Assert.Equal(25, mapped.Networking.UdpDelivery.AcknowledgementDelayMilliseconds);
			Assert.Equal(60, mapped.Simulation.TickRateHz);
			Assert.Equal(["core", "player_identity", "chat_service"], mapped.Modules.Services);
			Assert.Equal(["chat_lobby"], mapped.Modules.GameStates);
			Assert.Equal("SavedData", mapped.Server.DataPath);
			Assert.Equal("cert.pem", mapped.Server.CertificatePath);
			Assert.Equal("arena", mapped.Match.VariantId);
		}

		[Fact]
		public void Constructor_ExposesDropdownOptionsForCommonChoices()
		{
			var viewModel = new ServerSettingsViewModel(new GameHostConfig(), new MockServerLauncher());

			Assert.Contains("0.0.0.0", viewModel.HostOptions);
			Assert.Contains("127.0.0.1", viewModel.HostOptions);
			Assert.Contains("Information", viewModel.LogLevelOptions);
			Assert.Contains("Error", viewModel.LogLevelOptions);
			Assert.Contains(TransportProtocol.Tcp, viewModel.ProtocolOptions);
			Assert.Contains(TransportProtocol.Udp, viewModel.ProtocolOptions);
			Assert.False(viewModel.IsUdpSelected);
			viewModel.Protocol = TransportProtocol.Udp;
			Assert.True(viewModel.IsUdpSelected);
		}

		[Fact]
		public void StartServerCommand_RaisesLoadingAndStartedEvents()
		{
			var viewModel = new ServerSettingsViewModel(new GameHostConfig(), new MockServerLauncher());
			var requested = false;
			var started = false;
			viewModel.ServerStartRequested += (_, _) => requested = true;
			viewModel.ServerStarted += (_, _) => started = true;

			viewModel.StartServerCommand.Execute(null);

			Assert.True(requested);
			Assert.True(started);
		}

		[Fact]
		public void ServerConfigLoader_LoadOrDefault_LoadsExistingJson()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-coreux-config-{Guid.NewGuid():N}.json");
			var config = new GameHostConfig
			{
				Server = new ServerSettings
				{
					Host = "127.0.0.1",
					Port = 9001
				}
			};
			File.WriteAllText(path, JsonSerializer.Serialize(config));

			try
			{
				GameHostConfig loaded = ServerConfigLoader.LoadOrDefault(path);

				Assert.Equal("127.0.0.1", loaded.Server.Host);
				Assert.Equal(9001, loaded.Server.Port);
			}
			finally
			{
				File.Delete(path);
			}
		}

		[Fact]
		public void ServerConfigLoader_LoadOrDefault_FallsBackForMissingJson()
		{
			string path = Path.Combine(Path.GetTempPath(), $"missing-hawknet-coreux-config-{Guid.NewGuid():N}.json");

			GameHostConfig loaded = ServerConfigLoader.LoadOrDefault(path);

			Assert.Equal("0.0.0.0", loaded.Server.Host);
			Assert.Equal(7777, loaded.Server.Port);
		}
	}
}
