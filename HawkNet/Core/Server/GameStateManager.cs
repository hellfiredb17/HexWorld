/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using HawkNet.Core.Shared;

namespace HawkNet.Core.Server
{
	/// <summary>
		/// Owns configured game state factories, active state lifecycle, and destination-based state transitions.
	/// </summary>
	public class GameStateManager
	{
		//---- Variables
		//--------------
		private readonly Dictionary<string, Func<IGameState>> _factories = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, GameModeStateSettings> _states = new(StringComparer.OrdinalIgnoreCase);
		private readonly GameModeSettings _settings;
		private readonly ILogger _logger;
		private IGameState? _activeGameState;
		private string? _activeStateId;
		private bool _initialized;
		private long _epoch;

		//---- Properties
		//---------------
		public IGameState? ActiveGameState => _activeGameState;
		public string? ActiveStateId => _activeStateId;
		public long Epoch => Interlocked.Read(ref _epoch);

		//---- Ctor
		//---------
		public GameStateManager(GameModeSettings settings, ILogger? logger = null)
		{
			_settings = settings;
			_logger = logger ?? NullLogger.Instance;
		}

		//---- Public
		//-----------
		public void RegisterState(string id, Func<IGameState> factory)
		{
			if(string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException("Game state id cannot be empty.", nameof(id));
			}

			HawkNet.Core.Guard.NotNull(factory);

			if(!_factories.TryAdd(id.Trim(), factory))
			{
				throw new InvalidOperationException($"Game state factory '{id}' is already registered.");
			}
		}

		public void Start()
		{
			if(_initialized)
			{
				return;
			}

			BuildStateGraph();
			EnterInitialState();
			_initialized = true;
		}

		public void Update(float deltaTime)
		{
			if (_activeGameState is IAsyncGameState asyncState)
				asyncState.TickAsync(deltaTime).GetAwaiter().GetResult();
			else
				_activeGameState?.Update(deltaTime);
		}

		public bool TryTransitionTo(string toStateId, byte[]? payload = null)
		{
			if(string.IsNullOrWhiteSpace(toStateId))
			{
				_logger.Warning("Transition target state id is empty.", nameof(GameStateManager));
				return false;
			}

			if(_activeGameState is null || string.IsNullOrWhiteSpace(_activeStateId))
			{
				_logger.Warning($"Transition to '{toStateId}' rejected: no active state.", nameof(GameStateManager));
				return false;
			}

			if(!_states.TryGetValue(_activeStateId, out var activeStateSettings))
			{
				_logger.Warning($"Transition to '{toStateId}' rejected: active state '{_activeStateId}' is not configured.", nameof(GameStateManager));
				return false;
			}

			string targetStateId = toStateId.Trim();
			if(!activeStateSettings.Transitions.Any(
				allowedStateId => string.Equals(
					allowedStateId?.Trim(),
					targetStateId,
					StringComparison.OrdinalIgnoreCase)))
			{
				_logger.Warning($"Transition to '{targetStateId}' is not configured for state '{_activeStateId}'.", nameof(GameStateManager));
				return false;
			}

			if(!_states.ContainsKey(targetStateId))
			{
				_logger.Warning($"Transition target state '{targetStateId}' is not configured.", nameof(GameStateManager));
				return false;
			}

			if(!_factories.TryGetValue(targetStateId, out var targetFactory))
			{
				_logger.Warning($"Transition target state '{targetStateId}' has no registered factory.", nameof(GameStateManager));
				return false;
			}

			ExitAndDisposeActiveState();
			IGameState targetState = targetFactory();
			try
			{
				EnterState(targetState, payload ?? Array.Empty<byte>());
			}
			catch
			{
				targetState.Dispose();
				throw;
			}
			_activeGameState = targetState;
			_activeStateId = targetStateId;
			Interlocked.Increment(ref _epoch);
			_logger.Log($"Transitioned to state '{targetStateId}'.", nameof(GameStateManager));
			return true;
		}

		public void Stop()
		{
			ExitAndDisposeActiveState();
			_initialized = false;
		}

		//---- Private
		//------------
		private void BuildStateGraph()
		{
			if(string.IsNullOrWhiteSpace(_settings.InitialStateId))
			{
				throw new InvalidOperationException("GameMode.InitialStateId cannot be empty.");
			}

			_states.Clear();
			foreach(var state in _settings.States)
			{
				if(string.IsNullOrWhiteSpace(state.Id))
				{
					throw new InvalidOperationException("GameMode state id cannot be empty.");
				}

				if(!_states.TryAdd(state.Id.Trim(), state))
				{
					throw new InvalidOperationException($"GameMode state id '{state.Id}' is duplicated.");
				}
			}

			if(!_states.ContainsKey(_settings.InitialStateId))
			{
				throw new InvalidOperationException($"GameMode initial state '{_settings.InitialStateId}' is not configured.");
			}

			if(!_factories.ContainsKey(_settings.InitialStateId))
			{
				throw new InvalidOperationException($"GameMode initial state '{_settings.InitialStateId}' has no registered factory.");
			}
		}

		private void EnterInitialState()
		{
			string initialStateId = _settings.InitialStateId.Trim();
			IGameState initialState = _factories[initialStateId]();
			try
			{
				EnterState(initialState, Array.Empty<byte>());
			}
			catch
			{
				initialState.Dispose();
				throw;
			}
			_activeGameState = initialState;
			_activeStateId = initialStateId;
			Interlocked.Increment(ref _epoch);
			_logger.Log($"Entered initial state '{initialStateId}'.", nameof(GameStateManager));
		}

		private void ExitAndDisposeActiveState()
		{
			if(_activeGameState is null)
			{
				_activeStateId = null;
				return;
			}

			if (_activeGameState is IAsyncGameState asyncState)
				asyncState.ExitAsync().GetAwaiter().GetResult();
			else
				_activeGameState.Exit();
			_activeGameState.Dispose();
			_activeGameState = null;
			_activeStateId = null;
		}

		private static void EnterState(IGameState state, byte[] payload)
		{
			if (state is IAsyncGameState asyncState)
				asyncState.EnterAsync(payload).GetAwaiter().GetResult();
			else
				state.Enter(payload);
		}
	}
}
