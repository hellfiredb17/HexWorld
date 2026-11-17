/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public readonly record struct ClientConnectedEvent(
		int PlayerId,
		string GameStateId,
		byte[] GameStatePayload) : IGameEvent;

	public readonly record struct ClientReconnectedEvent(
		int PlayerId,
		string GameStateId,
		byte[] GameStatePayload) : IGameEvent;

	public readonly record struct ClientDisconnectedEvent : IGameEvent;

	public readonly record struct ClientPacketReceivedEvent(
		PacketType PacketType,
		byte[] Payload) : IGameEvent;

	public readonly record struct ClientChatMessageReceivedEvent(ChatEntry Entry) : IGameEvent;

	public readonly record struct ClientChatHistoryReceivedEvent(IReadOnlyList<ChatEntry> History) : IGameEvent;

	public readonly record struct ClientDisplayNameChangedEvent(int PlayerId, string DisplayName) : IGameEvent;

	public readonly record struct ClientGameStateChangeReceivedEvent(
		string GameStateId,
		byte[] GameStatePayload) : IGameEvent;

	public readonly record struct ClientChatLobbyReadyStateReceivedEvent(
		ChatLobbyReadyStateResponse ReadyState) : IGameEvent;

	public readonly record struct ClientChatLobbyCountdownReceivedEvent(
		ChatLobbyCountdownResponse Countdown) : IGameEvent;

	public readonly record struct ClientChatLobbyPlayerStateReceivedEvent(
		ChatLobbyPlayerStateResponse PlayerState) : IGameEvent;

	public readonly record struct ClientMatchPhaseChangeReceivedEvent(
		MatchPhaseChangeResponse PhaseChange) : IGameEvent;

	public readonly record struct ClientGameStateMessageReceivedEvent(
		string MessageId,
		byte[] Payload) : IGameEvent;

	public readonly record struct ClientCompatibilityFailedEvent(
		ClientCompatibilityFailureReason Reason,
		string Message,
		string? ModuleId,
		string? DependencyId,
		string? RequestedVersion,
		string? SupportedVersion,
		ClientRuntimeManifest Manifest) : IGameEvent;

	public readonly record struct ClientHandshakeFailedEvent(string Message, Exception? Exception) : IGameEvent;

	public readonly record struct ClientStateEntryFailedEvent(
		string GameStateId,
		string Message,
		Exception? Exception) : IGameEvent;

	public readonly record struct ClientFaultedEvent(string Message, Exception? Exception) : IGameEvent;
}
