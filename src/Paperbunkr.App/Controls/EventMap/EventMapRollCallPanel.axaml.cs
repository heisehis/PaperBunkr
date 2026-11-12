using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Controls.EventMap;

public partial class EventMapRollCallPanel : UserControl
{
    public EventMapRollCallPanel()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down && e.Source is not TextBox)
                e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
        }, RoutingStrategies.Tunnel);
    }
}
