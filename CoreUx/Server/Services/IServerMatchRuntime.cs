using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.World;

namespace HawkNet.CoreUx.Server.Services;

public interface IServerMatchRuntime : IDisposable
{
    event EventHandler<ServerMatchSnapshot>? SnapshotChanged;

    event EventHandler<MatchPhaseChangeResponse>? PhaseChanged;

    event EventHandler<ChatEntry>? ChatMessageReceived;

    event EventHandler<TimeSpan>? GameTimeChanged;

    event EventHandler<TimeSpan>? PhaseTimeChanged;

    event EventHandler<WorldSnapshot>? WorldSnapshotChanged;

    Task<ServerMatchSnapshot> LoadSnapshotAsync(CancellationToken token = default);

    Task SendMessageAsync(string message, CancellationToken token = default);

    Task StopMatchAsync(CancellationToken token = default);

	Task SelectRegionAsync(string regionId, CancellationToken token = default) => Task.CompletedTask;
}
