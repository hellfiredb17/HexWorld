using ClanIronSim.World;

namespace ClanIronSim.Entity;

public sealed class NavigatorPathState
{
    public NavigatorMovementStatus Status { get; set; } = NavigatorMovementStatus.Idle;

    public List<MapLocation> Waypoints { get; set; } = new();

    public int CurrentWaypointIndex { get; set; }

    public MapLocation Destination { get; set; }

    public string FailureReason { get; set; } = string.Empty;
}
