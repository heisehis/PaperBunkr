using System;
using Avalonia;

namespace Paperbunkr.App.Views;

/// <summary>
/// Pure double-page-spread pairing/sizing math (docs/superpowers/specs/2026-08-15-reader-double-page-
/// spread-design.md §2/§4), mirroring <see cref="ZoomPanMath"/>'s plain-value-type shape - no Avalonia
/// app context needed. The combined-size trick this backs is deliberate: a spread reduces to "one
/// wider virtual page" fed through <see cref="ZoomPanMath"/>'s existing fit/zoom/pan math unchanged,
/// rather than inventing separate two-image layout math.
/// </summary>
internal static class SpreadLayoutMath
{
    /// <summary>Byte-for-byte CE's own pairing test (confirmed from <c>ComicDisplayControl.GetImageInfo</c>) - both pages portrait-or-square. Index-0/page-count bounds are the caller's concern, not this function's.</summary>
    public static bool IsPairEligible(PixelSize a, PixelSize b) =>
        a.Width <= a.Height && b.Width <= b.Height;

    /// <summary><see cref="Combined"/> is the spread's virtual pixel size, fed through <see cref="ZoomPanMath.ComputeBaseScale"/>/<see cref="ZoomPanMath.ClampPan"/> exactly as if it were one image. <see cref="LeftWidthFraction"/> is the first page's share of the combined width, for <see cref="SplitSpread"/>.</summary>
    public readonly record struct SpreadSize(PixelSize Combined, double LeftWidthFraction);

    /// <summary>CE's own combined-spread-size formula (confirmed from <c>ComicDisplayControl.GetImageInfo</c>): both pages scaled to a common height, widths summed.</summary>
    public static SpreadSize ComputeCombinedSize(PixelSize a, PixelSize b)
    {
        double commonHeight = Math.Max(a.Height, b.Height);
        double widthA = a.Width * commonHeight / a.Height;
        double widthB = b.Width * commonHeight / b.Height;
        double totalWidth = widthA + widthB;

        var combined = new PixelSize(Math.Max(1, (int)Math.Round(totalWidth)), Math.Max(1, (int)Math.Round(commonHeight)));
        return new SpreadSize(combined, widthA / totalWidth);
    }

    /// <summary>Divides <paramref name="destRect"/> into left/right sub-rects at <paramref name="leftWidthFraction"/> of its width - the counterpart to <see cref="ComputeCombinedSize"/>'s <see cref="SpreadSize.LeftWidthFraction"/>, applied once the combined size has already been fit/zoomed/panned into a real on-screen rect.</summary>
    public static (Rect Left, Rect Right) SplitSpread(Rect destRect, double leftWidthFraction)
    {
        double leftWidth = destRect.Width * leftWidthFraction;
        var left = new Rect(destRect.X, destRect.Y, leftWidth, destRect.Height);
        var right = new Rect(destRect.X + leftWidth, destRect.Y, destRect.Width - leftWidth, destRect.Height);
        return (left, right);
    }
}
