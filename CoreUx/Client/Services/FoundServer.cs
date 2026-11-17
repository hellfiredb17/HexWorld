namespace HawkNet.CoreUx.Client.Services;

public sealed record FoundServer
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 7777;

    public string Name { get; init; } = "HawkNet Server";
}
