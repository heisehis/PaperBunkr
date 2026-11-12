using System;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Lays each child across a fraction of the panel's width (<see cref="StartProperty"/>..<see cref="EndProperty"/>, 0..1), full height. Used by the
/// creator career timeline (docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md §4.3) to place a run's bar along the active years
/// without binding grid definitions (which aren't in the visual tree and can't bind).
/// </summary>
public class FractionPanel : Panel
{
    public static readonly AttachedProperty<double> StartProperty =
        AvaloniaProperty.RegisterAttached<FractionPanel, Control, double>("Start");

    public static readonly AttachedProperty<double> EndProperty =
        AvaloniaProperty.RegisterAttached<FractionPanel, Control, double>("End", 1);

    /// <summary>Narrowest width a child gets, so a one-issue run is still something to see and click.</summary>
    public static readonly StyledProperty<double> MinChildWidthProperty =
        AvaloniaProperty.Register<FractionPanel, double>(nameof(MinChildWidth), 8);

    static FractionPanel()
    {
        AffectsParentArrange<FractionPanel>(StartProperty, EndProperty);
    }

    public static double GetStart(Control control) => control.GetValue(StartProperty);
    public static void SetStart(Control control, double value) => control.SetValue(StartProperty, value);
    public static double GetEnd(Control control) => control.GetValue(EndProperty);
    public static void SetEnd(Control control, double value) => control.SetValue(EndProperty, value);

    public double MinChildWidth
    {
        get => GetValue(MinChildWidthProperty);
        set => SetValue(MinChildWidthProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var (x, width) = Span(GetStart(child), GetEnd(child), finalSize.Width, MinChildWidth);
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
        }
        return finalSize;
    }

    /// <summary>The x and width for a start/end fraction pair, clamped inside <paramref name="total"/>.</summary>
    public static (double X, double Width) Span(double start, double end, double total, double minWidth)
    {
        start = Math.Clamp(start, 0, 1);
        end = Math.Clamp(Math.Max(start, end), 0, 1);
        double width = Math.Min(total, Math.Max(minWidth, (end - start) * total));
        double x = Math.Min(start * total, Math.Max(0, total - width));
        return (x, width);
    }
}
