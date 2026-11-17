using HawkNet.Core.Client;
using HawkNet.Core.Shared;

namespace HawkNet.CoreUx.Client.Services;

public sealed record ClientChatLobbySnapshot
{
    public IReadOnlyList<ChatLobbyPlayer> Players { get; init; } = Array.Empty<ChatLobbyPlayer>();

    public IReadOnlyList<ChatEntry> ChatHistory { get; init; } = Array.Empty<ChatEntry>();

    public ChatLobbyReadyStateResponse ReadyState { get; init; } = new();

    public ChatLobbyCountdownResponse? Countdown { get; init; }

    public ClientPlayerData? CurrentPlayer { get; init; }
}
