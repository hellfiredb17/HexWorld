using System.Collections.ObjectModel;
using System.ComponentModel;
using ClanIronSim;
using ClanIronSim.World;
using HawkNet.CoreUx.Common.Infrastructure;

namespace ClanIronSim.CoreUx.WorldBuilder.ViewModels;

public sealed class MapSectionEditorViewModel : ObservableObject
{
    private string _name = "Forest Ruins Outpost";
    private MapTileShape _shape = MapTileShape.Rectangle;
    private HexOrientation _orientation = HexOrientation.PointyTop;
    private int _columns = 10;
    private int _rows = 8;
    private string _tool = "Paint";
    private TilePaletteItem _selectedBrush;
    private SectionCellViewModel? _selectedCell;
    private double _zoom = 1;
    private string _status = "Mouse wheel: Zoom  •  Middle drag: Pan  •  Click: Paint";

    public MapSectionEditorViewModel()
    {
        Tiles = new ObservableCollection<TilePaletteItem>
        {
            new("Forest Floor", "forest", "avares://ClanIronSim.CoreUx/Assets/forest-floor-tile.png"),
            new("Rough Ground", "forest", "avares://ClanIronSim.CoreUx/Assets/rough-ground-tile.png")
        };
        _selectedBrush = Tiles[0];
        SelectBrushCommand = new RelayCommand<TilePaletteItem>(item => SelectedBrush = item);
        SelectToolCommand = new RelayCommand<string>(value => Tool = value);
        ClosePopupCommand = new RelayCommand(() => SelectedCell = null);
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
        ApplyCommand = new RelayCommand(() => { SectionApplied?.Invoke(CreateDefinition()); Status = $"Applied “{Name}” to the current world-building session"; });
        ZoomInCommand = new RelayCommand(() => Zoom += .1);
        ZoomOutCommand = new RelayCommand(() => Zoom -= .1);
        FitCommand = new RelayCommand(() => FitRequested?.Invoke());
        SeedDemo();
    }

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public MapTileShape Shape { get => _shape; private set { if (SetProperty(ref _shape, value)) { OnPropertyChanged(nameof(IsSquare)); OnPropertyChanged(nameof(IsHex)); ResetGrid(); RequestFit(); } } }
    public HexOrientation Orientation { get => _orientation; private set { if (SetProperty(ref _orientation, value)) { OnPropertyChanged(nameof(IsPointy)); OnPropertyChanged(nameof(IsFlat)); ResetGrid(); RequestFit(); } } }
    public int Columns { get => _columns; set { if (SetProperty(ref _columns, Math.Clamp(value, 1, 64))) { ResetGrid(); RequestFit(); } } }
    public int Rows { get => _rows; set { if (SetProperty(ref _rows, Math.Clamp(value, 1, 64))) { ResetGrid(); RequestFit(); } } }
    public string Tool { get => _tool; set { if (SetProperty(ref _tool, value)) OnPropertyChanged(nameof(CanvasCursorHint)); } }
    public string CanvasCursorHint => Tool switch { "Pan" => "Drag to pan", "Erase" => "Click a tile to erase", "Select" => "Click a tile to edit instance", _ => "Click or drag to paint" };
    public TilePaletteItem SelectedBrush { get => _selectedBrush; set => SetProperty(ref _selectedBrush, value); }
    public SectionCellViewModel? SelectedCell { get => _selectedCell; set { if (SetProperty(ref _selectedCell, value)) OnPropertyChanged(nameof(HasSelectedCell)); } }
    public bool HasSelectedCell => SelectedCell is not null;
    public double Zoom { get => _zoom; set { if (SetProperty(ref _zoom, Math.Clamp(value, .5, 2.5))) OnPropertyChanged(nameof(ZoomLabel)); } }
    public string ZoomLabel => $"{Zoom:P0}";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsSquare { get => Shape == MapTileShape.Rectangle; set { if (value) Shape = MapTileShape.Rectangle; } }
    public bool IsHex { get => Shape == MapTileShape.Hex; set { if (value) Shape = MapTileShape.Hex; } }
    public bool IsPointy { get => Orientation == HexOrientation.PointyTop; set { if (value) Orientation = HexOrientation.PointyTop; } }
    public bool IsFlat { get => Orientation == HexOrientation.FlatTop; set { if (value) Orientation = HexOrientation.FlatTop; } }
    public ObservableCollection<TilePaletteItem> Tiles { get; }
    public ObservableCollection<SectionCellViewModel> Cells { get; } = new();
    public RelayCommand<TilePaletteItem> SelectBrushCommand { get; }
    public RelayCommand<string> SelectToolCommand { get; }
    public RelayCommand ClosePopupCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand FitCommand { get; }
    public event EventHandler? BackRequested;
    public event Action<MapSectionDefinition>? SectionApplied;
    public event Action? FitRequested;

    public void Interact(int x, int z)
    {
        SectionCellViewModel? cell = Cells.FirstOrDefault(candidate => candidate.X == x && candidate.Z == z);
        if (Tool == "Erase") { if (cell is not null) Cells.Remove(cell); SelectedCell = null; }
        else if (Tool == "Select") SelectedCell = cell;
        else if (Tool == "Paint")
        {
            if (cell is null) { cell = new SectionCellViewModel(x, z, SelectedBrush); Cells.Add(cell); }
            else cell.Tile = SelectedBrush;
            SelectedCell = null;
        }
        OnPropertyChanged(nameof(Cells));
    }

    public string ToJson() => SimJson.ToJson(CreateDefinition(), writeIndented: true);

    public void LoadJson(string json)
    {
        MapSectionDefinition definition = SimJson.FromJson<MapSectionDefinition>(json);
        _shape = definition.Shape; _orientation = definition.HexOrientation; _columns = Math.Max(1, definition.Width); _rows = Math.Max(1, definition.Depth); Name = definition.Name;
        Cells.Clear();
        foreach (MapSectionTileInstance tile in definition.Tiles)
        {
            TilePaletteItem palette = Tiles.FirstOrDefault(item => item.Name == tile.TileName) ?? Tiles[0];
            var cell = new SectionCellViewModel(tile.GridX, tile.GridZ, palette) { Movement = tile.Attributes.FirstOrDefault(a => a.ModifierKey == "movement")?.Amount ?? 0, IsBlocked = tile.Attributes.Any(a => a.ModifierKey == "blocking") };
            Cells.Add(cell);
        }
        OnPropertyChanged(string.Empty); Status = "Section JSON loaded";
    }

    public MapSectionDefinition CreateDefinition() => new()
    {
        Name = Name.Trim(), Shape = Shape, HexOrientation = Orientation, Width = Columns, Depth = Rows,
        Tiles = Cells.Select(cell => new MapSectionTileInstance { GridX = cell.X, GridZ = cell.Z, TileName = cell.Tile.Name, Tags = new List<string> { cell.Tile.Tag }, Attributes = CreateAttributes(cell) }).ToList()
    };

    private static List<MapAttribute> CreateAttributes(SectionCellViewModel cell)
    {
        var result = new List<MapAttribute>();
        if (cell.Movement != 0) result.Add(new MapAttribute { Id = Guid.NewGuid().ToString("N"), ModifierKey = "movement", Amount = cell.Movement });
        if (cell.IsBlocked) result.Add(new MapAttribute { Id = Guid.NewGuid().ToString("N"), ModifierKey = "blocking", Amount = 1 });
        return result;
    }

    private void ResetGrid() { Cells.Clear(); SelectedCell = null; OnPropertyChanged(nameof(Cells)); }
    private void RequestFit() => FitRequested?.Invoke();
    private void SeedDemo() { for (int z = 1; z < 7; z++) for (int x = 1; x < 9; x++) Cells.Add(new SectionCellViewModel(x, z, (x + z) % 5 == 0 ? Tiles[1] : Tiles[0])); }
}

public sealed record TilePaletteItem(string Name, string Tag, string AssetUri);

public sealed class SectionCellViewModel : ObservableObject
{
    private TilePaletteItem _tile;
    private int _movement;
    private bool _isBlocked;
    public SectionCellViewModel(int x, int z, TilePaletteItem tile) { X = x; Z = z; _tile = tile; }
    public int X { get; }
    public int Z { get; }
    public TilePaletteItem Tile { get => _tile; set => SetProperty(ref _tile, value); }
    public int Movement { get => _movement; set => SetProperty(ref _movement, value); }
    public bool IsBlocked { get => _isBlocked; set => SetProperty(ref _isBlocked, value); }
}
