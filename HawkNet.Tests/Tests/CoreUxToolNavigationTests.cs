using Avalonia.Controls;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.CoreUx.Client.Services;
using HawkNet.CoreUx.Client.ViewModels;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Server.Services;
using HawkNet.CoreUx.Server.ViewModels;
using Xunit;

namespace HawkNet.Tests;

public sealed class CoreUxToolNavigationTests
{
    [Fact]
    public void ServerTool_CreatesFreshViewsAndCloseReturnsToServerMenu()
    {
        var tool = new FakeTool();
        var viewModel = new MainViewModel(
            ScreenFlowConfigLoader.LoadOrDefault(),
            new GameHostConfig(),
            new MockServerLauncher(),
            () => new MockChatLobbyRuntime(),
            () => new MockServerMatchRuntime(),
            [tool]);

        viewModel.MainMenu.Tools.Single().OpenCommand.Execute(null);
        Control first = Assert.IsAssignableFrom<Control>(viewModel.CurrentViewModel);
        tool.Close();
        Assert.Same(viewModel.MainMenu, viewModel.CurrentViewModel);

        viewModel.OpenTool(tool);
        Control second = Assert.IsAssignableFrom<Control>(viewModel.CurrentViewModel);

        Assert.NotSame(first, second);
        Assert.Equal(2, tool.CreateCount);
    }

    [Fact]
    public void ClientTool_CloseReturnsToClientMenu()
    {
        var tool = new FakeTool();
        var viewModel = new ClientMainViewModel(
            new GameClientConfig(),
            new MockClientLauncher(),
            new MockClientStateManager(),
            () => new MockClientChatLobbyRuntime(),
            () => new MockClientMatchRuntime(),
            startAtMainMenu: true,
            tools: [tool]);

        viewModel.MainMenu.Tools.Single().OpenCommand.Execute(null);
        Assert.IsAssignableFrom<Control>(viewModel.CurrentViewModel);

        tool.Close();

        Assert.Same(viewModel.MainMenu, viewModel.CurrentViewModel);
    }

    private sealed class FakeTool : ICoreUxTool
    {
        private Action? _close;

        public string Id => "fake";
        public string DisplayName => "Fake Tool";
        public int Order => 1;
        public int CreateCount { get; private set; }

        public Control CreateView(Action close)
        {
            CreateCount++;
            _close = close;
            return new Border { Tag = CreateCount };
        }

        public void Close() => _close?.Invoke();
    }
}
