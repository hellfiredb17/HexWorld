using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.World;

namespace HawkNet.CoreUx.Server.Services;

public sealed class MockServerMatchRuntime : IServerMatchRuntime
{
    private ServerMatchSnapshot _snapshot;

    public MockServerMatchRuntime()
        : this(new ServerMatchSnapshot
        {
            Payload = new MatchPayload
            {
                VariantId = "base",
                PhaseId = "match",
                PhaseSeconds = 300,
                PhaseStartedAtUtc = DateTime.UtcNow,
                PhaseEndsAtUtc = DateTime.UtcNow.AddMinutes(5),
                Players =
                [
                    new MatchPlayer { PlayerId = 2, DisplayName = "Bravo", IsConnected = true },
                    new MatchPlayer { PlayerId = 1, DisplayName = "Alpha", IsConnected = true }
                ]
            },
            ChatHistory =
            [
                new ChatEntry(1, "Alpha", DateTime.UtcNow, "Match started.")
            ]
        })
    {
    }

    public MockServerMatchRuntime(ServerMatchSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public event EventHandler<ServerMatchSnapshot>? SnapshotChanged;

    public event EventHandler<MatchPhaseChangeResponse>? PhaseChanged;

    public event EventHandler<ChatEntry>? ChatMessageReceived;

    public event EventHandler<TimeSpan>? GameTimeChanged;

    public event EventHandler<TimeSpan>? PhaseTimeChanged;

    public event EventHandler<WorldSnapshot>? WorldSnapshotChanged;

    public int StopMatchRequestCount { get; private set; }

    public List<string> SentMessages { get; } = new();

    public Func<string, CancellationToken, Task>? SendMessageOverride { get; set; }

    public Func<CancellationToken, Task>? StopMatchOverride { get; set; }

    public Task<ServerMatchSnapshot> LoadSnapshotAsync(CancellationToken token = default)
    {
        return Task.FromResult(_snapshot);
    }

    public async Task SendMessageAsync(string message, CancellationToken token = default)
    {
        SentMessages.Add(message);
        if (SendMessageOverride is not null)
        {
            await SendMessageOverride(message, token);
            return;
        }

        ChatMessageReceived?.Invoke(this, new ChatEntry(0, "Admin", DateTime.UtcNow, message));
    }

    public async Task StopMatchAsync(CancellationToken token = default)
    {
        StopMatchRequestCount++;
        if (StopMatchOverride is not null)
        {
            await StopMatchOverride(token);
        }
    }

    public void ApplySnapshot(ServerMatchSnapshot snapshot)
    {
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
        if (snapshot.World is not null)
        {
            WorldSnapshotChanged?.Invoke(this, snapshot.World);
        }
    }

    public void ApplyPhaseChange(MatchPhaseChangeResponse phaseChange)
    {
        PhaseChanged?.Invoke(this, phaseChange);
    }

    public void AddChatMessage(ChatEntry entry)
    {
        ChatMessageReceived?.Invoke(this, entry);
    }

    public void ApplyGameTime(TimeSpan gameTime)
    {
        GameTimeChanged?.Invoke(this, gameTime);
    }

    public void ApplyPhaseTime(TimeSpan phaseTime)
    {
        PhaseTimeChanged?.Invoke(this, phaseTime);
    }

    public void Dispose()
    {
    }
}
