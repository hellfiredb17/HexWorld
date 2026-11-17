using Avalonia.Media.Imaging;
using HawkNet.CoreUx.Common.Infrastructure;

namespace HawkNet.CoreUx.Common.TextureSelection;

internal sealed class TextureMaterialItemViewModel : ObservableObject, IDisposable
{
    private bool _isSelected;

    public TextureMaterialItemViewModel(string displayName, string? filePath, Bitmap? thumbnail)
    {
        DisplayName = displayName;
        FilePath = filePath;
        Thumbnail = thumbnail;
    }

    public string DisplayName { get; }

    public string? FilePath { get; }

    public Bitmap? Thumbnail { get; }

    public bool IsNone => FilePath is null;

    public bool HasThumbnail => Thumbnail is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void Dispose() => Thumbnail?.Dispose();
}
