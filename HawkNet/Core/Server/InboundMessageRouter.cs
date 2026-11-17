/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;
using System.Diagnostics;

namespace HawkNet.Core.Server
{
	/// <summary>
	/// Routes client-originated gameplay packets to server events.
	/// </summary>
	public class InboundMessageRouter
	{
		//---- Variables
		//--------------
		private readonly IEventDispatcher _eventDispatcher;
		private readonly ILogger _logger;
		private readonly ServerDiagnostics? _diagnostics;
		private readonly IPayloadCodec _payloadCodec;

		//---- Ctor
		//---------
		public InboundMessageRouter(
			IEventDispatcher eventDispatcher,
			ILogger? logger = null,
			ServerDiagnostics? diagnostics = null,
			IPayloadCodec? payloadCodec = null)
		{
			_eventDispatcher = eventDispatcher;
			_logger = logger ?? NullLogger.Instance;
			_diagnostics = diagnostics;
			_payloadCodec = payloadCodec ?? new JsonPayloadCodec();
		}

		//---- Public
		//-----------
		public async Task<bool> RouteAsync(int playerId, PacketType packetType, byte[] payload, CancellationToken token = default)
		{
			long started = Stopwatch.GetTimestamp();
			try
			{
				switch(packetType)
				{
					case PacketType.ChatMessage:
						await _eventDispatcher.DispatchAsync(new ChatMessageReceivedEvent(playerId, payload), token);
						return true;
					case PacketType.PlayerInput:
						await _eventDispatcher.DispatchAsync(new PlayerInputReceivedEvent(playerId, payload), token);
						return true;
					case PacketType.DisplayNameUpdate:
						await _eventDispatcher.DispatchAsync(new DisplayNameUpdateReceivedEvent(playerId, payload), token);
						return true;
					case PacketType.ChatLobbyReadyRequest:
						await _eventDispatcher.DispatchAsync(new ChatLobbyReadyRequestReceivedEvent(playerId, payload), token);
						return true;
					case PacketType.MatchCommandRequest:
						return await RouteMatchCommandAsync(playerId, payload, token);
					case PacketType.GameStateMessage:
						return await RouteGameStateMessageAsync(playerId, payload, token);
					case PacketType.HeartBeat:
					case PacketType.ReconnectRequest:
						return false;
					default:
						_logger.Warning($"Unsupported client packet type {packetType}.", nameof(InboundMessageRouter));
						return false;
				}
			}
			finally
			{
				_diagnostics?.RecordInboundRoute(HawkNet.Core.TimeCompatibility.GetElapsedTime(started));
			}
		}

		private async Task<bool> RouteMatchCommandAsync(int playerId, byte[] payload, CancellationToken token)
		{
			if(!_payloadCodec.TryDecode(payload, out MatchCommandRequest request))
			{
				await _eventDispatcher.DispatchAsync(
					new MatchCommandRejectedEvent(playerId, string.Empty, "Invalid match command request payload."),
					token);
				return false;
			}

			string commandId = request.CommandId.Trim();
			if(string.IsNullOrWhiteSpace(commandId))
			{
				await _eventDispatcher.DispatchAsync(
					new MatchCommandRejectedEvent(playerId, commandId, "Match command id cannot be empty."),
					token);
				return false;
			}

			await _eventDispatcher.DispatchAsync(
				new MatchCommandReceivedEvent(playerId, commandId, request.Payload.ToArray()),
				token);
			return true;
		}

		private async Task<bool> RouteGameStateMessageAsync(int playerId, byte[] payload, CancellationToken token)
		{
			if(!_payloadCodec.TryDecode(payload, out GameStateMessage message))
			{
				return false;
			}

			string messageId = message.MessageId.Trim();
			if(string.IsNullOrWhiteSpace(messageId))
			{
				return false;
			}

			await _eventDispatcher.DispatchAsync(
				new GameStateMessageReceivedEvent(playerId, messageId, message.Payload.ToArray()),
				token);
			return true;
		}
	}
}
