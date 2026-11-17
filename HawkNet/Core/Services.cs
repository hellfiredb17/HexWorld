/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

namespace HawkNet.Core
{
	public enum ServiceState
	{
		NotInitialized,
		Initializing,
		Ready,
		Error
	}

	public interface IService
	{
		ServiceState State { get; }

		Task CreateAsync(CancellationToken token = default);
		Task InitializeAsync(CancellationToken token = default);
		Task ShutdownAsync(CancellationToken token = default);

		void RegisterServices();
		void UnregisterServices();
	}

	public interface IServiceRegistry
	{
		void Register<TService>(TService service)
			where TService : class;

		void Replace<TService>(TService service)
			where TService : class;

		TService Get<TService>()
			where TService : class;

		bool TryGet<TService>(out TService service)
			where TService : class;
	}

	public sealed class ServiceRegistry : IServiceRegistry
	{
		private readonly Dictionary<Type, object> _services = new();

		public void Register<TService>(TService service)
			where TService : class
		{
			HawkNet.Core.Guard.NotNull(service);

			if(!_services.TryAdd(typeof(TService), service))
			{
				throw new InvalidOperationException($"Service '{typeof(TService).Name}' is already registered.");
			}
		}

		public void Replace<TService>(TService service)
			where TService : class
		{
			HawkNet.Core.Guard.NotNull(service);
			_services[typeof(TService)] = service;
		}

		public TService Get<TService>()
			where TService : class
		{
			if(!TryGet<TService>(out var service))
			{
				throw new InvalidOperationException($"Service '{typeof(TService).Name}' is not registered.");
			}

			return service;
		}

		public bool TryGet<TService>(out TService service)
			where TService : class
		{
			if(_services.TryGetValue(typeof(TService), out var value) && value is TService typedService)
			{
				service = typedService;
				return true;
			}

			service = null!;
			return false;
		}
	}

	public static class Services
	{
		//---- Variables
		//--------------
		private static readonly object _lock = new();
		private static readonly Dictionary<Type, ServiceEntry> _services = new();

		//---- Public
		//-----------
		public static Task<T> GetAsync<T>(CancellationToken token = default)
			where T : class, IService
		{
			ServiceEntry entry;
			lock(_lock)
			{
				if(!_services.TryGetValue(typeof(T), out entry!))
				{
					throw new InvalidOperationException($"Service '{typeof(T).Name}' is not registered.");
				}
			}

			return GetReadyServiceAsync<T>(entry, token);
		}

		public static bool IsReady<T>()
			where T : class, IService
		{
			lock(_lock)
			{
				return _services.TryGetValue(typeof(T), out var entry)
					&& entry.Service.State == ServiceState.Ready
					&& entry.StartupTask.IsCompletedSuccessfully;
			}
		}

		public static void RegisterService<T>(T service)
			where T : class, IService
		{
			HawkNet.Core.Guard.NotNull(service);

			ServiceEntry entry;
			lock(_lock)
			{
				if(_services.ContainsKey(typeof(T)))
				{
					throw new InvalidOperationException($"Service '{typeof(T).Name}' is already registered.");
				}

				entry = new ServiceEntry(service);
				_services.Add(typeof(T), entry);
			}

			try
			{
				service.RegisterServices();
				entry.StartupTask = StartServiceAsync(service);
			}
			catch
			{
				lock(_lock)
				{
					_services.Remove(typeof(T));
				}

				throw;
			}
		}

		public static bool UnregisterService<T>()
			where T : class, IService
		{
			ServiceEntry entry;
			lock(_lock)
			{
				if(!_services.Remove(typeof(T), out entry!))
				{
					return false;
				}
			}

			entry.Service.UnregisterServices();
			return true;
		}

		public static async Task ShutdownAsync(CancellationToken token = default)
		{
			ServiceEntry[] entries;
			lock(_lock)
			{
				entries = _services.Values.ToArray();
				_services.Clear();
			}

			foreach(var entry in entries)
			{
				try
				{
					await entry.StartupTask;
				}
				catch
				{
					// Shutdown should still give the service a chance to clean up after a failed startup.
				}

				await entry.Service.ShutdownAsync(token);
				entry.Service.UnregisterServices();
			}
		}

		internal static void ClearForTests()
		{
			lock(_lock)
			{
				_services.Clear();
			}
		}

		//---- Private
		//------------
		private static async Task<T> GetReadyServiceAsync<T>(ServiceEntry entry, CancellationToken token)
			where T : class, IService
		{
			if(entry.Service.State == ServiceState.Error)
			{
				throw new InvalidOperationException($"Service '{typeof(T).Name}' is in an error state.");
			}

			await TaskCompatibility.WaitAsync(entry.StartupTask, token);

			if(entry.Service.State == ServiceState.Error)
			{
				throw new InvalidOperationException($"Service '{typeof(T).Name}' is in an error state.");
			}

			if(entry.Service.State != ServiceState.Ready)
			{
				throw new InvalidOperationException($"Service '{typeof(T).Name}' is not ready.");
			}

			return (T)entry.Service;
		}

		private static async Task StartServiceAsync(IService service)
		{
			await service.CreateAsync();
			await service.InitializeAsync();
		}

		private sealed class ServiceEntry
		{
			public IService Service { get; }
			public Task StartupTask { get; set; } = Task.CompletedTask;

			public ServiceEntry(IService service)
			{
				Service = service;
			}
		}
	}
}
