namespace HawkNet.CoreUx.Common.LayerSelection;

/// <summary>
/// Immutable state emitted when a layer is updated or closed.
/// </summary>
public sealed record LayerSelectionDefinition(
    int LayerNumber,
    string? TexturePath,
    IReadOnlyList<LayerAttributeDefinition> Attributes);
