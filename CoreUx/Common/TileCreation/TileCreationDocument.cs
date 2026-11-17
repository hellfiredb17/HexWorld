using HawkNet.CoreUx.Common.LayerSelection;

namespace HawkNet.CoreUx.Common.TileCreation;

public sealed record TileCreationDocument
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public string? TileName { get; init; }
    // Older documents used flat-top hexagons and omitted this property.
    public TileModel Model { get; init; } = TileModel.HexFlat;
    /// <summary>Descending layer order. Texture paths in saved files are relative to the tile file.</summary>
    public IReadOnlyList<LayerSelectionDefinition> Layers { get; init; } = [];
}

public sealed class TileChangedEventArgs(TileCreationDocument document) : EventArgs
{
    public TileCreationDocument Document { get; } = document;
}

public sealed class TileSavedEventArgs(string filePath, TileCreationDocument document) : EventArgs
{
    public string FilePath { get; } = filePath;
    public TileCreationDocument Document { get; } = document;
}
