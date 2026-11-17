/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	/// <summary>
	/// Bridges low-level GameClient callbacks into typed client events.
	/// </summary>
	public sealed class ClientNetworkMessageHandler : IService, IDisposable
	{
		//---- Variables
		//--------------
		private readonly GameClient _client;
		private readonly IEventDispatcher _eventDispatcher;
		private readonly IPayloadCodec _payloadCodec;
		private bool _isStarted;
		private bool _disposed;
		private ServiceState _state = ServiceState.NotInitialized;

		//---- Ctor
		//---------
		public ClientNetworkMessageHandler(
			GameClient client,
			IEventDispatcher eventDispatcher,
			IPayloadCodec? payloadCodec = null)
		{
			_client = client ?? throw new ArgumentNullException(nameof(client));
			_eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
			_payloadCodec = payloadCodec ?? new JsonPayloadCodec();
		}

		public ServiceState State => _state;

		//---- Public
		//-----------
		public void Start()
		{
			HawkNet.Core.Guard.NotDisposed(_disposed, this);

			if(_isStarted)
			{
				return;
			}

			_client.Connected += OnConnected;
			_client.Reconnected += OnReconnected;
			_client.Disconnected += OnDisconnected;
			_client.PacketReceived += OnPacketReceived;
			_isStarted = true;
			_state = ServiceState.Ready;
		}

		public void Stop()
		{
			if(!_isStarted)
			{
				return;
			}

			_client.Connected -= OnConnected;
			_client.Reconnected -= OnReconnected;
			_client.Disconnected -= OnDisconnected;
			_client.PacketReceived -= OnPacketReceived;
			_isStarted = false;
			_state = ServiceState.NotInitialized;
		}

		public void Dispose()
		{
			if(_disposed)
			{
				return;
			}

			Stop();
			_disposed = true;
		}

		public Task CreateAsync(CancellationToken token = default)
		{
			_state = ServiceState.Initializing;
			return Task.CompletedTask;
		}

		public Task InitializeAsync(CancellationToken token = default)
		{
			Start();
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken token = default)
		{
			Stop();
			return Task.CompletedTask;
		}

		public void RegisterServices()
		{
		}

		public void UnregisterServices()
		{
			Stop();
		}

		//---- Private
		//------------
		private void OnConnected()
		{
			if(_client.PlayerId is not int playerId)
			{
				return;
			}

			_ = _eventDispatcher.DispatchAsync(new ClientConnectedEvent(
				playerId,
				_client.CurrentGameStateId,
				_client.CurrentGameStatePayload.ToArray()));
		}

		private void OnReconnected()
		{
			if(_client.PlayerId is not int playerId)
			{
				return;
			}

			_ = _eventDispatcher.DispatchAsync(new ClientReconnectedEvent(
				playerId,
				_client.CurrentGameStateId,
				_client.CurrentGameStatePayload.ToArray()));
		}

		private void OnDisconnected()
		{
			_ = _eventDispatcher.DispatchAsync(new ClientDisconnectedEvent());
		}

		private void OnPacketReceived(Shared.PacketType packetType, byte[] payload)
		{
			if(packetType == PacketType.GameStateChange)
			{
				DispatchGameStateChange(payload);
			}
			else if(packetType == PacketType.ChatLobbyReadyState)
			{
				DispatchChatLobbyReadyState(payload);
			}
			else if(packetType == PacketType.ChatLobbyCountdown)
			{
				DispatchChatLobbyCountdown(payload);
			}
			else if(packetType == PacketType.ChatLobbyPlayerState)
			{
				DispatchChatLobbyPlayerState(payload);
			}
			else if(packetType == PacketType.MatchPhaseChange)
			{
				DispatchMatchPhaseChange(payload);
			}
			else if(packetType == PacketType.GameStateMessage)
			{
				DispatchGameStateMessage(payload);
			}

			_ = _eventDispatcher.DispatchAsync(new ClientPacketReceivedEvent(packetType, payload.ToArray()));
		}

		private void DispatchGameStateChange(byte[] payload)
		{
			try
			{
				var message = GameStateProtocol.DecodeChange(payload, _payloadCodec);
				_ = _eventDispatcher.DispatchAsync(new ClientGameStateChangeReceivedEvent(
					message.GameStateId,
					message.GameStatePayload.ToArray()));
			}
			catch(Exception)
			{
				// Invalid state-change payloads are ignored by the handler.
			}
		}

		private void DispatchChatLobbyReadyState(byte[] payload)
		{
			try
			{
				var message = _payloadCodec.Decode<ChatLobbyReadyStateResponse>(payload);
				_ = _eventDispatcher.DispatchAsync(new ClientChatLobbyReadyStateReceivedEvent(message));
			}
			catch(Exception)
			{
				// Invalid lobby ready payloads are ignored by the parsed event path.
			}
		}

		private void DispatchChatLobbyCountdown(byte[] payload)
		{
			try
			{
				var message = _payloadCodec.Decode<ChatLobbyCountdownResponse>(payload);
				_ = _eventDispatcher.DispatchAsync(new ClientChatLobbyCountdownReceivedEvent(message));
			}
			catch(Exception)
			{
				// Invalid lobby countdown payloads are ignored by the parsed event path.
			}
		}

		private void DispatchChatLobbyPlayerState(byte[] payload)
		{
			try
			{
				var message = _payloadCodec.Decode<ChatLobbyPlayerStateResponse>(payload);
				_ = _eventDispatcher.DispatchAsync(new ClientChatLobbyPlayerStateReceivedEvent(message));
			}
			catch(Exception)
			{
				// Invalid lobby player-state payloads are ignored by the parsed event path.
			}
		}

		private void DispatchMatchPhaseChange(byte[] payload)
		{
			try
			{
				var message = _payloadCodec.Decode<MatchPhaseChangeResponse>(payload);
				_ = _eventDispatcher.DispatchAsync(new ClientMatchPhaseChangeReceivedEvent(message));
			}
			catch(Exception)
			{
				// Invalid match phase-change payloads are ignored by the parsed event path.
			}
		}

		private void DispatchGameStateMessage(byte[] payload)
		{
			try
			{
				var message = GameStateMessageProtocol.Decode(payload, _payloadCodec);
				if(string.IsNullOrWhiteSpace(message.MessageId))
				{
					return;
				}

				_ = _eventDispatcher.DispatchAsync(new ClientGameStateMessageReceivedEvent(
					message.MessageId.Trim(),
					message.Payload.ToArray()));
			}
			catch(Exception)
			{
				// Invalid game-state messages are ignored by the parsed event path.
			}
		}
	}
}
