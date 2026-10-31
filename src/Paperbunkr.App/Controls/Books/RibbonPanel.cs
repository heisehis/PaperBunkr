using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Books;

namespace Paperbunkr.App.Controls.Books;

/// <summary>Lays the ribbon's segments out in one row, each as wide as its <see cref="RibbonSegment.Weight"/> share (read from the child's DataContext).</summary>
public sealed class RibbonPanel : Panel
{
    public const double Gap = 4;

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double width = double.IsInfinity(availableSize.Width) ? Children.Count * 24 : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var weights = Children.Select(c => c.DataContext is RibbonSegment s ? s.Weight : 1d).ToArray();
        var widths = RibbonMath.Widths(weights, finalSize.Width, Gap);
        double x = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
            x += widths[i] + Gap;
        }

        return finalSize;
    }
}
