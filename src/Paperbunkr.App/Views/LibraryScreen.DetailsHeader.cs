using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The Details table header's two drags (docs/superpowers/specs/2026-10-04-list-layouts-design.md §8): the grip at a
/// column's right edge resizes it, and dragging the header itself moves the column. Both have a keyboard path in List
/// Options (the width field, Move Up / Move Down), so neither is the only way.
/// </summary>
public partial class LibraryScreen
{
    /// <summary>A header cell is its column's width plus this gap (<c>Button.detailsHeader</c>'s right margin, where the grip sits).</summary>
    private const double DetailsHeaderGap = 10;

    /// <summary>How far the pointer must travel before a press on a header becomes a move instead of a click-to-sort.</summary>
    private const double HeaderDragThreshold = 8;

    private DetailsColumn? _headerPressColumn;
    private Point _headerPressPoint;
    private bool _headerDragging;

    private void AttachDetailsHeaderDrag()
    {
        // Tunnel: a Button swallows a left press (Avalonia 12), so a handler on the header button itself never sees it.
        DetailsHeaderItems.AddHandler(PointerPressedEvent, OnDetailsHeaderPointerPressed, RoutingStrategies.Tunnel);
        DetailsHeaderItems.AddHandler(PointerMovedEvent, OnDetailsHeaderPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        DetailsHeaderItems.AddHandler(PointerReleasedEvent, OnDetailsHeaderPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        DetailsHeaderItems.AddHandler(PointerCaptureLostEvent, (_, _) => EndHeaderDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnColumnGripDragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb { DataContext: DetailsColumn column } && DataContext is LibraryScreenViewModel vm)
        {
            vm.SetDetailsColumnWidth(column, column.Width + e.Vector.X, commit: false);
        }
    }

    private void OnColumnGripDragCompleted(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb { DataContext: DetailsColumn column } && DataContext is LibraryScreenViewModel vm)
        {
            vm.SetDetailsColumnWidth(column, column.Width, commit: true);
        }
    }

    private void OnDetailsHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _headerDragging = false;
        _headerPressColumn = null;
        if (!e.GetCurrentPoint(DetailsHeaderItems).Properties.IsLeftButtonPressed
            || e.Source is not Visual source
            || source.FindAncestorOfType<Thumb>(includeSelf: true) is not null)
        {
            return;
        }

        _headerPressColumn = (source as StyledElement)?.DataContext as DetailsColumn
            ?? source.FindAncestorOfType<Button>(includeSelf: true)?.DataContext as DetailsColumn;
        _headerPressPoint = e.GetPosition(DetailsHeaderItems);
    }

    private void OnDetailsHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_headerPressColumn is null || _headerDragging)
        {
            return;
        }

        if (!e.GetCurrentPoint(DetailsHeaderItems).Properties.IsLeftButtonPressed)
        {
            _headerPressColumn = null;
            return;
        }

        if (Math.Abs(e.GetPosition(DetailsHeaderItems).X - _headerPressPoint.X) >= HeaderDragThreshold)
        {
            // Taking the capture off the header button is also what stops its Click (the sort) firing on release.
            _headerDragging = true;
            e.Pointer.Capture(DetailsHeaderItems);
            DetailsHeaderItems.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        }
    }

    private void OnDetailsHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_headerDragging && _headerPressColumn is { } column && DataContext is LibraryScreenViewModel vm
            && DetailsHeaderColumnAt(vm, e.GetPosition(DetailsHeaderItems).X) is { } target && !ReferenceEquals(target, column))
        {
            vm.MoveDetailsColumnTo(column, target);
            e.Handled = true;
        }

        e.Pointer.Capture(null);
        EndHeaderDrag();
    }

    private void EndHeaderDrag()
    {
        _headerDragging = false;
        _headerPressColumn = null;
        DetailsHeaderItems.Cursor = null;
    }

    /// <summary>The visible column whose header cell covers <paramref name="x"/> (header coordinates); the first or last one when x is outside them.</summary>
    internal static DetailsColumn? DetailsHeaderColumnAt(LibraryScreenViewModel vm, double x)
    {
        var visible = vm.DetailsColumns.Where(c => c.IsVisible).ToList();
        double right = 0;
        foreach (var column in visible)
        {
            right += column.Width + DetailsHeaderGap;
            if (x < right)
            {
                return column;
            }
        }

        return visible.LastOrDefault();
    }
}
