using System.Net;
using System.Net.Sockets;
using System.Text;
using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class MessageDispatcherTests
	{
		[Fact]
		public async Task SendToPlayerAsync_WritesPacketToConnectedPlayer()
		{
			await using var pair = await ConnectedClientPair.CreateAsync(playerId: 1);
			var dispatcher = new MessageDispatcher(pair.ConnectionManager);
			byte[] payload = Encoding.UTF8.GetBytes("hello player");

			await dispatcher.SendToPlayerAsync(1, PacketType.ChatMessage, payload);

			var result = await SignalShared.ReadNetworkStream(pair.Client.GetStream()).WaitAsync(TimeSpan.FromSeconds(2));
			Assert.True(result.Success);
			Assert.Equal(PacketType.ChatMessage, result.PacketType);
			Assert.Equal(payload, result.Payload);
		}

		[Fact]
		public async Task SendToPlayerAsync_SkipsMissingPlayer()
		{
			var manager = new ConnectionManager(IPAddress.Loopback, 0);
			var dispatcher = new MessageDispatcher(manager);

			await dispatcher.SendToPlayerAsync(999, PacketType.ChatMessage, Encoding.UTF8.GetBytes("missing"));
		}

		[Fact]
		public async Task SendToPlayerAsync_SkipsDisconnectedSession()
		{
			var manager = new ConnectionManager(IPAddress.Loopback, 0);
			manager.RegisterSession(new PlayerSession
			{
				PlayerId = 1,
				Connection = null,
				LastActive = DateTime.UtcNow
			});
			var dispatcher = new MessageDispatcher(manager);

			await dispatcher.SendToPlayerAsync(1, PacketType.ChatMessage, Encoding.UTF8.GetBytes("disconnected"));
		}

		[Fact]
		public async Task BroadcastAsync_WritesPacketToAllConnectedPlayers()
		{
			await using var firstPair = await ConnectedClientPair.CreateAsync(playerId: 1);
			await using var secondPair = await ConnectedClientPair.CreateAsync(firstPair.ConnectionManager, playerId: 2);
			var dispatcher = new MessageDispatcher(firstPair.ConnectionManager);
			byte[] payload = Encoding.UTF8.GetBytes("broadcast");

			await dispatcher.BroadcastAsync(PacketType.ChatMessage, payload);

			var firstResult = await SignalShared.ReadNetworkStream(firstPair.Client.GetStream()).WaitAsync(TimeSpan.FromSeconds(2));
			var secondResult = await SignalShared.ReadNetworkStream(secondPair.Client.GetStream()).WaitAsync(TimeSpan.FromSeconds(2));

			Assert.True(firstResult.Success);
			Assert.Equal(PacketType.ChatMessage, firstResult.PacketType);
			Assert.Equal(payload, firstResult.Payload);
			Assert.True(secondResult.Success);
			Assert.Equal(PacketType.ChatMessage, secondResult.PacketType);
			Assert.Equal(payload, secondResult.Payload);
		}

		[Fact]
		public async Task BroadcastAsync_SkipsDisconnectedSessions()
		{
			await using var pair = await ConnectedClientPair.CreateAsync(playerId: 1);
			pair.ConnectionManager.RegisterSession(new PlayerSession
			{
				PlayerId = 2,
				Connection = null,
				LastActive = DateTime.UtcNow
			});
			var dispatcher = new MessageDispatcher(pair.ConnectionManager);
			byte[] payload = Encoding.UTF8.GetBytes("broadcast connected only");

			await dispatcher.BroadcastAsync(PacketType.ChatMessage, payload);

			var result = await SignalShared.ReadNetworkStream(pair.Client.GetStream()).WaitAsync(TimeSpan.FromSeconds(2));
			Assert.True(result.Success);
			Assert.Equal(PacketType.ChatMessage, result.PacketType);
			Assert.Equal(payload, result.Payload);
		}

		[Fact]
		public async Task SendToPlayersAsync_WritesPacketOnlyToSelectedConnectedPlayers()
		{
			await using var firstPair = await ConnectedClientPair.CreateAsync(playerId: 1);
			await using var secondPair = await ConnectedClientPair.CreateAsync(firstPair.ConnectionManager, playerId: 2);
			var dispatcher = new MessageDispatcher(firstPair.ConnectionManager);
			byte[] payload = Encoding.UTF8.GetBytes("selected");

			await dispatcher.SendToPlayersAsync(new[] { 2, 999 }, PacketType.PlayerInput, payload);

			var secondResult = await SignalShared.ReadNetworkStream(secondPair.Client.GetStream()).WaitAsync(TimeSpan.FromSeconds(2));
			await Task.Delay(100);

			Assert.True(secondResult.Success);
			Assert.Equal(PacketType.PlayerInput, secondResult.PacketType);
			Assert.Equal(payload, secondResult.Payload);
			Assert.Equal(0, firstPair.Client.Available);
		}

		private sealed class ConnectedClientPair : IAsyncDisposable
		{
			private readonly TcpClient _serverTcpClient;
			private readonly ClientConnection _serverConnection;

			public ConnectionManager ConnectionManager { get; }
			public TcpClient Client { get; }

			private ConnectedClientPair(
				ConnectionManager connectionManager,
				TcpClient client,
				TcpClient serverTcpClient,
				ClientConnection serverConnection)
			{
				ConnectionManager = connectionManager;
				Client = client;
				_serverTcpClient = serverTcpClient;
				_serverConnection = serverConnection;
			}

			public static Task<ConnectedClientPair> CreateAsync(int playerId)
			{
				return CreateAsync(new ConnectionManager(IPAddress.Loopback, 0), playerId);
			}

			public static async Task<ConnectedClientPair> CreateAsync(ConnectionManager connectionManager, int playerId)
			{
				var listener = new TcpListener(IPAddress.Loopback, 0);
				listener.Start();
				int port = ((IPEndPoint)listener.LocalEndpoint).Port;

				var client = new TcpClient();
				var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
				var serverTcpClient = await listener.AcceptTcpClientAsync();
				await connectTask;
				listener.Stop();

				var serverConnection = new ClientConnection(serverTcpClient, connectionManager);
				connectionManager.RegisterSession(new PlayerSession
				{
					PlayerId = playerId,
					Connection = serverConnection,
					LastActive = DateTime.UtcNow
				});

				return new ConnectedClientPair(connectionManager, client, serverTcpClient, serverConnection);
			}

			public async ValueTask DisposeAsync()
			{
				await _serverConnection.DisconnectAsync();
				Client.Close();
				_serverTcpClient.Close();
			}
		}
	}
}
