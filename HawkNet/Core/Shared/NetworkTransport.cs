using System.Net;
using System.Net.Sockets;

namespace HawkNet.Core.Shared
{
	public interface ITransportConnection : IAsyncDisposable
	{
		bool IsConnected { get; }
		EndPoint? RemoteEndPoint { get; }

		Task SendAsync(byte[] packet, CancellationToken token = default);
		Task SendAsync(byte[] packet, DeliveryOptions delivery, CancellationToken token = default) => SendAsync(packet, token);
		Task<ReadNetworkPacketResult> ReadPacketAsync(CancellationToken token = default);
		Task DisconnectAsync();
	}

	public sealed class TcpTransportConnection : ITransportConnection
	{
		private readonly TcpClient _client;
		private readonly NetworkStream _stream;

		public TcpTransportConnection(TcpClient client)
		{
			_client = client ?? throw new ArgumentNullException(nameof(client));
			_stream = _client.GetStream();
		}

		public bool IsConnected => _client.Connected;
		public EndPoint? RemoteEndPoint => _client.Client.RemoteEndPoint;

		public Task SendAsync(byte[] packet, CancellationToken token = default)
		{
			return _stream.WriteAsync(packet, token).AsTask();
		}

		public Task SendAsync(byte[] packet, DeliveryOptions delivery, CancellationToken token = default) => SendAsync(packet, token);

		public Task<ReadNetworkPacketResult> ReadPacketAsync(CancellationToken token = default)
		{
			return SignalShared.ReadNetworkStream(_stream, token);
		}

		public Task DisconnectAsync()
		{
			_stream.Close();
			_client.Close();
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync()
		{
			_stream.Dispose();
			_client.Dispose();
			return default;
		}
	}
}
