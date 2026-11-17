using HawkNet.Core.Shared;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Common.ViewModels;
using HawkNet.CoreUx.Server.Services;
using HawkNet.CoreUx.Server.ViewModels;
using Xunit;

namespace HawkNet.Tests
{
	public class ChatLobbyViewModelTests
	{
		[Fact]
		public void ApplyPlayers_SortsMembersByDisplayNameAscendingThenPlayerId()
		{
			var viewModel = new ChatLobbyViewModel(new MockChatLobbyRuntime(new ChatLobbySnapshot()));

			viewModel.ApplyPlayers(
			[
				new ChatLobbyPlayer { PlayerId = 3, DisplayName = "zulu", IsReady = false },
				new ChatLobbyPlayer { PlayerId = 2, DisplayName = "Alpha", IsReady = true },
				new ChatLobbyPlayer { PlayerId = 1, DisplayName = "alpha", IsReady = false }
			]);

			Assert.Equal([1, 2, 3], viewModel.Members.Select(member => member.PlayerId).ToArray());
			Assert.Equal(3, viewModel.PlayerCount);
			Assert.Equal(1, viewModel.ReadyPlayerCount);
			Assert.Equal("3 / 64", viewModel.PlayerCountText);
			Assert.Equal("1 ready", viewModel.ReadyCountText);
			Assert.Equal("[1][alpha]", viewModel.Members[0].DisplayLabel);
		}

		[Fact]
		public void ApplyReadyState_UpdatesExistingMemberReadiness()
		{
			var viewModel = new ChatLobbyViewModel(new MockChatLobbyRuntime(new ChatLobbySnapshot()));
			viewModel.ApplyPlayers(
			[
				new ChatLobbyPlayer { PlayerId = 1, DisplayName = "Alpha", IsReady = false }
			]);

			viewModel.ApplyReadyState(new ChatLobbyReadyStateResponse
			{
				Players =
				[
					new ChatLobbyReadyPlayer { PlayerId = 1, DisplayName = "Alpha", IsReady = true }
				]
			});

			Assert.True(viewModel.Members.Single().IsReady);
			Assert.Equal("Ready", viewModel.Members.Single().ReadyText);
			Assert.Equal(1, viewModel.ReadyPlayerCount);
		}

		[Fact]
		public void ApplyChatHistory_SortsMessagesByTimestamp()
		{
			var viewModel = new ChatLobbyViewModel(new MockChatLobbyRuntime(new ChatLobbySnapshot()));
			var later = DateTime.UtcNow;
			var earlier = later.AddMinutes(-1);

			viewModel.ApplyChatHistory(
			[
				new ChatEntry(2, "Bravo", later, "second"),
				new ChatEntry(1, "Alpha", earlier, "first")
			]);

			Assert.Equal(["first", "second"], viewModel.ChatMessages.Select(message => message.Message).ToArray());
		}

		[Fact]
		public void ChatMessages_UseAdminLabelForServerMessagesAndPlayerIdLabelsForPlayers()
		{
			var admin = new ChatMessageViewModel(0, "Server", DateTime.UtcNow, "admin message");
			var player = new ChatMessageViewModel(42, "Nova", DateTime.UtcNow, "player message");

			Assert.Equal("Admin", admin.SenderLabel);
			Assert.Equal("[42][Nova]", player.SenderLabel);
			Assert.NotSame(admin.SenderBrush, player.SenderBrush);
		}

		[Fact]
		public async Task SendMessageCommand_SendsPendingMessageAndClearsInput()
		{
			var runtime = new MockChatLobbyRuntime(new ChatLobbySnapshot());
			var viewModel = new ChatLobbyViewModel(runtime)
			{
				PendingMessage = " hello lobby "
			};

			await viewModel.Initialization;
			viewModel.SendMessageCommand.Execute(null);
			await Task.Delay(25);

			Assert.Equal(["hello lobby"], runtime.SentMessages);
			Assert.Equal(string.Empty, viewModel.PendingMessage);
		}

		[Fact]
		public async Task Constructor_AppliesInjectedRuntimeSnapshot()
		{
			var runtime = new MockChatLobbyRuntime(new ChatLobbySnapshot
			{
				MaxPlayers = 12,
				Players =
				[
					new ChatLobbyPlayer { PlayerId = 4, DisplayName = "Delta", IsReady = true }
				],
				ChatHistory =
				[
					new ChatEntry(4, "Delta", DateTime.UtcNow, "real snapshot")
				],
				ReadyState = new ChatLobbyReadyStateResponse
				{
					Players =
					[
						new ChatLobbyReadyPlayer { PlayerId = 4, DisplayName = "Delta", IsReady = true }
					],
					ReadyPlayerCount = 1,
					RequiredReadyPlayerCount = 1,
					ReadyPercentage = 100f
				}
			});

			var viewModel = new ChatLobbyViewModel(runtime);

			await viewModel.Initialization;

			Assert.Equal("1 / 12", viewModel.PlayerCountText);
			Assert.Equal("1 ready", viewModel.ReadyCountText);
			Assert.Equal("real snapshot", Assert.Single(viewModel.ChatMessages).Message);
		}

		[Fact]
		public async Task CountdownChanges_UpdateCountdownText()
		{
			var runtime = new MockChatLobbyRuntime(new ChatLobbySnapshot());
			var viewModel = new ChatLobbyViewModel(runtime);
			await viewModel.Initialization;

			runtime.ApplyCountdown(new ChatLobbyCountdownResponse
			{
				Status = ChatLobbyCountdownStatus.Started,
				RemainingSeconds = 4.2f
			});

			Assert.Equal("Started: 5s", viewModel.CountdownText);

			runtime.ApplyCountdown(null);

			Assert.Equal("No countdown", viewModel.CountdownText);
		}

		[Fact]
		public async Task SendMessageCommand_KeepsPendingMessageWhenRuntimeFails()
		{
			var runtime = new MockChatLobbyRuntime(new ChatLobbySnapshot())
			{
				SendMessageOverride = (_, _) => throw new InvalidOperationException("offline")
			};
			var viewModel = new ChatLobbyViewModel(runtime)
			{
				PendingMessage = "keep me"
			};

			await viewModel.Initialization;
			viewModel.SendMessageCommand.Execute(null);
			await Task.Delay(25);

			Assert.Equal("keep me", viewModel.PendingMessage);
			Assert.Contains("offline", viewModel.StatusMessage);
		}

		[Fact]
		public async Task StartMatchCommand_RaisesLoadingRequestThatWaitsForRuntime()
		{
			var runtime = new MockChatLobbyRuntime(new ChatLobbySnapshot());
			var viewModel = new ChatLobbyViewModel(runtime);
			LoadingTransitionRequest? request = null;
			viewModel.MatchStartRequested += (_, transitionRequest) => request = transitionRequest;

			await viewModel.Initialization;
			viewModel.StartMatchCommand.Execute(null);

			Assert.NotNull(request);
			await request!.WaitForReadyAsync(CancellationToken.None);
			Assert.Equal(1, runtime.StartMatchRequestCount);
		}

		[Fact]
		public async Task RuntimeSnapshotChanges_UpdateMembersAndStatus()
		{
			var runtime = new MockChatLobbyRuntime(new ChatLobbySnapshot());
			var viewModel = new ChatLobbyViewModel(runtime);
			await viewModel.Initialization;

			runtime.ApplySnapshot(new ChatLobbySnapshot
			{
				Players =
				[
					new ChatLobbyPlayer { PlayerId = 9, DisplayName = "Nine", IsReady = false }
				]
			});

			Assert.Equal("[9][Nine]", Assert.Single(viewModel.Members).DisplayLabel);
			Assert.Equal("[9][Nine] joined.", viewModel.StatusMessage);
		}

		[Fact]
		public async Task RuntimeChatMessages_UpdateChatAndStatus()
		{
			var runtime = new MockChatLobbyRuntime(new ChatLobbySnapshot());
			var viewModel = new ChatLobbyViewModel(runtime);
			await viewModel.Initialization;

			runtime.AddChatMessage(new ChatEntry(7, "Seven", DateTime.UtcNow, "hello"));

			Assert.Equal("hello", Assert.Single(viewModel.ChatMessages).Message);
			Assert.Equal("[7][Seven] sent a message.", viewModel.StatusMessage);
		}

		[Fact]
		public async Task StopServerCommand_RaisesStopServerRequest()
		{
			var viewModel = new ChatLobbyViewModel(new MockChatLobbyRuntime(new ChatLobbySnapshot()));
			int requests = 0;
			viewModel.StopServerRequested += (_, _) => requests++;

			await viewModel.Initialization;
			viewModel.StopServerCommand.Execute(null);

			Assert.Equal(1, requests);
		}
	}
}
