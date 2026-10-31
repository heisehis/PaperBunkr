using System.Linq;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>
/// Chooses the Event Map's trunk series (docs/superpowers/specs/2026-09-25-event-map-design.md §1 "Choosing the spine").
/// A saved choice that is still a member wins; the sentinel 0 forces relay; otherwise a member series whose name matches
/// the event's (volume/year ignored) is used, preferring the one with most rows, then the one that appears first.
/// A spine is never chosen just because a series has the most issues - that drew whole crossover chapters as tie-ins.
/// The name rule lives in <see cref="EventSpineResolver"/>, shared with the series connector.
/// </summary>
public static class SpineResolver
{
    /// <summary>Stored in <c>StoryEvent.SpineSeriesId</c> to mean "never use a spine for this event".</summary>
    public const int ForceRelay = EventSpineResolver.ForceRelay;

    public static SpineChoice Resolve(EventMapSource source)
    {
        int? autoMatch = EventSpineResolver.FindNameMatch(source.EventName, source.Rows.Select(r => (r.SeriesId, r.SeriesName)).ToList());

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
}
