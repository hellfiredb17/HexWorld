namespace ClanIronSim.HawkNet;

public sealed class ClanIronSimMatchSettings
{
    public string WorldStatePath { get; set; } = Path.Combine(
        "Resources",
        "WorldData",
        "test_rect_island_worldstate.json");

    public string VariantId { get; set; } = "clan_iron";
}
