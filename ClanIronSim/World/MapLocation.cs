using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public readonly struct MapLocation
{
    public MapLocation(
        string regionId,
        string placedSectionId,
        int sectionGridX,
        int sectionGridZ,
        int tileGridX,
        int tileGridZ,
        float worldX,
        float worldZ)
        : this(
            regionId,
            placedSectionId,
            new SimIntVector2(sectionGridX, sectionGridZ),
            new SimIntVector2(tileGridX, tileGridZ),
            new SimVector2(worldX, worldZ))
    {
    }

    [JsonConstructor]
    public MapLocation(
        string regionId,
        string placedSectionId,
        SimIntVector2 sectionGrid,
        SimIntVector2 tileGrid,
        SimVector2 worldPosition)
    {
        RegionId = regionId ?? string.Empty;
        PlacedSectionId = placedSectionId ?? string.Empty;
        SectionGrid = sectionGrid;
        TileGrid = tileGrid;
        WorldPosition = worldPosition;
    }

    public string RegionId { get; }

    public string PlacedSectionId { get; }

    public SimIntVector2 SectionGrid { get; }

    [JsonIgnore]
    public int SectionGridX => SectionGrid.X;

    [JsonIgnore]
    public int SectionGridZ => SectionGrid.Z;

    public SimIntVector2 TileGrid { get; }

    [JsonIgnore]
    public int TileGridX => TileGrid.X;

    [JsonIgnore]
    public int TileGridZ => TileGrid.Z;

    public SimVector2 WorldPosition { get; }

    [JsonIgnore]
    public float WorldX => WorldPosition.X;

    [JsonIgnore]
    public float WorldZ => WorldPosition.Z;
}
