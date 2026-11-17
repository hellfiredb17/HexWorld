/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;
using System.Diagnostics;

namespace HawkNet.Core.Server
{
	public sealed record BroadcastSendResult(int Requested, int Succeeded, IReadOnlyDictionary<int, SendResult> Failures)
	{
		public bool Success => Failures.Count == 0;
	}

	/// <summary>
	/// Handles outbound server-to-client packet delivery.
	/// </summary>
	public class MessageDispatcher
	{
		//---- Variables
		//--------------
		private readonly ConnectionManager _connectionManager;
		private readonly ILogger _logger;
		private readonly ServerDiagnostics? _diagnostics;

		//---- Ctor
		//---------
		public MessageDispatcher(
			ConnectionManager connectionManager,
			ILogger? logger = null,
			ServerDiagnostics? diagnostics = null)
		{
			_connectionManager = connectionManager;
			_logger = logger ?? NullLogger.Instance;
			_diagnostics = diagnostics;
		}

		//---- Public
		//-----------
		public async Task<SendResult> SendToPlayerAsync(int playerId, PacketType packetType, byte[] payload, CancellationToken token = default)
		{
			return await SendToPlayerAsync(playerId, packetType, payload, PacketDeliveryPolicy.GetDefault(packetType), token);
		}

		public async Task<SendResult> SendToPlayerAsync(int playerId, PacketType packetType, byte[] payload, DeliveryOptions delivery, CancellationToken token = default)
		{
			long started = Stopwatch.GetTimestamp();
			byte[] packet = SignalShared.BuildPacket(packetType, payload);
			try
			{
				if(_connectionManager.TryGetConnectedConnection(playerId, out var connection))
				{
					SendResult result = await connection.SendAsync(packet, delivery, token);
					if (result.Success && result.TransportWritten)
					{
						_connectionManager.PacketCounters.RecordOutbound(packetType, packet.Length);
						_logger.DebugLog($"Sent packet to Player {playerId} type {packetType} size {payload.Length} bytes.", nameof(MessageDispatcher));
					}
					else if (!result.Success) _diagnostics?.RecordOutboundSendFailure();
					return result;
				}
				_diagnostics?.RecordOutboundSendFailure();
				return new SendResult(false, SendFailureReason.NotConnected);
			}
			finally
			{
				_diagnostics?.RecordOutboundSend(HawkNet.Core.TimeCompatibility.GetElapsedTime(started));
			}
		}

		public async Task<BroadcastSendResult> SendToPlayersAsync(IEnumerable<int> playerIds, PacketType packetType, byte[] payload, CancellationToken token = default)
		{
			return await SendToPlayersAsync(playerIds, packetType, payload, PacketDeliveryPolicy.GetDefault(packetType), token);
		}

		public async Task<BroadcastSendResult> SendToPlayersAsync(IEnumerable<int> playerIds, PacketType packetType, byte[] payload, DeliveryOptions delivery, CancellationToken token = default)
		{
			long started = Stopwatch.GetTimestamp();
			byte[] packet = SignalShared.BuildPacket(packetType, payload);
			try
			{
				int[] targets = playerIds.Distinct().ToArray();
				var failures = new Dictionary<int, SendResult>();
				Task<(int PlayerId, SendResult Result)>[] sends = targets.Select(async playerId =>
				{
					if(_connectionManager.TryGetConnectedConnection(playerId, out var connection))
					{
						return (playerId, await connection.SendAsync(packet, delivery, token));
					}
					return (playerId, new SendResult(false, SendFailureReason.NotConnected));
				}).ToArray();
				var results = await Task.WhenAll(sends);
				foreach (var item in results)
					if (item.Result.Success && item.Result.TransportWritten) _connectionManager.PacketCounters.RecordOutbound(packetType, packet.Length);
					else
					{
						if (!item.Result.Success)
						{
							failures[item.PlayerId] = item.Result;
							_diagnostics?.RecordOutboundSendFailure();
						}
					}

				_logger.DebugLog($"Sent packet to selected players type {packetType} size {payload.Length} bytes.", nameof(MessageDispatcher));
				return new BroadcastSendResult(targets.Length, targets.Length - failures.Count, failures);
			}
			finally
			{
				_diagnostics?.RecordOutboundSend(HawkNet.Core.TimeCompatibility.GetElapsedTime(started));
			}
		}

		public Task<BroadcastSendResult> BroadcastAsync(PacketType packetType, byte[] payload, CancellationToken token = default)
		{
			return SendToPlayersAsync(_connectionManager.GetConnectedConnections().Select(item => item.PlayerId), packetType, payload, token);
		}

		public Task<BroadcastSendResult> BroadcastAsync(PacketType packetType, byte[] payload, DeliveryOptions delivery, CancellationToken token = default)
		{
			return SendToPlayersAsync(_connectionManager.GetConnectedConnections().Select(item => item.PlayerId), packetType, payload, delivery, token);
		}
	}
}
