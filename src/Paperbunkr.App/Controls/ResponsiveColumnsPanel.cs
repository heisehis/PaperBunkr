using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Equal-width columns that follow the width they are given: as many as fit at <see cref="MinColumnWidth"/> (at most <see cref="MaxColumns"/>, at
/// most one per visible child), each row as tall as its tallest child. A child that is hidden frees its slot, so the remaining ones widen to fill
/// the row - what the Insights Today tiles need and a WrapPanel (fixed-width cells, ragged right edge) cannot do.
/// </summary>
public sealed class ResponsiveColumnsPanel : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<ResponsiveColumnsPanel, double>(nameof(MinColumnWidth), 340);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<ResponsiveColumnsPanel, int>(nameof(MaxColumns), 3);

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<ResponsiveColumnsPanel, double>(nameof(Gap), 12);

    static ResponsiveColumnsPanel()
    {
        AffectsMeasure<ResponsiveColumnsPanel>(MinColumnWidthProperty, MaxColumnsProperty, GapProperty);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>The column count for a panel <paramref name="width"/> wide holding <paramref name="visibleChildren"/> children.</summary>
    public static int ColumnsFor(double width, double minColumnWidth, double gap, int maxColumns, int visibleChildren)
    {
        int fit = (int)Math.Floor((width + gap) / (minColumnWidth + gap));
        return Math.Max(1, Math.Min(Math.Min(fit, Math.Max(1, maxColumns)), Math.Max(1, visibleChildren)));
    }

    private double AvailableWidth(double width) =>
        double.IsInfinity(width) || width <= 0 ? MinColumnWidth : width;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = AvailableWidth(availableSize.Width);
        var visible = VisibleChildren();
        int columns = ColumnsFor(width, MinColumnWidth, Gap, MaxColumns, visible.Count);
        double cell = (width - (columns - 1) * Gap) / columns;
        foreach (var child in visible)
        {
            child.Measure(new Size(cell, double.PositiveInfinity));
        }

        return new Size(width, Place(visible, width, out _));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = AvailableWidth(finalSize.Width);
        Place(VisibleChildren(), width, out var placements);
        foreach (var (child, bounds) in placements)
        {
            child.Arrange(bounds);
        }

        return new Size(width, finalSize.Height);
    }

    private List<Control> VisibleChildren()
    {
        var visible = new List<Control>();
        foreach (var child in Children)
        {
            if (child.IsVisible)
            {
                visible.Add(child);
            }
        }

        return visible;
    }

    private double Place(List<Control> children, double width, out List<(Control Child, Rect Bounds)> placements)
    {
        placements = new List<(Control, Rect)>();
        if (children.Count == 0)
        {
            return 0;
        }

        int columns = ColumnsFor(width, MinColumnWidth, Gap, MaxColumns, children.Count);
        double cell = (width - (columns - 1) * Gap) / columns;
        double y = 0;
        for (int start = 0; start < children.Count; start += columns)
        {
            double rowHeight = 0;
            for (int i = start; i < Math.Min(children.Count, start + columns); i++)
            {
                rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
            }

            for (int i = start; i < Math.Min(children.Count, start + columns); i++)
            {
                placements.Add((children[i], new Rect((i - start) * (cell + Gap), y, cell, rowHeight)));
            }

            y += rowHeight + (start + columns < children.Count ? Gap : 0);
        }

        return y;
    }
}
