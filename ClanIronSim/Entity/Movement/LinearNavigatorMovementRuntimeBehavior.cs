using ClanIronSim.SimMath;
using ClanIronSim.World;

namespace ClanIronSim.Entity.Movement;

public sealed class LinearNavigatorMovementRuntimeBehavior : INavigatorMovementRuntimeBehavior
{
    public void Update(NavigatorEntity navigator, float deltaTime)
    {
        MapLocation waypoint = navigator.GetCurrentWaypoint();
        SimVector2 toWaypoint = waypoint.WorldPosition - navigator.CurrentWorldPosition;
        float distance = toWaypoint.Length;
        if (distance <= NavigatorEntity.ArrivalTolerance)
        {
            navigator.ArriveAtCurrentWaypoint();
            return;
        }

        float maxStep = System.Math.Max(0, navigator.MaxSpeed) * deltaTime;
        if (maxStep <= 0)
        {
            navigator.Velocity = SimVector2.Zero;
            navigator.RotationVelocity = 0;
            return;
        }

        SimVector2 direction = toWaypoint.Normalize();
        navigator.BaseForward = direction;
        navigator.RotationVelocity = 0;
        navigator.Velocity = direction * System.Math.Min(navigator.MaxSpeed, distance / deltaTime);

        if (maxStep >= distance)
        {
            navigator.CurrentWorldPosition = waypoint.WorldPosition;
            navigator.ArriveAtCurrentWaypoint();
            return;
        }

        navigator.CurrentWorldPosition += direction * maxStep;
    }
}
