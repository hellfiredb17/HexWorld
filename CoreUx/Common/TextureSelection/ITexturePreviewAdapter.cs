using Avalonia.Controls;

namespace HawkNet.CoreUx.Common.TextureSelection;

/// <summary>
/// Bridges the common texture selector to a caller-owned model renderer.
/// </summary>
public interface ITexturePreviewAdapter
{
    /// <summary>
    /// Gets the unparented control that renders the caller's model.
    /// </summary>
    Control PreviewControl { get; }

    /// <summary>
    /// Applies a texture to the preview model. A null path removes the material.
    /// </summary>
    ValueTask ApplyTextureAsync(string? texturePath, CancellationToken cancellationToken = default);
}
