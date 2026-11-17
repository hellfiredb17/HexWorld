using HawkNet.Core;
using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class GameStateTests
	{
		[Fact]
		public async Task SendMessageAsync_DispatchesSendPlayerMessageEvent()
		{
			var dispatcher = new EventDispatcher();
			var state = new TestGameState(dispatcher);
			SendPlayerMessageEvent? receivedEvent = null;
			dispatcher.Subscribe<SendPlayerMessageEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});
			byte[] payload = { 1, 2, 3 };

			await state.SendMessageAsync(12, PacketType.PlayerInput, payload);

			Assert.NotNull(receivedEvent);
			Assert.Equal(12, receivedEvent.Value.PlayerId);
			Assert.Equal(PacketType.PlayerInput, receivedEvent.Value.PacketType);
			Assert.Equal(payload, receivedEvent.Value.Payload);
		}

		[Fact]
		public async Task BroadcastAsync_DispatchesBroadcastMessageEvent()
		{
			var dispatcher = new EventDispatcher();
			var state = new TestGameState(dispatcher);
			BroadcastMessageEvent? receivedEvent = null;
			dispatcher.Subscribe<BroadcastMessageEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});
			byte[] payload = { 4, 5, 6 };

			await state.BroadcastAsync(PacketType.ChatMessage, payload);

			Assert.NotNull(receivedEvent);
			Assert.Equal(PacketType.ChatMessage, receivedEvent.Value.PacketType);
			Assert.Equal(payload, receivedEvent.Value.Payload);
		}

		[Fact]
		public async Task RequestTransitionAsync_DispatchesTransitionRequestEvent()
		{
			var dispatcher = new EventDispatcher();
			var state = new TestGameState(dispatcher);
			GameStateTransitionRequestedEvent? receivedEvent = null;
			dispatcher.Subscribe<GameStateTransitionRequestedEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});
			byte[] payload = { 7, 8, 9 };

			await state.RequestTransitionForTestAsync("match", payload);

			Assert.NotNull(receivedEvent);
			Assert.Equal("match", receivedEvent.Value.ToStateId);
			Assert.Equal(payload, receivedEvent.Value.Payload);
		}

		[Fact]
		public async Task Dispose_UnsubscribesTrackedSubscriptions()
		{
			var dispatcher = new EventDispatcher();
			var state = new TestGameState(dispatcher);
			state.SubscribeToPlayerInput();

			state.Dispose();
			await dispatcher.DispatchAsync(new PlayerInputReceivedEvent(1, Array.Empty<byte>()));

			Assert.Equal(0, state.PlayerInputCount);
		}

		private sealed class TestGameState : GameState
		{
			public int PlayerInputCount { get; private set; }

			public TestGameState(IEventDispatcher eventDispatcher)
				: base(eventDispatcher)
			{
			}

			public override void Enter(byte[] transitionPayload)
			{
			}

			public override void Update(float deltaTime)
			{
			}

			public override void Exit()
			{
			}

			public void SubscribeToPlayerInput()
			{
				Subscribe<PlayerInputReceivedEvent>((_, _) =>
				{
					PlayerInputCount++;
					return Task.CompletedTask;
				});
			}

			public Task RequestTransitionForTestAsync(string toStateId, byte[] payload)
			{
				return RequestTransitionAsync(toStateId, payload);
			}
		}
	}
}
