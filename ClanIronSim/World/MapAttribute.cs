namespace ClanIronSim.World;

public sealed class MapAttribute
{
    public string Id { get; set; } = string.Empty;

    public string ModifierKey { get; set; } = string.Empty;

    public int Amount { get; set; }
}
