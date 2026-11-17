using ClanIronSim.SimMath;
using ClanIronSim.World;

namespace ClanIronSim.Events;

public sealed class MoveEventPayload
{
    public MoveStatus Status { get; set; } = MoveStatus.Idle;

    public string MoverId { get; set; } = string.Empty;

    public List<MapLocation> Path { get; set; } = new();

    public List<SimVector2> PathWorldPositions { get; set; } = new();

    public int CurrentIndex { get; set; }

    public SimVector2 CurrentWorldPosition { get; set; }

    public SimVector2 DestinationWorldPosition { get; set; }

    public SimVector2 Velocity { get; set; }

    public SimVector2 FaceForward { get; set; } = SimVector2.Forward;

    public SimVector2 BaseForward { get; set; } = SimVector2.Forward;

    public float RotationVelocity { get; set; }

    public float MaxSpeed { get; set; }

    public float MaxRotationSpeed { get; set; }

    public string FailureReason { get; set; } = string.Empty;
}
