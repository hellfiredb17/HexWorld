namespace ClanIronSim.Commands;

public sealed class SimAction
{
    public SimActionType Type { get; set; } = SimActionType.PlayerHit;

    public SimActionTarget Target { get; set; } = SimActionTarget.Broadcast;

    public long Tick { get; set; }

    public string RegionId { get; set; } = string.Empty;

    public int? PlayerId { get; set; }

    public CommandStatusActionPayload? CommandStatus { get; set; }

    public MoveActionPayload? Move { get; set; }

    public ActionPlayerHitPayload? PlayerHit { get; set; }

    public ActionInteractableDestroyedPayload? InteractableDestroyed { get; set; }
}
