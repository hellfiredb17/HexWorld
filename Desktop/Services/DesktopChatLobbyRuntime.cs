using Avalonia.Threading;
using HawkNet.Core;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Server.Services;
using HawkNet.Game.GameState.Lobby;

namespace HawkNet.Desktop.Services;

public sealed class DesktopChatLobbyRuntime : IChatLobbyRuntime
{
    private const string MatchStateId = "match";
    private readonly GameHost? _host;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly Func<Action, Task>? _dispatchAsync;
    private TaskCompletionSource? _matchTransitionCompletion;
    private ChatLobbyCountdownStatus _lastCountdownStatus = ChatLobbyCountdownStatus.None;
    private int _lastCountdownWholeSeconds = -1;
    private bool _disposed;

    public DesktopChatLobbyRuntime(GameHost? host, Func<Action, Task>? dispatchAsync = null)
    {
        _host = host;
        _dispatchAsync = dispatchAsync;
        SubscribeToHostEvents();
    }

    public event EventHandler<ChatLobbySnapshot>? SnapshotChanged;

    public event EventHandler<ChatEntry>? ChatMessageReceived;

    public event EventHandler<ChatLobbyReadyStateResponse>? ReadyStateChanged;

    public event EventHandler<ChatLobbyCountdownResponse?>? CountdownChanged;

    public event EventHandler? MatchStarted;

    public Task<ChatLobbySnapshot> LoadSnapshotAsync(CancellationToken token = default)
    {
        return Task.FromResult(CreateSnapshot());
    }

    public Task SendMessageAsync(string message, CancellationToken token = default)
    {
        return GetChatService().SendSystemMessageAsync(message, "Admin", token);
    }

    public async Task StartMatchAsync(CancellationToken token = default)
    {
        GameHost host = GetHost();
        if (string.Equals(host.ActiveGameStateId, MatchStateId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnsureLobbyState();
        if (_matchTransitionCompletion is not null)
        {
            throw new InvalidOperationException("A match transition is already in progress.");
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _matchTransitionCompletion = completion;
        using CancellationTokenRegistration registration = token.Register(() => completion.TrySetCanceled(token));

        try
        {
            await host.RequestGameStateTransitionAsync(MatchStateId, BuildReadyTransitionPayload(), token);
            await completion.Task;
        }
        finally
        {
            if (ReferenceEquals(_matchTransitionCompletion, completion))
            {
                _matchTransitionCompletion = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_host is not null)
        {
            _host.UpdateLoop.Update -= OnUpdate;
        }

        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _matchTransitionCompletion?.TrySetCanceled();
        _matchTransitionCompletion = null;
    }

    private void SubscribeToHostEvents()
    {
        if (_host is null)
        {
            return;
        }

        _host.UpdateLoop.Update += OnUpdate;
        _subscriptions.Add(_host.Events.Subscribe<PlayerConnectedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_host.Events.Subscribe<PlayerReconnectedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_host.Events.Subscribe<PlayerConnectionLostEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_host.Events.Subscribe<PlayerDisconnectedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_host.Events.Subscribe<PlayerDisplayNameChangedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_host.Events.Subscribe<ChatLobbyReadyRequestReceivedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_host.Events.Subscribe<ChatMessageLoggedEvent>(OnChatMessageLoggedAsync));
        _subscriptions.Add(_host.Events.Subscribe<GameStateTransitionCompletedEvent>(OnTransitionCompletedAsync));
        _subscriptions.Add(_host.Events.Subscribe<GameStateTransitionFailedEvent>(OnTransitionFailedAsync));
    }

    private Task RefreshSnapshotAsync()
    {
        if (_disposed || _host?.ActiveGameState is not ChatLobbyState)
        {
            return Task.CompletedTask;
        }

        ChatLobbySnapshot snapshot = CreateSnapshot();
        DispatchToUi(() =>
        {
            SnapshotChanged?.Invoke(this, snapshot);
            ReadyStateChanged?.Invoke(this, snapshot.ReadyState);
            CountdownChanged?.Invoke(this, snapshot.Countdown);
        });
        return Task.CompletedTask;
    }

    private void OnUpdate(float deltaTime)
    {
        if (_disposed || _host?.ActiveGameState is not ChatLobbyState lobby)
        {
            return;
        }

        ChatLobbyCountdownResponse? countdown = BuildCountdown(lobby);
        ChatLobbyCountdownStatus status = countdown?.Status ?? ChatLobbyCountdownStatus.None;
        int wholeSeconds = countdown is null ? -1 : (int)Math.Ceiling(Math.Max(0f, countdown.RemainingSeconds));
        if (status == _lastCountdownStatus && wholeSeconds == _lastCountdownWholeSeconds)
        {
            return;
        }

        _lastCountdownStatus = status;
        _lastCountdownWholeSeconds = wholeSeconds;
        DispatchToUi(() => CountdownChanged?.Invoke(this, countdown));
    }

    private Task OnChatMessageLoggedAsync(ChatMessageLoggedEvent gameEvent, CancellationToken token)
    {
        DispatchToUi(() => ChatMessageReceived?.Invoke(this, gameEvent.Entry));
        return Task.CompletedTask;
    }

    private Task OnTransitionCompletedAsync(GameStateTransitionCompletedEvent gameEvent, CancellationToken token)
    {
        if (string.Equals(gameEvent.ToStateId, MatchStateId, StringComparison.OrdinalIgnoreCase))
        {
            _matchTransitionCompletion?.TrySetResult();
            DispatchToUi(() => MatchStarted?.Invoke(this, EventArgs.Empty));
        }

        return Task.CompletedTask;
    }

    private Task OnTransitionFailedAsync(GameStateTransitionFailedEvent gameEvent, CancellationToken token)
    {
        if (string.Equals(gameEvent.ToStateId, MatchStateId, StringComparison.OrdinalIgnoreCase))
        {
            _matchTransitionCompletion?.TrySetException(new InvalidOperationException(gameEvent.Reason));
        }

        return Task.CompletedTask;
    }

    private ChatLobbySnapshot CreateSnapshot()
    {
        GameHost host = GetHost();
        ChatLobbyState lobby = EnsureLobbyState();
        ChatLobbyReadyStateResponse readyState = BuildReadyState(lobby);

        return new ChatLobbySnapshot
        {
            Players = BuildPlayers(lobby, readyState),
            ChatHistory = GetChatService().GetHistory(),
            ReadyState = readyState,
            Countdown = BuildCountdown(lobby),
            MaxPlayers = host.Config.Server.MaxPlayers,
            MapName = $"State: {host.ActiveGameStateId ?? "unknown"}"
        };
    }

    private static ChatLobbyCountdownResponse? BuildCountdown(ChatLobbyState lobby)
    {
        if (!lobby.IsCountdownActive)
        {
            return null;
        }

        return new ChatLobbyCountdownResponse
        {
            Status = ChatLobbyCountdownStatus.Started,
            RemainingSeconds = lobby.CountdownRemainingSeconds
        };
    }

    private ChatLobbyState EnsureLobbyState()
    {
        GameHost host = GetHost();
        return host.ActiveGameState as ChatLobbyState
            ?? throw new InvalidOperationException("The active server state is not the chat lobby.");
    }

    private GameHost GetHost()
    {
        return _host ?? throw new InvalidOperationException("No HawkNet server is running.");
    }

    private ChatService GetChatService()
    {
        return GetHost().Services.Get<ChatService>();
    }

    private IPayloadCodec GetPayloadCodec()
    {
        return GetHost().Services.Get<IPayloadCodec>();
    }

    private IPlayerIdentityService GetPlayerIdentityService()
    {
        return GetHost().Services.Get<IPlayerIdentityService>();
    }

    private ChatLobbyReadyStateResponse BuildReadyState(ChatLobbyState lobby)
    {
        int[] connectedPlayers = lobby.ConnectedPlayers.OrderBy(id => id).ToArray();
        ChatLobbyReadyPlayer[] players = connectedPlayers
            .Select(playerId => CreateReadyPlayer(lobby, playerId))
            .ToArray();
        int readyPlayerCount = players.Count(player => player.IsReady);
        int requiredReadyPlayerCount = GetRequiredReadyPlayerCount(connectedPlayers.Length);

        return new ChatLobbyReadyStateResponse
        {
            Players = players,
            ReadyPlayerCount = readyPlayerCount,
            RequiredReadyPlayerCount = requiredReadyPlayerCount,
            ReadyPercentage = connectedPlayers.Length == 0
                ? 0f
                : (readyPlayerCount / (float)connectedPlayers.Length) * 100f
        };
    }

    private ChatLobbyReadyPlayer CreateReadyPlayer(ChatLobbyState lobby, int playerId)
    {
        ChatLobbyReadyPlayer? readyPlayer = lobby.ReadyPlayers.FirstOrDefault(player => player.PlayerId == playerId);
        if (readyPlayer is not null)
        {
            return readyPlayer;
        }

        return new ChatLobbyReadyPlayer
        {
            PlayerId = playerId,
            DisplayName = GetPlayerIdentityService().GetDisplayName(playerId),
            IsReady = false,
            ReadyPayload = Array.Empty<byte>()
        };
    }

    private ChatLobbyPlayer[] BuildPlayers(ChatLobbyState lobby, ChatLobbyReadyStateResponse readyState)
    {
        return lobby.ConnectedPlayers
            .OrderBy(playerId => GetPlayerIdentityService().GetDisplayName(playerId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(playerId => playerId)
            .Select(playerId =>
            {
                ChatLobbyReadyPlayer? readyPlayer = readyState.Players.FirstOrDefault(player => player.PlayerId == playerId);
                return new ChatLobbyPlayer
                {
                    PlayerId = playerId,
                    DisplayName = readyPlayer?.DisplayName ?? GetPlayerIdentityService().GetDisplayName(playerId),
                    IsReady = readyPlayer is { IsReady: true },
                    ReadyPayload = readyPlayer?.ReadyPayload ?? Array.Empty<byte>()
                };
            })
            .ToArray();
    }

    private int GetRequiredReadyPlayerCount(int connectedPlayerCount)
    {
        if (connectedPlayerCount == 0)
        {
            return 0;
        }

        float threshold = Math.Clamp(GetHost().Config.ChatLobby.ReadyThresholdPercent, 0f, 100f);
        int required = (int)Math.Ceiling(connectedPlayerCount * (threshold / 100f));
        return Math.Clamp(required, 1, connectedPlayerCount);
    }

    private byte[] BuildReadyTransitionPayload()
    {
        return GetPayloadCodec().Encode(BuildReadyState(EnsureLobbyState()));
    }

    private void DispatchToUi(Action action)
    {
        if (_dispatchAsync is not null)
        {
            _ = _dispatchAsync(action);
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
