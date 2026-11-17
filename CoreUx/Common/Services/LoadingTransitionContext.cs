namespace HawkNet.CoreUx.Common.Services;

public sealed class LoadingTransitionContext
{
    public LoadingTransitionContext(
        string headerText,
        string contentText,
        string nextViewKey,
        Func<CancellationToken, Task> waitForReadyAsync,
        string? imageKey = null)
    {
        HeaderText = headerText;
        ContentText = contentText;
        NextViewKey = nextViewKey;
        WaitForReadyAsync = waitForReadyAsync;
        ImageKey = imageKey;
    }

    public string HeaderText { get; }

    public string ContentText { get; }

    public string NextViewKey { get; }

    public Func<CancellationToken, Task> WaitForReadyAsync { get; }

    public string? ImageKey { get; }
}
