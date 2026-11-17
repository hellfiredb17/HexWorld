using HawkNet.Core.Shared;

namespace HawkNet.CoreUx.Client.Services;

public sealed class MockClientChatLobbyRuntime : IClientChatLobbyRuntime
{
    private ClientChatLobbySnapshot _snapshot = new()
    {
        Players =
        [
            new ChatLobbyPlayer { PlayerId = 1, DisplayName = "Player 1", IsReady = false }
        ]
    };

    public event EventHandler<ClientChatLobbySnapshot>? SnapshotChanged;

    public event EventHandler<ChatEntry>? ChatMessageReceived;

    public event EventHandler<ChatLobbyReadyStateResponse>? ReadyStateChanged;

    public event EventHandler<ChatLobbyCountdownResponse>? CountdownChanged;

    public List<string> SentMessages { get; } = new();

    public List<string> ChangedNames { get; } = new();

    public List<bool> ReadyRequests { get; } = new();

    public Task<ClientChatLobbySnapshot> LoadSnapshotAsync(CancellationToken token = default)
    {
        return Task.FromResult(_snapshot);
    }

    public Task SendMessageAsync(string message, CancellationToken token = default)
    {
        SentMessages.Add(message);
        var entry = new ChatEntry(1, "Player 1", DateTime.UtcNow, message);
        ChatMessageReceived?.Invoke(this, entry);
        return Task.CompletedTask;
    }

    public Task ChangeDisplayNameAsync(string displayName, CancellationToken token = default)
    {
        ChangedNames.Add(displayName);
        return Task.CompletedTask;
    }

    public Task SetReadyAsync(bool isReady, CancellationToken token = default)
    {
        ReadyRequests.Add(isReady);
        return Task.CompletedTask;
    }

    public void ApplySnapshot(ClientChatLobbySnapshot snapshot)
    {
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    public void ApplyReadyState(ChatLobbyReadyStateResponse readyState)
    {
        _snapshot = _snapshot with { ReadyState = readyState };
        ReadyStateChanged?.Invoke(this, readyState);
    }

    public void ApplyCountdown(ChatLobbyCountdownResponse countdown)
    {
        _snapshot = _snapshot with { Countdown = countdown };
        CountdownChanged?.Invoke(this, countdown);
    }

    public void Dispose()
    {
    }
}
