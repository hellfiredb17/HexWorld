using System.Text;
using Avalonia.Threading;
using ClanIronSim;
using ClanIronSim.Commands;
using ClanIronSim.HawkNet;
using ClanIronSim.SimMath;
using ClanIronSim.World;
using HawkNet.Core;
using HawkNet.Core.Client;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.Services;
using HawkNet.CoreUx.Client.World;
using SimWorldState = ClanIronSim.WorldState.WorldState;

namespace ClanIronSim.CoreUx.Client;

public sealed class ClanIronClientMatchRuntime : IClientMatchRuntime
{
    private const string DefaultRegionId = "rect island";
    private readonly HawkNetClient? _client;
    private readonly ILogger _logger;
    private readonly List<IDisposable> _subscriptions = new();
    private WorldSnapshot? _lastWorldSnapshot;
    private SimWorldState? _staticWorld;
    private bool _disposed;

    public ClanIronClientMatchRuntime(HawkNetClient? client, ILogger? logger = null)
    {
        _client = client;
        _logger = logger
            ?? (_client?.Services.TryGet<ILogger>(out var clientLogger) == true ? clientLogger : NullLogger.Instance);
        Subscribe();
    }

    public event EventHandler<ClientMatchSnapshot>? SnapshotChanged;

    public event EventHandler<MatchPhaseChangeResponse>? PhaseChanged;

    public event EventHandler<ChatEntry>? ChatMessageReceived;

    public event EventHandler<WorldSnapshot>? WorldSnapshotChanged;

    public event EventHandler<WorldEntitySnapshot>? WorldEntityChanged;

    public Task<ClientMatchSnapshot> LoadSnapshotAsync(CancellationToken token = default)
    {
        try
        {
            _logger.Log("Loading client match snapshot.", nameof(ClanIronClientMatchRuntime));
            return Task.FromResult(CreateSnapshot());
        }
        catch (Exception exception)
        {
            _logger.Exception(exception, "Client match snapshot load failed.", nameof(ClanIronClientMatchRuntime));
            throw;
        }
    }

    public async Task RequestWorldStateAsync(string regionId, CancellationToken token = default)
    {
        try
        {
            ThrowIfDisposed();
            EnsureStaticWorldLoaded();
            string requestedRegionId = string.IsNullOrWhiteSpace(regionId) ? DefaultRegionId : regionId;
            _logger.Log($"Requesting ClanIron world state region='{requestedRegionId}'.", nameof(ClanIronClientMatchRuntime));
            SimRequest request = new()
            {
                Type = SimRequestType.RequestRegionState,
                RequestRegionState = new RequestRegionStatePayload
                {
                    RegionId = requestedRegionId
                }
            };
            await SendClanIronMessageAsync(
                ClanIronSimHawkNetMessageIds.SimRequest,
                Encoding.UTF8.GetBytes(SimJson.ToJson(request)),
                token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Exception(exception, "RequestWorldStateAsync failed.", nameof(ClanIronClientMatchRuntime));
            throw;
        }
    }

    public async Task SendMoveCommandAsync(WorldPoint destination, CancellationToken token = default)
    {
        try
        {
            ThrowIfDisposed();
            _logger.Log(
                $"Sending ClanIron move command destination=({destination.X:0.###},{destination.Z:0.###}).",
                nameof(ClanIronClientMatchRuntime));
            SimCommand command = new()
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    WorldPosition = new SimVector2(destination.X, destination.Z)
                }
            };
            await SendClanIronMessageAsync(
                ClanIronSimHawkNetMessageIds.SimCommand,
                Encoding.UTF8.GetBytes(SimJson.ToJson(command)),
                token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Exception(exception, "SendMoveCommandAsync failed.", nameof(ClanIronClientMatchRuntime));
            throw;
        }
    }

    public Task SendMessageAsync(string message, CancellationToken token = default)
    {
        ThrowIfDisposed();
        return GetClient().ChatService.SendMessageAsync(message, token);
    }

    public Task SendCommandAsync(string commandId, string payloadText, CancellationToken token = default)
    {
        ThrowIfDisposed();
        byte[] payload = string.IsNullOrWhiteSpace(payloadText)
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(payloadText);
        _logger.Log($"Sending debug match command '{commandId}' payloadBytes={payload.Length}.", nameof(ClanIronClientMatchRuntime));
        return GetClient().Services.Get<ClientMatchService>().SendCommandAsync(commandId, payload, token);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void Subscribe()
    {
        if (_client is null)
        {
            return;
        }

        _subscriptions.Add(_client.Events.Subscribe<ClientMatchPhaseChangeReceivedEvent>((gameEvent, _) =>
        {
            _logger.Log(
                $"Match phase received variant='{gameEvent.PhaseChange.VariantId}' phase='{gameEvent.PhaseChange.PhaseId}' players={gameEvent.PhaseChange.Players.Count()}.",
                nameof(ClanIronClientMatchRuntime));
            Dispatch(() =>
            {
                PhaseChanged?.Invoke(this, gameEvent.PhaseChange);
                SnapshotChanged?.Invoke(this, CreateSnapshot());
            });
            return Task.CompletedTask;
        }));
        _subscriptions.Add(_client.Events.Subscribe<ClientChatMessageReceivedEvent>((gameEvent, _) =>
        {
            Dispatch(() => ChatMessageReceived?.Invoke(this, gameEvent.Entry));
            return Task.CompletedTask;
        }));
        _subscriptions.Add(_client.Events.Subscribe<ClientChatHistoryReceivedEvent>((_, _) =>
        {
            Dispatch(() => SnapshotChanged?.Invoke(this, CreateSnapshot()));
            return Task.CompletedTask;
        }));
        _subscriptions.Add(_client.Events.Subscribe<ClientGameStateMessageReceivedEvent>((gameEvent, _) =>
        {
            _logger.Log(
                $"Client game-state message received id='{gameEvent.MessageId}' payloadBytes={gameEvent.Payload.Length}.",
                nameof(ClanIronClientMatchRuntime));
            HandleGameStateMessage(gameEvent.MessageId, gameEvent.Payload);
            return Task.CompletedTask;
        }));
    }

    private ClientMatchSnapshot CreateSnapshot()
    {
        ClientMatchState match = GetMatch();
        WorldSnapshot? world = _lastWorldSnapshot;
        if (world is null)
        {
            EnsureStaticWorldLoaded();
            world = CreateWorldSnapshot(DefaultRegionId, Array.Empty<SimInteractableState>());
        }

        return new ClientMatchSnapshot
        {
            Payload = new MatchPayload
            {
                VariantId = match.VariantId,
                PhaseId = match.PhaseId,
                PhaseSeconds = match.PhaseSeconds,
                PhaseStartedAtUtc = match.PhaseStartedAtUtc,
                PhaseEndsAtUtc = match.PhaseEndsAtUtc,
                Players = match.Players.ToArray()
            },
            ChatHistory = GetClient().ChatService.GetHistory(),
            CurrentPlayerId = GetClient().GameClient.PlayerId,
            World = world
        };
    }

    private void HandleGameStateMessage(string messageId, byte[] payload)
    {
        if (_disposed)
        {
            return;
        }

        if (!messageId.StartsWith("clan_iron.", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            string json = Encoding.UTF8.GetString(payload);
            switch (messageId)
            {
                case ClanIronSimHawkNetMessageIds.SimResponse:
                    ApplySimResponse(SimJson.FromJson<SimResponse>(json));
                    break;
                case ClanIronSimHawkNetMessageIds.SimAction:
                    ApplySimAction(SimJson.FromJson<SimAction>(json));
                    break;
                default:
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.Exception(exception, $"Failed to handle ClanIron message '{messageId}'.", nameof(ClanIronClientMatchRuntime));
            // Game-specific payloads should not tear down the reusable match UI.
        }
    }

    private void ApplySimResponse(SimResponse response)
    {
        if (_disposed)
        {
            return;
        }

        if (response.Type != SimResponseType.RegionState || response.RegionState is null)
        {
            return;
        }

        WorldSnapshot snapshot = CreateWorldSnapshot(
            response.RegionState.RegionId,
            response.RegionState.Interactables);
        _logger.Log(
            $"Applied ClanIron region state region='{snapshot.RegionId}' tiles={snapshot.Tiles.Count} entities={snapshot.Entities.Count}.",
            nameof(ClanIronClientMatchRuntime));
        _lastWorldSnapshot = snapshot;
        Dispatch(() => WorldSnapshotChanged?.Invoke(this, snapshot));
    }

    private void ApplySimAction(SimAction action)
    {
        if (_disposed)
        {
            return;
        }

        if (action.Type != SimActionType.Move || action.Move is null)
        {
            return;
        }

        WorldEntitySnapshot snapshot = new()
        {
            Id = action.Move.MoverId,
            EntityType = "NavigatorEntity",
            PlayerId = action.Move.PlayerId ?? action.PlayerId ?? TryParsePlayerId(action.Move.MoverId),
            WorldX = action.Move.CurrentWorldPosition.X,
            WorldZ = action.Move.CurrentWorldPosition.Z,
            DestinationWorldX = action.Move.DestinationWorldPosition.X,
            DestinationWorldZ = action.Move.DestinationWorldPosition.Z,
            VelocityX = action.Move.Velocity.X,
            VelocityZ = action.Move.Velocity.Z,
            FaceForwardX = action.Move.FaceForward.X,
            FaceForwardZ = action.Move.FaceForward.Z,
            BaseForwardX = action.Move.BaseForward.X,
            BaseForwardZ = action.Move.BaseForward.Z,
            RotationVelocity = action.Move.RotationVelocity,
            MaxSpeed = action.Move.MaxSpeed,
            MaxRotationSpeed = action.Move.MaxRotationSpeed,
            CurrentPathIndex = action.Move.CurrentIndex,
            PathWorldPositions = action.Move.PathWorldPositions
                .Select(position => new WorldPoint(position.X, position.Z))
                .ToArray(),
            ServerUpdatedAtUtc = DateTime.UtcNow,
            MovementStatus = action.Move.Status.ToString(),
            CanMove = true
        };
        _logger.Log(
            $"Applied ClanIron sim action move mover='{snapshot.Id}' player={snapshot.PlayerId?.ToString() ?? "<none>"} status='{snapshot.MovementStatus}'.",
            nameof(ClanIronClientMatchRuntime));
        Dispatch(() => WorldEntityChanged?.Invoke(this, snapshot));
    }

    private WorldSnapshot CreateWorldSnapshot(
        string regionId,
        IReadOnlyList<SimInteractableState> interactables)
    {
        EnsureStaticWorldLoaded();
        MapRegion? region = _staticWorld?.Map.Regions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, regionId, StringComparison.OrdinalIgnoreCase));
        if (region is null)
        {
            return new WorldSnapshot
            {
                RegionId = regionId,
                RegionName = regionId,
                LocalPlayerId = GetClient().GameClient.PlayerId
            };
        }

        return new WorldSnapshot
        {
            RegionId = region.Id,
            RegionName = string.IsNullOrWhiteSpace(region.DisplayName) ? region.Id : region.DisplayName,
            LocalPlayerId = GetClient().GameClient.PlayerId,
            Tiles = region.Sections
                .SelectMany(section => section.Section.Tiles.Select(tile => CreateTileSnapshot(region, section, tile)))
                .ToArray(),
            Entities = interactables
                .Select(CreateEntitySnapshot)
                .ToArray()
        };
    }

    private static WorldTileSnapshot CreateTileSnapshot(MapRegion region, PlacedMapSection section, MapTile tile)
    {
        return new WorldTileSnapshot
        {
            Id = $"{section.Id}:{tile.GridX}:{tile.GridZ}",
            RegionId = region.Id,
            PlacedSectionId = section.Id,
            GridX = tile.GridX,
            GridZ = tile.GridZ,
            WorldX = tile.WorldX,
            WorldZ = tile.WorldZ,
            SizeX = tile.SizeX,
            SizeZ = tile.SizeY,
            CenterX = tile.WorldCenterX,
            CenterZ = tile.WorldCenterZ,
            Shape = tile.Shape == MapTileShape.Hex ? WorldTileShape.Hex : WorldTileShape.Rectangle,
            HexOrientation = tile is MapHexTile { Orientation: HexOrientation.FlatTop }
                ? WorldHexOrientation.FlatTop
                : WorldHexOrientation.PointyTop,
            MovementCost = tile.MovementCost,
            IsBlocked = tile.MovementCost < 0,
            IsSpawner = IsPlayerSpawner(tile)
        };
    }

    private static bool IsPlayerSpawner(MapTile tile)
    {
        return tile.Attributes.Any(attribute =>
            string.Equals(attribute.Id, "player spawner", StringComparison.OrdinalIgnoreCase)
            && string.Equals(attribute.ModifierKey, "spawner", StringComparison.OrdinalIgnoreCase));
    }

    private static WorldEntitySnapshot CreateEntitySnapshot(SimInteractableState interactable)
    {
        return new WorldEntitySnapshot
        {
            Id = interactable.Id,
            EntityType = interactable.EntityType,
            PlayerId = TryParsePlayerId(interactable.Id),
            WorldX = interactable.CurrentWorldPosition.X,
            WorldZ = interactable.CurrentWorldPosition.Z,
            DestinationWorldX = interactable.CurrentWorldPosition.X,
            DestinationWorldZ = interactable.CurrentWorldPosition.Z,
            ServerUpdatedAtUtc = DateTime.UtcNow,
            MovementStatus = interactable.MovementStatus?.ToString() ?? string.Empty,
            CanMove = interactable.CanMove
        };
    }

    private static int? TryParsePlayerId(string interactableId)
    {
        const string prefix = "player_";
        const string suffix = "_nav";
        if (!interactableId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !interactableId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string idText = interactableId[prefix.Length..^suffix.Length];
        return int.TryParse(idText, out int playerId) ? playerId : null;
    }

    private async Task SendClanIronMessageAsync(string messageId, byte[] payload, CancellationToken token)
    {
        ThrowIfDisposed();
        HawkNetClient client = GetClient();
        byte[] packetPayload = GameStateMessageProtocol.Encode(new GameStateMessage
        {
            MessageId = messageId,
            Payload = payload
        }, client.Services.Get<IPayloadCodec>());
        _logger.Log(
            $"Sending game-state message id='{messageId}' payloadBytes={payload.Length} packetBytes={packetPayload.Length}.",
            nameof(ClanIronClientMatchRuntime));
        await client.GameClient.SendPacketAsync(PacketType.GameStateMessage, packetPayload, DeliveryOptions.ReliableGameplay, token).ConfigureAwait(false);
    }

    private void EnsureStaticWorldLoaded()
    {
        if (_staticWorld is not null)
        {
            return;
        }

        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "WorldData",
            "test_rect_island_worldstate.json");
        if (!File.Exists(path))
        {
            return;
        }

        _staticWorld = SimWorldState.FromJson(File.ReadAllText(path));
    }

    private HawkNetClient GetClient()
    {
        ThrowIfDisposed();
        return _client ?? throw new InvalidOperationException("No HawkNet client is connected.");
    }

    private ClientMatchState GetMatch()
    {
        return GetClient().GameStateManager.ActiveState as ClientMatchState
            ?? throw new InvalidOperationException("The active client state is not the match.");
    }

    private static void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ClanIronClientMatchRuntime));
        }
    }
}
