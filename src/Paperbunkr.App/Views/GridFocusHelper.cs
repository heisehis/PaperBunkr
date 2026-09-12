using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Views;

/// <summary>
/// Resolves an arbitrary item in an <see cref="ItemsControl"/> to its focusable control, realizing
/// it first if it's currently off-screen/unrealized. Shared by <see cref="GridKeyboardNavigation"/>
/// (whose own virtualized path only supports single-step relative movement via
/// <c>INavigableContainer</c>, not "jump to this specific item") and <c>TypeAheadSearch</c>
/// (docs/superpowers/specs/2026-09-12-grid-typeahead-rangeselect-quit-design.md), which both need
/// "focus item X regardless of whether it's currently realized" - a concept that belongs to neither
/// one exclusively.
/// </summary>
internal static class GridFocusHelper
{
    /// <summary>
    /// Focuses <paramref name="item"/> inside <paramref name="itemsControl"/>, force-realizing it
    /// first if necessary. Returns <see langword="true"/> if a focusable control was found and
    /// focused. <c>ListBox</c> has its own public <c>ScrollIntoView</c>; the plain-<c>ItemsControl</c>
    /// grids use <see cref="Controls.VirtualizingWrapPanel.ScrollToIndex"/>/
    /// <see cref="Controls.VirtualizingVariableWrapPanel.ScrollToIndex"/> instead, added for exactly
    /// this purpose since their own <c>ScrollIntoView</c> override is protected.
    /// </summary>
    public static bool FocusItem(ItemsControl itemsControl, object item)
    {
        var container = itemsControl.ContainerFromItem(item);
        if (container is null)
        {
            int index = itemsControl.Items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            switch (itemsControl)
            {
                case ListBox listBox:
                    listBox.ScrollIntoView(index);
                    container = itemsControl.ContainerFromIndex(index);
                    break;
                default:
                    container = itemsControl.ItemsPanelRoot switch
                    {
                        Controls.VirtualizingWrapPanel vwp => vwp.ScrollToIndex(index),
                        Controls.VirtualizingVariableWrapPanel vvwp => vvwp.ScrollToIndex(index),
                        _ => null,
                    };
                    break;
            }
        }

        if (container is not Control controlContainer)
        {
            return false;
        }

        var focusable = controlContainer.Focusable
            ? controlContainer
            : controlContainer.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(c => c.Focusable);

        if (focusable is null)
        {
            return false;
        }

        focusable.Focus();
        return true;
    }
}
