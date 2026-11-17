using HawkNet.Core.Shared;
using System.Collections.Concurrent;
using Xunit;

namespace HawkNet.Tests;

public sealed class UdpDeliveryTests
{
    [Fact]
    public void UdpWireUpgrade_UsesHandshakeProtocolVersionThree()
    {
        Assert.Equal("3", HandshakeProtocol.CurrentProtocolVersion);
    }

    [Fact]
    public void EnvelopeCodec_RoundTripsEveryFieldAndTimestamp()
    {
        byte[] framed = SignalShared.BuildPacket(PacketType.ChatMessage, new byte[] { 1, 2, 3 });
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var source = new UdpEnvelope(
            UdpEnvelopeFlags.HasPayload | UdpEnvelopeFlags.HasAcknowledgement | UdpEnvelopeFlags.Reliable,
            UdpDeliveryChannel.Gameplay, uint.MaxValue, 41, 0xA5A5A5A5, timestamp, framed);

        byte[] encoded = UdpEnvelopeCodec.Encode(source);
        bool success = UdpEnvelopeCodec.TryDecode(encoded, out UdpEnvelope decoded, out string error);

        Assert.True(success, error);
        Assert.Equal(source.Flags, decoded.Flags);
        Assert.Equal(source.Channel, decoded.Channel);
        Assert.Equal(source.Sequence, decoded.Sequence);
        Assert.Equal(source.Acknowledgement, decoded.Acknowledgement);
        Assert.Equal(source.AcknowledgementBits, decoded.AcknowledgementBits);
        Assert.Equal(framed, decoded.Payload);
        Assert.InRange(decoded.SentAtUnixMilliseconds, timestamp, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        ReadNetworkPacketResult packet = SignalShared.ReadNetworkDatagram(decoded.Payload);
        Assert.True(packet.Success, packet.ErrorMsg);
        Assert.True(packet.SentAtUnixMilliseconds > 0);
    }

    [Fact]
    public void EnvelopeCodec_RejectsMalformedHeadersAndOversizedDatagrams()
    {
        byte[] valid = UdpEnvelopeCodec.Encode(new UdpEnvelope(
            UdpEnvelopeFlags.HasAcknowledgement, UdpDeliveryChannel.Control, 0, 1, 0,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Array.Empty<byte>()));

        Assert.False(UdpEnvelopeCodec.TryDecode(valid[..10], out _, out _));
        valid[0] = (byte)'X';
        Assert.False(UdpEnvelopeCodec.TryDecode(valid, out _, out _));
        byte[] badVersion = UdpEnvelopeCodec.Encode(new UdpEnvelope(UdpEnvelopeFlags.HasAcknowledgement, UdpDeliveryChannel.Control, 0, 1, 0, 1, Array.Empty<byte>()));
        badVersion[2]++;
        Assert.False(UdpEnvelopeCodec.TryDecode(badVersion, out _, out _));
        byte[] badFlags = badVersion.ToArray(); badFlags[2] = UdpEnvelopeCodec.EnvelopeVersion; badFlags[3] = 0xFF;
        Assert.False(UdpEnvelopeCodec.TryDecode(badFlags, out _, out _));
        byte[] badChannel = badVersion.ToArray(); badChannel[2] = UdpEnvelopeCodec.EnvelopeVersion; badChannel[4] = 0xFF;
        Assert.False(UdpEnvelopeCodec.TryDecode(badChannel, out _, out _));
        byte[] badLength = badVersion.ToArray(); badLength[2] = UdpEnvelopeCodec.EnvelopeVersion; badLength[26] = 1;
        Assert.False(UdpEnvelopeCodec.TryDecode(badLength, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => UdpEnvelopeCodec.Encode(new UdpEnvelope(
            UdpEnvelopeFlags.HasPayload, UdpDeliveryChannel.Snapshot, 1, 0, 0, 1,
            new byte[UdpEnvelopeCodec.MaxFramedPacketBytes + 1])));
    }

    [Theory]
    [InlineData(0u, uint.MaxValue, true)]
    [InlineData(uint.MaxValue, 0u, false)]
    [InlineData(2u, 1u, true)]
    [InlineData(1u, 2u, false)]
    public void SequenceComparison_IsWrapSafe(uint candidate, uint baseline, bool expected)
    {
        Assert.Equal(expected, UdpEnvelopeCodec.IsNewerSequence(candidate, baseline));
    }

    [Fact]
    public void PacketPolicy_UsesSafeDefaults()
    {
        foreach (PacketType type in Enum.GetValues<PacketType>())
        {
            DeliveryOptions actual = PacketDeliveryPolicy.GetDefault(type);
            DeliveryOptions expected = type switch
            {
                PacketType.HeartBeat => DeliveryOptions.UnreliableControl,
                PacketType.PlayerInput => DeliveryOptions.UnreliableGameplay,
                PacketType.GameStateMessage => DeliveryOptions.ReliableGameplay,
                _ => DeliveryOptions.ReliableControl
            };
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void DeliverySettings_ValidateBoundsAndTimingRelationships()
    {
        Assert.Empty(UdpDeliveryConfiguration.Validate(new UdpDeliverySettings()));
        IReadOnlyList<string> errors = UdpDeliveryConfiguration.Validate(new UdpDeliverySettings
        {
            MaxDatagramBytes = 1201,
            ReliableWindowSize = 33,
            ReorderCapacity = 0,
            MinimumRetransmitMilliseconds = 200,
            InitialRetransmitMilliseconds = 100,
            MaximumRetransmitMilliseconds = 50,
            AcknowledgementDelayMilliseconds = 100
        });

        Assert.True(errors.Count >= 5);
        Assert.Contains(errors, error => error.Contains("MaxDatagramBytes"));
        Assert.Contains(errors, error => error.Contains("ReliableWindowSize"));
        Assert.Contains(errors, error => error.Contains("ReorderCapacity"));
    }

    [Fact]
    public async Task ReliableOrdered_DeliversExactlyOnceInOrderAfterReorderingAndDuplication()
    {
        var sent = new List<byte[]>();
        var gate = new object();
        UdpDeliveryPeer? sender = null;
        await using var receiver = new UdpDeliveryPeer(FastSettings(), (datagram, _) =>
        {
            sender!.ProcessDatagram(datagram);
            return Task.CompletedTask;
        });
        await using var ownedSender = sender = new UdpDeliveryPeer(FastSettings(), (datagram, _) =>
        {
            lock (gate) sent.Add(datagram);
            return Task.CompletedTask;
        });

        await sender.SendAsync(new byte[] { 1 }, DeliveryOptions.ReliableGameplay);
        await sender.SendAsync(new byte[] { 2 }, DeliveryOptions.ReliableGameplay);
        byte[][] initial;
        lock (gate) initial = sent.Take(2).ToArray();
        receiver.ProcessDatagram(initial[1]);
        receiver.ProcessDatagram(initial[1]);
        receiver.ProcessDatagram(initial[0]);

        Assert.Equal(new byte[] { 1 }, await receiver.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(new byte[] { 2 }, await receiver.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1)));
        await WaitUntilAsync(() => sender.GetUdpDeliveryMetrics().Acknowledgements == 2);
        Assert.True(receiver.GetUdpDeliveryMetrics().Duplicates >= 1);
        Assert.Equal(0, sender.GetUdpDeliveryMetrics().CurrentWindowOccupancy);
    }

    [Fact]
    public async Task UnreliableSequenced_NeverDeliversOlderPacketAfterNewerPacket()
    {
        var sent = new List<byte[]>();
        await using var sender = new UdpDeliveryPeer(FastSettings(), (datagram, _) => { sent.Add(datagram); return Task.CompletedTask; });
        await using var receiver = new UdpDeliveryPeer(FastSettings(), (_, _) => Task.CompletedTask);

        await sender.SendAsync(new byte[] { 1 }, DeliveryOptions.UnreliableSnapshot);
        await sender.SendAsync(new byte[] { 2 }, DeliveryOptions.UnreliableSnapshot);
        receiver.ProcessDatagram(sent[1]);
        receiver.ProcessDatagram(sent[0]);

        Assert.Equal(new byte[] { 2 }, await receiver.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, receiver.GetUdpDeliveryMetrics().StaleSequencedDrops);
    }

    [Fact]
    public async Task ReliableWindow_WaitsForAcknowledgementBeforeAdmittingMore()
    {
        UdpDeliverySettings settings = FastSettings() with { ReliableWindowSize = 1 };
        UdpDeliveryPeer? sender = null;
        UdpDeliveryPeer? receiver = null;
        sender = new UdpDeliveryPeer(settings, (datagram, _) => { receiver!.ProcessDatagram(datagram); return Task.CompletedTask; });
        receiver = new UdpDeliveryPeer(settings, (datagram, _) => { sender.ProcessDatagram(datagram); return Task.CompletedTask; });
        await using var senderOwner = sender;
        await using var receiverOwner = receiver;

        await sender.SendAsync(new byte[] { 1 }, DeliveryOptions.ReliableControl);
        Task second = sender.SendAsync(new byte[] { 2 }, DeliveryOptions.ReliableControl);
        Assert.False(second.IsCompleted);
        await second.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(new byte[] { 1 }, await receiver.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(new byte[] { 2 }, await receiver.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, sender.GetUdpDeliveryMetrics().HighWaterWindowOccupancy);
    }

    [Fact]
    public async Task Shutdown_CancelsPendingReliableWindowAdmissions()
    {
        UdpDeliverySettings settings = FastSettings() with { ReliableWindowSize = 1 };
        var peer = new UdpDeliveryPeer(settings, (_, _) => Task.CompletedTask);
        await peer.SendAsync(new byte[] { 1 }, DeliveryOptions.ReliableControl);
        Task blocked = peer.SendAsync(new byte[] { 2 }, DeliveryOptions.ReliableControl);
        Assert.False(blocked.IsCompleted);

        await peer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<ObjectDisposedException>(() => blocked);
    }

    [Fact]
    public async Task ReliableDelivery_ReportsRetryExhaustionUnderTotalLoss()
    {
        UdpDeliverySettings settings = FastSettings() with { MaximumRetries = 1 };
        await using var peer = new UdpDeliveryPeer(settings, (_, _) => Task.CompletedTask);
        await peer.SendAsync(new byte[] { 1 }, DeliveryOptions.ReliableControl);

        await WaitUntilAsync(() => peer.IsFailed, 2000);

        Assert.Equal(1, peer.GetUdpDeliveryMetrics().RetryExhausted);
        Assert.True(peer.GetUdpDeliveryMetrics().Retransmissions >= 1);
        await Assert.ThrowsAsync<IOException>(() => peer.ReceiveAsync());
    }

    [Fact]
    public async Task Retransmission_ReusesApplicationTimestampButRebuildsEnvelopeTimestamp()
    {
        var datagrams = new ConcurrentQueue<byte[]>();
        UdpDeliverySettings settings = FastSettings() with { MaximumRetries = 1 };
        await using var peer = new UdpDeliveryPeer(settings, (datagram, _) => { datagrams.Enqueue(datagram); return Task.CompletedTask; });
        byte[] packet = SignalShared.BuildPacket(PacketType.ChatMessage, new byte[] { 7 });

        await peer.SendAsync(packet, DeliveryOptions.ReliableControl);
        await WaitUntilAsync(() => datagrams.Count >= 2, 1000);
        byte[][] sent = datagrams.Take(2).ToArray();
        Assert.True(UdpEnvelopeCodec.TryDecode(sent[0], out UdpEnvelope first, out _));
        Assert.True(UdpEnvelopeCodec.TryDecode(sent[1], out UdpEnvelope retry, out _));

        Assert.Equal(first.Payload, retry.Payload);
        Assert.True(retry.SentAtUnixMilliseconds > first.SentAtUnixMilliseconds);
        Assert.Equal(
            SignalShared.ReadNetworkDatagram(first.Payload).SentAtUnixMilliseconds,
            SignalShared.ReadNetworkDatagram(retry.Payload).SentAtUnixMilliseconds);
    }

    [Fact]
    public async Task ReliableOrdered_DeliversOneThousandMessagesThroughDeterministicImpairment()
    {
        UdpDeliverySettings settings = FastSettings() with
        {
            InitialRetransmitMilliseconds = 60,
            MaximumRetransmitMilliseconds = 300,
            MaximumRetries = 6
        };
        var link = new DeterministicImpairedLink();
        UdpDeliveryPeer? sender = null;
        UdpDeliveryPeer? receiver = null;
        sender = new UdpDeliveryPeer(settings, (datagram, _) => link.TransmitAsync(datagram, receiver!), receiveQueueCapacity: 2048);
        receiver = new UdpDeliveryPeer(settings, (datagram, _) => link.TransmitAsync(datagram, sender!), receiveQueueCapacity: 2048);
        await using var senderOwner = sender;
        await using var receiverOwner = receiver;

        for (int value = 0; value < 1000; value++)
            await sender.SendAsync(BitConverter.GetBytes(value), DeliveryOptions.ReliableGameplay);

        for (int expected = 0; expected < 1000; expected++)
        {
            byte[] payload = await receiver.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(expected, BitConverter.ToInt32(payload));
        }
        await WaitUntilAsync(() => sender.GetUdpDeliveryMetrics().CurrentWindowOccupancy == 0, 5000);
        await link.DrainAsync();

        Assert.True(sender.GetUdpDeliveryMetrics().Retransmissions >= 90);
        Assert.True(receiver.GetUdpDeliveryMetrics().Duplicates > 0);
        Assert.True(receiver.GetUdpDeliveryMetrics().ReorderedPackets > 0);
    }

    private static UdpDeliverySettings FastSettings() => new()
    {
        ReliableWindowSize = 32,
        ReorderCapacity = 32,
        InitialRetransmitMilliseconds = 40,
        MinimumRetransmitMilliseconds = 20,
        MaximumRetransmitMilliseconds = 200,
        MaximumRetries = 3,
        AcknowledgementDelayMilliseconds = 5
    };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 1000)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private sealed class DeterministicImpairedLink
    {
        private readonly ConcurrentDictionary<string, int> _attempts = new();
        private readonly ConcurrentBag<Task> _deliveries = new();

        public Task TransmitAsync(byte[] datagram, UdpDeliveryPeer target)
        {
            Assert.True(UdpEnvelopeCodec.TryDecode(datagram, out UdpEnvelope envelope, out string error), error);
            string key = $"{target.GetHashCode()}:{envelope.Channel}:{envelope.Sequence}:{(byte)envelope.Flags}";
            int attempt = _attempts.AddOrUpdate(key, 1, (_, count) => count + 1);
            if (envelope.HasPayload && attempt == 1 && envelope.Sequence % 10 == 0) return Task.CompletedTask;

            Schedule(datagram, target, envelope.HasPayload ? (int)((8 - envelope.Sequence % 8) % 8) : 1);
            if (envelope.HasPayload && attempt == 1 && envelope.Sequence % 20 == 1)
                Schedule(datagram.ToArray(), target, 10);
            return Task.CompletedTask;
        }

        public async Task DrainAsync() => await Task.WhenAll(_deliveries.ToArray());

        private void Schedule(byte[] datagram, UdpDeliveryPeer target, int delayMilliseconds)
        {
            _deliveries.Add(Task.Run(async () =>
            {
                if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds);
                if (!target.IsFailed) target.ProcessDatagram(datagram);
            }));
        }
    }
}
