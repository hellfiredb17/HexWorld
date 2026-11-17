using System.Text.Json.Serialization;

namespace ClanIronSim.SimMath;

public readonly struct SimVector2
{
    [JsonConstructor]
    public SimVector2(float x, float z)
    {
        X = x;
        Z = z;
    }

    public float X { get; }

    public float Z { get; }

    public float Length => (float)System.Math.Sqrt((X * X) + (Z * Z));

    public static SimVector2 Zero => new(0, 0);

    public static SimVector2 Forward => new(0, 1);

    public SimVector2 Normalize()
    {
        float length = Length;
        if (length <= 0)
        {
            return Zero;
        }

        return new SimVector2(X / length, Z / length);
    }

    public float Dot(SimVector2 other)
    {
        return (X * other.X) + (Z * other.Z);
    }

    public float Cross(SimVector2 other)
    {
        return (X * other.Z) - (Z * other.X);
    }

    public float DistanceTo(SimVector2 other)
    {
        return Subtract(other).Length;
    }

    public float AngleToDegrees(SimVector2 other)
    {
        SimVector2 from = Normalize();
        SimVector2 to = other.Normalize();
        if (from.Length <= SimMathConstants.Epsilon || to.Length <= SimMathConstants.Epsilon)
        {
            return 0f;
        }

        float dot = SimMathf.Clamp(from.Dot(to), -1f, 1f);
        return (float)System.Math.Acos(dot) * SimMathConstants.RadiansToDegrees;
    }

    public float SignedAngleToDegrees(SimVector2 other)
    {
        SimVector2 from = Normalize();
        SimVector2 to = other.Normalize();
        if (from.Length <= SimMathConstants.Epsilon || to.Length <= SimMathConstants.Epsilon)
        {
            return 0f;
        }

        float angle = (float)System.Math.Atan2(from.Cross(to), from.Dot(to))
            * SimMathConstants.RadiansToDegrees;
        return SimMathf.NormalizeAngleDegrees(angle);
    }

    public SimVector2 RotateDegrees(float degrees)
    {
        float radians = degrees * SimMathConstants.DegreesToRadians;
        float cos = (float)System.Math.Cos(radians);
        float sin = (float)System.Math.Sin(radians);
        return new SimVector2(
            (X * cos) - (Z * sin),
            (X * sin) + (Z * cos));
    }

    public SimVector2 Add(SimVector2 other)
    {
        return new SimVector2(X + other.X, Z + other.Z);
    }

    public SimVector2 Subtract(SimVector2 other)
    {
        return new SimVector2(X - other.X, Z - other.Z);
    }

    public SimVector2 Multiply(float scalar)
    {
        return new SimVector2(X * scalar, Z * scalar);
    }

    public static SimVector2 operator +(SimVector2 left, SimVector2 right)
    {
        return left.Add(right);
    }

    public static SimVector2 operator -(SimVector2 left, SimVector2 right)
    {
        return left.Subtract(right);
    }

    public static SimVector2 operator *(SimVector2 vector, float scalar)
    {
        return vector.Multiply(scalar);
    }

    public static SimVector2 operator *(float scalar, SimVector2 vector)
    {
        return vector.Multiply(scalar);
    }
}
