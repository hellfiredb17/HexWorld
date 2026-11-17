using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HawkNet.CoreUx.Common.TextureSelection;

namespace HawkNet.CoreUx.Common.LayerSelection;

public sealed partial class LayerSelectionView : UserControl
{
    private LayerSelectionContext? _context;
    private LayerSelectionViewModel? _viewModel;
    private LayerAttributeItemViewModel? _draggedAttribute;
    private Control? _dragHandle;
    private TextureSelectorWindow? _textureSelector;

    public LayerSelectionView()
    {
        InitializeComponent();
        AttributeList.AddHandler(PointerMovedEvent, OnDragHandleMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AttributeList.AddHandler(PointerReleasedEvent, OnDragHandleReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public LayerSelectionView(LayerSelectionContext context) : this() => Initialize(context);

    public LayerSelectionDefinition? CurrentLayer => _viewModel?.CurrentLayer;

    public event EventHandler<LayerUpdatedEventArgs>? Updated;

    public event EventHandler? CloseRequested;

    public void Initialize(LayerSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DetachViewModel();
        _context = context;
        _viewModel = new LayerSelectionViewModel(context);
        _viewModel.Updated += OnUpdated;
        _viewModel.CloseRequested += OnCloseRequested;
        DataContext = _viewModel;
        _ = _viewModel.InitializeAsync();
    }

    private async void OnPreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (_context is null || _viewModel is null)
        {
            return;
        }

        if (_textureSelector is not null)
        {
            _textureSelector.Activate();
            return;
        }

        ITexturePreviewAdapter selectorAdapter;
        try
        {
            selectorAdapter = _context.TextureSelectorPreviewAdapterFactory()
                ?? throw new InvalidOperationException("The texture selector preview factory returned null.");
        }
        catch (Exception exception)
        {
            _viewModel.ReportError($"Material selector could not open: {exception.Message}");
            return;
        }

        var selectorContext = new TextureSelectorContext(selectorAdapter)
        {
            InitialDirectory = _context.InitialTextureDirectory,
            InitialTexturePath = _viewModel.SelectedTexturePath,
            PreviewInteractionHint = _context.TexturePreviewInteractionHint
        };
        var selector = new TextureSelectorWindow(selectorContext);
        _textureSelector = selector;
        selector.SelectionChanged += OnTextureSelectionChanged;
        selector.Closed += OnTextureSelectorClosed;

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            await selector.ShowDialog(owner);
        }
        else
        {
            selector.Show();
        }
    }

    private async void OnTextureSelectionChanged(object? sender, TextureSelectionChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.ApplyTextureAsync(e.TexturePath);
        }
    }

    private void OnTextureSelectorClosed(object? sender, EventArgs e)
    {
        if (sender is TextureSelectorWindow selector)
        {
            selector.SelectionChanged -= OnTextureSelectionChanged;
            selector.Closed -= OnTextureSelectorClosed;
        }

        _textureSelector = null;
    }

    private void OnAddAttributeClicked(object? sender, RoutedEventArgs e) => _viewModel?.AddAttribute();

    private void OnRemoveAttributeClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && sender is Button { DataContext: LayerAttributeItemViewModel attribute })
        {
            _viewModel.RemoveAttribute(attribute);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => _viewModel?.RequestClose();

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(AttributeList).Properties.IsLeftButtonPressed || sender is not Border handle)
        {
            return;
        }

        Border? card = handle.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("attribute-card"));
        if (handle is null || card?.DataContext is not LayerAttributeItemViewModel attribute)
        {
            return;
        }

        _draggedAttribute = attribute;
        _dragHandle = AttributeList;
        e.Pointer.Capture(AttributeList);
        e.Handled = true;
    }

    private void OnDragHandleMoved(object? sender, PointerEventArgs e)
    {
        if (_viewModel is null || _draggedAttribute is null || _dragHandle is null || !e.GetCurrentPoint(_dragHandle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Avalonia.Point point = e.GetPosition(AttributeList);
        double[] slotCenters = AttributeList.GetVisualDescendants()
            .OfType<Border>()
            .Where(card => card.Classes.Contains("attribute-card"))
            .Select(card => new
            {
                Card = card,
                Origin = card.TransformToVisual(AttributeList)?.Transform(default)
            })
            .Where(item => item.Origin is not null)
            .OrderBy(item => item.Origin!.Value.Y)
            .Select(item => item.Origin!.Value.Y + (item.Card.Bounds.Height / 2d))
            .ToArray();

        if (slotCenters.Length > 0)
        {
            int targetIndex = Enumerable.Range(0, slotCenters.Length)
                .MinBy(index => Math.Abs(slotCenters[index] - point.Y));
            _viewModel.MoveAttribute(_draggedAttribute, targetIndex);
        }
    }

    private void OnDragHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedAttribute is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        _draggedAttribute = null;
        _dragHandle = null;
        e.Handled = true;
    }

    private void OnUpdated(object? sender, LayerUpdatedEventArgs e) => Updated?.Invoke(this, e);

    private void OnCloseRequested(object? sender, EventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void DetachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.Updated -= OnUpdated;
            _viewModel.CloseRequested -= OnCloseRequested;
        }
    }
}
