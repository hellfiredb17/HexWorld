using System.Text.Json.Serialization;
using ClanIronSim.Entity.Movement;
using ClanIronSim.Events;
using ClanIronSim.SimMath;
using ClanIronSim.World;

namespace ClanIronSim.Entity;

public class NavigatorEntity : InteractableEntity
{
    private NavigatorMovementBehaviorType _movementBehaviorType = NavigatorMovementBehaviorType.Linear;
    private INavigatorMovementRuntimeBehavior? _movementRuntimeBehavior;
    private ISimEventDispatcher _eventDispatcher = NullSimEventDispatcher.Instance;

    public override bool CanMove => true;

    public SimVector2 FaceForward { get; set; } = SimVector2.Forward;

    public SimVector2 BaseForward { get; set; } = SimVector2.Forward;

    public SimVector2 Velocity { get; set; } = SimVector2.Zero;

    public SimVector2 CurrentWorldPosition { get; set; } = SimVector2.Zero;

    public float RotationVelocity { get; set; }

    public float MaxSpeed { get; set; } = 1;

    public float MaxRotationSpeed { get; set; } = 1;

    public float DecelerationRate { get; set; } = 1;

    public NavigatorPathState PathState { get; set; } = new();

    public NavigatorMovementBehaviorType MovementBehaviorType
    {
        get => _movementBehaviorType;
        set
        {
            _movementBehaviorType = value;
            _movementRuntimeBehavior = NavigatorMovementRuntimeBehaviorFactory.Create(value);
        }
    }

    [JsonIgnore]
    public INavigatorMovementRuntimeBehavior MovementRuntimeBehavior
    {
        get
        {
            _movementRuntimeBehavior ??= NavigatorMovementRuntimeBehaviorFactory.Create(MovementBehaviorType);
            return _movementRuntimeBehavior;
        }
        set => _movementRuntimeBehavior = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void SetPath(IEnumerable<MapLocation> waypoints)
    {
        SetPath(waypoints, NullSimEventDispatcher.Instance);
    }

    public void SetPath(IEnumerable<MapLocation> waypoints, ISimEventDispatcher events)
    {
        if (waypoints is null)
        {
            throw new ArgumentNullException(nameof(waypoints));
        }

        List<MapLocation> waypointList = waypoints.ToList();
        PathState = new NavigatorPathState
        {
            Status = waypointList.Count == 0
                ? NavigatorMovementStatus.Failed
                : NavigatorMovementStatus.Moving,
            Waypoints = waypointList,
            CurrentWaypointIndex = 0,
            Destination = waypointList.Count == 0 ? default : waypointList[^1],
            FailureReason = waypointList.Count == 0 ? "Navigator path cannot be empty." : string.Empty
        };
        Velocity = SimVector2.Zero;
        RotationVelocity = 0;

        AdvancePastReachedWaypoints();
        if (PathState.Status == NavigatorMovementStatus.Failed)
        {
            DispatchMoveEvent(events, MoveStatus.Failed);
        }
        else if (PathState.Status == NavigatorMovementStatus.Completed)
        {
            DispatchMoveEvent(events, MoveStatus.Completed);
        }
        else if (PathState.Status == NavigatorMovementStatus.Moving)
        {
            DispatchMoveEvent(events, MoveStatus.Starting);
        }
    }

    public void ClearPath()
    {
        PathState = new NavigatorPathState();
        Velocity = SimVector2.Zero;
        RotationVelocity = 0;
    }

    public void CancelMovement()
    {
        CancelMovement(NullSimEventDispatcher.Instance);
    }

    public void CancelMovement(ISimEventDispatcher events)
    {
        Velocity = SimVector2.Zero;
        RotationVelocity = 0;
        PathState.Status = NavigatorMovementStatus.Canceled;
        DispatchMoveEvent(events, MoveStatus.Canceled);
    }

    public override void Update(float deltaTime)
    {
        Update(deltaTime, NullSimEventDispatcher.Instance);
    }

    public override void Update(float deltaTime, ISimEventDispatcher events)
    {
        if (deltaTime <= 0 || PathState.Status != NavigatorMovementStatus.Moving)
        {
            return;
        }

        if (PathState.Waypoints.Count == 0
            || PathState.CurrentWaypointIndex < 0
            || PathState.CurrentWaypointIndex >= PathState.Waypoints.Count)
        {
            FailMovement("Navigator path has no current waypoint.");
            return;
        }

        _eventDispatcher = events ?? NullSimEventDispatcher.Instance;
        try
        {
            MovementRuntimeBehavior.Update(this, deltaTime);
        }
        finally
        {
            _eventDispatcher = NullSimEventDispatcher.Instance;
        }
    }

    internal MapLocation GetCurrentWaypoint()
    {
        return PathState.Waypoints[PathState.CurrentWaypointIndex];
    }

    internal void ArriveAtCurrentWaypoint()
    {
        ArriveAtCurrentWaypoint(raiseEvent: true);
    }

    private void ArriveAtCurrentWaypoint(bool raiseEvent)
    {
        int arrivedIndex = PathState.CurrentWaypointIndex;
        Location = PathState.Waypoints[arrivedIndex];
        CurrentWorldPosition = Location.WorldPosition;
        PathState.CurrentWaypointIndex++;
        if (PathState.CurrentWaypointIndex >= PathState.Waypoints.Count)
        {
            PathState.Status = NavigatorMovementStatus.Completed;
            Velocity = SimVector2.Zero;
            RotationVelocity = 0;
            if (raiseEvent)
            {
                DispatchMoveEvent(_eventDispatcher, MoveStatus.Completed);
            }

            return;
        }

        if (raiseEvent)
        {
            DispatchMoveEvent(_eventDispatcher, MoveStatus.Continuing);
        }
    }

    private void AdvancePastReachedWaypoints()
    {
        while (PathState.Status == NavigatorMovementStatus.Moving
            && PathState.CurrentWaypointIndex < PathState.Waypoints.Count
            && IsAt(PathState.Waypoints[PathState.CurrentWaypointIndex].WorldPosition))
        {
            ArriveAtCurrentWaypoint(raiseEvent: false);
        }
    }

    private bool IsAt(SimVector2 position)
    {
        return position.DistanceTo(CurrentWorldPosition) <= ArrivalTolerance;
    }

    internal void FailMovement(string reason)
    {
        PathState.Status = NavigatorMovementStatus.Failed;
        PathState.FailureReason = reason;
        Velocity = SimVector2.Zero;
        RotationVelocity = 0;
        DispatchMoveEvent(_eventDispatcher, MoveStatus.Failed);
    }

    private void DispatchMoveEvent(ISimEventDispatcher events, MoveStatus status)
    {
        events.Dispatch(new MoveStatusChangeEvent
        {
            Payload = CreateMoveEventPayload(status)
        });
    }

    private MoveEventPayload CreateMoveEventPayload(MoveStatus status)
    {
        SimVector2 destination = PathState.Waypoints.Count > 0
            ? PathState.Waypoints[^1].WorldPosition
            : CurrentWorldPosition;
        return new MoveEventPayload
        {
            Status = status,
            MoverId = Id,
            Path = PathState.Waypoints.ToList(),
            PathWorldPositions = PathState.Waypoints.Select(location => location.WorldPosition).ToList(),
            CurrentIndex = PathState.CurrentWaypointIndex,
            CurrentWorldPosition = CurrentWorldPosition,
            DestinationWorldPosition = destination,
            Velocity = Velocity,
            FaceForward = FaceForward,
            BaseForward = BaseForward,
            RotationVelocity = RotationVelocity,
            MaxSpeed = MaxSpeed,
            MaxRotationSpeed = MaxRotationSpeed,
            FailureReason = PathState.FailureReason
        };
    }

    internal const float ArrivalTolerance = 0.001f;
}
