namespace ClanIronSim.Commands;

public sealed class SimCommand
{
    public SimCommandType Type { get; set; } = SimCommandType.PlayerMoveToWorldPosition;

    public PlayerMoveToWorldPositionCommandPayload? PlayerMoveToWorldPosition { get; set; }
}
