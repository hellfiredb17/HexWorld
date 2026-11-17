using HawkNet.Core;
using Xunit;

namespace HawkNet.Tests
{
	public class EventDispatcherTests
	{
		public EventDispatcherTests()
		{
			Services.ClearForTests();
		}

		[Fact]
		public async Task DispatchAsync_InvokesSubscribedHandler()
		{
			var dispatcher = new EventDispatcher();
			TestEvent? receivedEvent = null;
			dispatcher.Subscribe<TestEvent>((gameEvent, _) =>
			{
				receivedEvent = gameEvent;
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new TestEvent(42));

			Assert.Equal(42, receivedEvent?.Value);
		}

		[Fact]
		public async Task DispatchAsync_InvokesHandlersSequentially()
		{
			var dispatcher = new EventDispatcher();
			var order = new List<int>();
			dispatcher.Subscribe<TestEvent>((_, _) =>
			{
				order.Add(1);
				return Task.CompletedTask;
			});
			dispatcher.Subscribe<TestEvent>((_, _) =>
			{
				order.Add(2);
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new TestEvent(1));

			Assert.Equal(new[] { 1, 2 }, order);
		}

		[Fact]
		public async Task Subscription_Dispose_UnsubscribesHandler()
		{
			var dispatcher = new EventDispatcher();
			int callCount = 0;
			var subscription = dispatcher.Subscribe<TestEvent>((_, _) =>
			{
				callCount++;
				return Task.CompletedTask;
			});

			subscription.Dispose();
			await dispatcher.DispatchAsync(new TestEvent(1));

			Assert.Equal(0, callCount);
		}

		[Fact]
		public async Task DispatchAsync_IsolatesHandlerErrors()
		{
			var dispatcher = new EventDispatcher();
			int successfulHandlerCount = 0;
			dispatcher.Subscribe<TestEvent>((_, _) => throw new InvalidOperationException("expected test failure"));
			dispatcher.Subscribe<TestEvent>((_, _) =>
			{
				successfulHandlerCount++;
				return Task.CompletedTask;
			});

			await dispatcher.DispatchAsync(new TestEvent(1));

			Assert.Equal(1, successfulHandlerCount);
		}

		[Fact]
		public async Task EventDispatcher_CanBeRegisteredAndRetrievedAsService()
		{
			var dispatcher = new EventDispatcher();
			Services.RegisterService(dispatcher);

			var resolvedDispatcher = await Services.GetAsync<EventDispatcher>();

			Assert.Same(dispatcher, resolvedDispatcher);
			Assert.Equal(ServiceState.Ready, dispatcher.State);
			Assert.True(Services.IsReady<EventDispatcher>());
		}

		[Fact]
		public async Task EventDispatcher_ShutdownClearsSubscriptions()
		{
			var dispatcher = new EventDispatcher();
			int callCount = 0;
			dispatcher.Subscribe<TestEvent>((_, _) =>
			{
				callCount++;
				return Task.CompletedTask;
			});
			Services.RegisterService(dispatcher);
			await Services.GetAsync<EventDispatcher>();

			await Services.ShutdownAsync();
			await dispatcher.DispatchAsync(new TestEvent(1));

			Assert.Equal(0, callCount);
			Assert.Equal(ServiceState.NotInitialized, dispatcher.State);
		}

		private readonly record struct TestEvent(int Value) : IGameEvent;
	}
}
