using System.Net;
using System.Net.Sockets;
using HawkNet.CommandLine;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests;

public sealed class CommandLineSmokeTests
{
    [Theory]
    [InlineData(TransportProtocol.Tcp)]
    [InlineData(TransportProtocol.Udp)]
    public async Task GenericServerAndClient_ConnectAndStopCleanly(TransportProtocol protocol)
    {
        int port = GetAvailablePort(protocol);
        string root = FindRepositoryRoot();
        string serverConfig = Path.Combine(root, "HawkNet.CommandLine", "Config", "game-server.json");
        string clientConfig = Path.Combine(root, "HawkNet.CommandLine", "Config", "game-client.json");
        var serverConsole = new BufferedCliConsole();
        var clientConsole = new BufferedCliConsole(endInput: true);
        using var serverCancellation = new CancellationTokenSource();
        string[] udpOverrides = protocol == TransportProtocol.Udp
            ? ["--udp-window", "16", "--udp-reorder", "16", "--udp-rto-initial", "300", "--udp-ack-delay", "25"]
            : [];
        string[] serverArguments = ["--config", serverConfig, "--port", port.ToString(), "--protocol", protocol.ToString(), .. udpOverrides];
        Task<int> serverTask = ServerCliRunner.RunAsync(
            serverArguments,
            console: serverConsole,
            token: serverCancellation.Token);

        try
        {
            await WaitForAsync(() => serverConsole.Output.Contains("server running", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(5));

            string[] clientArguments = ["--config", clientConfig, "--port", port.ToString(), "--protocol", protocol.ToString(), .. udpOverrides];
            int clientResult = await ClientCliRunner.RunAsync(
                clientArguments,
                console: clientConsole).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, clientResult);
            Assert.Contains("client connected", clientConsole.Output, StringComparison.OrdinalIgnoreCase);
            if (protocol == TransportProtocol.Udp)
            {
                Assert.Contains("window=16", serverConsole.Output, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("window=16", clientConsole.Output, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("ack-delay=25ms", clientConsole.Output, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            serverCancellation.Cancel();
            Assert.Equal(0, await serverTask.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private static int GetAvailablePort(TransportProtocol protocol)
    {
        if (protocol == TransportProtocol.Udp)
        {
            using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        throw new TimeoutException("Timed out waiting for CLI state.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HawkNet.CommandLine", "HawkNet.CommandLine.csproj"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class BufferedCliConsole(bool endInput = false) : ICliConsole
    {
        private readonly StringWriter _writer = new();
        private readonly object _sync = new();
        public string Output { get { lock (_sync) return _writer.ToString(); } }
        public void Write(string value) { lock (_sync) _writer.Write(value); }
        public void WriteLine(string value = "") { lock (_sync) _writer.WriteLine(value); }
        public ValueTask<string?> ReadLineAsync(CancellationToken token = default) =>
            endInput ? ValueTask.FromResult<string?>(null) : ValueTask.FromCanceled<string?>(token);
    }
}
