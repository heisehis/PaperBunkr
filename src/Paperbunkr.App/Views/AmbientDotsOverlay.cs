using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// Very slow drifting dots behind the splash emblem (docs/superpowers/specs/2026-09-21-cosmetics-pitch-
/// design.md #6). Frame-driven from <see cref="TopLevel.RequestAnimationFrame"/> (same approach as
/// <c>SmoothScrollViewer</c>), started explicitly by <c>SplashWindow</c> only after the window has opened
/// and only when motion is allowed, and stopped when the splash closes. Never hit-testable. The motion is a
/// pure function of time (<see cref="DotAt"/>), so it allocates nothing per frame beyond the brush cache.
/// </summary>
public sealed class AmbientDotsOverlay : Control
{
    public const int DotCount = 14;

    /// <summary>Extra travel above/below the bounds so a dot wraps while fully off-screen, never popping in view.</summary>
    private const double WrapMargin = 12;

    private double _startSeconds;
    private double _nowSeconds;
    private bool _running;
    private IBrush[]? _brushes;
    private Color _color = Color.FromRgb(0xC9, 0x80, 0x3F);

    /// <summary>The dot tint - the splash passes the persisted theme's accent.</summary>
    public Color DotColor
    {
        get => _color;
        set
        {
            _color = value;
            _brushes = null;
        }
    }

    public bool IsRunning => _running;

    public AmbientDotsOverlay()
    {
        IsHitTestVisible = false;
    }

    /// <summary>Pure drift function: where dot <paramref name="index"/> is after <paramref name="seconds"/>, its radius, and its opacity.</summary>
    public static (Point Center, double Radius, double Opacity) DotAt(int index, double seconds, double width, double height)
    {
        static double Frac(double v) => v - Math.Floor(v);

        double span = height + (2 * WrapMargin);
        double baseX = Frac((index + 1) * 0.6180339887) * width;
        double speed = 4 + (6 * Frac(index * 0.37));          // px per second, upward
        double startY = Frac(index * 0.7548776662) * span;
        double travelled = Frac((startY + (speed * seconds)) / span) * span;
        double y = height + WrapMargin - travelled;
        double x = baseX + (6 * Math.Sin((seconds * 0.4) + (index * 1.7)));
        double radius = 1 + (1.6 * Frac(index * 0.53));
        double opacity = 0.06 + (0.12 * Frac(index * 0.29));
        return (new Point(x, y), radius, opacity);
    }

    /// <summary>Starts the drift. No-op when already running or not attached to a <see cref="TopLevel"/>.</summary>
    public void Start()
    {
        if (_running || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        _running = true;
        _startSeconds = double.NaN;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    public void Stop() => _running = false;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _running = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFrame(TimeSpan timestamp)
    {
        if (!_running)
        {
            return;
        }

        if (double.IsNaN(_startSeconds))
        {
            _startSeconds = timestamp.TotalSeconds;
        }

        _nowSeconds = timestamp.TotalSeconds - _startSeconds;
        InvalidateVisual();

        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            topLevel.RequestAnimationFrame(OnFrame);
        }
        else
        {
            _running = false;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (!_running || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var brushes = _brushes ??= BuildBrushes();
        for (int i = 0; i < DotCount; i++)
        {
            var (center, radius, _) = DotAt(i, _nowSeconds, Bounds.Width, Bounds.Height);
            context.DrawEllipse(brushes[i], null, center, radius, radius);
        }
    }

    private IBrush[] BuildBrushes()
    {
        var brushes = new IBrush[DotCount];
        for (int i = 0; i < DotCount; i++)
        {
            byte alpha = (byte)Math.Round(DotAt(i, 0, 1, 1).Opacity * 255);
            brushes[i] = new SolidColorBrush(Color.FromArgb(alpha, _color.R, _color.G, _color.B)).ToImmutable();
        }

        return brushes;
    }
}
