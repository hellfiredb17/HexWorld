namespace ClanIronSim.Entity.Movement;

public static class NavigatorMovementRuntimeBehaviorFactory
{
    public static INavigatorMovementRuntimeBehavior Create(NavigatorMovementBehaviorType behaviorType)
    {
        return behaviorType switch
        {
            NavigatorMovementBehaviorType.Linear => new LinearNavigatorMovementRuntimeBehavior(),
            NavigatorMovementBehaviorType.Steering => new SteeringNavigatorMovementRuntimeBehavior(),
            _ => throw new ArgumentOutOfRangeException(nameof(behaviorType), behaviorType, "Unsupported navigator movement behavior.")
        };
    }
}
