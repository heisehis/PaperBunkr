namespace Paperbunkr.App.Models;

/// <summary>One row in the Novels reader's Bookmarks drawer (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §6).</summary>
public sealed class BookBookmarkSummary
{
    public int Id { get; init; }

    public int ChapterIndex { get; init; }

    public int CharacterOffset { get; init; }

    public string ChapterTitle { get; init; } = string.Empty;

    public string Excerpt { get; init; } = string.Empty;
}
