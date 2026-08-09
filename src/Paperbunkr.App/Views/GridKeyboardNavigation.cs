using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Paperbunkr.App.Views;

public enum GridNavigationDirection { Left, Right, Up, Down, Home, End }

/// <summary>
/// Spatial 2D arrow-key movement through a <see cref="WrapPanel"/>-backed grid, extending the Tab
/// order that already exists on Library cards and Detail issue tiles (P5, docs/alpha-roadmap.md),
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
    /// Thin live-control wrapper - the only part touching Avalonia controls. Walks
    /// <paramref name="itemsControl"/>'s realized containers, calls <see cref="Navigate{T}"/>, and
    /// focuses the resolved target container. Returns <see langword="false"/> only for keys other
    /// than Left/Right/Up/Down/Home/End (so Tab/Enter/Space/everything else stays untouched) or an
    /// empty/unrealized list; returns <see langword="true"/> even on a clamped no-op, so arrow keys
    /// never bubble to scroll a parent <see cref="ScrollViewer"/>.
    /// </summary>
    public static bool TryHandleArrowKey(ItemsControl itemsControl, object currentItem, Key key)
    {
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

        if (direction is null)
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
}
