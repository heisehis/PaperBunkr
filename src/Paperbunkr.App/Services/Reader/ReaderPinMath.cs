using System;
using Avalonia;

namespace Paperbunkr.App.Services.Reader;

/// <summary>The three sizes of the floating pinned page (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #29).</summary>
public enum PinSize
{
    Small,
    Medium,
    Large,
}

/// <summary>The corner the pinned page rests in.</summary>
public enum PinCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Pure geometry for the pinned reference page: its size steps, the corner a drag lands in, and the size of the copy that is kept.</summary>
public static class ReaderPinMath
{
    /// <summary>The longest side, in pixels, of the scaled copy taken when a page is pinned (enough for the large size on a sharp screen).</summary>
    public const int CopyLongSide = 600;

    /// <summary>The panel's width in device-independent pixels for a size.</summary>
    public static double WidthFor(PinSize size) => size switch
    {
        PinSize.Small => 160,
        PinSize.Medium => 260,
        _ => 400,
    };

    /// <summary>The next size in the cycle small, medium, large, small.</summary>
    public static PinSize Next(PinSize size) => size == PinSize.Large ? PinSize.Small : size + 1;

    /// <summary>One size up (<paramref name="up"/>) or down, stopping at the ends (the mouse wheel over the pin).</summary>
    public static PinSize Step(PinSize size, bool up) => up ? (PinSize)Math.Min((int)size + 1, (int)PinSize.Large) : (PinSize)Math.Max((int)size - 1, (int)PinSize.Small);

    /// <summary>The corner nearest a point in the reader (a drag released there).</summary>
    public static PinCorner NearestCorner(Point point, Size bounds)
    {
        bool right = point.X >= bounds.Width / 2;
        bool bottom = point.Y >= bounds.Height / 2;
        return (right, bottom) switch
        {
            (false, false) => PinCorner.TopLeft,
            (true, false) => PinCorner.TopRight,
            (false, true) => PinCorner.BottomLeft,
            _ => PinCorner.BottomRight,
        };
    }

    /// <summary>The size of the scaled copy of a page of <paramref name="source"/> pixels: its long side is <see cref="CopyLongSide"/> (never enlarged), the aspect ratio kept.</summary>
    public static PixelSize CopySize(PixelSize source)
    {
        int longest = Math.Max(source.Width, source.Height);
        if (longest <= 0)
        {
            return new PixelSize(1, 1);
        }

        double scale = Math.Min(1.0, CopyLongSide / (double)longest);
        return new PixelSize(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
    }

    /// <summary>The margin that keeps a pinned page in a corner clear of the chrome clusters (top: the navigate and actions clusters; bottom: the view cluster and the page-turn strip).</summary>
    public static Thickness MarginFor(PinCorner corner) => corner switch
    {
        PinCorner.TopLeft => new Thickness(16, 64, 0, 0),
        PinCorner.TopRight => new Thickness(0, 64, 16, 0),
        PinCorner.BottomLeft => new Thickness(16, 0, 0, 90),
        _ => new Thickness(0, 0, 16, 90),
    };
}
