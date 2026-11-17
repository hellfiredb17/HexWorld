using System.Collections.ObjectModel;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Common.Infrastructure;

namespace HawkNet.CoreUx.Client.World;

public sealed class WorldViewModel : ObservableObject
{
    private readonly Dictionary<int, MatchPlayer> _playersById = new();
    private string _regionId = string.Empty;
    private string _regionName = string.Empty;
    private int? _localPlayerId;
    private WorldTileViewModel? _hoveredTile;
    private WorldTileViewModel? _selectedTile;
    private WorldEntityViewModel? _hoveredEntity;
    private WorldEntityViewModel? _selectedEntity;
    private string _statusText = "World view waiting for state.";
    private bool _showMovementPaths;
    private bool _showServerPositionMarkers;
    private bool _showRenderPositionMarkers;
    private bool _showCorrectionLabels;
    private WorldMovementPathScope _movementPathScope = WorldMovementPathScope.LocalPlayer;
	private long _topologyRevision = -1;
	private long _entityRevision = -1;

    public WorldViewModel()
        : this(WorldViewerConfig.CreateDefault())
    {
    }

    public WorldViewModel(WorldViewerConfig config)
    {
        Config = config ?? WorldViewerConfig.CreateDefault();
    }

    public WorldViewerConfig Config { get; }

    public ObservableCollection<WorldTileViewModel> Tiles { get; } = new();

    public ObservableCollection<WorldEntityViewModel> Entities { get; } = new();

    public event EventHandler<WorldMoveRequestEventArgs>? MoveRequested;
	public event EventHandler<WorldEntityViewModel>? EntityFocusRequested;

    public void Update(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        foreach (WorldEntityViewModel entity in Entities)
        {
            entity.Update(deltaTime);
        }
    }

    public string RegionId
    {
        get => _regionId;
        private set => SetProperty(ref _regionId, value);
    }

    public string RegionName
    {
        get => _regionName;
        private set => SetProperty(ref _regionName, value);
    }

    public int? LocalPlayerId
    {
        get => _localPlayerId;
        private set => SetProperty(ref _localPlayerId, value);
    }

    public WorldTileViewModel? HoveredTile
    {
        get => _hoveredTile;
        private set
        {
            if (ReferenceEquals(_hoveredTile, value))
            {
                if (value is not null)
                {
                    value.IsHovered = true;
                }

                return;
            }

            if (_hoveredTile is not null)
            {
                _hoveredTile.IsHovered = false;
            }

            if (SetProperty(ref _hoveredTile, value) && value is not null)
            {
                value.IsHovered = true;
                StatusText = value.IsBlocked
                    ? $"Tile {value.DisplayText} is blocked."
                    : $"Tile {value.DisplayText} cost {value.MovementCost}.";
            }
        }
    }

    public WorldTileViewModel? SelectedTile
    {
        get => _selectedTile;
        private set
        {
            if (_selectedTile is not null)
            {
                _selectedTile.IsSelected = false;
            }

            if (SetProperty(ref _selectedTile, value) && value is not null)
            {
                value.IsSelected = true;
            }
        }
    }

    public WorldEntityViewModel? HoveredEntity
    {
        get => _hoveredEntity;
        private set
        {
            if (ReferenceEquals(_hoveredEntity, value))
            {
                if (value is not null)
                {
                    value.IsHovered = true;
                }

                return;
            }

            if (_hoveredEntity is not null)
            {
                _hoveredEntity.IsHovered = false;
            }

            if (SetProperty(ref _hoveredEntity, value) && value is not null)
            {
                value.IsHovered = true;
                StatusText = string.IsNullOrWhiteSpace(value.MovementStatus)
                    ? value.DisplayName
                    : $"{value.DisplayName}: {value.MovementStatus}";
            }
        }
    }

    public WorldEntityViewModel? SelectedEntity
    {
        get => _selectedEntity;
        private set
        {
            if (_selectedEntity is not null)
            {
                _selectedEntity.IsSelected = false;
            }

            if (SetProperty(ref _selectedEntity, value) && value is not null)
            {
                value.IsSelected = true;
            }

            OnPropertyChanged(nameof(HasSelectedEntity));
            OnPropertyChanged(nameof(SelectedEntityDisplayName));
            OnPropertyChanged(nameof(SelectedEntityMovementStatus));
        }
    }

    public bool HasSelectedEntity => SelectedEntity is not null;

    public string SelectedEntityDisplayName => SelectedEntity?.DisplayName ?? string.Empty;

    public string SelectedEntityMovementStatus => SelectedEntity?.MovementStatus ?? string.Empty;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool ShowMovementPaths
    {
        get => _showMovementPaths;
        set
        {
            if (SetProperty(ref _showMovementPaths, value))
            {
                OnPropertyChanged(nameof(VisibleMovementPathOverlays));
            }
        }
    }

    public WorldMovementPathScope MovementPathScope
    {
        get => _movementPathScope;
        set
        {
            if (SetProperty(ref _movementPathScope, value))
            {
                OnPropertyChanged(nameof(VisibleMovementPathOverlays));
                OnPropertyChanged(nameof(VisibleMovementDebugEntities));
            }
        }
    }

    public IReadOnlyList<WorldMovementPathOverlay> VisibleMovementPathOverlays => GetVisibleMovementPathOverlays();

    public bool ShowServerPositionMarkers
    {
        get => _showServerPositionMarkers;
        set
        {
            if (SetProperty(ref _showServerPositionMarkers, value))
            {
                OnPropertyChanged(nameof(VisibleMovementDebugEntities));
            }
        }
    }

    public bool ShowRenderPositionMarkers
    {
        get => _showRenderPositionMarkers;
        set
        {
            if (SetProperty(ref _showRenderPositionMarkers, value))
            {
                OnPropertyChanged(nameof(VisibleMovementDebugEntities));
            }
        }
    }

    public bool ShowCorrectionLabels
    {
        get => _showCorrectionLabels;
        set
        {
            if (SetProperty(ref _showCorrectionLabels, value))
            {
                OnPropertyChanged(nameof(VisibleMovementDebugEntities));
            }
        }
    }

    public IReadOnlyList<WorldEntityViewModel> VisibleMovementDebugEntities => GetVisibleMovementDebugEntities();

    public void ApplyPlayers(IEnumerable<MatchPlayer> players, int? localPlayerId)
    {
        _playersById.Clear();
        foreach (MatchPlayer player in players)
        {
            _playersById[player.PlayerId] = player;
        }

        LocalPlayerId = localPlayerId;
        foreach (WorldEntityViewModel entity in Entities)
        {
            if (entity.PlayerId is int playerId && _playersById.TryGetValue(playerId, out MatchPlayer? player))
            {
                entity.Apply(new WorldEntitySnapshot
                {
                    Id = entity.Id,
                    EntityType = entity.EntityType,
                    PlayerId = entity.PlayerId,
                    DisplayName = player.DisplayName,
                    WorldX = entity.WorldX,
                    WorldZ = entity.WorldZ,
                    DestinationWorldX = entity.DestinationWorldX,
                    DestinationWorldZ = entity.DestinationWorldZ,
                    VelocityX = entity.VelocityX,
                    VelocityZ = entity.VelocityZ,
                    FaceForwardX = entity.FaceForwardX,
                    FaceForwardZ = entity.FaceForwardZ,
                    BaseForwardX = entity.BaseForwardX,
                    BaseForwardZ = entity.BaseForwardZ,
                    RotationVelocity = entity.RotationVelocity,
                    MaxSpeed = entity.MaxSpeed,
                    MaxRotationSpeed = entity.MaxRotationSpeed,
                    CurrentPathIndex = entity.CurrentPathIndex,
                    PathWorldPositions = entity.PathWorldPositions,
                    ServerUpdatedAtUtc = entity.ServerUpdatedAtUtc,
                    MovementStatus = entity.MovementStatus,
                    CanMove = entity.CanMove
                }, localPlayerId, Config);
            }
        }

        OnPropertyChanged(nameof(VisibleMovementPathOverlays));
        OnPropertyChanged(nameof(VisibleMovementDebugEntities));
    }

    public void ApplySnapshot(WorldSnapshot snapshot)
    {
		bool regionChanged = !string.Equals(RegionId, snapshot.RegionId, StringComparison.OrdinalIgnoreCase);
        RegionId = snapshot.RegionId;
        RegionName = string.IsNullOrWhiteSpace(snapshot.RegionName) ? snapshot.RegionId : snapshot.RegionName;
        LocalPlayerId = snapshot.LocalPlayerId;
        string? hoveredTileId = HoveredTile?.Id;
        string? hoveredEntityId = HoveredEntity?.Id;
        string? selectedTileId = SelectedTile?.Id;
        string? selectedEntityId = SelectedEntity?.Id;
		bool topologyChanged = regionChanged || _topologyRevision != snapshot.TopologyRevision
			|| (snapshot.TopologyRevision == 0 && !Tiles.Select(tile => tile.Id).SequenceEqual(snapshot.Tiles.Select(tile => tile.Id)));
		if (topologyChanged)
		{
			Tiles.Clear();
			foreach (WorldTileSnapshot tile in snapshot.Tiles) Tiles.Add(new WorldTileViewModel(tile));
			_topologyRevision = snapshot.TopologyRevision;
		}

		if (regionChanged || snapshot.EntityRevision == 0 || snapshot.EntityRevision > _entityRevision)
		{
			var incoming = snapshot.Entities.ToDictionary(entity => entity.Id, StringComparer.OrdinalIgnoreCase);
			for (int index = Entities.Count - 1; index >= 0; index--)
				if (!incoming.ContainsKey(Entities[index].Id)) Entities.RemoveAt(index);
			var existing = Entities.ToDictionary(entity => entity.Id, StringComparer.OrdinalIgnoreCase);
			foreach (WorldEntitySnapshot entitySnapshot in snapshot.Entities)
			{
				WorldEntitySnapshot named = WithDisplayName(entitySnapshot);
				if (existing.TryGetValue(named.Id, out WorldEntityViewModel? entity)) entity.Apply(named, snapshot.LocalPlayerId, Config);
				else Entities.Add(CreateEntityViewModel(named, snapshot.LocalPlayerId));
			}
			_entityRevision = snapshot.EntityRevision;
		}

        SelectedTile = Tiles.FirstOrDefault(tile => string.Equals(tile.Id, selectedTileId, StringComparison.OrdinalIgnoreCase));
        SelectedEntity = Entities.FirstOrDefault(entity => string.Equals(entity.Id, selectedEntityId, StringComparison.OrdinalIgnoreCase));
        HoveredTile = Tiles.FirstOrDefault(tile => string.Equals(tile.Id, hoveredTileId, StringComparison.OrdinalIgnoreCase));
        HoveredEntity = Entities.FirstOrDefault(entity => string.Equals(entity.Id, hoveredEntityId, StringComparison.OrdinalIgnoreCase));

        StatusText = Tiles.Count == 0
            ? "World state loaded with no tiles."
            : $"World state loaded: {Tiles.Count} tiles, {Entities.Count} entities.";
        OnPropertyChanged(nameof(VisibleMovementPathOverlays));
        OnPropertyChanged(nameof(VisibleMovementDebugEntities));
    }

    public void ApplyEntity(WorldEntitySnapshot snapshot)
    {
        WorldEntitySnapshot named = WithDisplayName(snapshot);
        WorldEntityViewModel? entity = Entities.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, snapshot.Id, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            Entities.Add(CreateEntityViewModel(named, LocalPlayerId));
        }
        else
        {
            entity.Apply(named, LocalPlayerId, Config);
        }

        StatusText = string.IsNullOrWhiteSpace(named.MovementStatus)
            ? $"{named.DisplayName} updated."
            : $"{named.DisplayName}: {named.MovementStatus}.";
        OnPropertyChanged(nameof(VisibleMovementPathOverlays));
        OnPropertyChanged(nameof(VisibleMovementDebugEntities));
    }

    public void HoverTile(WorldTileViewModel? tile)
    {
        HoveredTile = tile;
    }

    public void HoverEntity(WorldEntityViewModel? entity)
    {
        HoveredEntity = entity;
    }

    public void SelectEntity(WorldEntityViewModel? entity)
    {
        SelectedEntity = entity;
        if (entity is not null)
        {
            StatusText = string.IsNullOrWhiteSpace(entity.MovementStatus)
                ? entity.DisplayName
                : $"{entity.DisplayName}: {entity.MovementStatus}";
        }
    }

	public void FocusEntity(WorldEntityViewModel entity)
	{
		SelectEntity(entity);
		EntityFocusRequested?.Invoke(this, entity);
	}

    public void ClickTile(WorldTileViewModel tile)
    {
        if (tile is null)
        {
            return;
        }

        SelectedTile = tile;
        if (tile.IsBlocked)
        {
            StatusText = $"Tile {tile.DisplayText} is blocked.";
            return;
        }

        StatusText = $"Moving to tile {tile.DisplayText}.";
        MoveRequested?.Invoke(this, new WorldMoveRequestEventArgs(tile));
    }

    public void ClearSelection()
    {
        SelectedTile = null;
        SelectedEntity = null;
        StatusText = "Selection cleared.";
    }

    private WorldEntitySnapshot WithDisplayName(WorldEntitySnapshot snapshot)
    {
        if (snapshot.PlayerId is int playerId
            && _playersById.TryGetValue(playerId, out MatchPlayer? player)
            && !string.IsNullOrWhiteSpace(player.DisplayName))
        {
            return snapshot with { DisplayName = player.DisplayName };
        }

        return snapshot;
    }

    private WorldEntityViewModel CreateEntityViewModel(WorldEntitySnapshot snapshot, int? localPlayerId)
    {
        return new WorldEntityViewModel(snapshot, localPlayerId, Config);
    }

    private IReadOnlyList<WorldMovementPathOverlay> GetVisibleMovementPathOverlays()
    {
        if (!ShowMovementPaths)
        {
            return Array.Empty<WorldMovementPathOverlay>();
        }

        var overlays = new List<WorldMovementPathOverlay>();
        foreach (WorldEntityViewModel entity in Entities)
        {
            if (!ShouldShowMovementPath(entity))
            {
                continue;
            }

            WorldMovementPathOverlay? overlay = CreateMovementPathOverlay(entity);
            if (overlay is not null)
            {
                overlays.Add(overlay);
            }
        }

        return overlays;
    }

    private bool ShouldShowMovementPath(WorldEntityViewModel entity)
    {
        return ShouldShowMovementDebugEntity(entity);
    }

    private IReadOnlyList<WorldEntityViewModel> GetVisibleMovementDebugEntities()
    {
        if (!ShowServerPositionMarkers && !ShowRenderPositionMarkers && !ShowCorrectionLabels)
        {
            return Array.Empty<WorldEntityViewModel>();
        }

        return Entities.Where(ShouldShowMovementDebugEntity).ToArray();
    }

    private bool ShouldShowMovementDebugEntity(WorldEntityViewModel entity)
    {
        if (MovementPathScope == WorldMovementPathScope.LocalPlayer && !entity.IsLocalPlayer)
        {
            return false;
        }

        return IsActiveMovementStatus(entity.MovementStatus);
    }

    private static WorldMovementPathOverlay? CreateMovementPathOverlay(WorldEntityViewModel entity)
    {
        if (entity.PathWorldPositions.Count == 0)
        {
            return null;
        }

        int currentIndex = Math.Clamp(entity.CurrentPathIndex, 0, entity.PathWorldPositions.Count - 1);
        var points = new List<WorldPoint>
        {
            new(entity.RenderWorldX, entity.RenderWorldZ)
        };

        for (int index = currentIndex; index < entity.PathWorldPositions.Count; index++)
        {
            points.Add(entity.PathWorldPositions[index]);
        }

        return new WorldMovementPathOverlay(
            entity,
            points,
            entity.PathWorldPositions[currentIndex],
            entity.PathWorldPositions[^1]);
    }

    private static bool IsActiveMovementStatus(string status)
    {
        return string.Equals(status, "Starting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Moving", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Continuing", StringComparison.OrdinalIgnoreCase);
    }
}
