using System.Net;
using System.Text.Json;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.Core.Client;
using HawkNet.CoreUx.Client.World;
using HawkNet.Game.GameState.Match;
using Xunit;

namespace HawkNet.Tests;

public class ServerHardeningTests
{
    [Fact]
    public void Configuration_ResolvesLocalhostAndCidr()
    {
        Assert.True(ServerConfiguration.TryResolveBindAddress("localhost", out IPAddress? address));
        Assert.Equal(IPAddress.Loopback, address);
        Assert.True(IpNetwork.TryParse("192.168.4.0/24", out IpNetwork network));
        Assert.True(network.Contains(IPAddress.Parse("192.168.4.99")));
        Assert.False(network.Contains(IPAddress.Parse("192.168.5.1")));
    }

    [Fact]
	public void Configuration_AcceptsUdpAndRejectsUnknownTransportAndInvalidJson()
	{
		var udpConfig = new GameHostConfig { Networking = new NetworkingSettings { Protocol = TransportProtocol.Udp } };
		Assert.True(ServerConfiguration.Validate(udpConfig).IsValid);
		var invalidTransport = new GameHostConfig { Networking = new NetworkingSettings { Protocol = (TransportProtocol)999 } };
		Assert.Contains(ServerConfiguration.Validate(invalidTransport).Errors, error => error.Contains("not supported"));

        string path = Path.Combine(Path.GetTempPath(), $"hawknet-invalid-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ nope");
            Assert.Throws<InvalidOperationException>(() => ServerConfiguration.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExecutionScheduler_IsBoundedAndPreservesOrder()
    {
        var scheduler = new ServerExecutionScheduler(2);
        var observed = new List<long>();
        Assert.True(scheduler.TryEnqueue(Command(1)));
        Assert.True(scheduler.TryEnqueue(Command(2)));
        Assert.False(scheduler.TryEnqueue(Command(3)));

        int count = scheduler.Drain(10, (command, _) =>
        {
            observed.Add(command.Sequence);
            return Task.CompletedTask;
        });

        Assert.Equal(2, count);
        Assert.Equal([1L, 2L], observed);
        Assert.Equal(1, scheduler.Metrics.Rejected);
        Assert.Equal(2, scheduler.Metrics.HighWaterMark);
    }

    [Fact]
    public void WorldSnapshot_UpdatesEntitiesWithoutRebuildingTopologyOrSelection()
    {
        var world = new WorldViewModel();
        world.ApplySnapshot(World(1, 1, 10));
        WorldTileViewModel tile = Assert.Single(world.Tiles);
        WorldEntityViewModel entity = Assert.Single(world.Entities);
        world.SelectEntity(entity);

        world.ApplySnapshot(World(1, 2, 20));

        Assert.Same(tile, Assert.Single(world.Tiles));
        Assert.Same(entity, Assert.Single(world.Entities));
        Assert.Same(entity, world.SelectedEntity);
        Assert.Equal(20, entity.WorldX);
    }

    [Fact]
    public async Task MatchModule_CanRunWithCoreOnly()
    {
        int port = GetAvailablePort();
        var config = new GameHostConfig
        {
            Server = new ServerSettings { Host = "127.0.0.1", Port = port, LogLevel = "Error" },
            Modules = new ModuleSettings { Services = ["core"], GameStates = ["match"] },
            GameMode = new GameModeSettings { InitialStateId = "match", States = [new GameModeStateSettings { Id = "match" }] },
            Match = new MatchSettings { Phases = [new MatchPhaseSettings { PhaseId = "match", Seconds = -1 }] }
        };
        var host = HawkNetServer.Create(config);
        try
        {
            await host.BeginAsync();
            Assert.IsType<MatchState>(host.ActiveGameState);
            Assert.Null(host.Diagnostics);
            Assert.False(host.Services.TryGet<ChatService>(out _));
        }
        finally { await host.StopAsync(); }
    }

	[Fact]
	public async Task ConnectionManager_ClosesIdleUnauthenticatedConnection()
	{
		int port = GetAvailablePort();
		var config = new GameHostConfig
		{
			Server = new ServerSettings { Host = "127.0.0.1", Port = port, AllowedIPs = [] },
			Security = new SecuritySettings { HandshakeTimeoutSeconds = 1 }
		};
		var manager = new ConnectionManager(config);
		await manager.StartAsync();
		try
		{
			using var client = new System.Net.Sockets.TcpClient();
			await client.ConnectAsync(IPAddress.Loopback, port);
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
			byte[] buffer = new byte[1];
			int read = await client.GetStream().ReadAsync(buffer, timeout.Token);
			Assert.Equal(0, read);
		}
		finally { await manager.StopAsync(); }
	}

	[Fact]
	public async Task UdpClientAndServer_HandshakeSendAndReconnect()
	{
		int port = GetAvailableUdpPort();
		var serverConfig = new GameHostConfig
		{
			Server = new ServerSettings
			{
				Host = "127.0.0.1",
				Port = port,
				AllowedIPs = [],
				HeartbeatIntervalSeconds = 1,
				HeartbeatTimeoutSeconds = 5
			},
			Networking = new NetworkingSettings { Protocol = TransportProtocol.Udp },
			Diagnostics = new DiagnosticsSettings { EnableConsoleLogging = false }
		};
		GameHost host = HawkNetServer.Create(serverConfig);
		var client = new GameClient(new GameClientConfig
		{
			Server = new GameClientServerSettings { Host = "127.0.0.1", Port = port },
			Networking = new GameClientNetworkingSettings
			{
				Protocol = TransportProtocol.Udp,
				HeartbeatIntervalSeconds = 1
			}
		});
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));

		try
		{
			await host.BeginAsync();
			await client.ConnectAsync(timeout.Token);
			int playerId = Assert.IsType<int>(client.PlayerId);
			Assert.True(client.IsConnected);
			Assert.IsType<UdpClientTransport>(client.Transport);

			await client.SendHeaderOnlyPacketAsync(PacketType.HeartBeat, timeout.Token);
			await client.ReconnectAsync(timeout.Token);

			Assert.Equal(playerId, client.PlayerId);
			Assert.True(client.IsConnected);
		}
		finally
		{
			await client.DisconnectAsync();
			await host.StopAsync();
			client.Dispose();
		}
	}

	[Fact]
	public void DatagramParser_RejectsMultipleOrTruncatedFramedPackets()
	{
		byte[] packet = SignalShared.BuildHeaderOnlyPacket(PacketType.HeartBeat);
		Assert.True(SignalShared.ReadNetworkDatagram(packet).Success);

		Assert.False(SignalShared.ReadNetworkDatagram(packet[..^1]).Success);
		Assert.False(SignalShared.ReadNetworkDatagram([.. packet, 0]).Success);
	}

    private static InboundServerCommand Command(long sequence) =>
        new(sequence, sequence, 1, 4, PacketType.PlayerInput, [], DateTime.UtcNow);

    private static WorldSnapshot World(long topology, long entities, float x) => new()
    {
        RegionId = "region", TopologyRevision = topology, EntityRevision = entities,
        Tiles = [new WorldTileSnapshot { Id = "tile" }],
        Entities = [new WorldEntitySnapshot { Id = "entity", WorldX = x, ServerUpdatedAtUtc = DateTime.UtcNow }]
    };

    private static int GetAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

	private static int GetAvailableUdpPort()
	{
		using var client = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
	}
}
