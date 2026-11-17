namespace HawkNet.CoreUx.Client.Services;

public interface IClientStateManager
{
    event EventHandler<ClientStateChangedEventArgs>? StateChanged;

    event EventHandler<ClientStateEntryFailedEventArgs>? StateEntryFailed;

    event EventHandler<ClientDisconnectedEventArgs>? Disconnected;

    string? CurrentStateId { get; }
}
