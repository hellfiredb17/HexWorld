using Avalonia.Threading;
using HawkNet.Core;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.World;
using HawkNet.CoreUx.Server.Services;
using HawkNet.Game.GameState.Match;

namespace HawkNet.Desktop.Services;

public sealed class DesktopServerMatchRuntime : IServerMatchRuntime
{
    private const string MatchStateId = "match";
    private const string MatchPhaseTimerId = "match_phase";
    private const string LobbyStateId = "chat_lobby";
    private readonly GameHost? _host;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly Func<Action, Task>? _dispatchAsync;
    private TaskCompletionSource? _returnToLobbyCompletion;
    private bool _disposed;
	private readonly IServerWorldMonitorProvider? _worldMonitor;
	private float _monitorElapsed;
	private string? _selectedRegionId;

    public DesktopServerMatchRuntime(GameHost? host, Func<Action, Task>? dispatchAsync = null)
    {
        _host = host;
        _dispatchAsync = dispatchAsync;
		_worldMonitor = host is not null && host.Services.TryGet<IServerWorldMonitorProvider>(out var provider) ? provider : null;
        SubscribeToHostEvents();
    }

    public event EventHandler<ServerMatchSnapshot>? SnapshotChanged;

    public event EventHandler<MatchPhaseChangeResponse>? PhaseChanged;

    public event EventHandler<ChatEntry>? ChatMessageReceived;

    public event EventHandler<TimeSpan>? GameTimeChanged;

    public event EventHandler<TimeSpan>? PhaseTimeChanged;

    public event EventHandler<WorldSnapshot>? WorldSnapshotChanged;

	public async Task<ServerMatchSnapshot> LoadSnapshotAsync(CancellationToken token = default)
    {
		if (_host is null || _host.LifecycleState != GameHostLifecycleState.Running) return CreateSnapshot();
		ServerMatchSnapshot? snapshot = null;
		await _host.ExecutionScheduler.ScheduleAsync(_ =>
		{
			snapshot = CreateSnapshot();
			return Task.CompletedTask;
		}, token);
		return snapshot ?? throw new InvalidOperationException("The server did not produce a match snapshot.");
    }

    public Task SendMessageAsync(string message, CancellationToken token = default)
    {
        EnsureMatchState();
        return GetChatService().SendSystemMessageAsync(message, "Admin", token);
    }

    public async Task StopMatchAsync(CancellationToken token = default)
    {
        GameHost host = GetHost();
        if (string.Equals(host.ActiveGameStateId, LobbyStateId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnsureMatchState();
        if (_returnToLobbyCompletion is not null)
        {
            throw new InvalidOperationException("A return-to-lobby transition is already in progress.");
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _returnToLobbyCompletion = completion;
        using CancellationTokenRegistration registration = token.Register(() => completion.TrySetCanceled(token));

        try
        {
            await host.RequestGameStateTransitionAsync(LobbyStateId, Array.Empty<byte>(), token);
            await completion.Task;
        }
        finally
        {
            if (ReferenceEquals(_returnToLobbyCompletion, completion))
            {
                _returnToLobbyCompletion = null;
            }
        }
    }

	public Task SelectRegionAsync(string regionId, CancellationToken token = default)
	{
		_selectedRegionId = regionId;
		if (_host is null) return Task.CompletedTask;
		return _host.ExecutionScheduler.ScheduleAsync(_ =>
		{
			WorldSnapshot? snapshot = CreateWorldSnapshotOrNull();
			if (snapshot is not null) DispatchToUi(() => WorldSnapshotChanged?.Invoke(this, snapshot));
			return Task.CompletedTask;
		}, token);
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
        _returnToLobbyCompletion?.TrySetCanceled();
        _returnToLobbyCompletion = null;
    }

    private void SubscribeToHostEvents()
    {
        if (_host is null)
        {
            return;
        }

        _host.UpdateLoop.Update += OnUpdate;
        _subscriptions.Add(_host.Events.Subscribe<MatchStartedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_host.Events.Subscribe<MatchPhaseChangedEvent>(OnMatchPhaseChangedAsync));
        _subscriptions.Add(_host.Events.Subscribe<MatchPlayerJoinedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_host.Events.Subscribe<MatchPlayerDisconnectedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_host.Events.Subscribe<ChatMessageLoggedEvent>(OnChatMessageLoggedAsync));
        _subscriptions.Add(_host.Events.Subscribe<GameStateTransitionCompletedEvent>(OnTransitionCompletedAsync));
        _subscriptions.Add(_host.Events.Subscribe<GameStateTransitionFailedEvent>(OnTransitionFailedAsync));
    }

    private void OnUpdate(float delta)
    {
        if (_disposed || _host?.ActiveGameState is not MatchState)
        {
            return;
        }

		_monitorElapsed += delta;
		if (_monitorElapsed < 0.1f) return;
		_monitorElapsed = 0;
        TimeSpan gameTime = _host.UpdateLoop.GetTrackTime(MatchStateId);
        TimeSpan phaseTime = _host.UpdateLoop.GetTrackTime(MatchPhaseTimerId);
        WorldSnapshot? world = CreateWorldSnapshotOrNull();
        DispatchToUi(() =>
        {
            GameTimeChanged?.Invoke(this, gameTime);
            PhaseTimeChanged?.Invoke(this, phaseTime);
            if (world is not null)
            {
                WorldSnapshotChanged?.Invoke(this, world);
            }
        });
    }

    private Task RefreshSnapshotAsync()
    {
        if (_disposed || _host?.ActiveGameState is not MatchState)
        {
            return Task.CompletedTask;
        }

        ServerMatchSnapshot snapshot = CreateSnapshot();
        DispatchToUi(() => SnapshotChanged?.Invoke(this, snapshot));
        return Task.CompletedTask;
    }

    private Task OnMatchPhaseChangedAsync(MatchPhaseChangedEvent gameEvent, CancellationToken token)
    {
        var phaseChange = new MatchPhaseChangeResponse
        {
            VariantId = gameEvent.Payload.VariantId,
            PhaseId = gameEvent.Payload.PhaseId,
            PhaseSeconds = gameEvent.Payload.PhaseSeconds,
            PhaseStartedAtUtc = gameEvent.Payload.PhaseStartedAtUtc,
            PhaseEndsAtUtc = gameEvent.Payload.PhaseEndsAtUtc,
            Players = gameEvent.Payload.Players
        };
        DispatchToUi(() =>
        {
            PhaseChanged?.Invoke(this, phaseChange);
            SnapshotChanged?.Invoke(this, CreateSnapshot());
        });
        return Task.CompletedTask;
    }

    private Task OnChatMessageLoggedAsync(ChatMessageLoggedEvent gameEvent, CancellationToken token)
    {
        DispatchToUi(() => ChatMessageReceived?.Invoke(this, gameEvent.Entry));
        return Task.CompletedTask;
    }

    private Task OnTransitionCompletedAsync(GameStateTransitionCompletedEvent gameEvent, CancellationToken token)
    {
        if (string.Equals(gameEvent.ToStateId, LobbyStateId, StringComparison.OrdinalIgnoreCase))
        {
            _returnToLobbyCompletion?.TrySetResult();
        }

        return Task.CompletedTask;
    }

    private Task OnTransitionFailedAsync(GameStateTransitionFailedEvent gameEvent, CancellationToken token)
    {
        if (string.Equals(gameEvent.ToStateId, LobbyStateId, StringComparison.OrdinalIgnoreCase))
        {
            _returnToLobbyCompletion?.TrySetException(new InvalidOperationException(gameEvent.Reason));
        }

        return Task.CompletedTask;
    }

    private ServerMatchSnapshot CreateSnapshot()
    {
        GameHost host = GetHost();
        MatchPayload payload = CreatePayload();
        return new ServerMatchSnapshot
        {
			Monitor = host.GetMonitorSnapshot(),
			Regions = _worldMonitor?.GetRegions(host) ?? Array.Empty<WorldRegionDescriptor>(),
            Payload = payload,
            ChatHistory = GetChatService().GetHistory(),
            MaxPlayers = host.Config.Server.MaxPlayers,
            GameTime = host.UpdateLoop.GetTrackTime(MatchStateId),
            PhaseTime = host.UpdateLoop.GetTrackTime(MatchPhaseTimerId),
            World = CreateWorldSnapshotOrNull()
        };
    }

    private WorldSnapshot? CreateWorldSnapshotOrNull()
    {
		return _worldMonitor?.Capture(GetHost(), _selectedRegionId);
    }

    private MatchPayload CreatePayload()
    {
        GameHost host = GetHost();
        MatchState match = EnsureMatchState();
        byte[] payloadBytes = match.CreatePayloadForPlayer(0, GetPayloadCodec());
        return GetPayloadCodec().Decode<MatchPayload>(payloadBytes);
    }

    private MatchState EnsureMatchState()
    {
        GameHost host = GetHost();
        return host.ActiveGameState as MatchState
            ?? throw new InvalidOperationException("The active server state is not the match.");
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
