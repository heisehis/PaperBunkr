namespace Paperbunkr.App.Models;

/// <summary>One row in the Novels reader's TOC drawer (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §5).</summary>
public sealed class BookChapterSummary
{
    public int Index { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>The element id inside chapter <see cref="Index"/>'s file this row points at; null when it is the file's own start. A book can list many rows per file.</summary>
    public string? Fragment { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Read / Current / Unread for this reader (docs/superpowers/specs/2026-10-27-books-slice2b-detail-design.md §5).</summary>
    public BookChapterState State { get; init; }

    /// <summary>This chapter's length relative to the longest chapter, 0-1, for the thin length bar.</summary>
    public double LengthFraction { get; init; }

    /// <summary>How far through the current chapter (0-1); 0 for any other chapter.</summary>
    public double CurrentFraction { get; init; }

    public bool IsRead => State == BookChapterState.Read;

    public bool IsCurrent => State == BookChapterState.Current;

    public bool IsUnread => State == BookChapterState.Unread;

    /// <summary>Nearest ancestor part/section title from the source format's own navigation hierarchy (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md, TOC grouping) - null for a chapter that isn't grouped under anything (EPUB's own nav had no parent with children for it, or the source format doesn't carry this structure at all).</summary>
    public string? PartTitle { get; init; }
}
