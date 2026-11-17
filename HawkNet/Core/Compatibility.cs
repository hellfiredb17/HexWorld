/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Security.Cryptography;

[assembly: InternalsVisibleTo("HawkNet.Tests")]

namespace HawkNet.Core
{
	internal static class TaskCompatibility
	{
		public static async Task WaitAsync(Task task, CancellationToken token)
		{
#if NET8_0_OR_GREATER
			await task.WaitAsync(token);
#else
			if(task.IsCompleted)
			{
				await task;
				return;
			}

			var completion = new TaskCompletionSource<bool>();
			using(token.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), completion))
			{
				if(task == await Task.WhenAny(task, completion.Task))
				{
					await task;
					return;
				}
			}

			throw new OperationCanceledException(token);
#endif
		}
	}

	internal static class Guard
	{
		public static void NotNull<T>(T value, string? parameterName = null)
			where T : class
		{
			if(value is null)
			{
				throw new ArgumentNullException(parameterName);
			}
		}

		public static void NotDisposed(bool disposed, object instance)
		{
			if(disposed)
			{
				throw new ObjectDisposedException(instance.GetType().Name);
			}
		}
	}

	internal static class TimeCompatibility
	{
		public static TimeSpan GetElapsedTime(long startingTimestamp)
		{
#if NET8_0_OR_GREATER
			return Stopwatch.GetElapsedTime(startingTimestamp);
#else
			long elapsedTicks = Stopwatch.GetTimestamp() - startingTimestamp;
			double seconds = (double)elapsedTicks / Stopwatch.Frequency;
			return TimeSpan.FromSeconds(seconds);
#endif
		}
	}

	internal static class RandomCompatibility
	{
		public static byte[] GetBytes(int count)
		{
#if NET8_0_OR_GREATER
			return RandomNumberGenerator.GetBytes(count);
#else
			var bytes = new byte[count];
			using(var generator = RandomNumberGenerator.Create())
			{
				generator.GetBytes(bytes);
			}

			return bytes;
#endif
		}
	}
}

namespace System.Runtime.CompilerServices
{
	internal static class IsExternalInit
	{
	}
}
