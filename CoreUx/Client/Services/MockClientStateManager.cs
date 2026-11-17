namespace HawkNet.CoreUx.Client.Services;

public sealed class MockClientStateManager : IClientStateManager
{
    public event EventHandler<ClientStateChangedEventArgs>? StateChanged;

    public event EventHandler<ClientStateEntryFailedEventArgs>? StateEntryFailed;

    public event EventHandler<ClientDisconnectedEventArgs>? Disconnected;

    public string? CurrentStateId { get; private set; }

    public void ChangeState(string stateId)
    {
        CurrentStateId = stateId;
        StateChanged?.Invoke(this, new ClientStateChangedEventArgs(stateId));
    }

    public void Disconnect(string message = "Disconnected.", bool isExpected = false)
    {
        Disconnected?.Invoke(this, new ClientDisconnectedEventArgs(message, isExpected));
    }

    public void FailStateEntry(string stateId, string message)
    {
        StateEntryFailed?.Invoke(this, new ClientStateEntryFailedEventArgs(stateId, message));
    }
}
