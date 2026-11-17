using System;
using System.Text.Json;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	public enum TransportProtocol
	{
		Tcp,
		Udp
	}

	public record GameHostConfig
	{
		// TODO: Move module/state settings behind a ConfigService once config ownership grows beyond this root record.
		public ServerSettings Server { get; init; } = new();
		public NetworkingSettings Networking { get; init; } = new();
		public SimulationSettings Simulation { get; init; } = new();
		public ChatSettings Chat { get; init; } = new();
		public PlayerIdentitySettings PlayerIdentity { get; init; } = new();
		public ChatLobbySettings ChatLobby { get; init; } = new();
		public MatchSettings Match { get; init; } = new();
		public GameModeSettings GameMode { get; init; } = new();
		public SecuritySettings Security { get; init; } = new();
		public DiagnosticsSettings Diagnostics { get; init; } = new();
		public MaintenanceSettings Maintenance { get; init; } = new();
		public ModuleSettings Modules { get; init; } = new();
		public Dictionary<string, JsonElement> ModuleOptions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	}

	public record ServerSettings
	{
		public string Host { get; init; } = "0.0.0.0";
		public int Port { get; init; } = 7777;
		public int MaxPlayers { get; init; } = 64;
		public int MaxConcurrentConnections { get; init; } = 100;
		public int SessionTimeoutSeconds { get; init; } = 30;
		public int HeartbeatIntervalSeconds { get; init; } = 15;
		public int HeartbeatTimeoutSeconds { get; init; } = 0;
		public int NextPlayerIdStart { get; init; } = 1000;
		public bool UseTls { get; init; } = false;
		public string CertificatePath { get; init; } = string.Empty;
		public string DataPath { get; init; } = "Data";
		public string LogLevel { get; init; } = "Information";
		public bool EnableVerboseLogging { get; init; } = false;
		public string[] AllowedIPs { get; init; } = Array.Empty<string>();
	}

	public record NetworkingSettings
	{
		public TransportProtocol Protocol { get; init; } = TransportProtocol.Tcp;
		public int ReceiveBufferSize { get; init; } = 8192;
		public int SendBufferSize { get; init; } = 8192;
		public int OutboundQueueCapacity { get; init; } = 256;
		public int UdpReceiveQueueCapacity { get; init; } = 256;
		public UdpDeliverySettings UdpDelivery { get; init; } = new();
		public bool NoDelay { get; init; } = true;
	}

	public record SimulationSettings
	{
		public int TickRateHz { get; init; } = 30;
		public int MaxCatchUpTicks { get; init; } = 5;
		public int InboundQueueCapacity { get; init; } = 4096;
		public int MaxCommandsPerTick { get; init; } = 512;
	}

	public record ChatSettings
	{
		public int MaxHistoryMessages { get; init; } = 100;
		public int MaxMessageLength { get; init; } = 512;
		public int MaxMessageBytes { get; init; } = 2048;
		public int MaxMessagesPerWindow { get; init; } = 5;
		public int MessageRateWindowSeconds { get; init; } = 10;
	}

	public record PlayerIdentitySettings
	{
		public int MaxDisplayNameLength { get; init; } = 24;
		public string DefaultDisplayNameFormat { get; init; } = "Player {0}";
	}

	public record ChatLobbySettings
	{
		public float ReadyThresholdPercent { get; init; } = 100f;
		public float TransitionCountdownSeconds { get; init; } = 5f;
		public string ReadyTransitionStateId { get; init; } = string.Empty;
		public int MaxReadyPayloadBytes { get; init; } = 4096;
	}

	public record MatchPhaseSettings
	{
		public string PhaseId { get; init; } = string.Empty;
		public float Seconds { get; init; } = 0f;
	}

	public record MatchSettings
	{
		public string VariantId { get; init; } = "base";

		public MatchPhaseSettings[] Phases { get; init; } =
		[
			new MatchPhaseSettings
			{
				PhaseId = "pre_match",
				Seconds = 5f
			},
			new MatchPhaseSettings
			{
				PhaseId = "match",
				Seconds = 300f
			},
			new MatchPhaseSettings
			{
				PhaseId = "post_match",
				Seconds = 10f
			}
		];

		public string CompletedTransitionStateId { get; init; } = string.Empty;
	}

	public record GameModeSettings
	{
		public string InitialStateId { get; init; } = "chat_lobby";
		public GameModeStateSettings[] States { get; init; } =
		[
			new GameModeStateSettings
			{
				Id = "chat_lobby"
			}
		];
	}

	public record GameModeStateSettings
	{
		public string Id { get; init; } = string.Empty;
		public string[] Transitions { get; init; } = Array.Empty<string>();
	}

	public record SecuritySettings
	{
		public int HandshakeTimeoutSeconds { get; init; } = 10;
		public int MaxCommandPayloadBytes { get; init; } = 16384;
		public int MaxGameStateMessageBytes { get; init; } = 32768;
		public int MaxInvalidPacketsPerConnection { get; init; } = 5;
		public int MaxPacketsPerWindow { get; init; } = 300;
		public int PacketRateWindowSeconds { get; init; } = 10;
	}

	public record DiagnosticsSettings
	{
		public bool EnableConsoleLogging { get; init; } = false;
		public int LogIntervalSeconds { get; init; } = 30;
	}

	public record MaintenanceSettings
	{
		public bool AutoRestart { get; init; } = false;
		public string RestartWindowUtc { get; init; } = "03:00-03:15";
	}

	public record ModuleSettings
	{
		public string[] Services { get; init; } =
		[
			"core",
			"player_identity",
			"diagnostics",
			"chat_service"
		];

		public string[] GameStates { get; init; } =
		[
			"chat_lobby"
		];
	}
}
