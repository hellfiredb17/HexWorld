using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClanIronSim;

public static class SimJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions(writeIndented: false);

    public static readonly JsonSerializerOptions IndentedOptions = CreateOptions(writeIndented: true);

    public static string ToJson<T>(T value, bool writeIndented = false)
    {
        return JsonSerializer.Serialize(value, writeIndented ? IndentedOptions : Options);
    }

    public static T FromJson<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Sim JSON cannot be empty.", nameof(json));
        }

        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidOperationException("Sim JSON did not contain the requested payload.");
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
    }
}
