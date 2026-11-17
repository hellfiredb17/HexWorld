namespace HawkNet.CoreUx.Client.World;

public sealed record WorldEntitySnapshot
{
    public string Id { get; init; } = string.Empty;

    public string EntityType { get; init; } = string.Empty;

    public int? PlayerId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public float WorldX { get; init; }

    public float WorldZ { get; init; }

    public float DestinationWorldX { get; init; }

    public float DestinationWorldZ { get; init; }

    public float VelocityX { get; init; }

    public float VelocityZ { get; init; }

    public float FaceForwardX { get; init; }

    public float FaceForwardZ { get; init; }

    public float BaseForwardX { get; init; }

    public float BaseForwardZ { get; init; }

    public float RotationVelocity { get; init; }

    public float MaxSpeed { get; init; }

    public float MaxRotationSpeed { get; init; }

    public int CurrentPathIndex { get; init; }

    public IReadOnlyList<WorldPoint> PathWorldPositions { get; init; } = Array.Empty<WorldPoint>();

    public DateTime ServerUpdatedAtUtc { get; init; }

    public string MovementStatus { get; init; } = string.Empty;

    public bool CanMove { get; init; }
}
