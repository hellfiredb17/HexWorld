namespace HawkNet.CoreUx.Client.Services;

public sealed class ClientDisconnectedEventArgs : EventArgs
{
    public ClientDisconnectedEventArgs(string message, bool isExpected)
    {
        Message = message;
        IsExpected = isExpected;
    }

    public string Message { get; }

    public bool IsExpected { get; }
}
