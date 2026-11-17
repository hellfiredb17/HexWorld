using HawkNet.CoreUx.Common.Services;

namespace HawkNet.CoreUx.Common.ViewModels;

public sealed class LoadingScreenViewModel
{
    public LoadingScreenViewModel(LoadingTransitionContext context)
        : this(context.HeaderText, context.ContentText, context.ImageKey)
    {
    }

    public LoadingScreenViewModel(string headerText, string contentText, string? imageKey = null)
    {
        HeaderText = headerText;
        ContentText = contentText;
        ImageKey = imageKey;
    }

    public string HeaderText { get; }

    public string ContentText { get; }

    public string? ImageKey { get; }

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageKey);
}
