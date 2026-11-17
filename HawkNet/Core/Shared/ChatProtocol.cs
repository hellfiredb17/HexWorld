/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

namespace HawkNet.Core.Shared
{
	public readonly record struct ChatEntry(
		int PlayerId,
		string DisplayName,
		DateTime TimestampUtc,
		string Message);
}
