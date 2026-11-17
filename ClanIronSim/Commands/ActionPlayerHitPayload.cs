using ClanIronSim.World;

namespace ClanIronSim.Commands;

public sealed class ActionPlayerHitPayload
{
    public int PlayerId { get; set; }

    public string SourceId { get; set; } = string.Empty;

    public int Damage { get; set; }

    public int? RemainingHp { get; set; }

    public MapLocation Location { get; set; }
}
