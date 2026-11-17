using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

[JsonConverter(typeof(MapTileSettingsJsonConverter))]
public sealed class MapTileSettings
{
    public MapTileShape Shape { get; set; } = MapTileShape.Rectangle;

    public SimVector2 Size { get; set; } = new(1f, 1f);

    [JsonIgnore]
    public float SizeX
    {
        get => Size.X;
        set => Size = new SimVector2(value, Size.Z);
    }

    [JsonIgnore]
    public float SizeY
    {
        get => Size.Z;
        set => Size = new SimVector2(Size.X, value);
    }

    public HexOrientation HexOrientation { get; set; } = HexOrientation.PointyTop;
}
