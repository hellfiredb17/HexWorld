using ClanIronSim.SimMath;

namespace ClanIronSim.Commands;

public sealed class PlayerMoveToWorldPositionCommandPayload
{
    public int PlayerId { get; set; }

    public string NavigatorId { get; set; } = string.Empty;

    public SimVector2 WorldPosition { get; set; }
}
