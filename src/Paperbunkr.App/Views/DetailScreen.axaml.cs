using Avalonia.Controls;

namespace Paperbunkr.App.Views;

public partial class DetailScreen : UserControl
{
    public DetailScreen()
    {
        InitializeComponent();
        DetailCosmetics.Attach(this);
        KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
    }
}
