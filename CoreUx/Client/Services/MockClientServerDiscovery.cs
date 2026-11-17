using HawkNet.Core.Server;

namespace HawkNet.CoreUx.Client.Services;

public sealed class MockClientServerDiscovery : IClientServerDiscovery
{
    public List<FoundServer> Results { get; } = new();

    public int SearchRequestCount { get; private set; }

    public int LastPort { get; private set; }
	public TransportProtocol LastProtocol { get; private set; } = TransportProtocol.Tcp;

    public Func<int, CancellationToken, Task<IReadOnlyList<FoundServer>>>? SearchOverride { get; set; }

    public Task<IReadOnlyList<FoundServer>> SearchAsync(int port, CancellationToken token = default)
	{
		return SearchAsync(port, TransportProtocol.Tcp, token);
	}

	public Task<IReadOnlyList<FoundServer>> SearchAsync(int port, TransportProtocol protocol, CancellationToken token = default)
    {
        SearchRequestCount++;
        LastPort = port;
		LastProtocol = protocol;
        if (SearchOverride is not null)
        {
            return SearchOverride(port, token);
        }

        return Task.FromResult<IReadOnlyList<FoundServer>>(Results.ToArray());
    }
}
