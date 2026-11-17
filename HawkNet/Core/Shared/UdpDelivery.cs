using System.Buffers.Binary;
using System.Diagnostics;

namespace HawkNet.Core.Shared;

public enum UdpDeliveryMode : byte
{
    ReliableOrdered = 0,
    UnreliableSequenced = 1
}

public enum UdpDeliveryChannel : byte
{
    Control = 0,
    Gameplay = 1,
    Snapshot = 2
}

public readonly record struct DeliveryOptions(UdpDeliveryMode Mode, UdpDeliveryChannel Channel)
{
    public static DeliveryOptions ReliableControl => new(UdpDeliveryMode.ReliableOrdered, UdpDeliveryChannel.Control);
    public static DeliveryOptions ReliableGameplay => new(UdpDeliveryMode.ReliableOrdered, UdpDeliveryChannel.Gameplay);
    public static DeliveryOptions UnreliableControl => new(UdpDeliveryMode.UnreliableSequenced, UdpDeliveryChannel.Control);
    public static DeliveryOptions UnreliableGameplay => new(UdpDeliveryMode.UnreliableSequenced, UdpDeliveryChannel.Gameplay);
    public static DeliveryOptions UnreliableSnapshot => new(UdpDeliveryMode.UnreliableSequenced, UdpDeliveryChannel.Snapshot);
}

public static class PacketDeliveryPolicy
{
    public static DeliveryOptions GetDefault(PacketType packetType) => packetType switch
    {
        PacketType.HeartBeat => DeliveryOptions.UnreliableControl,
        PacketType.PlayerInput => DeliveryOptions.UnreliableGameplay,
        PacketType.GameStateMessage => DeliveryOptions.ReliableGameplay,
        _ => DeliveryOptions.ReliableControl
    };
}

public sealed record UdpDeliverySettings
{
    public int MaxDatagramBytes { get; init; } = 1200;
    public int ReliableWindowSize { get; init; } = 32;
    public int ReorderCapacity { get; init; } = 32;
    public int InitialRetransmitMilliseconds { get; init; } = 250;
    public int MinimumRetransmitMilliseconds { get; init; } = 100;
    public int MaximumRetransmitMilliseconds { get; init; } = 2000;
    public int MaximumRetries { get; init; } = 6;
    public int AcknowledgementDelayMilliseconds { get; init; } = 20;
}

public static class UdpDeliveryConfiguration
{
    public static IReadOnlyList<string> Validate(UdpDeliverySettings? settings)
    {
        var errors = new List<string>();
        if (settings is null) { errors.Add("UdpDelivery settings are required."); return errors; }
        int minimumDatagram = UdpEnvelopeCodec.HeaderSize + SignalShared.LengthPrefixSize + SignalShared.HeaderSize;
        if (settings.MaxDatagramBytes < minimumDatagram || settings.MaxDatagramBytes > UdpEnvelopeCodec.DefaultMaxDatagramBytes)
            errors.Add($"UdpDelivery.MaxDatagramBytes must be between {minimumDatagram} and {UdpEnvelopeCodec.DefaultMaxDatagramBytes}.");
        if (settings.ReliableWindowSize is < 1 or > 32) errors.Add("UdpDelivery.ReliableWindowSize must be between 1 and 32.");
        if (settings.ReorderCapacity is < 1 or > 32) errors.Add("UdpDelivery.ReorderCapacity must be between 1 and 32.");
        if (settings.MinimumRetransmitMilliseconds < 1) errors.Add("UdpDelivery.MinimumRetransmitMilliseconds must be greater than zero.");
        if (settings.InitialRetransmitMilliseconds < settings.MinimumRetransmitMilliseconds) errors.Add("UdpDelivery.InitialRetransmitMilliseconds cannot be less than the minimum.");
        if (settings.MaximumRetransmitMilliseconds < settings.InitialRetransmitMilliseconds) errors.Add("UdpDelivery.MaximumRetransmitMilliseconds cannot be less than the initial value.");
        if (settings.MaximumRetries < 0) errors.Add("UdpDelivery.MaximumRetries cannot be negative.");
        if (settings.AcknowledgementDelayMilliseconds is < 1 or > 1000) errors.Add("UdpDelivery.AcknowledgementDelayMilliseconds must be between 1 and 1000.");
        if (settings.AcknowledgementDelayMilliseconds >= settings.InitialRetransmitMilliseconds) errors.Add("UdpDelivery.AcknowledgementDelayMilliseconds must be less than the initial retransmit timeout.");
        return errors;
    }
}

public interface IUdpDeliveryClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp);
    long GetUtcUnixMilliseconds();
    Task DelayAsync(TimeSpan delay, CancellationToken token);
}

public sealed class SystemUdpDeliveryClock : IUdpDeliveryClock
{
    public static SystemUdpDeliveryClock Instance { get; } = new();
    private SystemUdpDeliveryClock() { }
    public long GetTimestamp() => Stopwatch.GetTimestamp();
    public TimeSpan GetElapsedTime(long startingTimestamp) => HawkNet.Core.TimeCompatibility.GetElapsedTime(startingTimestamp);
    public long GetUtcUnixMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public Task DelayAsync(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token);
}

public readonly record struct UdpDeliveryMetricsSnapshot(
    long SentDatagrams,
    long ReceivedDatagrams,
    long ReliableAdmissions,
    long Acknowledgements,
    long Retransmissions,
    long RetryExhausted,
    long Duplicates,
    long StaleSequencedDrops,
    long ReorderedPackets,
    long ReorderOverflow,
    long SupersededSends,
    long AcknowledgementOnlyDatagrams,
    double SmoothedRoundTripMilliseconds,
    double RetransmitTimeoutMilliseconds,
    double JitterMilliseconds,
    int CurrentWindowOccupancy,
    int HighWaterWindowOccupancy)
{
    public static UdpDeliveryMetricsSnapshot Empty => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static UdpDeliveryMetricsSnapshot Aggregate(IEnumerable<UdpDeliveryMetricsSnapshot> snapshots)
    {
        UdpDeliveryMetricsSnapshot[] values = snapshots.ToArray();
        if (values.Length == 0) return Empty;
        return new UdpDeliveryMetricsSnapshot(
            values.Sum(v => v.SentDatagrams), values.Sum(v => v.ReceivedDatagrams),
            values.Sum(v => v.ReliableAdmissions), values.Sum(v => v.Acknowledgements),
            values.Sum(v => v.Retransmissions), values.Sum(v => v.RetryExhausted),
            values.Sum(v => v.Duplicates), values.Sum(v => v.StaleSequencedDrops),
            values.Sum(v => v.ReorderedPackets), values.Sum(v => v.ReorderOverflow),
            values.Sum(v => v.SupersededSends), values.Sum(v => v.AcknowledgementOnlyDatagrams),
            values.Average(v => v.SmoothedRoundTripMilliseconds), values.Average(v => v.RetransmitTimeoutMilliseconds),
            values.Average(v => v.JitterMilliseconds), values.Sum(v => v.CurrentWindowOccupancy),
            values.Sum(v => v.HighWaterWindowOccupancy));
    }
}

public interface IUdpDeliveryMetricsSource
{
    UdpDeliveryMetricsSnapshot GetUdpDeliveryMetrics();
}

internal interface IUdpDeliveryMetricsRecorder
{
    void RecordSupersededSend();
}

[Flags]
public enum UdpEnvelopeFlags : byte
{
    None = 0,
    HasPayload = 1,
    HasAcknowledgement = 2,
    Reliable = 4
}

public readonly record struct UdpEnvelope(
    UdpEnvelopeFlags Flags,
    UdpDeliveryChannel Channel,
    uint Sequence,
    uint Acknowledgement,
    uint AcknowledgementBits,
    long SentAtUnixMilliseconds,
    byte[] Payload)
{
    public bool HasPayload => (Flags & UdpEnvelopeFlags.HasPayload) != 0;
    public bool HasAcknowledgement => (Flags & UdpEnvelopeFlags.HasAcknowledgement) != 0;
    public bool IsReliable => (Flags & UdpEnvelopeFlags.Reliable) != 0;
    public UdpDeliveryMode Mode => IsReliable ? UdpDeliveryMode.ReliableOrdered : UdpDeliveryMode.UnreliableSequenced;
}

public static class UdpEnvelopeCodec
{
    public const byte EnvelopeVersion = 1;
    public const int HeaderSize = 27;
    public const int DefaultMaxDatagramBytes = 1200;
    public const int MaxFramedPacketBytes = DefaultMaxDatagramBytes - HeaderSize;
    public const int MaxApplicationPayloadBytes = MaxFramedPacketBytes - SignalShared.LengthPrefixSize - SignalShared.HeaderSize;

    public static byte[] Encode(UdpEnvelope envelope, int maxDatagramBytes = DefaultMaxDatagramBytes)
    {
        if (envelope.Payload is null) throw new ArgumentNullException(nameof(envelope.Payload));
        ValidateChannel(envelope.Channel);
        if (!envelope.HasPayload && envelope.Payload.Length != 0)
            throw new ArgumentException("An acknowledgement-only envelope cannot contain a payload.", nameof(envelope));
        if (envelope.HasPayload && envelope.Payload.Length == 0)
            throw new ArgumentException("A payload envelope cannot contain an empty payload.", nameof(envelope));
        if (envelope.IsReliable && !envelope.HasPayload)
            throw new ArgumentException("Only payload envelopes may set the reliable flag.", nameof(envelope));
        if (!envelope.HasAcknowledgement && (envelope.Acknowledgement != 0 || envelope.AcknowledgementBits != 0))
            throw new ArgumentException("Acknowledgement values require the acknowledgement flag.", nameof(envelope));
        if (!envelope.HasPayload && envelope.Sequence != 0)
            throw new ArgumentException("Acknowledgement-only envelopes must use sequence zero.", nameof(envelope));
        if (envelope.SentAtUnixMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(envelope), "UDP envelope timestamp must be positive.");
        int effectiveMaximum = Math.Min(maxDatagramBytes, DefaultMaxDatagramBytes);
        int length = HeaderSize + envelope.Payload.Length;
        if (length > effectiveMaximum)
            throw new ArgumentOutOfRangeException(nameof(envelope), $"UDP datagram length {length} exceeds the configured maximum {effectiveMaximum} bytes.");

        byte[] buffer = new byte[length];
        buffer[0] = (byte)'H';
        buffer[1] = (byte)'N';
        buffer[2] = EnvelopeVersion;
        buffer[3] = (byte)envelope.Flags;
        buffer[4] = (byte)envelope.Channel;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(5, 4), envelope.Sequence);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(9, 4), envelope.Acknowledgement);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(13, 4), envelope.AcknowledgementBits);
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(17, 8), envelope.SentAtUnixMilliseconds);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(25, 2), checked((ushort)envelope.Payload.Length));
        envelope.Payload.CopyTo(buffer, HeaderSize);
        return buffer;
    }

    public static bool TryDecode(byte[] datagram, out UdpEnvelope envelope, out string error, int maxDatagramBytes = DefaultMaxDatagramBytes)
    {
        envelope = default;
        error = string.Empty;
        if (datagram is null) { error = "UDP datagram cannot be null."; return false; }
        if (datagram.Length < HeaderSize) { error = $"UDP datagram is shorter than the {HeaderSize}-byte envelope header."; return false; }
        int effectiveMaximum = Math.Min(maxDatagramBytes, DefaultMaxDatagramBytes);
        if (datagram.Length > effectiveMaximum) { error = $"UDP datagram exceeds the configured maximum {effectiveMaximum} bytes."; return false; }
        if (datagram[0] != (byte)'H' || datagram[1] != (byte)'N') { error = "UDP envelope magic is invalid."; return false; }
        if (datagram[2] != EnvelopeVersion) { error = $"UDP envelope version {datagram[2]} is unsupported."; return false; }
        const UdpEnvelopeFlags validFlags = UdpEnvelopeFlags.HasPayload | UdpEnvelopeFlags.HasAcknowledgement | UdpEnvelopeFlags.Reliable;
        UdpEnvelopeFlags flags = (UdpEnvelopeFlags)datagram[3];
        if ((flags & ~validFlags) != 0) { error = "UDP envelope contains unsupported flags."; return false; }
        if ((flags & (UdpEnvelopeFlags.HasPayload | UdpEnvelopeFlags.HasAcknowledgement)) == 0) { error = "UDP envelope contains neither payload nor acknowledgement."; return false; }
        if ((flags & UdpEnvelopeFlags.Reliable) != 0 && (flags & UdpEnvelopeFlags.HasPayload) == 0) { error = "Only payload envelopes may set the reliable flag."; return false; }
        var channel = (UdpDeliveryChannel)datagram[4];
        if (!Enum.IsDefined(typeof(UdpDeliveryChannel), channel)) { error = $"UDP delivery channel {datagram[4]} is invalid."; return false; }
        ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(datagram.AsSpan(25, 2));
        if (datagram.Length != HeaderSize + payloadLength) { error = "UDP envelope payload length does not match the datagram length."; return false; }
        bool hasPayload = (flags & UdpEnvelopeFlags.HasPayload) != 0;
        if (hasPayload != (payloadLength > 0)) { error = "UDP envelope payload flag does not match its payload length."; return false; }
        uint sequence = BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(5, 4));
        uint acknowledgement = BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(9, 4));
        uint acknowledgementBits = BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(13, 4));
        long sentAt = BinaryPrimitives.ReadInt64BigEndian(datagram.AsSpan(17, 8));
        bool hasAcknowledgement = (flags & UdpEnvelopeFlags.HasAcknowledgement) != 0;
        if (!hasAcknowledgement && (acknowledgement != 0 || acknowledgementBits != 0)) { error = "Acknowledgement values require the acknowledgement flag."; return false; }
        if (!hasPayload && sequence != 0) { error = "Acknowledgement-only envelopes must use sequence zero."; return false; }
        if (sentAt <= 0) { error = "UDP envelope timestamp must be positive."; return false; }

        envelope = new UdpEnvelope(
            flags, channel,
            sequence, acknowledgement, acknowledgementBits, sentAt,
            datagram.AsSpan(HeaderSize, payloadLength).ToArray());
        return true;
    }

    public static bool IsNewerSequence(uint candidate, uint baseline) => unchecked((int)(candidate - baseline)) > 0;

    private static void ValidateChannel(UdpDeliveryChannel channel)
    {
        if (!Enum.IsDefined(typeof(UdpDeliveryChannel), channel))
            throw new ArgumentOutOfRangeException(nameof(channel));
    }
}

internal sealed class UdpDeliveryMetrics
{
    public long SentDatagrams, ReceivedDatagrams, ReliableAdmissions, Acknowledgements, Retransmissions,
        RetryExhausted, Duplicates, StaleSequencedDrops, ReorderedPackets, ReorderOverflow,
        SupersededSends, AcknowledgementOnlyDatagrams;
    public int CurrentWindowOccupancy, HighWaterWindowOccupancy;
    public double SmoothedRtt, Rto, Jitter;
    private readonly object _lock = new();

    public void SetTiming(double srtt, double rto, double jitter)
    {
        lock (_lock) { SmoothedRtt = srtt; Rto = rto; Jitter = jitter; }
    }

    public UdpDeliveryMetricsSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new(
                Interlocked.Read(ref SentDatagrams), Interlocked.Read(ref ReceivedDatagrams),
                Interlocked.Read(ref ReliableAdmissions), Interlocked.Read(ref Acknowledgements),
                Interlocked.Read(ref Retransmissions), Interlocked.Read(ref RetryExhausted),
                Interlocked.Read(ref Duplicates), Interlocked.Read(ref StaleSequencedDrops),
                Interlocked.Read(ref ReorderedPackets), Interlocked.Read(ref ReorderOverflow),
                Interlocked.Read(ref SupersededSends), Interlocked.Read(ref AcknowledgementOnlyDatagrams),
                SmoothedRtt, Rto, Jitter, Volatile.Read(ref CurrentWindowOccupancy), Volatile.Read(ref HighWaterWindowOccupancy));
        }
    }
}
