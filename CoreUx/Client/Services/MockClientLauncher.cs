using HawkNet.Core.Client;

namespace HawkNet.CoreUx.Client.Services;

public sealed class MockClientLauncher : IClientLauncher
{
    public int ConnectRequestCount { get; private set; }

    public int DisconnectRequestCount { get; private set; }

    public GameClientConfig? LastConfig { get; private set; }

    public Func<GameClientConfig, CancellationToken, Task>? ConnectOverride { get; set; }

    public Func<CancellationToken, Task>? DisconnectOverride { get; set; }

    public async Task ConnectAsync(GameClientConfig config, CancellationToken token = default)
    {
        ConnectRequestCount++;
        LastConfig = config;
        if (ConnectOverride is not null)
        {
            await ConnectOverride(config, token);
        }
    }

    public async Task DisconnectAsync(CancellationToken token = default)
    {
        DisconnectRequestCount++;
        if (DisconnectOverride is not null)
        {
            await DisconnectOverride(token);
        }
    }
}
