using System.Net;
using System.Net.Sockets;
using System.Text;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	public class GameClientTests
	{
		[Fact]
		public async Task ConnectAsync_SendsSessionRequestAndStoresServerConfirmation()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var connectedTcs = new TaskCompletionSource();
			client.Connected += () => connectedTcs.TrySetResult();

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			var sessionRequest = await ReadPacketAsync(stream);
			Assert.Equal(PacketType.HandshakeRequest, sessionRequest.PacketType);
			Assert.Empty(sessionRequest.Payload);

			byte[] reconnectToken = CreateReconnectToken(7);
			byte[] statePayload = Encoding.UTF8.GetBytes("state data");
			await SendHandshakeResponseAsync(stream, playerId: 42, reconnectToken, "chat_lobby", statePayload);

			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));
			await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

			Assert.Equal(GameClientState.Connected, client.State);
			Assert.True(client.IsConnected);
			Assert.Equal(42, client.PlayerId);
			Assert.True(client.HasReconnectToken);
			Assert.Equal("chat_lobby", client.CurrentGameStateId);
			Assert.Equal(statePayload, client.CurrentGameStatePayload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task ReceivePacket_RaisesPacketReceived()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var receivedTcs = new TaskCompletionSource<(PacketType, byte[])>();
			client.PacketReceived += (type, payload) => receivedTcs.TrySetResult((type, payload));

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, playerId: 42, CreateReconnectToken(7));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			var payload = Encoding.UTF8.GetBytes("hello from server");
			await stream.WriteAsync(SignalShared.BuildPacket(PacketType.ChatMessage, payload));

			var (packetType, receivedPayload) = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(PacketType.ChatMessage, packetType);
			Assert.Equal(payload, receivedPayload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task SendPacketAsync_WritesToServerStream()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, playerId: 42, CreateReconnectToken(7));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			var payload = Encoding.UTF8.GetBytes("msg from client");
			await client.SendPacketAsync(PacketType.ChatMessage, payload);

			var packet = await ReadPacketAsync(stream).WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(PacketType.ChatMessage, packet.PacketType);
			Assert.Equal(payload, packet.Payload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task HeartbeatLoop_SendsHeaderOnlyHeartbeatAfterConnectionConfirmation()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port, heartbeatIntervalSeconds: 1);

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, playerId: 42, CreateReconnectToken(7));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			var heartbeat = await ReadPacketAsync(stream).WaitAsync(TimeSpan.FromSeconds(3));
			Assert.Equal(PacketType.HeartBeat, heartbeat.PacketType);
			Assert.Empty(heartbeat.Payload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task DisconnectAsync_IsIdempotentAndRaisesDisconnectedOnce()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			int disconnectedCount = 0;
			client.Disconnected += () => Interlocked.Increment(ref disconnectedCount);

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, playerId: 42, CreateReconnectToken(7));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			await client.DisconnectAsync();
			await client.DisconnectAsync();

			Assert.Equal(GameClientState.Disconnected, client.State);
			Assert.False(client.IsConnected);
			Assert.Equal(1, disconnectedCount);
		}

		[Fact]
		public async Task ReconnectAsync_SendsStoredPlayerIdAndReconnectToken()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var reconnectedTcs = new TaskCompletionSource();
			client.Reconnected += () => reconnectedTcs.TrySetResult();

			byte[] reconnectToken = CreateReconnectToken(9);
			var connectTask = client.ConnectAsync();

			using(var firstServerClient = await listener.AcceptTcpClientAsync())
			{
				var firstStream = firstServerClient.GetStream();
				await ReadPacketAsync(firstStream);
				await SendHandshakeResponseAsync(firstStream, playerId: 42, reconnectToken);
				await connectTask.WaitAsync(TimeSpan.FromSeconds(2));
			}

			await client.DisconnectAsync();

			var reconnectTask = client.ReconnectAsync();
			using var secondServerClient = await listener.AcceptTcpClientAsync();
			var secondStream = secondServerClient.GetStream();
			var reconnectRequest = await ReadPacketAsync(secondStream).WaitAsync(TimeSpan.FromSeconds(2));

			Assert.Equal(PacketType.ReconnectRequest, reconnectRequest.PacketType);
			Assert.Equal(ConnectionManager.ReconnectPayloadSize, reconnectRequest.Payload.Length);
			Assert.Equal(42, BitConverter.ToInt32(reconnectRequest.Payload, 0));
			Assert.Equal(reconnectToken, reconnectRequest.Payload.Skip(sizeof(int)).ToArray());

			await SendHandshakeResponseAsync(secondStream, playerId: 42, reconnectToken);
			await reconnectTask.WaitAsync(TimeSpan.FromSeconds(2));
			await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

			Assert.Equal(GameClientState.Connected, client.State);
			Assert.True(client.IsConnected);

			await client.DisconnectAsync();
		}

		private static GameClient CreateClient(int port, int heartbeatIntervalSeconds = 15)
		{
			return new GameClient(new GameClientConfig
			{
				Server = new GameClientServerSettings
				{
					Host = "127.0.0.1",
					Port = port
				},
				Networking = new GameClientNetworkingSettings
				{
					HeartbeatIntervalSeconds = heartbeatIntervalSeconds
				}
			});
		}

		private static TcpListener StartListener(out int port)
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			port = ((IPEndPoint)listener.LocalEndpoint).Port;
			return listener;
		}

		private static async Task<ReadNetworkPacketResult> ReadPacketAsync(NetworkStream stream)
		{
			var result = await SignalShared.ReadNetworkStream(stream).WaitAsync(TimeSpan.FromSeconds(2));
			Assert.True(result.Success, result.ErrorMsg);
			return result;
		}

		private static Task SendHandshakeResponseAsync(
			NetworkStream stream,
			int playerId,
			byte[] reconnectToken,
			string gameStateId = "chat_lobby",
			byte[]? gameStatePayload = null)
		{
			byte[] payload = HandshakeProtocol.EncodeResponse(new HandshakeResponse
			{
				PlayerId = playerId,
				ReconnectToken = reconnectToken,
				Runtime = new ClientRuntimeManifest
				{
					Services = ["player_identity", "diagnostics", "chat_service"],
					GameStates = ["chat_lobby"],
					InitialGameStateId = gameStateId,
					InitialGameStatePayload = gameStatePayload ?? Array.Empty<byte>()
				}
			});
			byte[] packet = SignalShared.BuildPacket(PacketType.HandshakeResponse, payload);
			return stream.WriteAsync(packet, 0, packet.Length);
		}

		private static byte[] CreateReconnectToken(byte seed)
		{
			return Enumerable.Range(0, ConnectionManager.ReconnectTokenSize)
				.Select(index => (byte)(seed + index))
				.ToArray();
		}
	}
}
