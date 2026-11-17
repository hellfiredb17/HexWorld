using ClanIronSim.CoreUx.WorldBuilder.ViewModels;
using ClanIronSim.World;
using HawkNet.CoreUx.Common.Infrastructure;

namespace ClanIronSim.CoreUx.WorldBuilder;

public sealed class ClanIronWorldBuilderSession : ObservableObject
{
    private readonly Action _close;
    private object _currentViewModel = null!;
    private MapTileDefinition? _activeTileDefinition;
    private MapSectionDefinition? _activeSectionDefinition;

    public ClanIronWorldBuilderSession(
        Action close,
        WorldBuilderStartPage startPage = WorldBuilderStartPage.MainMenu)
    {
        _close = close ?? throw new ArgumentNullException(nameof(close));
        CurrentViewModel = startPage switch
        {
            WorldBuilderStartPage.TileEditor => CreateTileEditor(),
            WorldBuilderStartPage.SectionEditor => CreateSectionEditor(),
            _ => CreateMainMenu()
        };
    }

    public object CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public MapTileDefinition? ActiveTileDefinition
    {
        get => _activeTileDefinition;
        private set => SetProperty(ref _activeTileDefinition, value);
    }

    public MapSectionDefinition? ActiveSectionDefinition
    {
        get => _activeSectionDefinition;
        private set => SetProperty(ref _activeSectionDefinition, value);
    }

    public void OpenTileEditor() => CurrentViewModel = CreateTileEditor();

    public void OpenSectionEditor() => CurrentViewModel = CreateSectionEditor();

    private WorldBuilderMainMenuViewModel CreateMainMenu()
    {
        var menu = new WorldBuilderMainMenuViewModel();
        menu.BackRequested += (_, _) => _close();
        menu.CreateNewRequested += (_, _) => OpenTileEditor();
        menu.ModifyExistingRequested += (_, _) => OpenTileEditor();
        menu.CreateSectionRequested += (_, _) => OpenSectionEditor();
        return menu;
    }

    private MapTileEditorViewModel CreateTileEditor()
    {
        var editor = new MapTileEditorViewModel();
        editor.BackRequested += (_, _) => CurrentViewModel = CreateMainMenu();
        editor.TileApplied += definition => ActiveTileDefinition = definition;
        return editor;
    }

    private MapSectionEditorViewModel CreateSectionEditor()
    {
        var editor = new MapSectionEditorViewModel();
        editor.BackRequested += (_, _) => CurrentViewModel = CreateMainMenu();
        editor.SectionApplied += definition => ActiveSectionDefinition = definition;
        return editor;
    }
}
