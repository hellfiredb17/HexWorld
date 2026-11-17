using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HawkNet.CoreUx.Server.Views;

public partial class ServerMatchView : UserControl
{
    public ServerMatchView()
    {
        InitializeComponent();
    }

    private void ZoomInClicked(object? sender, RoutedEventArgs e)
    {
        WorldMap.ZoomIn();
    }

    private void ZoomOutClicked(object? sender, RoutedEventArgs e)
    {
        WorldMap.ZoomOut();
    }

    private void ResetMapClicked(object? sender, RoutedEventArgs e)
    {
        WorldMap.ResetView();
    }
}
