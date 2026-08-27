namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>One candidate arc/event from <see cref="IReadingListSource.SearchAsync"/> (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §3).</summary>
public sealed record ArcSearchResult(string Id, string Name, string? Deck, string? Publisher, int IssueCount);

/// <summary>One issue in an arc's curated reading order, as reported by the source - resolved against the library via <see cref="ReadingListMatcher"/>, never persisted as-is.</summary>
public sealed record ArcIssue(string Series, string Number, int Year, string? CoverImageUrl);

/// <summary>Arc-level (not per-issue) synopsis + cover art, always best-effort - a failed or empty fetch never blocks list creation.</summary>
public sealed record ArcOverviewInfo(string? Description, string? CoverImageUrl);
