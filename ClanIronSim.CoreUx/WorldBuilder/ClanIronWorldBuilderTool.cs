using Avalonia.Controls;
using ClanIronSim.CoreUx.WorldBuilder.Views;
using HawkNet.CoreUx.Common.Services;

namespace ClanIronSim.CoreUx.WorldBuilder;

public enum WorldBuilderStartPage
{
    MainMenu,
    TileEditor,
    SectionEditor
}

public sealed class ClanIronWorldBuilderTool : ICoreUxTool
{
    private readonly WorldBuilderStartPage _startPage;

    public ClanIronWorldBuilderTool(WorldBuilderStartPage startPage = WorldBuilderStartPage.MainMenu)
    {
        _startPage = startPage;
    }

    public string Id => "claniron.world-builder";

    public string DisplayName => "World Builder";

    public int Order => 100;

    public Control CreateView(Action close)
    {
        ArgumentNullException.ThrowIfNull(close);
        return new WorldBuilderHostView
        {
            DataContext = new ClanIronWorldBuilderSession(close, _startPage)
        };
    }
}
