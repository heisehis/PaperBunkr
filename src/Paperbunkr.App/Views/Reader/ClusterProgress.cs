using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Views.Reader;

/// <summary>The page cluster's progress line: a rounded track, the read part in the accent, a hairline tick where each part begins. Not
/// interactive (Go to page in Contents jumps).</summary>
public sealed class ClusterProgress : Control
{
    public static readonly StyledProperty<double> FractionProperty = AvaloniaProperty.Register<ClusterProgress, double>(nameof(Fraction));
    public static readonly StyledProperty<IReadOnlyList<double>?> TicksProperty = AvaloniaProperty.Register<ClusterProgress, IReadOnlyList<double>?>(nameof(Ticks));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<ClusterProgress, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> FillBrushProperty = AvaloniaProperty.Register<ClusterProgress, IBrush?>(nameof(FillBrush));
    public static readonly StyledProperty<IBrush?> TickBrushProperty = AvaloniaProperty.Register<ClusterProgress, IBrush?>(nameof(TickBrush));

    static ClusterProgress() => AffectsRender<ClusterProgress>(FractionProperty, TicksProperty, TrackBrushProperty, FillBrushProperty, TickBrushProperty);

    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    public IReadOnlyList<double>? Ticks { get => GetValue(TicksProperty); set => SetValue(TicksProperty, value); }

    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    public IBrush? FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    public IBrush? TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double barHeight = 4;
        double y = (Bounds.Height - barHeight) / 2;
        if (width <= 0)
        {
            return;
        }

        var track = new Rect(0, y, width, barHeight);
        context.DrawRectangle(TrackBrush, null, track, 2, 2);
        double fill = Math.Clamp(Fraction, 0, 1);
        if (fill > 0 && FillBrush is not null)
        {
            context.DrawRectangle(FillBrush, null, new Rect(0, y, width * fill, barHeight), 2, 2);
        }

        if (Ticks is { Count: > 0 } ticks && TickBrush is not null)
        {
            var pen = new Pen(TickBrush, 1);
            foreach (double tick in ticks)
            {
                if (tick <= 0 || tick >= 1)
                {
                    continue;
                }

                double x = Math.Round(width * tick) + 0.5;
                context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
            }
        }
    }
}
