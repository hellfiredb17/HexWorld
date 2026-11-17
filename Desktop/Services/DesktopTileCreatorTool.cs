using Avalonia.Controls;
using HawkNet.CoreUx.Common.Services;
using HawkNet.CoreUx.Common.TileCreation;

namespace HawkNet.Desktop.Services;

internal sealed class DesktopTileCreatorTool(Func<TileCreatorContext> createContext) : ICoreUxTool
{
    public string Id => "hawknet.create-tile";
    public string DisplayName => "Create Tile";
    public int Order => 110;

    public Control CreateView(Action close)
    {
        ArgumentNullException.ThrowIfNull(close);
        var view = new TileCreatorView(createContext());
        view.CloseRequested += (_, _) => close();
        // Embedded tools are removed when navigating back or closing the host window.
        bool disposed = false;
        view.DetachedFromVisualTree += (_, _) =>
        {
            if (disposed) return;
            disposed = true;
            view.Dispose();
        };
        return view;
    }
}
