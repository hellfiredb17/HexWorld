/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 * Created: 10/30/2025 10:56:52 PM
 * Description: Game host interface.
 */

namespace HawkNet.Core.Server
{
	public interface IGameHost
	{
		public Task BeginAsync();
		public Task StopAsync();

	} // end class
} // end namespace
