using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.Services;
using HawkNet.CoreUx.Client.World;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.ViewModels;

namespace HawkNet.CoreUx.Client.ViewModels;

public sealed class ClientMatchViewModel : ObservableObject, IDisposable
{
    private readonly IClientMatchRuntime _runtime;
    private readonly DispatcherTimer _phaseTimer;
    private readonly DispatcherTimer _worldRefreshTimer;
    private readonly DispatcherTimer _worldUpdateTimer;
    private DateTime _lastWorldUpdateUtc;
    private int _emptyWorldRefreshAttempts;
    private bool _disposed;
    private string _variantId = "base";
    private string _phaseId = "match";
    private DateTime _phaseEndsAtUtc;
    private string _pendingMessage = string.Empty;
    private string _commandId = "ping";
    private string _commandPayload = string.Empty;
    private string _statusMessage = "Match state received.";
    private const string DefaultRegionId = "rect island";

    public ClientMatchViewModel()
        : this(new MockClientMatchRuntime())
    {
    }

    public ClientMatchViewModel(IClientMatchRuntime runtime)
        : this(runtime, WorldViewerConfig.CreateDefault())
    {
    }

    public ClientMatchViewModel(IClientMatchRuntime runtime, WorldViewerConfig worldViewerConfig)
    {
        _runtime = runtime;
        World = new WorldViewModel(worldViewerConfig);
        World.MovementPathScope = WorldMovementPathScope.LocalPlayer;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _runtime.PhaseChanged += OnPhaseChanged;
        _runtime.ChatMessageReceived += OnChatMessageReceived;
        _runtime.WorldSnapshotChanged += OnWorldSnapshotChanged;
        _runtime.WorldEntityChanged += OnWorldEntityChanged;
        World.MoveRequested += OnWorldMoveRequested;
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, () => !string.IsNullOrWhiteSpace(PendingMessage));
        SendCommandCommand = new AsyncRelayCommand(SendCommandAsync, () => !string.IsNullOrWhiteSpace(CommandId));
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        _phaseTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnPhaseTimerTick);
        _phaseTimer.Start();
        _lastWorldUpdateUtc = DateTime.UtcNow;
        _worldUpdateTimer = new DispatcherTimer(
            GetWorldUpdateInterval(World.Config),
            DispatcherPriority.Render,
            OnWorldUpdateTimerTick);
        _worldUpdateTimer.Start();
        _worldRefreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(750), DispatcherPriority.Background, OnWorldRefreshTimerTick);
        Initialization = LoadInitialSnapshotAsync();
    }

    public string Title => "Client Match";

    public Task Initialization { get; }

    public ObservableCollection<ChatMessageViewModel> ChatMessages { get; } = new();

    public ObservableCollection<MatchPlayerViewModel> Players { get; } = new();

    public WorldViewModel World { get; }

    public ICommand SendMessageCommand { get; }

    public ICommand SendCommandCommand { get; }

    public ICommand DisconnectCommand { get; }

    public event EventHandler? DisconnectRequested;

    public event EventHandler<string>? RuntimeErrorOccurred;

    public string VariantId
    {
        get => _variantId;
        private set => SetProperty(ref _variantId, value);
    }

    public string PhaseId
    {
        get => _phaseId;
        private set
        {
            if (SetProperty(ref _phaseId, value))
            {
                OnPropertyChanged(nameof(PhaseText));
            }
        }
    }

    public string PhaseText => string.IsNullOrWhiteSpace(PhaseId) ? "Unknown phase" : PhaseId;

    public DateTime PhaseEndsAtUtc
    {
        get => _phaseEndsAtUtc;
        private set
        {
            if (SetProperty(ref _phaseEndsAtUtc, value))
            {
                OnPropertyChanged(nameof(PhaseRemainingText));
            }
        }
    }

    public string PhaseRemainingText => PhaseEndsAtUtc <= DateTime.UtcNow
        ? "00:00"
        : (PhaseEndsAtUtc - DateTime.UtcNow).ToString(@"mm\:ss");

    public int PlayerCount => Players.Count;

    public int ConnectedPlayerCount => Players.Count(player => player.IsConnected);

    public string PlayerCountText => $"{ConnectedPlayerCount} / {PlayerCount}";

    public string PendingMessage
    {
        get => _pendingMessage;
        set
        {
            if (SetProperty(ref _pendingMessage, value) && SendMessageCommand is AsyncRelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public string CommandId
    {
        get => _commandId;
        set
        {
            if (SetProperty(ref _commandId, value) && SendCommandCommand is AsyncRelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public string CommandPayload
    {
        get => _commandPayload;
        set => SetProperty(ref _commandPayload, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private async Task LoadInitialSnapshotAsync()
    {
        try
        {
            ClientMatchSnapshot snapshot = await _runtime.LoadSnapshotAsync();
            if (_disposed)
            {
                return;
            }

            ApplySnapshot(snapshot);
            if (snapshot.World is null || snapshot.World.Entities.Count == 0)
            {
                await _runtime.RequestWorldStateAsync(DefaultRegionId);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Match load canceled.";
        }
        catch (ObjectDisposedException)
        {
            StatusMessage = "Match load canceled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Match load failed: {exception.Message}";
            RuntimeErrorOccurred?.Invoke(this, StatusMessage);
        }
    }

    private void ApplySnapshot(ClientMatchSnapshot snapshot)
    {
        ApplyPayload(snapshot.Payload);
        World.ApplyPlayers(snapshot.Payload.Players, snapshot.CurrentPlayerId);
        if (snapshot.World is not null)
        {
            World.ApplySnapshot(snapshot.World);
        }

        ChatMessages.Clear();
        foreach (ChatEntry entry in snapshot.ChatHistory.OrderBy(entry => entry.TimestampUtc))
        {
            AddChatMessage(entry);
        }
    }

    private void ApplyPayload(MatchPayload payload)
    {
        VariantId = payload.VariantId;
        PhaseId = payload.PhaseId;
        PhaseEndsAtUtc = payload.PhaseEndsAtUtc;
        Players.Clear();
        foreach (MatchPlayer player in payload.Players
            .OrderBy(player => player.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.PlayerId))
        {
            Players.Add(new MatchPlayerViewModel(player.PlayerId, player.DisplayName, player.IsConnected));
        }

        OnPropertyChanged(nameof(PlayerCount));
        OnPropertyChanged(nameof(ConnectedPlayerCount));
        OnPropertyChanged(nameof(PlayerCountText));
    }

    private void ApplyPhaseChange(MatchPhaseChangeResponse phaseChange)
    {
        ApplyPayload(new MatchPayload
        {
            VariantId = phaseChange.VariantId,
            PhaseId = phaseChange.PhaseId,
            PhaseSeconds = phaseChange.PhaseSeconds,
            PhaseStartedAtUtc = phaseChange.PhaseStartedAtUtc,
            PhaseEndsAtUtc = phaseChange.PhaseEndsAtUtc,
            Players = phaseChange.Players
        });
        World.ApplyPlayers(phaseChange.Players, World.LocalPlayerId);
        _ = RefreshWorldStateAsync();
    }

    private void AddChatMessage(ChatEntry entry)
    {
        ChatMessages.Add(new ChatMessageViewModel(
            entry.PlayerId,
            entry.DisplayName,
            entry.TimestampUtc,
            entry.Message));
    }

    private async Task SendMessageAsync(CancellationToken token)
    {
        string message = PendingMessage.Trim();
        if (message.Length == 0)
        {
            return;
        }

        try
        {
            await _runtime.SendMessageAsync(message, token);
            PendingMessage = string.Empty;
            StatusMessage = "Message sent.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Send failed: {exception.Message}";
            RuntimeErrorOccurred?.Invoke(this, StatusMessage);
        }
    }

    private async Task SendCommandAsync(CancellationToken token)
    {
        try
        {
            await _runtime.SendCommandAsync(CommandId, CommandPayload, token);
            StatusMessage = $"Command '{CommandId.Trim()}' sent.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Command failed: {exception.Message}";
            RuntimeErrorOccurred?.Invoke(this, StatusMessage);
        }
    }

    private Task DisconnectAsync(CancellationToken token)
    {
        DisconnectRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void OnSnapshotChanged(object? sender, ClientMatchSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    private void OnPhaseChanged(object? sender, MatchPhaseChangeResponse phaseChange)
    {
        ApplyPhaseChange(phaseChange);
    }

    private void OnChatMessageReceived(object? sender, ChatEntry entry)
    {
        AddChatMessage(entry);
    }

    private void OnWorldSnapshotChanged(object? sender, WorldSnapshot snapshot)
    {
        World.ApplySnapshot(snapshot);
        if (snapshot.Entities.Count == 0 && _emptyWorldRefreshAttempts < 5)
        {
            _worldRefreshTimer.Start();
        }
        else
        {
            _worldRefreshTimer.Stop();
        }
    }

    private void OnWorldEntityChanged(object? sender, WorldEntitySnapshot snapshot)
    {
        World.ApplyEntity(snapshot);
    }

    private async Task RefreshWorldStateAsync()
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            await _runtime.RequestWorldStateAsync(string.IsNullOrWhiteSpace(World.RegionId) ? DefaultRegionId : World.RegionId);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                StatusMessage = $"World refresh failed: {exception.Message}";
            }
        }
    }

    private async void OnWorldRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || World.Entities.Count > 0 || _emptyWorldRefreshAttempts >= 5)
        {
            _worldRefreshTimer.Stop();
            return;
        }

        _emptyWorldRefreshAttempts++;
        await RefreshWorldStateAsync();
    }

    private void OnPhaseTimerTick(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PhaseRemainingText));
    }

    private void OnWorldUpdateTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        float deltaTime = Math.Max(0f, (float)(nowUtc - _lastWorldUpdateUtc).TotalSeconds);
        _lastWorldUpdateUtc = nowUtc;
        World.Update(ClampWorldUpdateDelta(deltaTime, World.Config));
    }

    private async void OnWorldMoveRequested(object? sender, WorldMoveRequestEventArgs e)
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            await _runtime.SendMoveCommandAsync(e.Destination);
            StatusMessage = $"Move requested: {e.Tile.DisplayText}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Move canceled.";
        }
        catch (ObjectDisposedException)
        {
            StatusMessage = "Move canceled.";
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                StatusMessage = $"Move failed: {exception.Message}";
                RuntimeErrorOccurred?.Invoke(this, StatusMessage);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _runtime.SnapshotChanged -= OnSnapshotChanged;
        _runtime.PhaseChanged -= OnPhaseChanged;
        _runtime.ChatMessageReceived -= OnChatMessageReceived;
        _runtime.WorldSnapshotChanged -= OnWorldSnapshotChanged;
        _runtime.WorldEntityChanged -= OnWorldEntityChanged;
        World.MoveRequested -= OnWorldMoveRequested;
        _phaseTimer.Stop();
        _worldUpdateTimer.Stop();
        _worldRefreshTimer.Stop();
        _runtime.Dispose();
    }

    private static TimeSpan GetWorldUpdateInterval(WorldViewerConfig config)
    {
        int framesPerSecond = Math.Clamp(config.TargetUpdateFramesPerSecond, 1, 240);
        return TimeSpan.FromSeconds(1d / framesPerSecond);
    }

    internal static float ClampWorldUpdateDelta(float deltaTime, WorldViewerConfig config)
    {
        if (deltaTime <= 0f)
        {
            return 0f;
        }

        return Math.Min(deltaTime, (float)GetWorldUpdateInterval(config).TotalSeconds * 2f);
    }
}
