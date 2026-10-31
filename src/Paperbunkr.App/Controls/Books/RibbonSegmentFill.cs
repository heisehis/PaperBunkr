using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Paperbunkr.App.Controls.Books;

/// <summary>Draws one ribbon segment: a track, the read part in the accent, or a dashed outline for a ghost. Brushes come from the theme.</summary>
public sealed class RibbonSegmentFill : Control
{
    public static readonly StyledProperty<double> FillProperty =
        AvaloniaProperty.Register<RibbonSegmentFill, double>(nameof(Fill));

    public static readonly StyledProperty<bool> IsGhostProperty =
        AvaloniaProperty.Register<RibbonSegmentFill, bool>(nameof(IsGhost));

    static RibbonSegmentFill() => AffectsRender<RibbonSegmentFill>(FillProperty, IsGhostProperty);

    public RibbonSegmentFill()
    {
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public double Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public bool IsGhost
    {
        get => GetValue(IsGhostProperty);
        set => SetValue(IsGhostProperty, value);
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? found) && found is IBrush brush ? brush : new ImmutableSolidColorBrush(Colors.Gray);

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        const double radius = 2;
        if (IsGhost)
        {
            context.DrawRectangle(null, new Pen(Brush("PbTextFaintBrush"), 1, new ImmutableDashStyle([3, 2], 0)), rect.Deflate(0.5), radius, radius);
            return;
        }

        context.DrawRectangle(Brush("PbSurface3Brush"), null, rect, radius, radius);
        double fill = Math.Clamp(Fill, 0, 1);
        if (fill > 0)
        {
            context.DrawRectangle(Brush("PbAccentBrush"), null, new Rect(0, 0, rect.Width * fill, rect.Height), radius, radius);
        }
    }
}
