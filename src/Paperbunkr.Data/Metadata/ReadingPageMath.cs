namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Shared "pages read" conventions for the Insights dashboard (docs/superpowers/specs/
/// 2026-09-05-insights-dashboard-design.md §6). Comics and PDF novels report real page counts;
/// reflowed EPUB/FB2/MOBI novels have no intrinsic page count, so their "pages" are estimated from
/// character count. One constant, one place, referenced by both the book reader (which writes the
/// per-session estimate into the <c>ReadingEvent</c> log) and <see cref="InsightsResolver"/> (which
/// aggregates lifetime "pages" totals).
/// </summary>
public static class ReadingPageMath
{
    /// <summary>Characters per estimated page for reflowed text. A round, disclosed approximation.</summary>
    public const int EpubCharsPerPage = 1800;

    /// <summary>Estimated page count for a reflowed-text run of <paramref name="characters"/> chars. Never negative.</summary>
    public static int EstimatePagesFromChars(long characters)
        => characters <= 0 ? 0 : (int)(characters / EpubCharsPerPage);
}
