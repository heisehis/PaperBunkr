using System;
using Avalonia.Media;

namespace Paperbunkr.App.Services;

/// <summary>
/// Pure sizing and colour rules for the Library's bookshelf view (Cosmetics pitch #19, docs/superpowers/specs/2026-10-12-cosmetics-pitch-completion-design.md C).
/// Everything here is deterministic from an item's own data, so a spine never changes size between renders and the packed layout can be computed
/// from the data alone (the panel never has to realize a container to measure it).
/// </summary>
public static class SpineMetrics
{
    /// <summary>Height of one shelf row: the tallest spine, the board it stands on, and a little air above.</summary>
    public const double CellHeight = 236;

    /// <summary>Thickness of the shelf board drawn at the bottom of every row.</summary>
    public const double BoardHeight = 12;

    public const double MinWidth = 24;
    public const double MaxWidth = 66;

    private const double BaseHeight = 196;
    private const double HeightJitter = 14;

    /// <summary>An issue spine: thicker for a longer book (18 + a third of the pages, clamped to 24..54 wide). No page count is treated as a thin 32 pages.</summary>
    public static double WidthForPages(int? pages) => Math.Clamp(18 + ((pages is > 0 ? pages.Value : 32) / 3.0), MinWidth, 54);

    /// <summary>A series spine stands for a run of issues, so it is wider the more issues it holds (clamped to 28..66).</summary>
    public static double WidthForSeries(int issueCount) => Math.Clamp(24 + (Math.Max(0, issueCount) * 0.9), 28, MaxWidth);

    /// <summary>Spine height: the base plus a few pixels either way taken from the item's id, so a shelf looks hand-stacked but never reshuffles.</summary>
    public static double HeightFor(int seed)
    {
        uint h = unchecked((uint)seed * 2654435761u);
        h ^= h >> 15;
        double unit = (h % 1000) / 999.0;      // 0..1, deterministic
        return Math.Round(BaseHeight + ((unit * 2 - 1) * HeightJitter));
    }

    /// <summary>The spine body colour for a cover's dominant colour: pulled towards a dark, slightly desaturated tone so a row of spines reads as books rather than a rainbow.</summary>
    public static Color BodyColor(Color dominant)
    {
        double grey = (dominant.R + dominant.G + dominant.B) / 3.0;
        byte Channel(byte c) => (byte)Math.Clamp(((c * 0.8) + (grey * 0.2)) * 0.72, 0, 255);
        return Color.FromRgb(Channel(dominant.R), Channel(dominant.G), Channel(dominant.B));
    }

    /// <summary>WCAG-style relative luminance of an sRGB colour.</summary>
    public static double Luminance(Color color)
    {
        static double Lin(byte c)
        {
            double s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Lin(color.R)) + (0.7152 * Lin(color.G)) + (0.0722 * Lin(color.B));
    }

    /// <summary>Whether text on <paramref name="body"/> should be the dark tone (a light spine) rather than the light one.</summary>
    public static bool UseDarkText(Color body) => Luminance(body) > 0.4;

    /// <summary>Top-to-bottom row index of item <paramref name="index"/> is not needed by the view; the shelf boards only need the row height.</summary>
    public static double RowHeight => CellHeight;
}
