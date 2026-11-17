using System.Text;
using ClanIronSim.Commands;
using ClanIronSim.Entity;
using ClanIronSim.Player;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using SimWorldState = ClanIronSim.WorldState.WorldState;

namespace ClanIronSim.HawkNet;

public sealed class ClanIronSimMatch : GameMatchBase
{
    private readonly ClanIronSimMatchSettings _settings;
    private GameOverlord? _overlord;

    public ClanIronSimMatch()
        : this(new ClanIronSimMatchSettings())
    {
    }

    public ClanIronSimMatch(ClanIronSimMatchSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public GameOverlord? Overlord => _overlord;

    public override async Task InitializeAsync(IGameMatchContext context, MatchPayload payload, CancellationToken token = default)
    {
        await base.InitializeAsync(context, payload, token).ConfigureAwait(false);
        string worldStatePath = ResolveWorldStatePath(_settings.WorldStatePath);
        Context.Logger.Log(
            $"Initializing ClanIron match worldStatePath='{worldStatePath}' connectedPlayers={Players.Count(player => player.IsConnected)}.",
            nameof(ClanIronSimMatch));
        _overlord = new GameOverlord(await SimWorldState.LoadAsync(worldStatePath, token).ConfigureAwait(false));
        foreach (MatchPlayer player in Players.Where(player => player.IsConnected))
        {
            AddSimPlayer(player);
        }
    }

    public override async Task SubscribeAsync(CancellationToken token = default)
    {
        await base.SubscribeAsync(token).ConfigureAwait(false);
        Subscribe<GameStateMessageReceivedEvent>(OnGameStateMessageReceived);
    }

    public override async Task UpdateAsync(float deltaTime, CancellationToken token = default)
    {
        if (_overlord is null)
        {
            return;
        }

        IReadOnlyList<SimAction> actions = await _overlord.UpdateAsync(deltaTime, token).ConfigureAwait(false);
        foreach (SimAction action in actions)
        {
            await SendSimActionAsync(action, token).ConfigureAwait(false);
        }
    }

    protected override async Task OnPlayerJoinedAsync(MatchPlayer player, CancellationToken token)
    {
        await base.OnPlayerJoinedAsync(player, token).ConfigureAwait(false);
        AddSimPlayer(player);
    }

    protected override async Task OnPlayerDisconnectedAsync(MatchPlayer player, ConnectionCloseReason reason, CancellationToken token)
    {
        await base.OnPlayerDisconnectedAsync(player, reason, token).ConfigureAwait(false);
        if (reason == ConnectionCloseReason.SessionTimeout)
        {
            _overlord?.TryRemovePlayer(player.PlayerId);
        }
    }

    private async Task OnGameStateMessageReceived(GameStateMessageReceivedEvent gameEvent, CancellationToken token)
    {
        if (_overlord is null || !IsClanIronMessage(gameEvent.MessageId))
        {
            return;
        }

        Context.Logger.Log(
            $"ClanIron game-state message received player={gameEvent.PlayerId} messageId='{gameEvent.MessageId}' payloadBytes={gameEvent.Payload.Length}.",
            nameof(ClanIronSimMatch));
        switch (gameEvent.MessageId)
        {
            case ClanIronSimHawkNetMessageIds.SimCommand:
                HandleSimCommand(gameEvent.PlayerId, gameEvent.Payload);
                break;
            case ClanIronSimHawkNetMessageIds.SimRequest:
                await HandleSimRequestAsync(gameEvent.PlayerId, gameEvent.Payload, token).ConfigureAwait(false);
                break;
            default:
                break;
        }
    }

    private void HandleSimCommand(int playerId, byte[] payload)
    {
        if (_overlord is null)
        {
            return;
        }

        SimCommand command = SimJson.FromJson<SimCommand>(Encoding.UTF8.GetString(payload));
        Context.Logger.Log(
            $"ClanIron sim command player={playerId} type={command.Type} payloadBytes={payload.Length}.",
            nameof(ClanIronSimMatch));
        if (command.PlayerMoveToWorldPosition is not null)
        {
            command.PlayerMoveToWorldPosition.PlayerId = playerId;
        }

        _overlord.EnqueueCommand(command);
        Context.Logger.Log($"ClanIron sim command queued player={playerId} type={command.Type}.", nameof(ClanIronSimMatch));
    }

    private async Task HandleSimRequestAsync(int playerId, byte[] payload, CancellationToken token)
    {
        if (_overlord is null)
        {
            return;
        }

        SimRequest request = SimJson.FromJson<SimRequest>(Encoding.UTF8.GetString(payload));
        Context.Logger.Log(
            $"ClanIron sim request player={playerId} type={request.Type} payloadBytes={payload.Length}.",
            nameof(ClanIronSimMatch));
        SimResponse response = await _overlord.RunRequestAsync(request, token).ConfigureAwait(false);
        byte[] responsePayload = Encoding.UTF8.GetBytes(SimJson.ToJson(response));
        Context.Logger.Log(
            $"ClanIron sim response player={playerId} type={response.Type} payloadBytes={responsePayload.Length}.",
            nameof(ClanIronSimMatch));
        await SendGameStateMessageToPlayerAsync(
            playerId,
            ClanIronSimHawkNetMessageIds.SimResponse,
            responsePayload,
            DeliveryOptions.ReliableGameplay,
            token).ConfigureAwait(false);
    }

    private void AddSimPlayer(MatchPlayer player)
    {
        if (_overlord is null || _overlord.Players.TryGetProfile(player.PlayerId, out _))
        {
            Context.Logger.DebugLog(
                $"ClanIron AddSimPlayer skipped player={player.PlayerId} overlordReady={_overlord is not null}.",
                nameof(ClanIronSimMatch));
            return;
        }

        bool added = _overlord.TryAddPlayer(
            new PlayerProfile
            {
                Id = player.PlayerId,
                DisplayName = player.DisplayName
            },
            out NavigatorEntity? navigator);
        if (added && navigator is not null)
        {
            Context.Logger.Log(
                $"ClanIron player spawned player={player.PlayerId} navigator='{navigator.Id}' world=({navigator.CurrentWorldPosition.X:0.###},{navigator.CurrentWorldPosition.Z:0.###}).",
                nameof(ClanIronSimMatch));
            return;
        }

        Context.Logger.Warning($"ClanIron player spawn failed player={player.PlayerId}.", nameof(ClanIronSimMatch));
    }

    private async Task SendSimActionAsync(SimAction action, CancellationToken token)
    {
        byte[] payload = Encoding.UTF8.GetBytes(SimJson.ToJson(action));
        DeliveryOptions delivery = action.Type == SimActionType.Move && action.Move?.Status == MoveStatus.Continuing
            ? DeliveryOptions.UnreliableGameplay
            : DeliveryOptions.ReliableGameplay;
        if (action.Target == SimActionTarget.Player && action.PlayerId is int playerId)
        {
            Context.Logger.Log(
                $"ClanIron sim action to player={playerId} type={action.Type} payloadBytes={payload.Length}.",
                nameof(ClanIronSimMatch));
            await SendGameStateMessageToPlayerAsync(
                playerId,
                ClanIronSimHawkNetMessageIds.SimAction,
                payload,
                delivery,
                token).ConfigureAwait(false);
            return;
        }

        Context.Logger.Log(
            $"ClanIron sim action broadcast type={action.Type} payloadBytes={payload.Length}.",
            nameof(ClanIronSimMatch));
        await BroadcastGameStateMessageAsync(
            ClanIronSimHawkNetMessageIds.SimAction,
            payload,
            delivery,
            token).ConfigureAwait(false);
    }

    private Task SendGameStateMessageToPlayerAsync(
        int playerId,
        string messageId,
        byte[] payload,
        DeliveryOptions delivery,
        CancellationToken token)
    {
        return Context.Events.DispatchAsync(
            new SendPlayerMessageEvent(
                playerId,
                PacketType.GameStateMessage,
                GameStateMessageProtocol.Encode(new GameStateMessage
                {
                    MessageId = messageId,
                    Payload = payload
                }, Context.PayloadCodec),
                delivery),
            token);
    }

    private Task BroadcastGameStateMessageAsync(string messageId, byte[] payload, DeliveryOptions delivery, CancellationToken token)
    {
        return Context.Events.DispatchAsync(
            new BroadcastMessageEvent(
                PacketType.GameStateMessage,
                GameStateMessageProtocol.Encode(new GameStateMessage
                {
                    MessageId = messageId,
                    Payload = payload
                }, Context.PayloadCodec),
                delivery),
            token);
    }

    private static bool IsClanIronMessage(string messageId)
    {
        return messageId.StartsWith("clan_iron.", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWorldStatePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(AppContext.BaseDirectory, path);
    }
}
