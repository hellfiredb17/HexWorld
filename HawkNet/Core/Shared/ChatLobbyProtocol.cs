namespace HawkNet.Core.Shared
{
	public sealed record ChatLobbyPayload
	{
		public IReadOnlyList<ChatLobbyPlayer> Players { get; init; } = Array.Empty<ChatLobbyPlayer>();
		public IReadOnlyList<ChatEntry> ChatHistory { get; init; } = Array.Empty<ChatEntry>();
		public ChatLobbyReadyStateResponse ReadyState { get; init; } = new();
		public ChatLobbyCountdownResponse? Countdown { get; init; }
	}

	public sealed record ChatLobbyPlayer
	{
		public int PlayerId { get; init; }
		public string DisplayName { get; init; } = string.Empty;
		public bool IsReady { get; init; }
		public byte[] ReadyPayload { get; init; } = [];
	}

	public sealed record ChatLobbyPlayerStateResponse
	{
		public ChatLobbyPlayer[] Players { get; init; } = [];
	}

	public enum ChatLobbyCountdownStatus
	{
		None,
		Started,
		Tick,
		Canceled,
		Completed
	}

	public sealed record ChatLobbyReadyRequest
	{
		public bool IsReady { get; init; }
		public byte[] Payload { get; init; } = [];
	}

	public sealed record ChatLobbyReadyPlayer
	{
		public int PlayerId { get; init; }
		public string DisplayName { get; init; } = string.Empty;
		public bool IsReady { get; init; }
		public byte[] ReadyPayload { get; init; } = [];
	}

	public sealed record ChatLobbyReadyStateResponse
	{
		public ChatLobbyReadyPlayer[] Players { get; init; } = [];
		public int ReadyPlayerCount { get; init; }
		public int RequiredReadyPlayerCount { get; init; }
		public float ReadyPercentage { get; init; }
	}

	public sealed record ChatLobbyCountdownResponse
	{
		public ChatLobbyCountdownStatus Status { get; init; }
		public float RemainingSeconds { get; init; }
		public DateTime StartedAtUtc { get; init; }
		public DateTime EndsAtUtc { get; init; }
	}
}
