using System.Windows.Input;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.Services;

namespace HawkNet.CoreUx.Common.ViewModels;

public sealed class MainMenuViewModel
{
    public MainMenuViewModel(IEnumerable<ICoreUxTool>? tools = null)
    {
        CreateServerCommand = new AsyncRelayCommand(_ =>
        {
            CreateServerRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        FindLobbyCommand = new AsyncRelayCommand(_ =>
        {
            FindLobbyRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        Tools = (tools ?? Array.Empty<ICoreUxTool>())
            .OrderBy(tool => tool.Order)
            .ThenBy(tool => tool.Id, StringComparer.Ordinal)
            .Select(tool => new MainMenuToolViewModel(tool, requested => ToolRequested?.Invoke(requested)))
            .ToArray();
    }

    public string Title => "HawkNet";

    public ICommand CreateServerCommand { get; }

    public ICommand FindLobbyCommand { get; }

    public IReadOnlyList<MainMenuToolViewModel> Tools { get; }

    public event EventHandler? CreateServerRequested;

    public event EventHandler? FindLobbyRequested;

    public event Action<ICoreUxTool>? ToolRequested;
}
