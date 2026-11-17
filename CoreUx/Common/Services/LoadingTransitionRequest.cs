namespace HawkNet.CoreUx.Common.Services;

public sealed class LoadingTransitionRequest
{
    public LoadingTransitionRequest(
        string transitionKey,
        Func<CancellationToken, Task> waitForReadyAsync)
    {
        TransitionKey = transitionKey;
        WaitForReadyAsync = waitForReadyAsync;
    }

    public string TransitionKey { get; }

    public Func<CancellationToken, Task> WaitForReadyAsync { get; }
}
