using ClanIronSim.Events;
using System.Text.Json.Serialization;

namespace ClanIronSim.Entity;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(NavigatorEntity), "navigator")]
public class InteractableEntity : Entity
{
    public virtual bool CanMove => false;

    public List<EntityAttribute> Attributes { get; set; } = new();

    public virtual void Update(float deltaTime)
    {
    }

    public virtual void Update(float deltaTime, ISimEventDispatcher events)
    {
        Update(deltaTime);
    }

    public bool TryGetAttribute(string key, string subtype, out EntityAttribute? attribute)
    {
        attribute = null;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(subtype))
        {
            return false;
        }

        attribute = Attributes.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Subtype, subtype, StringComparison.OrdinalIgnoreCase));
        return attribute is not null;
    }

    public bool TryGetNumberAttribute(string key, string subtype, out int value)
    {
        value = default;
        return TryGetAttribute(key, subtype, out EntityAttribute? attribute)
            && attribute!.TryGetNumber(out value);
    }

    public bool TryGetBooleanAttribute(string key, string subtype, out bool value)
    {
        value = default;
        return TryGetAttribute(key, subtype, out EntityAttribute? attribute)
            && attribute!.TryGetBoolean(out value);
    }

    public bool TryGetTextAttribute(string key, string subtype, out string value)
    {
        value = string.Empty;
        return TryGetAttribute(key, subtype, out EntityAttribute? attribute)
            && attribute!.TryGetText(out value);
    }
}
