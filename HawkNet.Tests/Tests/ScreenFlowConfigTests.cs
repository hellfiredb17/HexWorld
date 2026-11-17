using System.Text.Json;
using HawkNet.CoreUx.Common.Services;
using Xunit;

namespace HawkNet.Tests
{
	public class ScreenFlowConfigTests
	{
		[Fact]
		public void LoadOrDefault_LoadsTransitionConfigFromJson()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-coreux-flow-{Guid.NewGuid():N}.json");
			var config = new ScreenFlowConfig
			{
				InitialView = ScreenKeys.ChatLobby,
				DefaultLoadingMilliseconds = 10,
				Transitions = new Dictionary<string, ScreenTransitionConfig>
				{
					["custom"] = new()
					{
						From = ScreenKeys.ChatLobby,
						To = ScreenKeys.Match,
						HeaderText = "Custom",
						ContentText = "Custom transition"
					}
				}
			};
			File.WriteAllText(path, JsonSerializer.Serialize(config));

			try
			{
				ScreenFlowConfig loaded = ScreenFlowConfigLoader.LoadOrDefault(path);

				Assert.Equal(ScreenKeys.ChatLobby, loaded.InitialView);
				Assert.Equal(10, loaded.DefaultLoadingMilliseconds);
				Assert.Equal(ScreenKeys.Match, loaded.Transitions["custom"].To);
				Assert.True(loaded.Transitions.ContainsKey(TransitionKeys.ServerStarted));
			}
			finally
			{
				File.Delete(path);
			}
		}

		[Fact]
		public void LoadOrDefault_FallsBackForMissingJson()
		{
			string path = Path.Combine(Path.GetTempPath(), $"missing-hawknet-coreux-flow-{Guid.NewGuid():N}.json");

			ScreenFlowConfig loaded = ScreenFlowConfigLoader.LoadOrDefault(path);

			Assert.Equal(ScreenKeys.ServerSettings, loaded.InitialView);
			Assert.True(loaded.Transitions.ContainsKey(TransitionKeys.StartMatch));
		}
	}
}
