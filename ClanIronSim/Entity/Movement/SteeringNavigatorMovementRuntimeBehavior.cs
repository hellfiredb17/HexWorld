using ClanIronSim.SimMath;
using ClanIronSim.World;

namespace ClanIronSim.Entity.Movement;

public sealed class SteeringNavigatorMovementRuntimeBehavior : INavigatorMovementRuntimeBehavior
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

        SimVector2 targetDirection = toWaypoint.Normalize();
        SimVector2 currentForward = navigator.BaseForward.Normalize();
        if (currentForward.Length <= SimMathConstants.Epsilon)
        {
            currentForward = SimVector2.Forward;
        }

        float signedAngle = currentForward.SignedAngleToDegrees(targetDirection);
        float maxTurn = System.Math.Max(0, navigator.MaxRotationSpeed) * deltaTime;
        float turnDegrees = SimMathf.Clamp(signedAngle, -maxTurn, maxTurn);
        navigator.BaseForward = currentForward.RotateDegrees(turnDegrees).Normalize();
        navigator.RotationVelocity = deltaTime > 0 ? turnDegrees / deltaTime : 0;

        if (System.Math.Abs(signedAngle) >= 90f)
        {
            navigator.Velocity = SimVector2.Zero;
            return;
        }

        float maxStep = System.Math.Max(0, navigator.MaxSpeed) * deltaTime;
        if (maxStep <= 0)
        {
            navigator.Velocity = SimVector2.Zero;
            return;
        }

        float previousDistance = distance;
        float speed = System.Math.Min(navigator.MaxSpeed, distance / deltaTime);
        navigator.Velocity = navigator.BaseForward * speed;
        SimVector2 nextPosition = navigator.CurrentWorldPosition + (navigator.BaseForward * maxStep);
        float nextDistance = nextPosition.DistanceTo(waypoint.WorldPosition);

        if (nextDistance <= NavigatorEntity.ArrivalTolerance
            || (maxStep >= previousDistance && nextDistance >= previousDistance))
        {
            navigator.CurrentWorldPosition = waypoint.WorldPosition;
            navigator.ArriveAtCurrentWaypoint();
            return;
        }

        navigator.CurrentWorldPosition = nextPosition;
    }
}
