using System.Net;
using System.Net.Sockets;
using System.Text;
using HawkNet.Core;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	public class ClientNetworkMessageHandlerTests
	{
		[Fact]
		public async Task Connected_DispatchesClientConnectedEventWithHandshakeData()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			using var handler = new ClientNetworkMessageHandler(client, dispatcher);
			var connectedTcs = new TaskCompletionSource<ClientConnectedEvent>();
			dispatcher.Subscribe<ClientConnectedEvent>((gameEvent, _) =>
			{
				connectedTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			handler.Start();

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			byte[] statePayload = Encoding.UTF8.GetBytes("state");
			await SendHandshakeResponseAsync(stream, 12, CreateReconnectToken(3), "chat_lobby", statePayload);
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			var connected = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(12, connected.PlayerId);
			Assert.Equal("chat_lobby", connected.GameStateId);
			Assert.Equal(statePayload, connected.GameStatePayload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task PacketReceived_DispatchesClientPacketReceivedEvent()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			using var handler = new ClientNetworkMessageHandler(client, dispatcher);
			var packetTcs = new TaskCompletionSource<ClientPacketReceivedEvent>();
			dispatcher.Subscribe<ClientPacketReceivedEvent>((gameEvent, _) =>
			{
				packetTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			handler.Start();

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 12, CreateReconnectToken(3));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			byte[] payload = Encoding.UTF8.GetBytes("hello");
			await stream.WriteAsync(SignalShared.BuildPacket(PacketType.ChatMessage, payload));

			var packet = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(PacketType.ChatMessage, packet.PacketType);
			Assert.Equal(payload, packet.Payload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task GameStateChangePacket_DispatchesParsedStateChangeEventAndRawPacketEvent()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			using var handler = new ClientNetworkMessageHandler(client, dispatcher);
			var stateChangeTcs = new TaskCompletionSource<ClientGameStateChangeReceivedEvent>();
			var packetTcs = new TaskCompletionSource<ClientPacketReceivedEvent>();
			dispatcher.Subscribe<ClientGameStateChangeReceivedEvent>((gameEvent, _) =>
			{
				stateChangeTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			dispatcher.Subscribe<ClientPacketReceivedEvent>((gameEvent, _) =>
			{
				packetTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			handler.Start();

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 12, CreateReconnectToken(3));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			byte[] statePayload = Encoding.UTF8.GetBytes("state payload");
			byte[] payload = GameStateProtocol.EncodeChange(new GameStateChangeResponse
			{
				GameStateId = "match",
				GameStatePayload = statePayload
			});
			await stream.WriteAsync(SignalShared.BuildPacket(PacketType.GameStateChange, payload));

			var stateChange = await stateChangeTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal("match", stateChange.GameStateId);
			Assert.Equal(statePayload, stateChange.GameStatePayload);

			var rawPacket = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(PacketType.GameStateChange, rawPacket.PacketType);
			Assert.Equal(payload, rawPacket.Payload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task GameStateChangePacket_WithInvalidPayload_DoesNotDispatchParsedStateChangeEvent()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			using var handler = new ClientNetworkMessageHandler(client, dispatcher);
			var stateChangeTcs = new TaskCompletionSource<ClientGameStateChangeReceivedEvent>();
			var packetTcs = new TaskCompletionSource<ClientPacketReceivedEvent>();
			dispatcher.Subscribe<ClientGameStateChangeReceivedEvent>((gameEvent, _) =>
			{
				stateChangeTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			dispatcher.Subscribe<ClientPacketReceivedEvent>((gameEvent, _) =>
			{
				packetTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			handler.Start();

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 12, CreateReconnectToken(3));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			byte[] payload = Encoding.UTF8.GetBytes("{bad json");
			await stream.WriteAsync(SignalShared.BuildPacket(PacketType.GameStateChange, payload));

			var rawPacket = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(PacketType.GameStateChange, rawPacket.PacketType);
			Assert.False(stateChangeTcs.Task.IsCompleted);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task GameStateMessagePacket_DispatchesParsedGameStateMessageEventAndRawPacketEvent()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			using var handler = new ClientNetworkMessageHandler(client, dispatcher, codec);
			var messageTcs = new TaskCompletionSource<ClientGameStateMessageReceivedEvent>();
			var packetTcs = new TaskCompletionSource<ClientPacketReceivedEvent>();
			dispatcher.Subscribe<ClientGameStateMessageReceivedEvent>((gameEvent, _) =>
			{
				messageTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			dispatcher.Subscribe<ClientPacketReceivedEvent>((gameEvent, _) =>
			{
				packetTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			handler.Start();

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 12, CreateReconnectToken(3));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			byte[] innerPayload = Encoding.UTF8.GetBytes("inner");
			byte[] payload = GameStateMessageProtocol.Encode(new GameStateMessage
			{
				MessageId = "game.event",
				Payload = innerPayload
			}, codec);
			await stream.WriteAsync(SignalShared.BuildPacket(PacketType.GameStateMessage, payload));

			var message = await messageTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal("game.event", message.MessageId);
			Assert.Equal(innerPayload, message.Payload);

			var rawPacket = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(PacketType.GameStateMessage, rawPacket.PacketType);
			Assert.Equal(payload, rawPacket.Payload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task Disconnected_DispatchesClientDisconnectedEvent()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			using var handler = new ClientNetworkMessageHandler(client, dispatcher);
			var disconnectedTcs = new TaskCompletionSource<ClientDisconnectedEvent>();
			dispatcher.Subscribe<ClientDisconnectedEvent>((gameEvent, _) =>
			{
				disconnectedTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			handler.Start();

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 12, CreateReconnectToken(3));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			await client.DisconnectAsync();

			await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
		}

		[Fact]
		public async Task PacketDispatch_ContinuesWhenOneSubscriberFails()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			using var handler = new ClientNetworkMessageHandler(client, dispatcher);
			var packetTcs = new TaskCompletionSource<ClientPacketReceivedEvent>();
			dispatcher.Subscribe<ClientPacketReceivedEvent>((_, _) =>
			{
				throw new InvalidOperationException("boom");
			});
			dispatcher.Subscribe<ClientPacketReceivedEvent>((gameEvent, _) =>
			{
				packetTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			handler.Start();

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 12, CreateReconnectToken(3));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			byte[] payload = Encoding.UTF8.GetBytes("still routed");
			await stream.WriteAsync(SignalShared.BuildPacket(PacketType.PlayerInput, payload));

			var packet = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(PacketType.PlayerInput, packet.PacketType);
			Assert.Equal(payload, packet.Payload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task Stop_UnsubscribesFromGameClientEvents()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			using var handler = new ClientNetworkMessageHandler(client, dispatcher);
			var connectedTcs = new TaskCompletionSource<ClientConnectedEvent>();
			dispatcher.Subscribe<ClientConnectedEvent>((gameEvent, _) =>
			{
				connectedTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});
			handler.Start();
			handler.Stop();

			var connectTask = client.ConnectAsync();

			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 12, CreateReconnectToken(3));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			await Task.Delay(100);
			Assert.False(connectedTcs.Task.IsCompleted);

			await client.DisconnectAsync();
		}

		private static GameClient CreateClient(int port)
		{
			return new GameClient(new GameClientConfig
			{
				Server = new GameClientServerSettings
				{
					Host = "127.0.0.1",
					Port = port
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
