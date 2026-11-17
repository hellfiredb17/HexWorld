using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.World;

namespace HawkNet.CoreUx.Client.Services;

public interface IClientMatchRuntime : IDisposable
{
    event EventHandler<ClientMatchSnapshot>? SnapshotChanged;

    event EventHandler<MatchPhaseChangeResponse>? PhaseChanged;

    event EventHandler<ChatEntry>? ChatMessageReceived;

    event EventHandler<WorldSnapshot>? WorldSnapshotChanged;

    event EventHandler<WorldEntitySnapshot>? WorldEntityChanged;

    Task<ClientMatchSnapshot> LoadSnapshotAsync(CancellationToken token = default);

    Task RequestWorldStateAsync(string regionId, CancellationToken token = default);

    Task SendMoveCommandAsync(WorldPoint destination, CancellationToken token = default);

    Task SendMessageAsync(string message, CancellationToken token = default);

    Task SendCommandAsync(string commandId, string payloadText, CancellationToken token = default);
}
