using Avalonia.Controls;

namespace Paperbunkr.App.Views;

public partial class MapHeaderStrip : UserControl
{
    public MapHeaderStrip()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Classes.Set("compact", e.NewSize.Width < 1000);
    }
}
