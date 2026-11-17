/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	public enum ConnectionCloseReason
	{
		Unknown,
		ClientDisconnected,
		ServerShutdown,
		HeartbeatTimeout,
		SessionTimeout,
		InvalidReconnectPayload,
		InvalidReconnectToken,
		MaxPlayersReached,
		MaxConcurrentConnectionsReached,
		RemoteIpNotAllowed,
		TooManyInvalidPackets,
		PacketRateLimitExceeded,
		HandshakeTimeout,
		ServerInboxFull,
		OutboundQueueFull,
		SendFailed,
		ProtocolError,
		ReliableDeliveryFailed
	}

	public readonly record struct PlayerConnectedEvent(int PlayerId) : IGameEvent;
	public readonly record struct PlayerReconnectedEvent(int PlayerId) : IGameEvent;
	public readonly record struct PlayerConnectionLostEvent(int PlayerId, ConnectionCloseReason Reason) : IGameEvent;
	public readonly record struct PlayerDisconnectedEvent(int PlayerId, ConnectionCloseReason Reason) : IGameEvent;
	public readonly record struct DisplayNameUpdateReceivedEvent(int PlayerId, byte[] Payload) : IGameEvent;
	public readonly record struct PlayerDisplayNameChangedEvent(PlayerIdentity Identity) : IGameEvent;
	public readonly record struct PlayerInputReceivedEvent(int PlayerId, byte[] Payload) : IGameEvent;
	public readonly record struct ChatMessageReceivedEvent(int PlayerId, byte[] Payload) : IGameEvent;
	public readonly record struct ChatMessageLoggedEvent(ChatEntry Entry) : IGameEvent;
	public readonly record struct ChatLobbyReadyRequestReceivedEvent(int PlayerId, byte[] Payload) : IGameEvent;
	public readonly record struct MatchCommandReceivedEvent(int PlayerId, string CommandId, byte[] Payload) : IGameEvent;
	public readonly record struct MatchCommandRejectedEvent(int PlayerId, string CommandId, string Reason) : IGameEvent;
	public readonly record struct GameStateMessageReceivedEvent(int PlayerId, string MessageId, byte[] Payload) : IGameEvent;
	public readonly record struct MatchPhaseChangedEvent(string PhaseId, MatchPhaseSettings Phase, MatchPayload Payload) : IGameEvent;
	public readonly record struct MatchPlayerJoinedEvent(MatchPlayer Player) : IGameEvent;
	public readonly record struct MatchPlayerDisconnectedEvent(MatchPlayer Player, ConnectionCloseReason Reason) : IGameEvent;
	public readonly record struct MatchStartedEvent(MatchPayload Payload) : IGameEvent;
	public readonly record struct MatchEndedEvent(MatchPayload Payload) : IGameEvent;
	public readonly record struct GameStateTransitionRequestedEvent(string ToStateId, byte[] Payload) : IGameEvent;
	public readonly record struct GameStateTransitionCompletedEvent(string FromStateId, string ToStateId) : IGameEvent;
	public readonly record struct GameStateTransitionFailedEvent(string ToStateId, string? FromStateId, string Reason) : IGameEvent;
	public readonly record struct SendPlayerMessageEvent(int PlayerId, PacketType PacketType, byte[] Payload, DeliveryOptions? Delivery = null) : IGameEvent;
	public readonly record struct BroadcastMessageEvent(PacketType PacketType, byte[] Payload, DeliveryOptions? Delivery = null) : IGameEvent;
}
