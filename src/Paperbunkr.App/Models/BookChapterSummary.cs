namespace Paperbunkr.App.Models;

/// <summary>One row in the Novels reader's TOC drawer (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §5).</summary>
public sealed class BookChapterSummary
{
    public int Index { get; init; }

    public string Title { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}
