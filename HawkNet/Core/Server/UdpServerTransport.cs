using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server;

/// <summary>
/// UDP server transport that presents each remote endpoint as a virtual connection to the
/// connection/session layer. Protocol-v3 delivery envelopes carry one framed HawkNet packet.
/// </summary>
public sealed class UdpServerTransport : IServerTransport, IUdpDeliveryMetricsSource
{
	private readonly NetworkingSettings _settings;
	private readonly ConcurrentDictionary<IPEndPoint, UdpServerTransportConnection> _connections = new();
	private readonly object _metricsLock = new();
	private UdpDeliveryMetricsSnapshot _retiredMetrics;
	private bool _hasRetiredMetrics;
	private readonly ConcurrentQueue<ITransportConnection> _pendingConnections = new();
	private readonly SemaphoreSlim _pendingSignal = new(0);
	private readonly object _lifecycleLock = new();
	private UdpClient? _client;
	private CancellationTokenSource? _receiveCts;
	private Task? _receiveTask;
	private bool _isRunning;

	public UdpServerTransport(GameHostConfig config)
	{
		if (config is null) throw new ArgumentNullException(nameof(config));
		_settings = config.Networking;
		if (!ServerConfiguration.TryResolveBindAddress(config.Server.Host, out IPAddress? address))
			throw new InvalidOperationException($"Invalid server bind address '{config.Server.Host}'.");
		IPAddress = address!;
		Port = config.Server.Port;
	}

	public IPAddress IPAddress { get; }
	public int Port { get; }
	public IPEndPoint? LocalEndPoint => _client?.Client.LocalEndPoint as IPEndPoint;
	internal bool IsRunning => Volatile.Read(ref _isRunning);

	public Task StartAsync(CancellationToken token = default)
	{
		lock (_lifecycleLock)
		{
			if (_isRunning) return Task.CompletedTask;
			var client = new UdpClient(IPAddress.AddressFamily);
			client.Client.ReceiveBufferSize = Math.Max(1, _settings.ReceiveBufferSize);
			client.Client.SendBufferSize = Math.Max(1, _settings.SendBufferSize);
			if (IPAddress.Equals(IPAddress.IPv6Any)) client.Client.DualMode = true;
			client.Client.Bind(new IPEndPoint(IPAddress, Port));
			_client = client;
			_receiveCts = CancellationTokenSource.CreateLinkedTokenSource(token);
			_isRunning = true;
			_receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);
		}
		return Task.CompletedTask;
	}

	public async Task<ITransportConnection> AcceptConnectionAsync(CancellationToken token = default)
	{
		while (true)
		{
			if (_pendingConnections.TryDequeue(out ITransportConnection? connection)) return connection;
			await _pendingSignal.WaitAsync(token);
		}
	}

	public async Task StopAsync()
	{
		Task? receiveTask;
		lock (_lifecycleLock)
		{
			if (!_isRunning) return;
			_isRunning = false;
			_receiveCts?.Cancel();
			_client?.Close();
			receiveTask = _receiveTask;
		}

		UdpServerTransportConnection[] connections = _connections.Values.ToArray();
		_connections.Clear();
		foreach (UdpServerTransportConnection connection in connections) RetireMetrics(connection.GetUdpDeliveryMetrics());
		await Task.WhenAll(connections.Select(connection => connection.CloseFromOwnerAsync()));

		if (receiveTask is not null)
		{
			try { await receiveTask; }
			catch (OperationCanceledException) { }
			catch (ObjectDisposedException) { }
			catch (SocketException) { }
		}
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
		_receiveCts?.Dispose();
		_pendingSignal.Dispose();
		_client?.Dispose();
	}

	internal async Task SendToAsync(byte[] packet, IPEndPoint remoteEndPoint, CancellationToken token)
	{
		if (packet.Length > _settings.UdpDelivery.MaxDatagramBytes)
			throw new ArgumentOutOfRangeException(nameof(packet), $"UDP datagrams cannot exceed {_settings.UdpDelivery.MaxDatagramBytes} bytes.");
		UdpClient client = _client ?? throw new InvalidOperationException("UDP server transport is not running.");
#if NET8_0_OR_GREATER
		await client.SendAsync(packet.AsMemory(), remoteEndPoint, token);
#else
		token.ThrowIfCancellationRequested();
		await client.SendAsync(packet, packet.Length, remoteEndPoint);
		token.ThrowIfCancellationRequested();
#endif
	}

	public UdpDeliveryMetricsSnapshot GetUdpDeliveryMetrics()
	{
		IEnumerable<UdpDeliveryMetricsSnapshot> active = _connections.Values.Select(connection => connection.GetUdpDeliveryMetrics());
		lock (_metricsLock)
			return UdpDeliveryMetricsSnapshot.Aggregate(_hasRetiredMetrics ? active.Prepend(_retiredMetrics) : active);
	}

	internal void Remove(IPEndPoint remoteEndPoint, UdpServerTransportConnection connection)
	{
		if (((ICollection<KeyValuePair<IPEndPoint, UdpServerTransportConnection>>)_connections)
			.Remove(new KeyValuePair<IPEndPoint, UdpServerTransportConnection>(remoteEndPoint, connection)))
			RetireMetrics(connection.GetUdpDeliveryMetrics());
	}

	private void RetireMetrics(UdpDeliveryMetricsSnapshot snapshot)
	{
		snapshot = snapshot with { CurrentWindowOccupancy = 0 };
		lock (_metricsLock)
		{
			_retiredMetrics = _hasRetiredMetrics
				? UdpDeliveryMetricsSnapshot.Aggregate(new[] { _retiredMetrics, snapshot })
				: snapshot;
			_hasRetiredMetrics = true;
		}
	}

	private async Task ReceiveLoopAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			UdpReceiveResult received;
			try
			{
				UdpClient client = _client ?? throw new ObjectDisposedException(nameof(UdpServerTransport));
#if NET8_0_OR_GREATER
				received = await client.ReceiveAsync(token);
#else
				received = await client.ReceiveAsync();
				token.ThrowIfCancellationRequested();
#endif
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
			catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
			catch (SocketException) when (token.IsCancellationRequested) { break; }
			catch (SocketException) { continue; }

			ReadNetworkPacketResult discoveryPacket = SignalShared.ReadNetworkDatagram(received.Buffer);
			if (discoveryPacket.Success && discoveryPacket.PacketType == PacketType.DiscoveryRequest)
			{
				await SendToAsync(
					SignalShared.BuildHeaderOnlyPacket(PacketType.DiscoveryResponse),
					received.RemoteEndPoint,
					token);
				continue;
			}

			if (!UdpEnvelopeCodec.TryDecode(received.Buffer, out UdpEnvelope envelope, out _, _settings.UdpDelivery.MaxDatagramBytes))
				continue;

			UdpServerTransportConnection connection = null!;
			while (!_connections.TryGetValue(received.RemoteEndPoint, out connection!))
			{
				if (!IsInitialSessionEnvelope(envelope)) break;
				var candidate = new UdpServerTransportConnection(
					this,
					received.RemoteEndPoint,
					_settings.UdpDelivery,
					_settings.UdpReceiveQueueCapacity);
				if (_connections.TryAdd(received.RemoteEndPoint, candidate))
				{
					connection = candidate;
					_pendingConnections.Enqueue(connection);
					_pendingSignal.Release();
					break;
				}
				await candidate.CloseFromOwnerAsync();
			}

			if (connection is not null) connection.TryProcessDatagram(received.Buffer);
		}
	}

	private static bool IsInitialSessionEnvelope(UdpEnvelope envelope)
	{
		if (!envelope.HasPayload || !envelope.IsReliable || envelope.Channel != UdpDeliveryChannel.Control) return false;
		ReadNetworkPacketResult packet = SignalShared.ReadNetworkDatagram(envelope.Payload);
		return packet.Success && packet.PacketType is PacketType.HandshakeRequest or PacketType.ReconnectRequest;
	}
}

internal sealed class UdpServerTransportConnection : ITransportConnection, IUdpDeliveryMetricsSource, IUdpDeliveryMetricsRecorder
{
	private readonly UdpServerTransport _owner;
	private readonly IPEndPoint _remoteEndPoint;
	private readonly UdpDeliveryPeer _deliveryPeer;
	private int _closed;

	public UdpServerTransportConnection(UdpServerTransport owner, IPEndPoint remoteEndPoint, UdpDeliverySettings settings, int receiveQueueCapacity)
	{
		_owner = owner;
		_remoteEndPoint = remoteEndPoint;
		_deliveryPeer = new UdpDeliveryPeer(settings, (datagram, token) => owner.SendToAsync(datagram, remoteEndPoint, token), receiveQueueCapacity: receiveQueueCapacity);
	}

	public bool IsConnected => Volatile.Read(ref _closed) == 0 && _owner.IsRunning;
	public EndPoint RemoteEndPoint => _remoteEndPoint;

	public Task SendAsync(byte[] packet, CancellationToken token = default)
	{
		ReadNetworkPacketResult parsed = SignalShared.ReadNetworkDatagram(packet);
		if (!parsed.Success) throw new ArgumentException("The UDP transport requires one valid framed HawkNet packet.", nameof(packet));
		return SendAsync(packet, PacketDeliveryPolicy.GetDefault(parsed.PacketType), token);
	}

	public Task SendAsync(byte[] packet, DeliveryOptions delivery, CancellationToken token = default)
	{
		if (!IsConnected) throw new InvalidOperationException("UDP endpoint is disconnected.");
		return _deliveryPeer.SendAsync(packet, delivery, token);
	}

	public async Task<ReadNetworkPacketResult> ReadPacketAsync(CancellationToken token = default)
	{
		if (!IsConnected) return FailedRead("UDP endpoint disconnected.");
		try { return SignalShared.ReadNetworkDatagram(await _deliveryPeer.ReceiveAsync(token)); }
		catch (IOException exception) { return FailedRead(exception.Message); }
	}

	public async Task DisconnectAsync()
	{
		if (Interlocked.Exchange(ref _closed, 1) == 0)
		{
			_owner.Remove(_remoteEndPoint, this);
			await _deliveryPeer.DisposeAsync();
		}
	}

	public ValueTask DisposeAsync()
	{
		return new ValueTask(DisconnectAsync());
	}

	internal bool TryProcessDatagram(byte[] datagram)
	{
		if (!IsConnected) return false;
		try { _deliveryPeer.ProcessDatagram(datagram); return !_deliveryPeer.IsFailed; }
		catch (InvalidDataException) { return true; }
		catch (IOException) { return false; }
	}

	internal async Task CloseFromOwnerAsync()
	{
		if (Interlocked.Exchange(ref _closed, 1) == 0)
		{
			await _deliveryPeer.DisposeAsync();
		}
	}

	public UdpDeliveryMetricsSnapshot GetUdpDeliveryMetrics() => _deliveryPeer.GetUdpDeliveryMetrics();
	public void RecordSupersededSend() => _deliveryPeer.RecordSupersededSend();

	private static ReadNetworkPacketResult FailedRead(string message) => new()
	{
		Success = false,
		PacketType = PacketType.Unknown,
		Payload = Array.Empty<byte>(),
		ErrorMsg = message
	};
}
