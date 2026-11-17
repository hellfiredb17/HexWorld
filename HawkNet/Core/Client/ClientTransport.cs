using System.Net.Sockets;
using System.Net;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public interface IClientTransport : IAsyncDisposable
	{
		bool IsConnected { get; }

		Task ConnectAsync(CancellationToken token = default);
		Task DisconnectAsync(CancellationToken token = default);
		Task SendAsync(byte[] packet, CancellationToken token = default);
		Task SendAsync(byte[] packet, DeliveryOptions delivery, CancellationToken token = default) => SendAsync(packet, token);
		Task<ReadNetworkPacketResult> ReadPacketAsync(CancellationToken token = default);
	}

	public sealed class TcpClientTransport : IClientTransport
	{
		private readonly GameClientConfig _config;
		private TcpClient? _client;
		private TcpTransportConnection? _connection;

		public TcpClientTransport(GameClientConfig config)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
		}

		public bool IsConnected => _connection?.IsConnected == true;

		public async Task ConnectAsync(CancellationToken token = default)
		{
			if(IsConnected)
			{
				return;
			}

			_client = new TcpClient
			{
				NoDelay = _config.Networking.NoDelay,
				ReceiveBufferSize = Math.Max(1, _config.Networking.ReceiveBufferSize),
				SendBufferSize = Math.Max(1, _config.Networking.SendBufferSize)
			};

#if NET8_0_OR_GREATER
			await _client.ConnectAsync(_config.Server.Host, _config.Server.Port, token);
#else
			await _client.ConnectAsync(_config.Server.Host, _config.Server.Port);
			token.ThrowIfCancellationRequested();
#endif
			_connection = new TcpTransportConnection(_client);
		}

		public async Task DisconnectAsync(CancellationToken token = default)
		{
			if(_connection is not null)
			{
				await _connection.DisconnectAsync();
				await _connection.DisposeAsync();
				_connection = null;
			}

			_client = null;
			await Task.CompletedTask;
		}

		public Task SendAsync(byte[] packet, CancellationToken token = default)
		{
			return _connection is null
				? throw new InvalidOperationException("Not connected to server.")
				: _connection.SendAsync(packet, token);
		}

		public Task SendAsync(byte[] packet, DeliveryOptions delivery, CancellationToken token = default) => SendAsync(packet, token);

		public Task<ReadNetworkPacketResult> ReadPacketAsync(CancellationToken token = default)
		{
			return _connection is null
				? throw new InvalidOperationException("Not connected to server.")
				: _connection.ReadPacketAsync(token);
		}

		public async ValueTask DisposeAsync()
		{
			await DisconnectAsync();
		}
	}

	/// <summary>Client transport using protocol-v3 UDP delivery envelopes.</summary>
	public sealed class UdpClientTransport : IClientTransport, IUdpDeliveryMetricsSource
	{
		private readonly GameClientConfig _config;
		private UdpClient? _client;
		private UdpDeliveryPeer? _deliveryPeer;
		private CancellationTokenSource? _receiveCts;
		private Task? _receiveTask;
		private UdpDeliveryMetricsSnapshot _lastMetrics;
		private int _connected;

		public UdpClientTransport(GameClientConfig config)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
		}

		public bool IsConnected => Volatile.Read(ref _connected) == 1 && _client is not null;

		public Task ConnectAsync(CancellationToken token = default)
		{
			if (IsConnected) return Task.CompletedTask;
			token.ThrowIfCancellationRequested();
			IPAddress remoteAddress = ResolveRemoteAddress(_config.Server.Host);
			var client = new UdpClient(remoteAddress.AddressFamily);
			client.Client.ReceiveBufferSize = Math.Max(1, _config.Networking.ReceiveBufferSize);
			client.Client.SendBufferSize = Math.Max(1, _config.Networking.SendBufferSize);
			client.Connect(new IPEndPoint(remoteAddress, _config.Server.Port));
			_client = client;
			_deliveryPeer = new UdpDeliveryPeer(_config.Networking.UdpDelivery, SendDatagramAsync);
			_lastMetrics = UdpDeliveryMetricsSnapshot.Empty;
			_deliveryPeer.ReliableDeliveryFailed += OnReliableDeliveryFailed;
			_receiveCts = new CancellationTokenSource();
			_receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);
			Volatile.Write(ref _connected, 1);
			return Task.CompletedTask;
		}

		public async Task DisconnectAsync(CancellationToken token = default)
		{
			Volatile.Write(ref _connected, 0);
			_receiveCts?.Cancel();
			UdpClient? client = Interlocked.Exchange(ref _client, null);
			client?.Close();
			client?.Dispose();
			if (_receiveTask is not null)
			{
				try { await _receiveTask.ConfigureAwait(false); }
				catch (OperationCanceledException) { }
				catch (ObjectDisposedException) { }
				catch (SocketException) { }
			}
			if (_deliveryPeer is not null)
			{
				_lastMetrics = _deliveryPeer.GetUdpDeliveryMetrics() with { CurrentWindowOccupancy = 0 };
				_deliveryPeer.ReliableDeliveryFailed -= OnReliableDeliveryFailed;
				await _deliveryPeer.DisposeAsync().ConfigureAwait(false);
			}
			_deliveryPeer = null;
			_receiveTask = null;
			_receiveCts?.Dispose();
			_receiveCts = null;
		}

		public Task SendAsync(byte[] packet, CancellationToken token = default)
		{
            ReadNetworkPacketResult parsed = SignalShared.ReadNetworkDatagram(packet);
            if (!parsed.Success) throw new ArgumentException("The UDP transport requires one valid framed HawkNet packet.", nameof(packet));
            return SendAsync(packet, PacketDeliveryPolicy.GetDefault(parsed.PacketType), token);
        }

		public Task SendAsync(byte[] packet, DeliveryOptions delivery, CancellationToken token = default)
		{
			UdpDeliveryPeer peer = _deliveryPeer ?? throw new InvalidOperationException("Not connected to server.");
			return peer.SendAsync(packet, delivery, token);
		}

		private async Task SendDatagramAsync(byte[] datagram, CancellationToken token)
		{
			UdpClient client = _client ?? throw new InvalidOperationException("Not connected to server.");
#if NET8_0_OR_GREATER
			await client.SendAsync(datagram.AsMemory(), token);
#else
			token.ThrowIfCancellationRequested();
			await client.SendAsync(datagram, datagram.Length);
			token.ThrowIfCancellationRequested();
#endif
		}

		public async Task<ReadNetworkPacketResult> ReadPacketAsync(CancellationToken token = default)
		{
			UdpDeliveryPeer peer = _deliveryPeer ?? throw new InvalidOperationException("Not connected to server.");
			return SignalShared.ReadNetworkDatagram(await peer.ReceiveAsync(token).ConfigureAwait(false));
		}

		public UdpDeliveryMetricsSnapshot GetUdpDeliveryMetrics() => _deliveryPeer?.GetUdpDeliveryMetrics() ?? _lastMetrics;

		private async Task ReceiveLoopAsync(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					UdpClient client = _client ?? throw new ObjectDisposedException(nameof(UdpClientTransport));
#if NET8_0_OR_GREATER
					UdpReceiveResult result = await client.ReceiveAsync(token);
#else
					UdpReceiveResult result = await client.ReceiveAsync();
					token.ThrowIfCancellationRequested();
#endif
					_deliveryPeer?.ProcessDatagram(result.Buffer);
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
				catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
				catch (SocketException) when (token.IsCancellationRequested) { break; }
				catch (InvalidDataException) { }
			}
		}

		private void OnReliableDeliveryFailed()
		{
			Volatile.Write(ref _connected, 0);
			_receiveCts?.Cancel();
			_client?.Close();
		}

		public async ValueTask DisposeAsync()
		{
			await DisconnectAsync();
		}

		private static IPAddress ResolveRemoteAddress(string host)
		{
			if (IPAddress.TryParse(host, out IPAddress? literal)) return literal;
			IPAddress[] addresses = Dns.GetHostAddresses(host);
			return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
				?? addresses.FirstOrDefault()
				?? throw new SocketException((int)SocketError.HostNotFound);
		}
	}
}
