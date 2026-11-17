using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public sealed class ClientMatchState : ClientGameState<MatchPayload>
	{
		private readonly List<MatchPlayer> _players = new();
		private MatchPayload? _payload;
		private bool _isActive;

		public ClientMatchState(IEventDispatcher eventDispatcher, IPayloadCodec payloadCodec)
			: base(eventDispatcher, payloadCodec)
		{
		}

		public bool IsActive => _isActive;
		public string VariantId => _payload?.VariantId ?? string.Empty;
		public string PhaseId => _payload?.PhaseId ?? string.Empty;
		public float PhaseSeconds => _payload?.PhaseSeconds ?? 0f;
		public DateTime PhaseStartedAtUtc => _payload?.PhaseStartedAtUtc ?? default;
		public DateTime PhaseEndsAtUtc => _payload?.PhaseEndsAtUtc ?? default;
		public IReadOnlyList<MatchPlayer> Players => _players.ToArray();
		public float PhaseRemainingSeconds
		{
			get
			{
				if(_payload is null || _payload.PhaseEndsAtUtc == default || _payload.PhaseEndsAtUtc == DateTime.MaxValue)
				{
					return 0f;
				}

				return Math.Max(0f, (float)(_payload.PhaseEndsAtUtc - DateTime.UtcNow).TotalSeconds);
			}
		}

		protected override void Enter(MatchPayload? payload)
		{
			_isActive = true;
			if(payload is not null)
			{
				ApplyPayload(payload);
			}

			Subscribe<ClientMatchPhaseChangeReceivedEvent>(OnPhaseChangeReceived);
		}

		public override void Update(float deltaTime)
		{
		}

		public override void Exit()
		{
			_isActive = false;
			_payload = null;
			_players.Clear();
			UnsubscribeAll();
		}

		private Task OnPhaseChangeReceived(ClientMatchPhaseChangeReceivedEvent gameEvent, CancellationToken token)
		{
			ApplyPayload(new MatchPayload
			{
				VariantId = gameEvent.PhaseChange.VariantId,
				PhaseId = gameEvent.PhaseChange.PhaseId,
				PhaseSeconds = gameEvent.PhaseChange.PhaseSeconds,
				PhaseStartedAtUtc = gameEvent.PhaseChange.PhaseStartedAtUtc,
				PhaseEndsAtUtc = gameEvent.PhaseChange.PhaseEndsAtUtc,
				Players = gameEvent.PhaseChange.Players
			});
			return Task.CompletedTask;
		}

		private void ApplyPayload(MatchPayload payload)
		{
			_payload = payload;
			_players.Clear();
			_players.AddRange(payload.Players);
		}
	}
}
