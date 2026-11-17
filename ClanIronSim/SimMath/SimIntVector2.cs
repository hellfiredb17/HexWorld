using System.Text.Json.Serialization;

namespace ClanIronSim.SimMath;

public readonly struct SimIntVector2 : IEquatable<SimIntVector2>
{
    [JsonConstructor]
    public SimIntVector2(int x, int z)
    {
        X = x;
        Z = z;
    }

    public int X { get; }

    public int Z { get; }

    public int ManhattanLength => System.Math.Abs(X) + System.Math.Abs(Z);

    public static SimIntVector2 Zero => new(0, 0);

    public static SimIntVector2 One => new(1, 1);

    public SimIntVector2 Add(SimIntVector2 other)
    {
        return new SimIntVector2(X + other.X, Z + other.Z);
    }

    public SimIntVector2 Subtract(SimIntVector2 other)
    {
        return new SimIntVector2(X - other.X, Z - other.Z);
    }

    public SimIntVector2 Multiply(int scalar)
    {
        return new SimIntVector2(X * scalar, Z * scalar);
    }

    public int ManhattanDistanceTo(SimIntVector2 other)
    {
        return System.Math.Abs(X - other.X) + System.Math.Abs(Z - other.Z);
    }

    public bool Equals(SimIntVector2 other)
    {
        return X == other.X && Z == other.Z;
    }

    public override bool Equals(object? obj)
    {
        return obj is SimIntVector2 other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (X * 397) ^ Z;
        }
    }

    public static SimIntVector2 operator +(SimIntVector2 left, SimIntVector2 right)
    {
        return left.Add(right);
    }

    public static SimIntVector2 operator -(SimIntVector2 left, SimIntVector2 right)
    {
        return left.Subtract(right);
    }

    public static SimIntVector2 operator *(SimIntVector2 vector, int scalar)
    {
        return vector.Multiply(scalar);
    }

    public static SimIntVector2 operator *(int scalar, SimIntVector2 vector)
    {
        return vector.Multiply(scalar);
    }

    public static bool operator ==(SimIntVector2 left, SimIntVector2 right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SimIntVector2 left, SimIntVector2 right)
    {
        return !left.Equals(right);
    }
}
