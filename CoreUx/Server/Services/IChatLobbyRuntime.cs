using HawkNet.Core.Shared;

namespace HawkNet.CoreUx.Server.Services;

public interface IChatLobbyRuntime : IDisposable
{
    event EventHandler<ChatLobbySnapshot>? SnapshotChanged;

    event EventHandler<ChatEntry>? ChatMessageReceived;

    event EventHandler<ChatLobbyReadyStateResponse>? ReadyStateChanged;

    event EventHandler<ChatLobbyCountdownResponse?>? CountdownChanged;

    event EventHandler? MatchStarted;

    Task<ChatLobbySnapshot> LoadSnapshotAsync(CancellationToken token = default);

    Task SendMessageAsync(string message, CancellationToken token = default);

    Task StartMatchAsync(CancellationToken token = default);
}
