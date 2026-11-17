/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 * Created: 11/3/2025 1:19:09 AM
 * Description: Fixed timestep update loop.
 */

using System.Diagnostics;

namespace HawkNet.Core
{
	public readonly record struct UpdateLoopMetricsSnapshot(
		float FixedDelta,
		int MaxCatchUpTicks,
		long TotalTicks,
		double LastTickDurationMilliseconds,
		double AverageTickDurationMilliseconds,
		double MaxTickDurationMilliseconds,
		long CatchUpTicks,
		long CatchUpLimitHitCount,
		long SkippedCatchUpTicks);

	/// <summary>
	/// Runs fixed timestep simulation callbacks with capped catch-up.
	/// </summary>
	public class UpdateLoop
	{
		//---- Events
		//-----------
		public event Action<float>? PreUpdate;
		public event Action<float>? Update;
		public event Action<float>? PostUpdate;
		public event Action<float>? TickUpdate;

		//---- Variables
		//--------------
		private readonly object _stateLock = new();
		private readonly object _trackedTimesLock = new();
		private readonly Dictionary<string, TimeSpan> _trackedTimes = new(StringComparer.OrdinalIgnoreCase);
		private CancellationTokenSource? _cancellationTokenSource;
		private Task? _loopTask;
		private float _delta;
		private float _fixedDelta;
		private TimeSpan _timeSinceStart;
		private int _maxCatchUpTicks;
		private long _totalTicks;
		private long _lastTickDurationTimestampTicks;
		private long _totalTickDurationTimestampTicks;
		private long _maxTickDurationTimestampTicks;
		private long _catchUpTicks;
		private long _catchUpLimitHitCount;
		private long _skippedCatchUpTicks;

		//---- Properties
		//---------------
		/// <summary>
		/// Time represented by each simulation tick.
		/// </summary>
		public float DeltaTime => _delta;

		/// <summary>
		/// Fixed delta time used by the simulation loop.
		/// </summary>
		public float FixedDelta => _fixedDelta;

		public bool IsRunning => _loopTask is { IsCompleted: false };

		public TimeSpan TimeSinceStart
		{
			get
			{
				lock(_trackedTimesLock)
				{
					return _timeSinceStart;
				}
			}
		}

		public UpdateLoopMetricsSnapshot Metrics => GetMetricsSnapshot();

		//---- Public
		//-----------
		public void StartTrackTime(string id)
		{
			string key = NormalizeTrackId(id);
			lock(_trackedTimesLock)
			{
				_trackedTimes[key] = TimeSpan.Zero;
			}
		}

		public TimeSpan GetTrackTime(string id)
		{
			string key = NormalizeTrackId(id);
			lock(_trackedTimesLock)
			{
				return _trackedTimes.TryGetValue(key, out var time)
					? time
					: TimeSpan.Zero;
			}
		}

		public void ResetTrackTime(string id)
		{
			string key = NormalizeTrackId(id);
			lock(_trackedTimesLock)
			{
				if(_trackedTimes.ContainsKey(key))
				{
					_trackedTimes[key] = TimeSpan.Zero;
				}
			}
		}

		public bool RemoveTrackTime(string id)
		{
			string key = NormalizeTrackId(id);
			lock(_trackedTimesLock)
			{
				return _trackedTimes.Remove(key);
			}
		}

		public void StartUpdateLoop(float fixedUpdate)
		{
			StartUpdateLoop(fixedUpdate, 5);
		}

		public void StartUpdateLoop(float fixedUpdate, int maxCatchUpTicks)
		{
			if(fixedUpdate <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(fixedUpdate), "Fixed update time must be greater than zero.");
			}

			if(maxCatchUpTicks <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(maxCatchUpTicks), "Max catch-up ticks must be greater than zero.");
			}

			lock(_stateLock)
			{
				StopUpdateLoop();
				_cancellationTokenSource = new CancellationTokenSource();
				_fixedDelta = fixedUpdate;
				_delta = fixedUpdate;
				_maxCatchUpTicks = maxCatchUpTicks;
				ResetMetrics();
				ResetTimeSinceStart();
				var token = _cancellationTokenSource.Token;
				_loopTask = Task.Run(() => RunLoopAsync(token));
			}
		}

		public void StopUpdateLoop()
		{
			_cancellationTokenSource?.Cancel();
		}

		public async Task StopUpdateLoopAsync()
		{
			Task? loopTask;
			CancellationTokenSource? cancellationTokenSource;

			lock(_stateLock)
			{
				cancellationTokenSource = _cancellationTokenSource;
				loopTask = _loopTask;
				cancellationTokenSource?.Cancel();
				_cancellationTokenSource = null;
				_loopTask = null;
			}

			if(loopTask is not null)
			{
				try
				{
					await loopTask;
				}
				catch(OperationCanceledException)
				{
					// Expected during shutdown.
				}
			}

			cancellationTokenSource?.Dispose();
		}

		//---- Private
		//------------
		private async Task RunLoopAsync(CancellationToken token)
		{
			var stopwatch = Stopwatch.StartNew();
			double previousTime = stopwatch.Elapsed.TotalSeconds;
			double accumulator = 0;

			try
			{
				while(!token.IsCancellationRequested)
				{
					double currentTime = stopwatch.Elapsed.TotalSeconds;
					double elapsed = currentTime - previousTime;
					previousTime = currentTime;
					ProcessElapsed(elapsed, ref accumulator);

					double remainingSeconds = Math.Max(0, _fixedDelta - accumulator);
					int delayMilliseconds = Math.Max(1, (int)Math.Ceiling(remainingSeconds * 1000));
					await Task.Delay(delayMilliseconds, token);
				}
			}
			catch(OperationCanceledException)
			{
				// Expected during shutdown.
			}
			finally
			{
				stopwatch.Stop();
			}
		}

		internal int ProcessElapsedForTesting(float fixedUpdate, int maxCatchUpTicks, double elapsedSeconds)
		{
			_fixedDelta = fixedUpdate;
			_delta = fixedUpdate;
			_maxCatchUpTicks = maxCatchUpTicks;
			ResetMetrics();
			ResetTimeSinceStart();
			double accumulator = 0;
			return ProcessElapsed(elapsedSeconds, ref accumulator);
		}

		private int ProcessElapsed(double elapsedSeconds, ref double accumulator)
		{
			accumulator += elapsedSeconds;

			int ticksProcessed = 0;
			while(accumulator >= _fixedDelta && ticksProcessed < _maxCatchUpTicks)
			{
				RunTick();
				accumulator -= _fixedDelta;
				ticksProcessed++;
			}

			if(ticksProcessed == _maxCatchUpTicks && accumulator >= _fixedDelta)
			{
				long skippedTicks = (long)Math.Floor(accumulator / _fixedDelta);
				Interlocked.Increment(ref _catchUpLimitHitCount);
				Interlocked.Add(ref _skippedCatchUpTicks, skippedTicks);
				accumulator = 0;
			}

			if(ticksProcessed > 1)
			{
				Interlocked.Add(ref _catchUpTicks, ticksProcessed - 1);
			}

			return ticksProcessed;
		}

		private void RunTick()
		{
			long startTimestamp = Stopwatch.GetTimestamp();
			_delta = _fixedDelta;
			UpdateTrackedTimesAndGlobalTime(_fixedDelta);
			PreUpdate?.Invoke(_fixedDelta);
			Update?.Invoke(_fixedDelta);
			PostUpdate?.Invoke(_fixedDelta);
			long elapsedTimestampTicks = Stopwatch.GetTimestamp() - startTimestamp;
			RecordTickDuration(elapsedTimestampTicks);
			TickUpdate?.Invoke(_fixedDelta);
		}

		private UpdateLoopMetricsSnapshot GetMetricsSnapshot()
		{
			long totalTicks = Interlocked.Read(ref _totalTicks);
			long totalDurationTicks = Interlocked.Read(ref _totalTickDurationTimestampTicks);
			double averageDuration = totalTicks > 0
				? ToMilliseconds(totalDurationTicks) / totalTicks
				: 0;

			return new UpdateLoopMetricsSnapshot(
				_fixedDelta,
				_maxCatchUpTicks,
				totalTicks,
				ToMilliseconds(Interlocked.Read(ref _lastTickDurationTimestampTicks)),
				averageDuration,
				ToMilliseconds(Interlocked.Read(ref _maxTickDurationTimestampTicks)),
				Interlocked.Read(ref _catchUpTicks),
				Interlocked.Read(ref _catchUpLimitHitCount),
				Interlocked.Read(ref _skippedCatchUpTicks));
		}

		private void ResetMetrics()
		{
			Interlocked.Exchange(ref _totalTicks, 0);
			Interlocked.Exchange(ref _lastTickDurationTimestampTicks, 0);
			Interlocked.Exchange(ref _totalTickDurationTimestampTicks, 0);
			Interlocked.Exchange(ref _maxTickDurationTimestampTicks, 0);
			Interlocked.Exchange(ref _catchUpTicks, 0);
			Interlocked.Exchange(ref _catchUpLimitHitCount, 0);
			Interlocked.Exchange(ref _skippedCatchUpTicks, 0);
		}

		private void RecordTickDuration(long elapsedTimestampTicks)
		{
			Interlocked.Increment(ref _totalTicks);
			Interlocked.Exchange(ref _lastTickDurationTimestampTicks, elapsedTimestampTicks);
			Interlocked.Add(ref _totalTickDurationTimestampTicks, elapsedTimestampTicks);

			long currentMax;
			do
			{
				currentMax = Interlocked.Read(ref _maxTickDurationTimestampTicks);
				if(elapsedTimestampTicks <= currentMax)
				{
					return;
				}
			}
			while(Interlocked.CompareExchange(ref _maxTickDurationTimestampTicks, elapsedTimestampTicks, currentMax) != currentMax);
		}

		private void UpdateTrackedTimesAndGlobalTime(float delta)
		{
			TimeSpan deltaTime = TimeSpan.FromSeconds(delta);
			lock(_trackedTimesLock)
			{
				_timeSinceStart += deltaTime;
				foreach(string key in _trackedTimes.Keys.ToArray())
				{
					_trackedTimes[key] += deltaTime;
				}
			}
		}

		private static string NormalizeTrackId(string id)
		{
			if(string.IsNullOrWhiteSpace(id))
			{
				throw new ArgumentException("Track id cannot be empty.", nameof(id));
			}

			return id.Trim();
		}

		private void ResetTimeSinceStart()
		{
			lock(_trackedTimesLock)
			{
				_timeSinceStart = TimeSpan.Zero;
			}
		}

		private static double ToMilliseconds(long timestampTicks)
		{
			return timestampTicks * 1000.0 / Stopwatch.Frequency;
		}

	} // end class
} // end namespace
