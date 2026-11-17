using System.Collections.ObjectModel;
using System.Windows.Input;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.World;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Common.ViewModels;
using HawkNet.CoreUx.Server.Services;

namespace HawkNet.CoreUx.Server.ViewModels;

public sealed class ServerMatchViewModel : ObservableObject, IDisposable
{
    private readonly IServerMatchRuntime _runtime;
    private string _variantId = "base";
    private string _phaseId = "match";
    private DateTime _matchStartedAtUtc = DateTime.UtcNow;
    private DateTime _matchEndsAtUtc = DateTime.UtcNow.AddMinutes(5);
    private int _maxPlayers = 64;
    private TimeSpan _gameTime;
    private TimeSpan _phaseTime;
    private string _pendingMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private string _playerSearchText = string.Empty;
    private string _inspectorSection = InspectorSections.Players;
	private ServerMonitorSnapshot? _monitor;
	private WorldRegionDescriptor? _selectedRegion;
	private bool _connectedPlayersOnly;

    public ServerMatchViewModel()
        : this(new MockServerMatchRuntime())
    {
    }

    public ServerMatchViewModel(IServerMatchRuntime runtime)
    {
        _runtime = runtime;
        World.MovementPathScope = WorldMovementPathScope.AllMovingEntities;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _runtime.PhaseChanged += OnPhaseChanged;
        _runtime.ChatMessageReceived += OnChatMessageReceived;
        _runtime.GameTimeChanged += OnGameTimeChanged;
        _runtime.PhaseTimeChanged += OnPhaseTimeChanged;
        _runtime.WorldSnapshotChanged += OnWorldSnapshotChanged;
        World.PropertyChanged += OnWorldPropertyChanged;
        StopMatchCommand = new AsyncRelayCommand(StopMatchAsync);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync);
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessage);
        ShowMapInspectorCommand = new RelayCommand(() => InspectorSection = InspectorSections.Map);
        ShowPlayersInspectorCommand = new RelayCommand(() => InspectorSection = InspectorSections.Players);
        ShowEventsInspectorCommand = new RelayCommand(() => InspectorSection = InspectorSections.Events);
        ShowAdminInspectorCommand = new RelayCommand(() => InspectorSection = InspectorSections.Admin);
        ShowSettingsInspectorCommand = new RelayCommand(() => InspectorSection = InspectorSections.Settings);
		SelectRegionCommand = new AsyncRelayCommand(SelectRegionAsync, () => SelectedRegion is not null);
        Initialization = LoadInitialSnapshotAsync();
    }

    public string Title => "Match";

    public ObservableCollection<ChatMessageViewModel> ChatMessages { get; } = new();

    public ObservableCollection<MatchPlayerViewModel> Players { get; } = new();

    public ObservableCollection<MatchPlayerViewModel> FilteredPlayers { get; } = new();

    public WorldViewModel World { get; } = new();

    public Task Initialization { get; }

    public ICommand StopMatchCommand { get; }

    public ICommand StopServerCommand { get; }

    public ICommand SendMessageCommand { get; }

    public ICommand ShowMapInspectorCommand { get; }

    public ICommand ShowPlayersInspectorCommand { get; }

    public ICommand ShowEventsInspectorCommand { get; }

    public ICommand ShowAdminInspectorCommand { get; }

    public ICommand ShowSettingsInspectorCommand { get; }

    public event EventHandler<LoadingTransitionRequest>? ReturnToLobbyRequested;

    public event EventHandler? StopServerRequested;

    public event EventHandler<string>? RuntimeErrorOccurred;

    public string VariantId
    {
        get => _variantId;
        private set => SetProperty(ref _variantId, value);
    }

    public string PhaseId
    {
        get => _phaseId;
        private set => SetProperty(ref _phaseId, value);
    }

    public DateTime MatchStartedAtUtc
    {
        get => _matchStartedAtUtc;
        private set => SetProperty(ref _matchStartedAtUtc, value);
    }

    public DateTime MatchEndsAtUtc
    {
        get => _matchEndsAtUtc;
        private set => SetProperty(ref _matchEndsAtUtc, value);
    }

    public int MaxPlayers
    {
        get => _maxPlayers;
        set
        {
            if (SetProperty(ref _maxPlayers, value))
            {
                OnMatchStatsChanged();
            }
        }
    }

    public int PlayerCount => Players.Count;

    public int ConnectedPlayerCount => Players.Count(player => player.IsConnected);

    public string PlayerCountText => $"{ConnectedPlayerCount} / {MaxPlayers}";
	public string ServerStateText => _monitor?.LifecycleState.ToString() ?? "Unknown";
	public string ServerHealthText => _monitor?.Health ?? "Unknown";
	public string ListenEndpointText => _monitor?.ListenEndPoint?.ToString() ?? "Not listening";
	public string InboxStatusText => _monitor is null ? "Unavailable" : $"{_monitor.Inbox.Depth} / {_monitor.Inbox.Capacity} queued";
	public string UdpStatusText => _monitor is null ? "Unavailable"
		: $"window {_monitor.UdpDelivery.CurrentWindowOccupancy}/{_monitor.UdpDelivery.HighWaterWindowOccupancy}, " +
		  $"retry {_monitor.UdpDelivery.Retransmissions}, drop {_monitor.UdpDelivery.StaleSequencedDrops}";
    public ObservableCollection<string> RuntimeEvents { get; } = new();
	public ObservableCollection<WorldRegionDescriptor> Regions { get; } = new();
	public ICommand SelectRegionCommand { get; }
	public WorldRegionDescriptor? SelectedRegion
	{
		get => _selectedRegion;
		set
		{
			if (SetProperty(ref _selectedRegion, value) && SelectRegionCommand is AsyncRelayCommand command) command.RaiseCanExecuteChanged();
		}
	}
	public bool ConnectedPlayersOnly
	{
		get => _connectedPlayersOnly;
		set { if (SetProperty(ref _connectedPlayersOnly, value)) RefreshFilteredPlayers(); }
	}

    public TimeSpan GameTime
    {
        get => _gameTime;
        private set
        {
            if (SetProperty(ref _gameTime, value))
            {
                OnPropertyChanged(nameof(GameTimeText));
                OnPropertyChanged(nameof(MatchTimeText));
            }
        }
    }

    public string GameTimeText => FormatDuration(GameTime);

    public TimeSpan PhaseTime
    {
        get => _phaseTime;
        private set
        {
            if (SetProperty(ref _phaseTime, value))
            {
                OnPropertyChanged(nameof(PhaseTimeText));
            }
        }
    }

    public TimeSpan PhaseDuration => TotalMatchTime;

    public string PhaseTimeText => $"{FormatDuration(PhaseTime)} / {FormatDuration(PhaseDuration)}";

    public TimeSpan TotalMatchTime => MatchEndsAtUtc > MatchStartedAtUtc
        ? MatchEndsAtUtc - MatchStartedAtUtc
        : TimeSpan.Zero;

    public TimeSpan ElapsedMatchTime
    {
        get
        {
            if (MatchStartedAtUtc == default)
            {
                return TimeSpan.Zero;
            }

            DateTime now = DateTime.UtcNow;
            if (now <= MatchStartedAtUtc)
            {
                return TimeSpan.Zero;
            }

            DateTime cappedNow = now > MatchEndsAtUtc ? MatchEndsAtUtc : now;
            return cappedNow - MatchStartedAtUtc;
        }
    }

    public string MatchTimeText => GameTimeText;

    public string PhaseText => string.IsNullOrWhiteSpace(PhaseId) ? "Unknown phase" : PhaseId;

    public string MatchEndsAtText => MatchEndsAtUtc.ToLocalTime().ToString("HH:mm");

    public string InspectorSection
    {
        get => _inspectorSection;
        private set
        {
            if (SetProperty(ref _inspectorSection, value))
            {
                OnPropertyChanged(nameof(IsMapInspectorVisible));
                OnPropertyChanged(nameof(IsPlayersInspectorVisible));
                OnPropertyChanged(nameof(IsEventsInspectorVisible));
                OnPropertyChanged(nameof(IsAdminInspectorVisible));
                OnPropertyChanged(nameof(IsSettingsInspectorVisible));
            }
        }
    }

    public bool IsMapInspectorVisible => InspectorSection == InspectorSections.Map;

    public bool IsPlayersInspectorVisible => InspectorSection == InspectorSections.Players;

    public bool IsEventsInspectorVisible => InspectorSection == InspectorSections.Events;

    public bool IsAdminInspectorVisible => InspectorSection == InspectorSections.Admin;

    public bool IsSettingsInspectorVisible => InspectorSection == InspectorSections.Settings;

    public string PlayerSearchText
    {
        get => _playerSearchText;
        set
        {
            if (SetProperty(ref _playerSearchText, value))
            {
                RefreshFilteredPlayers();
            }
        }
    }

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

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasSelectedPlayer => World.HasSelectedEntity;

    public string SelectedPlayerName => World.SelectedEntityDisplayName;

    public string SelectedPlayerIdText => World.SelectedEntity?.PlayerId?.ToString() ?? "Unknown";

    public string SelectedNavigatorId => World.SelectedEntity?.Id ?? string.Empty;

    public string SelectedMovementStatus => World.SelectedEntityMovementStatus;

    public string SelectedWorldPositionText => SelectedServerPositionText;

    public string SelectedServerPositionText => World.SelectedEntity is null
        ? string.Empty
        : FormatWorldPoint(World.SelectedEntity.WorldX, World.SelectedEntity.WorldZ);

    public string SelectedRenderPositionText => World.SelectedEntity is null
        ? string.Empty
        : FormatWorldPoint(World.SelectedEntity.RenderWorldX, World.SelectedEntity.RenderWorldZ);

    public string SelectedDestinationText => World.SelectedEntity is null
        ? string.Empty
        : FormatWorldPoint(World.SelectedEntity.DestinationWorldX, World.SelectedEntity.DestinationWorldZ);

    public string SelectedSpeedText => World.SelectedEntity is null
        ? string.Empty
        : $"{World.SelectedEntity.MaxSpeed:0.##}";

    public string SelectedRotationSpeedText => World.SelectedEntity is null
        ? string.Empty
        : $"{World.SelectedEntity.MaxRotationSpeed:0.##}";

    public string SelectedPathIndexText => World.SelectedEntity is null
        ? string.Empty
        : $"{World.SelectedEntity.CurrentPathIndex} / {World.SelectedEntity.PathWorldPositions.Count}";

    public string SelectedCorrectionErrorText => World.SelectedEntity is null
        ? string.Empty
        : $"{Distance(World.SelectedEntity.RenderWorldX, World.SelectedEntity.RenderWorldZ, World.SelectedEntity.WorldX, World.SelectedEntity.WorldZ):0.###}";

    public void ApplyPayload(MatchPayload payload)
    {
        VariantId = payload.VariantId;
        PhaseId = payload.PhaseId;
        MatchStartedAtUtc = payload.PhaseStartedAtUtc;
        MatchEndsAtUtc = payload.PhaseEndsAtUtc;
        OnPropertyChanged(nameof(MatchEndsAtText));
        ApplyPlayers(payload.Players);
        World.ApplyPlayers(payload.Players, localPlayerId: null);
        OnMatchStatsChanged();
    }

    public void ApplyPhaseChange(MatchPhaseChangeResponse phaseChange)
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
    }

    public void ApplyPlayers(IEnumerable<MatchPlayer> players)
    {
        Players.Clear();
        foreach (MatchPlayer player in players
            .OrderBy(player => player.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.PlayerId))
        {
            Players.Add(new MatchPlayerViewModel(player.PlayerId, player.DisplayName, player.IsConnected, SelectPlayer));
        }

        RefreshFilteredPlayers();
        OnMatchStatsChanged();
    }

    public void ApplyChatHistory(IEnumerable<ChatEntry> entries)
    {
        ChatMessages.Clear();
        foreach (ChatEntry entry in entries.OrderBy(entry => entry.TimestampUtc))
        {
            AddChatMessage(entry);
        }
    }

    public void AddChatMessage(ChatEntry entry)
    {
        ChatMessages.Add(new ChatMessageViewModel(
            entry.PlayerId,
            entry.DisplayName,
            entry.TimestampUtc,
            entry.Message));
    }

    private void OnMatchStatsChanged()
    {
        OnPropertyChanged(nameof(PlayerCount));
        OnPropertyChanged(nameof(ConnectedPlayerCount));
        OnPropertyChanged(nameof(PlayerCountText));
        OnPropertyChanged(nameof(TotalMatchTime));
        OnPropertyChanged(nameof(ElapsedMatchTime));
        OnPropertyChanged(nameof(MatchTimeText));
        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(PhaseDuration));
        OnPropertyChanged(nameof(PhaseTimeText));
    }

    private void RefreshFilteredPlayers()
    {
        string search = PlayerSearchText.Trim();
        FilteredPlayers.Clear();
		foreach (MatchPlayerViewModel player in Players.Where(player =>
			(!ConnectedPlayersOnly || player.IsConnected)
			&& (string.IsNullOrEmpty(search)
				|| player.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| player.PlayerId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))))
        {
            FilteredPlayers.Add(player);
        }
    }

    private void SelectPlayer(int playerId)
    {
        WorldEntityViewModel? entity = World.Entities.FirstOrDefault(candidate => candidate.PlayerId == playerId);
        if (entity is not null)
        {
			World.FocusEntity(entity);
        }
    }

    private Task StopMatchAsync(CancellationToken token)
    {
        ReturnToLobbyRequested?.Invoke(
            this,
            new LoadingTransitionRequest(TransitionKeys.ReturnToLobby, _runtime.StopMatchAsync));
        return Task.CompletedTask;
    }

    private bool CanSendMessage()
    {
        return !string.IsNullOrWhiteSpace(PendingMessage);
    }

    private async Task SendMessageAsync(CancellationToken token)
    {
        string message = PendingMessage.Trim();
        if (string.IsNullOrWhiteSpace(message))
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

    private Task StopServerAsync(CancellationToken token)
    {
        StopServerRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss\.ff")
            : duration.ToString(@"mm\:ss\.ff");
    }

    private static string FormatWorldPoint(float x, float z)
    {
        return $"{x:0.##}, {z:0.##}";
    }

    private static float Distance(float ax, float az, float bx, float bz)
    {
        float dx = bx - ax;
        float dz = bz - az;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    private async Task LoadInitialSnapshotAsync()
    {
        try
        {
            ApplySnapshot(await _runtime.LoadSnapshotAsync());
        }
        catch (Exception exception)
        {
            RuntimeErrorOccurred?.Invoke(this, $"Match load failed: {exception.Message}");
        }
    }

    private void ApplySnapshot(ServerMatchSnapshot snapshot)
    {
		_monitor = snapshot.Monitor;
		Regions.Clear();
		foreach (WorldRegionDescriptor region in snapshot.Regions) Regions.Add(region);
		SelectedRegion = Regions.FirstOrDefault(region => string.Equals(region.Id, snapshot.World?.RegionId, StringComparison.OrdinalIgnoreCase)) ?? Regions.FirstOrDefault();
		OnPropertyChanged(nameof(ServerStateText));
		OnPropertyChanged(nameof(ServerHealthText));
		OnPropertyChanged(nameof(ListenEndpointText));
		OnPropertyChanged(nameof(InboxStatusText));
		OnPropertyChanged(nameof(UdpStatusText));
        MaxPlayers = snapshot.MaxPlayers;
        GameTime = snapshot.GameTime;
        PhaseTime = snapshot.PhaseTime;
        ApplyPayload(snapshot.Payload);
        ApplyChatHistory(snapshot.ChatHistory);
        if (snapshot.World is not null)
        {
            World.ApplySnapshot(snapshot.World);
            OnSelectedPlayerChanged();
        }
    }

    private void OnSnapshotChanged(object? sender, ServerMatchSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    private void OnPhaseChanged(object? sender, MatchPhaseChangeResponse phaseChange)
    {
        ApplyPhaseChange(phaseChange);
		AddRuntimeEvent($"Phase changed to {phaseChange.PhaseId}");
    }

    private void OnChatMessageReceived(object? sender, ChatEntry entry)
    {
        AddChatMessage(entry);
		AddRuntimeEvent($"Chat from {entry.DisplayName}");
    }

	private void AddRuntimeEvent(string message)
	{
		RuntimeEvents.Insert(0, $"{DateTime.Now:T}  {message}");
		while (RuntimeEvents.Count > 100) RuntimeEvents.RemoveAt(RuntimeEvents.Count - 1);
	}

	private async Task SelectRegionAsync(CancellationToken token)
	{
		if (SelectedRegion is null) return;
		await _runtime.SelectRegionAsync(SelectedRegion.Id, token);
		AddRuntimeEvent($"Viewing region {SelectedRegion.DisplayName}");
	}

    private void OnGameTimeChanged(object? sender, TimeSpan gameTime)
    {
        GameTime = gameTime;
    }

    private void OnPhaseTimeChanged(object? sender, TimeSpan phaseTime)
    {
        PhaseTime = phaseTime;
    }

    private void OnWorldSnapshotChanged(object? sender, WorldSnapshot snapshot)
    {
        World.ApplySnapshot(snapshot);
        OnSelectedPlayerChanged();
    }

    private void OnWorldPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(World.SelectedEntity)
            || e.PropertyName == nameof(World.HasSelectedEntity)
            || e.PropertyName == nameof(World.SelectedEntityDisplayName)
            || e.PropertyName == nameof(World.SelectedEntityMovementStatus))
        {
            OnSelectedPlayerChanged();
        }
    }

    private void OnSelectedPlayerChanged()
    {
        OnPropertyChanged(nameof(HasSelectedPlayer));
        OnPropertyChanged(nameof(SelectedPlayerName));
        OnPropertyChanged(nameof(SelectedPlayerIdText));
        OnPropertyChanged(nameof(SelectedNavigatorId));
        OnPropertyChanged(nameof(SelectedMovementStatus));
        OnPropertyChanged(nameof(SelectedWorldPositionText));
        OnPropertyChanged(nameof(SelectedServerPositionText));
        OnPropertyChanged(nameof(SelectedRenderPositionText));
        OnPropertyChanged(nameof(SelectedDestinationText));
        OnPropertyChanged(nameof(SelectedSpeedText));
        OnPropertyChanged(nameof(SelectedRotationSpeedText));
        OnPropertyChanged(nameof(SelectedPathIndexText));
        OnPropertyChanged(nameof(SelectedCorrectionErrorText));
    }

    public void Dispose()
    {
        _runtime.SnapshotChanged -= OnSnapshotChanged;
        _runtime.PhaseChanged -= OnPhaseChanged;
        _runtime.ChatMessageReceived -= OnChatMessageReceived;
        _runtime.GameTimeChanged -= OnGameTimeChanged;
        _runtime.PhaseTimeChanged -= OnPhaseTimeChanged;
        _runtime.WorldSnapshotChanged -= OnWorldSnapshotChanged;
        World.PropertyChanged -= OnWorldPropertyChanged;
        _runtime.Dispose();
    }
}

public static class InspectorSections
{
    public const string Map = "Map";
    public const string Players = "Players";
    public const string Events = "Events";
    public const string Admin = "Admin";
    public const string Settings = "Settings";
}
