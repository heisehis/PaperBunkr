using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// Draws a single decoded page, letterboxed (uniform-fit, centered) within the given bounds.
/// Matches onboarding.md §8's mechanism choice for paged mode
/// (docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md §4). Doesn't own
/// <see cref="_bitmap"/>'s lifetime — <see cref="Paperbunkr.App.Services.PageImageDecoder"/> does.
///
/// <paramref name="fitMode"/>/<paramref name="fitOnlyIfOversized"/>/<paramref name="rotationDegrees"/>
/// back the fit-mode/rotation controls (docs/superpowers/specs/
/// 2026-08-10-reader-polish-core-viewing-controls-design.md §4).
/// </summary>
public sealed class ReaderPageDrawOperation : ICustomDrawOperation
{
    private readonly Bitmap? _bitmap;
    private readonly bool _highQuality;
    private readonly double _zoom;
    private readonly double _panOffsetX;
    private readonly double _panOffsetY;
    private readonly ImageFitMode _fitMode;
    private readonly bool _fitOnlyIfOversized;
    private readonly int _rotationDegrees;

    /// <summary>
    /// <paramref name="highQuality"/> backs Preferences &gt; Reader &gt; Display's "High quality
    /// page display" toggle (CE parity: <c>ImageDisplayOptions.HighQuality</c>, default on) -
    /// smoother vs. cheaper interpolation when a page is scaled to fit the canvas.
    /// <paramref name="zoom"/>/<paramref name="panOffsetX"/>/<paramref name="panOffsetY"/> back
    /// <see cref="PageCanvas"/>'s zoom/pan gestures (docs/superpowers/specs/
    /// 2026-08-09-reader-gestures-and-grid-navigation-design.md).
    /// </summary>
    public ReaderPageDrawOperation(Rect bounds, Bitmap? bitmap, bool highQuality = true, double zoom = 1.0, double panOffsetX = 0, double panOffsetY = 0,
        ImageFitMode fitMode = ImageFitMode.Fit, bool fitOnlyIfOversized = false, int rotationDegrees = 0)
    {
        Bounds = bounds;
        _bitmap = bitmap;
        _highQuality = highQuality;
        _zoom = zoom;
        _panOffsetX = panOffsetX;
        _panOffsetY = panOffsetY;
        _fitMode = fitMode;
        _fitOnlyIfOversized = fitOnlyIfOversized;
        _rotationDegrees = rotationDegrees;
    }

    public Rect Bounds { get; }

    public bool HitTest(Point p) => Bounds.Contains(p);

    public void Render(ImmediateDrawingContext context)
    {
        if (_bitmap is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var pixelSize = _bitmap.PixelSize;

        // Fit/pan math operates on the *effective* (post-rotation) shape - a 90/270-rotated
        // portrait page occupies a landscape footprint on screen, so fitting/centering has to
        // reason about the rotated shape, not the bitmap's own native orientation.
        bool swapped = _rotationDegrees is 90 or 270;
        var effectivePixelSize = swapped ? new PixelSize(pixelSize.Height, pixelSize.Width) : pixelSize;

        double scale = ZoomPanMath.ComputeBaseScale(Bounds.Size, effectivePixelSize, _fitMode, _fitOnlyIfOversized) * _zoom;

        // Re-clamp locally (draw-time only, not written back) so a window resize while
        // zoomed+panned degrades gracefully instead of showing a stale offset until the next
        // gesture event re-clamps the stored VM state.
        var (panX, panY) = ZoomPanMath.ClampPan(Bounds.Size, effectivePixelSize, _zoom, _panOffsetX, _panOffsetY, _fitMode, _fitOnlyIfOversized);

        double centerX = Bounds.X + (Bounds.Width / 2) + panX;
        double centerY = Bounds.Y + (Bounds.Height / 2) + panY;

        // The bitmap itself is always scaled/drawn at its own native orientation - rotation is
        // applied as a transform around the shared center point, not by pre-rotating pixels.
        double nativeWidth = pixelSize.Width * scale;
        double nativeHeight = pixelSize.Height * scale;
        var destRect = new Rect(centerX - (nativeWidth / 2), centerY - (nativeHeight / 2), nativeWidth, nativeHeight);

        // ImmediateDrawingContext.DrawBitmap has no interpolation-mode overload, and the platform
        // impl's render-options stack that would otherwise control it is marked [Unstable] -
        // inaccessible outside Avalonia's own assemblies. Pre-scaling via CreateScaledBitmap (the
        // same API PageImageDecoder.GetThumbnail already uses) sidesteps that entirely and draws
        // the result 1:1, at the cost of a CPU-side rescale per repaint rather than a GPU-composited
        // stretch - acceptable here since repaints only happen on page turns/window resizes, not
        // every frame.
        var targetSize = new PixelSize(Math.Max(1, (int)Math.Round(nativeWidth)), Math.Max(1, (int)Math.Round(nativeHeight)));
        var mode = _highQuality ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.LowQuality;
        using var scaled = _bitmap.CreateScaledBitmap(targetSize, mode);

        if (_rotationDegrees == 0)
        {
            context.DrawBitmap(scaled, new Rect(0, 0, targetSize.Width, targetSize.Height), destRect);
            return;
        }

        // Rotate around the shared center point: translate it to the origin, rotate, translate
        // back - Avalonia's Matrix composes left-to-right (apply the leftmost operand first), same
        // convention as WPF's Matrix.
        double radians = _rotationDegrees * Math.PI / 180;
        var rotateAroundCenter = Matrix.CreateTranslation(-centerX, -centerY) * Matrix.CreateRotation(radians) * Matrix.CreateTranslation(centerX, centerY);
        using (context.PushPostTransform(rotateAroundCenter))
        {
            context.DrawBitmap(scaled, new Rect(0, 0, targetSize.Width, targetSize.Height), destRect);
        }
    }

    // Bitmap identity changes on every page turn, so there's nothing cheap to compare that would
    // usefully skip a re-render - always report "different."
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose()
    {
    }
}
