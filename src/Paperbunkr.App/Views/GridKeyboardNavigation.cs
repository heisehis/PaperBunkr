using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Views;

public enum GridNavigationDirection { Left, Right, Up, Down, Home, End }

/// <summary>
/// Spatial 2D arrow-key movement through a <see cref="WrapPanel"/>-backed grid, extending the Tab
/// order that already exists on Library cards and Detail issue tiles (P5, docs/Paperbunkr-Roadmap.md),
/// per docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md. Moves focus
/// only - Enter/Space/click remain the sole selection mechanism, unchanged.
/// </summary>
public static class GridKeyboardNavigation
{
    private const double RowEpsilon = 2.0;

    public readonly record struct GridItem<T>(T Item, Rect Bounds) where T : class;

    /// <summary>
    /// Pure core, no Avalonia control types. Left/Right/Home/End are plain index math over
    /// <paramref name="items"/>'s order, clamped at the ends (no wraparound). Up/Down find the
    /// nearest row above/below by <c>Bounds.Y</c> (within <see cref="RowEpsilon"/>) and pick
    /// whichever item in that row has the closest live X to <paramref name="current"/> - no
    /// remembered "anchor column" state.
    /// </summary>
    public static T Navigate<T>(IReadOnlyList<GridItem<T>> items, T current, GridNavigationDirection direction)
        where T : class
    {
        if (items.Count == 0)
        {
            return current;
        }

        int currentIndex = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i].Item, current))
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
        {
            return current;
        }

        return direction switch
        {
            GridNavigationDirection.Home => items[0].Item,
            GridNavigationDirection.End => items[^1].Item,
            GridNavigationDirection.Left => currentIndex > 0 ? items[currentIndex - 1].Item : current,
            GridNavigationDirection.Right => currentIndex < items.Count - 1 ? items[currentIndex + 1].Item : current,
            GridNavigationDirection.Up => NavigateRow(items, items[currentIndex], searchUp: true),
            GridNavigationDirection.Down => NavigateRow(items, items[currentIndex], searchUp: false),
            _ => current,
        };
    }

    private static T NavigateRow<T>(IReadOnlyList<GridItem<T>> items, GridItem<T> current, bool searchUp)
        where T : class
    {
        double? targetRowY = null;
        foreach (var item in items)
        {
            bool isCandidateRow = searchUp
                ? item.Bounds.Y < current.Bounds.Y - RowEpsilon
                : item.Bounds.Y > current.Bounds.Y + RowEpsilon;
            if (!isCandidateRow)
            {
                continue;
            }

            if (targetRowY is null ||
                (searchUp && item.Bounds.Y > targetRowY.Value) ||
                (!searchUp && item.Bounds.Y < targetRowY.Value))
            {
                targetRowY = item.Bounds.Y;
            }
        }

        if (targetRowY is null)
        {
            return current.Item;
        }

        T? best = null;
        double bestDistance = double.MaxValue;
        foreach (var item in items)
        {
            if (Math.Abs(item.Bounds.Y - targetRowY.Value) > RowEpsilon)
            {
                continue;
            }

            double distance = Math.Abs(item.Bounds.X - current.Bounds.X);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = item.Item;
            }
        }

        return best ?? current.Item;
    }

    /// <summary>
    /// Thin live-control wrapper - the only part touching Avalonia controls. Handles BOTH
    /// non-virtualized panels (plain <see cref="WrapPanel"/>, via <see cref="Navigate{T}"/> over
    /// every item's realized container) and virtualized panels (<c>VirtualizingWrapPanel</c>, via
    /// Avalonia's own <see cref="INavigableContainer"/>.GetControl extension point) automatically,
    /// detected from whether <paramref name="itemsControl"/>'s realized
    /// <see cref="ItemsControl.ItemsPanelRoot"/> implements <see cref="INavigableContainer"/>.
    /// Returns <see langword="false"/> only for keys other than Left/Right/Up/Down/Home/End (so
    /// Tab/Enter/Space/everything else stays untouched) or an empty/unrealized non-virtualized
    /// list; returns <see langword="true"/> even on a clamped no-op, so arrow keys never bubble to
    /// scroll a parent <see cref="ScrollViewer"/>.
    ///
    /// Real bug, found via manual testing 2026-09-02: the original version of this method only
    /// implemented the non-virtualized path (<c>ContainerFromIndex</c>/<c>ContainerFromItem</c>,
    /// which only find realized/on-screen containers) and unconditionally returned
    /// <see langword="true"/> regardless of whether a target container was actually found - so for
    /// any screen whose grid had switched to <c>VirtualizingWrapPanel</c> (Library's own main card
    /// grid did, for cover-memory virtualization, without this navigation path being updated to
    /// match), arrow keys silently no-opped outside the visible viewport while still reporting
    /// "handled." <c>SmartScreen.axaml.cs</c>'s own <c>OnResultCardKeyDown</c> already had the
    /// correct virtualized-path logic (confirmed via reflection against the installed
    /// <c>Avalonia.Controls.dll</c> - <c>VirtualizingPanel</c> explicitly implements
    /// <c>INavigableContainer.GetControl</c>, forwarding to the protected virtual method
    /// <c>VirtualizingWrapPanel</c> overrides with real index math and its own
    /// <c>ScrollIntoView</c> to realize the target) - generalized here into the one shared entry
    /// point every grid-nav call site now uses, instead of Smart Lists carrying its own duplicate.
    /// </summary>
    public static bool TryHandleArrowKey(ItemsControl itemsControl, Control fromControl, Key key)
    {
        if (itemsControl.ItemsPanelRoot is INavigableContainer navigable)
        {
            return TryHandleArrowKeyVirtualized(navigable, fromControl, key);
        }

        GridNavigationDirection? direction = key switch
        {
            Key.Left => GridNavigationDirection.Left,
            Key.Right => GridNavigationDirection.Right,
            Key.Up => GridNavigationDirection.Up,
            Key.Down => GridNavigationDirection.Down,
            Key.Home => GridNavigationDirection.Home,
            Key.End => GridNavigationDirection.End,
            _ => null,
        };

        if (direction is null || fromControl.DataContext is not { } currentItem)
        {
            return false;
        }

        int count = itemsControl.ItemCount;
        var items = new List<GridItem<object>>(count);
        for (int i = 0; i < count; i++)
        {
            if (itemsControl.ContainerFromIndex(i) is Control { DataContext: { } dataContext } container)
            {
                items.Add(new GridItem<object>(dataContext, container.Bounds));
            }
        }

        if (items.Count == 0)
        {
            return false;
        }

        object target = Navigate(items, currentItem, direction.Value);
        if (itemsControl.ContainerFromItem(target) is Control targetContainer)
        {
            targetContainer.Focus();
        }

        return true;
    }

    private static bool TryHandleArrowKeyVirtualized(INavigableContainer navigable, Control fromControl, Key key)
    {
        NavigationDirection? direction = key switch
        {
            Key.Left => NavigationDirection.Left,
            Key.Right => NavigationDirection.Right,
            Key.Up => NavigationDirection.Up,
            Key.Down => NavigationDirection.Down,
            Key.Home => NavigationDirection.First,
            Key.End => NavigationDirection.Last,
            _ => null,
        };

        if (direction is null)
        {
            return false;
        }

        // Real bug, found via manual testing 2026-09-02: VirtualizingWrapPanel.GetControl (via
        // INavigableContainer, see this method's own group doc comment above) returns the realized
        // *container* for the target item - for an ItemsControl using a DataTemplate, that's the
        // ContentPresenter hosting the template's root element, not the actually-focusable control
        // inside it (e.g. the card's own Button). ContentPresenter itself isn't Focusable, so
        // .Focus() on it silently no-ops (confirmed: Control.IsFocused stayed false immediately
        // after calling it) - navigation always reported "handled" but visibly never moved focus at
        // all. Walk into the returned container to find the real focusable element instead.
        if (navigable.GetControl(direction.Value, fromControl, wrap: false) is Control target)
        {
            var focusable = target.Focusable ? target : target.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(c => c.Focusable);
            focusable?.Focus();
        }

        return true;
    }
}
