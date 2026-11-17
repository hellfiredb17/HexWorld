/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public interface IClientGameState : IDisposable
	{
		void Enter(byte[] payload);
		void Update(float deltaTime);
		void Exit();
	}

	public abstract class ClientGameState : IClientGameState
	{
		private readonly List<IDisposable> _subscriptions = new();
		private bool _disposed;

		protected IEventDispatcher EventDispatcher { get; }

		protected ClientGameState(IEventDispatcher eventDispatcher)
		{
			EventDispatcher = eventDispatcher;
		}

		public abstract void Enter(byte[] payload);
		public abstract void Update(float deltaTime);
		public abstract void Exit();

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

		protected IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
			where TEvent : IGameEvent
		{
			var subscription = EventDispatcher.Subscribe(handler);
			_subscriptions.Add(subscription);
			return subscription;
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

	public abstract class ClientGameState<TPayload> : ClientGameState
		where TPayload : class
	{
		private readonly IPayloadCodec _payloadCodec;

		protected ClientGameState(IEventDispatcher eventDispatcher, IPayloadCodec payloadCodec)
			: base(eventDispatcher)
		{
			_payloadCodec = payloadCodec ?? throw new ArgumentNullException(nameof(payloadCodec));
		}

		public sealed override void Enter(byte[] payload)
		{
			TPayload? typedPayload = null;
			if(payload.Length > 0)
			{
				typedPayload = _payloadCodec.Decode<TPayload>(payload);
			}

			Enter(typedPayload);
		}

		protected abstract void Enter(TPayload? payload);
	}
}
