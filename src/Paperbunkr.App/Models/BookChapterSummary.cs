namespace Paperbunkr.App.Models;

/// <summary>One row in the Novels reader's TOC drawer (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §5).</summary>
public sealed class BookChapterSummary
{
    public int Index { get; init; }

    public string Title { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    /// <summary>Nearest ancestor part/section title from the source format's own navigation hierarchy (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md, TOC grouping) - null for a chapter that isn't grouped under anything (EPUB's own nav had no parent with children for it, or the source format doesn't carry this structure at all).</summary>
    public string? PartTitle { get; init; }
}
