using Avalonia.Controls;

namespace HawkNet.CoreUx.Common.LayerSelection;

public sealed partial class LayerSelectionWindow : Window
{
    private LayerSelectionView? _selector;
    private bool _closeCommitted;

    public LayerSelectionWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public LayerSelectionWindow(LayerSelectionContext context) : this() => Initialize(context);

    public LayerSelectionDefinition? CurrentLayer => _selector?.CurrentLayer;

    public event EventHandler<LayerUpdatedEventArgs>? Updated;

    public void Initialize(LayerSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DetachSelector();
        _selector = this.FindControl<LayerSelectionView>("Selector")
            ?? throw new InvalidOperationException("Layer selector view was not created.");
        _selector.Initialize(context);
        _selector.Updated += OnUpdated;
        _selector.CloseRequested += OnCloseRequested;
    }

    private void OnUpdated(object? sender, LayerUpdatedEventArgs e)
    {
        if (e.Reason == LayerUpdateReason.Close)
        {
            _closeCommitted = true;
        }

        Updated?.Invoke(this, e);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_closeCommitted && _selector?.CurrentLayer is { } layer)
        {
            _closeCommitted = true;
            Updated?.Invoke(this, new LayerUpdatedEventArgs(layer, LayerUpdateReason.Close));
        }
    }

    private void OnClosed(object? sender, EventArgs e) => DetachSelector();

    private void DetachSelector()
    {
        if (_selector is not null)
        {
            _selector.Updated -= OnUpdated;
            _selector.CloseRequested -= OnCloseRequested;
        }
    }
}
