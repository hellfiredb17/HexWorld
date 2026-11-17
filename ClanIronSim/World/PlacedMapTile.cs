using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public readonly struct PlacedMapTile : IEquatable<PlacedMapTile>
{
    public PlacedMapTile(PlacedMapSection section, MapTile tile)
    {
        Section = section;
        Tile = tile;
    }

    public PlacedMapSection Section { get; }

    public MapTile Tile { get; }

    public SimIntVector2 GridPosition => Tile?.GridPosition ?? SimIntVector2.Zero;

    public SimVector2 WorldPosition => Tile?.WorldPosition ?? SimVector2.Zero;

    public SimVector2 WorldCenter => Tile?.WorldCenter ?? SimVector2.Zero;

    public bool Equals(PlacedMapTile other)
    {
        return ReferenceEquals(Section, other.Section)
            && ReferenceEquals(Tile, other.Tile);
    }

    public override bool Equals(object? obj)
    {
        return obj is PlacedMapTile other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((Section is null ? 0 : Section.GetHashCode()) * 397)
                ^ (Tile is null ? 0 : Tile.GetHashCode());
        }
    }

    public static bool operator ==(PlacedMapTile left, PlacedMapTile right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PlacedMapTile left, PlacedMapTile right)
    {
        return !left.Equals(right);
    }
}
