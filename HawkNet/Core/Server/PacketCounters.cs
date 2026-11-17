/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using System.Collections.Concurrent;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	public readonly record struct PacketCounterSnapshot(
		long InboundPackets,
		long InboundBytes,
		double AverageInboundPacketBytes,
		int MaxInboundPacketBytes,
		long OutboundPackets,
		long OutboundBytes,
		double AverageOutboundPacketBytes,
		int MaxOutboundPacketBytes);

	public readonly record struct PacketTypeCounterSnapshot(
		PacketType PacketType,
		long InboundPackets,
		long InboundBytes,
		double AverageInboundPacketBytes,
		int MaxInboundPacketBytes,
		long OutboundPackets,
		long OutboundBytes,
		double AverageOutboundPacketBytes,
		int MaxOutboundPacketBytes);

	public sealed class PacketCounters
	{
		//---- Variables
		//--------------
		private readonly ConcurrentDictionary<PacketType, PacketCounterBucket> _countersByType = new();
		private long _inboundPackets;
		private long _inboundBytes;
		private int _maxInboundPacketBytes;
		private long _outboundPackets;
		private long _outboundBytes;
		private int _maxOutboundPacketBytes;

		//---- Public
		//-----------
		public void RecordInbound(PacketType packetType, int byteCount)
		{
			Record(packetType, Math.Max(0, byteCount), isInbound: true);
		}

		public void RecordOutbound(PacketType packetType, int byteCount)
		{
			Record(packetType, Math.Max(0, byteCount), isInbound: false);
		}

		public PacketCounterSnapshot GetSnapshot()
		{
			return new PacketCounterSnapshot(
				Interlocked.Read(ref _inboundPackets),
				Interlocked.Read(ref _inboundBytes),
				GetAverage(Interlocked.Read(ref _inboundBytes), Interlocked.Read(ref _inboundPackets)),
				Volatile.Read(ref _maxInboundPacketBytes),
				Interlocked.Read(ref _outboundPackets),
				Interlocked.Read(ref _outboundBytes),
				GetAverage(Interlocked.Read(ref _outboundBytes), Interlocked.Read(ref _outboundPackets)),
				Volatile.Read(ref _maxOutboundPacketBytes));
		}

		public PacketTypeCounterSnapshot GetSnapshot(PacketType packetType)
		{
			if(!_countersByType.TryGetValue(packetType, out var bucket))
			{
				return new PacketTypeCounterSnapshot(packetType, 0, 0, 0, 0, 0, 0, 0, 0);
			}

			return bucket.GetSnapshot(packetType);
		}

		public IReadOnlyCollection<PacketTypeCounterSnapshot> GetSnapshotsByType()
		{
			return _countersByType
				.Select(pair => pair.Value.GetSnapshot(pair.Key))
				.OrderBy(snapshot => snapshot.PacketType)
				.ToArray();
		}

		//---- Private
		//------------
		private void Record(PacketType packetType, int byteCount, bool isInbound)
		{
			var bucket = _countersByType.GetOrAdd(packetType, _ => new PacketCounterBucket());
			if(isInbound)
			{
				Interlocked.Increment(ref _inboundPackets);
				Interlocked.Add(ref _inboundBytes, byteCount);
				UpdateMax(ref _maxInboundPacketBytes, byteCount);
				bucket.RecordInbound(byteCount);
				return;
			}

			Interlocked.Increment(ref _outboundPackets);
			Interlocked.Add(ref _outboundBytes, byteCount);
			UpdateMax(ref _maxOutboundPacketBytes, byteCount);
			bucket.RecordOutbound(byteCount);
		}

		private static double GetAverage(long bytes, long packets)
		{
			if(packets == 0)
			{
				return 0;
			}

			return (double)bytes / packets;
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

		private sealed class PacketCounterBucket
		{
			private long _inboundPackets;
			private long _inboundBytes;
			private int _maxInboundPacketBytes;
			private long _outboundPackets;
			private long _outboundBytes;
			private int _maxOutboundPacketBytes;

			public void RecordInbound(int byteCount)
			{
				Interlocked.Increment(ref _inboundPackets);
				Interlocked.Add(ref _inboundBytes, byteCount);
				UpdateMax(ref _maxInboundPacketBytes, byteCount);
			}

			public void RecordOutbound(int byteCount)
			{
				Interlocked.Increment(ref _outboundPackets);
				Interlocked.Add(ref _outboundBytes, byteCount);
				UpdateMax(ref _maxOutboundPacketBytes, byteCount);
			}

			public PacketTypeCounterSnapshot GetSnapshot(PacketType packetType)
			{
				return new PacketTypeCounterSnapshot(
					packetType,
					Interlocked.Read(ref _inboundPackets),
					Interlocked.Read(ref _inboundBytes),
					GetAverage(Interlocked.Read(ref _inboundBytes), Interlocked.Read(ref _inboundPackets)),
					Volatile.Read(ref _maxInboundPacketBytes),
					Interlocked.Read(ref _outboundPackets),
					Interlocked.Read(ref _outboundBytes),
					GetAverage(Interlocked.Read(ref _outboundBytes), Interlocked.Read(ref _outboundPackets)),
					Volatile.Read(ref _maxOutboundPacketBytes));
			}
		}
	}
}
