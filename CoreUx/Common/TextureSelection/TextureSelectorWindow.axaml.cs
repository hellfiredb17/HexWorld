using Avalonia.Controls;

namespace HawkNet.CoreUx.Common.TextureSelection;

public sealed partial class TextureSelectorWindow : Window
{
    private TextureSelectorView? _selector;
    private string? _closedTexturePath;
    private TextureSelectionDocument? _closedDocument;

    public TextureSelectorWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    public TextureSelectorWindow(TextureSelectorContext context) : this()
    {
        Initialize(context);
    }

    public Task Initialization => _selector?.Initialization ?? Task.CompletedTask;

    public string? SelectedTexturePath => _selector is null ? _closedTexturePath : _selector.SelectedTexturePath;

    public TextureSelectionDocument? CurrentDocument => _selector?.CurrentDocument ?? _closedDocument;

    public event EventHandler<TextureSelectionChangedEventArgs>? SelectionChanged;

    public event EventHandler<TextureSelectionSavedEventArgs>? SelectionSaved;

    public void Initialize(TextureSelectorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_selector is not null)
        {
            _selector.CloseRequested -= OnCloseRequested;
            _selector.SelectionChanged -= OnSelectionChanged;
            _selector.SelectionSaved -= OnSelectionSaved;
        }

        _selector = this.FindControl<TextureSelectorView>("Selector")
            ?? throw new InvalidOperationException("Texture selector view was not created.");
        _selector.Initialize(context);
        _selector.CloseRequested += OnCloseRequested;
        _selector.SelectionChanged += OnSelectionChanged;
        _selector.SelectionSaved += OnSelectionSaved;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnSelectionChanged(object? sender, TextureSelectionChangedEventArgs e) => SelectionChanged?.Invoke(this, e);

    private void OnSelectionSaved(object? sender, TextureSelectionSavedEventArgs e) => SelectionSaved?.Invoke(this, e);

    private void OnClosed(object? sender, EventArgs e)
    {
        _closedTexturePath = _selector?.SelectedTexturePath;
        _closedDocument = _selector?.CurrentDocument;
        _selector?.Dispose();
        _selector = null;
    }
}
