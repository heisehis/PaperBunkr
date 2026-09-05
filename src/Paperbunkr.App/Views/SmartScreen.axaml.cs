using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class SmartScreen : UserControl
{
    public SmartScreen()
    {
        InitializeComponent();
    }

    /// <summary>Backdrop click-to-close for the Grouped Review overlay (docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-scan-alerts-design.md §3) - same pattern as LibraryScreen's Add-issue overlay backdrop.</summary>
    private void OnGroupedReviewBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SmartScreenViewModel vm)
        {
            vm.CloseGroupedReviewCommand.Execute(null);
        }
    }

    /// <summary>
    /// Spatial arrow-key navigation across the virtualized results grid (docs/superpowers/specs/
    /// 2026-08-31-keyboard-operability-design.md). <see cref="GridKeyboardNavigation.TryHandleArrowKey"/>
    /// now handles virtualized panels itself (generalized from this handler's own original
    /// implementation, once Library's main card grid turned out to need the exact same fix - see
    /// that method's own doc comment), so this is just the standard wiring every other grid uses.
    /// </summary>
    private void OnResultCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control fromControl ||
            fromControl.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return;
        }

        if (GridKeyboardNavigation.TryHandleArrowKey(itemsControl, fromControl, e.Key))
        {
            e.Handled = true;
        }
    }
}
