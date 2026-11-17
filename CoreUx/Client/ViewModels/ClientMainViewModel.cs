using HawkNet.Core;
using HawkNet.Core.Client;
using HawkNet.CoreUx.Client.Services;
using HawkNet.CoreUx.Client.World;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Common.ViewModels;

namespace HawkNet.CoreUx.Client.ViewModels;

public sealed class ClientMainViewModel : ObservableObject
{
    private readonly IClientLauncher _clientLauncher;
    private readonly IClientStateManager _stateManager;
    private readonly Func<IClientChatLobbyRuntime> _chatLobbyRuntimeFactory;
    private readonly Func<IClientMatchRuntime> _matchRuntimeFactory;
    private readonly ILogger _logger;
    private readonly WorldViewerConfig _worldViewerConfig;
    private object _currentViewModel;
    private ErrorPopupViewModel? _errorPopup;

    public ClientMainViewModel()
        : this(
            new GameClientConfig(),
            new MockClientLauncher(),
            new MockClientStateManager(),
            () => new MockClientChatLobbyRuntime(),
            () => new MockClientMatchRuntime())
    {
    }

    public ClientMainViewModel(
        GameClientConfig config,
        IClientLauncher clientLauncher,
        IClientStateManager stateManager,
        Func<IClientChatLobbyRuntime> chatLobbyRuntimeFactory,
        Func<IClientMatchRuntime> matchRuntimeFactory,
        bool startAtMainMenu = false,
        IClientServerDiscovery? serverDiscovery = null,
        ILogger? logger = null,
        WorldViewerConfig? worldViewerConfig = null,
        IEnumerable<ICoreUxTool>? tools = null)
    {
        _clientLauncher = clientLauncher;
        _stateManager = stateManager;
        _chatLobbyRuntimeFactory = chatLobbyRuntimeFactory;
        _matchRuntimeFactory = matchRuntimeFactory;
        _logger = logger ?? NullLogger.Instance;
        _worldViewerConfig = worldViewerConfig ?? WorldViewerConfig.CreateDefault();
        _stateManager.StateChanged += OnClientStateChanged;
        _stateManager.StateEntryFailed += OnClientStateEntryFailed;
        _stateManager.Disconnected += OnClientDisconnected;
        DismissErrorCommand = new AsyncRelayCommand(_ =>
        {
            ErrorPopup = null;
            return Task.CompletedTask;
        });
        MainMenu = new MainMenuViewModel(tools);
        MainMenu.CreateServerRequested += OnCreateServerRequested;
        MainMenu.FindLobbyRequested += OnFindLobbyRequested;
        MainMenu.ToolRequested += OpenTool;
        ConnectionSetup = new ConnectionSetupViewModel(config, clientLauncher, serverDiscovery);
        ConnectionSetup.LoadingTransitionRequested += OnConnectionLoadingTransitionRequested;
        ConnectionSetup.ConnectFailed += OnConnectionFailed;
        _currentViewModel = startAtMainMenu ? MainMenu : ConnectionSetup;
    }

    public string Title => "HawkNet Client";

    public ConnectionSetupViewModel ConnectionSetup { get; }

    public MainMenuViewModel MainMenu { get; }

    public AsyncRelayCommand DismissErrorCommand { get; }

    public event EventHandler? CreateServerRequested;

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

    private async void OnConnectionLoadingTransitionRequested(object? sender, LoadingTransitionRequest request)
    {
        await RunConnectionTransitionAsync(request);
    }

    private void OnFindLobbyRequested(object? sender, EventArgs eventArgs)
    {
        CurrentViewModel = ConnectionSetup;
    }

    private void OnCreateServerRequested(object? sender, EventArgs eventArgs)
    {
        CreateServerRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunConnectionTransitionAsync(LoadingTransitionRequest request)
    {
        _logger.Log("Connection transition started.", nameof(ClientMainViewModel));
        CurrentViewModel = new LoadingScreenViewModel(
            "Connecting Client",
            "Opening a connection to the HawkNet server.");
        try
        {
            await request.WaitForReadyAsync(CancellationToken.None);
            _logger.Log($"Connection ready. Navigating to current state '{_stateManager.CurrentStateId}'.", nameof(ClientMainViewModel));
            NavigateToState(_stateManager.CurrentStateId);
        }
        catch (Exception exception)
        {
            _logger.Exception(exception, "Client connection transition failed.", nameof(ClientMainViewModel));
            CurrentViewModel = ConnectionSetup;
            ShowError("Client Connect Failed", exception.Message);
        }
    }

    private void NavigateToState(string? stateId)
    {
        _logger.Log(
            $"NavigateToState requested state='{stateId ?? "<null>"}' from view '{CurrentViewModel.GetType().Name}'.",
            nameof(ClientMainViewModel));
        if (string.Equals(stateId, "chat_lobby", StringComparison.OrdinalIgnoreCase))
        {
            DisposeCurrentViewModel();
            CurrentViewModel = CreateChatLobbyViewModel();
            _logger.Log("Navigated to chat lobby.", nameof(ClientMainViewModel));
            return;
        }

        if (string.Equals(stateId, "match", StringComparison.OrdinalIgnoreCase))
        {
            DisposeCurrentViewModel();
            CurrentViewModel = CreateMatchViewModel();
            _logger.Log("Navigated to match.", nameof(ClientMainViewModel));
            return;
        }

        if (CurrentViewModel is LoadingScreenViewModel)
        {
            CurrentViewModel = ConnectionSetup;
        }

        _logger.Warning($"Unknown client state '{stateId ?? "<null>"}'.", nameof(ClientMainViewModel));
        ShowError("Unknown Client State", string.IsNullOrWhiteSpace(stateId)
            ? "The server did not provide an initial client state."
            : $"Client state '{stateId}' is not supported.");
    }

    private ClientChatLobbyViewModel CreateChatLobbyViewModel()
    {
        var lobby = new ClientChatLobbyViewModel(_chatLobbyRuntimeFactory());
        lobby.DisconnectRequested += OnDisconnectRequested;
        lobby.RuntimeErrorOccurred += OnRuntimeErrorOccurred;
        return lobby;
    }

    private ClientMatchViewModel CreateMatchViewModel()
    {
        var match = new ClientMatchViewModel(_matchRuntimeFactory(), _worldViewerConfig);
        match.DisconnectRequested += OnDisconnectRequested;
        match.RuntimeErrorOccurred += OnRuntimeErrorOccurred;
        return match;
    }

    private async void OnDisconnectRequested(object? sender, EventArgs eventArgs)
    {
        _logger.Log($"Disconnect requested by view '{sender?.GetType().Name ?? "<unknown>"}'.", nameof(ClientMainViewModel));
        await DisconnectAsync(showPopup: false);
    }

    private async Task DisconnectAsync(bool showPopup, string message = "Disconnected.")
    {
        _logger.Warning(
            $"DisconnectAsync showPopup={showPopup} message='{message}' currentView='{CurrentViewModel.GetType().Name}'.",
            nameof(ClientMainViewModel));
        try
        {
            await _clientLauncher.DisconnectAsync();
        }
        catch (Exception exception)
        {
            _logger.Exception(exception, "Client launcher disconnect failed.", nameof(ClientMainViewModel));
            ShowError("Disconnect Failed", exception.Message);
            return;
        }

        DisposeCurrentViewModel();
        ConnectionSetup.StatusMessage = message;
        CurrentViewModel = ConnectionSetup;
        if (showPopup)
        {
            ShowError("Client Disconnected", message);
        }
    }

    private void OnClientStateChanged(object? sender, ClientStateChangedEventArgs eventArgs)
    {
        _logger.Log($"Client state changed to '{eventArgs.StateId}'.", nameof(ClientMainViewModel));
        NavigateToState(eventArgs.StateId);
    }

    private void OnClientStateEntryFailed(object? sender, ClientStateEntryFailedEventArgs eventArgs)
    {
        _logger.Error(
            $"Client state entry failed for '{eventArgs.StateId}': {eventArgs.Message}",
            nameof(ClientMainViewModel));
        ShowError("Client State Failed", eventArgs.Message);
    }

    private async void OnClientDisconnected(object? sender, ClientDisconnectedEventArgs eventArgs)
    {
        _logger.Warning(
            $"Client disconnected event expected={eventArgs.IsExpected} message='{eventArgs.Message}' currentView='{CurrentViewModel.GetType().Name}'.",
            nameof(ClientMainViewModel));
        if (ReferenceEquals(CurrentViewModel, ConnectionSetup))
        {
            return;
        }

        await DisconnectAsync(showPopup: !eventArgs.IsExpected, eventArgs.Message);
    }

    private void OnConnectionFailed(object? sender, string message)
    {
        _logger.Error($"Client connection failed: {message}", nameof(ClientMainViewModel));
        CurrentViewModel = ConnectionSetup;
        ShowError("Client Connect Failed", message);
    }

    private void OnRuntimeErrorOccurred(object? sender, string message)
    {
        _logger.Error($"Client runtime error from '{sender?.GetType().Name ?? "<unknown>"}': {message}", nameof(ClientMainViewModel));
        ShowError("Client Runtime Error", message);
    }

    private void DisposeCurrentViewModel()
    {
        if (CurrentViewModel is IDisposable disposable && !ReferenceEquals(CurrentViewModel, ConnectionSetup))
        {
            disposable.Dispose();
        }
    }

    private void ShowError(string title, string message, string? details = null)
    {
        _logger.Warning($"Showing error '{title}': {message}", nameof(ClientMainViewModel));
        ErrorPopup = new ErrorPopupViewModel(title, message, details, DismissErrorCommand);
    }
}
