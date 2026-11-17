using Avalonia.Threading;
using HawkNet.Core;
using HawkNet.Core.Client;
using HawkNet.CoreUx.Client.Services;

namespace HawkNet.Desktop.Services;

public sealed class DesktopClientLauncher : IClientLauncher, IClientStateManager
{
    private static readonly TimeSpan StateEntryTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StateEntryPollInterval = TimeSpan.FromMilliseconds(25);
    private readonly string _configPath;
    private readonly ILogger _logger;
    private readonly List<IDisposable> _subscriptions = new();
    private HawkNetClient? _activeClient;
    private int _stateApplyVersion;
    private bool _isDisconnecting;

    public DesktopClientLauncher(string? configPath = null, ILogger? logger = null)
    {
        _configPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "Config", "game-client.json");
        _logger = logger ?? NullLogger.Instance;
    }

    public event EventHandler<ClientStateChangedEventArgs>? StateChanged;

    public event EventHandler<ClientStateEntryFailedEventArgs>? StateEntryFailed;

    public event EventHandler<ClientDisconnectedEventArgs>? Disconnected;

    public HawkNetClient? ActiveClient => _activeClient;

    public string? CurrentStateId { get; private set; }

    public async Task ConnectAsync(GameClientConfig config, CancellationToken token = default)
    {
        if (_activeClient is not null)
        {
            if (_activeClient.GameClient.IsConnected)
            {
                throw new InvalidOperationException("A HawkNet client is already connected.");
            }

            if (config.Networking.ReconnectEnabled && _activeClient.GameClient.PlayerId is not null && _activeClient.GameClient.HasReconnectToken)
            {
                await ReconnectActiveClientAsync(config, token);
                return;
            }

            await DisposeActiveClientAsync(token);
        }

        var client = HawkNetClient.Create(config);
        Subscribe(client);
        _activeClient = client;
        try
        {
            _isDisconnecting = false;
            _logger.Log($"Connecting to {config.Server.Host}:{config.Server.Port}.", nameof(DesktopClientLauncher));
            await client.ConnectAsync(token);
            if (!string.IsNullOrWhiteSpace(client.GameClient.CurrentGameStateId))
            {
                await WaitForClientStateAsync(client, client.GameClient.CurrentGameStateId, token);
            }

            _logger.Log(
                $"Connected as player {client.GameClient.PlayerId?.ToString() ?? "unknown"} in state '{client.GameClient.CurrentGameStateId}'.",
                nameof(DesktopClientLauncher));
            await ClientConfigLoader.SaveAsync(config, _configPath, token);
        }
        catch (Exception exception)
        {
            _logger.Exception(exception, "Client connect failed.", nameof(DesktopClientLauncher));
            _activeClient = null;
            ClearSubscriptions();
            await client.DisposeAsync();
            CurrentStateId = null;
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken token = default)
    {
        if (_activeClient is null)
        {
            CurrentStateId = null;
            return;
        }

        HawkNetClient client = _activeClient;
        _isDisconnecting = true;
        try
        {
            _logger.Log("Disconnect requested by desktop client.", nameof(DesktopClientLauncher));
            Interlocked.Increment(ref _stateApplyVersion);
            CurrentStateId = null;
            await client.DisconnectAsync(token);
        }
        finally
        {
            _isDisconnecting = false;
        }
    }

    private async Task ReconnectActiveClientAsync(GameClientConfig config, CancellationToken token)
    {
        HawkNetClient client = _activeClient ?? throw new InvalidOperationException("No HawkNet client is available to reconnect.");
        try
        {
            _isDisconnecting = false;
            _logger.Log(
                $"Reconnecting player {client.GameClient.PlayerId?.ToString() ?? "unknown"} to {config.Server.Host}:{config.Server.Port}.",
                nameof(DesktopClientLauncher));
            await client.ReconnectAsync(token);
            if (!string.IsNullOrWhiteSpace(client.GameClient.CurrentGameStateId))
            {
                await WaitForClientStateAsync(client, client.GameClient.CurrentGameStateId, token);
            }

            _logger.Log(
                $"Reconnected as player {client.GameClient.PlayerId?.ToString() ?? "unknown"} in state '{client.GameClient.CurrentGameStateId}'.",
                nameof(DesktopClientLauncher));
            await ClientConfigLoader.SaveAsync(config, _configPath, token);
        }
        catch (Exception exception)
        {
            _logger.Exception(exception, "Client reconnect failed.", nameof(DesktopClientLauncher));
            await DisposeActiveClientAsync(token);
            CurrentStateId = null;
            throw;
        }
    }

    private void Subscribe(HawkNetClient client)
    {
        _subscriptions.Add(client.Events.Subscribe<ClientConnectedEvent>((gameEvent, _) =>
        {
            _logger.Log(
                $"ClientConnectedEvent player={gameEvent.PlayerId} state='{gameEvent.GameStateId}' payloadBytes={gameEvent.GameStatePayload.Length}.",
                nameof(DesktopClientLauncher));
            QueueStateApply(client, gameEvent.GameStateId);
            return Task.CompletedTask;
        }));
        _subscriptions.Add(client.Events.Subscribe<ClientReconnectedEvent>((gameEvent, _) =>
        {
            _logger.Log(
                $"ClientReconnectedEvent player={gameEvent.PlayerId} state='{gameEvent.GameStateId}' payloadBytes={gameEvent.GameStatePayload.Length}.",
                nameof(DesktopClientLauncher));
            QueueStateApply(client, gameEvent.GameStateId);
            return Task.CompletedTask;
        }));
        _subscriptions.Add(client.Events.Subscribe<ClientGameStateChangeReceivedEvent>((gameEvent, _) =>
        {
            _logger.Log(
                $"GameStateChangeReceived state='{gameEvent.GameStateId}' payloadBytes={gameEvent.GameStatePayload.Length}.",
                nameof(DesktopClientLauncher));
            QueueStateApply(client, gameEvent.GameStateId);
            return Task.CompletedTask;
        }));
        _subscriptions.Add(client.Events.Subscribe<ClientDisconnectedEvent>((_, _) =>
        {
            _logger.Warning("ClientDisconnectedEvent received.", nameof(DesktopClientLauncher));
            if (_isDisconnecting)
            {
                return Task.CompletedTask;
            }

            DispatchDisconnect("Disconnected.");
            return Task.CompletedTask;
        }));
        _subscriptions.Add(client.Events.Subscribe<ClientFaultedEvent>((gameEvent, _) =>
        {
            LogClientFailure("ClientFaultedEvent", gameEvent.Message, gameEvent.Exception);
            DispatchDisconnect(gameEvent.Message);
            return Task.CompletedTask;
        }));
        _subscriptions.Add(client.Events.Subscribe<ClientHandshakeFailedEvent>((gameEvent, _) =>
        {
            LogClientFailure("ClientHandshakeFailedEvent", gameEvent.Message, gameEvent.Exception);
            DispatchDisconnect(gameEvent.Message);
            return Task.CompletedTask;
        }));
        _subscriptions.Add(client.Events.Subscribe<ClientCompatibilityFailedEvent>((gameEvent, _) =>
        {
            _logger.Error(
                $"ClientCompatibilityFailedEvent reason={gameEvent.Reason} module='{gameEvent.ModuleId}' dependency='{gameEvent.DependencyId}' message='{gameEvent.Message}'.",
                nameof(DesktopClientLauncher));
            DispatchDisconnect(gameEvent.Message);
            return Task.CompletedTask;
        }));
        _subscriptions.Add(client.Events.Subscribe<ClientStateEntryFailedEvent>((gameEvent, _) =>
        {
            LogClientFailure("ClientStateEntryFailedEvent", gameEvent.Message, gameEvent.Exception);
            Dispatch(() => StateEntryFailed?.Invoke(
                this,
                new ClientStateEntryFailedEventArgs(gameEvent.GameStateId, gameEvent.Message)));
            return Task.CompletedTask;
        }));
    }

    private void QueueStateApply(HawkNetClient client, string stateId)
    {
        CurrentStateId = stateId;
        int version = Interlocked.Increment(ref _stateApplyVersion);
        _logger.Log($"Queueing UI state apply for '{stateId}' version={version}.", nameof(DesktopClientLauncher));
        _ = Task.Run(async () =>
        {
            await WaitForClientStateAsync(client, stateId, CancellationToken.None).ConfigureAwait(false);
            if (version != Volatile.Read(ref _stateApplyVersion)
                || !ReferenceEquals(client, _activeClient)
                || _isDisconnecting)
            {
                _logger.DebugLog(
                    $"Skipped UI state apply for '{stateId}' version={version}; currentVersion={Volatile.Read(ref _stateApplyVersion)} disconnecting={_isDisconnecting}.",
                    nameof(DesktopClientLauncher));
                return;
            }

            _logger.Log($"Applying UI state '{stateId}'.", nameof(DesktopClientLauncher));
            Dispatch(() => StateChanged?.Invoke(this, new ClientStateChangedEventArgs(stateId)));
        });
    }

    private void DispatchDisconnect(string message)
    {
        CurrentStateId = null;
        _logger.Warning(
            $"Dispatching desktop disconnect. expected={_isDisconnecting} message='{message}'.",
            nameof(DesktopClientLauncher));
        Dispatch(() => Disconnected?.Invoke(this, new ClientDisconnectedEventArgs(message, _isDisconnecting)));
    }

    private void LogClientFailure(string eventName, string message, Exception? exception)
    {
        if (exception is null)
        {
            _logger.Error($"{eventName}: {message}", nameof(DesktopClientLauncher));
            return;
        }

        _logger.Exception(exception, $"{eventName}: {message}", nameof(DesktopClientLauncher));
    }

    private void ClearSubscriptions()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private async Task DisposeActiveClientAsync(CancellationToken token)
    {
        HawkNetClient? client = _activeClient;
        if (client is null)
        {
            return;
        }

        _activeClient = null;
        ClearSubscriptions();
        CurrentStateId = null;
        await client.ShutdownAsync(token);
        await client.DisposeAsync();
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

    private static async Task WaitForClientStateAsync(
        HawkNetClient client,
        string stateId,
        CancellationToken token)
    {
        DateTime deadlineUtc = DateTime.UtcNow + StateEntryTimeout;
        while (!token.IsCancellationRequested && DateTime.UtcNow < deadlineUtc)
        {
            if (string.Equals(client.GameStateManager.ActiveStateId, stateId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(StateEntryPollInterval, token).ConfigureAwait(false);
        }
    }
}
