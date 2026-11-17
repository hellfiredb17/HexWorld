using System.Windows.Input;

namespace HawkNet.CoreUx.Common.ViewModels;

public sealed class ErrorPopupViewModel
{
    public ErrorPopupViewModel(
        string title,
        string message,
        string? details,
        ICommand dismissCommand)
    {
        Title = title;
        Message = message;
        Details = details;
        DismissCommand = dismissCommand;
    }

    public string Title { get; }

    public string Message { get; }

    public string? Details { get; }

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public ICommand DismissCommand { get; }
}
