namespace Paperbunkr.Data.Entities;

/// <summary>
/// A manual bookmark within a <see cref="Book"/> (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §2/§6). Position is a character offset within a
/// chapter, not a page number — stable across font-size/theme changes and window resizes, since
/// the reflowable reader computes pagination at render time rather than storing it.
/// </summary>
public class BookBookmark
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public Book? Book { get; set; }

    public int ChapterIndex { get; set; }

    public int CharacterOffset { get; set; }

    /// <summary>Snippet of text around the offset, so a bookmarks list is recognizable without re-opening the book.</summary>
    public string Excerpt { get; set; } = string.Empty;

    public DateTime CreatedTime { get; set; }
}
