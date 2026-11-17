using HawkNet.Core.Client;

namespace HawkNet.CoreUx.Client.Services;

public interface IClientLauncher
{
    Task ConnectAsync(GameClientConfig config, CancellationToken token = default);

    Task DisconnectAsync(CancellationToken token = default);
}
