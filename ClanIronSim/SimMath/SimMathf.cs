namespace ClanIronSim.SimMath;

public static class SimMathf
{
    public static float Clamp(float value, float min, float max)
    {
        if (min > max)
        {
            throw new ArgumentException("Minimum cannot be greater than maximum.", nameof(min));
        }

        return System.Math.Min(System.Math.Max(value, min), max);
    }

    public static float Clamp01(float value)
    {
        return Clamp(value, 0f, 1f);
    }

    public static bool Approximately(float left, float right, float epsilon = SimMathConstants.Epsilon)
    {
        return System.Math.Abs(left - right) <= epsilon;
    }

    public static float NormalizeAngleDegrees(float degrees)
    {
        float normalized = degrees % 360f;
        if (normalized > 180f)
        {
            normalized -= 360f;
        }
        else if (normalized <= -180f)
        {
            normalized += 360f;
        }

        return normalized;
    }

    public static float MoveTowards(float current, float target, float maxDelta)
    {
        if (maxDelta < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelta), "Max delta cannot be negative.");
        }

        float delta = target - current;
        if (System.Math.Abs(delta) <= maxDelta)
        {
            return target;
        }

        return current + (System.Math.Sign(delta) * maxDelta);
    }
}
