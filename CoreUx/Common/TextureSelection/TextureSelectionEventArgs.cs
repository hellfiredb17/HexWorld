namespace HawkNet.CoreUx.Common.TextureSelection;

public sealed class TextureSelectionChangedEventArgs : EventArgs
{
    public TextureSelectionChangedEventArgs(string? texturePath) => TexturePath = texturePath;

    public string? TexturePath { get; }
}

public sealed class TextureSelectionSavedEventArgs : EventArgs
{
    public TextureSelectionSavedEventArgs(string filePath, TextureSelectionDocument document)
    {
        FilePath = filePath;
        Document = document;
    }

    public string FilePath { get; }

    public TextureSelectionDocument Document { get; }
}
