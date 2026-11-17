using ClanIronSim.CoreUx.WorldBuilder;
using ClanIronSim.CoreUx.WorldBuilder.ViewModels;
using ClanIronSim.CoreUx.WorldBuilder.Views;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.CoreUx.Client.Services;
using HawkNet.CoreUx.Client.ViewModels;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Server.Services;
using HawkNet.CoreUx.Server.ViewModels;
using Xunit;

namespace HawkNet.Tests;

public sealed class ClanIronWorldBuilderToolTests
{
    [Fact]
    public void ServerMenu_OpensEditorsAppliesResultsAndClosesToMenu()
    {
        var tool = new ClanIronWorldBuilderTool();
        var viewModel = new MainViewModel(
            ScreenFlowConfigLoader.LoadOrDefault(),
            new GameHostConfig(),
            new MockServerLauncher(),
            () => new MockChatLobbyRuntime(),
            () => new MockServerMatchRuntime(),
            [tool]);

        viewModel.MainMenu.Tools.Single().OpenCommand.Execute(null);
        var host = Assert.IsType<WorldBuilderHostView>(viewModel.CurrentViewModel);
        var session = Assert.IsType<ClanIronWorldBuilderSession>(host.DataContext);
        var menu = Assert.IsType<WorldBuilderMainMenuViewModel>(session.CurrentViewModel);

        menu.CreateNewCommand.Execute(null);
        var tileEditor = Assert.IsType<MapTileEditorViewModel>(session.CurrentViewModel);
        tileEditor.ApplyTile();
        Assert.NotNull(session.ActiveTileDefinition);

        tileEditor.BackCommand.Execute(null);
        menu = Assert.IsType<WorldBuilderMainMenuViewModel>(session.CurrentViewModel);
        menu.CreateSectionCommand.Execute(null);
        var sectionEditor = Assert.IsType<MapSectionEditorViewModel>(session.CurrentViewModel);
        sectionEditor.ApplyCommand.Execute(null);
        Assert.NotNull(session.ActiveSectionDefinition);

        sectionEditor.BackCommand.Execute(null);
        Assert.IsType<WorldBuilderMainMenuViewModel>(session.CurrentViewModel).BackCommand.Execute(null);
        Assert.Same(viewModel.MainMenu, viewModel.CurrentViewModel);
    }

    [Fact]
    public void ClientMenu_ProvidesFullyWorkingEditorNavigation()
    {
        var tool = new ClanIronWorldBuilderTool();
        var viewModel = new ClientMainViewModel(
            new GameClientConfig(),
            new MockClientLauncher(),
            new MockClientStateManager(),
            () => new MockClientChatLobbyRuntime(),
            () => new MockClientMatchRuntime(),
            startAtMainMenu: true,
            tools: [tool]);

        viewModel.MainMenu.Tools.Single().OpenCommand.Execute(null);
        var host = Assert.IsType<WorldBuilderHostView>(viewModel.CurrentViewModel);
        var session = Assert.IsType<ClanIronWorldBuilderSession>(host.DataContext);
        var menu = Assert.IsType<WorldBuilderMainMenuViewModel>(session.CurrentViewModel);
        menu.CreateSectionCommand.Execute(null);

        Assert.IsType<MapSectionEditorViewModel>(session.CurrentViewModel);
    }

    [Theory]
    [InlineData(WorldBuilderStartPage.TileEditor, typeof(MapTileEditorViewModel))]
    [InlineData(WorldBuilderStartPage.SectionEditor, typeof(MapSectionEditorViewModel))]
    public void StandaloneModes_OpenRequestedEditor(WorldBuilderStartPage startPage, Type expectedType)
    {
        var session = new ClanIronWorldBuilderSession(() => { }, startPage);

        Assert.IsType(expectedType, session.CurrentViewModel);
    }
}
