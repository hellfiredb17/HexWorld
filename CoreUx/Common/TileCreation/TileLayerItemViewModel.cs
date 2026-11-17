using System.Collections.ObjectModel;
using Avalonia.Controls;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.LayerSelection;
using HawkNet.CoreUx.Common.TextureSelection;

namespace HawkNet.CoreUx.Common.TileCreation;

internal sealed class TileLayerItemViewModel(ITexturePreviewAdapter adapter, LayerSelectionDefinition layer) : ObservableObject, IDisposable
{
    private int _number = layer.LayerNumber;
    private string? _texturePath = layer.TexturePath;
    public ITexturePreviewAdapter Adapter { get; } = adapter;
    public Control PreviewControl => Adapter.PreviewControl;
    public int LayerNumber
    {
        get => _number;
        set { if (SetProperty(ref _number, value)) { OnPropertyChanged(nameof(HeaderText)); OnPropertyChanged(nameof(IsZero)); } }
    }
    public string HeaderText => $"Layer {LayerNumber}";
    public bool IsZero => LayerNumber == 0;
    public string? TexturePath => _texturePath;
    public string MaterialName => _texturePath is null ? "None" : Path.GetFileName(_texturePath);
    public ObservableCollection<LayerAttributeDefinition> Attributes { get; } = new(layer.Attributes);
    public bool HasNoAttributes => Attributes.Count == 0;
    public LayerSelectionDefinition Snapshot => new(LayerNumber, TexturePath, Attributes.ToArray());
    public void ApplyDefinition(LayerSelectionDefinition definition)
    {
        _texturePath = definition.TexturePath;
        Attributes.Clear();
        foreach (var attribute in definition.Attributes) Attributes.Add(attribute);
        OnPropertyChanged(nameof(MaterialName));
        OnPropertyChanged(nameof(HasNoAttributes));
    }
    public void Dispose() { if (Adapter is IDisposable disposable) disposable.Dispose(); }
}
