using System.Collections.ObjectModel;
using Avalonia.Controls;
using HawkNet.CoreUx.Common.Infrastructure;

namespace HawkNet.CoreUx.Common.LayerSelection;

internal sealed class LayerSelectionViewModel : ObservableObject
{
    internal static readonly IReadOnlyList<string> AttributeTypes = ["Movement", "Energy", "HitPoints"];

    private readonly LayerSelectionContext _context;
    private string? _selectedTexturePath;
    private string _statusMessage;
    private bool _isBusy;

    public LayerSelectionViewModel(LayerSelectionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _selectedTexturePath = NormalizeOptionalPath(context.InitialTexturePath);
        _statusMessage = "Layer details return when closed";

        foreach (LayerAttributeDefinition attribute in context.InitialAttributes)
        {
            Attributes.Add(new LayerAttributeItemViewModel(attribute.Type, attribute.Value));
        }
    }

    public int LayerNumber => _context.LayerNumber;

    public string HeaderText => $"Layer {LayerNumber}";

    public Control PreviewControl => _context.LayerPreviewAdapter.PreviewControl;

    public ObservableCollection<LayerAttributeItemViewModel> Attributes { get; } = [];

    public int AttributeCount => Attributes.Count;

    public string AttributeCountText => AttributeCount == 1 ? "1 attribute" : $"{AttributeCount} attributes";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? SelectedTexturePath => _selectedTexturePath;

    public string MaterialName => string.IsNullOrWhiteSpace(_selectedTexturePath)
        ? "None"
        : Path.GetFileName(_selectedTexturePath);

    public LayerSelectionDefinition CurrentLayer => new(
        LayerNumber,
        _selectedTexturePath,
        Attributes.Select(attribute => attribute.ToDefinition()).ToArray());

    public event EventHandler<LayerUpdatedEventArgs>? Updated;

    public event EventHandler? CloseRequested;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (await ApplyTextureAsync(_context.InitialTexturePath, cancellationToken))
        {
            StatusMessage = "Layer details return when closed";
        }
    }

    public void AddAttribute()
    {
        Attributes.Add(new LayerAttributeItemViewModel(AttributeTypes[0], string.Empty));
        NotifyAttributesChanged();
        StatusMessage = "Attribute added.";
    }

    public bool RemoveAttribute(LayerAttributeItemViewModel attribute)
    {
        bool removed = Attributes.Remove(attribute);
        if (removed)
        {
            NotifyAttributesChanged();
            StatusMessage = "Attribute removed.";
        }

        return removed;
    }

    public bool MoveAttribute(LayerAttributeItemViewModel attribute, int targetIndex)
    {
        int oldIndex = Attributes.IndexOf(attribute);
        if (oldIndex < 0 || Attributes.Count < 2)
        {
            return false;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Attributes.Count - 1);
        if (oldIndex == targetIndex)
        {
            return false;
        }

        Attributes.Move(oldIndex, targetIndex);
        StatusMessage = "Attribute order updated.";
        return true;
    }

    public async Task<bool> ApplyTextureAsync(string? texturePath, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return false;
        }

        string? normalized = NormalizeOptionalPath(texturePath);
        string? previous = _selectedTexturePath;
        IsBusy = true;
        try
        {
            await _context.LayerPreviewAdapter.ApplyTextureAsync(normalized, cancellationToken);
            _selectedTexturePath = normalized;
            OnPropertyChanged(nameof(SelectedTexturePath));
            OnPropertyChanged(nameof(MaterialName));
            StatusMessage = normalized is null ? "Material removed from layer." : $"Material set to {Path.GetFileName(normalized)}.";
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _selectedTexturePath = previous;
            StatusMessage = $"Layer preview update failed: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Commit(LayerUpdateReason reason)
    {
        Updated?.Invoke(this, new LayerUpdatedEventArgs(CurrentLayer, reason));
        StatusMessage = reason == LayerUpdateReason.Update ? "Layer updated." : "Layer changes applied.";
    }

    public void RequestClose()
    {
        Commit(LayerUpdateReason.Close);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ReportError(string message) => StatusMessage = message;

    internal static string NormalizeType(string? type) =>
        AttributeTypes.FirstOrDefault(candidate => string.Equals(candidate, type, StringComparison.OrdinalIgnoreCase))
        ?? AttributeTypes[0];

    private static string? NormalizeOptionalPath(string? path) => string.IsNullOrWhiteSpace(path)
        ? null
        : Path.GetFullPath(path);

    private void NotifyAttributesChanged()
    {
        OnPropertyChanged(nameof(AttributeCount));
        OnPropertyChanged(nameof(AttributeCountText));
        OnPropertyChanged(nameof(CurrentLayer));
    }
}
