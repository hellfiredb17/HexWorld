using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Common.ViewModels;
using HawkNet.CoreUx.Server.Services;
using HawkNet.CoreUx.Server.ViewModels;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class MainViewModelTests
	{
		[Fact]
		public async Task MockFlow_CanMoveFromLobbyToMatchAndBackThroughLoading()
		{
			var viewModel = new MainViewModel(CreateFastFlowConfig());
			viewModel.ServerSettings.StartServerCommand.Execute(null);
			Assert.IsType<LoadingScreenViewModel>(viewModel.CurrentViewModel);

			await WaitForCurrentViewModelAsync<ChatLobbyViewModel>(viewModel);
			var lobby = Assert.IsType<ChatLobbyViewModel>(viewModel.CurrentViewModel);

			lobby.StartMatchCommand.Execute(null);
			Assert.IsType<LoadingScreenViewModel>(viewModel.CurrentViewModel);

			await WaitForCurrentViewModelAsync<ServerMatchViewModel>(viewModel);
			var match = Assert.IsType<ServerMatchViewModel>(viewModel.CurrentViewModel);

			match.StopMatchCommand.Execute(null);
			Assert.IsType<LoadingScreenViewModel>(viewModel.CurrentViewModel);

			await WaitForCurrentViewModelAsync<ChatLobbyViewModel>(viewModel);
			Assert.IsType<ChatLobbyViewModel>(viewModel.CurrentViewModel);
		}

		[Fact]
		public async Task MockFlow_UsesConfiguredTransitionTarget()
		{
			var config = CreateFastFlowConfig();
			config.Transitions[TransitionKeys.StartMatch] = config.Transitions[TransitionKeys.StartMatch] with
			{
				To = ScreenKeys.ChatLobby,
				HeaderText = "Skipping Match"
			};
			var viewModel = new MainViewModel(config);
			viewModel.ServerSettings.StartServerCommand.Execute(null);
			await WaitForCurrentViewModelAsync<ChatLobbyViewModel>(viewModel);
			var lobby = Assert.IsType<ChatLobbyViewModel>(viewModel.CurrentViewModel);

			lobby.StartMatchCommand.Execute(null);
			var loading = Assert.IsType<LoadingScreenViewModel>(viewModel.CurrentViewModel);
			Assert.Equal("Skipping Match", loading.HeaderText);

			await WaitForCurrentViewModelAsync<ChatLobbyViewModel>(viewModel);
			Assert.IsType<ChatLobbyViewModel>(viewModel.CurrentViewModel);
		}

		[Fact]
		public async Task ChatLobbyRuntimeMatchStarted_NavigatesServerUiToMatch()
		{
			var runtime = new MockChatLobbyRuntime();
			var viewModel = new MainViewModel(
				CreateFastFlowConfig(),
				new GameHostConfig(),
				new MockServerLauncher(),
				() => runtime,
				() => new MockServerMatchRuntime());
			viewModel.ServerSettings.StartServerCommand.Execute(null);
			await WaitForCurrentViewModelAsync<ChatLobbyViewModel>(viewModel);

			runtime.CompleteMatchStart();

			await WaitForCurrentViewModelAsync<ServerMatchViewModel>(viewModel);
			Assert.IsType<ServerMatchViewModel>(viewModel.CurrentViewModel);
		}

		[Fact]
		public async Task ServerStartFailure_ReturnsToSettingsAndShowsErrorPopup()
		{
			var viewModel = new MainViewModel(
				CreateFastFlowConfig(),
				new GameHostConfig(),
				new ThrowingServerLauncher("Nope."));

			viewModel.ServerSettings.StartServerCommand.Execute(null);

			await WaitForErrorPopupAsync(viewModel);
			Assert.Same(viewModel.ServerSettings, viewModel.CurrentViewModel);
			Assert.True(viewModel.HasErrorPopup);
			Assert.NotNull(viewModel.ErrorPopup);
			Assert.Equal("Server Start Failed", viewModel.ErrorPopup.Title);
			Assert.Equal("Nope.", viewModel.ErrorPopup.Message);
			Assert.Equal("Start failed: Nope.", viewModel.ServerSettings.StatusMessage);

			viewModel.ErrorPopup.DismissCommand.Execute(null);

			Assert.False(viewModel.HasErrorPopup);
			Assert.Null(viewModel.ErrorPopup);
		}

		[Fact]
		public async Task ServerStartRequest_ShowsLoadingUntilLauncherCompletes()
		{
			var launcher = new BlockingServerLauncher();
			var viewModel = new MainViewModel(
				CreateFastFlowConfig(),
				new GameHostConfig(),
				launcher);

			viewModel.ServerSettings.StartServerCommand.Execute(null);

			await launcher.Started.Task;
			var loading = Assert.IsType<LoadingScreenViewModel>(viewModel.CurrentViewModel);
			Assert.Equal("Starting Server", loading.HeaderText);

			launcher.Complete();

			await WaitForCurrentViewModelAsync<ChatLobbyViewModel>(viewModel);
			Assert.IsType<ChatLobbyViewModel>(viewModel.CurrentViewModel);
		}

		[Fact]
		public async Task StopServerRequest_StopsLauncherAndReturnsToSettings()
		{
			var launcher = new MockServerLauncher();
			var viewModel = new MainViewModel(
				CreateFastFlowConfig(),
				new GameHostConfig(),
				launcher);
			viewModel.ServerSettings.StartServerCommand.Execute(null);
			await WaitForCurrentViewModelAsync<ChatLobbyViewModel>(viewModel);
			var lobby = Assert.IsType<ChatLobbyViewModel>(viewModel.CurrentViewModel);

			lobby.StopServerCommand.Execute(null);

			await WaitForCurrentViewModelAsync<ServerSettingsViewModel>(viewModel);
			Assert.Equal(1, launcher.StopRequestCount);
			Assert.Same(viewModel.ServerSettings, viewModel.CurrentViewModel);
			Assert.Equal("Server stopped.", viewModel.ServerSettings.StatusMessage);
		}

		[Fact]
		public void MainMenu_CreateServerNavigatesToServerSettings()
		{
			var config = CreateFastFlowConfig() with
			{
				InitialView = ScreenKeys.MainMenu
			};
			var viewModel = new MainViewModel(config);
			Assert.IsType<MainMenuViewModel>(viewModel.CurrentViewModel);

			viewModel.MainMenu.CreateServerCommand.Execute(null);

			Assert.Same(viewModel.ServerSettings, viewModel.CurrentViewModel);
		}

		[Fact]
		public void ServerSettings_BackNavigatesToMainMenu()
		{
			var viewModel = new MainViewModel(CreateFastFlowConfig() with
			{
				InitialView = ScreenKeys.MainMenu
			});
			viewModel.MainMenu.CreateServerCommand.Execute(null);
			Assert.Same(viewModel.ServerSettings, viewModel.CurrentViewModel);

			viewModel.ServerSettings.BackCommand.Execute(null);

			Assert.Same(viewModel.MainMenu, viewModel.CurrentViewModel);
		}

		[Fact]
		public void MainMenu_FindLobbyRaisesRequest()
		{
			var config = CreateFastFlowConfig() with
			{
				InitialView = ScreenKeys.MainMenu
			};
			var viewModel = new MainViewModel(config);
			var raised = false;
			viewModel.FindLobbyRequested += (_, _) => raised = true;

			viewModel.MainMenu.FindLobbyCommand.Execute(null);

			Assert.True(raised);
		}

		private static async Task WaitForCurrentViewModelAsync<T>(MainViewModel viewModel)
		{
			var timeoutAt = DateTime.UtcNow.AddSeconds(3);
			while (DateTime.UtcNow < timeoutAt)
			{
				if (viewModel.CurrentViewModel is T)
				{
					return;
				}

				await Task.Delay(25);
			}

			Assert.Fail($"Timed out waiting for {typeof(T).Name}.");
		}

		private static async Task WaitForErrorPopupAsync(MainViewModel viewModel)
		{
			var timeoutAt = DateTime.UtcNow.AddSeconds(3);
			while (DateTime.UtcNow < timeoutAt)
			{
				if (viewModel.HasErrorPopup)
				{
					return;
				}

				await Task.Delay(25);
			}

			Assert.Fail("Timed out waiting for error popup.");
		}

		private static ScreenFlowConfig CreateFastFlowConfig()
		{
			return ScreenFlowConfig.CreateDefault() with
			{
				DefaultLoadingMilliseconds = 50
			};
		}

		private sealed class ThrowingServerLauncher : IServerLauncher
		{
			private readonly string _message;

			public ThrowingServerLauncher(string message)
			{
				_message = message;
			}

			public Task StartAsync(GameHostConfig config, CancellationToken token = default)
			{
				throw new InvalidOperationException(_message);
			}

			public Task StopAsync(CancellationToken token = default)
			{
				return Task.CompletedTask;
			}
		}

		private sealed class BlockingServerLauncher : IServerLauncher
		{
			private readonly TaskCompletionSource _complete = new(TaskCreationOptions.RunContinuationsAsynchronously);

			public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

			public async Task StartAsync(GameHostConfig config, CancellationToken token = default)
			{
				Started.TrySetResult();
				await _complete.Task;
			}

			public Task StopAsync(CancellationToken token = default)
			{
				return Task.CompletedTask;
			}

			public void Complete()
			{
				_complete.TrySetResult();
			}
		}
	}
}
