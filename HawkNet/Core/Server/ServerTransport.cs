using System.Net;
using System.Net.Sockets;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	public interface IServerTransport : IAsyncDisposable
	{
		IPAddress IPAddress { get; }
		int Port { get; }
		IPEndPoint? LocalEndPoint { get; }

		Task StartAsync(CancellationToken token = default);
		Task<ITransportConnection> AcceptConnectionAsync(CancellationToken token = default);
		Task StopAsync();
	}

	public interface IServerTransportFactory
	{
		bool Supports(TransportProtocol protocol);
		IServerTransport Create(GameHostConfig config);
	}

	public sealed class TcpServerTransportFactory : IServerTransportFactory
	{
		public bool Supports(TransportProtocol protocol) => protocol == TransportProtocol.Tcp;
		public IServerTransport Create(GameHostConfig config)
		{
			if (!Supports(config.Networking.Protocol)) throw new NotSupportedException($"Transport '{config.Networking.Protocol}' is not supported by this factory.");
			return new TcpServerTransport(config);
		}
	}

	public sealed class BuiltInServerTransportFactory : IServerTransportFactory
	{
		public bool Supports(TransportProtocol protocol) => protocol is TransportProtocol.Tcp or TransportProtocol.Udp;

		public IServerTransport Create(GameHostConfig config) => config.Networking.Protocol switch
		{
			TransportProtocol.Tcp => new TcpServerTransport(config),
			TransportProtocol.Udp => new UdpServerTransport(config),
			_ => throw new NotSupportedException($"Transport '{config.Networking.Protocol}' is not supported by the built-in factory.")
		};
	}

	public sealed class TcpServerTransport : IServerTransport
	{
		private readonly NetworkingSettings _settings;
		private readonly TcpListener _listener;

		public TcpServerTransport(GameHostConfig config)
		{
			HawkNet.Core.Guard.NotNull(config);
			_settings = config.Networking;
			if (!ServerConfiguration.TryResolveBindAddress(config.Server.Host, out IPAddress? address))
				throw new InvalidOperationException($"Invalid server bind address '{config.Server.Host}'.");
			IPAddress = address!;
			Port = config.Server.Port;
			_listener = new TcpListener(IPAddress, Port);
		}

		public IPAddress IPAddress { get; }
		public int Port { get; }
		public IPEndPoint? LocalEndPoint => _listener.LocalEndpoint as IPEndPoint;

		public Task StartAsync(CancellationToken token = default)
		{
			_listener.Start();
			return Task.CompletedTask;
		}

		public async Task<ITransportConnection> AcceptConnectionAsync(CancellationToken token = default)
		{
#if NET8_0_OR_GREATER
			var client = await _listener.AcceptTcpClientAsync(token);
#else
			var client = await _listener.AcceptTcpClientAsync();
			token.ThrowIfCancellationRequested();
#endif
			ConfigureTcpClient(client);
			return new TcpTransportConnection(client);
		}

		public Task StopAsync()
		{
			_listener.Stop();
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync()
		{
			_listener.Stop();
			return default;
		}

		private void ConfigureTcpClient(TcpClient client)
		{
			client.NoDelay = _settings.NoDelay;
			client.ReceiveBufferSize = _settings.ReceiveBufferSize;
			client.SendBufferSize = _settings.SendBufferSize;
		}
	}
}
