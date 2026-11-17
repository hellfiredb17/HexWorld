using Avalonia.Controls;
using ClanIronSim.CoreUx.WorldBuilder.Views;
using ClanIronSim.World;
using HawkNet.CoreUx.Common.TextureSelection;
using HawkNet.CoreUx.Common.TileCreation;

namespace HawkNet.Desktop;

internal sealed class TextureSelectorDemoPreviewAdapter : ITexturePreviewAdapter, IDisposable
{
    private readonly MapTilePreviewControl _preview;

    public TextureSelectorDemoPreviewAdapter(double tileWidth = 3.8, double tileHeight = 3.4, TileModel model = TileModel.HexFlat)
    {
        _preview = new MapTilePreviewControl
        {
            Shape = model == TileModel.Square ? MapTileShape.Rectangle : MapTileShape.Hex,
            Orientation = model == TileModel.HexPoint ? HexOrientation.PointyTop : HexOrientation.FlatTop,
            TileWidth = model == TileModel.Square ? Math.Min(tileWidth, tileHeight) : model == TileModel.HexPoint ? tileHeight : tileWidth,
            TileHeight = model == TileModel.Square ? Math.Min(tileWidth, tileHeight) : model == TileModel.HexPoint ? tileWidth : tileHeight,
            IsInteractive = true
        };
    }

    public Control PreviewControl => _preview;

    public ValueTask ApplyTextureAsync(string? texturePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _preview.SetTexturePath(texturePath);
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _preview.SetTexturePath(null);
}
