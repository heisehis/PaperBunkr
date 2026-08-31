using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Views;

public partial class SmartScreen : UserControl
{
    public SmartScreen()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Spatial arrow-key navigation across the virtualized results grid (docs/superpowers/specs/
    /// 2026-08-31-keyboard-operability-design.md) - deliberately NOT
    /// <c>GridKeyboardNavigation.TryHandleArrowKey</c> (that helper assumes every item has a realized
    /// container via <c>ContainerFromIndex</c>, which is false for a virtualizing panel - it would
    /// silently limit navigation to whatever's already on screen). <c>VirtualizingWrapPanel</c>
    /// already implements the correct, virtualization-safe navigation itself via Avalonia's own
    /// <c>INavigableContainer.GetControl(NavigationDirection)</c> extension point - confirmed via
    /// reflection against the installed <c>Avalonia.Controls.dll</c>: <c>VirtualizingPanel</c>
    /// explicitly implements <c>INavigableContainer.GetControl</c>, forwarding to the protected
    /// virtual <c>GetControl</c> that <c>VirtualizingWrapPanel</c> overrides with real index math
    /// (<c>fromIndex ± itemsPerRow</c> for Up/Down) and its own <c>ScrollIntoView</c> to realize the
    /// target. This handler's only job is to actually invoke that - a bare <c>ItemsControl</c>
    /// (unlike <c>ListBox</c>) never calls it on its own for arrow keys.
    /// </summary>
    private void OnResultCardKeyDown(object? sender, KeyEventArgs e)
    {
        NavigationDirection? direction = e.Key switch
        {
            Key.Left => NavigationDirection.Left,
            Key.Right => NavigationDirection.Right,
            Key.Up => NavigationDirection.Up,
            Key.Down => NavigationDirection.Down,
            Key.Home => NavigationDirection.First,
            Key.End => NavigationDirection.Last,
            _ => null,
        };

        if (direction is null ||
            sender is not Control fromControl ||
            fromControl.FindAncestorOfType<ItemsControl>() is not { ItemsPanelRoot: INavigableContainer navigable })
        {
            return;
        }

        if (navigable.GetControl(direction.Value, fromControl, wrap: false) is Control target)
        {
            target.Focus();
        }

        // Same "always handled" contract as GridKeyboardNavigation.TryHandleArrowKey - even a
        // clamped no-op consumes the key so it never bubbles to scroll the parent ScrollViewer.
        e.Handled = true;
    }
}
