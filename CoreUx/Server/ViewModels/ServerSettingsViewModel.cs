using System.Windows.Input;
using System.Net;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Server.Services;

namespace HawkNet.CoreUx.Server.ViewModels;

public sealed class ServerSettingsViewModel : ObservableObject
{
    private readonly IServerLauncher _serverLauncher;
    private readonly GameHostConfig _sourceConfig;
    private string _statusMessage = "Ready to configure server.";
    private bool _isStarting;
	private string _profilePath = "Config/game-server.profile.json";

    private string _host = "0.0.0.0";
    private int _port = 7777;
    private int _maxPlayers = 64;
    private int _maxConcurrentConnections = 100;
    private int _sessionTimeoutSeconds = 30;
    private int _heartbeatIntervalSeconds = 15;
    private int _heartbeatTimeoutSeconds;
    private int _nextPlayerIdStart = 1000;
    private string _logLevel = "Information";
    private bool _enableVerboseLogging;
    private string _allowedIPs = string.Empty;
    private TransportProtocol _protocol = TransportProtocol.Tcp;
    private int _receiveBufferSize = 8192;
    private int _sendBufferSize = 8192;
	private int _outboundQueueCapacity = 256;
	private int _udpReceiveQueueCapacity = 256;
    private int _udpMaxDatagramBytes = 1200;
    private int _udpReliableWindowSize = 32;
    private int _udpReorderCapacity = 32;
    private int _udpInitialRetransmitMilliseconds = 250;
    private int _udpMinimumRetransmitMilliseconds = 100;
    private int _udpMaximumRetransmitMilliseconds = 2000;
    private int _udpMaximumRetries = 6;
    private int _udpAcknowledgementDelayMilliseconds = 20;
    private bool _noDelay = true;
    private int _tickRateHz = 30;
    private int _maxCatchUpTicks = 5;
	private int _inboundQueueCapacity = 4096;
	private int _maxCommandsPerTick = 512;
    private int _maxHistoryMessages = 100;
    private int _maxMessageLength = 512;
    private int _maxMessageBytes = 2048;
    private int _maxMessagesPerWindow = 5;
    private int _messageRateWindowSeconds = 10;
    private int _maxInvalidPacketsPerConnection = 5;
	private int _handshakeTimeoutSeconds = 10;
	private int _maxCommandPayloadBytes = 16384;
	private int _maxGameStateMessageBytes = 32768;
    private int _maxPacketsPerWindow = 300;
    private int _packetRateWindowSeconds = 10;
    private bool _enableConsoleLogging;
    private int _logIntervalSeconds = 30;
    private string _serviceModules = string.Empty;
    private string _gameStateModules = string.Empty;

    public ServerSettingsViewModel()
        : this(ServerConfigLoader.LoadOrDefault(), new MockServerLauncher())
    {
    }

    public ServerSettingsViewModel(GameHostConfig config, IServerLauncher serverLauncher)
    {
        _sourceConfig = config;
        _serverLauncher = serverLauncher;
		HostOptions = GetHostOptions();
        LogLevelOptions = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];
        ProtocolOptions = [TransportProtocol.Tcp, TransportProtocol.Udp];
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
        StartServerCommand = new AsyncRelayCommand(StartServerAsync, () => !IsStarting);
		ValidateCommand = new RelayCommand(ValidateProfile);
		LoadProfileCommand = new RelayCommand(LoadProfile);
		SaveProfileCommand = new RelayCommand(SaveProfile);
        LoadFromConfig(config);
    }

    public string[] HostOptions { get; }

    public string[] LogLevelOptions { get; }

    public TransportProtocol[] ProtocolOptions { get; }

    public ICommand StartServerCommand { get; }
	public ICommand BackCommand { get; }
	public ICommand ValidateCommand { get; }
	public ICommand LoadProfileCommand { get; }
	public ICommand SaveProfileCommand { get; }
	public string ProfilePath { get => _profilePath; set => SetProperty(ref _profilePath, value); }

    public event EventHandler<GameHostConfig>? ServerStartRequested;

    public event EventHandler<GameHostConfig>? ServerStarted;

    public event EventHandler<string>? ServerStartFailed;

    public event EventHandler<LoadingTransitionRequest>? LoadingTransitionRequested;

	public event EventHandler? BackRequested;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsStarting
    {
        get => _isStarting;
        private set
        {
            if (SetProperty(ref _isStarting, value) && StartServerCommand is AsyncRelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
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

    public int MaxPlayers
    {
        get => _maxPlayers;
        set => SetProperty(ref _maxPlayers, value);
    }

    public int MaxConcurrentConnections
    {
        get => _maxConcurrentConnections;
        set => SetProperty(ref _maxConcurrentConnections, value);
    }

    public int SessionTimeoutSeconds
    {
        get => _sessionTimeoutSeconds;
        set => SetProperty(ref _sessionTimeoutSeconds, value);
    }

    public int HeartbeatIntervalSeconds
    {
        get => _heartbeatIntervalSeconds;
        set => SetProperty(ref _heartbeatIntervalSeconds, value);
    }

    public int HeartbeatTimeoutSeconds
    {
        get => _heartbeatTimeoutSeconds;
        set => SetProperty(ref _heartbeatTimeoutSeconds, value);
    }

    public int NextPlayerIdStart
    {
        get => _nextPlayerIdStart;
        set => SetProperty(ref _nextPlayerIdStart, value);
    }

    public string LogLevel
    {
        get => _logLevel;
        set => SetProperty(ref _logLevel, value);
    }

    public bool EnableVerboseLogging
    {
        get => _enableVerboseLogging;
        set => SetProperty(ref _enableVerboseLogging, value);
    }

    public string AllowedIPs
    {
        get => _allowedIPs;
        set => SetProperty(ref _allowedIPs, value);
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
	public int OutboundQueueCapacity { get => _outboundQueueCapacity; set => SetProperty(ref _outboundQueueCapacity, value); }
	public int UdpReceiveQueueCapacity { get => _udpReceiveQueueCapacity; set => SetProperty(ref _udpReceiveQueueCapacity, value); }
    public int UdpMaxDatagramBytes { get => _udpMaxDatagramBytes; set => SetProperty(ref _udpMaxDatagramBytes, value); }
    public int UdpReliableWindowSize { get => _udpReliableWindowSize; set => SetProperty(ref _udpReliableWindowSize, value); }
    public int UdpReorderCapacity { get => _udpReorderCapacity; set => SetProperty(ref _udpReorderCapacity, value); }
    public int UdpInitialRetransmitMilliseconds { get => _udpInitialRetransmitMilliseconds; set => SetProperty(ref _udpInitialRetransmitMilliseconds, value); }
    public int UdpMinimumRetransmitMilliseconds { get => _udpMinimumRetransmitMilliseconds; set => SetProperty(ref _udpMinimumRetransmitMilliseconds, value); }
    public int UdpMaximumRetransmitMilliseconds { get => _udpMaximumRetransmitMilliseconds; set => SetProperty(ref _udpMaximumRetransmitMilliseconds, value); }
    public int UdpMaximumRetries { get => _udpMaximumRetries; set => SetProperty(ref _udpMaximumRetries, value); }
    public int UdpAcknowledgementDelayMilliseconds { get => _udpAcknowledgementDelayMilliseconds; set => SetProperty(ref _udpAcknowledgementDelayMilliseconds, value); }

    public int TickRateHz
    {
        get => _tickRateHz;
        set => SetProperty(ref _tickRateHz, value);
    }

    public int MaxCatchUpTicks
    {
        get => _maxCatchUpTicks;
        set => SetProperty(ref _maxCatchUpTicks, value);
    }
	public int InboundQueueCapacity { get => _inboundQueueCapacity; set => SetProperty(ref _inboundQueueCapacity, value); }
	public int MaxCommandsPerTick { get => _maxCommandsPerTick; set => SetProperty(ref _maxCommandsPerTick, value); }

    public int MaxHistoryMessages
    {
        get => _maxHistoryMessages;
        set => SetProperty(ref _maxHistoryMessages, value);
    }

    public int MaxMessageLength
    {
        get => _maxMessageLength;
        set => SetProperty(ref _maxMessageLength, value);
    }

    public int MaxMessageBytes
    {
        get => _maxMessageBytes;
        set => SetProperty(ref _maxMessageBytes, value);
    }

    public int MaxMessagesPerWindow
    {
        get => _maxMessagesPerWindow;
        set => SetProperty(ref _maxMessagesPerWindow, value);
    }

    public int MessageRateWindowSeconds
    {
        get => _messageRateWindowSeconds;
        set => SetProperty(ref _messageRateWindowSeconds, value);
    }

    public int MaxInvalidPacketsPerConnection
    {
        get => _maxInvalidPacketsPerConnection;
        set => SetProperty(ref _maxInvalidPacketsPerConnection, value);
    }
	public int HandshakeTimeoutSeconds { get => _handshakeTimeoutSeconds; set => SetProperty(ref _handshakeTimeoutSeconds, value); }
	public int MaxCommandPayloadBytes { get => _maxCommandPayloadBytes; set => SetProperty(ref _maxCommandPayloadBytes, value); }
	public int MaxGameStateMessageBytes { get => _maxGameStateMessageBytes; set => SetProperty(ref _maxGameStateMessageBytes, value); }

    public int MaxPacketsPerWindow
    {
        get => _maxPacketsPerWindow;
        set => SetProperty(ref _maxPacketsPerWindow, value);
    }

    public int PacketRateWindowSeconds
    {
        get => _packetRateWindowSeconds;
        set => SetProperty(ref _packetRateWindowSeconds, value);
    }

    public bool EnableConsoleLogging
    {
        get => _enableConsoleLogging;
        set => SetProperty(ref _enableConsoleLogging, value);
    }

    public int LogIntervalSeconds
    {
        get => _logIntervalSeconds;
        set => SetProperty(ref _logIntervalSeconds, value);
    }

    public string ServiceModules
    {
        get => _serviceModules;
        set => SetProperty(ref _serviceModules, value);
    }

    public string GameStateModules
    {
        get => _gameStateModules;
        set => SetProperty(ref _gameStateModules, value);
    }

    public GameHostConfig ToConfig()
    {
        return _sourceConfig with
        {
            Server = _sourceConfig.Server with
            {
                Host = Host,
                Port = Port,
                MaxPlayers = MaxPlayers,
                MaxConcurrentConnections = MaxConcurrentConnections,
                SessionTimeoutSeconds = SessionTimeoutSeconds,
                HeartbeatIntervalSeconds = HeartbeatIntervalSeconds,
                HeartbeatTimeoutSeconds = HeartbeatTimeoutSeconds,
                NextPlayerIdStart = NextPlayerIdStart,
                LogLevel = LogLevel,
                EnableVerboseLogging = EnableVerboseLogging,
                AllowedIPs = SplitList(AllowedIPs)
            },
            Networking = _sourceConfig.Networking with
            {
                Protocol = Protocol,
                ReceiveBufferSize = ReceiveBufferSize,
                SendBufferSize = SendBufferSize,
				OutboundQueueCapacity = OutboundQueueCapacity,
				UdpReceiveQueueCapacity = UdpReceiveQueueCapacity,
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
            },
            Simulation = _sourceConfig.Simulation with
            {
                TickRateHz = TickRateHz,
				MaxCatchUpTicks = MaxCatchUpTicks,
				InboundQueueCapacity = InboundQueueCapacity,
				MaxCommandsPerTick = MaxCommandsPerTick
            },
            Chat = _sourceConfig.Chat with
            {
                MaxHistoryMessages = MaxHistoryMessages,
                MaxMessageLength = MaxMessageLength,
                MaxMessageBytes = MaxMessageBytes,
                MaxMessagesPerWindow = MaxMessagesPerWindow,
                MessageRateWindowSeconds = MessageRateWindowSeconds
            },
            Security = _sourceConfig.Security with
            {
                MaxInvalidPacketsPerConnection = MaxInvalidPacketsPerConnection,
				HandshakeTimeoutSeconds = HandshakeTimeoutSeconds,
				MaxCommandPayloadBytes = MaxCommandPayloadBytes,
				MaxGameStateMessageBytes = MaxGameStateMessageBytes,
                MaxPacketsPerWindow = MaxPacketsPerWindow,
                PacketRateWindowSeconds = PacketRateWindowSeconds
            },
            Diagnostics = _sourceConfig.Diagnostics with
            {
                EnableConsoleLogging = EnableConsoleLogging,
                LogIntervalSeconds = LogIntervalSeconds
            },
            Modules = _sourceConfig.Modules with
            {
                Services = SplitList(ServiceModules),
                GameStates = SplitList(GameStateModules)
            }
        };
    }

    private void LoadFromConfig(GameHostConfig config)
    {
        Host = config.Server.Host;
        Port = config.Server.Port;
        MaxPlayers = config.Server.MaxPlayers;
        MaxConcurrentConnections = config.Server.MaxConcurrentConnections;
        SessionTimeoutSeconds = config.Server.SessionTimeoutSeconds;
        HeartbeatIntervalSeconds = config.Server.HeartbeatIntervalSeconds;
        HeartbeatTimeoutSeconds = config.Server.HeartbeatTimeoutSeconds;
        NextPlayerIdStart = config.Server.NextPlayerIdStart;
        LogLevel = config.Server.LogLevel;
        EnableVerboseLogging = config.Server.EnableVerboseLogging;
        AllowedIPs = JoinList(config.Server.AllowedIPs);
        Protocol = config.Networking.Protocol;
        ReceiveBufferSize = config.Networking.ReceiveBufferSize;
        SendBufferSize = config.Networking.SendBufferSize;
        NoDelay = config.Networking.NoDelay;
		OutboundQueueCapacity = config.Networking.OutboundQueueCapacity;
		UdpReceiveQueueCapacity = config.Networking.UdpReceiveQueueCapacity;
        UdpMaxDatagramBytes = config.Networking.UdpDelivery.MaxDatagramBytes;
        UdpReliableWindowSize = config.Networking.UdpDelivery.ReliableWindowSize;
        UdpReorderCapacity = config.Networking.UdpDelivery.ReorderCapacity;
        UdpInitialRetransmitMilliseconds = config.Networking.UdpDelivery.InitialRetransmitMilliseconds;
        UdpMinimumRetransmitMilliseconds = config.Networking.UdpDelivery.MinimumRetransmitMilliseconds;
        UdpMaximumRetransmitMilliseconds = config.Networking.UdpDelivery.MaximumRetransmitMilliseconds;
        UdpMaximumRetries = config.Networking.UdpDelivery.MaximumRetries;
        UdpAcknowledgementDelayMilliseconds = config.Networking.UdpDelivery.AcknowledgementDelayMilliseconds;
        TickRateHz = config.Simulation.TickRateHz;
        MaxCatchUpTicks = config.Simulation.MaxCatchUpTicks;
		InboundQueueCapacity = config.Simulation.InboundQueueCapacity;
		MaxCommandsPerTick = config.Simulation.MaxCommandsPerTick;
        MaxHistoryMessages = config.Chat.MaxHistoryMessages;
        MaxMessageLength = config.Chat.MaxMessageLength;
        MaxMessageBytes = config.Chat.MaxMessageBytes;
        MaxMessagesPerWindow = config.Chat.MaxMessagesPerWindow;
        MessageRateWindowSeconds = config.Chat.MessageRateWindowSeconds;
        MaxInvalidPacketsPerConnection = config.Security.MaxInvalidPacketsPerConnection;
		HandshakeTimeoutSeconds = config.Security.HandshakeTimeoutSeconds;
		MaxCommandPayloadBytes = config.Security.MaxCommandPayloadBytes;
		MaxGameStateMessageBytes = config.Security.MaxGameStateMessageBytes;
        MaxPacketsPerWindow = config.Security.MaxPacketsPerWindow;
        PacketRateWindowSeconds = config.Security.PacketRateWindowSeconds;
        EnableConsoleLogging = config.Diagnostics.EnableConsoleLogging;
        LogIntervalSeconds = config.Diagnostics.LogIntervalSeconds;
        ServiceModules = JoinList(config.Modules.Services);
        GameStateModules = JoinList(config.Modules.GameStates);
    }

    private Task StartServerAsync(CancellationToken token)
    {
        IsStarting = true;
        GameHostConfig config = ToConfig();
        StatusMessage = $"Starting server on {Host}:{Port}...";
        ServerStartRequested?.Invoke(this, config);

        if (LoadingTransitionRequested is not null)
        {
            LoadingTransitionRequested.Invoke(
                this,
                new LoadingTransitionRequest(
                    TransitionKeys.ServerStarted,
                    transitionToken => RunServerStartAsync(config, transitionToken)));
            return Task.CompletedTask;
        }

        return RunServerStartAsync(config, token);
    }

    private async Task RunServerStartAsync(GameHostConfig config, CancellationToken token)
    {
        try
        {
            ServerConfigValidationResult validation = ServerConfiguration.Validate(config);
            validation.ThrowIfInvalid();
            if (validation.Warnings.Count > 0)
            {
                StatusMessage = string.Join(" ", validation.Warnings);
            }
            await _serverLauncher.StartAsync(config, token);
            StatusMessage = $"Start requested for {Host}:{Port}.";
            ServerStarted?.Invoke(this, config);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Start failed: {exception.Message}";
            ServerStartFailed?.Invoke(this, exception.Message);
            throw;
        }
        finally
        {
            IsStarting = false;
        }
    }

    private static string JoinList(IEnumerable<string> values)
    {
        return string.Join(Environment.NewLine, values);
    }

    private static string[] SplitList(string value)
    {
        return value
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToArray();
    }

	private static string[] GetHostOptions()
	{
		var options = new List<string> { "127.0.0.1", "localhost", "0.0.0.0", "::1" };
		try
		{
			options.AddRange(Dns.GetHostAddresses(Dns.GetHostName())
				.Where(address => !IPAddress.IsLoopback(address))
				.Select(address => address.ToString()));
		}
		catch { /* Manual entry remains available when interface discovery fails. */ }
		return options.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private void ValidateProfile()
	{
		ServerConfigValidationResult result = ServerConfiguration.Validate(ToConfig());
		StatusMessage = result.IsValid
			? result.Warnings.Count == 0 ? "Configuration is valid." : "Valid with warnings: " + string.Join(" ", result.Warnings)
			: "Configuration errors: " + string.Join(" ", result.Errors);
	}

	private void LoadProfile()
	{
		try
		{
			LoadFromConfig(ServerConfiguration.Load(ProfilePath));
			StatusMessage = $"Loaded profile {Path.GetFullPath(ProfilePath)}.";
		}
		catch (Exception exception) { StatusMessage = $"Profile load failed: {exception.Message}"; }
	}

	private void SaveProfile()
	{
		try
		{
			ServerConfigLoader.Save(ProfilePath, ToConfig());
			StatusMessage = $"Saved profile {Path.GetFullPath(ProfilePath)}.";
		}
		catch (Exception exception) { StatusMessage = $"Profile save failed: {exception.Message}"; }
	}
}
