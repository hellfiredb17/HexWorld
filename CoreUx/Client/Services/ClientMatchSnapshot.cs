using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.World;

namespace HawkNet.CoreUx.Client.Services;

public sealed record ClientMatchSnapshot
{
    public MatchPayload Payload { get; init; } = new();

    public IReadOnlyList<ChatEntry> ChatHistory { get; init; } = Array.Empty<ChatEntry>();

    public int? CurrentPlayerId { get; init; }

    public WorldSnapshot? World { get; init; }
}
