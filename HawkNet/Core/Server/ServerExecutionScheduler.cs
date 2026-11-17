using System.Collections.Concurrent;
using System.Diagnostics;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server;

public readonly record struct InboundServerCommand(
	long Sequence,
	long ConnectionSequence,
	long StateEpoch,
	int PlayerId,
	PacketType PacketType,
	byte[] Payload,
	DateTime ReceivedAtUtc);

public readonly record struct ServerInboxMetricsSnapshot(
	int Depth,
	int Capacity,
	int HighWaterMark,
	long Accepted,
	long Rejected,
	double LastLatencyMilliseconds,
	double MaxLatencyMilliseconds);

public interface IServerInbox
{
	ServerInboxMetricsSnapshot Metrics { get; }
	bool TryEnqueue(InboundServerCommand command);
}

public interface IServerExecutionScheduler : IServerInbox
{
	long NextSequence();
	long NextConnectionSequence(int playerId);
	Task ScheduleAsync(Func<CancellationToken, Task> work, CancellationToken token = default);
}

internal sealed record ServerExecutionWorkItem(
	InboundServerCommand? Command,
	Func<CancellationToken, Task> Work,
	long EnqueuedTimestamp);

public sealed class ServerExecutionScheduler : IServerExecutionScheduler
{
	private readonly object _lock = new();
	private readonly Queue<ServerExecutionWorkItem> _queue = new();
	private readonly ConcurrentDictionary<int, long> _connectionSequences = new();
	private readonly int _capacity;
	private long _sequence;
	private long _accepted;
	private long _rejected;
	private int _highWaterMark;
	private long _lastLatencyTicks;
	private long _maxLatencyTicks;

	public ServerExecutionScheduler(int capacity)
	{
		if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
		_capacity = capacity;
	}

	public ServerInboxMetricsSnapshot Metrics
	{
		get
		{
			int depth;
			lock (_lock) depth = _queue.Count;
			return new ServerInboxMetricsSnapshot(
				depth,
				_capacity,
				Volatile.Read(ref _highWaterMark),
				Interlocked.Read(ref _accepted),
				Interlocked.Read(ref _rejected),
				ToMilliseconds(Interlocked.Read(ref _lastLatencyTicks)),
				ToMilliseconds(Interlocked.Read(ref _maxLatencyTicks)));
		}
	}

	public long NextSequence() => Interlocked.Increment(ref _sequence);

	public long NextConnectionSequence(int playerId) =>
		_connectionSequences.AddOrUpdate(playerId, 1, (_, current) => current + 1);

	public Task ScheduleAsync(Func<CancellationToken, Task> work, CancellationToken token = default)
	{
		if (work is null) throw new ArgumentNullException(nameof(work));
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!TryEnqueue(async schedulerToken =>
		{
			try
			{
				await work(schedulerToken);
				completion.TrySetResult(true);
			}
			catch (Exception exception) { completion.TrySetException(exception); }
		})) throw new InvalidOperationException("The server command queue is full.");
		if (token.CanBeCanceled) token.Register(() => completion.TrySetCanceled(token));
		return completion.Task;
	}

	public bool TryEnqueue(InboundServerCommand command) =>
		TryEnqueueCore(new ServerExecutionWorkItem(command, _ => Task.CompletedTask, Stopwatch.GetTimestamp()));

	internal bool TryEnqueue(Func<CancellationToken, Task> work) =>
		TryEnqueueCore(new ServerExecutionWorkItem(null, work, Stopwatch.GetTimestamp()));

	internal int Drain(
		int maximum,
		Func<InboundServerCommand, CancellationToken, Task> commandHandler,
		CancellationToken token = default)
	{
		int processed = 0;
		while (processed < maximum && TryDequeue(out ServerExecutionWorkItem? item))
		{
			token.ThrowIfCancellationRequested();
			RecordLatency(Stopwatch.GetTimestamp() - item!.EnqueuedTimestamp);
			Task task = item.Command is InboundServerCommand command
				? commandHandler(command, token)
				: item.Work(token);
			task.GetAwaiter().GetResult();
			processed++;
		}
		return processed;
	}

	private bool TryEnqueueCore(ServerExecutionWorkItem item)
	{
		lock (_lock)
		{
			if (_queue.Count >= _capacity)
			{
				Interlocked.Increment(ref _rejected);
				return false;
			}
			_queue.Enqueue(item);
			Interlocked.Increment(ref _accepted);
			if (_queue.Count > _highWaterMark) _highWaterMark = _queue.Count;
			return true;
		}
	}

	private bool TryDequeue(out ServerExecutionWorkItem? item)
	{
		lock (_lock)
		{
			if (_queue.Count == 0)
			{
				item = null;
				return false;
			}
			item = _queue.Dequeue();
			return true;
		}
	}

	private void RecordLatency(long ticks)
	{
		Interlocked.Exchange(ref _lastLatencyTicks, ticks);
		long current;
		do
		{
			current = Interlocked.Read(ref _maxLatencyTicks);
			if (ticks <= current) return;
		}
		while (Interlocked.CompareExchange(ref _maxLatencyTicks, ticks, current) != current);
	}

	private static double ToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
