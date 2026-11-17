using Avalonia.Controls;

namespace HawkNet.CoreUx.Common.TileCreation;

public sealed partial class TileCreatorWindow : Window
{
    private TileCreationDocument? _closedDocument;
    private string? _closedFilePath;
    private bool _finishingClose;
    public TileCreatorWindow()
    {
        InitializeComponent();
        Creator.CloseRequested += (_, _) => Close();
        Creator.Changed += (_, args) => { Title = Creator.FilePath is null ? "Tile Creator" : Path.GetFileName(Creator.FilePath); Changed?.Invoke(this, args); };
        Creator.Saved += (_, args) => { Title = Path.GetFileName(args.FilePath); Saved?.Invoke(this, args); };
        Closed += (_, _) =>
        {
            _closedDocument = Creator.CurrentDocument;
            _closedFilePath = Creator.FilePath;
            Creator.Dispose();
        };
        Closing += OnClosing;
    }
    public TileCreatorWindow(TileCreatorContext context) : this() => Initialize(context);
    public TileCreationDocument? CurrentDocument => _closedDocument ?? Creator.CurrentDocument;
    public string? FilePath => _closedFilePath ?? Creator.FilePath;
    public Task Initialization => Creator.Initialization;
    public event EventHandler<TileChangedEventArgs>? Changed;
    public event EventHandler<TileSavedEventArgs>? Saved;
    public void Initialize(TileCreatorContext context) => Creator.Initialize(context);
    public Task<bool> LoadAsync(string path, CancellationToken token = default) => Creator.LoadAsync(path, token);
    public Task<bool> SaveAsAsync(string path, CancellationToken token = default) => Creator.SaveAsAsync(path, token);
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (Creator.HasEditor && !_finishingClose)
        {
            e.Cancel = true;
            await Creator.CloseEditorAsync();
            _finishingClose = true;
            Close();
            return;
        }
    }
}
