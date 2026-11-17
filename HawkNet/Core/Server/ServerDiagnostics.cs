/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using System.Collections.Concurrent;
using System.Diagnostics;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	public enum ChatDiagnosticsRejectionReason
	{
		Empty,
		ByteLimit,
		RateLimit
	}

	public readonly record struct TimingDiagnosticsSnapshot(
		long Count,
		double AverageMilliseconds,
		double MaxMilliseconds);

	public readonly record struct ChatDiagnosticsSnapshot(
		long AcceptedMessages,
		long EmptyMessagesIgnored,
		long ByteLimitRejectedMessages,
		long RateLimitRejectedMessages,
		long PayloadBytes,
		double AveragePayloadBytes,
		int MaxPayloadBytes,
		TimingDiagnosticsSnapshot Processing);

	public readonly record struct ConnectionCloseReasonDiagnosticsSnapshot(
		ConnectionCloseReason Reason,
		long Count);

	public readonly record struct ServerDiagnosticsSnapshot(
		DateTime TimestampUtc,
		TimeSpan Uptime,
		int SessionCount,
		int ConnectedPlayerCount,
		PacketCounterSnapshot PacketTotals,
		IReadOnlyCollection<PacketTypeCounterSnapshot> PacketTypes,
		ChatDiagnosticsSnapshot Chat,
		TimingDiagnosticsSnapshot InboundRoute,
		TimingDiagnosticsSnapshot OutboundSend,
		IReadOnlyCollection<ConnectionCloseReasonDiagnosticsSnapshot> Disconnects,
		long MalformedPacketReads,
		long OutboundSendFailures,
		UpdateLoopMetricsSnapshot UpdateLoop,
		UdpDeliveryMetricsSnapshot UdpDelivery = default);

	public sealed class ServerDiagnostics
	{
		//---- Variables
		//--------------
		private readonly DiagnosticsSettings _settings;
		private readonly ILogger _logger;
		private readonly Func<int> _getSessionCount;
		private readonly Func<int> _getConnectedPlayerCount;
		private readonly Func<PacketCounterSnapshot> _getPacketTotals;
		private readonly Func<IReadOnlyCollection<PacketTypeCounterSnapshot>> _getPacketTypes;
		private readonly Func<UpdateLoopMetricsSnapshot> _getUpdateLoopMetrics;
		private readonly Func<UdpDeliveryMetricsSnapshot> _getUdpDeliveryMetrics;
		private readonly DateTime _startedAtUtc = DateTime.UtcNow;
		private readonly TimingBucket _inboundRouteTiming = new();
		private readonly TimingBucket _outboundSendTiming = new();
		private readonly TimingBucket _chatProcessingTiming = new();
		private readonly ConcurrentDictionary<ConnectionCloseReason, long> _disconnectsByReason = new();
		private CancellationTokenSource? _logCts;
		private Task? _logTask;
		private long _acceptedChatMessages;
		private long _emptyChatMessagesIgnored;
		private long _byteLimitRejectedChatMessages;
		private long _rateLimitRejectedChatMessages;
		private long _chatPayloadBytes;
		private long _malformedPacketReads;
		private long _outboundSendFailures;
		private int _maxChatPayloadBytes;

		//---- Properties
		//---------------
		public int LogIntervalSeconds => Math.Max(1, _settings.LogIntervalSeconds);

		//---- Ctor
		//---------
		public ServerDiagnostics(
			DiagnosticsSettings settings,
			ILogger? logger = null,
			Func<int>? getSessionCount = null,
			Func<int>? getConnectedPlayerCount = null,
			Func<PacketCounterSnapshot>? getPacketTotals = null,
			Func<IReadOnlyCollection<PacketTypeCounterSnapshot>>? getPacketTypes = null,
			Func<UpdateLoopMetricsSnapshot>? getUpdateLoopMetrics = null,
			Func<UdpDeliveryMetricsSnapshot>? getUdpDeliveryMetrics = null)
		{
			_settings = settings;
			_logger = logger ?? NullLogger.Instance;
			_getSessionCount = getSessionCount ?? (() => 0);
			_getConnectedPlayerCount = getConnectedPlayerCount ?? (() => 0);
			_getPacketTotals = getPacketTotals ?? (() => default);
			_getPacketTypes = getPacketTypes ?? (() => Array.Empty<PacketTypeCounterSnapshot>());
			_getUpdateLoopMetrics = getUpdateLoopMetrics ?? (() => default);
			_getUdpDeliveryMetrics = getUdpDeliveryMetrics ?? (() => default);
		}

		//---- Public
		//-----------
		public Task StartAsync()
		{
			if(!_settings.EnableConsoleLogging || _logTask is { IsCompleted: false })
			{
				return Task.CompletedTask;
			}

			_logCts = new CancellationTokenSource();
			_logTask = Task.Run(() => RunLoggingLoopAsync(_logCts.Token));
			return Task.CompletedTask;
		}

		public async Task StopAsync()
		{
			var logCts = _logCts;
			var logTask = _logTask;
			_logCts = null;
			_logTask = null;

			if(logCts is null)
			{
				return;
			}

			logCts.Cancel();
			try
			{
				if(logTask is not null)
				{
					await logTask;
				}
			}
			catch(OperationCanceledException)
			{
				// Expected during shutdown.
			}
			finally
			{
				logCts.Dispose();
			}
		}

		public ServerDiagnosticsSnapshot GetSnapshot()
		{
			long acceptedMessages = Interlocked.Read(ref _acceptedChatMessages);
			long payloadBytes = Interlocked.Read(ref _chatPayloadBytes);
			var chat = new ChatDiagnosticsSnapshot(
				acceptedMessages,
				Interlocked.Read(ref _emptyChatMessagesIgnored),
				Interlocked.Read(ref _byteLimitRejectedChatMessages),
				Interlocked.Read(ref _rateLimitRejectedChatMessages),
				payloadBytes,
				acceptedMessages == 0 ? 0 : (double)payloadBytes / acceptedMessages,
				Volatile.Read(ref _maxChatPayloadBytes),
				_chatProcessingTiming.GetSnapshot());

			return new ServerDiagnosticsSnapshot(
				DateTime.UtcNow,
				DateTime.UtcNow - _startedAtUtc,
				_getSessionCount(),
				_getConnectedPlayerCount(),
				_getPacketTotals(),
				_getPacketTypes(),
				chat,
				_inboundRouteTiming.GetSnapshot(),
				_outboundSendTiming.GetSnapshot(),
				GetDisconnectSnapshots(),
				Interlocked.Read(ref _malformedPacketReads),
				Interlocked.Read(ref _outboundSendFailures),
				_getUpdateLoopMetrics(),
				_getUdpDeliveryMetrics());
		}

		public void RecordInboundRoute(TimeSpan duration)
		{
			_inboundRouteTiming.Record(duration);
		}

		public void RecordOutboundSend(TimeSpan duration)
		{
			_outboundSendTiming.Record(duration);
		}

		public void RecordOutboundSendFailure()
		{
			Interlocked.Increment(ref _outboundSendFailures);
		}

		public void RecordChatAccepted(int payloadBytes, TimeSpan duration)
		{
			int safePayloadBytes = Math.Max(0, payloadBytes);
			Interlocked.Increment(ref _acceptedChatMessages);
			Interlocked.Add(ref _chatPayloadBytes, safePayloadBytes);
			UpdateMax(ref _maxChatPayloadBytes, safePayloadBytes);
			_chatProcessingTiming.Record(duration);
		}

		public void RecordChatRejected(ChatDiagnosticsRejectionReason reason, int payloadBytes, TimeSpan duration)
		{
			switch(reason)
			{
				case ChatDiagnosticsRejectionReason.Empty:
					Interlocked.Increment(ref _emptyChatMessagesIgnored);
					break;
				case ChatDiagnosticsRejectionReason.ByteLimit:
					Interlocked.Increment(ref _byteLimitRejectedChatMessages);
					break;
				case ChatDiagnosticsRejectionReason.RateLimit:
					Interlocked.Increment(ref _rateLimitRejectedChatMessages);
					break;
			}

			_chatProcessingTiming.Record(duration);
		}

		public void RecordDisconnect(ConnectionCloseReason reason)
		{
			_disconnectsByReason.AddOrUpdate(reason, 1, (_, count) => count + 1);
		}

		public void RecordMalformedPacketRead()
		{
			Interlocked.Increment(ref _malformedPacketReads);
		}

		//---- Private
		//------------
		private async Task RunLoggingLoopAsync(CancellationToken token)
		{
			while(!token.IsCancellationRequested)
			{
				LogSnapshot(GetSnapshot());
				await Task.Delay(TimeSpan.FromSeconds(LogIntervalSeconds), token);
			}
		}

		private void LogSnapshot(ServerDiagnosticsSnapshot snapshot)
		{
			_logger.Log(
				$"Uptime={snapshot.Uptime:c}; Sessions={snapshot.SessionCount}; Connected={snapshot.ConnectedPlayerCount}; " +
				$"PacketsIn={snapshot.PacketTotals.InboundPackets}/{snapshot.PacketTotals.InboundBytes}B; " +
				$"PacketsOut={snapshot.PacketTotals.OutboundPackets}/{snapshot.PacketTotals.OutboundBytes}B; " +
				$"MalformedPackets={snapshot.MalformedPacketReads}; " +
				$"ChatAccepted={snapshot.Chat.AcceptedMessages}; ChatRejected={snapshot.Chat.EmptyMessagesIgnored + snapshot.Chat.ByteLimitRejectedMessages + snapshot.Chat.RateLimitRejectedMessages}; " +
				$"InboundRouteAvg={snapshot.InboundRoute.AverageMilliseconds:0.###}ms; OutboundSendAvg={snapshot.OutboundSend.AverageMilliseconds:0.###}ms; " +
				$"TickAvg={snapshot.UpdateLoop.AverageTickDurationMilliseconds:0.###}ms; " +
				$"UdpWindow={snapshot.UdpDelivery.CurrentWindowOccupancy}/{snapshot.UdpDelivery.HighWaterWindowOccupancy}; " +
				$"UdpRetries={snapshot.UdpDelivery.Retransmissions}; UdpRetryFailures={snapshot.UdpDelivery.RetryExhausted}.",
				nameof(ServerDiagnostics));
		}

		private IReadOnlyCollection<ConnectionCloseReasonDiagnosticsSnapshot> GetDisconnectSnapshots()
		{
			return _disconnectsByReason
				.Select(pair => new ConnectionCloseReasonDiagnosticsSnapshot(pair.Key, pair.Value))
				.OrderBy(snapshot => snapshot.Reason)
				.ToArray();
		}

		private static void UpdateMax(ref int target, int value)
		{
			int current;
			do
			{
				current = Volatile.Read(ref target);
				if(value <= current)
				{
					return;
				}
			}
			while(Interlocked.CompareExchange(ref target, value, current) != current);
		}

		private sealed class TimingBucket
		{
			private long _count;
			private long _totalTimestampTicks;
			private long _maxTimestampTicks;

			public void Record(TimeSpan duration)
			{
				long timestampTicks = Math.Max(0, duration.Ticks) * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
				Interlocked.Increment(ref _count);
				Interlocked.Add(ref _totalTimestampTicks, timestampTicks);
				UpdateMax(ref _maxTimestampTicks, timestampTicks);
			}

			public TimingDiagnosticsSnapshot GetSnapshot()
			{
				long count = Interlocked.Read(ref _count);
				long total = Interlocked.Read(ref _totalTimestampTicks);
				double average = count == 0 ? 0 : ToMilliseconds(total) / count;
				return new TimingDiagnosticsSnapshot(
					count,
					average,
					ToMilliseconds(Interlocked.Read(ref _maxTimestampTicks)));
			}

			private static void UpdateMax(ref long target, long value)
			{
				long current;
				do
				{
					current = Interlocked.Read(ref target);
					if(value <= current)
					{
						return;
					}
				}
				while(Interlocked.CompareExchange(ref target, value, current) != current);
			}

			private static double ToMilliseconds(long timestampTicks)
			{
				return timestampTicks * 1000.0 / Stopwatch.Frequency;
			}
		}
	}
}
