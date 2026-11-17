using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public readonly struct WorldMapPosition
{
    public WorldMapPosition(float x, float z)
    {
        X = x;
        Z = z;
    }

    public WorldMapPosition(SimVector2 position)
        : this(position.X, position.Z)
    {
    }

    public float X { get; }

    public float Z { get; }

    [JsonIgnore]
    public SimVector2 Position => new(X, Z);
}
