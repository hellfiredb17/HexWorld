using System.Text.Json;
using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

internal sealed class MapTileSettingsJsonConverter : JsonConverter<MapTileSettings>
{
    public override MapTileSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        SettingsShapeReadResult shapeResult = ReadShape(root);

        return new MapTileSettings
        {
            Shape = shapeResult.Shape,
            Size = ReadSize(root),
            HexOrientation = ReadHexOrientation(root, shapeResult.Orientation)
        };
    }

    public override void Write(Utf8JsonWriter writer, MapTileSettings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(nameof(MapTileSettings.Shape), value.Shape.ToString());
        WriteVector(writer, nameof(MapTileSettings.Size), value.Size);
        writer.WriteString(nameof(MapTileSettings.HexOrientation), value.HexOrientation.ToString());
        writer.WriteEndObject();
    }

    private static SettingsShapeReadResult ReadShape(JsonElement root)
    {
        if (!TryGetProperty(root, nameof(MapTileSettings.Shape), out JsonElement property))
        {
            return new SettingsShapeReadResult(MapTileShape.Rectangle, HexOrientation.PointyTop);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() switch
            {
                string value when string.Equals(value, nameof(MapTileShape.Rectangle), StringComparison.OrdinalIgnoreCase) =>
                    new SettingsShapeReadResult(MapTileShape.Rectangle, HexOrientation.PointyTop),
                string value when string.Equals(value, nameof(MapTileShape.Hex), StringComparison.OrdinalIgnoreCase) =>
                    new SettingsShapeReadResult(MapTileShape.Hex, HexOrientation.PointyTop),
                string value when string.Equals(value, LegacyHexPointyTop, StringComparison.OrdinalIgnoreCase) =>
                    new SettingsShapeReadResult(MapTileShape.Hex, HexOrientation.PointyTop),
                string value when string.Equals(value, LegacyHexFlatTop, StringComparison.OrdinalIgnoreCase) =>
                    new SettingsShapeReadResult(MapTileShape.Hex, HexOrientation.FlatTop),
                _ => throw new JsonException("Unsupported map tile settings shape.")
            };
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int shapeValue))
        {
            return shapeValue switch
            {
                0 => new SettingsShapeReadResult(MapTileShape.Rectangle, HexOrientation.PointyTop),
                1 => new SettingsShapeReadResult(MapTileShape.Hex, HexOrientation.PointyTop),
                2 => new SettingsShapeReadResult(MapTileShape.Hex, HexOrientation.FlatTop),
                _ => throw new JsonException("Unsupported map tile settings shape.")
            };
        }

        throw new JsonException("Map tile settings shape must be a string or number.");
    }

    private static HexOrientation ReadHexOrientation(JsonElement root, HexOrientation fallback)
    {
        if (!TryGetProperty(root, nameof(MapTileSettings.HexOrientation), out JsonElement property))
        {
            return fallback;
        }

        if (property.ValueKind == JsonValueKind.String
            && Enum.TryParse(property.GetString(), ignoreCase: true, out HexOrientation orientation))
        {
            return orientation;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int orientationValue))
        {
            return (HexOrientation)orientationValue;
        }

        throw new JsonException("Hex orientation must be a string or number.");
    }

    private static SimVector2 ReadSize(JsonElement root)
    {
        if (TryGetProperty(root, nameof(MapTileSettings.Size), out JsonElement property)
            && property.ValueKind == JsonValueKind.Object)
        {
            return new SimVector2(
                ReadFloat(property, nameof(SimVector2.X), defaultValue: 1f),
                ReadFloat(property, nameof(SimVector2.Z), defaultValue: 1f));
        }

        return new SimVector2(
            ReadFloat(root, nameof(MapTileSettings.SizeX), defaultValue: 1f),
            ReadFloat(root, nameof(MapTileSettings.SizeY), defaultValue: 1f));
    }

    private static void WriteVector(Utf8JsonWriter writer, string name, SimVector2 vector)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber(nameof(SimVector2.X), vector.X);
        writer.WriteNumber(nameof(SimVector2.Z), vector.Z);
        writer.WriteEndObject();
    }

    private static float ReadFloat(JsonElement root, string name, float defaultValue)
    {
        if (!TryGetProperty(root, name, out JsonElement property))
        {
            return defaultValue;
        }

        return property.GetSingle();
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement property)
    {
        foreach (JsonProperty candidate in root.EnumerateObject())
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private const string LegacyHexPointyTop = "HexPointyTop";
    private const string LegacyHexFlatTop = "HexFlatTop";

    private readonly struct SettingsShapeReadResult
    {
        public SettingsShapeReadResult(MapTileShape shape, HexOrientation orientation)
        {
            Shape = shape;
            Orientation = orientation;
        }

        public MapTileShape Shape { get; }

        public HexOrientation Orientation { get; }
    }
}
