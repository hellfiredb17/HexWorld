using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HawkNet.CoreUx.Common.Infrastructure;

namespace HawkNet.CoreUx.Common.TextureSelection;

public sealed partial class TextureSelectorView : UserControl, IDisposable
{
    private TextureSelectorViewModel? _viewModel;

    public TextureSelectorView()
    {
        InitializeComponent();
    }

    public TextureSelectorView(TextureSelectorContext context) : this()
    {
        Initialize(context);
    }

    public Task Initialization { get; private set; } = Task.CompletedTask;

    public string? SelectedTexturePath => _viewModel?.SelectedTexturePath;

    public TextureSelectionDocument? CurrentDocument => _viewModel?.CurrentDocument;

    public event EventHandler? CloseRequested;

    public event EventHandler<TextureSelectionChangedEventArgs>? SelectionChanged;

    public event EventHandler<TextureSelectionSavedEventArgs>? SelectionSaved;

    public void Initialize(TextureSelectorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DetachViewModel();
        _viewModel = new TextureSelectorViewModel(context);
        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.SelectionChanged += OnSelectionChanged;
        _viewModel.SelectionSaved += OnSelectionSaved;
        DataContext = _viewModel;
        Initialization = _viewModel.InitializeAsync();
    }

    public Task<bool> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _viewModel?.LoadAsync(filePath, cancellationToken) ?? Task.FromResult(false);
    }

    public Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        return _viewModel?.SaveAsync(cancellationToken) ?? Task.FromResult(false);
    }

    public Task<bool> SaveAsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _viewModel?.SaveAsAsync(filePath, cancellationToken) ?? Task.FromResult(false);
    }

    private async void OnChooseDirectoryClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose texture directory",
            AllowMultiple = false,
            SuggestedStartLocation = await StoragePickerLocations.ResolveAsync(storage, _viewModel.CurrentDocument.TextureDirectory)
        });
        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            await _viewModel.ChooseDirectoryAsync(path);
        }
    }

    private async void OnMaterialClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && sender is Button { DataContext: TextureMaterialItemViewModel material })
        {
            await _viewModel.SelectMaterialAsync(material);
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnSelectionChanged(object? sender, TextureSelectionChangedEventArgs e) => SelectionChanged?.Invoke(this, e);

    private void OnSelectionSaved(object? sender, TextureSelectionSavedEventArgs e) => SelectionSaved?.Invoke(this, e);

    private void DetachViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.SelectionChanged -= OnSelectionChanged;
        _viewModel.SelectionSaved -= OnSelectionSaved;
        _viewModel.Dispose();
        _viewModel = null;
    }

    public void Dispose() => DetachViewModel();
}

