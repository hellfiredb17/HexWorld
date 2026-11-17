namespace HawkNet.CoreUx.Common.TextureSelection;

/// <summary>
/// Portable, versioned state persisted by the texture selector.
/// </summary>
public sealed record TextureSelectionDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public string? TextureDirectory { get; init; }

    /// <summary>
    /// Texture path relative to <see cref="TextureDirectory"/> when possible; null represents None.
    /// </summary>
    public string? SelectedTexture { get; init; }
}
