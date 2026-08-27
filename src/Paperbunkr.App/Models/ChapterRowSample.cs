using System;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row in the manga detail screen's Chapters tab (docs/superpowers/specs/2026-08-23-manga-
/// detail-screen-design.md) - deliberately not <see cref="IssueListRow"/> (that DTO carries the
/// full CE field catalog for a cross-series flat list) and deliberately carries no cover art:
/// unlike Western comic issues, manga chapters don't have variant covers, so the list-row format
/// itself is the differentiator from the Issues tab's cover-tile grid, not a smaller cover.
/// </summary>
public sealed class ChapterRowSample
{
    public int Id { get; init; }
    public required string DisplayNumber { get; init; }
    public float? NumberSortKey { get; init; }
    public string? Title { get; init; }
    public bool IsRead { get; init; }
    public bool IsInProgress { get; init; }
    public double ReadPercentage { get; init; }
    public int BookmarkCount { get; init; }
    public bool IsMissing { get; init; }
    public string? ScanInformation { get; init; }
    public DateTime? Date { get; init; }

    public bool HasBookmark => BookmarkCount > 0;
    public bool HasScanInformation => !string.IsNullOrWhiteSpace(ScanInformation);
}

/// <summary>Session-only filter state for the Chapters tab - not persisted, unlike the Library/
/// Comic List sort-group layout system (docs/superpowers/specs/2026-08-17-library-saved-list-
/// layouts-design.md), since this list scopes to a single series, not a cross-library view.</summary>
public enum ChapterListFilter
{
    All,
    Unread,
    Bookmarked,
    Missing,
}

/// <summary>Session-only sort field for the Chapters tab - see <see cref="ChapterListFilter"/>'s own doc comment.</summary>
public enum ChapterSortField
{
    Number,
    Date,
}
