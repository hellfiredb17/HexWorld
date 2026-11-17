using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClanIronSim.CoreUx.WorldBuilder.Views;

public sealed partial class WorldBuilderHostView : UserControl
{
    public WorldBuilderHostView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
