using System;

namespace Paperbunkr.Data.Entities;

/// <summary>
/// One series-level event on the Detail screen's Activity tab (docs/superpowers/specs/2026-09-13-
/// activity-tab-event-log-expansion-design.md) - metadata linked/unlinked, tracker linked/unlinked/
/// synced, rating changed. Append-only, merged with <see cref="ReadingEvent"/> rows at read time
/// (<c>DetailTabsViewModel.RefreshActivity</c>) rather than sharing that table - <see cref="ReadingEvent"/>
/// already feeds Insights-dashboard pace/streak/totals math, and mixing unrelated kinds into that
/// enum would risk breaking it.
///
/// Deliberately <b>no FK</b> to <see cref="Series"/>/<see cref="Issue"/>, same convention as
/// <see cref="ReadingEvent"/> - a series' activity history should survive deletion of the series or
/// issue it describes.
/// </summary>
public class SeriesActivityEvent
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    /// <summary>Set for <see cref="SeriesActivityEventKind.RatingChanged"/> (which issue was rated); null for the series-level kinds.</summary>
    public int? IssueId { get; set; }

    public SeriesActivityEventKind Kind { get; set; }

    public DateTime TimestampUtc { get; set; }

    /// <summary>Provider/service name for metadata/tracker events, or a ready-to-display sentence for TrackerSynced/RatingChanged.</summary>
    public string Detail { get; set; } = string.Empty;
}

public enum SeriesActivityEventKind
{
    MetadataLinked = 0,
    MetadataUnlinked = 1,
    TrackerLinked = 2,
    TrackerUnlinked = 3,
    TrackerSynced = 4,
    RatingChanged = 5,
}
