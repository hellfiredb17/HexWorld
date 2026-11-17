using HawkNet.Core;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;

namespace HawkNet.Game.GameState.Match
{
	public sealed class MatchState : Core.Server.GameState, IGameStatePayloadProvider
	{
		private readonly IGameMatch _match;
		private readonly IGameMatchContext _context;
		private readonly MatchSettings _settings;
		private readonly IPayloadCodec _payloadCodec;
		private readonly ILogger _logger;
		private readonly MatchPhaseSettings[] _phases;
		private const string MatchTimerId = "match";
		private const string MatchPhaseTimerId = "match_phase";
		private int _phaseIndex = -1;
		private MatchPhaseSettings? _activePhase;
		private DateTime _phaseStartedAtUtc;
		private DateTime _phaseEndsAtUtc;
		private bool _isActive;
		private bool _isComplete;
		private bool _isMatchPhaseActive;
		private bool _phaseAdvanceQueued;

		public MatchState(
			IEventDispatcher eventDispatcher,
			IGameMatch match,
			IGameMatchContext context,
			MatchSettings settings,
			IPayloadCodec payloadCodec,
			ILogger? logger = null)
			: base(eventDispatcher)
		{
			_match = match ?? throw new ArgumentNullException(nameof(match));
			_context = context ?? throw new ArgumentNullException(nameof(context));
			_settings = settings ?? new MatchSettings();
			_payloadCodec = payloadCodec ?? throw new ArgumentNullException(nameof(payloadCodec));
			_logger = logger ?? NullLogger.Instance;
			_phases = NormalizePhases(_settings.Phases);
			if(_context is GameMatchContext matchContext)
			{
				matchContext.SetCompleteCurrentPhaseHandler(CompleteCurrentPhaseAsync);
			}
		}

		public string VariantId => _settings.VariantId;
		public IGameMatch Match => _match;
		public string ActivePhaseId => _activePhase?.PhaseId ?? string.Empty;
		public float ActivePhaseSeconds => _activePhase?.Seconds ?? 0f;
		public DateTime PhaseStartedAtUtc => _phaseStartedAtUtc;
		public DateTime PhaseEndsAtUtc => _phaseEndsAtUtc;
		public IReadOnlyCollection<MatchPlayer> Players => _match.Players;
		public bool IsActive => _isActive;
		public bool IsComplete => _isComplete;

		public Task CompleteCurrentPhaseAsync(byte[]? payload = null, CancellationToken token = default)
		{
			if(!_isActive || _isComplete || _activePhase is null || _phaseAdvanceQueued)
			{
				return Task.CompletedTask;
			}

			_phaseAdvanceQueued = true;
			return ExecuteOperationAsync(async operationToken =>
			{
				try
				{
					await CompleteActivePhaseAsync(operationToken);
					await AdvanceToNextPhaseAsync(operationToken);
				}
				finally
				{
					_phaseAdvanceQueued = false;
				}
			}, token);
		}

		public override void Enter(byte[] transitionPayload)
		{
			_isActive = true;
			_isComplete = false;
			_phaseIndex = -1;
			if(_context.Services.TryGet<UpdateLoop>(out var updateLoop))
			{
				updateLoop.StartTrackTime(MatchTimerId);
			}

			var initialPayload = CreateInitialPayload(transitionPayload);
			ExecuteOperationAsync(async token =>
			{
				Subscribe<MatchPlayerJoinedEvent>(OnMatchPlayerJoined);
				Subscribe<MatchPlayerDisconnectedEvent>(OnMatchPlayerDisconnected);
				await _match.InitializeAsync(_context, initialPayload, token);
				await _match.SubscribeAsync(token);
				await AdvanceToNextPhaseAsync(token);
			}).GetAwaiter().GetResult();
			_logger.Log($"Entered match variant '{VariantId}'.", nameof(MatchState));
		}

		public override void Update(float deltaTime)
		{
			if(!_isActive || _isComplete)
			{
				return;
			}

			ExecuteOperationAsync(token => _match.UpdateAsync(deltaTime, token)).GetAwaiter().GetResult();
			if(_activePhase is null || _activePhase.Seconds < 0f)
			{
				return;
			}

			if(DateTime.UtcNow >= _phaseEndsAtUtc && !_phaseAdvanceQueued)
			{
				CompleteCurrentPhaseAsync().GetAwaiter().GetResult();
			}
		}

		public override void Exit()
		{
			_isActive = false;
			if(_context.Services.TryGet<UpdateLoop>(out var updateLoop))
			{
				updateLoop.RemoveTrackTime(MatchTimerId);
				updateLoop.RemoveTrackTime(MatchPhaseTimerId);
			}

			ExecuteOperationAsync(async token =>
			{
				await _match.UnsubscribeAsync(token);
				_match.Dispose();
			}).GetAwaiter().GetResult();
			UnsubscribeAll();
			_logger.Log("Exited.", nameof(MatchState));
		}

		public byte[] CreatePayloadForPlayer(int playerId, IPayloadCodec codec)
		{
			HawkNet.Core.Guard.NotNull(codec);
			return codec.Encode(CreatePayload());
		}

		private MatchPayload CreateInitialPayload(byte[] transitionPayload)
		{
			if(transitionPayload.Length > 0)
			{
				try
				{
					var readyState = _payloadCodec.Decode<ChatLobbyReadyStateResponse>(transitionPayload);
					return new MatchPayload
					{
						VariantId = VariantId,
						Players = readyState.Players
							.Select(player => new MatchPlayer
							{
								PlayerId = player.PlayerId,
								DisplayName = player.DisplayName,
								IsConnected = true,
								ReadyPayload = player.ReadyPayload.ToArray()
							})
							.ToArray()
					};
				}
				catch(Exception ex)
				{
					_logger.Warning($"Failed to decode match transition payload: {ex.Message}", nameof(MatchState));
				}
			}

			return new MatchPayload
			{
				VariantId = VariantId,
				Players = _context.GetConnectedPlayerIds()
					.Select(playerId => new MatchPlayer
					{
						PlayerId = playerId,
					DisplayName = _context.PlayerIdentityService?.GetDisplayName(playerId) ?? $"Player {playerId}",
						IsConnected = true,
						ReadyPayload = []
					})
					.ToArray()
			};
		}

		private MatchPayload CreatePayload()
		{
			return new MatchPayload
			{
				VariantId = VariantId,
				PhaseId = ActivePhaseId,
				PhaseSeconds = ActivePhaseSeconds,
				PhaseStartedAtUtc = _phaseStartedAtUtc,
				PhaseEndsAtUtc = _phaseEndsAtUtc,
				Players = _match.Players.ToArray()
			};
		}

		private async Task AdvanceToNextPhaseAsync(CancellationToken token)
		{
			while(_isActive && !_isComplete)
			{
				_phaseIndex++;
				if(_phaseIndex >= _phases.Length)
				{
					await CompleteMatchAsync(token);
					return;
				}

				await EnterPhaseAsync(_phases[_phaseIndex], token);
				if(_activePhase is null || _activePhase.Seconds != 0f)
				{
					return;
				}

				await CompleteActivePhaseAsync(token);
			}
		}

		private async Task EnterPhaseAsync(MatchPhaseSettings phase, CancellationToken token)
		{
			_activePhase = phase;
			if(_context.Services.TryGet<UpdateLoop>(out var updateLoop))
			{
				updateLoop.StartTrackTime(MatchPhaseTimerId);
			}

			_phaseStartedAtUtc = DateTime.UtcNow;
			_phaseEndsAtUtc = phase.Seconds < 0f
				? DateTime.MaxValue
				: _phaseStartedAtUtc.AddSeconds(Math.Max(0f, phase.Seconds));
			_logger.Log($"Entered match phase '{phase.PhaseId}'.", nameof(MatchState));

			switch(phase.PhaseId)
			{
				case "pre_match":
					await _match.PreMatchAsync(token);
					break;
				case "match":
					_isMatchPhaseActive = true;
					await _match.BeginMatchAsync(token);
					await EventDispatcher.DispatchAsync(new MatchStartedEvent(CreatePayload()), token);
					break;
				case "post_match":
					await _match.PostMatchAsync(token);
					break;
				default:
					await _match.EnterPhaseAsync(phase, token);
					break;
			}

			if (_context.ChatService is not null)
				await _context.ChatService.SendSystemMessageAsync($"Phase changed to {phase.PhaseId}.", "Admin", token);
			await EventDispatcher.DispatchAsync(new MatchPhaseChangedEvent(phase.PhaseId, phase, CreatePayload()), token);
			await BroadcastPhaseChangeAsync(token);
		}

		private async Task CompleteActivePhaseAsync(CancellationToken token)
		{
			if(_isMatchPhaseActive && string.Equals(ActivePhaseId, "match", StringComparison.OrdinalIgnoreCase))
			{
				_isMatchPhaseActive = false;
				await _match.EndMatchAsync(token);
				await EventDispatcher.DispatchAsync(new MatchEndedEvent(CreatePayload()), token);
			}
		}

		private async Task CompleteMatchAsync(CancellationToken token)
		{
			if(_isComplete)
			{
				return;
			}

			await CompleteActivePhaseAsync(token);
			_isComplete = true;
			_activePhase = null;
			if(!string.IsNullOrWhiteSpace(_settings.CompletedTransitionStateId))
			{
				await RequestTransitionAsync(_settings.CompletedTransitionStateId.Trim(), Array.Empty<byte>(), token);
			}
		}

		private Task BroadcastPhaseChangeAsync(CancellationToken token)
		{
			return BroadcastAsync(
				PacketType.MatchPhaseChange,
				_payloadCodec.Encode(new MatchPhaseChangeResponse
				{
					VariantId = VariantId,
					PhaseId = ActivePhaseId,
					PhaseSeconds = ActivePhaseSeconds,
					PhaseStartedAtUtc = _phaseStartedAtUtc,
					PhaseEndsAtUtc = _phaseEndsAtUtc,
					Players = _match.Players.ToArray()
				}),
				token);
		}

		private async Task OnMatchPlayerJoined(MatchPlayerJoinedEvent gameEvent, CancellationToken token)
		{
			if(!_isActive || _isComplete)
			{
				return;
			}

			if (_context.ChatService is not null)
				await _context.ChatService.SendSystemMessageAsync($"{gameEvent.Player.DisplayName} joined the match.", "Admin", token);
			await BroadcastPhaseChangeAsync(token);
		}

		private async Task OnMatchPlayerDisconnected(MatchPlayerDisconnectedEvent gameEvent, CancellationToken token)
		{
			if(!_isActive || _isComplete)
			{
				return;
			}

			string message = gameEvent.Reason == ConnectionCloseReason.ClientDisconnected
				? $"{gameEvent.Player.DisplayName} left the match."
				: $"{gameEvent.Player.DisplayName} lost connection.";
			if (_context.ChatService is not null)
				await _context.ChatService.SendSystemMessageAsync(message, "Admin", token);
			await BroadcastPhaseChangeAsync(token);
		}

		private async Task ExecuteOperationAsync(Func<CancellationToken, Task> operation, CancellationToken token = default)
		{
			try
			{
				await operation(token);
			}
			catch(Exception ex)
			{
				_logger.Exception(ex, "Match operation failed.", nameof(MatchState));
				throw;
			}
		}

		private static MatchPhaseSettings[] NormalizePhases(IEnumerable<MatchPhaseSettings>? phases)
		{
			var normalized = (phases ?? Array.Empty<MatchPhaseSettings>())
				.Where(phase => !string.IsNullOrWhiteSpace(phase.PhaseId))
				.Select(phase => phase with
				{
					PhaseId = phase.PhaseId.Trim()
				})
				.ToArray();

			return normalized.Length == 0
				? new MatchSettings().Phases
				: normalized;
		}
	}
}
