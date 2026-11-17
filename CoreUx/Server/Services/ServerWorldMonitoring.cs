using HawkNet.Core.Server;
using HawkNet.CoreUx.Client.World;

namespace HawkNet.CoreUx.Server.Services;

public sealed record WorldRegionDescriptor(string Id, string DisplayName);

public sealed record WorldEntityDelta
{
    public string RegionId { get; init; } = string.Empty;
    public long Revision { get; init; }
    public IReadOnlyList<WorldEntitySnapshot> AddedOrUpdated { get; init; } = Array.Empty<WorldEntitySnapshot>();
    public IReadOnlyList<string> RemovedIds { get; init; } = Array.Empty<string>();
}

public interface IServerWorldMonitorProvider
{
    IReadOnlyList<WorldRegionDescriptor> GetRegions(GameHost host);
    WorldSnapshot? Capture(GameHost host, string? regionId = null);
}

public sealed record ServerInspectorContribution(string Id, string Title, object ViewModel);

public interface IServerInspectorProvider
{
    IEnumerable<ServerInspectorContribution> CreateInspectors(GameHost host);
}
