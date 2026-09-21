using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Small circular progress ring for an Activity Center job row (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #21), drawn with
/// the same <see cref="ArcGeometry"/> the Insights donut and the tile progress ring use. Determinate: an arc of <see cref="Fraction"/> starting at
/// 12 o'clock. Indeterminate: a fixed quarter arc (STATIC - the job row's existing marquee bar still carries the "working" motion, so this adds no
/// animation and needs no reduced-motion handling). Hidden by its owner when the job isn't running. Track and value colours come from theme
/// resources so it follows the skin.
/// </summary>
public sealed class ProgressArcRing : Control
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<ProgressArcRing, double>(nameof(Fraction));

    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<ProgressArcRing, bool>(nameof(IsIndeterminate));

    public const double Size = 26;
    private const double Stroke = 2.5;

    static ProgressArcRing()
    {
        AffectsRender<ProgressArcRing>(FractionProperty, IsIndeterminateProperty);
    }

    public ProgressArcRing()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    public bool IsIndeterminate { get => GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        var track = ResolveBrush("PbBorderBrush", Color.FromRgb(0x33, 0x35, 0x3D));
        var value = ResolveBrush("PbAccentBrush", Color.FromRgb(0x5B, 0x8D, 0xEF));
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double radius = (Math.Min(Bounds.Width, Bounds.Height) - Stroke) / 2;
        if (radius <= 0)
        {
            return;
        }

        context.DrawEllipse(null, new Pen(track, Stroke), center, radius, radius);

        double sweepFraction = IsIndeterminate ? 0.25 : Math.Clamp(Fraction, 0, 1);
        if (sweepFraction <= 0.004)
        {
            return;
        }

        var pen = new Pen(value, Stroke, lineCap: PenLineCap.Round);
        if (sweepFraction >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
        }
        else
        {
            context.DrawGeometry(null, pen, ArcGeometry.CreateArc(center, radius, ArcGeometry.TopAngle, sweepFraction * Math.PI * 2));
        }
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? found) && found is IBrush brush ? brush : new SolidColorBrush(fallback);
}
