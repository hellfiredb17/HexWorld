namespace ClanIronSim.World;

public sealed class MapSectionDefinition
{
    public string Name { get; set; } = string.Empty;
    public MapTileShape Shape { get; set; } = MapTileShape.Rectangle;
    public HexOrientation HexOrientation { get; set; } = HexOrientation.PointyTop;
    public int Width { get; set; } = 10;
    public int Depth { get; set; } = 8;
    public List<MapSectionTileInstance> Tiles { get; set; } = new();
}

public sealed class MapSectionTileInstance
{
    public int GridX { get; set; }
    public int GridZ { get; set; }
    public string TileName { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<MapAttribute> Attributes { get; set; } = new();
}
