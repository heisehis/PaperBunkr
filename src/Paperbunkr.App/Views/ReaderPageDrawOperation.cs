using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;

namespace Paperbunkr.App.Views;

/// <summary>
/// Draws a single decoded page, letterboxed (uniform-fit, centered) within the given bounds.
/// Matches onboarding.md §8's mechanism choice for paged mode
/// (docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md §4). Doesn't own
/// <see cref="_bitmap"/>'s lifetime — <see cref="Paperbunkr.App.Services.PageImageDecoder"/> does.
/// </summary>
public sealed class ReaderPageDrawOperation : ICustomDrawOperation
{
    private readonly Bitmap? _bitmap;
    private readonly bool _highQuality;

    /// <summary>
    /// <paramref name="highQuality"/> backs Preferences &gt; Reader &gt; Display's "High quality
    /// page display" toggle (CE parity: <c>ImageDisplayOptions.HighQuality</c>, default on) -
    /// smoother vs. cheaper interpolation when a page is scaled to fit the canvas.
    /// </summary>
    public ReaderPageDrawOperation(Rect bounds, Bitmap? bitmap, bool highQuality = true)
    {
        Bounds = bounds;
        _bitmap = bitmap;
        _highQuality = highQuality;
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
        double scale = Math.Min(Bounds.Width / pixelSize.Width, Bounds.Height / pixelSize.Height);
        double width = pixelSize.Width * scale;
        double height = pixelSize.Height * scale;
        double x = Bounds.X + ((Bounds.Width - width) / 2);
        double y = Bounds.Y + ((Bounds.Height - height) / 2);

        var destRect = new Rect(x, y, width, height);

        // ImmediateDrawingContext.DrawBitmap has no interpolation-mode overload, and the platform
        // impl's render-options stack that would otherwise control it is marked [Unstable] -
        // inaccessible outside Avalonia's own assemblies. Pre-scaling via CreateScaledBitmap (the
        // same API PageImageDecoder.GetThumbnail already uses) sidesteps that entirely and draws
        // the result 1:1, at the cost of a CPU-side rescale per repaint rather than a GPU-composited
        // stretch - acceptable here since repaints only happen on page turns/window resizes, not
        // every frame.
        var targetSize = new PixelSize(Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
        var mode = _highQuality ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.LowQuality;
        using var scaled = _bitmap.CreateScaledBitmap(targetSize, mode);
        context.DrawBitmap(scaled, new Rect(0, 0, targetSize.Width, targetSize.Height), destRect);
    }

    // Bitmap identity changes on every page turn, so there's nothing cheap to compare that would
    // usefully skip a re-render - always report "different."
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose()
    {
    }
}
