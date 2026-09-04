using Avalonia.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// Tier-2 Activity Center drawer (docs/superpowers/specs/2026-09-03-activity-center-design.md).
/// Hosted as an overlay sibling in <c>MainWindow</c>; DataContext is the
/// <c>ActivityCenterViewModel</c>. Slide-up is driven by the <c>DrawerRoot.open</c> pseudo-class
/// bound to <c>IsDrawerOpen</c>. Escape/close-on-outside are handled by <c>MainWindow</c>.
/// </summary>
public partial class ActivityDrawerView : UserControl
{
    public ActivityDrawerView()
    {
        InitializeComponent();
    }
}
