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
    public string? Volume { get; init; }
    public bool IsRead { get; init; }

    /// <summary>Unread and released within the last 14 days - shows a "NEW" badge in the release feed.</summary>
    public bool IsNew { get; init; }

    public bool IsInProgress { get; init; }
    public double ReadPercentage { get; init; }
    public int BookmarkCount { get; init; }
    public bool IsMissing { get; init; }
    public string? ScanInformation { get; init; }
    public DateTime? Date { get; init; }

    public bool HasBookmark => BookmarkCount > 0;
    public bool HasScanInformation => !string.IsNullOrWhiteSpace(ScanInformation);

    /// <summary>Fully-read badge on the chapter row (docs/superpowers/specs/2026-09-04-detail-
    /// screen-icons-and-glyphs-design.md §8) - suppressed while still in progress, since the
    /// row already shows a progress bar for that state.</summary>
    public bool ShowReadGlyph => IsRead && !IsInProgress;
}

/// <summary>A volume section in the manga release-feed Chapters tab (docs/superpowers/specs/
/// 2026-08-28-detail-screens-streaming-redesign-design.md).</summary>
public sealed class ChapterVolumeGroup
{
    public required string VolumeLabel { get; init; }
    public required System.Collections.Generic.IReadOnlyList<ChapterRowSample> Chapters { get; init; }
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
