using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using HawkNet.Core.Server;
using HawkNet.CoreUx.Server.Services;

namespace HawkNet.Desktop.Services;

public sealed class DesktopServerLauncher : IServerLauncher
{
    private readonly string _runtimeConfigPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Action<IHawkNetServerBuilder>? _configureServer;
    private GameHost? _activeHost;

    public DesktopServerLauncher(
        string? runtimeConfigPath = null,
        Action<IHawkNetServerBuilder>? configureServer = null)
    {
        _configureServer = configureServer;
        _runtimeConfigPath = runtimeConfigPath ?? Path.Combine(
            AppContext.BaseDirectory,
            "Runtime",
            "game-server.runtime.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public GameHost? ActiveHost => _activeHost;

    public string RuntimeConfigPath => _runtimeConfigPath;

    public async Task StartAsync(GameHostConfig config, CancellationToken token = default)
    {
        if (_activeHost is not null)
        {
            throw new InvalidOperationException("A HawkNet server is already running.");
        }

        ServerConfiguration.Validate(config).ThrowIfInvalid();
        EnsurePortIsAvailable(config);

        string? directory = Path.GetDirectoryName(_runtimeConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(config, _jsonOptions);
        await File.WriteAllTextAsync(_runtimeConfigPath, json, token);

        GameHost host = HawkNetServer.Create(_runtimeConfigPath, builder => _configureServer?.Invoke(builder));
        try
        {
            await host.BeginAsync();
            _activeHost = host;
        }
        catch
        {
            await host.StopAsync();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken token = default)
    {
        if (_activeHost is null)
        {
            return;
        }

        GameHost host = _activeHost;
        _activeHost = null;
        await host.StopAsync();
    }

    private static void EnsurePortIsAvailable(GameHostConfig config)
    {
        string host = config.Server.Host;
        int port = config.Server.Port;

        if (!ServerConfiguration.TryResolveBindAddress(host, out IPAddress? address))
        {
            throw new InvalidOperationException(
                $"Server host '{host}' is not a valid IP address. Use an address like 127.0.0.1 or 0.0.0.0.");
        }

        try
        {
            var listener = new TcpListener(address!, port);
            listener.Start();
            listener.Stop();
        }
        catch (SocketException exception)
        {
            throw new InvalidOperationException($"Port {port} on {host} is already in use.", exception);
        }
    }
}
