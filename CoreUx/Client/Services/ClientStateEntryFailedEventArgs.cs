namespace HawkNet.CoreUx.Client.Services;

public sealed class ClientStateEntryFailedEventArgs : EventArgs
{
    public ClientStateEntryFailedEventArgs(string stateId, string message)
    {
        StateId = stateId;
        Message = message;
    }

    public string StateId { get; }

    public string Message { get; }
}
