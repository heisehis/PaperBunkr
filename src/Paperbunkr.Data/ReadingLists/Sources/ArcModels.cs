using Paperbunkr.Data.ReadingLists.Matching;

namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>One candidate arc/event from <see cref="IReadingListSource.SearchAsync"/> (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §3).</summary>
public sealed record ArcSearchResult(string Id, string Name, string? Deck, string? Publisher, int IssueCount);

/// <summary>
/// One issue in an arc's curated reading order, as reported by the source - resolved against the library, never persisted as-is. The four
/// provider ids are filled only by a source that has them (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.3): the
/// ComicVine and Metron adapters do, the scraped reading-order sites cannot.
/// </summary>
public sealed record ArcIssue(
    string Series, string Number, int Year, string? CoverImageUrl, string? Annotation = null, string? Title = null, string? Summary = null,
    string? ComicVineIssueId = null, string? ComicVineSeriesId = null, string? MetronIssueId = null, string? MetronSeriesId = null)
{
    /// <summary>This entry as the matcher takes it.</summary>
    public ComicReference ToReference(string? sourceKey) => new(
        Series, Number, Volume: null, Year: Year > 0 ? Year : null, Format: null, Title: Title, SourceKey: sourceKey,
        SourceEntryId: sourceKey == "Metron" || sourceKey == "MetronLists" ? MetronIssueId : ComicVineIssueId,
        ComicVineIssueId: ComicVineIssueId, ComicVineSeriesId: ComicVineSeriesId, MetronIssueId: MetronIssueId, MetronSeriesId: MetronSeriesId);
}

/// <summary>
/// A source's reading order together with what it could not deliver (design §5.7): lines it could not read as an issue, and how many
/// entries the source itself says the order has. <see cref="ReportedCount"/> above <c>Issues.Count</c> means the fetch was incomplete.
/// </summary>
public sealed record ArcEntryList(IReadOnlyList<ArcIssue> Issues, IReadOnlyList<string> SkippedLines, int? ReportedCount = null);

/// <summary>Arc-level (not per-issue) synopsis + cover art, always best-effort - a failed or empty fetch never blocks list creation. <see cref="ComicVineId"/> is Metron's <c>cv_id</c> for the arc (its ArcSerializer carries it), null from every other source.</summary>
public sealed record ArcOverviewInfo(string? Description, string? CoverImageUrl, string? ComicVineId = null);
