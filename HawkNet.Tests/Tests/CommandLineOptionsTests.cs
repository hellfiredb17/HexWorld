using HawkNet.CommandLine;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void ServerOptions_ApplyCliOverEnvironmentAndDefaults()
    {
        var environment = new Dictionary<string, string>
        {
            ["HAWKNET_SERVER_CONFIG"] = "environment.json",
            ["HAWKNET_SERVER_BIND"] = "10.0.0.4",
            ["HAWKNET_SERVER_PORT"] = "8000",
            ["HAWKNET_SERVER_PROTOCOL"] = "tcp",
            ["HAWKNET_SERVER_UDP_WINDOW"] = "16",
            ["HAWKNET_SERVER_UDP_ACK_DELAY"] = "15"
        };

        ServerCliOptions options = CliOptions.ParseServer(
            ["--config", "cli.json", "--port", "9000", "--protocol", "udp", "--udp-window", "24", "--udp-rto-initial", "300"],
            key => environment.GetValueOrDefault(key),
            "default.json");

        Assert.Equal("cli.json", options.ConfigPath);
        Assert.Equal("10.0.0.4", options.Bind);
        Assert.Equal(9000, options.Port);
        Assert.Equal(TransportProtocol.Udp, options.Protocol);
        Assert.Equal(24, options.UdpDelivery.ReliableWindowSize);
        Assert.Equal(300, options.UdpDelivery.InitialRetransmitMilliseconds);
        Assert.Equal(15, options.UdpDelivery.AcknowledgementDelayMilliseconds);
        Assert.True(options.UdpDelivery.HasOverrides);
        Assert.Equal("CLI", options.Sources["config"]);
        Assert.Equal("environment", options.Sources["bind"]);
        Assert.Equal("CLI", options.Sources["udp-window"]);
        Assert.Equal("environment", options.Sources["udp-ack-delay"]);
    }

    [Fact]
    public void ClientOptions_UseEnvironmentThenDefaults()
    {
        ClientCliOptions options = CliOptions.ParseClient(
            [],
            key => key == "HAWKNET_CLIENT_HOST" ? "server.lan" : null,
            "client-default.json");

        Assert.Equal("client-default.json", options.ConfigPath);
        Assert.Equal("server.lan", options.Host);
        Assert.Null(options.Port);
        Assert.Equal("default", options.Sources["config"]);
        Assert.Equal("environment", options.Sources["host"]);
    }

    [Theory]
    [InlineData("--port", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--protocol", "quic")]
    [InlineData("--unknown", "value")]
    public void InvalidServerOptions_AreRejected(string key, string value)
    {
        Assert.Throws<ArgumentException>(() => CliOptions.ParseServer([key, value], _ => null, "server.json"));
    }

    [Fact]
    public void UdpOverrides_ApplyWithoutReplacingUnspecifiedConfiguration()
    {
        ClientCliOptions options = CliOptions.ParseClient(
            ["--udp-retries", "9", "--udp-rto-max", "2500"],
            _ => null,
            "client.json");

        var original = new HawkNet.Core.Shared.UdpDeliverySettings
        {
            MaxDatagramBytes = 1100,
            ReliableWindowSize = 12
        };
        HawkNet.Core.Shared.UdpDeliverySettings applied = options.UdpDelivery.ApplyTo(original);

        Assert.Equal(1100, applied.MaxDatagramBytes);
        Assert.Equal(12, applied.ReliableWindowSize);
        Assert.Equal(9, applied.MaximumRetries);
        Assert.Equal(2500, applied.MaximumRetransmitMilliseconds);
    }

    [Fact]
    public void NonNumericUdpOverride_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            CliOptions.ParseClient(["--udp-window", "many"], _ => null, "client.json"));
    }

    [Fact]
    public async Task RootAndSubcommandHelp_ReturnSuccess()
    {
        var console = new TestCliConsole();

        Assert.Equal(0, await CommandLineProgram.RunAsync(["--help"], console: console));
        Assert.Equal(0, await CommandLineProgram.RunAsync(["client", "--help"], console: console));
        Assert.Contains("hawknet client", console.Output);
    }

    private sealed class TestCliConsole : ICliConsole
    {
        private readonly StringWriter _writer = new();
        public string Output => _writer.ToString();
        public void Write(string value) => _writer.Write(value);
        public void WriteLine(string value = "") => _writer.WriteLine(value);
        public ValueTask<string?> ReadLineAsync(CancellationToken token = default) => ValueTask.FromResult<string?>(null);
    }
}
