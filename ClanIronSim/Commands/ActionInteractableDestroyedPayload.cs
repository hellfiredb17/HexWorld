using ClanIronSim.World;

namespace ClanIronSim.Commands;

public sealed class ActionInteractableDestroyedPayload
{
    public string InteractableId { get; set; } = string.Empty;

    public string DestroyedById { get; set; } = string.Empty;

    public MapLocation Location { get; set; }
}
