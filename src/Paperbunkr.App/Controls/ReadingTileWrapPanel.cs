using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Controls;

/// <summary>
/// The Reading Lists gallery's grid (docs/superpowers/specs/2026-10-20-reading-lists-visual-identity-design.md §1): folder cards first, in
/// narrower columns, then a forced row break, then the wide list cards. A plain WrapPanel would let the last folder share a row with the first
/// list and leave a gap under it. Each child gets a cell as wide as its band's column (<see cref="ReadingGalleryView.Layout"/> /
/// <see cref="ReadingGalleryView.FolderLayout"/>); the card inside keeps its own right and bottom margin as the gap.
/// </summary>
public sealed class ReadingTileWrapPanel : Panel
{
    private readonly record struct Placement(Control Child, Rect Bounds);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = AvailableWidth(availableSize.Width);
        foreach (var child in Children)
        {
            if (child.IsVisible)
            {
                child.Measure(new Size(Cell(child, width), double.PositiveInfinity));
            }
        }

        return new Size(width, Place(width, out _));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = AvailableWidth(finalSize.Width);
        Place(width, out var placements);
        foreach (var placement in placements)
        {
            placement.Child.Arrange(placement.Bounds);
        }

        return new Size(width, finalSize.Height);
    }

    private static double AvailableWidth(double available) =>
        double.IsInfinity(available) || available <= 0 ? ReadingGalleryView.MinCardWidth + ReadingGalleryView.CardGap : available;

    private static bool IsFolder(Control child) => child.DataContext is ReadingGalleryTile { IsFolder: true };

    private static double Cell(Control child, double width) =>
        IsFolder(child) ? ReadingGalleryView.FolderLayout(width).Cell : ReadingGalleryView.Layout(width).Cell;

    /// <summary>Flows the children left to right; a row ends when the next cell does not fit or the folder band gives way to the list band.
    /// Returns the total height.</summary>
    private double Place(double width, out List<Placement> placements)
    {
        placements = new List<Placement>();
        double x = 0, y = 0, rowHeight = 0;
        bool? previousFolder = null;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            bool folder = IsFolder(child);
            double cell = Cell(child, width);
            if (previousFolder is { } was && (was != folder || x + cell > width + 0.5))
            {
                y += rowHeight;
                x = 0;
                rowHeight = 0;
            }

            placements.Add(new Placement(child, new Rect(x, y, cell, child.DesiredSize.Height)));
            x += cell;
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            previousFolder = folder;
        }

        return y + rowHeight;
    }
}
