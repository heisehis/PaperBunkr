using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Writes the append-only <see cref="ReadingEvent"/> log (docs/superpowers/specs/
/// 2026-09-05-insights-dashboard-design.md §5). Called from the three reader view-models at the
/// points that already mutate read state - opening an item, reading it through to the end, and
/// session teardown. The Insights screen subscribes to <see cref="ReadingEventRecorded"/> to drop
/// its cached snapshot when new activity lands while the app is running.
/// </summary>
public interface IReadingEventRecorder
{
    /// <summary>An item was opened for reading. Snapshot fields are frozen into the row.</summary>
    void RecordOpened(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre);

    /// <summary>
    /// An item was read through to the end. Always inserts a new row - re-reads re-emit, each is a
    /// distinct reading act (design §5). <paramref name="pagesRead"/> is the session's page delta
    /// up to the finish (an estimate for reflowed EPUBs).
    /// </summary>
    void RecordFinished(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre, int? pagesRead);

    /// <summary>
    /// Fills <see cref="ReadingEvent.PagesRead"/> on the most recent still-open (PagesRead == null)
    /// <see cref="ReadingEventKind.Opened"/> row for this item - called on session teardown so the
    /// "pages read" metric captures sessions that ended without a finish. No-op if there's no such row.
    /// </summary>
    void UpdateSessionPages(ReadingItemType itemType, int itemId, int pagesRead);

    /// <summary>Raised after any successful write. Handlers may be invoked from a background thread.</summary>
    event Action? ReadingEventRecorded;
}
