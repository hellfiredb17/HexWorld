using System.Net;
using System.Net.Sockets;
using HawkNet.Desktop.Services;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class DesktopClientServerDiscoveryTests
	{
		[Fact]
		public async Task SearchAsync_FindsOpenTcpEndpoint()
		{
			using var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
			var discovery = new DesktopClientServerDiscovery(TimeSpan.FromSeconds(1));

			IReadOnlyList<CoreUx.Client.Services.FoundServer> results = await discovery.SearchAsync(port);

			using TcpClient accepted = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Contains(results, result =>
				result.Host == IPAddress.Loopback.ToString()
				&& result.Port == port);
			Assert.All(results, result => Assert.Equal(port, result.Port));
		}

		[Fact]
		public async Task SearchAsync_ReturnsEmptyForClosedEndpoint()
		{
			int port = GetAvailablePort();
			var discovery = new DesktopClientServerDiscovery(TimeSpan.FromMilliseconds(200));

			IReadOnlyList<CoreUx.Client.Services.FoundServer> results = await discovery.SearchAsync(port);

			Assert.Empty(results);
		}

		[Fact]
		public async Task SearchAsync_FindsUdpHawkNetEndpoint()
		{
			int port = GetAvailableUdpPort();
			var transport = new UdpServerTransport(new GameHostConfig
			{
				Server = new ServerSettings { Host = "127.0.0.1", Port = port },
				Networking = new NetworkingSettings { Protocol = TransportProtocol.Udp }
			});
			await transport.StartAsync();
			try
			{
				var discovery = new DesktopClientServerDiscovery(TimeSpan.FromMilliseconds(300));

				IReadOnlyList<CoreUx.Client.Services.FoundServer> results =
					await discovery.SearchAsync(port, TransportProtocol.Udp);

				Assert.Contains(results, result =>
					result.Host == IPAddress.Loopback.ToString() && result.Port == port);
			}
			finally
			{
				await transport.DisposeAsync();
			}
		}

		private static int GetAvailablePort()
		{
			using var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}

		private static int GetAvailableUdpPort()
		{
			using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
			return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
		}
	}
}
