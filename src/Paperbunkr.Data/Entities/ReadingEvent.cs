namespace Paperbunkr.Data.Entities;

/// <summary>
/// One reading act — an item opened for reading, or read through to the end
/// (docs/superpowers/specs/2026-09-05-insights-dashboard-design.md §4). Append-only: rows are
/// written where read-state already changes (the three reader view-models) and what they record is
/// never changed afterwards. The only in-place writes are the one-time <see cref="PagesRead"/> fill on
/// session teardown and the <see cref="HiddenFromHistory"/> flag set by the Insights History tab's
/// Remove / Clear all (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md) - a
/// display flag only the History resolver reads; Stats / Goals / Recap / forecasts ignore it on purpose.
///
/// A plain growable table (same category as <see cref="ActivityRun"/> / <see cref="Workspace"/> /
/// <see cref="KeyBinding"/>), not part of <see cref="AppSettings"/>. Unlike <see cref="ActivityRun"/>
/// it is <b>never pruned</b> — the whole point of the log is long-range history for the Insights
/// screen's pace / streak / totals tiles. One row is ~80 bytes.
///
/// Deliberately <b>no FK</b> to <see cref="Issue"/> / <see cref="Book"/>: a reading event must
/// survive deletion of the item it describes, so lifetime totals don't drop when a file is removed
/// from the library. The <see cref="SeriesId"/> / <see cref="Publisher"/> / <see cref="PrimaryGenre"/>
/// columns are denormalised snapshots frozen at write time so the pace/composition queries stay flat
/// and self-sufficient. <see cref="SeriesTitle"/> / <see cref="ItemLabel"/> are display-name snapshots
/// for the History tab's "no longer in library" rows; live surfaces still look titles and covers up
/// from the item and fall back to the snapshot only when it's gone.
/// </summary>
public class ReadingEvent
{
    public int Id { get; set; }

    /// <summary>Which schema the item lives in. Comics and manga are both <see cref="ReadingItemType.Comic"/>.</summary>
    public ReadingItemType ItemType { get; set; }

    /// <summary><see cref="Issue.Id"/> when <see cref="ItemType"/> is Comic, <see cref="Book.Id"/> when Novel. No FK — see the class remarks.</summary>
    public int ItemId { get; set; }

    public ReadingEventKind Kind { get; set; }

    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// Pages read during the session this row represents. Null for backfilled rows and until a
    /// session's teardown fills it in. An <see cref="ReadingEventKind.Opened"/> row is updated in
    /// place with the session's page delta on teardown; a <see cref="ReadingEventKind.Finished"/>
    /// row carries the delta up to the finish. For reflowed EPUB novels this is an estimate
    /// (characters ÷ 1800 — design §6); real page counts for comics and PDF novels.
    /// </summary>
    public int? PagesRead { get; set; }

    /// <summary>
    /// Seconds of active reading in the session this row represents (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.1):
    /// time the reader was showing and the user had given input within the idle cutoff. Written at session teardown onto the same
    /// <see cref="ReadingEventKind.Opened"/> row as <see cref="PagesRead"/>, so the two form one pace sample. Null for every row written
    /// before this existed (no backfill) and for sessions too short to have a page delta; the pace calculation ignores nulls.
    /// </summary>
    public int? ActiveSeconds { get; set; }

    /// <summary>Frozen <see cref="Issue.SeriesId"/> / <see cref="Book.BookSeriesId"/> at write time. Nullable (standalone novel).</summary>
    public int? SeriesId { get; set; }

    /// <summary>Frozen effective publisher for a comic; null for a novel.</summary>
    public string? Publisher { get; set; }

    /// <summary>Frozen first genre tag for a comic; null when none / for a novel.</summary>
    public string? PrimaryGenre { get; set; }

    /// <summary>
    /// Frozen display name of the row's History group at write time: <see cref="Series.Name"/> for a
    /// comic, <see cref="BookSeries.Name"/> for a book in a series, <see cref="Book.Title"/> for a
    /// standalone book. Null for rows whose item was already gone when the snapshot was taken.
    /// </summary>
    public string? SeriesTitle { get; set; }

    /// <summary>
    /// Frozen per-item label at write time: <c>#12</c> / <c>Vol. 2</c> / the issue title for a comic,
    /// the book title for a book in a series, null for a standalone book (its title is <see cref="SeriesTitle"/>).
    /// </summary>
    public string? ItemLabel { get; set; }

    /// <summary>Hidden from the Insights History tab by Remove / Clear all. Read only by the History resolver.</summary>
    public bool HiddenFromHistory { get; set; }
}

public enum ReadingItemType
{
    Comic = 0,
    Novel = 1,
}

public enum ReadingEventKind
{
    Opened = 0,
    Finished = 1,
}
