namespace ClanIronSim.World;

public sealed class MapRectTile : MapTile
{
    public MapRectTile()
    {
        Shape = MapTileShape.Rectangle;
    }

    public override float WorldCenterX => WorldX + (SizeX / 2f);

    public override float WorldCenterZ => WorldZ + (SizeY / 2f);

    public override MapBounds Bounds => new(WorldX, WorldZ, WorldX + SizeX, WorldZ + SizeY);

    public override bool ContainsWorldPosition(float worldX, float worldZ)
    {
        return Bounds.Contains(worldX, worldZ);
    }

    internal override MapTile CopyForPlacement(int offsetX, int offsetZ)
    {
        var tile = new MapRectTile();
        CopyBaseTo(tile, offsetX, offsetZ);
        tile.Shape = MapTileShape.Rectangle;
        return tile;
    }
}
