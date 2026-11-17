using HawkNet.CoreUx.Client.Services;

namespace HawkNet.CoreUx.Client.ViewModels;

public sealed class FoundServerViewModel
{
    public FoundServerViewModel(FoundServer server)
    {
        Host = server.Host;
        Port = server.Port;
        Name = server.Name;
    }

    public string Host { get; }

    public int Port { get; }

    public string Name { get; }

    public string EndpointText => $"{Host}:{Port}";
}
