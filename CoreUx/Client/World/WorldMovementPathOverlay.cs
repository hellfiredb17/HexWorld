namespace HawkNet.CoreUx.Client.World;

public enum WorldMovementPathScope
{
    LocalPlayer,
    AllMovingEntities
}

public sealed record WorldMovementPathOverlay(
    WorldEntityViewModel Entity,
    IReadOnlyList<WorldPoint> Points,
    WorldPoint CurrentTarget,
    WorldPoint Destination);
