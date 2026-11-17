namespace HawkNet.Core.Shared
{
	public sealed record MatchPlayer
	{
		public int PlayerId { get; init; }
		public string DisplayName { get; init; } = string.Empty;
		public bool IsConnected { get; init; }
		public byte[] ReadyPayload { get; init; } = [];
	}

	public sealed record MatchPayload
	{
		public string VariantId { get; init; } = string.Empty;
		public string PhaseId { get; init; } = string.Empty;
		public float PhaseSeconds { get; init; }
		public DateTime PhaseStartedAtUtc { get; init; }
		public DateTime PhaseEndsAtUtc { get; init; }
		public MatchPlayer[] Players { get; init; } = [];
	}

	public sealed record MatchPhaseChangeResponse
	{
		public string VariantId { get; init; } = string.Empty;
		public string PhaseId { get; init; } = string.Empty;
		public float PhaseSeconds { get; init; }
		public DateTime PhaseStartedAtUtc { get; init; }
		public DateTime PhaseEndsAtUtc { get; init; }
		public MatchPlayer[] Players { get; init; } = [];
	}

	public sealed record MatchCommandRequest
	{
		public string CommandId { get; init; } = string.Empty;
		public byte[] Payload { get; init; } = [];
	}
}
