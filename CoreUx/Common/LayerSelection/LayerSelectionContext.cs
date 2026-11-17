using HawkNet.CoreUx.Common.TextureSelection;

namespace HawkNet.CoreUx.Common.LayerSelection;

/// <summary>
/// Describes a reusable layer-selection session.
/// </summary>
public sealed class LayerSelectionContext
{
    public LayerSelectionContext(
        int layerNumber,
        ITexturePreviewAdapter layerPreviewAdapter,
        Func<ITexturePreviewAdapter> textureSelectorPreviewAdapterFactory)
    {
        LayerNumber = layerNumber;
        LayerPreviewAdapter = layerPreviewAdapter ?? throw new ArgumentNullException(nameof(layerPreviewAdapter));
        TextureSelectorPreviewAdapterFactory = textureSelectorPreviewAdapterFactory
            ?? throw new ArgumentNullException(nameof(textureSelectorPreviewAdapterFactory));
    }

    public int LayerNumber { get; }

    /// <summary>The preview displayed in the layer-selection view.</summary>
    public ITexturePreviewAdapter LayerPreviewAdapter { get; }

    /// <summary>
    /// Creates a separate preview for the material selector. A factory is used because an Avalonia
    /// control can only have one visual parent.
    /// </summary>
    public Func<ITexturePreviewAdapter> TextureSelectorPreviewAdapterFactory { get; }

    public string? MaterialFilePath { get; init; }

    public string? InitialTextureDirectory { get; init; }

    public string? InitialTexturePath { get; init; }

    public string? TexturePreviewInteractionHint { get; init; }

    public IReadOnlyList<LayerAttributeDefinition> InitialAttributes { get; init; } = [];
}
