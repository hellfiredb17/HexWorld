using HawkNet.Core;
using HawkNet.Core.Client;
using Xunit;

namespace HawkNet.Tests
{
	public class ClientGameStateManagerTests
	{
		[Fact]
		public async Task ConnectedEvent_EntersRegisteredState()
		{
			var dispatcher = new EventDispatcher();
			using var manager = new ClientGameStateManager(dispatcher);
			TestClientState? state = null;
			manager.RegisterState("chat_lobby", () =>
			{
				state = new TestClientState(dispatcher);
				return state;
			});
			await manager.CreateAsync();
			await manager.InitializeAsync();
			byte[] payload = [1, 2, 3];

			await dispatcher.DispatchAsync(new ClientConnectedEvent(7, "chat_lobby", payload));

			Assert.Equal("chat_lobby", manager.ActiveStateId);
			Assert.Same(state, manager.ActiveState);
			Assert.True(state!.EnterCalled);
			Assert.Equal(payload, state.Payload);
		}

		[Fact]
		public async Task GameStateChange_ExitsOldStateAndEntersNewState()
		{
			var dispatcher = new EventDispatcher();
			using var manager = new ClientGameStateManager(dispatcher);
			TestClientState? first = null;
			TestClientState? second = null;
			manager.RegisterState("chat_lobby", () =>
			{
				first = new TestClientState(dispatcher);
				return first;
			});
			manager.RegisterState("match", () =>
			{
				second = new TestClientState(dispatcher);
				return second;
			});
			await manager.CreateAsync();
			await manager.InitializeAsync();

			await dispatcher.DispatchAsync(new ClientConnectedEvent(7, "chat_lobby", Array.Empty<byte>()));
			await dispatcher.DispatchAsync(new ClientGameStateChangeReceivedEvent("match", [9]));

			Assert.True(first!.ExitCalled);
			Assert.True(first.DisposeCalled);
			Assert.Equal("match", manager.ActiveStateId);
			Assert.Same(second, manager.ActiveState);
			Assert.True(second!.EnterCalled);
			Assert.Equal(new byte[] { 9 }, second.Payload);
		}

		[Fact]
		public async Task UnknownStateChange_LeavesActiveStateUnchanged()
		{
			var dispatcher = new EventDispatcher();
			using var manager = new ClientGameStateManager(dispatcher);
			TestClientState? state = null;
			manager.RegisterState("chat_lobby", () =>
			{
				state = new TestClientState(dispatcher);
				return state;
			});
			await manager.CreateAsync();
			await manager.InitializeAsync();
			await dispatcher.DispatchAsync(new ClientConnectedEvent(7, "chat_lobby", Array.Empty<byte>()));

			await dispatcher.DispatchAsync(new ClientGameStateChangeReceivedEvent("missing", Array.Empty<byte>()));

			Assert.Equal("chat_lobby", manager.ActiveStateId);
			Assert.Same(state, manager.ActiveState);
			Assert.False(state!.ExitCalled);
		}

		[Fact]
		public async Task Update_UpdatesActiveState()
		{
			var dispatcher = new EventDispatcher();
			using var manager = new ClientGameStateManager(dispatcher);
			TestClientState? state = null;
			manager.RegisterState("chat_lobby", () =>
			{
				state = new TestClientState(dispatcher);
				return state;
			});
			await manager.CreateAsync();
			await manager.InitializeAsync();
			await dispatcher.DispatchAsync(new ClientConnectedEvent(7, "chat_lobby", Array.Empty<byte>()));

			manager.Update(0.1f);

			Assert.Equal(1, state!.UpdateCount);
			Assert.Equal(0.1f, state.LastDeltaTime);
		}

		private sealed class TestClientState : ClientGameState
		{
			public bool EnterCalled { get; private set; }
			public bool ExitCalled { get; private set; }
			public bool DisposeCalled { get; private set; }
			public int UpdateCount { get; private set; }
			public float LastDeltaTime { get; private set; }
			public byte[] Payload { get; private set; } = Array.Empty<byte>();

			public TestClientState(IEventDispatcher eventDispatcher)
				: base(eventDispatcher)
			{
			}

			public override void Enter(byte[] payload)
			{
				EnterCalled = true;
				Payload = payload;
			}

			public override void Update(float deltaTime)
			{
				UpdateCount++;
				LastDeltaTime = deltaTime;
			}

			public override void Exit()
			{
				ExitCalled = true;
			}

			public override void Dispose()
			{
				DisposeCalled = true;
				base.Dispose();
			}
		}
	}
}
