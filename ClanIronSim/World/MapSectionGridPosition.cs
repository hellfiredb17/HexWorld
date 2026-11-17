using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public readonly struct MapSectionGridPosition
{
    public MapSectionGridPosition(PlacedMapSection section, int gridX, int gridZ)
    {
        Section = section ?? throw new ArgumentNullException(nameof(section));
        GridX = gridX;
        GridZ = gridZ;
    }

    public MapSectionGridPosition(PlacedMapSection section, SimIntVector2 gridPosition)
        : this(section, gridPosition.X, gridPosition.Z)
    {
    }

    public PlacedMapSection Section { get; }

    public int GridX { get; }

    public int GridZ { get; }

    public SimIntVector2 GridPosition => new(GridX, GridZ);
}
