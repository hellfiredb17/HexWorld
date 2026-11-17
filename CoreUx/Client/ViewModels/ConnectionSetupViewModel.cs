using System.Collections.ObjectModel;
using System.Windows.Input;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.Services;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.Services;

namespace HawkNet.CoreUx.Client.ViewModels;

public sealed class ConnectionSetupViewModel : ObservableObject
{
    private readonly IClientLauncher _clientLauncher;
    private readonly IClientServerDiscovery _serverDiscovery;
    private readonly GameClientConfig _sourceConfig;
    private string _statusMessage = "Ready to connect.";
    private bool _isConnecting;
    private bool _isSearching;
    private CancellationTokenSource? _searchCancellation;
    private FoundServerViewModel? _selectedServer;
    private int _scanPort = 7777;
    private string _host = "127.0.0.1";
    private int _port = 7777;
    private int _heartbeatIntervalSeconds = 15;
    private bool _reconnectEnabled = true;
    private int _receiveBufferSize = 8192;
    private int _sendBufferSize = 8192;
    private bool _noDelay = true;
	private TransportProtocol _protocol = TransportProtocol.Tcp;
    private int _udpMaxDatagramBytes = 1200;
    private int _udpReliableWindowSize = 32;
    private int _udpReorderCapacity = 32;
    private int _udpInitialRetransmitMilliseconds = 250;
    private int _udpMinimumRetransmitMilliseconds = 100;
    private int _udpMaximumRetransmitMilliseconds = 2000;
    private int _udpMaximumRetries = 6;
    private int _udpAcknowledgementDelayMilliseconds = 20;

    public ConnectionSetupViewModel()
        : this(new GameClientConfig(), new MockClientLauncher(), new MockClientServerDiscovery())
    {
    }

    public ConnectionSetupViewModel(
        GameClientConfig config,
        IClientLauncher clientLauncher,
        IClientServerDiscovery? serverDiscovery = null)
    {
        _sourceConfig = config;
        _clientLauncher = clientLauncher;
        _serverDiscovery = serverDiscovery ?? new MockClientServerDiscovery();
        HostOptions = ["127.0.0.1", "localhost", "::1"];
		ProtocolOptions = [TransportProtocol.Tcp, TransportProtocol.Udp];
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnecting);
        SearchCommand = new RelayCommand(StartOrStopSearch);
        LoadFromConfig(config);
    }

    public string[] HostOptions { get; }

	public TransportProtocol[] ProtocolOptions { get; }

    public ICommand ConnectCommand { get; }

    public ICommand SearchCommand { get; }

    public ObservableCollection<FoundServerViewModel> FoundServers { get; } = new();

    public string SearchButtonText => IsSearching ? "Stop" : "Search";

    public event EventHandler<GameClientConfig>? ConnectRequested;

    public event EventHandler<GameClientConfig>? Connected;

    public event EventHandler<string>? ConnectFailed;

    public event EventHandler<LoadingTransitionRequest>? LoadingTransitionRequested;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
            if (SetProperty(ref _isConnecting, value) && ConnectCommand is AsyncRelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                OnPropertyChanged(nameof(SearchButtonText));
            }
        }
    }

    public FoundServerViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (!SetProperty(ref _selectedServer, value) || value is null)
            {
                return;
            }

            Host = value.Host;
            Port = value.Port;
            StatusMessage = $"Selected {value.EndpointText}.";
        }
    }

    public int ScanPort
    {
        get => _scanPort;
        set => SetProperty(ref _scanPort, value);
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public int HeartbeatIntervalSeconds
    {
        get => _heartbeatIntervalSeconds;
        set => SetProperty(ref _heartbeatIntervalSeconds, value);
    }

    public bool ReconnectEnabled
    {
        get => _reconnectEnabled;
        set => SetProperty(ref _reconnectEnabled, value);
    }

    public int ReceiveBufferSize
    {
        get => _receiveBufferSize;
        set => SetProperty(ref _receiveBufferSize, value);
    }

    public int SendBufferSize
    {
        get => _sendBufferSize;
        set => SetProperty(ref _sendBufferSize, value);
    }

    public bool NoDelay
    {
        get => _noDelay;
        set => SetProperty(ref _noDelay, value);
    }

	public TransportProtocol Protocol
	{
		get => _protocol;
		set
        {
            if (SetProperty(ref _protocol, value)) OnPropertyChanged(nameof(IsUdpSelected));
        }
	}

    public bool IsUdpSelected => Protocol == TransportProtocol.Udp;
    public int UdpMaxDatagramBytes { get => _udpMaxDatagramBytes; set => SetProperty(ref _udpMaxDatagramBytes, value); }
    public int UdpReliableWindowSize { get => _udpReliableWindowSize; set => SetProperty(ref _udpReliableWindowSize, value); }
    public int UdpReorderCapacity { get => _udpReorderCapacity; set => SetProperty(ref _udpReorderCapacity, value); }
    public int UdpInitialRetransmitMilliseconds { get => _udpInitialRetransmitMilliseconds; set => SetProperty(ref _udpInitialRetransmitMilliseconds, value); }
    public int UdpMinimumRetransmitMilliseconds { get => _udpMinimumRetransmitMilliseconds; set => SetProperty(ref _udpMinimumRetransmitMilliseconds, value); }
    public int UdpMaximumRetransmitMilliseconds { get => _udpMaximumRetransmitMilliseconds; set => SetProperty(ref _udpMaximumRetransmitMilliseconds, value); }
    public int UdpMaximumRetries { get => _udpMaximumRetries; set => SetProperty(ref _udpMaximumRetries, value); }
    public int UdpAcknowledgementDelayMilliseconds { get => _udpAcknowledgementDelayMilliseconds; set => SetProperty(ref _udpAcknowledgementDelayMilliseconds, value); }

    public GameClientConfig ToConfig()
    {
        return _sourceConfig with
        {
            Server = _sourceConfig.Server with
            {
                Host = Host,
                Port = Port
            },
            Networking = _sourceConfig.Networking with
            {
				Protocol = Protocol,
                HeartbeatIntervalSeconds = HeartbeatIntervalSeconds,
                ReconnectEnabled = ReconnectEnabled,
                ReceiveBufferSize = ReceiveBufferSize,
                SendBufferSize = SendBufferSize,
                UdpDelivery = _sourceConfig.Networking.UdpDelivery with
                {
                    MaxDatagramBytes = UdpMaxDatagramBytes,
                    ReliableWindowSize = UdpReliableWindowSize,
                    ReorderCapacity = UdpReorderCapacity,
                    InitialRetransmitMilliseconds = UdpInitialRetransmitMilliseconds,
                    MinimumRetransmitMilliseconds = UdpMinimumRetransmitMilliseconds,
                    MaximumRetransmitMilliseconds = UdpMaximumRetransmitMilliseconds,
                    MaximumRetries = UdpMaximumRetries,
                    AcknowledgementDelayMilliseconds = UdpAcknowledgementDelayMilliseconds
                },
                NoDelay = NoDelay
            }
        };
    }

    private void LoadFromConfig(GameClientConfig config)
    {
        Host = config.Server.Host;
        Port = config.Server.Port;
        HeartbeatIntervalSeconds = config.Networking.HeartbeatIntervalSeconds;
		Protocol = config.Networking.Protocol;
        ReconnectEnabled = config.Networking.ReconnectEnabled;
        ReceiveBufferSize = config.Networking.ReceiveBufferSize;
        SendBufferSize = config.Networking.SendBufferSize;
        UdpMaxDatagramBytes = config.Networking.UdpDelivery.MaxDatagramBytes;
        UdpReliableWindowSize = config.Networking.UdpDelivery.ReliableWindowSize;
        UdpReorderCapacity = config.Networking.UdpDelivery.ReorderCapacity;
        UdpInitialRetransmitMilliseconds = config.Networking.UdpDelivery.InitialRetransmitMilliseconds;
        UdpMinimumRetransmitMilliseconds = config.Networking.UdpDelivery.MinimumRetransmitMilliseconds;
        UdpMaximumRetransmitMilliseconds = config.Networking.UdpDelivery.MaximumRetransmitMilliseconds;
        UdpMaximumRetries = config.Networking.UdpDelivery.MaximumRetries;
        UdpAcknowledgementDelayMilliseconds = config.Networking.UdpDelivery.AcknowledgementDelayMilliseconds;
        NoDelay = config.Networking.NoDelay;
    }

    private Task ConnectAsync(CancellationToken token)
    {
        IsConnecting = true;
        GameClientConfig config = ToConfig();
        StatusMessage = $"Connecting to {Host}:{Port}...";
        ConnectRequested?.Invoke(this, config);

        if (LoadingTransitionRequested is not null)
        {
            LoadingTransitionRequested.Invoke(
                this,
                new LoadingTransitionRequest(
                    TransitionKeys.ClientConnected,
                    transitionToken => RunConnectAsync(config, transitionToken)));
            return Task.CompletedTask;
        }

        return RunConnectAsync(config, token);
    }

    private void StartOrStopSearch()
    {
        if (IsSearching)
        {
            _searchCancellation?.Cancel();
            StatusMessage = "Stopping search...";
            return;
        }

        _ = RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        using var searchCancellation = new CancellationTokenSource();
        _searchCancellation = searchCancellation;
        int scanPort = ScanPort;
		TransportProtocol scanProtocol = Protocol;
        IsSearching = true;
        SelectedServer = null;
        FoundServers.Clear();
        StatusMessage = $"Searching current network for {scanProtocol} servers on port {scanPort}...";
        try
        {
            IReadOnlyList<FoundServer> results = await Task.Run(
				() => _serverDiscovery.SearchAsync(scanPort, scanProtocol, searchCancellation.Token),
                searchCancellation.Token);
            foreach (FoundServer server in results)
            {
                AddOrUpdateFoundServer(server);
            }

            StatusMessage = results.Count == 0
                ? "No servers found."
                : $"Found {results.Count} server(s).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Search stopped.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Search failed: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, searchCancellation))
            {
                _searchCancellation = null;
            }

            IsSearching = false;
        }
    }

    private void AddOrUpdateFoundServer(FoundServer server)
    {
        FoundServerViewModel? existing = FoundServers.FirstOrDefault(found =>
            string.Equals(found.Host, server.Host, StringComparison.OrdinalIgnoreCase)
            && found.Port == server.Port);
        if (existing is not null)
        {
            return;
        }

        var viewModel = new FoundServerViewModel(server);
        FoundServers.Add(viewModel);
    }

    private async Task RunConnectAsync(GameClientConfig config, CancellationToken token)
    {
        try
        {
            ValidateConfig(config);
            await _clientLauncher.ConnectAsync(config, token);
            StatusMessage = $"Connected to {Host}:{Port}.";
            Connected?.Invoke(this, config);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Connect failed: {exception.Message}";
            ConnectFailed?.Invoke(this, exception.Message);
            throw;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private static void ValidateConfig(GameClientConfig config)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.Server.Host)) errors.Add("Server host cannot be empty.");
        if (config.Server.Port is < 1 or > 65535) errors.Add("Server port must be between 1 and 65535.");
        if (config.Networking.ReceiveBufferSize <= 0) errors.Add("Receive buffer size must be greater than zero.");
        if (config.Networking.SendBufferSize <= 0) errors.Add("Send buffer size must be greater than zero.");
        if (config.Networking.HeartbeatIntervalSeconds <= 0) errors.Add("Heartbeat interval must be greater than zero.");
        errors.AddRange(UdpDeliveryConfiguration.Validate(config.Networking.UdpDelivery));
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
    }
}
