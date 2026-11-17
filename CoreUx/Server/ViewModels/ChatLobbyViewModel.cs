using System.Collections.ObjectModel;
using System.Windows.Input;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Common.ViewModels;
using HawkNet.CoreUx.Server.Services;

namespace HawkNet.CoreUx.Server.ViewModels;

public sealed class ChatLobbyViewModel : ObservableObject, IDisposable
{
    private readonly IChatLobbyRuntime _runtime;
    private string _pendingMessage = string.Empty;
    private int _maxPlayers = 64;
    private string _mapName = "Map preview pending";
    private string _statusMessage = "Server lobby is running.";
    private string _countdownText = "No countdown";
    private bool _hasLoadedSnapshot;
    private bool _manualMatchStartRequested;

    public ChatLobbyViewModel()
        : this(new MockChatLobbyRuntime())
    {
    }

    public ChatLobbyViewModel(IChatLobbyRuntime runtime)
    {
        _runtime = runtime;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _runtime.ChatMessageReceived += OnChatMessageReceived;
        _runtime.ReadyStateChanged += OnReadyStateChanged;
        _runtime.CountdownChanged += OnCountdownChanged;
        _runtime.MatchStarted += OnRuntimeMatchStarted;
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, () => !string.IsNullOrWhiteSpace(PendingMessage));
        StartMatchCommand = new AsyncRelayCommand(StartMatchAsync);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync);
        Initialization = LoadInitialSnapshotAsync();
    }

    public string Title => "Chat Lobby";

    public Task Initialization { get; }

    public ObservableCollection<ChatLobbyMemberViewModel> Members { get; } = new();

    public ObservableCollection<ChatMessageViewModel> ChatMessages { get; } = new();

    public ICommand SendMessageCommand { get; }

    public ICommand StartMatchCommand { get; }

    public ICommand StopServerCommand { get; }

    public event EventHandler<LoadingTransitionRequest>? MatchStartRequested;

    public event EventHandler? MatchStarted;

    public event EventHandler? StopServerRequested;

    public event EventHandler<string>? RuntimeErrorOccurred;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
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

    public int MaxPlayers
    {
        get => _maxPlayers;
        set
        {
            if (SetProperty(ref _maxPlayers, value))
            {
                OnPropertyChanged(nameof(PlayerCountText));
            }
        }
    }

    public string MapName
    {
        get => _mapName;
        set => SetProperty(ref _mapName, value);
    }

    public int PlayerCount => Members.Count;

    public int ReadyPlayerCount => Members.Count(member => member.IsReady);

    public string PlayerCountText => $"{PlayerCount} / {MaxPlayers}";

    public string ReadyCountText => $"{ReadyPlayerCount} ready";

    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    public void ApplyPlayers(IEnumerable<ChatLobbyPlayer> players)
    {
        Members.Clear();
        foreach (ChatLobbyPlayer player in players
            .OrderBy(player => player.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.PlayerId))
        {
            Members.Add(new ChatLobbyMemberViewModel(player.PlayerId, player.DisplayName, player.IsReady));
        }

        OnLobbyCountsChanged();
    }

    public void ApplyChatHistory(IEnumerable<ChatEntry> entries)
    {
        ChatMessages.Clear();
        foreach (ChatEntry entry in entries.OrderBy(entry => entry.TimestampUtc))
        {
            ChatMessages.Add(new ChatMessageViewModel(
                entry.PlayerId,
                entry.DisplayName,
                entry.TimestampUtc,
                entry.Message));
        }
    }

    public void AddChatMessage(ChatEntry entry)
    {
        var message = new ChatMessageViewModel(
            entry.PlayerId,
            entry.DisplayName,
            entry.TimestampUtc,
            entry.Message);
        ChatMessages.Add(message);
        StatusMessage = $"{message.SenderLabel} sent a message.";
    }

    public void ApplyReadyState(ChatLobbyReadyStateResponse readyState)
    {
        foreach (ChatLobbyReadyPlayer readyPlayer in readyState.Players)
        {
            ChatLobbyMemberViewModel? member = Members.FirstOrDefault(member => member.PlayerId == readyPlayer.PlayerId);
            if (member is not null)
            {
                member.IsReady = readyPlayer.IsReady;
            }
        }

        OnLobbyCountsChanged();
    }

    public void ApplyCountdown(ChatLobbyCountdownResponse? countdown)
    {
        if (countdown is null || countdown.Status == ChatLobbyCountdownStatus.None)
        {
            CountdownText = "No countdown";
            return;
        }

        CountdownText = $"{countdown.Status}: {Math.Ceiling(Math.Max(0f, countdown.RemainingSeconds)):0}s";
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

    private Task StartMatchAsync(CancellationToken token)
    {
        _manualMatchStartRequested = true;
        MatchStartRequested?.Invoke(
            this,
            new LoadingTransitionRequest(TransitionKeys.StartMatch, _runtime.StartMatchAsync));
        return Task.CompletedTask;
    }

    private Task StopServerAsync(CancellationToken token)
    {
        StopServerRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void OnLobbyCountsChanged()
    {
        OnPropertyChanged(nameof(PlayerCount));
        OnPropertyChanged(nameof(ReadyPlayerCount));
        OnPropertyChanged(nameof(PlayerCountText));
        OnPropertyChanged(nameof(ReadyCountText));
    }

    private async Task LoadInitialSnapshotAsync()
    {
        try
        {
            ApplySnapshot(await _runtime.LoadSnapshotAsync());
        }
        catch (Exception exception)
        {
            StatusMessage = $"Lobby load failed: {exception.Message}";
            RuntimeErrorOccurred?.Invoke(this, StatusMessage);
        }
    }

    private void ApplySnapshot(ChatLobbySnapshot snapshot)
    {
        UpdateSnapshotStatus(snapshot);
        MaxPlayers = snapshot.MaxPlayers;
        MapName = snapshot.MapName;
        ApplyPlayers(snapshot.Players);
        ApplyChatHistory(snapshot.ChatHistory);
        ApplyReadyState(snapshot.ReadyState);
        ApplyCountdown(snapshot.Countdown);
        _hasLoadedSnapshot = true;
    }

    private void UpdateSnapshotStatus(ChatLobbySnapshot snapshot)
    {
        if (!_hasLoadedSnapshot)
        {
            StatusMessage = "Server lobby is running.";
            return;
        }

        int[] previousIds = Members.Select(member => member.PlayerId).ToArray();
        int[] currentIds = snapshot.Players.Select(player => player.PlayerId).ToArray();
        int[] joinedIds = currentIds.Except(previousIds).ToArray();
        int[] leftIds = previousIds.Except(currentIds).ToArray();

        if (joinedIds.Length > 0)
        {
            ChatLobbyPlayer joined = snapshot.Players.First(player => player.PlayerId == joinedIds[0]);
            StatusMessage = $"[{joined.PlayerId}][{joined.DisplayName}] joined.";
            return;
        }

        if (leftIds.Length > 0)
        {
            ChatLobbyMemberViewModel left = Members.First(member => member.PlayerId == leftIds[0]);
            StatusMessage = $"{left.DisplayLabel} left.";
            return;
        }

        StatusMessage = $"Lobby updated. {snapshot.Players.Count} player(s) connected.";
    }

    private void OnSnapshotChanged(object? sender, ChatLobbySnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    private void OnChatMessageReceived(object? sender, ChatEntry entry)
    {
        AddChatMessage(entry);
    }

    private void OnReadyStateChanged(object? sender, ChatLobbyReadyStateResponse readyState)
    {
        ApplyReadyState(readyState);
    }

    private void OnCountdownChanged(object? sender, ChatLobbyCountdownResponse? countdown)
    {
        ApplyCountdown(countdown);
    }

    private void OnRuntimeMatchStarted(object? sender, EventArgs e)
    {
        if (_manualMatchStartRequested)
        {
            _manualMatchStartRequested = false;
            return;
        }

        MatchStarted?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _runtime.SnapshotChanged -= OnSnapshotChanged;
        _runtime.ChatMessageReceived -= OnChatMessageReceived;
        _runtime.ReadyStateChanged -= OnReadyStateChanged;
        _runtime.CountdownChanged -= OnCountdownChanged;
        _runtime.MatchStarted -= OnRuntimeMatchStarted;
        _runtime.Dispose();
    }
}
