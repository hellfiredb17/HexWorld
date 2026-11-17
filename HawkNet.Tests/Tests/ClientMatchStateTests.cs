using HawkNet.Core;
using HawkNet.Core.Client;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	public class ClientMatchStateTests
	{
		[Fact]
		public void Enter_AppliesMatchPayload()
		{
			var codec = new JsonPayloadCodec();
			using var state = new ClientMatchState(new EventDispatcher(), codec);
			var payload = new MatchPayload
			{
				VariantId = "base",
				PhaseId = "match",
				PhaseSeconds = 60f,
				PhaseStartedAtUtc = DateTime.UtcNow,
				PhaseEndsAtUtc = DateTime.UtcNow.AddSeconds(60),
				Players =
				[
					new MatchPlayer
					{
						PlayerId = 12,
						DisplayName = "Nova",
						IsConnected = true,
						ReadyPayload = [1, 2, 3]
					}
				]
			};

			state.Enter(codec.Encode(payload));

			Assert.True(state.IsActive);
			Assert.Equal("base", state.VariantId);
			Assert.Equal("match", state.PhaseId);
			Assert.Equal(60f, state.PhaseSeconds);
			Assert.InRange(state.PhaseRemainingSeconds, 0f, 60f);
			var player = Assert.Single(state.Players);
			Assert.Equal(12, player.PlayerId);
			Assert.Equal("Nova", player.DisplayName);
			Assert.Equal([1, 2, 3], player.ReadyPayload);
		}
	}
}
