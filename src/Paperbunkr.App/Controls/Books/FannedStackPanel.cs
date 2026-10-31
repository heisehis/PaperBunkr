using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Controls.Books;

/// <summary>
/// Series mode's fanned stack (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §4.2): up to three covers, the first in front,
/// the others fanned out behind it to the right and slightly turned. Fixed angles; nothing animates.
/// </summary>
public sealed class FannedStackPanel : Panel
{
    private static readonly double[] Angles = [0, -3, 6];
    private static readonly double[] Offsets = [0, 26, 52];

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(availableSize);
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            int slot = Math.Min(i, Angles.Length - 1);
            child.Arrange(new Rect(Offsets[slot], 24 - (slot * 8), child.DesiredSize.Width, child.DesiredSize.Height));
            child.RenderTransformOrigin = RelativePoint.Center;
            child.RenderTransform = new RotateTransform(Angles[slot]);
            child.ZIndex = Children.Count - i; // the first book sits on top
        }

        return finalSize;
    }
}
