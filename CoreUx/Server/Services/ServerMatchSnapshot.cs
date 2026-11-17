using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using HawkNet.CoreUx.Client.World;

namespace HawkNet.CoreUx.Server.Services;

public sealed record ServerMatchSnapshot
{
	public ServerMonitorSnapshot? Monitor { get; init; }
	public IReadOnlyList<WorldRegionDescriptor> Regions { get; init; } = Array.Empty<WorldRegionDescriptor>();
    public MatchPayload Payload { get; init; } = new();

    public IReadOnlyList<ChatEntry> ChatHistory { get; init; } = Array.Empty<ChatEntry>();

    public int MaxPlayers { get; init; } = 64;

    public TimeSpan GameTime { get; init; }

    public TimeSpan PhaseTime { get; init; }

    public WorldSnapshot? World { get; init; }
}
