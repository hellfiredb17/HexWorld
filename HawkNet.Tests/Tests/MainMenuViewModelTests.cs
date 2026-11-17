using Avalonia.Controls;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Common.ViewModels;
using Xunit;

namespace HawkNet.Tests
{
	public class MainMenuViewModelTests
	{
		[Fact]
		public void CreateServerCommand_RaisesCreateServerRequested()
		{
			var viewModel = new MainMenuViewModel();
			var raised = false;
			viewModel.CreateServerRequested += (_, _) => raised = true;

			viewModel.CreateServerCommand.Execute(null);

			Assert.True(raised);
		}

		[Fact]
		public void FindLobbyCommand_RaisesFindLobbyRequested()
		{
			var viewModel = new MainMenuViewModel();
			var raised = false;
			viewModel.FindLobbyRequested += (_, _) => raised = true;

			viewModel.FindLobbyCommand.Execute(null);

			Assert.True(raised);
		}

		[Fact]
		public void OptionalTools_AreOrderedAndRaiseRequestedTool()
		{
			var later = new FakeTool("later", "Later", 20);
			var earlierB = new FakeTool("b", "Earlier B", 10);
			var earlierA = new FakeTool("a", "Earlier A", 10);
			var viewModel = new MainMenuViewModel([later, earlierB, earlierA]);
			ICoreUxTool? requested = null;
			viewModel.ToolRequested += tool => requested = tool;

			Assert.Equal(["a", "b", "later"], viewModel.Tools.Select(tool => tool.Id));

			viewModel.Tools[0].OpenCommand.Execute(null);

			Assert.Same(earlierA, requested);
		}

		[Fact]
		public void NoTools_ProducesNoOptionalMenuEntries()
		{
			Assert.Empty(new MainMenuViewModel().Tools);
		}

		private sealed class FakeTool(string id, string displayName, int order) : ICoreUxTool
		{
			public string Id { get; } = id;
			public string DisplayName { get; } = displayName;
			public int Order { get; } = order;
			public Control CreateView(Action close) => new Border();
		}
	}
}
