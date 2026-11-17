using HawkNet.Core;
using Xunit;

namespace HawkNet.Tests
{
	[Collection(ServicesCollection.Name)]
	public class ServicesTests : IDisposable
	{
		public ServicesTests()
		{
			Services.ClearForTests();
		}

		public void Dispose()
		{
			Services.ClearForTests();
		}

		[Fact]
		public async Task GetAsync_ReturnsRegisteredReadyService()
		{
			var service = new TestService();
			Services.RegisterService(service);

			var result = await Services.GetAsync<TestService>();

			Assert.Same(service, result);
			Assert.Equal(ServiceState.Ready, service.State);
			Assert.Equal(1, service.CreateCount);
			Assert.Equal(1, service.InitializeCount);
			Assert.True(service.RegisterServicesCalled);
		}

		[Fact]
		public void RegisterService_DuplicateRegistrationThrows()
		{
			Services.RegisterService(new TestService());

			Assert.Throws<InvalidOperationException>(() => Services.RegisterService(new TestService()));
		}

		[Fact]
		public async Task GetAsync_WaitsForInitializationToComplete()
		{
			var service = new BlockingService();
			Services.RegisterService(service);

			Task<BlockingService> getTask = Services.GetAsync<BlockingService>();
			await service.InitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

			Assert.False(getTask.IsCompleted);
			Assert.False(Services.IsReady<BlockingService>());

			service.AllowInitialize.SetResult();
			var result = await getTask.WaitAsync(TimeSpan.FromSeconds(2));

			Assert.Same(service, result);
			Assert.True(Services.IsReady<BlockingService>());
		}

		[Fact]
		public async Task GetAsync_MultipleCallersAwaitSameInitialization()
		{
			var service = new BlockingService();
			Services.RegisterService(service);

			Task<BlockingService> firstGetTask = Services.GetAsync<BlockingService>();
			Task<BlockingService> secondGetTask = Services.GetAsync<BlockingService>();
			await service.InitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

			service.AllowInitialize.SetResult();
			var firstResult = await firstGetTask.WaitAsync(TimeSpan.FromSeconds(2));
			var secondResult = await secondGetTask.WaitAsync(TimeSpan.FromSeconds(2));

			Assert.Same(service, firstResult);
			Assert.Same(service, secondResult);
			Assert.Equal(1, service.CreateCount);
			Assert.Equal(1, service.InitializeCount);
		}

		[Fact]
		public async Task IsReady_IsFalseBeforeReadyAndTrueAfterReady()
		{
			var service = new BlockingService();
			Services.RegisterService(service);
			await service.InitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

			Assert.False(Services.IsReady<BlockingService>());

			service.AllowInitialize.SetResult();
			await Services.GetAsync<BlockingService>();

			Assert.True(Services.IsReady<BlockingService>());
		}

		[Fact]
		public async Task GetAsync_UnregisteredServiceThrows()
		{
			await Assert.ThrowsAsync<InvalidOperationException>(() => Services.GetAsync<TestService>());
		}

		[Fact]
		public async Task GetAsync_ErrorStateThrows()
		{
			var service = new ErrorStateService();
			Services.RegisterService(service);

			await Assert.ThrowsAsync<InvalidOperationException>(() => Services.GetAsync<ErrorStateService>());
			Assert.False(Services.IsReady<ErrorStateService>());
		}

		[Fact]
		public async Task UnregisterService_RemovesServiceAndCallsUnregisterHook()
		{
			var service = new TestService();
			Services.RegisterService(service);

			bool removed = Services.UnregisterService<TestService>();

			Assert.True(removed);
			Assert.True(service.UnregisterServicesCalled);
			await Assert.ThrowsAsync<InvalidOperationException>(() => Services.GetAsync<TestService>());
		}

		[Fact]
		public async Task ShutdownAsync_ShutsDownAndClearsRegisteredServices()
		{
			var service = new TestService();
			Services.RegisterService(service);

			await Services.ShutdownAsync();

			Assert.True(service.ShutdownCalled);
			Assert.True(service.UnregisterServicesCalled);
			await Assert.ThrowsAsync<InvalidOperationException>(() => Services.GetAsync<TestService>());
		}

		[Fact]
		public async Task ClearForTests_PreventsStaticStateLeakage()
		{
			Services.RegisterService(new TestService());

			Services.ClearForTests();

			await Assert.ThrowsAsync<InvalidOperationException>(() => Services.GetAsync<TestService>());
		}

		private class TestService : IService
		{
			public ServiceState State { get; protected set; } = ServiceState.NotInitialized;
			public int CreateCount { get; protected set; }
			public int InitializeCount { get; protected set; }
			public bool RegisterServicesCalled { get; private set; }
			public bool UnregisterServicesCalled { get; private set; }
			public bool ShutdownCalled { get; private set; }

			public virtual Task CreateAsync(CancellationToken token = default)
			{
				CreateCount++;
				State = ServiceState.Initializing;
				return Task.CompletedTask;
			}

			public virtual Task InitializeAsync(CancellationToken token = default)
			{
				InitializeCount++;
				State = ServiceState.Ready;
				return Task.CompletedTask;
			}

			public virtual Task ShutdownAsync(CancellationToken token = default)
			{
				ShutdownCalled = true;
				State = ServiceState.NotInitialized;
				return Task.CompletedTask;
			}

			public void RegisterServices()
			{
				RegisterServicesCalled = true;
			}

			public void UnregisterServices()
			{
				UnregisterServicesCalled = true;
			}
		}

		private sealed class BlockingService : TestService
		{
			public TaskCompletionSource InitializeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
			public TaskCompletionSource AllowInitialize { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

			public override async Task InitializeAsync(CancellationToken token = default)
			{
				InitializeCount++;
				InitializeStarted.TrySetResult();
				await AllowInitialize.Task.WaitAsync(token);
				State = ServiceState.Ready;
			}
		}

		private sealed class ErrorStateService : TestService
		{
			public override Task InitializeAsync(CancellationToken token = default)
			{
				InitializeCount++;
				State = ServiceState.Error;
				return Task.CompletedTask;
			}
		}
	}
}
