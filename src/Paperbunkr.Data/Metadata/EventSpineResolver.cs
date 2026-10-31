using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// An event's main series - the Event Map's trunk lane (docs/superpowers/specs/2026-09-25-event-map-design.md §1 "Choosing the spine") and
/// the series every tie-in links to (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.4). Moved here from the App's
/// <c>SpineResolver</c> so the series connector (Data) and the Event Map share one rule: a saved choice that is still a member wins; the
/// sentinel 0 means "no main series"; otherwise a member series whose name matches the event's (volume/year ignored), preferring the one
/// with most rows, then the one that appears first. Never the series with the most issues - that drew whole crossover chapters as tie-ins.
/// </summary>
public static class EventSpineResolver
{
    /// <summary>Stored in <c>StoryEvent.SpineSeriesId</c> to mean "never use a spine for this event".</summary>
    public const int ForceRelay = 0;

    /// <summary>The main series: the saved one if still a member, none if <paramref name="savedSpineSeriesId"/> is <see cref="ForceRelay"/>, else the name match.</summary>
    /// <param name="rowsInReadingOrder">One entry per member issue, in reading order.</param>
    public static int? Resolve(string? eventName, int? savedSpineSeriesId, IReadOnlyList<(int SeriesId, string SeriesName)> rowsInReadingOrder)
    {
        if (savedSpineSeriesId == ForceRelay)
        {
            return null;
        }

        if (savedSpineSeriesId is int saved && rowsInReadingOrder.Any(r => r.SeriesId == saved))
        {
            return saved;
        }

        return FindNameMatch(eventName, rowsInReadingOrder);
    }

    /// <summary>The member series whose name matches the event's, or null.</summary>
    /// <param name="rowsInReadingOrder">One entry per member issue, in reading order (the first index of a series is its first appearance).</param>
    public static int? FindNameMatch(string? eventName, IReadOnlyList<(int SeriesId, string SeriesName)> rowsInReadingOrder)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return null;
        }

        string name = StripYear(eventName);
        return rowsInReadingOrder
            .Select((row, index) => (row, index))
            .GroupBy(x => x.row.SeriesId)
            .Where(g => TitleNormalizer.NamesMatch(name, StripYear(g.First().row.SeriesName), ignoreVolume: true))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Min(x => x.index))
            .Select(g => (int?)g.Key)
            .FirstOrDefault();
    }

    /// <summary><see cref="TitleNormalizer.NamesMatch"/> ignores volume markers but not a "(2015)" year suffix, which series names carry for disambiguation.</summary>
    public static string StripYear(string name) => YearSuffix.Replace(name, string.Empty).Trim();

    private static readonly Regex YearSuffix = new(@"\s*\((?:19|20)\d{2}\)\s*$", RegexOptions.Compiled);
}
