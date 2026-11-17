using System.Net;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server;

public sealed record ServerMonitorSnapshot
{
	public GameHostLifecycleState LifecycleState { get; init; }
	public IPEndPoint? ListenEndPoint { get; init; }
	public string ActiveStateId { get; init; } = string.Empty;
	public IReadOnlyList<PlayerSessionSnapshot> Sessions { get; init; } = Array.Empty<PlayerSessionSnapshot>();
	public ServerInboxMetricsSnapshot Inbox { get; init; }
	public ServerDiagnosticsSnapshot? Diagnostics { get; init; }
	public UdpDeliveryMetricsSnapshot UdpDelivery { get; init; }
	public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;

	public string Health => LifecycleState != GameHostLifecycleState.Running ? LifecycleState.ToString()
		: Inbox.Depth >= Math.Max(1, (int)(Inbox.Capacity * 0.8)) ? "Degraded"
		: UdpDelivery.RetryExhausted > 0 ? "Degraded"
		: "Healthy";
}
