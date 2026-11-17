using System.Text.Json;
using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

internal sealed class MapTileJsonConverter : JsonConverter<MapTile>
{
    public override MapTile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        TileShapeReadResult shapeResult = ReadShape(root);
        MapTile tile = shapeResult.Shape switch
        {
            MapTileShape.Rectangle => new MapRectTile(),
            MapTileShape.Hex => new MapHexTile(),
            _ => throw new JsonException($"Unsupported map tile shape '{shapeResult.Shape}'.")
        };

        tile.Shape = shapeResult.Shape;
        tile.GridPosition = ReadIntVector(root, nameof(MapTile.GridPosition), nameof(MapTile.GridX), nameof(MapTile.GridZ));
        tile.LocalPosition = ReadVector(root, nameof(MapTile.LocalPosition), nameof(MapTile.X), nameof(MapTile.Z));
        tile.WorldPosition = ReadVector(root, nameof(MapTile.WorldPosition), nameof(MapTile.WorldX), nameof(MapTile.WorldZ));
        tile.Size = ReadVector(
            root,
            nameof(MapTile.Size),
            nameof(MapTile.SizeX),
            nameof(MapTile.SizeY),
            legacyScalarXName: nameof(Width),
            legacyScalarZName: nameof(Depth));
        tile.Attributes = ReadAttributes(root, options);
        if (tile is MapHexTile hexTile)
        {
            hexTile.Orientation = ReadHexOrientation(root, shapeResult.Orientation);
            hexTile.Coordinate = ReadHexCoord(root);
        }

        return tile;
    }

    public override void Write(Utf8JsonWriter writer, MapTile value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WriteIntVector(writer, nameof(MapTile.GridPosition), value.GridPosition);
        writer.WriteString(nameof(MapTile.Shape), value.Shape.ToString());
        WriteVector(writer, nameof(MapTile.LocalPosition), value.LocalPosition);
        WriteVector(writer, nameof(MapTile.WorldPosition), value.WorldPosition);
        WriteVector(writer, nameof(MapTile.Size), value.Size);
        if (value is MapHexTile hexTile)
        {
            writer.WriteString(nameof(MapTileSettings.HexOrientation), hexTile.Orientation.ToString());
            WriteHexCoord(writer, hexTile.Coordinate);
        }

        writer.WritePropertyName(nameof(MapTile.Attributes));
        JsonSerializer.Serialize(writer, value.Attributes, options);
        writer.WriteEndObject();
    }

    private static TileShapeReadResult ReadShape(JsonElement root)
    {
        if (!TryGetProperty(root, nameof(MapTile.Shape), out JsonElement property))
        {
            return new TileShapeReadResult(MapTileShape.Rectangle, HexOrientation.PointyTop);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() switch
            {
                string value when string.Equals(value, nameof(MapTileShape.Rectangle), StringComparison.OrdinalIgnoreCase) =>
                    new TileShapeReadResult(MapTileShape.Rectangle, HexOrientation.PointyTop),
                string value when string.Equals(value, nameof(MapTileShape.Hex), StringComparison.OrdinalIgnoreCase) =>
                    new TileShapeReadResult(MapTileShape.Hex, HexOrientation.PointyTop),
                string value when string.Equals(value, LegacyHexPointyTop, StringComparison.OrdinalIgnoreCase) =>
                    new TileShapeReadResult(MapTileShape.Hex, HexOrientation.PointyTop),
                string value when string.Equals(value, LegacyHexFlatTop, StringComparison.OrdinalIgnoreCase) =>
                    new TileShapeReadResult(MapTileShape.Hex, HexOrientation.FlatTop),
                _ => throw new JsonException("Unsupported map tile shape.")
            };
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int shapeValue))
        {
            return shapeValue switch
            {
                0 => new TileShapeReadResult(MapTileShape.Rectangle, HexOrientation.PointyTop),
                1 => new TileShapeReadResult(MapTileShape.Hex, HexOrientation.PointyTop),
                2 => new TileShapeReadResult(MapTileShape.Hex, HexOrientation.FlatTop),
                _ => throw new JsonException("Unsupported map tile shape.")
            };
        }

        throw new JsonException("Map tile shape must be a string or number.");
    }

    private static HexOrientation ReadHexOrientation(JsonElement root, HexOrientation fallback)
    {
        if (!TryGetProperty(root, nameof(MapTileSettings.HexOrientation), out JsonElement property)
            && !TryGetProperty(root, nameof(MapHexTile.Orientation), out property))
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

    private static HexCoord ReadHexCoord(JsonElement root)
    {
        if (!TryGetProperty(root, nameof(MapHexTile.Coordinate), out JsonElement property)
            || property.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        return new HexCoord(
            ReadInt(property, nameof(HexCoord.Q)),
            ReadInt(property, nameof(HexCoord.R)));
    }

    private static void WriteHexCoord(Utf8JsonWriter writer, HexCoord coordinate)
    {
        writer.WritePropertyName(nameof(MapHexTile.Coordinate));
        writer.WriteStartObject();
        writer.WriteNumber(nameof(HexCoord.Q), coordinate.Q);
        writer.WriteNumber(nameof(HexCoord.R), coordinate.R);
        writer.WriteEndObject();
    }

    private static SimIntVector2 ReadIntVector(JsonElement root, string name, string scalarXName, string scalarZName)
    {
        if (TryGetProperty(root, name, out JsonElement property)
            && property.ValueKind == JsonValueKind.Object)
        {
            return new SimIntVector2(
                ReadInt(property, nameof(SimIntVector2.X)),
                ReadInt(property, nameof(SimIntVector2.Z)));
        }

        return new SimIntVector2(ReadInt(root, scalarXName), ReadInt(root, scalarZName));
    }

    private static SimVector2 ReadVector(
        JsonElement root,
        string name,
        string scalarXName,
        string scalarZName,
        string? legacyScalarXName = null,
        string? legacyScalarZName = null)
    {
        if (TryGetProperty(root, name, out JsonElement property)
            && property.ValueKind == JsonValueKind.Object)
        {
            return new SimVector2(
                ReadFloat(property, nameof(SimVector2.X)),
                ReadFloat(property, nameof(SimVector2.Z)));
        }

        return new SimVector2(
            ReadFloat(root, scalarXName, legacyScalarXName),
            ReadFloat(root, scalarZName, legacyScalarZName));
    }

    private static void WriteIntVector(Utf8JsonWriter writer, string name, SimIntVector2 vector)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber(nameof(SimIntVector2.X), vector.X);
        writer.WriteNumber(nameof(SimIntVector2.Z), vector.Z);
        writer.WriteEndObject();
    }

    private static void WriteVector(Utf8JsonWriter writer, string name, SimVector2 vector)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber(nameof(SimVector2.X), vector.X);
        writer.WriteNumber(nameof(SimVector2.Z), vector.Z);
        writer.WriteEndObject();
    }

    private static int ReadInt(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out JsonElement property))
        {
            return 0;
        }

        return property.GetInt32();
    }

    private static float ReadFloat(JsonElement root, string name, string? legacyName = null)
    {
        if (TryGetProperty(root, name, out JsonElement property)
            || (legacyName is not null && TryGetProperty(root, legacyName, out property)))
        {
            return property.GetSingle();
        }

        return 0f;
    }

    private static List<MapAttribute> ReadAttributes(JsonElement root, JsonSerializerOptions options)
    {
        if (!TryGetProperty(root, nameof(MapTile.Attributes), out JsonElement property))
        {
            return new List<MapAttribute>();
        }

        return property.Deserialize<List<MapAttribute>>(options) ?? new List<MapAttribute>();
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

    private const string Width = "Width";
    private const string Depth = "Depth";
    private const string LegacyHexPointyTop = "HexPointyTop";
    private const string LegacyHexFlatTop = "HexFlatTop";

    private readonly struct TileShapeReadResult
    {
        public TileShapeReadResult(MapTileShape shape, HexOrientation orientation)
        {
            Shape = shape;
            Orientation = orientation;
        }

        public MapTileShape Shape { get; }

        public HexOrientation Orientation { get; }
    }
}
