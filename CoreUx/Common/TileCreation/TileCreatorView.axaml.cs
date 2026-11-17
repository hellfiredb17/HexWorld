using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HawkNet.CoreUx.Common.LayerSelection;
using HawkNet.CoreUx.Common.Infrastructure;
using HawkNet.CoreUx.Common.TextureSelection;

namespace HawkNet.CoreUx.Common.TileCreation;

public sealed partial class TileCreatorView : UserControl, IDisposable
{
    private static readonly FilePickerFileType TileFileType = new("HawkNet tile") { Patterns = ["*.tile"], MimeTypes = ["application/json"] };
    private TileCreatorContext? _context;
    private TileCreatorViewModel? _viewModel;
    private LayerSelectionWindow? _editor;
    private Task _editorUpdates = Task.CompletedTask;
    private TileLayerItemViewModel? _dragged;
    private double[] _slotCenters = [];
    private Point _dragPoint;
    private readonly DispatcherTimer _dragTimer;
    private readonly CancellationTokenSource _lifetime = new();

    public TileCreatorView()
    {
        InitializeComponent();
        LayerScroll.AddHandler(PointerMovedEvent, OnDragMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        LayerScroll.AddHandler(PointerReleasedEvent, OnDragReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        LayerScroll.PointerCaptureLost += (_, _) => StopDrag();
        _dragTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _dragTimer.Tick += (_, _) => DragStep();
    }
    public TileCreatorView(TileCreatorContext context) : this() => Initialize(context);
    public Task Initialization { get; private set; } = Task.CompletedTask;
    public TileCreationDocument? CurrentDocument => _viewModel?.CurrentDocument;
    public string? FilePath => _viewModel?.FilePath;
    internal bool HasEditor => _editor is not null;
    public event EventHandler<TileChangedEventArgs>? Changed;
    public event EventHandler<TileSavedEventArgs>? Saved;
    public event EventHandler? CloseRequested;

    public void Initialize(TileCreatorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_editor is not null || !Initialization.IsCompleted) throw new InvalidOperationException("Wait for the current session before reinitializing.");
        StopDrag(); DetachViewModel(); _context = context;
        _viewModel = new TileCreatorViewModel(context);
        _viewModel.Changed += OnChanged; _viewModel.Saved += OnSaved;
        DataContext = _viewModel; Initialization = _viewModel.InitializeAsync(_lifetime.Token);
    }
    public async Task<bool> LoadAsync(string path, CancellationToken token = default)
    {
        await Initialization;
        return _editor is null && _viewModel is not null && await _viewModel.LoadAsync(path, token);
    }
    public async Task<bool> SaveAsAsync(string path, CancellationToken token = default)
    {
        await Initialization; await _editorUpdates;
        return _editor is null && _viewModel is not null && await _viewModel.SaveAsAsync(path, token);
    }
    private async void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_editor is not null || _viewModel is null || _viewModel.IsBusy || sender is not ComboBox { SelectedIndex: >= 0 } selector) return;
        StopDrag();
        await _viewModel.SelectModelAsync((TileModel)selector.SelectedIndex);
    }

    private async void OnAddAboveClicked(object? sender, RoutedEventArgs e)
    {
        await Initialization;
        if (_viewModel is not null && await _viewModel.AddAsync(true)) LayerScroll.Offset = default;
    }
    private async void OnAddBelowClicked(object? sender, RoutedEventArgs e)
    {
        await Initialization;
        if (_viewModel is not null && await _viewModel.AddAsync(false))
            Dispatcher.UIThread.Post(() => LayerScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }
    private void OnRemoveClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TileLayerItemViewModel row }) _viewModel?.Remove(row);
    }
    private async void OnPreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (_context is null || _viewModel is null || sender is not Button { DataContext: TileLayerItemViewModel row }) return;
        if (_editor is not null) { _editor.Activate(); return; }
        var adapters = new List<ITexturePreviewAdapter>();
        ITexturePreviewAdapter CreateEditorAdapter()
        {
            var adapter = _context.CreatePreview(_viewModel.Model) ?? throw new InvalidOperationException("Preview factory returned null.");
            if (adapter.PreviewControl.Parent is not null || _viewModel.Layers.Any(item => ReferenceEquals(item.PreviewControl, adapter.PreviewControl)) || adapters.Any(item => ReferenceEquals(item.PreviewControl, adapter.PreviewControl)))
                throw new InvalidOperationException("The factory must return a fresh preview for the editor.");
            adapters.Add(adapter); return adapter;
        }
        try
        {
            var editor = new LayerSelectionWindow(new LayerSelectionContext(row.LayerNumber, CreateEditorAdapter(), CreateEditorAdapter)
            {
                InitialTexturePath = row.TexturePath,
                InitialTextureDirectory = row.TexturePath is null ? _context.InitialTextureDirectory : Path.GetDirectoryName(row.TexturePath),
                InitialAttributes = row.Snapshot.Attributes,
                TexturePreviewInteractionHint = _context.TexturePreviewInteractionHint
            });
            _editor = editor;
            editor.Updated += (_, args) => _editorUpdates = ApplyEditorUpdateAsync(_editorUpdates, row, args.Layer);
            if (TopLevel.GetTopLevel(this) is Window owner) await editor.ShowDialog(owner);
            else
            {
                var closed = new TaskCompletionSource();
                editor.Closed += (_, _) => closed.TrySetResult(); editor.Show(); await closed.Task;
            }
            await _editorUpdates;
        }
        catch (Exception ex) { _viewModel?.ReportError($"Layer editor could not open: {ex.Message}"); }
        finally { _editor = null; foreach (var adapter in adapters.OfType<IDisposable>()) adapter.Dispose(); }
    }
    private async Task ApplyEditorUpdateAsync(Task previous, TileLayerItemViewModel row, LayerSelectionDefinition definition)
    {
        await previous;
        if (_viewModel is not null) await _viewModel.UpdateLayerAsync(row, definition);
    }
    private async void OnLoadClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            var startLocation = await GetPickerStartLocationAsync(storage);
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load tile",
                AllowMultiple = false,
                FileTypeFilter = [TileFileType],
                SuggestedStartLocation = startLocation
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await LoadAsync(path);
        }
        catch (Exception ex) { _viewModel?.ReportError($"Tile picker failed: {ex.Message}"); }
    }
    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            var startLocation = await GetPickerStartLocationAsync(storage);
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save tile as",
                SuggestedFileName = _viewModel?.FilePath is null ? "untitled.tile" : _viewModel.HeaderText,
                DefaultExtension = "tile",
                FileTypeChoices = [TileFileType],
                SuggestedStartLocation = startLocation
            });
            if (file?.TryGetLocalPath() is { } path) await SaveAsAsync(path);
        }
        catch (Exception ex) { _viewModel?.ReportError($"Tile picker failed: {ex.Message}"); }
    }
    private Task<IStorageFolder?> GetPickerStartLocationAsync(IStorageProvider storage) =>
        StoragePickerLocations.ResolveAsync(storage,
            _viewModel?.FilePath is { } path ? Path.GetDirectoryName(path) : null,
            _context?.InitialDirectory);

    public async Task CloseEditorAsync() { _editor?.Close(); await _editorUpdates; }
    private async void OnCloseClicked(object? sender, RoutedEventArgs e) { await CloseEditorAsync(); CloseRequested?.Invoke(this, EventArgs.Empty); }
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window window) window.BeginMoveDrag(e);
    }
    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel?.CanEdit != true || !e.GetCurrentPoint(LayerScroll).Properties.IsLeftButtonPressed || sender is not Border { DataContext: TileLayerItemViewModel row }) return;
        _dragged = row; _dragPoint = e.GetPosition(LayerScroll);
        // Freeze slot centers for this gesture: variable-height rows otherwise shift the target back
        // across the pointer after a collection move, causing repeated swaps.
        _slotCenters = LayerList.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("tile-row"))
            .Select(border => (border.TransformToVisual(LayerScroll)?.Transform(default).Y ?? 0) + LayerScroll.Offset.Y + border.Bounds.Height / 2)
            .OrderBy(center => center).ToArray();
        e.Pointer.Capture(LayerScroll); _dragTimer.Start(); e.Handled = true;
    }
    private void OnDragMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null) return;
        _dragPoint = e.GetPosition(LayerScroll); DragStep(); e.Handled = true;
    }
    private void DragStep()
    {
        if (_dragged is null || _slotCenters.Length == 0) return;
        double delta = _dragPoint.Y < 36 ? -14 : _dragPoint.Y > LayerScroll.Bounds.Height - 36 ? 14 : 0;
        if (delta != 0) LayerScroll.Offset = new Vector(0, Math.Clamp(LayerScroll.Offset.Y + delta, 0, Math.Max(0, LayerScroll.Extent.Height - LayerScroll.Viewport.Height)));
        double position = _dragPoint.Y + LayerScroll.Offset.Y;
        int target = Enumerable.Range(0, _slotCenters.Length).MinBy(index => Math.Abs(_slotCenters[index] - position));
        _viewModel?.Move(_dragged, target);
    }
    private void OnDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragged is null) return;
        StopDrag(); e.Pointer.Capture(null); e.Handled = true;
    }
    private void StopDrag() { _dragTimer?.Stop(); _dragged = null; _slotCenters = []; }
    private void OnChanged(object? sender, TileChangedEventArgs e) => Changed?.Invoke(this, e);
    private void OnSaved(object? sender, TileSavedEventArgs e) => Saved?.Invoke(this, e);
    private void DetachViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.Changed -= OnChanged; _viewModel.Saved -= OnSaved; _viewModel.Dispose(); _viewModel = null;
    }
    public void Dispose() { StopDrag(); _editor?.Close(); _lifetime.Cancel(); DetachViewModel(); }
}
