using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// The shelf boards behind the bookshelf view's spines (Cosmetics pitch #19): one board across the full width at the foot of every row of
/// <see cref="SpineMetrics.CellHeight"/>. It sits in the same scrolling grid as the spine panel with the same margin, so it scrolls with the spines and
/// every row lines up exactly. Three flat rectangles per row, so even a 2,000-item library (about a hundred rows) is trivial to draw.
/// </summary>
public sealed class ShelfBoards : Control
{
    public ShelfBoards()
    {
        IsHitTestVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    private IBrush Resolve(string key, Color fallback) => this.TryFindResource(key, out object? found) && found is IBrush brush ? brush : new ImmutableSolidColorBrush(fallback);

    public override void Render(DrawingContext context)
    {
        double rowHeight = SpineMetrics.RowHeight;
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var board = Resolve("PbSurface3Brush", Color.FromRgb(0x2A, 0x2D, 0x36));
        var edge = Resolve("PbBorderBrush", Color.FromRgb(0x33, 0x35, 0x3D));
        var shadow = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x40, 0, 0, 0), 0),
                new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1),
            },
        }.ToImmutable();

        int first = 0;
        int last = (int)Math.Ceiling(Bounds.Height / rowHeight);
        for (int row = first; row < last; row++)
        {
            double boardTop = ((row + 1) * rowHeight) - SpineMetrics.BoardHeight;
            context.DrawRectangle(board, null, new Rect(0, boardTop, Bounds.Width, SpineMetrics.BoardHeight - 2));
            context.DrawRectangle(edge, null, new Rect(0, boardTop, Bounds.Width, 1));
            context.DrawRectangle(shadow, null, new Rect(0, boardTop + SpineMetrics.BoardHeight - 2, Bounds.Width, 6));
        }
    }
}
