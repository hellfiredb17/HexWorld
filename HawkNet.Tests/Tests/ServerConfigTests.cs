using System.Text.Json;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class ServerConfigTests
	{
		[Fact]
		public void MatchSettings_DefaultsProvideStandardPhaseFlow()
		{
			var config = new GameHostConfig();

			Assert.Equal(
				["pre_match", "match", "post_match"],
				config.Match.Phases.Select(phase => phase.PhaseId).ToArray());
			Assert.Equal([5f, 300f, 10f], config.Match.Phases.Select(phase => phase.Seconds).ToArray());
			Assert.Equal("base", config.Match.VariantId);
			Assert.Equal(string.Empty, config.Match.CompletedTransitionStateId);
		}

		[Fact]
		public void MatchSettings_DeserializesCustomPhaseFlow()
		{
			const string json = """
			{
			  "Match": {
			    "VariantId": "arena",
			    "Phases": [
			      {
			        "PhaseId": "loading",
			        "Seconds": -1
			      },
			      {
			        "PhaseId": "match",
			        "Seconds": 120
			      }
			    ],
			    "CompletedTransitionStateId": "results"
			  }
			}
			""";

			var config = JsonSerializer.Deserialize<GameHostConfig>(
				json,
				new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});

			Assert.NotNull(config);
			Assert.Equal(["loading", "match"], config.Match.Phases.Select(phase => phase.PhaseId).ToArray());
			Assert.Equal([-1f, 120f], config.Match.Phases.Select(phase => phase.Seconds).ToArray());
			Assert.Equal("arena", config.Match.VariantId);
			Assert.Equal("results", config.Match.CompletedTransitionStateId);
		}

		[Fact]
		public void ConfigService_ReturnsKnownServerSettings()
		{
			var root = new GameHostConfig
			{
				Server = new ServerSettings
				{
					Port = 8123
				},
				Match = new MatchSettings
				{
					VariantId = "arena"
				}
			};
			var config = new ConfigService(root);

			Assert.Same(root, config.Root);
			Assert.Same(root, config.Get<GameHostConfig>());
			Assert.Equal(8123, config.Get<ServerSettings>().Port);
			Assert.Equal("arena", config.Get<MatchSettings>().VariantId);
			Assert.Equal("chat_lobby", config.Get<GameModeSettings>().InitialStateId);
		}

		[Fact]
		public void ConfigService_UnknownSettingsTypeFailsClearly()
		{
			var config = new ConfigService(new GameHostConfig());

			var exception = Assert.Throws<InvalidOperationException>(() => config.Get<UnknownSettings>());

			Assert.Contains(nameof(UnknownSettings), exception.Message);
		}

		private sealed class UnknownSettings
		{
		}
	}
}
