using HawkNet.Core.Shared;

namespace HawkNet.CoreUx.Server.Services;

public sealed record ChatLobbySnapshot
{
    public IReadOnlyList<ChatLobbyPlayer> Players { get; init; } = Array.Empty<ChatLobbyPlayer>();

    public IReadOnlyList<ChatEntry> ChatHistory { get; init; } = Array.Empty<ChatEntry>();

    public ChatLobbyReadyStateResponse ReadyState { get; init; } = new();

    public ChatLobbyCountdownResponse? Countdown { get; init; }

    public int MaxPlayers { get; init; } = 64;

    public string MapName { get; init; } = "Map preview pending";
}
