namespace ClanIronSim.World;

public sealed class MapTileDefinition
{
    public string Name { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = new();

    public MapTile Tile { get; set; } = new MapRectTile();
}
