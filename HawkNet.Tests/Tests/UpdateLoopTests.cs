using HawkNet.Core;
using Xunit;

namespace HawkNet.Tests
{
	public class UpdateLoopTests
	{
		[Fact]
		public void ProcessElapsedForTesting_RunsCallbacksOnlyAfterFixedUpdateTime()
		{
			var updateLoop = new UpdateLoop();
			int updateCount = 0;
			updateLoop.Update += _ => Interlocked.Increment(ref updateCount);

			int firstProcessedTicks = updateLoop.ProcessElapsedForTesting(
				fixedUpdate: 0.1f,
				maxCatchUpTicks: 5,
				elapsedSeconds: 0.04);

			Assert.Equal(0, firstProcessedTicks);
			Assert.Equal(0, Volatile.Read(ref updateCount));

			int secondProcessedTicks = updateLoop.ProcessElapsedForTesting(
				fixedUpdate: 0.1f,
				maxCatchUpTicks: 5,
				elapsedSeconds: 0.11);

			Assert.Equal(1, secondProcessedTicks);
			Assert.Equal(1, Volatile.Read(ref updateCount));
		}

		[Fact]
		public async Task StartUpdateLoop_InvokesCallbacksWithFixedDeltaInOrder()
		{
			var updateLoop = new UpdateLoop();
			var callbackOrder = new List<string>();
			var updateTcs = new TaskCompletionSource();
			float observedDelta = 0;

			updateLoop.PreUpdate += delta =>
			{
				observedDelta = delta;
				callbackOrder.Add("pre");
			};
			updateLoop.Update += delta =>
			{
				observedDelta = delta;
				callbackOrder.Add("update");
			};
			updateLoop.PostUpdate += delta =>
			{
				observedDelta = delta;
				callbackOrder.Add("post");
			};
			updateLoop.TickUpdate += delta =>
			{
				observedDelta = delta;
				updateTcs.TrySetResult();
			};

			updateLoop.StartUpdateLoop(0.02f, 5);

			try
			{
				await updateTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

				Assert.Equal(0.02f, observedDelta);
				Assert.Equal(new[] { "pre", "update", "post" }, callbackOrder.Take(3));
				Assert.Equal(0.02f, updateLoop.DeltaTime);
				Assert.Equal(0.02f, updateLoop.FixedDelta);
				Assert.True(updateLoop.Metrics.TotalTicks >= 1);
				Assert.Equal(0.02f, updateLoop.Metrics.FixedDelta);
				Assert.Equal(5, updateLoop.Metrics.MaxCatchUpTicks);
			}
			finally
			{
				await updateLoop.StopUpdateLoopAsync();
			}
		}

		[Fact]
		public async Task StopUpdateLoopAsync_StopsFutureCallbacks()
		{
			var updateLoop = new UpdateLoop();
			int updateCount = 0;
			updateLoop.Update += _ => Interlocked.Increment(ref updateCount);

			updateLoop.StartUpdateLoop(0.02f, 5);
			await WaitForAsync(
				() => Volatile.Read(ref updateCount) > 0,
				TimeSpan.FromSeconds(1));

			await updateLoop.StopUpdateLoopAsync();
			int countAfterStop = Volatile.Read(ref updateCount);

			await Task.Delay(100);
			Assert.Equal(countAfterStop, Volatile.Read(ref updateCount));
			Assert.False(updateLoop.IsRunning);
		}

		[Fact]
		public void ProcessElapsedForTesting_CapsCatchUpTicks()
		{
			var updateLoop = new UpdateLoop();
			int updateCount = 0;
			updateLoop.Update += _ => updateCount++;

			int processedTicks = updateLoop.ProcessElapsedForTesting(
				fixedUpdate: 0.25f,
				maxCatchUpTicks: 2,
				elapsedSeconds: 1);

			Assert.Equal(2, processedTicks);
			Assert.Equal(2, updateCount);
			Assert.Equal(TimeSpan.FromSeconds(0.5), updateLoop.TimeSinceStart);
			Assert.Equal(2, updateLoop.Metrics.TotalTicks);
			Assert.Equal(1, updateLoop.Metrics.CatchUpTicks);
			Assert.Equal(1, updateLoop.Metrics.CatchUpLimitHitCount);
			Assert.Equal(2, updateLoop.Metrics.SkippedCatchUpTicks);
		}

		[Fact]
		public void ProcessElapsedForTesting_RecordsTickDurationMetrics()
		{
			var updateLoop = new UpdateLoop();
			updateLoop.Update += _ => Thread.Sleep(5);

			int processedTicks = updateLoop.ProcessElapsedForTesting(
				fixedUpdate: 0.01f,
				maxCatchUpTicks: 5,
				elapsedSeconds: 0.01);

			var metrics = updateLoop.Metrics;
			Assert.Equal(1, processedTicks);
			Assert.Equal(1, metrics.TotalTicks);
			Assert.True(metrics.LastTickDurationMilliseconds > 0);
			Assert.True(metrics.AverageTickDurationMilliseconds > 0);
			Assert.True(metrics.MaxTickDurationMilliseconds >= metrics.LastTickDurationMilliseconds);
		}

		[Fact]
		public void TrackedTime_AccumulatesOnEachUpdateAndCanResetRestartAndRemove()
		{
			var updateLoop = new UpdateLoop();
			updateLoop.StartTrackTime("match");

			updateLoop.ProcessElapsedForTesting(
				fixedUpdate: 0.25f,
				maxCatchUpTicks: 5,
				elapsedSeconds: 0.5);

			Assert.Equal(TimeSpan.FromSeconds(0.5), updateLoop.GetTrackTime("match"));

			updateLoop.ResetTrackTime("match");
			Assert.Equal(TimeSpan.Zero, updateLoop.GetTrackTime("match"));

			updateLoop.ProcessElapsedForTesting(
				fixedUpdate: 0.25f,
				maxCatchUpTicks: 5,
				elapsedSeconds: 0.25);
			updateLoop.StartTrackTime("match");
			Assert.Equal(TimeSpan.Zero, updateLoop.GetTrackTime("match"));

			Assert.True(updateLoop.RemoveTrackTime("match"));
			Assert.Equal(TimeSpan.Zero, updateLoop.GetTrackTime("match"));
		}

		private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
		{
			var start = DateTime.UtcNow;
			while(DateTime.UtcNow - start < timeout)
			{
				if(condition())
				{
					return;
				}

				await Task.Delay(10);
			}

			throw new TimeoutException("Timed out waiting for condition.");
		}
	}
}
