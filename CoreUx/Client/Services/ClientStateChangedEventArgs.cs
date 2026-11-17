namespace HawkNet.CoreUx.Client.Services;

public sealed class ClientStateChangedEventArgs : EventArgs
{
    public ClientStateChangedEventArgs(string stateId)
    {
        StateId = stateId;
    }

    public string StateId { get; }
}
