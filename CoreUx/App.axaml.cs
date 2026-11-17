using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HawkNet.CoreUx.Server.ViewModels;
using HawkNet.CoreUx.Server.Views;

namespace HawkNet.CoreUx;

public partial class App : Application
{
    public static Func<MainViewModel>? CreateMainViewModel { get; set; }

    public static Func<Window>? CreateMainWindow { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateMainWindow?.Invoke() ?? new MainView
            {
                DataContext = CreateMainViewModel?.Invoke() ?? new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
