using System.Text;
using HawkNet.Core;
using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class InboundMessageRouterTests
	{
		[Fact]
		public async Task Route_PlayerInput_PublishesPlayerInputEvent()
		{
			var eventDispatcher = new EventDispatcher();
			var router = new InboundMessageRouter(eventDispatcher);
			PlayerInputReceivedEvent? receivedEvent = null;
			eventDispatcher.Subscribe<PlayerInputReceivedEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});
			byte[] payloadBytes = Encoding.UTF8.GetBytes("input");

			bool routed = await router.RouteAsync(42, PacketType.PlayerInput, payloadBytes);

			Assert.True(routed);
			Assert.NotNull(receivedEvent);
			Assert.Equal(42, receivedEvent.Value.PlayerId);
			Assert.Equal(payloadBytes, receivedEvent.Value.Payload);
		}

		[Fact]
		public async Task Route_ChatMessage_PublishesChatEvent()
		{
			var eventDispatcher = new EventDispatcher();
			var router = new InboundMessageRouter(eventDispatcher);
			ChatMessageReceivedEvent? receivedEvent = null;
			eventDispatcher.Subscribe<ChatMessageReceivedEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});
			byte[] payloadBytes = Encoding.UTF8.GetBytes("chat");

			bool routed = await router.RouteAsync(7, PacketType.ChatMessage, payloadBytes);

			Assert.True(routed);
			Assert.NotNull(receivedEvent);
			Assert.Equal(7, receivedEvent.Value.PlayerId);
			Assert.Equal(payloadBytes, receivedEvent.Value.Payload);
		}

		[Fact]
		public async Task Route_DisplayNameUpdate_PublishesDisplayNameEvent()
		{
			var eventDispatcher = new EventDispatcher();
			var router = new InboundMessageRouter(eventDispatcher);
			DisplayNameUpdateReceivedEvent? receivedEvent = null;
			eventDispatcher.Subscribe<DisplayNameUpdateReceivedEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});
			byte[] payloadBytes = Encoding.UTF8.GetBytes("Pilot");

			bool routed = await router.RouteAsync(9, PacketType.DisplayNameUpdate, payloadBytes);

			Assert.True(routed);
			Assert.NotNull(receivedEvent);
			Assert.Equal(9, receivedEvent.Value.PlayerId);
			Assert.Equal(payloadBytes, receivedEvent.Value.Payload);
		}

		[Fact]
		public async Task Route_MatchCommandRequest_PublishesMatchCommandEvent()
		{
			var eventDispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			var router = new InboundMessageRouter(eventDispatcher, payloadCodec: codec);
			MatchCommandReceivedEvent? receivedEvent = null;
			eventDispatcher.Subscribe<MatchCommandReceivedEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});
			byte[] commandPayload = [1, 2, 3];
			byte[] payloadBytes = codec.Encode(new MatchCommandRequest
			{
				CommandId = "move",
				Payload = commandPayload
			});

			bool routed = await router.RouteAsync(11, PacketType.MatchCommandRequest, payloadBytes);

			Assert.True(routed);
			Assert.NotNull(receivedEvent);
			Assert.Equal(11, receivedEvent.Value.PlayerId);
			Assert.Equal("move", receivedEvent.Value.CommandId);
			Assert.Equal(commandPayload, receivedEvent.Value.Payload);
		}

		[Fact]
		public async Task Route_InvalidMatchCommandRequest_PublishesRejectedEvent()
		{
			var eventDispatcher = new EventDispatcher();
			var router = new InboundMessageRouter(eventDispatcher);
			MatchCommandRejectedEvent? rejectedEvent = null;
			eventDispatcher.Subscribe<MatchCommandRejectedEvent>((gameEvent, _) =>
			{
				rejectedEvent = gameEvent;
				return Task.CompletedTask;
			});

			bool routed = await router.RouteAsync(11, PacketType.MatchCommandRequest, [1, 2, 3]);

			Assert.False(routed);
			Assert.NotNull(rejectedEvent);
			Assert.Equal(11, rejectedEvent.Value.PlayerId);
			Assert.Contains("Invalid", rejectedEvent.Value.Reason);
		}

		[Fact]
		public async Task Route_GameStateMessage_PublishesGameStateMessageEvent()
		{
			var eventDispatcher = new EventDispatcher();
			var codec = new JsonPayloadCodec();
			var router = new InboundMessageRouter(eventDispatcher, payloadCodec: codec);
			GameStateMessageReceivedEvent? receivedEvent = null;
			eventDispatcher.Subscribe<GameStateMessageReceivedEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});
			byte[] messagePayload = [1, 2, 3];
			byte[] payloadBytes = GameStateMessageProtocol.Encode(new GameStateMessage
			{
				MessageId = "game.move",
				Payload = messagePayload
			}, codec);

			bool routed = await router.RouteAsync(11, PacketType.GameStateMessage, payloadBytes);

			Assert.True(routed);
			Assert.NotNull(receivedEvent);
			Assert.Equal(11, receivedEvent.Value.PlayerId);
			Assert.Equal("game.move", receivedEvent.Value.MessageId);
			Assert.Equal(messagePayload, receivedEvent.Value.Payload);
		}

		[Fact]
		public async Task Route_InvalidGameStateMessage_ReturnsFalse()
		{
			var eventDispatcher = new EventDispatcher();
			var router = new InboundMessageRouter(eventDispatcher);
			GameStateMessageReceivedEvent? receivedEvent = null;
			eventDispatcher.Subscribe<GameStateMessageReceivedEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});

			bool routed = await router.RouteAsync(11, PacketType.GameStateMessage, [1, 2, 3]);

			Assert.False(routed);
			Assert.Null(receivedEvent);
		}

		[Theory]
		[InlineData(PacketType.HeartBeat)]
		[InlineData(PacketType.ReconnectRequest)]
		[InlineData(PacketType.Unknown)]
		public async Task Route_NonGameplayPackets_DoNotPublishGameplayEvents(PacketType packetType)
		{
			var eventDispatcher = new EventDispatcher();
			var router = new InboundMessageRouter(eventDispatcher);
			int gameplayEventCount = 0;
			eventDispatcher.Subscribe<PlayerInputReceivedEvent>((_, _) =>
			{
				gameplayEventCount++;
				return Task.CompletedTask;
			});
			eventDispatcher.Subscribe<ChatMessageReceivedEvent>((_, _) =>
			{
				gameplayEventCount++;
				return Task.CompletedTask;
			});

			bool routed = await router.RouteAsync(1, packetType, Array.Empty<byte>());

			Assert.False(routed);
			Assert.Equal(0, gameplayEventCount);
		}
	}
}
