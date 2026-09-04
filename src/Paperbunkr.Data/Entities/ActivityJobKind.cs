namespace Paperbunkr.Data.Entities;

/// <summary>
/// Broad category of a background job surfaced in the Activity Center
/// (docs/superpowers/specs/2026-09-03-activity-center-design.md). Drives the row icon and the
/// history "type" filter. Stored as its string name (this context's enum convention), so members
/// may be appended freely; renaming one needs a data migration.
/// </summary>
public enum ActivityJobKind
{
    LibraryScan,
    BookScan,
    GenerateCovers,
    SyncMetadata,
    TrackerFetch,

    /// <summary>Bulk metadata scrape (ComicVine etc.) - reserved; no caller uses it until the bulk-scraper feature lands.</summary>
    Scrape,
    Import,
    Update,
    Migration,

    /// <summary>The single always-present ambient rollup row (live folder-watch + thumbnail decode). Never reaches a terminal state.</summary>
    Upkeep,
    Other,
}
