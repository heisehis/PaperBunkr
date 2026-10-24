using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>Shared event filtering for the Insights resolvers that care when something really happened (rhythm, milestones).</summary>
internal static class ReadingEventFilters
{
    /// <summary>
    /// Drops backfilled rows. The backfill writes an Opened and a Finished row for the same item at the identical timestamp
    /// (the item's single <c>OpenedTime</c>), so that coincidence is the signature - the same one <see cref="StatsResolver"/>
    /// uses for its journey-length records. Their weekday, hour and date are fabricated by the import, so anything that
    /// reports "when" must ignore them.
    /// </summary>
    public static List<ReadingEvent> WithoutBackfill(IEnumerable<ReadingEvent> events)
    {
        var all = events as IList<ReadingEvent> ?? events.ToList();
        var backfilled = all
            .GroupBy(e => (e.ItemType, e.ItemId, e.TimestampUtc))
            .Where(g => g.Select(e => e.Kind).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        return all.Where(e => !backfilled.Contains((e.ItemType, e.ItemId, e.TimestampUtc))).ToList();
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
}
