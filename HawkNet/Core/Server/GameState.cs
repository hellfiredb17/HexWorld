/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	/// <summary>
	/// Base class for event-driven game states.
	/// </summary>
	public abstract class GameState : IAsyncGameState
	{
		//---- Variables
		//--------------
		private readonly List<IDisposable> _subscriptions = new();
		private bool _disposed;

		//---- Properties
		//---------------
		protected IEventDispatcher EventDispatcher { get; }

		//---- Ctor
		//---------
		protected GameState(IEventDispatcher eventDispatcher)
		{
			EventDispatcher = eventDispatcher;
		}

		//---- IGameState
		//---------------
		public abstract void Enter(byte[] transitionPayload);
		public abstract void Update(float deltaTime);
		public abstract void Exit();
		public virtual Task EnterAsync(byte[] transitionPayload, CancellationToken token = default)
		{
			Enter(transitionPayload);
			return Task.CompletedTask;
		}
		public virtual Task TickAsync(float deltaTime, CancellationToken token = default)
		{
			Update(deltaTime);
			return Task.CompletedTask;
		}
		public virtual Task ExitAsync(CancellationToken token = default)
		{
			Exit();
			return Task.CompletedTask;
		}

		public Task SendMessageAsync(int id, PacketType packetType, byte[] payload, CancellationToken token = default)
		{
			return EventDispatcher.DispatchAsync(new SendPlayerMessageEvent(id, packetType, payload), token);
		}

		public Task SendMessageAsync(int id, PacketType packetType, byte[] payload, DeliveryOptions delivery, CancellationToken token = default)
		{
			return EventDispatcher.DispatchAsync(new SendPlayerMessageEvent(id, packetType, payload, delivery), token);
		}

		public Task BroadcastAsync(PacketType packetType, byte[] payload, CancellationToken token = default)
		{
			return EventDispatcher.DispatchAsync(new BroadcastMessageEvent(packetType, payload), token);
		}

		public Task BroadcastAsync(PacketType packetType, byte[] payload, DeliveryOptions delivery, CancellationToken token = default)
		{
			return EventDispatcher.DispatchAsync(new BroadcastMessageEvent(packetType, payload, delivery), token);
		}

		protected Task RequestTransitionAsync(
			string toStateId,
			byte[] payload,
			CancellationToken token = default)
		{
			return EventDispatcher.DispatchAsync(
				new GameStateTransitionRequestedEvent(toStateId, payload),
				token);
		}

		public virtual void Dispose()
		{
			if(_disposed)
			{
				return;
			}

			_disposed = true;
			UnsubscribeAll();
			GC.SuppressFinalize(this);
		}

		//---- Protected
		//--------------
		protected IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
			where TEvent : IGameEvent
		{
			var subscription = EventDispatcher.Subscribe(handler);
			_subscriptions.Add(subscription);
			return subscription;
		}

		protected void Unsubscribe(IDisposable subscription)
		{
			subscription.Dispose();
			_subscriptions.Remove(subscription);
		}

		protected void UnsubscribeAll()
		{
			foreach(var subscription in _subscriptions.ToArray())
			{
				subscription.Dispose();
			}

			_subscriptions.Clear();
		}
	}
}
