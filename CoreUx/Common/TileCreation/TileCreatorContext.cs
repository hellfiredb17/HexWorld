using HawkNet.CoreUx.Common.LayerSelection;
using HawkNet.CoreUx.Common.TextureSelection;

namespace HawkNet.CoreUx.Common.TileCreation;

/// <summary>Factories must return a fresh adapter/control for each invocation.
/// Adapters created by this component are disposed when they implement IDisposable.</summary>
public sealed class TileCreatorContext
{
    public TileCreatorContext(Func<ITexturePreviewAdapter> previewAdapterFactory)
    {
        PreviewAdapterFactory = previewAdapterFactory ?? throw new ArgumentNullException(nameof(previewAdapterFactory));
    }

    public Func<ITexturePreviewAdapter> PreviewAdapterFactory { get; }
    /// <summary>Creates a preview for the selected model, including nested editors.</summary>
    public TileCreatorContext(Func<TileModel, ITexturePreviewAdapter> previewAdapterFactory)
    {
        ModelPreviewAdapterFactory = previewAdapterFactory ?? throw new ArgumentNullException(nameof(previewAdapterFactory));
        PreviewAdapterFactory = () => previewAdapterFactory(InitialModel);
    }

    public Func<TileModel, ITexturePreviewAdapter>? ModelPreviewAdapterFactory { get; }
    public TileModel InitialModel { get; init; } = TileModel.HexFlat;

    internal ITexturePreviewAdapter CreatePreview(TileModel model)
    {
        if (ModelPreviewAdapterFactory is { } factory) return factory(model);
        if (model != InitialModel)
            throw new NotSupportedException("This host must supply a model-aware preview factory to change the tile model.");
        return PreviewAdapterFactory();
    }
    public string? FilePath { get; init; }
    public string? TileName { get; init; }
    /// <summary>Starting folder for Load / Save As before a tile file has been chosen.</summary>
    public string? InitialDirectory { get; init; }
    public string? InitialTextureDirectory { get; init; }
    public string? TexturePreviewInteractionHint { get; init; }
    /// <summary>Descending, contiguous numbers including zero. Empty starts with layer zero.</summary>
    public IReadOnlyList<LayerSelectionDefinition> InitialLayers { get; init; } = [];
}
