namespace ClanIronSim.Events;

public sealed class MoveStatusChangeEvent : SimEvent
{
    public override SimEventType Type => SimEventType.MoveStatusChange;

    public MoveEventPayload Payload { get; set; } = new();
}
