using System.Text;
using System.Text.Json;
using HawkNet.Core;
using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class ChatServiceTests
	{
		[Fact]
		public async Task ChatMessageReceived_StoresChatEntryWithPlayerMetadata()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(dispatcher, CreateSettings());

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(7, Encoding.UTF8.GetBytes("hello")));

			var entry = Assert.Single(chatService.GetHistory());
			Assert.Equal(7, entry.PlayerId);
			Assert.Equal("Player 7", entry.DisplayName);
			Assert.Equal("hello", entry.Message);
			Assert.True(DateTime.UtcNow - entry.TimestampUtc < TimeSpan.FromSeconds(2));
		}

		[Fact]
		public async Task ChatMessageReceived_UsesIdentityServiceDisplayName()
		{
			var dispatcher = new EventDispatcher();
			using var identityService = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());
			identityService.GetOrCreateIdentity(7);
			identityService.TrySetDisplayName(7, "Duncan", out _);
			using var chatService = new ChatService(dispatcher, CreateSettings(), identityService);

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(7, Encoding.UTF8.GetBytes("named hello")));

			var entry = Assert.Single(chatService.GetHistory());
			Assert.Equal("Duncan", entry.DisplayName);
		}

		[Fact]
		public async Task ChatMessageReceived_UsesDisplayNameFromInboundUpdate()
		{
			var dispatcher = new EventDispatcher();
			using var identityService = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());
			using var chatService = new ChatService(dispatcher, CreateSettings(), identityService);
			await dispatcher.DispatchAsync(new PlayerConnectedEvent(14));
			await dispatcher.DispatchAsync(new DisplayNameUpdateReceivedEvent(14, Encoding.UTF8.GetBytes("Nova")));

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(14, Encoding.UTF8.GetBytes("named chat")));

			var entry = Assert.Single(chatService.GetHistory());
			Assert.Equal("Nova", entry.DisplayName);
		}

		[Fact]
		public async Task ChatMessageReceived_CapsHistoryAtConfiguredLimit()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(dispatcher, CreateSettings(maxHistoryMessages: 2));

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("first")));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("second")));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("third")));

			var history = chatService.GetHistory();
			Assert.Equal(2, history.Count);
			Assert.Equal("second", history[0].Message);
			Assert.Equal("third", history[1].Message);
		}

		[Fact]
		public async Task ChatMessageReceived_DispatchesBroadcastMessageEvent()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(dispatcher, CreateSettings());
			BroadcastMessageEvent? broadcastEvent = null;
			dispatcher.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
			{
				broadcastEvent = gameEvent;
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(3, Encoding.UTF8.GetBytes("broadcast me")));

			Assert.NotNull(broadcastEvent);
			Assert.Equal(PacketType.ChatMessage, broadcastEvent.Value.PacketType);
			var entry = JsonSerializer.Deserialize<ChatEntry>(broadcastEvent.Value.Payload);
			Assert.Equal(3, entry.PlayerId);
			Assert.Equal("broadcast me", entry.Message);
		}

		[Fact]
		public async Task ChatMessageReceived_DispatchesParsedChatMessageLoggedEvent()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(dispatcher, CreateSettings());
			ChatMessageLoggedEvent? loggedEvent = null;
			dispatcher.Subscribe<ChatMessageLoggedEvent>((gameEvent, _) =>
			{
				loggedEvent = gameEvent;
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(5, Encoding.UTF8.GetBytes("parsed")));

			Assert.NotNull(loggedEvent);
			Assert.Equal(5, loggedEvent.Value.Entry.PlayerId);
			Assert.Equal("Player 5", loggedEvent.Value.Entry.DisplayName);
			Assert.Equal("parsed", loggedEvent.Value.Entry.Message);
		}


		[Fact]
		public async Task ChatMessageReceived_IgnoresEmptyMessages()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(dispatcher, CreateSettings());
			int broadcastCount = 0;
			dispatcher.Subscribe<BroadcastMessageEvent>((_, _) =>
			{
				broadcastCount++;
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("   ")));

			Assert.Empty(chatService.GetHistory());
			Assert.Equal(0, broadcastCount);
		}

		[Fact]
		public async Task ChatMessageReceived_TrimsMessagesToConfiguredLimit()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(dispatcher, CreateSettings(maxMessageLength: 4));

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("abcdef")));

			var entry = Assert.Single(chatService.GetHistory());
			Assert.Equal("abcd", entry.Message);
		}

		[Fact]
		public async Task ChatMessageReceived_RejectsPayloadOverConfiguredByteLimitBeforeLogging()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(dispatcher, CreateSettings(maxMessageBytes: 4));
			int broadcastCount = 0;
			int loggedCount = 0;
			dispatcher.Subscribe<BroadcastMessageEvent>((_, _) =>
			{
				broadcastCount++;
				return Task.CompletedTask;
			});
			dispatcher.Subscribe<ChatMessageLoggedEvent>((_, _) =>
			{
				loggedCount++;
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("12345")));

			Assert.Empty(chatService.GetHistory());
			Assert.Equal(0, broadcastCount);
			Assert.Equal(0, loggedCount);
		}

		[Fact]
		public async Task ChatMessageReceived_RejectsMessagesOverConfiguredRateLimit()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(
				dispatcher,
				CreateSettings(maxMessagesPerWindow: 2, messageRateWindowSeconds: 60));
			int broadcastCount = 0;
			int loggedCount = 0;
			dispatcher.Subscribe<BroadcastMessageEvent>((_, _) =>
			{
				broadcastCount++;
				return Task.CompletedTask;
			});
			dispatcher.Subscribe<ChatMessageLoggedEvent>((_, _) =>
			{
				loggedCount++;
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(3, Encoding.UTF8.GetBytes("one")));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(3, Encoding.UTF8.GetBytes("two")));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(3, Encoding.UTF8.GetBytes("three")));

			var history = chatService.GetHistory();
			Assert.Equal(2, history.Count);
			Assert.Equal(new[] { "one", "two" }, history.Select(entry => entry.Message));
			Assert.Equal(2, broadcastCount);
			Assert.Equal(2, loggedCount);
		}

		[Fact]
		public async Task PlayerDisconnected_ClearsChatRateLimitState()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(
				dispatcher,
				CreateSettings(maxMessagesPerWindow: 1, messageRateWindowSeconds: 60));

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(5, Encoding.UTF8.GetBytes("first")));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(5, Encoding.UTF8.GetBytes("blocked")));
			await dispatcher.DispatchAsync(new PlayerDisconnectedEvent(5, ConnectionCloseReason.SessionTimeout));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(5, Encoding.UTF8.GetBytes("after reconnect window")));

			var history = chatService.GetHistory();
			Assert.Equal(2, history.Count);
			Assert.Equal("first", history[0].Message);
			Assert.Equal("after reconnect window", history[1].Message);
		}

		[Fact]
		public async Task SendHistoryToPlayerAsync_DispatchesCurrentHistoryToTargetPlayer()
		{
			var dispatcher = new EventDispatcher();
			using var chatService = new ChatService(dispatcher, CreateSettings());
			SendPlayerMessageEvent? sendEvent = null;
			dispatcher.Subscribe<SendPlayerMessageEvent>((gameEvent, _) =>
			{
				sendEvent = gameEvent;
				return Task.CompletedTask;
			});
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("old message")));

			await chatService.SendHistoryToPlayerAsync(9);

			Assert.NotNull(sendEvent);
			Assert.Equal(9, sendEvent.Value.PlayerId);
			Assert.Equal(PacketType.ChatMessage, sendEvent.Value.PacketType);
			var history = JsonSerializer.Deserialize<List<ChatEntry>>(sendEvent.Value.Payload);
			Assert.NotNull(history);
			var entry = Assert.Single(history);
			Assert.Equal("old message", entry.Message);
		}

		[Fact]
		public async Task PlayerDisplayNameChanged_RewritesHistoryAndBroadcastsReplacementHistory()
		{
			var dispatcher = new EventDispatcher();
			using var identityService = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());
			using var chatService = new ChatService(dispatcher, CreateSettings(), identityService);
			BroadcastMessageEvent? replacementEvent = null;
			dispatcher.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
			{
				replacementEvent = gameEvent;
				return Task.CompletedTask;
			});
			await dispatcher.DispatchAsync(new PlayerConnectedEvent(4));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(4, Encoding.UTF8.GetBytes("before rename")));
			replacementEvent = null;

			await dispatcher.DispatchAsync(new DisplayNameUpdateReceivedEvent(4, Encoding.UTF8.GetBytes("Nova")));

			var entry = Assert.Single(chatService.GetHistory());
			Assert.Equal("Nova", entry.DisplayName);
			Assert.NotNull(replacementEvent);
			var history = JsonSerializer.Deserialize<List<ChatEntry>>(replacementEvent.Value.Payload);
			Assert.NotNull(history);
			Assert.Equal("Nova", Assert.Single(history).DisplayName);
		}

		private static ChatSettings CreateSettings(
			int maxHistoryMessages = 100,
			int maxMessageLength = 512,
			int maxMessageBytes = 2048,
			int maxMessagesPerWindow = 5,
			int messageRateWindowSeconds = 10)
		{
			return new ChatSettings
			{
				MaxHistoryMessages = maxHistoryMessages,
				MaxMessageLength = maxMessageLength,
				MaxMessageBytes = maxMessageBytes,
				MaxMessagesPerWindow = maxMessagesPerWindow,
				MessageRateWindowSeconds = messageRateWindowSeconds
			};
		}
	}
}
