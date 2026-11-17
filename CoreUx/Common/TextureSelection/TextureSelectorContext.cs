namespace HawkNet.CoreUx.Common.TextureSelection;

/// <summary>
/// Describes one texture-selector session.
/// </summary>
public sealed class TextureSelectorContext
{
    public TextureSelectorContext(ITexturePreviewAdapter previewAdapter)
    {
        PreviewAdapter = previewAdapter ?? throw new ArgumentNullException(nameof(previewAdapter));
    }

    public ITexturePreviewAdapter PreviewAdapter { get; }

    /// <summary>
    /// Optional current save target. Existing files are loaded when the selector initializes.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Optional starting directory used when no existing material document is loaded.
    /// </summary>
    public string? InitialDirectory { get; init; }

    /// <summary>
    /// Optional absolute or directory-relative texture used for a new document.
    /// </summary>
    public string? InitialTexturePath { get; init; }

    public string? PreviewInteractionHint { get; init; }
}
