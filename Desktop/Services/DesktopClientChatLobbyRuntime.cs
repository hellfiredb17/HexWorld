using Avalonia.Threading;
using HawkNet.Core.Client;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.Services;

namespace HawkNet.Desktop.Services;

public sealed class DesktopClientChatLobbyRuntime : IClientChatLobbyRuntime
{
    private readonly HawkNetClient? _client;
    private readonly List<IDisposable> _subscriptions = new();
    private bool _disposed;

    public DesktopClientChatLobbyRuntime(HawkNetClient? client)
    {
        _client = client;
        Subscribe();
    }

    public event EventHandler<ClientChatLobbySnapshot>? SnapshotChanged;

    public event EventHandler<ChatEntry>? ChatMessageReceived;

    public event EventHandler<ChatLobbyReadyStateResponse>? ReadyStateChanged;

    public event EventHandler<ChatLobbyCountdownResponse>? CountdownChanged;

    public Task<ClientChatLobbySnapshot> LoadSnapshotAsync(CancellationToken token = default)
    {
        return Task.FromResult(CreateSnapshot());
    }

    public Task SendMessageAsync(string message, CancellationToken token = default)
    {
        return GetLobby().SendChatMessageAsync(message, token);
    }

    public Task ChangeDisplayNameAsync(string displayName, CancellationToken token = default)
    {
        return GetLobby().ChangeDisplayNameAsync(displayName, token);
    }

    public Task SetReadyAsync(bool isReady, CancellationToken token = default)
    {
        return GetLobby().SetReadyAsync(isReady, Array.Empty<byte>(), token);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void Subscribe()
    {
        if (_client is null)
        {
            return;
        }

        _subscriptions.Add(_client.Events.Subscribe<ClientChatMessageReceivedEvent>((gameEvent, _) =>
        {
            Dispatch(() => ChatMessageReceived?.Invoke(this, gameEvent.Entry));
            return Task.CompletedTask;
        }));
        _subscriptions.Add(_client.Events.Subscribe<ClientChatHistoryReceivedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_client.Events.Subscribe<ClientChatLobbyPlayerStateReceivedEvent>((_, _) => RefreshSnapshotAsync()));
        _subscriptions.Add(_client.Events.Subscribe<ClientChatLobbyReadyStateReceivedEvent>((gameEvent, _) =>
        {
            Dispatch(() =>
            {
                ReadyStateChanged?.Invoke(this, gameEvent.ReadyState);
                SnapshotChanged?.Invoke(this, CreateSnapshot());
            });
            return Task.CompletedTask;
        }));
        _subscriptions.Add(_client.Events.Subscribe<ClientChatLobbyCountdownReceivedEvent>((gameEvent, _) =>
        {
            Dispatch(() =>
            {
                CountdownChanged?.Invoke(this, gameEvent.Countdown);
                SnapshotChanged?.Invoke(this, CreateSnapshot());
            });
            return Task.CompletedTask;
        }));
    }

    private Task RefreshSnapshotAsync()
    {
        if (_disposed || _client?.GameStateManager.ActiveState is not ClientChatLobbyState)
        {
            return Task.CompletedTask;
        }

        Dispatch(() => SnapshotChanged?.Invoke(this, CreateSnapshot()));
        return Task.CompletedTask;
    }

    private ClientChatLobbySnapshot CreateSnapshot()
    {
        ClientChatLobbyState lobby = GetLobby();
        return new ClientChatLobbySnapshot
        {
            Players = lobby.Players,
            ChatHistory = lobby.ChatHistory,
            ReadyState = new ChatLobbyReadyStateResponse
            {
                Players = lobby.ReadyPlayers.ToArray(),
                ReadyPlayerCount = lobby.ReadyPlayerCount,
                RequiredReadyPlayerCount = lobby.RequiredReadyPlayerCount,
                ReadyPercentage = lobby.ReadyPercentage
            },
            Countdown = new ChatLobbyCountdownResponse
            {
                Status = lobby.CountdownStatus,
                StartedAtUtc = lobby.CountdownStartedAtUtc,
                EndsAtUtc = lobby.CountdownEndsAtUtc,
                RemainingSeconds = lobby.CountdownRemainingSeconds
            },
            CurrentPlayer = lobby.CurrentPlayer
        };
    }

    private ClientChatLobbyState GetLobby()
    {
        return _client?.GameStateManager.ActiveState as ClientChatLobbyState
            ?? throw new InvalidOperationException("The active client state is not the chat lobby.");
    }

    private static void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
