using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.Services;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.ViewModels;

namespace HawkNet.CoreUx.Client.ViewModels;

public sealed class ClientChatLobbyViewModel : ObservableObject, IDisposable
{
    private readonly IClientChatLobbyRuntime _runtime;
    private readonly DispatcherTimer _countdownTimer;
    private string _pendingMessage = string.Empty;
    private string _displayName = string.Empty;
    private string _statusMessage = "Connected to lobby.";
    private string _countdownText = "No countdown";
    private ChatLobbyCountdownResponse? _countdown;
    private bool _isReady;
    private int? _currentPlayerId;

    public ClientChatLobbyViewModel()
        : this(new MockClientChatLobbyRuntime())
    {
    }

    public ClientChatLobbyViewModel(IClientChatLobbyRuntime runtime)
    {
        _runtime = runtime;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _runtime.ChatMessageReceived += OnChatMessageReceived;
        _runtime.ReadyStateChanged += OnReadyStateChanged;
        _runtime.CountdownChanged += OnCountdownChanged;
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, () => !string.IsNullOrWhiteSpace(PendingMessage));
        ChangeDisplayNameCommand = new AsyncRelayCommand(ChangeDisplayNameAsync, () => !string.IsNullOrWhiteSpace(DisplayName));
        ToggleReadyCommand = new AsyncRelayCommand(ToggleReadyAsync);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        _countdownTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnCountdownTimerTick);
        _countdownTimer.Start();
        Initialization = LoadInitialSnapshotAsync();
    }

    public string Title => "Client Lobby";

    public Task Initialization { get; }

    public ObservableCollection<ChatLobbyMemberViewModel> Members { get; } = new();

    public ObservableCollection<ChatMessageViewModel> ChatMessages { get; } = new();

    public ICommand SendMessageCommand { get; }

    public ICommand ChangeDisplayNameCommand { get; }

    public ICommand ToggleReadyCommand { get; }

    public ICommand DisconnectCommand { get; }

    public event EventHandler? DisconnectRequested;

    public event EventHandler<string>? RuntimeErrorOccurred;

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

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value) && ChangeDisplayNameCommand is AsyncRelayCommand command)
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

    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (SetProperty(ref _isReady, value))
            {
                OnPropertyChanged(nameof(ReadyButtonText));
            }
        }
    }

    public string ReadyButtonText => IsReady ? "Unready" : "Ready";

    public int PlayerCount => Members.Count;

    public int ReadyPlayerCount => Members.Count(member => member.IsReady);

    public string PlayerCountText => $"{PlayerCount} player(s)";

    public string ReadyCountText => $"{ReadyPlayerCount} ready";

    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    public string PlayerIdText => _currentPlayerId is int currentPlayerId
        ? currentPlayerId.ToString()
        : "Pending";

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

    private void ApplySnapshot(ClientChatLobbySnapshot snapshot)
    {
        DisplayName = snapshot.CurrentPlayer?.DisplayName ?? DisplayName;
        _currentPlayerId = snapshot.CurrentPlayer?.PlayerId;
        OnPropertyChanged(nameof(PlayerIdText));
        ApplyPlayers(snapshot.Players);
        ApplyChatHistory(snapshot.ChatHistory);
        ApplyReadyState(snapshot.ReadyState);
        ApplyCountdown(snapshot.Countdown);
    }

    private void ApplyPlayers(IEnumerable<ChatLobbyPlayer> players)
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

    private void ApplyChatHistory(IEnumerable<ChatEntry> entries)
    {
        ChatMessages.Clear();
        foreach (ChatEntry entry in entries.OrderBy(entry => entry.TimestampUtc))
        {
            AddChatMessage(entry);
        }
    }

    private void ApplyReadyState(ChatLobbyReadyStateResponse readyState)
    {
        foreach (ChatLobbyReadyPlayer readyPlayer in readyState.Players)
        {
            ChatLobbyMemberViewModel? member = Members.FirstOrDefault(member => member.PlayerId == readyPlayer.PlayerId);
            if (member is not null)
            {
                member.IsReady = readyPlayer.IsReady;
            }
        }

        IsReady = _currentPlayerId is int currentPlayerId
            && readyState.Players.Any(player => player.PlayerId == currentPlayerId && player.IsReady);
        OnLobbyCountsChanged();
    }

    private void ApplyCountdown(ChatLobbyCountdownResponse? countdown)
    {
        _countdown = countdown;
        UpdateCountdownText();
    }

    private void UpdateCountdownText()
    {
        if (_countdown is null || _countdown.Status == ChatLobbyCountdownStatus.None)
        {
            CountdownText = "No countdown";
            return;
        }

        float remaining = _countdown.RemainingSeconds;
        if (_countdown.Status == ChatLobbyCountdownStatus.Started && _countdown.EndsAtUtc != default)
        {
            remaining = Math.Max(0f, (float)(_countdown.EndsAtUtc - DateTime.UtcNow).TotalSeconds);
        }

        CountdownText = $"{_countdown.Status}: {remaining:0}s";
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

    private async Task ChangeDisplayNameAsync(CancellationToken token)
    {
        try
        {
            await _runtime.ChangeDisplayNameAsync(DisplayName.Trim(), token);
            StatusMessage = "Display name updated.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Name change failed: {exception.Message}";
            RuntimeErrorOccurred?.Invoke(this, StatusMessage);
        }
    }

    private async Task ToggleReadyAsync(CancellationToken token)
    {
        try
        {
            await _runtime.SetReadyAsync(!IsReady, token);
            IsReady = !IsReady;
            StatusMessage = IsReady ? "Ready sent." : "Unready sent.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ready update failed: {exception.Message}";
            RuntimeErrorOccurred?.Invoke(this, StatusMessage);
        }
    }

    private Task DisconnectAsync(CancellationToken token)
    {
        DisconnectRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void OnLobbyCountsChanged()
    {
        OnPropertyChanged(nameof(PlayerCount));
        OnPropertyChanged(nameof(ReadyPlayerCount));
        OnPropertyChanged(nameof(PlayerCountText));
        OnPropertyChanged(nameof(ReadyCountText));
    }

    private void OnSnapshotChanged(object? sender, ClientChatLobbySnapshot snapshot)
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

    private void OnCountdownChanged(object? sender, ChatLobbyCountdownResponse countdown)
    {
        ApplyCountdown(countdown);
    }

    private void OnCountdownTimerTick(object? sender, EventArgs e)
    {
        UpdateCountdownText();
    }

    public void Dispose()
    {
        _runtime.SnapshotChanged -= OnSnapshotChanged;
        _runtime.ChatMessageReceived -= OnChatMessageReceived;
        _runtime.ReadyStateChanged -= OnReadyStateChanged;
        _runtime.CountdownChanged -= OnCountdownChanged;
        _countdownTimer.Stop();
        _runtime.Dispose();
    }
}
