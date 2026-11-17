using HawkNet.CoreUx.Common.Infrastructure;

namespace HawkNet.CoreUx.Client.World;

public sealed class WorldEntityViewModel : ObservableObject
{
    private const float DistanceEpsilon = 0.0001f;
    private float _worldX;
    private float _worldZ;
    private float _renderWorldX;
    private float _renderWorldZ;
    private float _destinationWorldX;
    private float _destinationWorldZ;
    private float _velocityX;
    private float _velocityZ;
    private float _faceForwardX;
    private float _faceForwardZ;
    private float _baseForwardX;
    private float _baseForwardZ;
    private float _rotationVelocity;
    private float _maxSpeed;
    private float _maxRotationSpeed;
    private int _currentPathIndex;
    private IReadOnlyList<WorldPoint> _pathWorldPositions = Array.Empty<WorldPoint>();
    private DateTime _serverUpdatedAtUtc;
    private float _blendStartWorldX;
    private float _blendStartWorldZ;
    private float _blendTargetWorldX;
    private float _blendTargetWorldZ;
    private float _blendElapsedSeconds;
    private float _blendDurationSeconds;
    private bool _isBlending;
    private bool _hasAppliedServerState;
    private string _movementStatus = string.Empty;
    private string _displayName = string.Empty;
    private bool _isHovered;
    private bool _isSelected;
    private bool _isLocalPlayer;
    private WorldCorrectionMode _lastCorrectionMode;

    public WorldEntityViewModel(WorldEntitySnapshot snapshot, int? localPlayerId)
        : this(snapshot, localPlayerId, WorldViewerConfig.CreateDefault())
    {
    }

    public WorldEntityViewModel(WorldEntitySnapshot snapshot, int? localPlayerId, WorldViewerConfig config)
    {
        Id = snapshot.Id;
        EntityType = snapshot.EntityType;
        PlayerId = snapshot.PlayerId;
        CanMove = snapshot.CanMove;
        Apply(snapshot, localPlayerId, config);
    }

    public string Id { get; }

    public string EntityType { get; }

    public int? PlayerId { get; }

    public bool CanMove { get; }

    public float WorldX
    {
        get => _worldX;
        private set => SetProperty(ref _worldX, value);
    }

    public float WorldZ
    {
        get => _worldZ;
        private set => SetProperty(ref _worldZ, value);
    }

    public float RenderWorldX
    {
        get => _renderWorldX;
        private set => SetProperty(ref _renderWorldX, value);
    }

    public float RenderWorldZ
    {
        get => _renderWorldZ;
        private set => SetProperty(ref _renderWorldZ, value);
    }

    public float DestinationWorldX
    {
        get => _destinationWorldX;
        private set => SetProperty(ref _destinationWorldX, value);
    }

    public float DestinationWorldZ
    {
        get => _destinationWorldZ;
        private set => SetProperty(ref _destinationWorldZ, value);
    }

    public float VelocityX
    {
        get => _velocityX;
        private set => SetProperty(ref _velocityX, value);
    }

    public float VelocityZ
    {
        get => _velocityZ;
        private set => SetProperty(ref _velocityZ, value);
    }

    public float FaceForwardX
    {
        get => _faceForwardX;
        private set => SetProperty(ref _faceForwardX, value);
    }

    public float FaceForwardZ
    {
        get => _faceForwardZ;
        private set => SetProperty(ref _faceForwardZ, value);
    }

    public float BaseForwardX
    {
        get => _baseForwardX;
        private set => SetProperty(ref _baseForwardX, value);
    }

    public float BaseForwardZ
    {
        get => _baseForwardZ;
        private set => SetProperty(ref _baseForwardZ, value);
    }

    public float RotationVelocity
    {
        get => _rotationVelocity;
        private set => SetProperty(ref _rotationVelocity, value);
    }

    public float MaxSpeed
    {
        get => _maxSpeed;
        private set => SetProperty(ref _maxSpeed, value);
    }

    public float MaxRotationSpeed
    {
        get => _maxRotationSpeed;
        private set => SetProperty(ref _maxRotationSpeed, value);
    }

    public int CurrentPathIndex
    {
        get => _currentPathIndex;
        private set => SetProperty(ref _currentPathIndex, value);
    }

    public IReadOnlyList<WorldPoint> PathWorldPositions
    {
        get => _pathWorldPositions;
        private set => SetProperty(ref _pathWorldPositions, value);
    }

    public DateTime ServerUpdatedAtUtc
    {
        get => _serverUpdatedAtUtc;
        private set => SetProperty(ref _serverUpdatedAtUtc, value);
    }

    public string MovementStatus
    {
        get => _movementStatus;
        private set => SetProperty(ref _movementStatus, value);
    }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public bool IsLocalPlayer
    {
        get => _isLocalPlayer;
        private set => SetProperty(ref _isLocalPlayer, value);
    }

    public bool IsHovered
    {
        get => _isHovered;
        set => SetProperty(ref _isHovered, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsBlending => _isBlending;

    public WorldCorrectionMode LastCorrectionMode
    {
        get => _lastCorrectionMode;
        private set => SetProperty(ref _lastCorrectionMode, value);
    }

    public void Apply(WorldEntitySnapshot snapshot, int? localPlayerId)
    {
        Apply(snapshot, localPlayerId, WorldViewerConfig.CreateDefault());
    }

    public void Apply(WorldEntitySnapshot snapshot, int? localPlayerId, WorldViewerConfig config)
    {
        WorldX = snapshot.WorldX;
        WorldZ = snapshot.WorldZ;
        DestinationWorldX = snapshot.DestinationWorldX;
        DestinationWorldZ = snapshot.DestinationWorldZ;
        VelocityX = snapshot.VelocityX;
        VelocityZ = snapshot.VelocityZ;
        FaceForwardX = snapshot.FaceForwardX;
        FaceForwardZ = snapshot.FaceForwardZ;
        BaseForwardX = snapshot.BaseForwardX;
        BaseForwardZ = snapshot.BaseForwardZ;
        RotationVelocity = snapshot.RotationVelocity;
        MaxSpeed = snapshot.MaxSpeed;
        MaxRotationSpeed = snapshot.MaxRotationSpeed;
        CurrentPathIndex = snapshot.CurrentPathIndex;
        PathWorldPositions = snapshot.PathWorldPositions.ToArray();
        ServerUpdatedAtUtc = snapshot.ServerUpdatedAtUtc;
        MovementStatus = snapshot.MovementStatus;
        DisplayName = string.IsNullOrWhiteSpace(snapshot.DisplayName)
            ? snapshot.Id
            : snapshot.DisplayName;
        IsLocalPlayer = PlayerId is not null && PlayerId == localPlayerId;
        ReconcileRenderPosition(config ?? WorldViewerConfig.CreateDefault());
    }

    public void Update(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        if (_isBlending)
        {
            UpdateBlend(deltaTime);
        }

        if (!ShouldMoveAlongPath() || MaxSpeed <= 0f)
        {
            return;
        }

        MoveAlongPath(deltaTime);
    }

    public static bool IsTerminalMovementStatus(string status)
    {
        return string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase);
    }

    private void ReconcileRenderPosition(WorldViewerConfig config)
    {
        if (!_hasAppliedServerState)
        {
            LastCorrectionMode = WorldCorrectionMode.None;
            SnapRenderToServer();
            _hasAppliedServerState = true;
            return;
        }

        WorldReconciliationConfig reconciliation = config.Reconciliation;
        if (!reconciliation.Enabled
            || (reconciliation.SnapOnFailedCanceledOrCompleted && IsTerminalMovementStatus(MovementStatus)))
        {
            LastCorrectionMode = WorldCorrectionMode.Snap;
            SnapRenderToServer();
            return;
        }

        float error = Distance(RenderWorldX, RenderWorldZ, WorldX, WorldZ);
        if (error <= reconciliation.IgnoreErrorDistance)
        {
            LastCorrectionMode = WorldCorrectionMode.Ignore;
            return;
        }

        if (error > reconciliation.BlendErrorDistance)
        {
            LastCorrectionMode = WorldCorrectionMode.Snap;
            SnapRenderToServer();
            return;
        }

        LastCorrectionMode = WorldCorrectionMode.Blend;
        StartBlend(reconciliation);
    }

    private void SnapRenderToServer()
    {
        _isBlending = false;
        RenderWorldX = WorldX;
        RenderWorldZ = WorldZ;
    }

    private void StartBlend(WorldReconciliationConfig reconciliation)
    {
        _blendStartWorldX = RenderWorldX;
        _blendStartWorldZ = RenderWorldZ;
        WorldPoint target = GetCorrectionTarget();
        _blendTargetWorldX = target.X;
        _blendTargetWorldZ = target.Z;
        _blendElapsedSeconds = 0f;
        _blendDurationSeconds = Math.Max(0.001f, reconciliation.BlendMilliseconds / 1000f);
        _isBlending = true;
        OnPropertyChanged(nameof(IsBlending));
    }

    private WorldPoint GetCorrectionTarget()
    {
        if (PathWorldPositions.Count == 0)
        {
            return new WorldPoint(WorldX, WorldZ);
        }

        int targetIndex = Math.Clamp(CurrentPathIndex, 0, PathWorldPositions.Count - 1);
        return PathWorldPositions[targetIndex];
    }

    private void UpdateBlend(float deltaTime)
    {
        _blendElapsedSeconds += deltaTime;
        float t = Math.Clamp(_blendElapsedSeconds / _blendDurationSeconds, 0f, 1f);
        RenderWorldX = Lerp(_blendStartWorldX, _blendTargetWorldX, t);
        RenderWorldZ = Lerp(_blendStartWorldZ, _blendTargetWorldZ, t);
        if (t >= 1f)
        {
            _isBlending = false;
            OnPropertyChanged(nameof(IsBlending));
        }
    }

    private void MoveAlongPath(float deltaTime)
    {
        if (PathWorldPositions.Count == 0)
        {
            return;
        }

        float remainingStep = MaxSpeed * deltaTime;
        int targetIndex = Math.Clamp(CurrentPathIndex, 0, PathWorldPositions.Count - 1);
        while (remainingStep > 0f && targetIndex < PathWorldPositions.Count)
        {
            WorldPoint target = PathWorldPositions[targetIndex];
            float distance = Distance(RenderWorldX, RenderWorldZ, target.X, target.Z);
            if (distance <= DistanceEpsilon)
            {
                targetIndex++;
                continue;
            }

            if (remainingStep >= distance)
            {
                RenderWorldX = target.X;
                RenderWorldZ = target.Z;
                remainingStep -= distance;
                targetIndex++;
                continue;
            }

            float ratio = remainingStep / distance;
            RenderWorldX = Lerp(RenderWorldX, target.X, ratio);
            RenderWorldZ = Lerp(RenderWorldZ, target.Z, ratio);
            break;
        }
    }

    private static bool IsActiveMovementStatus(string status)
    {
        return string.Equals(status, "Starting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Moving", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldMoveAlongPath()
    {
        if (IsActiveMovementStatus(MovementStatus))
        {
            return true;
        }

        return string.Equals(MovementStatus, "Continuing", StringComparison.OrdinalIgnoreCase)
            && PathWorldPositions.Count > 0
            && CurrentPathIndex < PathWorldPositions.Count;
    }

    private static float Distance(float ax, float az, float bx, float bz)
    {
        float dx = bx - ax;
        float dz = bz - az;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    private static float Lerp(float start, float end, float amount)
    {
        return start + ((end - start) * amount);
    }
}
