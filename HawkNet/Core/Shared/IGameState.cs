/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 * Created: 10/31/2025 12:29:50 AM
 * Description: Interface for game state representation.
 */

using HawkNet.Core.Server;

namespace HawkNet.Core.Shared
{
	public interface IGameState : IDisposable
	{		
		public void Enter(byte[] transitionPayload);
		public void Update(float deltaTime);
		public void Exit();		

		public Task SendMessageAsync(int id, PacketType packetType, byte[] payload, CancellationToken token = default);
		public Task SendMessageAsync(int id, PacketType packetType, byte[] payload, DeliveryOptions delivery, CancellationToken token = default) =>
			SendMessageAsync(id, packetType, payload, token);
		public Task BroadcastAsync(PacketType packetType, byte[] payload, CancellationToken token = default);
		public Task BroadcastAsync(PacketType packetType, byte[] payload, DeliveryOptions delivery, CancellationToken token = default) =>
			BroadcastAsync(packetType, payload, token);

	} // end class

	public interface IAsyncGameState : IGameState
	{
		Task EnterAsync(byte[] transitionPayload, CancellationToken token = default);
		Task TickAsync(float deltaTime, CancellationToken token = default);
		Task ExitAsync(CancellationToken token = default);
	}
} // end namespace
