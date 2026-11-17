using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public readonly struct MapBounds : IEquatable<MapBounds>
{
    public MapBounds(float minX, float minZ, float maxX, float maxZ)
    {
        MinX = minX;
        MinZ = minZ;
        MaxX = maxX;
        MaxZ = maxZ;
    }

    public float MinX { get; }

    public float MinZ { get; }

    public float MaxX { get; }

    public float MaxZ { get; }

    public SimVector2 Min => new(MinX, MinZ);

    public SimVector2 Max => new(MaxX, MaxZ);

    public SimVector2 Size => new(MaxX - MinX, MaxZ - MinZ);

    public SimVector2 Center => new(MinX + ((MaxX - MinX) / 2f), MinZ + ((MaxZ - MinZ) / 2f));

    public bool Contains(float x, float z)
    {
        return x >= MinX
            && x < MaxX
            && z >= MinZ
            && z < MaxZ;
    }

    public bool Contains(SimVector2 position)
    {
        return Contains(position.X, position.Z);
    }

    public bool Overlaps(MapBounds other)
    {
        return MinX < other.MaxX
            && MaxX > other.MinX
            && MinZ < other.MaxZ
            && MaxZ > other.MinZ;
    }

    public bool Equals(MapBounds other)
    {
        return MinX.Equals(other.MinX)
            && MinZ.Equals(other.MinZ)
            && MaxX.Equals(other.MaxX)
            && MaxZ.Equals(other.MaxZ);
    }

    public override bool Equals(object? obj)
    {
        return obj is MapBounds other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = MinX.GetHashCode();
            hashCode = (hashCode * 397) ^ MinZ.GetHashCode();
            hashCode = (hashCode * 397) ^ MaxX.GetHashCode();
            hashCode = (hashCode * 397) ^ MaxZ.GetHashCode();
            return hashCode;
        }
    }

    public static bool operator ==(MapBounds left, MapBounds right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MapBounds left, MapBounds right)
    {
        return !left.Equals(right);
    }
}
