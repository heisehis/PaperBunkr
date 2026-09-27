using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Views.Stats;

/// <summary>
/// Tiny inline trend strip for the Stats screen's Reading Activity tiles (docs/superpowers/specs/2026-09-22-
/// insights-period-over-period-deltas-design.md) - decorative only, no hover/tooltip. Same hand-rolled-control
/// shape as <see cref="ActivityHeatmap"/>/<see cref="CategoryDonut"/> in this folder: a single
/// <see cref="StyledProperty{T}"/> holding the data, drawn with <see cref="DrawingContext"/>, brush resolved
/// from the app's own skin resources rather than <c>InsightsChartTheme</c> (that bridges to ScottPlot's own
/// <c>Color</c> type for the two full-size bar charts; this control never touches ScottPlot).
/// </summary>
public sealed class TrendSparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>> DataProperty =
        AvaloniaProperty.Register<TrendSparkline, IReadOnlyList<double>>(nameof(Data), Array.Empty<double>());

    static TrendSparkline()
    {
        AffectsRender<TrendSparkline>(DataProperty);
    }

    public IReadOnlyList<double> Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 80 : availableSize.Width;
        return new Size(width, 20);
    }

    public override void Render(DrawingContext context)
    {
        var points = Data;
        if (points.Count < 2 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return; // a single point (or none) isn't a shape worth drawing
        }

        double min = points.Min();
        double max = points.Max();
        double range = max - min;

        const double inset = 2; // keeps the line from clipping at the top/bottom edge
        double drawableHeight = Math.Max(0, Bounds.Height - inset * 2);
        double stepX = Bounds.Width / (points.Count - 1);

        double Y(double value) => range <= 0
            ? Bounds.Height / 2 // flat series - draw a level line down the middle rather than divide by zero
            : inset + drawableHeight - ((value - min) / range * drawableHeight);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, Y(points[0])), isFilled: false);
            for (int i = 1; i < points.Count; i++)
            {
                ctx.LineTo(new Point(stepX * i, Y(points[i])));
            }

            ctx.EndFigure(isClosed: false);
        }

        var brush = ResolveBrush("PbAccentBrush", Color.FromRgb(0x5b, 0x8d, 0xef));
        context.DrawGeometry(null, new Pen(brush, 1.5), geometry);
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);
}
