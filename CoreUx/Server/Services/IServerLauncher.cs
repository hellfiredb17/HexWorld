using HawkNet.Core.Server;

namespace HawkNet.CoreUx.Server.Services;

public interface IServerLauncher
{
    Task StartAsync(GameHostConfig config, CancellationToken token = default);

    Task StopAsync(CancellationToken token = default);
}
