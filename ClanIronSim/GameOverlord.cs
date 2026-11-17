using ClanIronSim.Commands;
using ClanIronSim.Entity;
using ClanIronSim.Events;
using ClanIronSim.Player;
using ClanIronSim.SimMath;
using ClanIronSim.World;
using SimWorldState = ClanIronSim.WorldState.WorldState;

namespace ClanIronSim;

public sealed class GameOverlord : ISimEventDispatcher
{
    private readonly object _commandLock = new();
    private readonly object _actionLock = new();
    private readonly List<QueuedSimCommand> _pendingCommands = new();
    private readonly List<SimAction> _pendingActions = new();
    private long _nextServerOrder;

    public GameOverlord()
        : this(new SimWorldState())
    {
    }

    public GameOverlord(SimWorldState world)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
    }

    public SimWorldState World { get; }

    public PlayerService Players => World.Players;

    public long CurrentTick { get; private set; }

    public static GameOverlord FromWorldStateJson(string json)
    {
        return new GameOverlord(SimWorldState.FromJson(json));
    }

    public static async Task<GameOverlord> LoadWorldStateAsync(string path, CancellationToken token = default)
    {
        return new GameOverlord(await SimWorldState.LoadAsync(path, token).ConfigureAwait(false));
    }

    public bool AssignPlayerNavigator(int playerId, string navigatorId)
    {
        if (string.IsNullOrWhiteSpace(navigatorId)
            || !World.TryGetNavigator(navigatorId, out NavigatorEntity? navigator))
        {
            return false;
        }

        string controlledNavigatorId = navigator!.Id;
        return Players.TryAssignNavigator(playerId, controlledNavigatorId);
    }

    public bool UnassignPlayerNavigator(int playerId)
    {
        return Players.TryUnassignNavigator(playerId);
    }

    public bool TryGetPlayerNavigator(int playerId, out NavigatorEntity? navigator)
    {
        navigator = null;
        if (!Players.TryGetNavigatorId(playerId, out string navigatorId))
        {
            return false;
        }

        return World.TryGetNavigator(navigatorId, out navigator);
    }

    public bool TryAddPlayer(PlayerProfile profile, out NavigatorEntity? navigator)
    {
        navigator = null;
        if (profile is null
            || Players.TryGetProfile(profile.Id, out _)
            || !TryGetOpenPlayerSpawner(out MapRegion? region, out PlacedMapSection? section, out MapTile? tile))
        {
            return false;
        }

        string navigatorId = $"player_{profile.Id}_nav";
        if (World.TryGetInteractable(navigatorId, out _))
        {
            return false;
        }

        var playerNavigator = new NavigatorEntity
        {
            Id = navigatorId
        };
        if (!World.TryCreateInteractable(playerNavigator, region!.Id, section!.Id, tile!.GridPosition))
        {
            return false;
        }

        if (!Players.TryRegisterPlayer(profile))
        {
            World.Interactables.Remove(playerNavigator);
            return false;
        }

        if (!Players.TryAssignNavigator(profile.Id, playerNavigator.Id))
        {
            Players.TryRemovePlayer(profile.Id);
            World.Interactables.Remove(playerNavigator);
            return false;
        }

        navigator = playerNavigator;
        return true;
    }

    public bool TryRemovePlayer(int playerId)
    {
        return Players.TryRemovePlayer(playerId);
    }

    public Task<SimResponse> RunRequestAsync(SimRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (request is null)
        {
            return Task.FromResult(SimResponse.Failed("Sim request cannot be null."));
        }

        SimResponse response = request.Type switch
        {
            SimRequestType.SimEmpty => SimResponse.Empty(),
            SimRequestType.RequestRegionState => BuildRegionStateResponse(request.RequestRegionState),
            _ => SimResponse.Failed($"Unsupported sim request type '{request.Type}'.")
        };
        return Task.FromResult(response);
    }

    public void EnqueueCommand(SimCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        lock (_commandLock)
        {
            _pendingCommands.Add(new QueuedSimCommand(_nextServerOrder++, command));
        }
    }

    public void EmitAction(SimAction action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        action.Tick = CurrentTick;
        lock (_actionLock)
        {
            _pendingActions.Add(action);
        }
    }

    public void Dispatch(SimEvent simEvent)
    {
        if (simEvent is null)
        {
            throw new ArgumentNullException(nameof(simEvent));
        }

        switch (simEvent)
        {
            case MoveStatusChangeEvent moveStatusChange:
                EmitMoveAction(moveStatusChange.Payload);
                break;
            default:
                break;
        }
    }

    public Task<IReadOnlyList<SimAction>> UpdateAsync(float deltaTime, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        List<QueuedSimCommand> commands = CapturePendingCommands();
        foreach (QueuedSimCommand queuedCommand in commands.OrderBy(command => command.ServerOrder))
        {
            token.ThrowIfCancellationRequested();
            ProcessCommand(queuedCommand.Command);
        }

        World.Update(deltaTime, this);
        IReadOnlyList<SimAction> actions = CapturePendingActions();
        CurrentTick++;
        return Task.FromResult(actions);
    }

    private List<QueuedSimCommand> CapturePendingCommands()
    {
        lock (_commandLock)
        {
            var commands = _pendingCommands.ToList();
            _pendingCommands.Clear();
            return commands;
        }
    }

    private IReadOnlyList<SimAction> CapturePendingActions()
    {
        lock (_actionLock)
        {
            List<SimAction> actions = _pendingActions.ToList();
            _pendingActions.Clear();
            return actions;
        }
    }

    private SimResponse BuildRegionStateResponse(RequestRegionStatePayload? payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.RegionId))
        {
            return SimResponse.Failed("Region state request requires a region id.");
        }

        MapRegion? region = World.Map.Regions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, payload.RegionId, StringComparison.OrdinalIgnoreCase));
        if (region is null)
        {
            return SimResponse.Failed($"Region '{payload.RegionId}' was not found.");
        }

        return new SimResponse
        {
            Type = SimResponseType.RegionState,
            RegionState = new RegionStateResponsePayload
            {
                RegionId = region.Id,
                Interactables = World.Interactables
                    .Where(interactable => string.Equals(interactable.Location.RegionId, region.Id, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(interactable => interactable.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(CreateInteractableState)
                    .ToList()
            }
        };
    }

    private static SimInteractableState CreateInteractableState(InteractableEntity interactable)
    {
        var state = new SimInteractableState
        {
            Id = interactable.Id,
            EntityType = interactable.GetType().Name,
            CanMove = interactable.CanMove,
            Location = interactable.Location,
            Attributes = interactable.Attributes
                .Select(attribute => new EntityAttribute
                {
                    Key = attribute.Key,
                    Subtype = attribute.Subtype,
                    ValueKind = attribute.ValueKind,
                    Value = attribute.Value
                })
                .ToList()
        };

        if (interactable is NavigatorEntity navigator)
        {
            state.CurrentWorldPosition = navigator.CurrentWorldPosition;
            state.Velocity = navigator.Velocity;
            state.FaceForward = navigator.FaceForward;
            state.BaseForward = navigator.BaseForward;
            state.RotationVelocity = navigator.RotationVelocity;
            state.MovementStatus = navigator.PathState.Status;
        }
        else
        {
            state.CurrentWorldPosition = interactable.Location.WorldPosition;
            state.FaceForward = SimVector2.Forward;
            state.BaseForward = SimVector2.Forward;
        }

        return state;
    }

    private void ProcessCommand(SimCommand command)
    {
        switch (command.Type)
        {
            case SimCommandType.PlayerMoveToWorldPosition:
                ProcessPlayerMoveToWorldPosition(command.PlayerMoveToWorldPosition);
                break;
            default:
                EmitCommandStatusAction(
                    command.Type,
                    sourceId: string.Empty,
                    playerId: null,
                    status: CommandStatus.Rejected,
                    reason: $"Unsupported sim command type '{command.Type}'.");
                break;
        }
    }

    private void ProcessPlayerMoveToWorldPosition(PlayerMoveToWorldPositionCommandPayload? payload)
    {
        if (payload is null)
        {
            EmitCommandStatusAction(
                SimCommandType.PlayerMoveToWorldPosition,
                sourceId: string.Empty,
                playerId: null,
                status: CommandStatus.Rejected,
                reason: "Move command payload is missing.");
            return;
        }

        if (!TryResolveMoveNavigator(payload, out NavigatorEntity? navigator, out string attemptedNavigatorId, out string failureReason))
        {
            EmitFailedMoveAction(payload.PlayerId, attemptedNavigatorId, failureReason, payload.WorldPosition);
            return;
        }

        if (!World.Map.TryGetPlacedTileAtWorldPosition(navigator!.Location.WorldPosition, out PlacedMapTile start))
        {
            EmitFailedMoveAction(payload.PlayerId, navigator, "Navigator current location does not resolve to a map tile.", payload.WorldPosition);
            return;
        }

        if (!World.Map.TryGetPlacedTileAtWorldPosition(payload.WorldPosition, out PlacedMapTile goal))
        {
            EmitFailedMoveAction(payload.PlayerId, navigator, "Move target does not resolve to a map tile.", payload.WorldPosition);
            return;
        }

        if (start.Equals(goal))
        {
            RememberNavigatorPlayer(payload.PlayerId, navigator);
            CancelExistingMovementIfNeeded(navigator);
            navigator.SetPath([navigator.Location], this);
            return;
        }

        if (!World.TryFindPath(start, goal, out IReadOnlyList<PlacedMapTile> placedPath))
        {
            EmitFailedMoveAction(payload.PlayerId, navigator, "No valid movement path was found.", payload.WorldPosition);
            return;
        }

        List<MapLocation> waypoints = placedPath
            .Select(CreateLocation)
            .ToList();
        RememberNavigatorPlayer(payload.PlayerId, navigator);
        CancelExistingMovementIfNeeded(navigator);
        navigator.SetPath(waypoints, this);
    }

    private void CancelExistingMovementIfNeeded(NavigatorEntity navigator)
    {
        if (navigator.PathState.Status == NavigatorMovementStatus.Moving)
        {
            navigator.CancelMovement(this);
        }
    }

    private bool TryResolveMoveNavigator(
        PlayerMoveToWorldPositionCommandPayload payload,
        out NavigatorEntity? navigator,
        out string attemptedNavigatorId,
        out string failureReason)
    {
        navigator = null;
        attemptedNavigatorId = string.Empty;
        failureReason = string.Empty;

        if (Players.TryGetNavigatorId(payload.PlayerId, out string mappedNavigatorId))
        {
            attemptedNavigatorId = mappedNavigatorId;
            if (World.TryGetNavigator(mappedNavigatorId, out navigator))
            {
                return true;
            }

            failureReason = $"Player '{payload.PlayerId}' is assigned to missing navigator '{mappedNavigatorId}'.";
            return false;
        }

        attemptedNavigatorId = payload.NavigatorId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(attemptedNavigatorId))
        {
            failureReason = $"Player '{payload.PlayerId}' does not have an assigned navigator.";
            return false;
        }

        if (World.TryGetNavigator(attemptedNavigatorId, out navigator))
        {
            return true;
        }

        failureReason = $"Navigator '{attemptedNavigatorId}' was not found.";
        return false;
    }

    private void RememberNavigatorPlayer(int playerId, NavigatorEntity navigator)
    {
        if (Players.TryGetProfile(playerId, out _))
        {
            Players.TryAssignNavigator(playerId, navigator.Id);
        }
    }

    private void EmitMoveAction(Events.MoveEventPayload payload)
    {
        int? playerId = Players.TryGetPlayerIdForNavigator(payload.MoverId, out int mappedPlayerId)
            ? mappedPlayerId
            : null;
        string regionId = payload.Path.Count > 0 ? payload.Path[0].RegionId : string.Empty;
        EmitAction(new SimAction
        {
            Type = SimActionType.Move,
            Target = payload.Status == MoveStatus.Failed ? SimActionTarget.Player : SimActionTarget.Region,
            RegionId = regionId,
            PlayerId = playerId,
            Move = new MoveActionPayload
            {
                Status = payload.Status,
                MoverId = payload.MoverId,
                PlayerId = playerId,
                PathWorldPositions = payload.PathWorldPositions.ToList(),
                CurrentIndex = payload.CurrentIndex,
                CurrentWorldPosition = payload.CurrentWorldPosition,
                DestinationWorldPosition = payload.DestinationWorldPosition,
                Velocity = payload.Velocity,
                FaceForward = payload.FaceForward,
                BaseForward = payload.BaseForward,
                RotationVelocity = payload.RotationVelocity,
                MaxSpeed = payload.MaxSpeed,
                MaxRotationSpeed = payload.MaxRotationSpeed,
                FailureReason = payload.FailureReason
            }
        });
    }

    private void EmitFailedMoveAction(int? playerId, NavigatorEntity navigator, string failureReason, SimVector2 destination)
    {
        EmitAction(new SimAction
        {
            Type = SimActionType.Move,
            Target = SimActionTarget.Player,
            RegionId = navigator.Location.RegionId,
            PlayerId = playerId,
            Move = new MoveActionPayload
            {
                Status = MoveStatus.Failed,
                MoverId = navigator.Id,
                PlayerId = playerId,
                CurrentIndex = navigator.PathState.CurrentWaypointIndex,
                CurrentWorldPosition = navigator.CurrentWorldPosition,
                DestinationWorldPosition = destination,
                Velocity = navigator.Velocity,
                FaceForward = navigator.FaceForward,
                BaseForward = navigator.BaseForward,
                RotationVelocity = navigator.RotationVelocity,
                MaxSpeed = navigator.MaxSpeed,
                MaxRotationSpeed = navigator.MaxRotationSpeed,
                FailureReason = failureReason
            }
        });
    }

    private void EmitFailedMoveAction(int? playerId, string moverId, string failureReason, SimVector2 destination)
    {
        EmitAction(new SimAction
        {
            Type = SimActionType.Move,
            Target = SimActionTarget.Player,
            PlayerId = playerId,
            Move = new MoveActionPayload
            {
                Status = MoveStatus.Failed,
                MoverId = moverId ?? string.Empty,
                PlayerId = playerId,
                DestinationWorldPosition = destination,
                FailureReason = failureReason
            }
        });
    }

    private void EmitCommandStatusAction(
        SimCommandType commandType,
        string sourceId,
        int? playerId,
        CommandStatus status,
        string reason)
    {
        EmitAction(new SimAction
        {
            Type = SimActionType.CommandStatus,
            Target = SimActionTarget.Player,
            PlayerId = playerId,
            CommandStatus = new CommandStatusActionPayload
            {
                CommandType = commandType,
                SourceId = sourceId ?? string.Empty,
                Status = status,
                Reason = reason ?? string.Empty
            }
        });
    }

    private MapLocation CreateLocation(PlacedMapTile placedTile)
    {
        string regionId = World.Map.Regions
            .FirstOrDefault(region => region.Sections.Contains(placedTile.Section))
            ?.Id ?? string.Empty;
        return new MapLocation(
            regionId,
            placedTile.Section.Id,
            placedTile.Section.GridPosition,
            placedTile.Tile.GridPosition,
            placedTile.Tile.WorldCenter);
    }

    private bool TryGetOpenPlayerSpawner(
        out MapRegion? region,
        out PlacedMapSection? section,
        out MapTile? tile)
    {
        region = null;
        section = null;
        tile = null;
        foreach (MapRegion candidateRegion in World.Map.Regions)
        {
            foreach (PlacedMapSection candidateSection in candidateRegion.Sections)
            {
                foreach (MapTile candidateTile in candidateSection.Section.Tiles)
                {
                    if (HasPlayerSpawner(candidateTile)
                        && !World.IsTileBlocked(candidateRegion.Id, candidateSection.Id, candidateTile.GridPosition))
                    {
                        region = candidateRegion;
                        section = candidateSection;
                        tile = candidateTile;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasPlayerSpawner(MapTile tile)
    {
        return tile.Attributes.Any(attribute =>
            string.Equals(attribute.Id, PlayerSpawnerAttributeId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(attribute.ModifierKey, PlayerSpawnerModifierKey, StringComparison.OrdinalIgnoreCase));
    }

    private const string PlayerSpawnerAttributeId = "player spawner";
    private const string PlayerSpawnerModifierKey = "spawner";

    private readonly struct QueuedSimCommand
    {
        public QueuedSimCommand(long serverOrder, SimCommand command)
        {
            ServerOrder = serverOrder;
            Command = command;
        }

        public long ServerOrder { get; }

        public SimCommand Command { get; }
    }

}
