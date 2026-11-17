using HawkNet.Core.Shared;

namespace HawkNet.CoreUx.Server.Services;

public sealed class MockChatLobbyRuntime : IChatLobbyRuntime
{
    private ChatLobbySnapshot _snapshot;

    public MockChatLobbyRuntime()
        : this(new ChatLobbySnapshot
        {
            Players =
            [
                new ChatLobbyPlayer { PlayerId = 2, DisplayName = "Bravo", IsReady = false },
                new ChatLobbyPlayer { PlayerId = 1, DisplayName = "Alpha", IsReady = true }
            ],
            ChatHistory =
            [
                new ChatEntry(1, "Alpha", DateTime.UtcNow, "Server lobby is ready.")
            ],
            ReadyState = new ChatLobbyReadyStateResponse
            {
                Players =
                [
                    new ChatLobbyReadyPlayer { PlayerId = 1, DisplayName = "Alpha", IsReady = true },
                    new ChatLobbyReadyPlayer { PlayerId = 2, DisplayName = "Bravo", IsReady = false }
                ],
                ReadyPlayerCount = 1,
                RequiredReadyPlayerCount = 2,
                ReadyPercentage = 50f
            }
        })
    {
    }

    public MockChatLobbyRuntime(ChatLobbySnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public event EventHandler<ChatLobbySnapshot>? SnapshotChanged;

    public event EventHandler<ChatEntry>? ChatMessageReceived;

    public event EventHandler<ChatLobbyReadyStateResponse>? ReadyStateChanged;

    public event EventHandler<ChatLobbyCountdownResponse?>? CountdownChanged;

    public event EventHandler? MatchStarted;

    public List<string> SentMessages { get; } = new();

    public int StartMatchRequestCount { get; private set; }

    public Func<string, CancellationToken, Task>? SendMessageOverride { get; set; }

    public Func<CancellationToken, Task>? StartMatchOverride { get; set; }

    public Task<ChatLobbySnapshot> LoadSnapshotAsync(CancellationToken token = default)
    {
        return Task.FromResult(_snapshot);
    }

    public async Task SendMessageAsync(string message, CancellationToken token = default)
    {
        if (SendMessageOverride is not null)
        {
            await SendMessageOverride(message, token);
            return;
        }

        SentMessages.Add(message);
        var entry = new ChatEntry(0, "Server", DateTime.UtcNow, message);
        ChatMessageReceived?.Invoke(this, entry);
    }

    public async Task StartMatchAsync(CancellationToken token = default)
    {
        StartMatchRequestCount++;
        if (StartMatchOverride is not null)
        {
            await StartMatchOverride(token);
        }
    }

    public void ApplySnapshot(ChatLobbySnapshot snapshot)
    {
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    public void ApplyReadyState(ChatLobbyReadyStateResponse readyState)
    {
        _snapshot = _snapshot with { ReadyState = readyState };
        ReadyStateChanged?.Invoke(this, readyState);
    }

    public void ApplyCountdown(ChatLobbyCountdownResponse? countdown)
    {
        _snapshot = _snapshot with { Countdown = countdown };
        CountdownChanged?.Invoke(this, countdown);
    }

    public void AddChatMessage(ChatEntry entry)
    {
        ChatMessageReceived?.Invoke(this, entry);
    }

    public void CompleteMatchStart()
    {
        MatchStarted?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }
}
