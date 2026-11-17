/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

namespace HawkNet.Core
{
	public interface IGameEvent
	{
	}

	public interface IEventDispatcher
	{
		IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
			where TEvent : IGameEvent;

		Task DispatchAsync<TEvent>(TEvent gameEvent, CancellationToken token = default)
			where TEvent : IGameEvent;
	}

	/// <summary>
	/// Dispatches strongly typed in-process server events to async handlers.
	/// </summary>
	public class EventDispatcher : IEventDispatcher, IService
	{
		//---- Variables
		//--------------
		private readonly object _lock = new();
		private readonly Dictionary<Type, List<Subscription>> _subscriptions = new();
		private readonly ILogger _logger;
		private ServiceState _state = ServiceState.NotInitialized;

		//---- Properties
		//---------------
		public ServiceState State => _state;

		//---- Ctor
		//---------
		public EventDispatcher(ILogger? logger = null)
		{
			_logger = logger ?? NullLogger.Instance;
		}

		//---- Public
		//-----------
		public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
			where TEvent : IGameEvent
		{
			HawkNet.Core.Guard.NotNull(handler);

			var eventType = typeof(TEvent);
			var subscription = new Subscription(this, eventType, async (gameEvent, token) =>
			{
				await handler((TEvent)gameEvent, token);
			});

			lock(_lock)
			{
				if(!_subscriptions.TryGetValue(eventType, out var subscriptions))
				{
					subscriptions = new List<Subscription>();
					_subscriptions[eventType] = subscriptions;
				}

				subscriptions.Add(subscription);
			}

			return subscription;
		}

		public async Task DispatchAsync<TEvent>(TEvent gameEvent, CancellationToken token = default)
			where TEvent : IGameEvent
		{
			Subscription[] subscriptions;
			lock(_lock)
			{
				if(!_subscriptions.TryGetValue(typeof(TEvent), out var registeredSubscriptions))
				{
					return;
				}

				subscriptions = registeredSubscriptions.ToArray();
			}

			foreach(var subscription in subscriptions)
			{
				if(token.IsCancellationRequested)
				{
					return;
				}

				try
				{
					await subscription.InvokeAsync(gameEvent, token);
				}
				catch(OperationCanceledException) when (token.IsCancellationRequested)
				{
					return;
				}
				catch(Exception ex)
				{
					_logger.Exception(ex, $"Handler failed for {typeof(TEvent).Name}.", nameof(EventDispatcher));
				}
			}
		}

		public Task CreateAsync(CancellationToken token = default)
		{
			_state = ServiceState.Initializing;
			return Task.CompletedTask;
		}

		public Task InitializeAsync(CancellationToken token = default)
		{
			_state = ServiceState.Ready;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken token = default)
		{
			UnsubscribeAll();
			_state = ServiceState.NotInitialized;
			return Task.CompletedTask;
		}

		public void RegisterServices()
		{
		}

		public void UnregisterServices()
		{
			UnsubscribeAll();
		}

		private void Unsubscribe(Subscription subscription)
		{
			lock(_lock)
			{
				if(_subscriptions.TryGetValue(subscription.EventType, out var subscriptions))
				{
					subscriptions.Remove(subscription);
					if(subscriptions.Count == 0)
					{
						_subscriptions.Remove(subscription.EventType);
					}
				}
			}
		}

		private void UnsubscribeAll()
		{
			lock(_lock)
			{
				foreach(var subscriptions in _subscriptions.Values)
				{
					foreach(var subscription in subscriptions)
					{
						subscription.MarkDisposed();
					}
				}

				_subscriptions.Clear();
			}
		}

		private sealed class Subscription : IDisposable
		{
			private readonly EventDispatcher _dispatcher;
			private readonly Func<IGameEvent, CancellationToken, Task> _handler;
			private int _disposed;

			public Type EventType { get; }

			public Subscription(EventDispatcher dispatcher, Type eventType, Func<IGameEvent, CancellationToken, Task> handler)
			{
				_dispatcher = dispatcher;
				EventType = eventType;
				_handler = handler;
			}

			public Task InvokeAsync(IGameEvent gameEvent, CancellationToken token)
			{
				if(Volatile.Read(ref _disposed) == 1)
				{
					return Task.CompletedTask;
				}

				return _handler(gameEvent, token);
			}

			public void Dispose()
			{
				if(Interlocked.Exchange(ref _disposed, 1) == 1)
				{
					return;
				}

				_dispatcher.Unsubscribe(this);
			}

			public void MarkDisposed()
			{
				Interlocked.Exchange(ref _disposed, 1);
			}
		}
	}
}
