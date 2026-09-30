using System.Linq;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>
/// Chooses the Event Map's trunk series (docs/superpowers/specs/2026-09-25-event-map-design.md §1 "Choosing the spine").
/// A saved choice that is still a member wins; the sentinel 0 forces relay; otherwise a member series whose name matches
/// the event's (volume/year ignored) is used, preferring the one with most rows, then the one that appears first.
/// A spine is never chosen just because a series has the most issues - that drew whole crossover chapters as tie-ins.
/// </summary>
public static class SpineResolver
{
    /// <summary>Stored in <c>StoryEvent.SpineSeriesId</c> to mean "never use a spine for this event".</summary>
    public const int ForceRelay = 0;

    public static SpineChoice Resolve(EventMapSource source)
    {
        int? autoMatch = FindNameMatch(source);

        if (source.SpineSeriesId == ForceRelay)
        {
            return new SpineChoice(null, SpineSource.None, autoMatch);
        }

        if (source.SpineSeriesId is int saved && source.Rows.Any(r => r.SeriesId == saved))
        {
            return new SpineChoice(saved, SpineSource.User, autoMatch);
        }

        return autoMatch is int auto
            ? new SpineChoice(auto, SpineSource.Auto, autoMatch)
            : new SpineChoice(null, SpineSource.None, null);
    }

    private static int? FindNameMatch(EventMapSource source)
    {
        if (string.IsNullOrWhiteSpace(source.EventName))
        {
            return null;
        }

        string eventName = StripYear(source.EventName);

        // Rows are in reading order, so the first row index of each series is its "first appearance".
        return source.Rows
            .Select((row, index) => (row, index))
            .GroupBy(x => x.row.SeriesId)
            .Where(g => TitleNormalizer.NamesMatch(eventName, StripYear(g.First().row.SeriesName), ignoreVolume: true))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Min(x => x.index))
            .Select(g => (int?)g.Key)
            .FirstOrDefault();
    }

    /// <summary><see cref="TitleNormalizer.NamesMatch"/> ignores volume markers but not a "(2015)" year suffix, which series names carry for disambiguation.</summary>
    private static string StripYear(string name) => YearSuffix.Replace(name, string.Empty).Trim();

    private static readonly Regex YearSuffix = new(@"\s*\((?:19|20)\d{2}\)\s*$", RegexOptions.Compiled);
}
