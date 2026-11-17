using HawkNet.Core;
using HawkNet.Core.Server;
using System.Text;
using Xunit;

namespace HawkNet.Tests
{
	public class PlayerIdentityServiceTests
	{
		[Fact]
		public void GetOrCreateIdentity_UsesConfiguredDefaultDisplayName()
		{
			var dispatcher = new EventDispatcher();
			using var service = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings
			{
				DefaultDisplayNameFormat = "Guest-{0}",
				MaxDisplayNameLength = 32
			});

			var identity = service.GetOrCreateIdentity(12);

			Assert.Equal(12, identity.PlayerId);
			Assert.Equal("Guest-12", identity.DisplayName);
		}

		[Fact]
		public void TrySetDisplayName_TrimsAndCapsDisplayName()
		{
			var dispatcher = new EventDispatcher();
			using var service = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings
			{
				MaxDisplayNameLength = 5
			});
			var originalIdentity = service.GetOrCreateIdentity(4);

			bool updated = service.TrySetDisplayName(4, "  Duncan  ", out var identity);

			Assert.True(updated);
			Assert.Equal("Dunca", identity.DisplayName);
			Assert.Equal("Dunca", service.GetDisplayName(4));
			Assert.Equal(originalIdentity.CreatedAtUtc, identity.CreatedAtUtc);
			Assert.True(identity.UpdatedAtUtc >= originalIdentity.UpdatedAtUtc);
		}

		[Fact]
		public async Task TrySetDisplayName_DispatchesNameChangedEvent()
		{
			var dispatcher = new EventDispatcher();
			using var service = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());
			service.GetOrCreateIdentity(8);
			PlayerDisplayNameChangedEvent? changedEvent = null;
			dispatcher.Subscribe<PlayerDisplayNameChangedEvent>((gameEvent, _) =>
			{
				changedEvent = gameEvent;
				return Task.CompletedTask;
			});

			service.TrySetDisplayName(8, "Pilot", out _);

			await Task.Delay(10);
			Assert.NotNull(changedEvent);
			Assert.Equal(8, changedEvent.Value.Identity.PlayerId);
			Assert.Equal("Pilot", changedEvent.Value.Identity.DisplayName);
		}

		[Fact]
		public void TrySetDisplayName_UnknownPlayerFailsAndDoesNotCreateIdentity()
		{
			var dispatcher = new EventDispatcher();
			using var service = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());

			bool updated = service.TrySetDisplayName(99, "Ghost", out var identity);

			Assert.False(updated);
			Assert.Equal(default, identity);
			Assert.False(service.TryGetIdentity(99, out _));
		}

		[Fact]
		public void TrySetDisplayName_WhitespaceNameFailsWithoutMutation()
		{
			var dispatcher = new EventDispatcher();
			using var service = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());
			var originalIdentity = service.GetOrCreateIdentity(2);

			bool updated = service.TrySetDisplayName(2, "   ", out var identity);

			Assert.False(updated);
			Assert.Equal(default, identity);
			Assert.True(service.TryGetIdentity(2, out var currentIdentity));
			Assert.Equal(originalIdentity, currentIdentity);
		}

		[Fact]
		public async Task TrySetDisplayName_FailedUpdateDoesNotDispatchNameChangedEvent()
		{
			var dispatcher = new EventDispatcher();
			using var service = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());
			int eventCount = 0;
			dispatcher.Subscribe<PlayerDisplayNameChangedEvent>((_, _) =>
			{
				eventCount++;
				return Task.CompletedTask;
			});

			service.TrySetDisplayName(42, "Unknown", out _);
			await Task.Delay(10);

			Assert.Equal(0, eventCount);
		}

		[Fact]
		public async Task PlayerDisconnected_RemovesIdentity()
		{
			var dispatcher = new EventDispatcher();
			using var service = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());
			service.GetOrCreateIdentity(3);

			await dispatcher.DispatchAsync(new PlayerDisconnectedEvent(3, ConnectionCloseReason.SessionTimeout));

			Assert.False(service.TryGetIdentity(3, out _));
		}

		[Fact]
		public async Task DisplayNameUpdateReceived_UpdatesKnownPlayerIdentity()
		{
			var dispatcher = new EventDispatcher();
			using var service = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings
			{
				MaxDisplayNameLength = 6
			});
			await dispatcher.DispatchAsync(new PlayerConnectedEvent(11));

			await dispatcher.DispatchAsync(new DisplayNameUpdateReceivedEvent(11, Encoding.UTF8.GetBytes("  Captain  ")));

			Assert.True(service.TryGetIdentity(11, out var identity));
			Assert.Equal("Captai", identity.DisplayName);
		}

		[Fact]
		public async Task DisplayNameUpdateReceived_UnknownPlayerDoesNotCreateIdentity()
		{
			var dispatcher = new EventDispatcher();
			using var service = new PlayerIdentityService(dispatcher, new PlayerIdentitySettings());

			await dispatcher.DispatchAsync(new DisplayNameUpdateReceivedEvent(404, Encoding.UTF8.GetBytes("Ghost")));

			Assert.False(service.TryGetIdentity(404, out _));
		}
	}
}
