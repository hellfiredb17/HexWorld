using HawkNet.Core.Server;

namespace HawkNet.CoreUx.Server.Services;

public sealed class MockServerLauncher : IServerLauncher
{
    public GameHostConfig? LastStartedConfig { get; private set; }

    public int StopRequestCount { get; private set; }

    public Task StartAsync(GameHostConfig config, CancellationToken token = default)
    {
        LastStartedConfig = config;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken token = default)
    {
        StopRequestCount++;
        LastStartedConfig = null;
        return Task.CompletedTask;
    }
}
