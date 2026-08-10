namespace Paperbunkr.App.Models;

/// <summary>One match in the Novels reader's in-book search (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §7).</summary>
public sealed class BookSearchResult
{
    public int ChapterIndex { get; init; }

    public int CharacterOffset { get; init; }

    public string ChapterTitle { get; init; } = string.Empty;

    public string Excerpt { get; init; } = string.Empty;
}
