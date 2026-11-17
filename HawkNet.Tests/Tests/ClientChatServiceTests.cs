using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HawkNet.Core;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	public class ClientChatServiceTests
	{
		[Fact]
		public async Task SendMessageAsync_SendsChatPacket()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			using var chatService = new ClientChatService(client, dispatcher);

			var connectTask = client.ConnectAsync();
			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 1, CreateReconnectToken(1));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			await chatService.SendMessageAsync("hello chat");

			var packet = await ReadPacketAsync(stream);
			Assert.Equal(PacketType.ChatMessage, packet.PacketType);
			Assert.Equal("hello chat", Encoding.UTF8.GetString(packet.Payload));

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task DisplayNameChangedEvent_SendsDisplayNameUpdatePacket()
		{
			using var listener = StartListener(out int port);
			var client = CreateClient(port);
			var dispatcher = new EventDispatcher();
			using var chatService = new ClientChatService(client, dispatcher);

			var connectTask = client.ConnectAsync();
			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 1, CreateReconnectToken(1));
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			await dispatcher.DispatchAsync(new ClientDisplayNameChangedEvent(1, "Nova"));

			var packet = await ReadPacketAsync(stream);
			Assert.Equal(PacketType.DisplayNameUpdate, packet.PacketType);
			Assert.Equal("Nova", Encoding.UTF8.GetString(packet.Payload));

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task ChatPacket_WithSingleEntry_AddsHistoryAndDispatchesMessageEvent()
		{
			var client = CreateUnconnectedClient();
			var dispatcher = new EventDispatcher();
			using var chatService = new ClientChatService(client, dispatcher);
			var entry = new ChatEntry(4, "Nova", DateTime.UtcNow, "hello");
			var messageTcs = new TaskCompletionSource<ClientChatMessageReceivedEvent>();
			dispatcher.Subscribe<ClientChatMessageReceivedEvent>((gameEvent, _) =>
			{
				messageTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new ClientPacketReceivedEvent(
				PacketType.ChatMessage,
				JsonSerializer.SerializeToUtf8Bytes(entry)));

			var message = await messageTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(entry, message.Entry);
			Assert.Equal(entry, Assert.Single(chatService.GetHistory()));
		}

		[Fact]
		public async Task ChatPacket_WithHistory_ReplacesHistoryAndDispatchesHistoryEvent()
		{
			var client = CreateUnconnectedClient();
			var dispatcher = new EventDispatcher();
			using var chatService = new ClientChatService(client, dispatcher);
			var history = new[]
			{
				new ChatEntry(1, "One", DateTime.UtcNow, "first"),
				new ChatEntry(2, "Two", DateTime.UtcNow, "second")
			};
			var historyTcs = new TaskCompletionSource<ClientChatHistoryReceivedEvent>();
			dispatcher.Subscribe<ClientChatHistoryReceivedEvent>((gameEvent, _) =>
			{
				historyTcs.TrySetResult(gameEvent);
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new ClientPacketReceivedEvent(
				PacketType.ChatMessage,
				JsonSerializer.SerializeToUtf8Bytes(history)));

			var receivedHistory = await historyTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
			Assert.Equal(history, receivedHistory.History);
			Assert.Equal(history, chatService.GetHistory());
		}

		[Fact]
		public async Task ChatPacket_WithNewEntry_AppendsAfterHistory()
		{
			var client = CreateUnconnectedClient();
			var dispatcher = new EventDispatcher();
			using var chatService = new ClientChatService(client, dispatcher);
			var history = new[]
			{
				new ChatEntry(1, "One", DateTime.UtcNow, "first")
			};
			var nextEntry = new ChatEntry(2, "Two", DateTime.UtcNow, "second");

			await dispatcher.DispatchAsync(new ClientPacketReceivedEvent(
				PacketType.ChatMessage,
				JsonSerializer.SerializeToUtf8Bytes(history)));
			await dispatcher.DispatchAsync(new ClientPacketReceivedEvent(
				PacketType.ChatMessage,
				JsonSerializer.SerializeToUtf8Bytes(nextEntry)));

			Assert.Equal(new[] { history[0], nextEntry }, chatService.GetHistory());
		}

		[Fact]
		public async Task NonChatPacket_IsIgnored()
		{
			var client = CreateUnconnectedClient();
			var dispatcher = new EventDispatcher();
			using var chatService = new ClientChatService(client, dispatcher);
			int messageEvents = 0;
			dispatcher.Subscribe<ClientChatMessageReceivedEvent>((_, _) =>
			{
				messageEvents++;
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new ClientPacketReceivedEvent(
				PacketType.PlayerInput,
				Encoding.UTF8.GetBytes("ignored")));

			Assert.Empty(chatService.GetHistory());
			Assert.Equal(0, messageEvents);
		}

		[Fact]
		public async Task InvalidChatPayload_IsIgnored()
		{
			var client = CreateUnconnectedClient();
			var dispatcher = new EventDispatcher();
			using var chatService = new ClientChatService(client, dispatcher);
			int messageEvents = 0;
			dispatcher.Subscribe<ClientChatMessageReceivedEvent>((_, _) =>
			{
				messageEvents++;
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new ClientPacketReceivedEvent(
				PacketType.ChatMessage,
				Encoding.UTF8.GetBytes("{not json")));

			Assert.Empty(chatService.GetHistory());
			Assert.Equal(0, messageEvents);
		}

		private static GameClient CreateUnconnectedClient()
		{
			return new GameClient("127.0.0.1", 1);
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

		private static Task SendHandshakeResponseAsync(NetworkStream stream, int playerId, byte[] reconnectToken)
		{
			byte[] payload = HandshakeProtocol.EncodeResponse(new HandshakeResponse
			{
				PlayerId = playerId,
				ReconnectToken = reconnectToken,
				Runtime = new ClientRuntimeManifest
				{
					Services = ["player_identity", "diagnostics", "chat_service"],
					GameStates = ["chat_lobby"],
					InitialGameStateId = "chat_lobby",
					InitialGameStatePayload = Array.Empty<byte>()
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
