using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A backdrop image that drifts slower than the page as it scrolls (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md C9)
/// - the 2026-09-07 DetailHero parallax done properly. That version translated an image that exactly filled its clipped frame,
/// so any movement exposed an empty edge and it was removed on 2026-09-08. This one draws the image
/// <see cref="ParallaxMath.DefaultOverscan"/> taller than its frame at both ends and only ever moves it within that spare margin
/// (<see cref="ParallaxMath.Offset"/>). Code-only (no .axaml, so no AVLN2000 exposure). Reduced Motion pins the offset at 0,
/// i.e. "off", not merely instant - continuous scroll-linked motion is exactly what that setting exists for.
/// <see cref="Visual.RenderTransform"/> is left free for callers (the Home hero's zoom uses it).
/// </summary>
public sealed class ParallaxBackdrop : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<ParallaxBackdrop, IImage?>(nameof(Source));

    private ScrollViewer? _scrollViewer;
    private double _offset;

    static ParallaxBackdrop()
    {
        AffectsRender<ParallaxBackdrop>(SourceProperty);
        ClipToBoundsProperty.OverrideDefaultValue<ParallaxBackdrop>(true);
    }

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>The current vertical drift in DIPs - exposed for tests.</summary>
    public double Offset => _offset;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => UpdateOffset();

    /// <summary>Recomputes the drift from where this control currently sits in its scroll viewport.</summary>
    public void UpdateOffset()
    {
        double scrolledPast = _scrollViewer is not null && this.TranslatePoint(new Point(0, 0), _scrollViewer) is { } topLeft
            ? -topLeft.Y
            : 0;
        ApplyScroll(scrolledPast);
    }

    /// <summary>Test seam (the headless test app has no theme, so its ScrollViewer can't scroll): applies the drift for a
    /// given distance scrolled past the viewport top. Reduced Motion always yields 0.</summary>
    internal void ApplyScroll(double scrolledPast)
    {
        double next = MotionTokens.IsReducedMotion() ? 0 : ParallaxMath.Offset(scrolledPast, Bounds.Height);

        if (Math.Abs(next - _offset) > 0.01)
        {
            _offset = next;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } image || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double overscan = Bounds.Height * ParallaxMath.DefaultOverscan;
        var dest = new Rect(0, -overscan + _offset, Bounds.Width, Bounds.Height + (2 * overscan));

        // UniformToFill: crop the source to the destination's aspect ratio, centred.
        var size = image.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        double destAspect = dest.Width / dest.Height;
        double srcAspect = size.Width / size.Height;
        Rect src = srcAspect > destAspect
            ? new Rect((size.Width - (size.Height * destAspect)) / 2, 0, size.Height * destAspect, size.Height)
            : new Rect(0, (size.Height - (size.Width / destAspect)) / 2, size.Width, size.Width / destAspect);

        context.DrawImage(image, src, dest);
    }
}
