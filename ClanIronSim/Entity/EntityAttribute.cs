using System.Globalization;
using System.Text.Json.Serialization;

namespace ClanIronSim.Entity;

public sealed class EntityAttribute
{
    public string Key { get; set; } = string.Empty;

    public string Subtype { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EntityAttributeValueKind ValueKind { get; set; } = EntityAttributeValueKind.Text;

    public string Value { get; set; } = string.Empty;

    public bool TryGetNumber(out int number)
    {
        number = default;
        return ValueKind == EntityAttributeValueKind.Number
            && int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    public bool TryGetBoolean(out bool value)
    {
        value = default;
        return ValueKind == EntityAttributeValueKind.Boolean
            && bool.TryParse(Value, out value);
    }

    public bool TryGetText(out string value)
    {
        value = Value;
        return ValueKind == EntityAttributeValueKind.Text;
    }
}
