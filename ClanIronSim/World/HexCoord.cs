using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public readonly struct HexCoord : IEquatable<HexCoord>
{
    private static readonly HexCoord[] Directions =
    [
        new(1, 0),
        new(1, -1),
        new(0, -1),
        new(-1, 0),
        new(-1, 1),
        new(0, 1)
    ];

    public HexCoord(int q, int r)
    {
        Q = q;
        R = r;
    }

    public HexCoord(SimIntVector2 axial)
        : this(axial.X, axial.Z)
    {
    }

    public int Q { get; }

    public int R { get; }

    public SimIntVector2 Axial => new(Q, R);

    public int S => -Q - R;

    public HexCoord Neighbor(int direction)
    {
        if (direction < 0 || direction >= Directions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Hex direction must be between 0 and 5.");
        }

        HexCoord offset = Directions[direction];
        return new HexCoord(Q + offset.Q, R + offset.R);
    }

    public IReadOnlyList<HexCoord> Neighbors()
    {
        var neighbors = new HexCoord[Directions.Length];
        for (int i = 0; i < Directions.Length; i++)
        {
            HexCoord offset = Directions[i];
            neighbors[i] = new HexCoord(Q + offset.Q, R + offset.R);
        }

        return neighbors;
    }

    public int DistanceTo(HexCoord other)
    {
        return (System.Math.Abs(Q - other.Q)
            + System.Math.Abs(R - other.R)
            + System.Math.Abs(S - other.S)) / 2;
    }

    public bool Equals(HexCoord other)
    {
        return Q == other.Q && R == other.R;
    }

    public override bool Equals(object? obj)
    {
        return obj is HexCoord other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Q * 397) ^ R;
        }
    }

    public static bool operator ==(HexCoord left, HexCoord right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(HexCoord left, HexCoord right)
    {
        return !left.Equals(right);
    }
}
