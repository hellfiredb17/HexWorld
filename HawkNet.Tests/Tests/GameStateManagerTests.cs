using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class GameStateManagerTests
	{
		[Fact]
		public void Start_EntersConfiguredInitialState()
		{
			var manager = new GameStateManager(CreateSettings("lobby"));
			var state = new TestState();
			manager.RegisterState("lobby", () => state);

			manager.Start();

			Assert.Same(state, manager.ActiveGameState);
			Assert.Equal("lobby", manager.ActiveStateId);
			Assert.True(state.EnterCalled);
			Assert.Empty(state.EnterPayload);
		}

		[Fact]
		public void Start_FailsWhenInitialStateIsMissingFromConfig()
		{
			var manager = new GameStateManager(new GameModeSettings
			{
				InitialStateId = "missing",
				States =
				[
					new GameModeStateSettings
					{
						Id = "lobby"
					}
				]
			});
			manager.RegisterState("missing", () => new TestState());

			Assert.Throws<InvalidOperationException>(() => manager.Start());
		}

		[Fact]
		public void Start_FailsWhenInitialStateHasNoRegisteredFactory()
		{
			var manager = new GameStateManager(CreateSettings("lobby"));

			Assert.Throws<InvalidOperationException>(() => manager.Start());
		}

		[Fact]
		public void Start_FailsWhenStateIdsAreDuplicated()
		{
			var manager = new GameStateManager(new GameModeSettings
			{
				InitialStateId = "lobby",
				States =
				[
					new GameModeStateSettings { Id = "lobby" },
					new GameModeStateSettings { Id = "LOBBY" }
				]
			});
			manager.RegisterState("lobby", () => new TestState());

			Assert.Throws<InvalidOperationException>(() => manager.Start());
		}

		[Fact]
		public void RegisterState_FailsWhenFactoryKeyIsDuplicated()
		{
			var manager = new GameStateManager(CreateSettings("lobby"));
			manager.RegisterState("lobby", () => new TestState());

			Assert.Throws<InvalidOperationException>(() => manager.RegisterState("LOBBY", () => new TestState()));
		}

		[Fact]
		public void TryTransitionTo_ExitsDisposesOldStateAndEntersTargetState()
		{
			var manager = new GameStateManager(new GameModeSettings
			{
				InitialStateId = "lobby",
				States =
				[
					new GameModeStateSettings
					{
						Id = "lobby",
						Transitions = ["match"]
					},
					new GameModeStateSettings
					{
						Id = "match"
					}
				]
			});
			var lobby = new TestState();
			var match = new TestState();
			manager.RegisterState("lobby", () => lobby);
			manager.RegisterState("match", () => match);
			manager.Start();

			byte[] payload = [1, 2, 3];
			bool transitioned = manager.TryTransitionTo("match", payload);

			Assert.True(transitioned);
			Assert.True(lobby.ExitCalled);
			Assert.True(lobby.DisposeCalled);
			Assert.True(match.EnterCalled);
			Assert.Equal(payload, match.EnterPayload);
			Assert.Same(match, manager.ActiveGameState);
			Assert.Equal("match", manager.ActiveStateId);
		}

		[Fact]
		public void TryTransitionTo_MissingTransitionLeavesActiveStateUnchanged()
		{
			var manager = new GameStateManager(CreateSettings("lobby"));
			var lobby = new TestState();
			manager.RegisterState("lobby", () => lobby);
			manager.Start();

			bool transitioned = manager.TryTransitionTo("missing");

			Assert.False(transitioned);
			Assert.False(lobby.ExitCalled);
			Assert.False(lobby.DisposeCalled);
			Assert.Same(lobby, manager.ActiveGameState);
		}

		[Fact]
		public void TryTransitionTo_UnregisteredTargetLeavesActiveStateUnchanged()
		{
			var manager = new GameStateManager(new GameModeSettings
			{
				InitialStateId = "lobby",
				States =
				[
					new GameModeStateSettings
					{
						Id = "lobby",
						Transitions = ["match"]
					},
					new GameModeStateSettings
					{
						Id = "match"
					}
				]
			});
			var lobby = new TestState();
			manager.RegisterState("lobby", () => lobby);
			manager.Start();

			bool transitioned = manager.TryTransitionTo("match");

			Assert.False(transitioned);
			Assert.False(lobby.ExitCalled);
			Assert.False(lobby.DisposeCalled);
			Assert.Same(lobby, manager.ActiveGameState);
		}

		[Fact]
		public void TryTransitionTo_TargetMissingFromConfigLeavesActiveStateUnchanged()
		{
			var manager = new GameStateManager(new GameModeSettings
			{
				InitialStateId = "lobby",
				States =
				[
					new GameModeStateSettings
					{
						Id = "lobby",
						Transitions = ["match"]
					}
				]
			});
			var lobby = new TestState();
			manager.RegisterState("lobby", () => lobby);
			manager.RegisterState("match", () => new TestState());
			manager.Start();

			bool transitioned = manager.TryTransitionTo("match");

			Assert.False(transitioned);
			Assert.False(lobby.ExitCalled);
			Assert.False(lobby.DisposeCalled);
			Assert.Same(lobby, manager.ActiveGameState);
		}

		[Fact]
		public void Update_UpdatesOnlyCurrentActiveState()
		{
			var manager = new GameStateManager(new GameModeSettings
			{
				InitialStateId = "lobby",
				States =
				[
					new GameModeStateSettings
					{
						Id = "lobby",
						Transitions = ["match"]
					},
					new GameModeStateSettings
					{
						Id = "match"
					}
				]
			});
			var lobby = new TestState();
			var match = new TestState();
			manager.RegisterState("lobby", () => lobby);
			manager.RegisterState("match", () => match);
			manager.Start();

			manager.Update(0.1f);
			manager.TryTransitionTo("match");
			manager.Update(0.2f);

			Assert.Equal(1, lobby.UpdateCount);
			Assert.Equal(0.1f, lobby.LastDeltaTime);
			Assert.Equal(1, match.UpdateCount);
			Assert.Equal(0.2f, match.LastDeltaTime);
		}

		private static GameModeSettings CreateSettings(string initialStateId)
		{
			return new GameModeSettings
			{
				InitialStateId = initialStateId,
				States =
				[
					new GameModeStateSettings
					{
						Id = initialStateId
					}
				]
			};
		}

		private sealed class TestState : IGameState
		{
			public bool EnterCalled { get; private set; }
			public bool ExitCalled { get; private set; }
			public bool DisposeCalled { get; private set; }
			public int UpdateCount { get; private set; }
			public float LastDeltaTime { get; private set; }
			public byte[] EnterPayload { get; private set; } = Array.Empty<byte>();

			public void Enter(byte[] transitionPayload)
			{
				EnterCalled = true;
				EnterPayload = transitionPayload;
			}

			public void Update(float deltaTime)
			{
				LastDeltaTime = deltaTime;
				UpdateCount++;
			}

			public void Exit()
			{
				ExitCalled = true;
			}

			public Task SendMessageAsync(int id, PacketType packetType, byte[] payload, CancellationToken token = default)
			{
				return Task.CompletedTask;
			}

			public Task BroadcastAsync(PacketType packetType, byte[] payload, CancellationToken token = default)
			{
				return Task.CompletedTask;
			}

			public void Dispose()
			{
				DisposeCalled = true;
			}
		}
	}
}
