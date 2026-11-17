using System.Collections.ObjectModel;
using ClanIronSim;
using ClanIronSim.SimMath;
using ClanIronSim.World;
using HawkNet.CoreUx.Common.Infrastructure;

namespace ClanIronSim.CoreUx.WorldBuilder.ViewModels;

public sealed class MapTileEditorViewModel : ObservableObject
{
    private string _name = "Forest Rough Ground";
    private MapTileShape _shape = MapTileShape.Rectangle;
    private HexOrientation _orientation = HexOrientation.PointyTop;
    private int _sizeX = 1;
    private int _sizeY = 1;
    private string _tagInput = string.Empty;
    private string _statusMessage = "Ready";

    public MapTileEditorViewModel()
    {
        Attributes.Add(new MapTileAttributeViewModel { Type = "Movement", Amount = 1 });
        Attributes.Add(new MapTileAttributeViewModel { Type = "Blocking", Amount = 1 });
        Tags.Add("forest");
        Tags.Add("rough");
        Tags.Add("ruins");
        AddAttributeCommand = new RelayCommand(() => Attributes.Add(new MapTileAttributeViewModel()));
        AddTagCommand = new RelayCommand(AddTag);
        RemoveAttributeCommand = new RelayCommand<MapTileAttributeViewModel>(attribute => Attributes.Remove(attribute));
        RemoveTagCommand = new RelayCommand<string>(tag => Tags.Remove(tag));
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
    }

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public MapTileShape Shape { get => _shape; private set { if (SetProperty(ref _shape, value)) { OnPropertyChanged(nameof(IsSquare)); OnPropertyChanged(nameof(IsHex)); } } }
    public HexOrientation Orientation { get => _orientation; private set { if (SetProperty(ref _orientation, value)) { OnPropertyChanged(nameof(IsPointy)); OnPropertyChanged(nameof(IsFlat)); } } }
    public int SizeX { get => _sizeX; set => SetProperty(ref _sizeX, Math.Max(1, value)); }
    public int SizeY { get => _sizeY; set => SetProperty(ref _sizeY, Math.Max(1, value)); }
    public string TagInput { get => _tagInput; set => SetProperty(ref _tagInput, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public bool IsSquare { get => Shape == MapTileShape.Rectangle; set { if (value) Shape = MapTileShape.Rectangle; } }
    public bool IsHex { get => Shape == MapTileShape.Hex; set { if (value) Shape = MapTileShape.Hex; } }
    public bool IsPointy { get => Orientation == HexOrientation.PointyTop; set { if (value) Orientation = HexOrientation.PointyTop; } }
    public bool IsFlat { get => Orientation == HexOrientation.FlatTop; set { if (value) Orientation = HexOrientation.FlatTop; } }

    public IReadOnlyList<string> AttributeTypes { get; } = new[] { "Movement", "Blocking" };
    public ObservableCollection<MapTileAttributeViewModel> Attributes { get; } = new();
    public ObservableCollection<string> Tags { get; } = new();
    public RelayCommand AddAttributeCommand { get; }
    public RelayCommand AddTagCommand { get; }
    public RelayCommand<MapTileAttributeViewModel> RemoveAttributeCommand { get; }
    public RelayCommand<string> RemoveTagCommand { get; }
    public RelayCommand BackCommand { get; }
    public event EventHandler? BackRequested;
    public event Action<MapTileDefinition>? TileApplied;

    public void ApplyTile()
    {
        MapTileDefinition definition = CreateDefinition();
        TileApplied?.Invoke(definition);
        StatusMessage = $"Applied “{definition.Name}” to the current world-building session";
    }

    public MapTileDefinition CreateDefinition()
    {
        MapTile tile = Shape == MapTileShape.Hex
            ? new MapHexTile { Orientation = Orientation }
            : new MapRectTile();
        tile.Size = new SimVector2((float)SizeX, (float)SizeY);
        tile.Attributes = Attributes.Select(attribute => new MapAttribute
        {
            Id = attribute.Id,
            ModifierKey = attribute.IsBlocking ? "blocking" : "movement",
            Amount = attribute.IsBlocking ? 1 : attribute.Amount
        }).ToList();
        return new MapTileDefinition { Name = Name.Trim(), Tags = Tags.ToList(), Tile = tile };
    }

    public string ToJson() => SimJson.ToJson(CreateDefinition(), writeIndented: true);

    public void LoadJson(string json)
    {
        MapTileDefinition definition;
        try { definition = SimJson.FromJson<MapTileDefinition>(json); }
        catch { definition = new MapTileDefinition { Tile = SimJson.FromJson<MapTile>(json) }; }

        Name = string.IsNullOrWhiteSpace(definition.Name) ? "Imported Tile" : definition.Name;
        Shape = definition.Tile.Shape;
        if (definition.Tile is MapHexTile hex) Orientation = hex.Orientation;
        SizeX = Math.Max(1, (int)MathF.Round(definition.Tile.SizeX));
        SizeY = Math.Max(1, (int)MathF.Round(definition.Tile.SizeY));
        Tags.Clear();
        foreach (string tag in definition.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag))) Tags.Add(tag);
        Attributes.Clear();
        foreach (MapAttribute attribute in definition.Tile.Attributes)
        {
            Attributes.Add(new MapTileAttributeViewModel
            {
                Id = string.IsNullOrWhiteSpace(attribute.Id) ? Guid.NewGuid().ToString("N") : attribute.Id,
                Type = string.Equals(attribute.ModifierKey, "blocking", StringComparison.OrdinalIgnoreCase) ? "Blocking" : "Movement",
                Amount = attribute.Amount
            });
        }
        StatusMessage = "JSON loaded";
    }

    private void AddTag()
    {
        string tag = TagInput.Trim().ToLowerInvariant();
        if (tag.Length == 0 || Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return;
        Tags.Add(tag);
        TagInput = string.Empty;
    }
}
