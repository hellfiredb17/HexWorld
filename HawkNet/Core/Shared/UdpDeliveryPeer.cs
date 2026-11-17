namespace HawkNet.Core.Shared;

/// <summary>Owns delivery state for one UDP remote endpoint.</summary>
public sealed class UdpDeliveryPeer : IAsyncDisposable, IUdpDeliveryMetricsSource
{
    private const int ChannelCount = 3;
    private readonly UdpDeliverySettings _settings;
    private readonly Func<byte[], CancellationToken, Task> _sendDatagram;
    private readonly IUdpDeliveryClock _clock;
    private readonly int _receiveQueueCapacity;
    private readonly ChannelState[] _channels;
    private readonly SemaphoreSlim[] _windowSlots;
    private readonly Queue<byte[]> _delivered = new();
    private readonly SemaphoreSlim _deliveredSignal = new(0);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly UdpDeliveryMetrics _metrics = new();
    private readonly Task _pumpTask;
    private TaskCompletionSource<bool>? _activeWriteCompletion;
    private double _smoothedRttMilliseconds;
    private double _rttVarianceMilliseconds;
    private double _jitterMilliseconds;
    private double _lastRttSampleMilliseconds;
    private double _retransmitMilliseconds;
    private int _failed;
    private int _disposed;

    public UdpDeliveryPeer(
        UdpDeliverySettings settings,
        Func<byte[], CancellationToken, Task> sendDatagram,
        IUdpDeliveryClock? clock = null,
        int receiveQueueCapacity = 256)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        IReadOnlyList<string> settingsErrors = UdpDeliveryConfiguration.Validate(settings);
        if (settingsErrors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, settingsErrors), nameof(settings));
        _sendDatagram = sendDatagram ?? throw new ArgumentNullException(nameof(sendDatagram));
        _clock = clock ?? SystemUdpDeliveryClock.Instance;
        _receiveQueueCapacity = receiveQueueCapacity > 0 ? receiveQueueCapacity : throw new ArgumentOutOfRangeException(nameof(receiveQueueCapacity));
        _channels = Enumerable.Range(0, ChannelCount).Select(_ => new ChannelState()).ToArray();
        _windowSlots = Enumerable.Range(0, ChannelCount)
            .Select(_ => new SemaphoreSlim(settings.ReliableWindowSize, settings.ReliableWindowSize)).ToArray();
        _retransmitMilliseconds = settings.InitialRetransmitMilliseconds;
        _metrics.SetTiming(0, _retransmitMilliseconds, 0);
        _pumpTask = Task.Run(() => RunPumpAsync(_lifetime.Token));
    }

    public event Action? ReliableDeliveryFailed;

    public bool IsFailed => Volatile.Read(ref _failed) == 1;

    public UdpDeliveryMetricsSnapshot GetUdpDeliveryMetrics() => _metrics.Snapshot();

    internal void RecordSupersededSend() => Interlocked.Increment(ref _metrics.SupersededSends);

    public async Task SendAsync(byte[] framedPacket, DeliveryOptions delivery, CancellationToken token = default)
    {
        if (framedPacket is null) throw new ArgumentNullException(nameof(framedPacket));
        ThrowIfUnavailable();
        int channelIndex = Validate(delivery);
        int maximum = _settings.MaxDatagramBytes - UdpEnvelopeCodec.HeaderSize;
        if (framedPacket.Length > maximum)
        {
            int applicationMaximum = maximum - SignalShared.LengthPrefixSize - SignalShared.HeaderSize;
            throw new ArgumentOutOfRangeException(nameof(framedPacket),
                $"Framed UDP packet length {framedPacket.Length} exceeds the configured maximum {maximum} bytes; the maximum application payload is {applicationMaximum} bytes.");
        }

        if (delivery.Mode == UdpDeliveryMode.UnreliableSequenced)
        {
            uint sequence;
            lock (_sync) sequence = NextSequence(ref _channels[channelIndex].NextUnreliableSend);
            await SendPayloadEnvelopeAsync(framedPacket, delivery, sequence, token).ConfigureAwait(false);
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try
        {
            await _windowSlots[channelIndex].WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            ThrowIfUnavailable();
            throw;
        }
        PendingReliable pending;
        lock (_sync)
        {
            ThrowIfUnavailable();
            ChannelState channel = _channels[channelIndex];
            uint sequence = NextSequence(ref channel.NextReliableSend);
            pending = new PendingReliable(sequence, framedPacket, _clock.GetTimestamp());
            channel.Pending.Add(sequence, pending);
            Interlocked.Increment(ref _metrics.ReliableAdmissions);
            int occupancy = Interlocked.Increment(ref _metrics.CurrentWindowOccupancy);
            UpdateHighWater(occupancy);
        }

        try
        {
            await SendPayloadEnvelopeAsync(framedPacket, delivery, pending.Sequence, token).ConfigureAwait(false);
            lock (_sync) pending.SentTimestamp = _clock.GetTimestamp();
        }
        catch
        {
            lock (_sync)
            {
                pending.Acknowledged = true;
                int released = ReleaseContiguousAcknowledged(_channels[channelIndex]);
                if (released > 0) Interlocked.Add(ref _metrics.CurrentWindowOccupancy, -released);
                for (int index = 0; index < released; index++) _windowSlots[channelIndex].Release();
            }
            throw;
        }
    }

    public void ProcessDatagram(byte[] datagram)
    {
        ThrowIfUnavailable();
        if (!UdpEnvelopeCodec.TryDecode(datagram, out UdpEnvelope envelope, out string error, _settings.MaxDatagramBytes))
            throw new InvalidDataException(error);
        Interlocked.Increment(ref _metrics.ReceivedDatagrams);
        int channelIndex = (int)envelope.Channel;
        int releaseCount = 0;
        int deliveredCount = 0;
        bool reliableReceiveOverflow = false;

        lock (_sync)
        {
            ChannelState channel = _channels[channelIndex];
            if (envelope.HasAcknowledgement)
                releaseCount = ProcessAcknowledgements(channel, envelope.Acknowledgement, envelope.AcknowledgementBits);
            if (envelope.HasPayload && envelope.IsReliable)
            {
                int distance = unchecked((int)(envelope.Sequence - channel.NextExpectedReliable));
                if (distance < 0)
                {
                    RecordReceivedSequence(channel, envelope.Sequence);
                    MarkAcknowledgementPending(channel);
                    Interlocked.Increment(ref _metrics.Duplicates);
                }
                else if (distance == 0)
                {
                    RecordReceivedSequence(channel, envelope.Sequence);
                    MarkAcknowledgementPending(channel);
                    if (EnqueueDelivered(envelope.Payload))
                    {
                        channel.NextExpectedReliable++;
                        deliveredCount++;
                    }
                    else reliableReceiveOverflow = true;
                    while (!reliableReceiveOverflow && channel.Reorder.TryGetValue(channel.NextExpectedReliable, out byte[]? next))
                    {
                        if (!EnqueueDelivered(next)) { reliableReceiveOverflow = true; break; }
                        channel.Reorder.Remove(channel.NextExpectedReliable);
                        deliveredCount++;
                        channel.NextExpectedReliable++;
                    }
                }
                else if (distance <= _settings.ReorderCapacity)
                {
                    if (channel.Reorder.TryAdd(envelope.Sequence, envelope.Payload))
                    {
                        RecordReceivedSequence(channel, envelope.Sequence);
                        MarkAcknowledgementPending(channel);
                        Interlocked.Increment(ref _metrics.ReorderedPackets);
                    }
                    else
                    {
                        RecordReceivedSequence(channel, envelope.Sequence);
                        MarkAcknowledgementPending(channel);
                        Interlocked.Increment(ref _metrics.Duplicates);
                    }
                }
                else
                {
                    Interlocked.Increment(ref _metrics.ReorderOverflow);
                }
            }
            else if (envelope.HasPayload && (!channel.HasUnreliableReceive || UdpEnvelopeCodec.IsNewerSequence(envelope.Sequence, channel.LastUnreliableReceive)))
            {
                channel.HasUnreliableReceive = true;
                channel.LastUnreliableReceive = envelope.Sequence;
                if (EnqueueDelivered(envelope.Payload)) deliveredCount++;
            }
            else if (envelope.HasPayload)
            {
                Interlocked.Increment(ref _metrics.StaleSequencedDrops);
            }
        }

        for (int index = 0; index < releaseCount; index++) _windowSlots[channelIndex].Release();
        if (releaseCount > 0) Interlocked.Add(ref _metrics.CurrentWindowOccupancy, -releaseCount);
        if (deliveredCount > 0) _deliveredSignal.Release(deliveredCount);
        if (reliableReceiveOverflow) FailReliableDelivery(recordRetryExhaustion: false);
    }

    public async Task<byte[]> ReceiveAsync(CancellationToken token = default)
    {
        while (true)
        {
            lock (_sync)
            {
                if (_delivered.Count > 0) return _delivered.Dequeue();
                ThrowIfUnavailable();
            }
            await _deliveredSignal.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private int ProcessAcknowledgements(ChannelState channel, uint acknowledgement, uint acknowledgementBits)
    {
        foreach ((uint sequence, PendingReliable pending) in channel.Pending.ToArray())
        {
            if (pending.Acknowledged || !IsAcknowledged(sequence, acknowledgement, acknowledgementBits)) continue;
            pending.Acknowledged = true;
            Interlocked.Increment(ref _metrics.Acknowledgements);
            if (pending.RetryCount == 0) UpdateRoundTrip(_clock.GetElapsedTime(pending.SentTimestamp).TotalMilliseconds);
        }
        return ReleaseContiguousAcknowledged(channel);
    }

    private static int ReleaseContiguousAcknowledged(ChannelState channel)
    {
        int released = 0;
        while (channel.Pending.TryGetValue(channel.NextReliableRelease, out PendingReliable? pending) && pending.Acknowledged)
        {
            channel.Pending.Remove(channel.NextReliableRelease);
            channel.NextReliableRelease++;
            released++;
        }
        return released;
    }

    private async Task RunPumpAsync(CancellationToken token)
    {
        int interval = Math.Max(5, Math.Min(_settings.AcknowledgementDelayMilliseconds, 20));
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _clock.DelayAsync(TimeSpan.FromMilliseconds(interval), token).ConfigureAwait(false);
                var sends = new List<(byte[] Payload, DeliveryOptions Delivery, uint Sequence)>();
                var acknowledgements = new List<(UdpDeliveryChannel Channel, uint Ack, uint Bits)>();
                bool exhausted = false;
                long now = _clock.GetTimestamp();
                lock (_sync)
                {
                    for (int index = 0; index < ChannelCount; index++)
                    {
                        ChannelState channel = _channels[index];
                        foreach (PendingReliable pending in channel.Pending.Values)
                        {
                            if (pending.Acknowledged) continue;
                            if (_clock.GetElapsedTime(pending.SentTimestamp).TotalMilliseconds < _retransmitMilliseconds) continue;
                            if (pending.RetryCount >= _settings.MaximumRetries) { exhausted = true; break; }
                            pending.RetryCount++;
                            pending.SentTimestamp = now;
                            sends.Add((pending.Payload, new DeliveryOptions(UdpDeliveryMode.ReliableOrdered, (UdpDeliveryChannel)index), pending.Sequence));
                            Interlocked.Increment(ref _metrics.Retransmissions);
                        }
                        if (exhausted) break;
                        if (channel.AcknowledgementPending
                            && _clock.GetElapsedTime(channel.AcknowledgementDueTimestamp).TotalMilliseconds >= _settings.AcknowledgementDelayMilliseconds)
                        {
                            acknowledgements.Add(((UdpDeliveryChannel)index, channel.LatestReceived, channel.ReceivedBits));
                            channel.AcknowledgementPending = false;
                        }
                    }
                }

                if (exhausted) { FailReliableDelivery(recordRetryExhaustion: true); return; }
                foreach (var send in sends) await SendPayloadEnvelopeAsync(send.Payload, send.Delivery, send.Sequence, token).ConfigureAwait(false);
                foreach (var ack in acknowledgements) await SendAcknowledgementOnlyAsync(ack.Channel, ack.Ack, ack.Bits, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { FailReliableDelivery(recordRetryExhaustion: false); }
    }

    private async Task SendPayloadEnvelopeAsync(byte[] payload, DeliveryOptions delivery, uint sequence, CancellationToken token)
    {
        UdpEnvelopeFlags flags = UdpEnvelopeFlags.HasPayload;
        if (delivery.Mode == UdpDeliveryMode.ReliableOrdered) flags |= UdpEnvelopeFlags.Reliable;
        uint ack = 0, bits = 0;
        lock (_sync)
        {
            ChannelState channel = _channels[(int)delivery.Channel];
            if (channel.HasReceivedSequence)
            {
                flags |= UdpEnvelopeFlags.HasAcknowledgement;
                ack = channel.LatestReceived;
                bits = channel.ReceivedBits;
                channel.AcknowledgementPending = false;
            }
        }
        await SendEnvelopeAsync(new UdpEnvelope(flags, delivery.Channel, sequence, ack, bits, _clock.GetUtcUnixMilliseconds(), payload), token).ConfigureAwait(false);
    }

    private Task SendAcknowledgementOnlyAsync(UdpDeliveryChannel channel, uint acknowledgement, uint bits, CancellationToken token)
    {
        Interlocked.Increment(ref _metrics.AcknowledgementOnlyDatagrams);
        return SendEnvelopeAsync(new UdpEnvelope(UdpEnvelopeFlags.HasAcknowledgement, channel, 0, acknowledgement, bits, _clock.GetUtcUnixMilliseconds(), Array.Empty<byte>()), token);
    }

    private async Task SendEnvelopeAsync(UdpEnvelope envelope, CancellationToken token)
    {
        byte[] datagram = UdpEnvelopeCodec.Encode(envelope, _settings.MaxDatagramBytes);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try { await _sendGate.WaitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { ThrowIfUnavailable(); throw; }
        var writeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync) _activeWriteCompletion = writeCompletion;
        try { await _sendDatagram(datagram, linked.Token).ConfigureAwait(false); Interlocked.Increment(ref _metrics.SentDatagrams); }
        finally
        {
            lock (_sync)
                if (ReferenceEquals(_activeWriteCompletion, writeCompletion)) _activeWriteCompletion = null;
            writeCompletion.TrySetResult(true);
            _sendGate.Release();
        }
    }

    private void RecordReceivedSequence(ChannelState channel, uint sequence)
    {
        if (!channel.HasReceivedSequence)
        {
            channel.HasReceivedSequence = true;
            channel.LatestReceived = sequence;
            channel.ReceivedBits = 0;
            return;
        }
        if (UdpEnvelopeCodec.IsNewerSequence(sequence, channel.LatestReceived))
        {
            uint shift = sequence - channel.LatestReceived;
            channel.ReceivedBits = shift > 32 ? 0
                : shift == 32 ? 1u << 31
                : (channel.ReceivedBits << (int)shift) | (1u << ((int)shift - 1));
            channel.LatestReceived = sequence;
        }
        else
        {
            uint distance = channel.LatestReceived - sequence;
            if (distance is >= 1 and <= 32) channel.ReceivedBits |= 1u << ((int)distance - 1);
        }
    }

    private void MarkAcknowledgementPending(ChannelState channel)
    {
        if (!channel.AcknowledgementPending) channel.AcknowledgementDueTimestamp = _clock.GetTimestamp();
        channel.AcknowledgementPending = true;
    }

    private void UpdateRoundTrip(double sample)
    {
        if (_smoothedRttMilliseconds == 0)
        {
            _smoothedRttMilliseconds = sample;
            _rttVarianceMilliseconds = sample / 2;
        }
        else
        {
            _rttVarianceMilliseconds = 0.75 * _rttVarianceMilliseconds + 0.25 * Math.Abs(_smoothedRttMilliseconds - sample);
            _smoothedRttMilliseconds = 0.875 * _smoothedRttMilliseconds + 0.125 * sample;
        }
        if (_lastRttSampleMilliseconds > 0) _jitterMilliseconds = 0.875 * _jitterMilliseconds + 0.125 * Math.Abs(sample - _lastRttSampleMilliseconds);
        _lastRttSampleMilliseconds = sample;
        _retransmitMilliseconds = Math.Clamp(_smoothedRttMilliseconds + 4 * _rttVarianceMilliseconds,
            _settings.MinimumRetransmitMilliseconds, _settings.MaximumRetransmitMilliseconds);
        _metrics.SetTiming(_smoothedRttMilliseconds, _retransmitMilliseconds, _jitterMilliseconds);
    }

    private static bool IsAcknowledged(uint sequence, uint ack, uint bits)
    {
        if (sequence == ack) return true;
        uint distance = ack - sequence;
        return distance is >= 1 and <= 32 && (bits & (1u << ((int)distance - 1))) != 0;
    }

    private bool EnqueueDelivered(byte[] payload)
    {
        if (_delivered.Count >= _receiveQueueCapacity)
        {
            Interlocked.Increment(ref _metrics.ReorderOverflow);
            return false;
        }
        _delivered.Enqueue(payload);
        return true;
    }

    private static uint NextSequence(ref uint value) { value++; return value; }

    private int Validate(DeliveryOptions delivery)
    {
        if (!Enum.IsDefined(typeof(UdpDeliveryMode), delivery.Mode)) throw new ArgumentOutOfRangeException(nameof(delivery));
        int channel = (int)delivery.Channel;
        if (channel < 0 || channel >= ChannelCount) throw new ArgumentOutOfRangeException(nameof(delivery));
        return channel;
    }

    private void UpdateHighWater(int occupancy)
    {
        int current;
        do
        {
            current = Volatile.Read(ref _metrics.HighWaterWindowOccupancy);
            if (occupancy <= current) return;
        } while (Interlocked.CompareExchange(ref _metrics.HighWaterWindowOccupancy, occupancy, current) != current);
    }

    private void FailReliableDelivery(bool recordRetryExhaustion)
    {
        if (Interlocked.Exchange(ref _failed, 1) == 1) return;
        if (recordRetryExhaustion) Interlocked.Increment(ref _metrics.RetryExhausted);
        _lifetime.Cancel();
        _deliveredSignal.Release();
        ReliableDeliveryFailed?.Invoke();
    }

    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref _disposed) == 1) throw new ObjectDisposedException(nameof(UdpDeliveryPeer));
        if (IsFailed) throw new IOException("Reliable UDP delivery has failed.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _lifetime.Cancel();
        _deliveredSignal.Release();
        await Task.Yield();
        try { await _pumpTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        Task? activeWrite;
        lock (_sync) activeWrite = _activeWriteCompletion?.Task;
        if (activeWrite is not null) await activeWrite.ConfigureAwait(false);
        lock (_sync)
        {
            foreach (ChannelState channel in _channels) { channel.Pending.Clear(); channel.Reorder.Clear(); }
            _delivered.Clear();
        }
        foreach (SemaphoreSlim slot in _windowSlots) slot.Dispose();
        _sendGate.Dispose();
        _deliveredSignal.Dispose();
        _lifetime.Dispose();
    }

    private sealed class ChannelState
    {
        public uint NextReliableSend;
        public uint NextReliableRelease = 1;
        public uint NextUnreliableSend;
        public uint NextExpectedReliable = 1;
        public readonly Dictionary<uint, PendingReliable> Pending = new();
        public readonly Dictionary<uint, byte[]> Reorder = new();
        public bool HasReceivedSequence;
        public uint LatestReceived;
        public uint ReceivedBits;
        public bool AcknowledgementPending;
        public long AcknowledgementDueTimestamp;
        public bool HasUnreliableReceive;
        public uint LastUnreliableReceive;
    }

    private sealed class PendingReliable(uint sequence, byte[] payload, long sentTimestamp)
    {
        public uint Sequence { get; } = sequence;
        public byte[] Payload { get; } = payload;
        public long SentTimestamp { get; set; } = sentTimestamp;
        public int RetryCount { get; set; }
        public bool Acknowledged { get; set; }
    }
}
