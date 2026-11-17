using HawkNet.Core;
using HawkNet.Core.Client;
using Xunit;

namespace HawkNet.Tests
{
	public class ClientPlayerServiceTests
	{
		[Fact]
		public async Task ConnectedEvent_InitializesCurrentPlayer()
		{
			var dispatcher = new EventDispatcher();
			using var playerService = new ClientPlayerService(dispatcher);

			await dispatcher.DispatchAsync(new ClientConnectedEvent(7, "chat_lobby", Array.Empty<byte>()));

			Assert.Equal(new ClientPlayerData(7, "Player 7"), playerService.CurrentPlayer);
		}

		[Fact]
		public async Task ReconnectedEvent_PreservesExistingDisplayNameForSamePlayer()
		{
			var dispatcher = new EventDispatcher();
			using var playerService = new ClientPlayerService(dispatcher);
			await dispatcher.DispatchAsync(new ClientConnectedEvent(7, "chat_lobby", Array.Empty<byte>()));
			await playerService.ChangeDisplayNameAsync("Nova");

			await dispatcher.DispatchAsync(new ClientReconnectedEvent(7, "chat_lobby", Array.Empty<byte>()));

			Assert.Equal(new ClientPlayerData(7, "Nova"), playerService.CurrentPlayer);
		}

		[Fact]
		public async Task ReconnectedEvent_ReplacesCurrentPlayerWhenPlayerIdChanges()
		{
			var dispatcher = new EventDispatcher();
			using var playerService = new ClientPlayerService(dispatcher);
			await dispatcher.DispatchAsync(new ClientConnectedEvent(7, "chat_lobby", Array.Empty<byte>()));

			await dispatcher.DispatchAsync(new ClientReconnectedEvent(8, "chat_lobby", Array.Empty<byte>()));

			Assert.Equal(new ClientPlayerData(8, "Player 8"), playerService.CurrentPlayer);
		}

		[Fact]
		public async Task ChangeDisplayNameAsync_UpdatesCurrentPlayerAndDispatchesEvent()
		{
			var dispatcher = new EventDispatcher();
			using var playerService = new ClientPlayerService(dispatcher);
			ClientDisplayNameChangedEvent? changedEvent = null;
			dispatcher.Subscribe<ClientDisplayNameChangedEvent>((gameEvent, _) =>
			{
				changedEvent = gameEvent;
				return Task.CompletedTask;
			});
			await dispatcher.DispatchAsync(new ClientConnectedEvent(7, "chat_lobby", Array.Empty<byte>()));

			await playerService.ChangeDisplayNameAsync("  Nova  ");

			Assert.Equal(new ClientPlayerData(7, "Nova"), playerService.CurrentPlayer);
			Assert.NotNull(changedEvent);
			Assert.Equal(7, changedEvent.Value.PlayerId);
			Assert.Equal("Nova", changedEvent.Value.DisplayName);
		}

		[Fact]
		public async Task ChangeDisplayNameAsync_RejectsWhitespaceName()
		{
			var dispatcher = new EventDispatcher();
			using var playerService = new ClientPlayerService(dispatcher);
			await dispatcher.DispatchAsync(new ClientConnectedEvent(7, "chat_lobby", Array.Empty<byte>()));

			await Assert.ThrowsAsync<ArgumentException>(() => playerService.ChangeDisplayNameAsync("   "));
		}

		[Fact]
		public async Task ChangeDisplayNameAsync_BeforePlayerKnown_Throws()
		{
			var dispatcher = new EventDispatcher();
			using var playerService = new ClientPlayerService(dispatcher);

			await Assert.ThrowsAsync<InvalidOperationException>(() => playerService.ChangeDisplayNameAsync("Nova"));
		}
	}
}
