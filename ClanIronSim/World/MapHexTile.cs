namespace ClanIronSim.World;

public sealed class MapHexTile : MapTile
{
    public MapHexTile()
    {
        Shape = MapTileShape.Hex;
    }

    public HexOrientation Orientation { get; set; } = HexOrientation.PointyTop;

    public HexCoord Coordinate { get; set; }

    public override float WorldCenterX => WorldX + (SizeX / 2f);

    public override float WorldCenterZ => WorldZ + (SizeY / 2f);

    public override MapBounds Bounds => new(WorldX, WorldZ, WorldX + SizeX, WorldZ + SizeY);

    public override bool ContainsWorldPosition(float worldX, float worldZ)
    {
        if (SizeX <= 0 || SizeY <= 0 || !Bounds.Contains(worldX, worldZ))
        {
            return false;
        }

        float normalizedX = System.Math.Abs(worldX - WorldCenterX) / (SizeX / 2f);
        float normalizedZ = System.Math.Abs(worldZ - WorldCenterZ) / (SizeY / 2f);
        return Orientation switch
        {
            HexOrientation.PointyTop => normalizedX <= 1f && normalizedZ <= 1f - (normalizedX * 0.5f),
            HexOrientation.FlatTop => normalizedZ <= 1f && normalizedX <= 1f - (normalizedZ * 0.5f),
            _ => false
        };
    }

    internal override MapTile CopyForPlacement(int offsetX, int offsetZ)
    {
        var tile = new MapHexTile();
        CopyBaseTo(tile, offsetX, offsetZ);
        tile.Shape = MapTileShape.Hex;
        tile.Orientation = Orientation;
        tile.Coordinate = Coordinate;
        return tile;
    }
}
