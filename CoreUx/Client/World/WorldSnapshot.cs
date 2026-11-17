namespace HawkNet.CoreUx.Client.World;

public sealed record WorldSnapshot
{
    public string RegionId { get; init; } = string.Empty;

    public string RegionName { get; init; } = string.Empty;

    public int? LocalPlayerId { get; init; }

    public long TopologyRevision { get; init; }

    public long EntityRevision { get; init; }

    public IReadOnlyList<WorldTileSnapshot> Tiles { get; init; } = Array.Empty<WorldTileSnapshot>();

    public IReadOnlyList<WorldEntitySnapshot> Entities { get; init; } = Array.Empty<WorldEntitySnapshot>();
}
