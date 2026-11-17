/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public sealed class ClientMatchService : IService
	{
		private readonly GameClient _client;
		private readonly IPayloadCodec _payloadCodec;
		private ServiceState _state = ServiceState.NotInitialized;

		public ClientMatchService(GameClient client, IPayloadCodec? payloadCodec = null)
		{
			_client = client ?? throw new ArgumentNullException(nameof(client));
			_payloadCodec = payloadCodec ?? new JsonPayloadCodec();
		}

		public ServiceState State => _state;

		public Task SendCommandAsync(string commandId, byte[] payload, CancellationToken token = default)
		{
			if(string.IsNullOrWhiteSpace(commandId))
			{
				throw new ArgumentException("Match command id cannot be empty.", nameof(commandId));
			}

			HawkNet.Core.Guard.NotNull(payload);
			return _client.SendPacketAsync(
				PacketType.MatchCommandRequest,
				_payloadCodec.Encode(new MatchCommandRequest
				{
					CommandId = commandId.Trim(),
					Payload = payload.ToArray()
				}),
				token);
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
			_state = ServiceState.NotInitialized;
			return Task.CompletedTask;
		}

		public void RegisterServices()
		{
		}

		public void UnregisterServices()
		{
			_state = ServiceState.NotInitialized;
		}
	}
}
