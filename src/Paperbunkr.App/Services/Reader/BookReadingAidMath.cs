using System;

namespace Paperbunkr.App.Services.Reader;

/// <summary>Where the line ruler's clear band sits (docs/superpowers/specs/2026-10-27-books-slice3b-reader-typography-aids-design.md §4).</summary>
public static class BookRulerMath
{
    public const int LinesInBand = 2;

    /// <summary>The band is two lines tall, centred on the pointer and kept inside the viewport.</summary>
    public static (double Top, double Height) Band(double pointerY, double lineHeightPx, double viewportHeight)
    {
        double height = Math.Max(0, lineHeightPx) * LinesInBand;
        if (height >= viewportHeight)
        {
            return (0, Math.Max(0, viewportHeight));
        }

        double top = Math.Clamp(pointerY - height / 2, 0, viewportHeight - height);
        return (top, height);
    }

    /// <summary>Moves the band by whole lines, still inside the viewport.</summary>
    public static double Step(double top, double lineHeightPx, int lines, double viewportHeight)
    {
        double height = Math.Max(0, lineHeightPx) * LinesInBand;
        return Math.Clamp(top + lines * Math.Max(0, lineHeightPx), 0, Math.Max(0, viewportHeight - height));
    }
}

/// <summary>How fast auto-scroll moves (docs/superpowers/specs/2026-10-27-books-slice3b-reader-typography-aids-design.md §4).</summary>
public static class BookAutoScrollMath
{
    public const int MinSpeed = 1;
    public const int MaxSpeed = 8;
    public const int DefaultSpeed = 3;

    /// <summary>Lines per second at speed 1; speed 3 is about 0.45 lines a second, roughly a relaxed 250 words a minute.</summary>
    private const double LinesPerSecondPerStep = 0.15;

    public static int ClampSpeed(int speed) => Math.Clamp(speed, MinSpeed, MaxSpeed);

    /// <summary>Scales with the line height, so it follows font size and line spacing.</summary>
    public static double PixelsPerSecond(int speed, double lineHeightPx) =>
        Math.Max(0, lineHeightPx) * LinesPerSecondPerStep * ClampSpeed(speed);
}
