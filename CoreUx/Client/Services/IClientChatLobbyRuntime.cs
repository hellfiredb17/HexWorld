using HawkNet.Core.Shared;

namespace HawkNet.CoreUx.Client.Services;

public interface IClientChatLobbyRuntime : IDisposable
{
    event EventHandler<ClientChatLobbySnapshot>? SnapshotChanged;

    event EventHandler<ChatEntry>? ChatMessageReceived;

    event EventHandler<ChatLobbyReadyStateResponse>? ReadyStateChanged;

    event EventHandler<ChatLobbyCountdownResponse>? CountdownChanged;

    Task<ClientChatLobbySnapshot> LoadSnapshotAsync(CancellationToken token = default);

    Task SendMessageAsync(string message, CancellationToken token = default);

    Task ChangeDisplayNameAsync(string displayName, CancellationToken token = default);

    Task SetReadyAsync(bool isReady, CancellationToken token = default);
}
