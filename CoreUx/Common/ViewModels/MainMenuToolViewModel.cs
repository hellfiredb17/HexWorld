using System.Windows.Input;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.Services;

namespace HawkNet.CoreUx.Common.ViewModels;

public sealed class MainMenuToolViewModel
{
    internal MainMenuToolViewModel(ICoreUxTool tool, Action<ICoreUxTool> open)
    {
        Tool = tool;
        OpenCommand = new RelayCommand(() => open(tool));
    }

    internal ICoreUxTool Tool { get; }

    public string Id => Tool.Id;

    public string DisplayName => Tool.DisplayName;

    public int Order => Tool.Order;

    public ICommand OpenCommand { get; }
}
