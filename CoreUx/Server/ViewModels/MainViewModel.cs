using HawkNet.Core.Server;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Common.ViewModels;
using HawkNet.CoreUx.Server.Services;

namespace HawkNet.CoreUx.Server.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ScreenFlowConfig _flowConfig;
    private readonly Func<IChatLobbyRuntime> _chatLobbyRuntimeFactory;
    private readonly Func<IServerMatchRuntime> _serverMatchRuntimeFactory;
    private readonly IServerLauncher _serverLauncher;
    private object _currentViewModel;
    private ErrorPopupViewModel? _errorPopup;

    public MainViewModel()
        : this(
            ScreenFlowConfigLoader.LoadOrDefault(),
            ServerConfigLoader.LoadOrDefault(),
            new MockServerLauncher())
    {
    }

    public MainViewModel(ScreenFlowConfig flowConfig)
        : this(flowConfig, ServerConfigLoader.LoadOrDefault(), new MockServerLauncher())
    {
    }

    public MainViewModel(
        ScreenFlowConfig flowConfig,
        GameHostConfig serverConfig,
        IServerLauncher serverLauncher)
        : this(
            flowConfig,
            serverConfig,
            serverLauncher,
            () => new MockChatLobbyRuntime(),
            () => new MockServerMatchRuntime())
    {
    }

    public MainViewModel(
        ScreenFlowConfig flowConfig,
        GameHostConfig serverConfig,
        IServerLauncher serverLauncher,
        Func<IChatLobbyRuntime> chatLobbyRuntimeFactory,
        Func<IServerMatchRuntime> serverMatchRuntimeFactory,
        IEnumerable<ICoreUxTool>? tools = null)
    {
        _flowConfig = flowConfig;
        _serverLauncher = serverLauncher;
        _chatLobbyRuntimeFactory = chatLobbyRuntimeFactory;
        _serverMatchRuntimeFactory = serverMatchRuntimeFactory;
        DismissErrorCommand = new AsyncRelayCommand(_ =>
        {
            ErrorPopup = null;
            return Task.CompletedTask;
        });
        MainMenu = new MainMenuViewModel(tools);
        MainMenu.CreateServerRequested += OnCreateServerRequested;
        MainMenu.FindLobbyRequested += OnFindLobbyRequested;
        MainMenu.ToolRequested += OpenTool;
        ServerSettings = new ServerSettingsViewModel(serverConfig, serverLauncher);
        ServerSettings.BackRequested += OnServerSettingsBackRequested;
        ServerSettings.LoadingTransitionRequested += OnLoadingTransitionRequested;
        ServerSettings.ServerStartFailed += OnServerStartFailed;
        _currentViewModel = CreateViewModel(_flowConfig.InitialView);
    }

    public string Title => "HawkNet";

    public ServerSettingsViewModel ServerSettings { get; }

    public MainMenuViewModel MainMenu { get; }

    public AsyncRelayCommand DismissErrorCommand { get; }

    public event EventHandler? FindLobbyRequested;

    public void OpenTool(ICoreUxTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        CurrentViewModel = tool.CreateView(() => CurrentViewModel = MainMenu);
    }

    public object CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public ErrorPopupViewModel? ErrorPopup
    {
        get => _errorPopup;
        private set
        {
            if (SetProperty(ref _errorPopup, value))
            {
                OnPropertyChanged(nameof(HasErrorPopup));
            }
        }
    }

    public bool HasErrorPopup => ErrorPopup is not null;

    private async Task RunLoadingTransitionAsync(LoadingTransitionRequest request)
    {
        if (!_flowConfig.Transitions.TryGetValue(request.TransitionKey, out ScreenTransitionConfig? transition))
        {
            return;
        }

        var context = new LoadingTransitionContext(
            transition.HeaderText,
            transition.ContentText,
            transition.To,
            request.WaitForReadyAsync,
            transition.ImageKey);
        DateTime startedAtUtc = DateTime.UtcNow;
        CurrentViewModel = new LoadingScreenViewModel(context);
        try
        {
            await context.WaitForReadyAsync(CancellationToken.None);
            await WaitForMinimumLoadingTimeAsync(startedAtUtc);
            CurrentViewModel = CreateViewModel(context.NextViewKey);
        }
        catch (Exception exception)
        {
            CurrentViewModel = CreateViewModel(transition.From);
            if (!HasErrorPopup)
            {
                ShowError("Transition Failed", exception.Message);
            }
        }
    }

    private async Task TransitionToAsync(string transitionKey)
    {
        if (!_flowConfig.Transitions.TryGetValue(transitionKey, out ScreenTransitionConfig? transition))
        {
            return;
        }

        await RunLoadingTransitionAsync(new LoadingTransitionRequest(
            transitionKey,
            _ => Task.CompletedTask
            ));
    }

    private async void OnLoadingTransitionRequested(object? sender, LoadingTransitionRequest request)
    {
        await RunLoadingTransitionAsync(request);
    }

    private void OnCreateServerRequested(object? sender, EventArgs eventArgs)
    {
        CurrentViewModel = ServerSettings;
    }

    private void OnServerSettingsBackRequested(object? sender, EventArgs eventArgs)
    {
        CurrentViewModel = MainMenu;
    }

    private void OnFindLobbyRequested(object? sender, EventArgs eventArgs)
    {
        FindLobbyRequested?.Invoke(this, EventArgs.Empty);
    }

    private ChatLobbyViewModel CreateChatLobbyViewModel()
    {
        var chatLobby = new ChatLobbyViewModel(_chatLobbyRuntimeFactory());
        chatLobby.MatchStartRequested += OnMatchStartRequested;
        chatLobby.MatchStarted += OnMatchStarted;
        chatLobby.StopServerRequested += OnStopServerRequested;
        chatLobby.RuntimeErrorOccurred += OnChatLobbyRuntimeError;
        return chatLobby;
    }

    private ServerMatchViewModel CreateServerMatchViewModel()
    {
        var match = new ServerMatchViewModel(_serverMatchRuntimeFactory());
        match.ReturnToLobbyRequested += OnReturnToLobbyRequested;
        match.StopServerRequested += OnStopServerRequested;
        match.RuntimeErrorOccurred += OnServerMatchRuntimeError;
        return match;
    }

    private async void OnMatchStartRequested(object? sender, LoadingTransitionRequest request)
    {
        await RunLoadingTransitionAsync(request);
        if (sender is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async void OnMatchStarted(object? sender, EventArgs eventArgs)
    {
        await TransitionToAsync(TransitionKeys.StartMatch);
        if (sender is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnChatLobbyRuntimeError(object? sender, string message)
    {
        ShowError("Chat Lobby Error", message);
    }

    private void OnServerMatchRuntimeError(object? sender, string message)
    {
        ShowError("Match Error", message);
    }

    private async void OnStopServerRequested(object? sender, EventArgs eventArgs)
    {
        await StopServerAsync();
    }

    private async Task StopServerAsync()
    {
        try
        {
            await _serverLauncher.StopAsync();
            ServerSettings.StatusMessage = "Server stopped.";
            if (CurrentViewModel is IDisposable disposable)
            {
                disposable.Dispose();
            }
            CurrentViewModel = ServerSettings;
        }
        catch (Exception exception)
        {
            ShowError("Server Stop Failed", exception.Message);
        }
    }

    private async void OnReturnToLobbyRequested(object? sender, LoadingTransitionRequest request)
    {
        await RunLoadingTransitionAsync(request);
        if (sender is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnServerStartFailed(object? sender, string message)
    {
        CurrentViewModel = ServerSettings;
        ShowError("Server Start Failed", message);
    }

    private async Task WaitForMinimumLoadingTimeAsync(DateTime startedAtUtc)
    {
        TimeSpan elapsed = DateTime.UtcNow - startedAtUtc;
        TimeSpan minimumLoadingTime = TimeSpan.FromMilliseconds(_flowConfig.DefaultLoadingMilliseconds);
        if (elapsed < minimumLoadingTime)
        {
            await Task.Delay(minimumLoadingTime - elapsed);
        }
    }

    private void ShowError(string title, string message, string? details = null)
    {
        ErrorPopup = new ErrorPopupViewModel(title, message, details, DismissErrorCommand);
    }

    private object CreateViewModel(string viewKey)
    {
        return viewKey.Trim().ToLowerInvariant() switch
        {
            ScreenKeys.MainMenu => MainMenu,
            ScreenKeys.ChatLobby => CreateChatLobbyViewModel(),
            ScreenKeys.Match => CreateServerMatchViewModel(),
            ScreenKeys.ServerSettings => ServerSettings,
            _ => ServerSettings
        };
    }
}
