using Avalonia.Controls;
using Avalonia.Input;
using HawkNet.CoreUx.Client.ViewModels;

namespace HawkNet.CoreUx.Client.Views;

public partial class ConnectionSetupView : UserControl
{
    public ConnectionSetupView()
    {
        InitializeComponent();
    }

    private void OnFoundServersSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ConnectionSetupViewModel viewModel
            && sender is ListBox listBox
            && listBox.SelectedItem is FoundServerViewModel selectedServer)
        {
            viewModel.SelectedServer = selectedServer;
        }
    }

    private void OnFoundServerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ConnectionSetupViewModel viewModel
            && sender is Control { DataContext: FoundServerViewModel selectedServer })
        {
            viewModel.SelectedServer = selectedServer;
        }
    }
}
