using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public sealed class PlacedMapSection
{
    public string Id { get; set; } = string.Empty;

    public SimIntVector2 GridPosition { get; set; }

    [JsonIgnore]
    public int GridX
    {
        get => GridPosition.X;
        set => GridPosition = new SimIntVector2(value, GridPosition.Z);
    }

    [JsonIgnore]
    public int GridZ
    {
        get => GridPosition.Z;
        set => GridPosition = new SimIntVector2(GridPosition.X, value);
    }

    public SimIntVector2 Offset { get; set; }

    [JsonIgnore]
    public int OffsetX
    {
        get => Offset.X;
        set => Offset = new SimIntVector2(value, Offset.Z);
    }

    [JsonIgnore]
    public int OffsetZ
    {
        get => Offset.Z;
        set => Offset = new SimIntVector2(Offset.X, value);
    }

    public MapSection Section { get; set; } = new();

    [JsonIgnore]
    public MapBounds WorldBounds
    {
        get
        {
            if (Section.Tiles.Count == 0)
            {
                return new MapBounds(OffsetX, OffsetZ, OffsetX, OffsetZ);
            }

            return new MapBounds(
                Section.Tiles.Min(tile => tile.Bounds.MinX),
                Section.Tiles.Min(tile => tile.Bounds.MinZ),
                Section.Tiles.Max(tile => tile.Bounds.MaxX),
                Section.Tiles.Max(tile => tile.Bounds.MaxZ));
        }
    }
}
