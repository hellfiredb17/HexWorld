using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.World;

namespace HawkNet.CoreUx.Client.Services;

public sealed class MockClientMatchRuntime : IClientMatchRuntime
{
    private ClientMatchSnapshot _snapshot = new()
    {
        Payload = new MatchPayload
        {
            VariantId = "base",
            PhaseId = "pre_match",
            PhaseStartedAtUtc = DateTime.UtcNow,
            PhaseEndsAtUtc = DateTime.UtcNow.AddSeconds(30)
        }
    };

    public event EventHandler<ClientMatchSnapshot>? SnapshotChanged;

    public event EventHandler<MatchPhaseChangeResponse>? PhaseChanged;

    public event EventHandler<ChatEntry>? ChatMessageReceived;

    public event EventHandler<WorldSnapshot>? WorldSnapshotChanged;

    public event EventHandler<WorldEntitySnapshot>? WorldEntityChanged;

    public List<string> SentMessages { get; } = new();

    public List<(string CommandId, string PayloadText)> SentCommands { get; } = new();

    public List<string> RequestedRegions { get; } = new();

    public List<WorldPoint> MoveRequests { get; } = new();

    public Task<ClientMatchSnapshot> LoadSnapshotAsync(CancellationToken token = default)
    {
        return Task.FromResult(_snapshot);
    }

    public Task RequestWorldStateAsync(string regionId, CancellationToken token = default)
    {
        RequestedRegions.Add(regionId);
        if (_snapshot.World is not null)
        {
            WorldSnapshotChanged?.Invoke(this, _snapshot.World);
        }

        return Task.CompletedTask;
    }

    public Task SendMoveCommandAsync(WorldPoint destination, CancellationToken token = default)
    {
        MoveRequests.Add(destination);
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(string message, CancellationToken token = default)
    {
        SentMessages.Add(message);
        ChatMessageReceived?.Invoke(this, new ChatEntry(1, "Player 1", DateTime.UtcNow, message));
        return Task.CompletedTask;
    }

    public Task SendCommandAsync(string commandId, string payloadText, CancellationToken token = default)
    {
        SentCommands.Add((commandId, payloadText));
        return Task.CompletedTask;
    }

    public void ApplySnapshot(ClientMatchSnapshot snapshot)
    {
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
        if (snapshot.World is not null)
        {
            WorldSnapshotChanged?.Invoke(this, snapshot.World);
        }
    }

    public void ApplyPhase(MatchPhaseChangeResponse phase)
    {
        PhaseChanged?.Invoke(this, phase);
    }

    public void ApplyWorld(WorldSnapshot snapshot)
    {
        _snapshot = _snapshot with { World = snapshot };
        WorldSnapshotChanged?.Invoke(this, snapshot);
    }

    public void ApplyEntity(WorldEntitySnapshot snapshot)
    {
        WorldEntityChanged?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
    }
}
