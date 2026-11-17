/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using System.Text;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public sealed class ClientChatService : IService, IDisposable
	{
		//---- Variables
		//--------------
		private readonly object _historyLock = new();
		private readonly GameClient _client;
		private readonly IEventDispatcher _eventDispatcher;
		private readonly IPayloadCodec _payloadCodec;
		private readonly List<IDisposable> _subscriptions = new();
		private readonly List<ChatEntry> _history = new();
		private bool _disposed;
		private ServiceState _state = ServiceState.NotInitialized;

		//---- Ctor
		//---------
		public ClientChatService(
			GameClient client,
			IEventDispatcher eventDispatcher,
			IPayloadCodec? payloadCodec = null)
		{
			_client = client ?? throw new ArgumentNullException(nameof(client));
			_eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
			_payloadCodec = payloadCodec ?? new JsonPayloadCodec();
			_subscriptions.Add(_eventDispatcher.Subscribe<ClientPacketReceivedEvent>(OnPacketReceived));
			_subscriptions.Add(_eventDispatcher.Subscribe<ClientDisplayNameChangedEvent>(OnDisplayNameChanged));
		}

		public ServiceState State => _state;

		//---- Public
		//-----------
		public IReadOnlyList<ChatEntry> GetHistory()
		{
			lock(_historyLock)
			{
				return _history.ToArray();
			}
		}

		public Task SendMessageAsync(string message, CancellationToken token = default)
		{
			if(string.IsNullOrWhiteSpace(message))
			{
				throw new ArgumentException("Chat message cannot be empty.", nameof(message));
			}

			return _client.SendPacketAsync(PacketType.ChatMessage, Encoding.UTF8.GetBytes(message), token);
		}

		public void Dispose()
		{
			if(_disposed)
			{
				return;
			}

			foreach(var subscription in _subscriptions)
			{
				subscription.Dispose();
			}

			_subscriptions.Clear();
			_state = ServiceState.NotInitialized;
			_disposed = true;
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
			Dispose();
			return Task.CompletedTask;
		}

		public void RegisterServices()
		{
		}

		public void UnregisterServices()
		{
			Dispose();
		}

		//---- Private
		//------------
		private async Task OnPacketReceived(ClientPacketReceivedEvent gameEvent, CancellationToken token)
		{
			if(gameEvent.PacketType != PacketType.ChatMessage)
			{
				return;
			}

			if(TryDecodeHistory(gameEvent.Payload, out var history))
			{
				ReplaceHistory(history);
				await _eventDispatcher.DispatchAsync(new ClientChatHistoryReceivedEvent(GetHistory()), token);
				return;
			}

			if(TryDecodeEntry(gameEvent.Payload, out var entry))
			{
				AddHistory(entry);
				await _eventDispatcher.DispatchAsync(new ClientChatMessageReceivedEvent(entry), token);
			}
		}

		private Task OnDisplayNameChanged(ClientDisplayNameChangedEvent gameEvent, CancellationToken token)
		{
			return _client.SendPacketAsync(
				PacketType.DisplayNameUpdate,
				Encoding.UTF8.GetBytes(gameEvent.DisplayName),
				token);
		}

		private bool TryDecodeHistory(byte[] payload, out ChatEntry[] history)
		{
			history = Array.Empty<ChatEntry>();
			if(!LooksLikeJsonArray(payload))
			{
				return false;
			}

			if(_payloadCodec.TryDecode(payload, out ChatEntry[] decodedHistory))
			{
				history = decodedHistory;
				return true;
			}

			return false;
		}

		private bool TryDecodeEntry(byte[] payload, out ChatEntry entry)
		{
			return _payloadCodec.TryDecode(payload, out entry);
		}

		private static bool LooksLikeJsonArray(byte[] payload)
		{
			foreach(byte value in payload)
			{
				if(char.IsWhiteSpace((char)value))
				{
					continue;
				}

				return value == (byte)'[';
			}

			return false;
		}

		private void ReplaceHistory(IEnumerable<ChatEntry> history)
		{
			lock(_historyLock)
			{
				_history.Clear();
				_history.AddRange(history);
			}
		}

		private void AddHistory(ChatEntry entry)
		{
			lock(_historyLock)
			{
				_history.Add(entry);
			}
		}
	}
}
