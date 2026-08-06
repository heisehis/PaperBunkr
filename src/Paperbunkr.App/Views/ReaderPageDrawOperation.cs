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

    public ReaderPageDrawOperation(Rect bounds, Bitmap? bitmap)
    {
        Bounds = bounds;
        _bitmap = bitmap;
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
        var sourceRect = new Rect(0, 0, pixelSize.Width, pixelSize.Height);
        context.DrawBitmap(_bitmap, sourceRect, destRect);
    }

    // Bitmap identity changes on every page turn, so there's nothing cheap to compare that would
    // usefully skip a re-render - always report "different."
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose()
    {
    }
}
