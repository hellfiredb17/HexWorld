using HawkNet.Core.Server;

namespace HawkNet.CoreUx.Client.Services;

public interface IClientServerDiscovery
{
    Task<IReadOnlyList<FoundServer>> SearchAsync(int port, CancellationToken token = default);
	Task<IReadOnlyList<FoundServer>> SearchAsync(int port, TransportProtocol protocol, CancellationToken token = default);
}
