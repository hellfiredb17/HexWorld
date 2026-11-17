using System.Collections.ObjectModel;
using System.Text.Json;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.LayerSelection;

namespace HawkNet.CoreUx.Common.TileCreation;

internal sealed class TileCreatorViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly TileCreatorContext _context;
    private readonly CancellationTokenSource _shutdown = new();
    private string? _filePath;
    private string? _tileName;
    private TileModel _model = TileModel.HexFlat;
    private int _highest;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    public TileCreatorViewModel(TileCreatorContext context)
    {
        _context = context;
        _filePath = context.FilePath is null ? null : Path.GetFullPath(context.FilePath);
        _tileName = context.TileName;
    }
    public ObservableCollection<TileLayerItemViewModel> Layers { get; } = [];
    public string HeaderText => _filePath is not null ? Path.GetFileName(_filePath) : _tileName ?? "Tile Creator";
    public string? FilePath => _filePath;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanEdit)); } }
    public bool CanEdit => !IsBusy;
    public string StatusMessage { get => _statusMessage; private set { SetProperty(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => StatusMessage.Length > 0;
    public IReadOnlyList<string> ModelOptions { get; } = ["Square", "Hex Flat", "Hex Point"];
    public TileModel Model => _model;
    public int SelectedModelIndex => (int)_model;
    public TileCreationDocument CurrentDocument => new() { TileName = _tileName, Model = _model, Layers = Layers.Select(row => row.Snapshot).ToArray() };
    public event EventHandler<TileChangedEventArgs>? Changed;
    public event EventHandler<TileSavedEventArgs>? Saved;

    public async Task<bool> InitializeAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = linked.Token;
        IsBusy = true;
        try { return await ReplaceAsync(new TileCreationDocument { TileName = _tileName, Model = _context.InitialModel, Layers = _context.InitialLayers.Count == 0 ? [new(0, null, [])] : _context.InitialLayers }, null, token); }
        finally { IsBusy = false; }
    }

    private TileLayerItemViewModel CreateRow(LayerSelectionDefinition layer, TileModel model)
    {
        var adapter = _context.CreatePreview(model) ?? throw new InvalidOperationException("Preview factory returned null.");
        if (Layers.Any(row => ReferenceEquals(row.PreviewControl, adapter.PreviewControl)) || adapter.PreviewControl.Parent is not null)
            throw new InvalidOperationException("Preview factory must create a separate control for every layer and editor.");
        return new TileLayerItemViewModel(adapter, layer);
    }

    public async Task<bool> AddAsync(bool above, CancellationToken token = default)
    {
        if (IsBusy) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = linked.Token;
        IsBusy = true;
        TileLayerItemViewModel? row = null;
        try
        {
            row = CreateRow(new(above ? _highest + 1 : _highest - Layers.Count, null, []), _model);
            await row.Adapter.ApplyTextureAsync(null, token);
            token.ThrowIfCancellationRequested();
            if (above) { _highest++; Layers.Insert(0, row); } else Layers.Add(row);
            Renumber(); NotifyChanged(); return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { row?.Dispose(); ReportError($"Layer could not be added: {ex.Message}"); return false; }
        catch (OperationCanceledException) { row?.Dispose(); return false; }
        finally { IsBusy = false; }
    }
    public bool Remove(TileLayerItemViewModel row)
    {
        if (IsBusy || !Layers.Contains(row)) return false;
        if (Layers.Count == 1) { ReportError("A tile must always have a layer 0."); return false; }
        if (row.LayerNumber >= 0 && _highest > 0) _highest--;
        Layers.Remove(row); row.Dispose(); Renumber(); NotifyChanged(); return true;
    }
    public bool Move(TileLayerItemViewModel row, int target)
    {
        int source = Layers.IndexOf(row);
        if (IsBusy || source < 0) return false;
        target = Math.Clamp(target, 0, Layers.Count - 1);
        if (source == target) return false;
        Layers.Move(source, target); Renumber(); NotifyChanged(); return true;
    }
    private void Renumber() { for (int i = 0; i < Layers.Count; i++) Layers[i].LayerNumber = _highest - i; }
    public async Task<bool> SelectModelAsync(TileModel model, CancellationToken token = default)
    {
        if (IsBusy || model == _model) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        IsBusy = true;
        try { return await ReplaceAsync(CurrentDocument with { Model = model }, null, linked.Token); }
        finally
        {
            // Restore the dropdown as well if preview construction failed.
            OnPropertyChanged(nameof(SelectedModelIndex));
            IsBusy = false;
        }
    }
    public async Task<bool> UpdateLayerAsync(TileLayerItemViewModel row, LayerSelectionDefinition layer, CancellationToken token = default)
    {
        if (IsBusy || !Layers.Contains(row)) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = linked.Token;
        IsBusy = true;
        try { await row.Adapter.ApplyTextureAsync(layer.TexturePath, token); token.ThrowIfCancellationRequested(); row.ApplyDefinition(layer); NotifyChanged(); return true; }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is not OperationCanceledException) { ReportError($"Layer preview update failed: {ex.Message}"); return false; }
        finally { IsBusy = false; }
    }
    public async Task<bool> LoadAsync(string path, CancellationToken token = default)
    {
        if (IsBusy) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = linked.Token;
        IsBusy = true;
        try
        {
            path = Path.GetFullPath(path);
            var document = JsonSerializer.Deserialize<TileCreationDocument>(await File.ReadAllTextAsync(path, token), JsonOptions)
                ?? throw new InvalidDataException("The tile document is empty.");
            return await ReplaceAsync(document, path, token);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { ReportError($"Tile could not be loaded: {ex.Message}"); return false; }
        finally { IsBusy = false; }
    }
    private async Task<bool> ReplaceAsync(TileCreationDocument document, string? path, CancellationToken token)
    {
        var staged = new List<TileLayerItemViewModel>();
        try
        {
            Validate(document);
            foreach (var layer in document.Layers)
            {
                string? texture = string.IsNullOrWhiteSpace(layer.TexturePath) ? null : Path.GetFullPath(layer.TexturePath, path is null ? Environment.CurrentDirectory : Path.GetDirectoryName(path)!);
                if (texture is not null && !File.Exists(texture)) throw new FileNotFoundException("Texture file was not found.", texture);
                var row = CreateRow(layer with { TexturePath = texture }, document.Model);
                if (staged.Any(other => ReferenceEquals(other.PreviewControl, row.PreviewControl)))
                    throw new InvalidOperationException("Preview factory reused a control.");
                staged.Add(row);
                await row.Adapter.ApplyTextureAsync(texture, token);
            }
            token.ThrowIfCancellationRequested();
            foreach (var row in Layers) row.Dispose();
            Layers.Clear(); foreach (var row in staged) Layers.Add(row);
            staged.Clear(); _highest = Layers[0].LayerNumber; _tileName = document.TileName;
            _model = document.Model;
            OnPropertyChanged(nameof(Model));
            OnPropertyChanged(nameof(SelectedModelIndex));
            if (path is not null) _filePath = path;
            OnPropertyChanged(nameof(HeaderText)); NotifyChanged(); return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { ReportError($"Tile could not be loaded: {ex.Message}"); return false; }
        finally { foreach (var row in staged) row.Dispose(); }
    }
    internal static void Validate(TileCreationDocument document)
    {
        if (document.Version != TileCreationDocument.CurrentVersion) throw new InvalidDataException("Unsupported tile document version.");
        if (!Enum.IsDefined(document.Model)) throw new InvalidDataException("Unsupported tile model.");
        if (document.Layers is null || document.Layers.Count == 0 || !document.Layers.Any(layer => layer is not null && layer.LayerNumber == 0))
            throw new InvalidDataException("The tile must contain layer 0.");
        for (int i = 0; i < document.Layers.Count; i++)
        {
            var layer = document.Layers[i];
            if (layer is null || layer.Attributes is null || layer.Attributes.Any(attribute => attribute is null || attribute.Type is null || attribute.Value is null))
                throw new InvalidDataException("Invalid layer attributes.");
            if (i > 0 && (long)document.Layers[i - 1].LayerNumber - 1 != layer.LayerNumber)
                throw new InvalidDataException("Layer numbers must be contiguous and descending.");
        }
    }
    public async Task<bool> SaveAsAsync(string path, CancellationToken token = default)
    {
        if (IsBusy) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        token = linked.Token;
        IsBusy = true;
        string? temporary = null;
        try
        {
            path = Path.GetFullPath(path);
            var snapshot = CurrentDocument; Validate(snapshot);
            string directory = Path.GetDirectoryName(path)!;
            var persisted = snapshot with { Layers = snapshot.Layers.Select(layer => layer with { TexturePath = layer.TexturePath is null ? null : Path.GetRelativePath(directory, layer.TexturePath) }).ToArray() };
            temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(persisted, JsonOptions), token);
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite: true); temporary = null;
            _filePath = path; OnPropertyChanged(nameof(HeaderText)); StatusMessage = "Tile saved.";
            Saved?.Invoke(this, new(path, snapshot)); return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { ReportError($"Tile could not be saved: {ex.Message}"); return false; }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); IsBusy = false; }
    }
    public void ReportError(string message) => StatusMessage = message;
    private void NotifyChanged() { StatusMessage = string.Empty; OnPropertyChanged(nameof(CurrentDocument)); Changed?.Invoke(this, new(CurrentDocument)); }
    public void Dispose() { _shutdown.Cancel(); foreach (var row in Layers) row.Dispose(); Layers.Clear(); }
}
