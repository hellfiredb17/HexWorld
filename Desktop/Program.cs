using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ClanIronSim.CoreUx.Client;
using ClanIronSim.CoreUx.Server;
using ClanIronSim.CoreUx.WorldBuilder;
using ClanIronSim.HawkNet;
using HawkNet.Core;
using HawkNet.CoreUx.Client.World;
using HawkNet.CoreUx;
using HawkNet.CoreUx.Client.Services;
using HawkNet.CoreUx.Client.ViewModels;
using HawkNet.CoreUx.Client.Views;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Common.LayerSelection;
using HawkNet.CoreUx.Common.TileCreation;
using HawkNet.CoreUx.Common.TextureSelection;
using HawkNet.CoreUx.Server.Services;
using HawkNet.CoreUx.Server.ViewModels;
using HawkNet.CoreUx.Server.Views;
using HawkNet.Desktop.Services;
using System.Diagnostics;

namespace HawkNet.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string startupLogPath = Path.Combine(AppContext.BaseDirectory, "hawknet-startup.log");
        Trace.Listeners.Add(new TextWriterTraceListener(startupLogPath));
        Trace.AutoFlush = true;
        if (args.Any(arg => string.Equals(arg, "tile-creator", StringComparison.OrdinalIgnoreCase)))
        {
            App.CreateMainWindow = CreateTileCreatorWindow;
        }
        else if (args.Any(arg => string.Equals(arg, "layer-selector", StringComparison.OrdinalIgnoreCase)))
        {
            App.CreateMainWindow = CreateLayerSelectionWindow;
        }
        else if (args.Any(arg => string.Equals(arg, "texture-selector", StringComparison.OrdinalIgnoreCase)))
        {
            App.CreateMainWindow = CreateTextureSelectorWindow;
        }
        else if (args.Any(arg => string.Equals(arg, "section-editor", StringComparison.OrdinalIgnoreCase)))
        {
            App.CreateMainWindow = CreateSectionEditorMainWindow;
        }
        else if (args.Any(arg => string.Equals(arg, "tile-editor", StringComparison.OrdinalIgnoreCase)))
        {
            App.CreateMainWindow = CreateTileEditorMainWindow;
        }
        else if (args.Any(arg => string.Equals(arg, "client", StringComparison.OrdinalIgnoreCase)))
        {
            App.CreateMainWindow = CreateClientMainWindow;
        }
        else
        {
            App.CreateMainWindow = CreateServerMainWindow;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Trace.TraceError(exception.ToString());
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                // WinUIComposition can deadlock during native dropdown/window disposal.
                // Keep GPU composition without using that teardown path.
                CompositionMode = [Win32CompositionMode.DirectComposition, Win32CompositionMode.RedirectionSurface]
            })
            .WithInterFont()
            .LogToTrace();
    }

    private static MainViewModel CreateMainViewModel()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string flowConfigPath = Path.Combine(baseDirectory, "Config", "desktop-flow.json");
        string serverConfigPath = Path.Combine(baseDirectory, "Config", "game-server.json");

        var serverLauncher = new DesktopServerLauncher(configureServer: builder =>
        {
            builder.AddModule(new ClanIronSimMatchModule());
            builder.AddService<IServerWorldMonitorProvider>(new ClanIronServerWorldMonitorProvider());
        });
        var tools = CreateDesktopTools();

        return new MainViewModel(
            ScreenFlowConfigLoader.LoadOrDefault(flowConfigPath),
            ServerConfigLoader.LoadOrDefault(serverConfigPath),
            serverLauncher,
            () => new DesktopChatLobbyRuntime(serverLauncher.ActiveHost),
            () => new DesktopServerMatchRuntime(serverLauncher.ActiveHost),
            tools);
    }

    private static MainView CreateServerMainWindow()
    {
        return CreateServerMainWindow(startAtMainMenu: true);
    }

    private static TextureSelectorWindow CreateTextureSelectorWindow()
    {
        string? assetDirectory = FindRepositoryDirectory("ClanIronSim.CoreUx", "Assets");
        string? initialTexture = assetDirectory is null ? null : Path.Combine(assetDirectory, "forest-floor-tile.png");
        return new TextureSelectorWindow(new TextureSelectorContext(new TextureSelectorDemoPreviewAdapter())
        {
            InitialDirectory = assetDirectory,
            InitialTexturePath = initialTexture,
            PreviewInteractionHint = "Drag to rotate  •  Scroll to zoom"
        });
    }

    private static LayerSelectionWindow CreateLayerSelectionWindow()
    {
        string? assetDirectory = FindRepositoryDirectory("ClanIronSim.CoreUx", "Assets");
        return new LayerSelectionWindow(new LayerSelectionContext(
            layerNumber: 0,
            layerPreviewAdapter: new TextureSelectorDemoPreviewAdapter(tileWidth: 4.2, tileHeight: 3.8),
            textureSelectorPreviewAdapterFactory: static () => new TextureSelectorDemoPreviewAdapter())
        {
            InitialTextureDirectory = assetDirectory,
            TexturePreviewInteractionHint = "Drag to rotate  •  Scroll to zoom"
        });
    }

    private static TileCreatorWindow CreateTileCreatorWindow()
    {
        return new TileCreatorWindow(CreateTileCreatorContext());
    }

    private static ICoreUxTool[] CreateDesktopTools() =>
        [new ClanIronWorldBuilderTool(), new DesktopTileCreatorTool(CreateTileCreatorContext)];

    private static TileCreatorContext CreateTileCreatorContext()
    {
        return new TileCreatorContext(static model => new TextureSelectorDemoPreviewAdapter(tileWidth: 5.8, tileHeight: 5.2, model: model))
        {
            InitialDirectory = Path.GetDirectoryName(FindRepositoryDirectory("CoreUx")) ?? AppContext.BaseDirectory,
            InitialTextureDirectory = FindRepositoryDirectory("ClanIronSim.CoreUx", "Assets"),
            TexturePreviewInteractionHint = "Drag to rotate  •  Scroll to zoom"
        };
    }

    private static string? FindRepositoryDirectory(params string[] relativeSegments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = relativeSegments.Aggregate(directory.FullName, Path.Combine);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static MainView CreateTileEditorMainWindow()
    {
        MainViewModel viewModel = CreateMainViewModel();
        viewModel.OpenTool(new ClanIronWorldBuilderTool(WorldBuilderStartPage.TileEditor));
        return new MainView { DataContext = viewModel };
    }

    private static MainView CreateSectionEditorMainWindow()
    {
        MainViewModel viewModel = CreateMainViewModel();
        viewModel.OpenTool(new ClanIronWorldBuilderTool(WorldBuilderStartPage.SectionEditor));
        return new MainView { DataContext = viewModel };
    }

    private static MainView CreateServerMainWindow(bool startAtMainMenu)
    {
        MainViewModel viewModel = CreateMainViewModel();
        if (!startAtMainMenu)
        {
            viewModel.MainMenu.CreateServerCommand.Execute(null);
        }

        viewModel.FindLobbyRequested += (_, _) => ReplaceMainWindow(CreateClientMainWindow(startAtMainMenu: false));
        return new MainView
        {
            DataContext = viewModel
        };
    }

    private static ClientMainView CreateClientMainWindow()
    {
        return CreateClientMainWindow(startAtMainMenu: false);
    }

    private static ClientMainView CreateClientMainWindow(bool startAtMainMenu)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string clientConfigPath = Path.Combine(baseDirectory, "Config", "game-client.json");
        string worldViewerConfigPath = Path.Combine(baseDirectory, "Config", "world-viewer.json");
        var logger = new NetLogger();
        var clientLauncher = new DesktopClientLauncher(clientConfigPath, logger);
        var viewModel = new ClientMainViewModel(
            ClientConfigLoader.LoadOrDefault(clientConfigPath),
            clientLauncher,
            clientLauncher,
            () => new DesktopClientChatLobbyRuntime(clientLauncher.ActiveClient),
            () => new ClanIronClientMatchRuntime(clientLauncher.ActiveClient),
            startAtMainMenu,
            new DesktopClientServerDiscovery(),
            logger,
            WorldViewerConfigLoader.LoadOrDefault(worldViewerConfigPath),
            CreateDesktopTools());
        viewModel.CreateServerRequested += (_, _) => ReplaceMainWindow(CreateServerMainWindow(startAtMainMenu: false));

        return new ClientMainView
        {
            DataContext = viewModel
        };
    }

    private static void ReplaceMainWindow(Avalonia.Controls.Window window)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Avalonia.Controls.Window? previousWindow = desktop.MainWindow;
            desktop.MainWindow = window;
            window.Show();
            previousWindow?.Close();
        }
    }
}

