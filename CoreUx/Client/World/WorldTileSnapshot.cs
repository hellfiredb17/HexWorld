namespace HawkNet.CoreUx.Client.World;

public sealed record WorldTileSnapshot
{
    public string Id { get; init; } = string.Empty;

    public string RegionId { get; init; } = string.Empty;

    public string PlacedSectionId { get; init; } = string.Empty;

    public int GridX { get; init; }

    public int GridZ { get; init; }

    public float WorldX { get; init; }

    public float WorldZ { get; init; }

    public float SizeX { get; init; } = 1f;

    public float SizeZ { get; init; } = 1f;

    public float CenterX { get; init; }

    public float CenterZ { get; init; }

    public WorldTileShape Shape { get; init; } = WorldTileShape.Rectangle;

    public WorldHexOrientation HexOrientation { get; init; } = WorldHexOrientation.PointyTop;

    public int MovementCost { get; init; } = 1;

    public bool IsBlocked { get; init; }

    public bool IsSpawner { get; init; }
}
