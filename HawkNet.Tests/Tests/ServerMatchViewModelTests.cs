using HawkNet.Core.Shared;
using HawkNet.CoreUx.Client.World;
using HawkNet.CoreUx.Server.Services;
using HawkNet.CoreUx.Server.ViewModels;
using Xunit;

namespace HawkNet.Tests
{
	public class ServerMatchViewModelTests
	{
		[Fact]
		public void ApplyPayload_MapsMatchSummaryAndSortsPlayers()
		{
			var viewModel = new ServerMatchViewModel
			{
				MaxPlayers = 8
			};
			var started = DateTime.UtcNow.AddSeconds(-30);

			viewModel.ApplyPayload(new MatchPayload
			{
				VariantId = "arena",
				PhaseId = "match",
				PhaseStartedAtUtc = started,
				PhaseEndsAtUtc = started.AddSeconds(120),
				Players =
				[
					new MatchPlayer { PlayerId = 3, DisplayName = "zulu", IsConnected = true },
					new MatchPlayer { PlayerId = 1, DisplayName = "Alpha", IsConnected = false },
					new MatchPlayer { PlayerId = 2, DisplayName = "alpha", IsConnected = true }
				]
			});

			Assert.Equal("arena", viewModel.VariantId);
			Assert.Equal("match", viewModel.PhaseId);
			Assert.Equal([1, 2, 3], viewModel.Players.Select(player => player.PlayerId).ToArray());
			Assert.Equal(3, viewModel.PlayerCount);
			Assert.Equal(2, viewModel.ConnectedPlayerCount);
			Assert.Equal("2 / 8", viewModel.PlayerCountText);
			Assert.Equal(TimeSpan.FromSeconds(120), viewModel.TotalMatchTime);
			Assert.Equal([1, 2, 3], viewModel.FilteredPlayers.Select(player => player.PlayerId).ToArray());
		}

		[Fact]
		public void PlayerSearchText_FiltersByNameAndId()
		{
			var viewModel = new ServerMatchViewModel();
			viewModel.ApplyPlayers(
			[
				new MatchPlayer { PlayerId = 12, DisplayName = "Kestrel", IsConnected = true },
				new MatchPlayer { PlayerId = 27, DisplayName = "Riverrun Guard", IsConnected = true }
			]);

			viewModel.PlayerSearchText = "river";
			Assert.Equal([27], viewModel.FilteredPlayers.Select(player => player.PlayerId).ToArray());

			viewModel.PlayerSearchText = "12";
			Assert.Equal([12], viewModel.FilteredPlayers.Select(player => player.PlayerId).ToArray());
		}

		[Fact]
		public void InspectorCommands_SwapVisibleInspectorSection()
		{
			var viewModel = new ServerMatchViewModel();
			Assert.True(viewModel.IsPlayersInspectorVisible);

			viewModel.ShowAdminInspectorCommand.Execute(null);
			Assert.True(viewModel.IsAdminInspectorVisible);
			Assert.False(viewModel.IsPlayersInspectorVisible);

			viewModel.ShowMapInspectorCommand.Execute(null);
			Assert.True(viewModel.IsMapInspectorVisible);
			Assert.Equal(InspectorSections.Map, viewModel.InspectorSection);
		}

		[Fact]
		public void ApplyChatHistory_SortsMessagesByTimestamp()
		{
			var viewModel = new ServerMatchViewModel();
			var later = DateTime.UtcNow;
			var earlier = later.AddMinutes(-2);

			viewModel.ApplyChatHistory(
			[
				new ChatEntry(2, "Bravo", later, "second"),
				new ChatEntry(1, "Alpha", earlier, "first")
			]);

			Assert.Equal(["first", "second"], viewModel.ChatMessages.Select(message => message.Message).ToArray());
		}

		[Fact]
		public void ApplyPhaseChange_UpdatesPhaseAndPlayers()
		{
			var viewModel = new ServerMatchViewModel();
			var started = DateTime.UtcNow;

			viewModel.ApplyPhaseChange(new MatchPhaseChangeResponse
			{
				VariantId = "base",
				PhaseId = "post_match",
				PhaseStartedAtUtc = started,
				PhaseEndsAtUtc = started.AddSeconds(10),
				Players =
				[
					new MatchPlayer { PlayerId = 7, DisplayName = "Seven", IsConnected = true }
				]
			});

			Assert.Equal("post_match", viewModel.PhaseText);
			Assert.Single(viewModel.Players);
			Assert.Equal("1 / 64", viewModel.PlayerCountText);
		}

		[Fact]
		public void StopMatchCommand_RequestsReturnToLobby()
		{
			var viewModel = new ServerMatchViewModel();
			int requests = 0;
			viewModel.ReturnToLobbyRequested += (_, _) => requests++;

			viewModel.StopMatchCommand.Execute(null);

			Assert.Equal(1, requests);
		}

		[Fact]
		public void StopServerCommand_RequestsServerStop()
		{
			var viewModel = new ServerMatchViewModel();
			int requests = 0;
			viewModel.StopServerRequested += (_, _) => requests++;

			viewModel.StopServerCommand.Execute(null);

			Assert.Equal(1, requests);
		}

		[Fact]
		public async Task RuntimeGameTime_FormatsAsMinutesSecondsAndHundredths()
		{
			var runtime = new MockServerMatchRuntime();
			var viewModel = new ServerMatchViewModel(runtime);
			await viewModel.Initialization;

			runtime.ApplyGameTime(TimeSpan.FromSeconds(7.42));

			Assert.Equal("00:07.42", viewModel.GameTimeText);
			Assert.Equal("00:07.42", viewModel.MatchTimeText);
		}

		[Fact]
		public async Task RuntimePhaseTime_FormatsCurrentPhaseElapsedAndTotalDuration()
		{
			var runtime = new MockServerMatchRuntime(new ServerMatchSnapshot
			{
				Payload = new MatchPayload
				{
					VariantId = "base",
					PhaseId = "match",
					PhaseStartedAtUtc = DateTime.UtcNow,
					PhaseEndsAtUtc = DateTime.UtcNow.AddSeconds(5)
				}
			});
			var viewModel = new ServerMatchViewModel(runtime);
			await viewModel.Initialization;

			runtime.ApplyPhaseTime(TimeSpan.FromSeconds(2.25));

			Assert.Equal("00:02.25 / 00:05.00", viewModel.PhaseTimeText);
		}

		[Fact]
		public async Task SendMessageCommand_SendsAdminMessageAndClearsInput()
		{
			var runtime = new MockServerMatchRuntime();
			var viewModel = new ServerMatchViewModel(runtime);
			await viewModel.Initialization;

			viewModel.PendingMessage = "  hello match  ";
			viewModel.SendMessageCommand.Execute(null);
			await WaitForAsync(
				() => runtime.SentMessages.Count == 1,
				TimeSpan.FromSeconds(1));

			Assert.Equal("hello match", runtime.SentMessages[0]);
			Assert.Equal(string.Empty, viewModel.PendingMessage);
			Assert.Equal("Message sent.", viewModel.StatusMessage);
			Assert.Contains(viewModel.ChatMessages, message => message.SenderLabel == "Admin" && message.Message == "hello match");
		}

		[Fact]
		public async Task SendMessageCommand_WhenRuntimeFails_KeepsPendingMessage()
		{
			var runtime = new MockServerMatchRuntime
			{
				SendMessageOverride = (_, _) => throw new InvalidOperationException("chat closed")
			};
			var viewModel = new ServerMatchViewModel(runtime);
			await viewModel.Initialization;

			viewModel.PendingMessage = "hello";
			viewModel.SendMessageCommand.Execute(null);
			await WaitForAsync(
				() => viewModel.StatusMessage.StartsWith("Send failed:", StringComparison.Ordinal),
				TimeSpan.FromSeconds(1));

			Assert.Equal("hello", viewModel.PendingMessage);
			Assert.Equal("Send failed: chat closed", viewModel.StatusMessage);
		}

		[Fact]
		public async Task WorldSelection_UpdatesSelectedPlayerInspector()
		{
			var runtime = new MockServerMatchRuntime(new ServerMatchSnapshot
			{
				Payload = new MatchPayload
				{
					Players =
					[
						new MatchPlayer { PlayerId = 7, DisplayName = "Scout", IsConnected = true }
					]
				},
				World = new WorldSnapshot
				{
					RegionId = "rect island",
					RegionName = "Rect Island",
					Entities =
					[
						new WorldEntitySnapshot
						{
							Id = "player_7_nav",
							EntityType = "NavigatorEntity",
							PlayerId = 7,
							WorldX = 2.5f,
							WorldZ = 2.5f,
							DestinationWorldX = 4.5f,
							DestinationWorldZ = 2.5f,
							MaxSpeed = 3.25f,
							MaxRotationSpeed = 180f,
							CurrentPathIndex = 1,
							PathWorldPositions =
							[
								new WorldPoint(3.5f, 2.5f),
								new WorldPoint(4.5f, 2.5f)
							],
							MovementStatus = "Idle",
							CanMove = true
						}
					]
				}
			});
			var viewModel = new ServerMatchViewModel(runtime);
			await viewModel.Initialization;

			viewModel.World.SelectEntity(viewModel.World.Entities.Single());

			Assert.True(viewModel.HasSelectedPlayer);
			Assert.Equal("Scout", viewModel.SelectedPlayerName);
			Assert.Equal("7", viewModel.SelectedPlayerIdText);
			Assert.Equal("player_7_nav", viewModel.SelectedNavigatorId);
			Assert.Equal("Idle", viewModel.SelectedMovementStatus);
			Assert.Equal("2.5, 2.5", viewModel.SelectedWorldPositionText);
			Assert.Equal("2.5, 2.5", viewModel.SelectedServerPositionText);
			Assert.Equal("2.5, 2.5", viewModel.SelectedRenderPositionText);
			Assert.Equal("4.5, 2.5", viewModel.SelectedDestinationText);
			Assert.Equal("3.25", viewModel.SelectedSpeedText);
			Assert.Equal("180", viewModel.SelectedRotationSpeedText);
			Assert.Equal("1 / 2", viewModel.SelectedPathIndexText);
			Assert.Equal("0", viewModel.SelectedCorrectionErrorText);
		}

		[Fact]
		public async Task PlayerSelectCommand_SelectsMatchingWorldEntity()
		{
			var runtime = new MockServerMatchRuntime(new ServerMatchSnapshot
			{
				Payload = new MatchPayload
				{
					Players = [new MatchPlayer { PlayerId = 7, DisplayName = "Scout", IsConnected = true }]
				},
				World = new WorldSnapshot
				{
					Entities = [new WorldEntitySnapshot { Id = "player_7", PlayerId = 7, DisplayName = "Scout" }]
				}
			});
			var viewModel = new ServerMatchViewModel(runtime);
			await viewModel.Initialization;

			viewModel.Players.Single().SelectCommand.Execute(null);

			Assert.Equal(7, viewModel.World.SelectedEntity?.PlayerId);
			Assert.Equal("Scout", viewModel.SelectedPlayerName);
		}

		private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
		{
			var start = DateTime.UtcNow;
			while (DateTime.UtcNow - start < timeout)
			{
				if (condition())
				{
					return;
				}

				await Task.Delay(10);
			}

			Assert.Fail("Timed out waiting for condition.");
		}
	}
}
