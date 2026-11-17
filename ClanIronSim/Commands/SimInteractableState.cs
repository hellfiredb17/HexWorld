using ClanIronSim.Entity;
using ClanIronSim.SimMath;
using ClanIronSim.World;

namespace ClanIronSim.Commands;

public sealed class SimInteractableState
{
    public string Id { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public bool CanMove { get; set; }

    public MapLocation Location { get; set; }

    public List<EntityAttribute> Attributes { get; set; } = new();

    public SimVector2 CurrentWorldPosition { get; set; }

    public SimVector2 Velocity { get; set; }

    public SimVector2 FaceForward { get; set; }

    public SimVector2 BaseForward { get; set; }

    public float RotationVelocity { get; set; }

    public NavigatorMovementStatus? MovementStatus { get; set; }
}
