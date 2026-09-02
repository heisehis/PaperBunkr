namespace Paperbunkr.Data.Entities;

/// <summary>
/// A user-selected text range highlighted within a <see cref="Book"/> (docs/superpowers/specs/
/// 2026-09-01-books-reader-ergonomics-and-annotations-design.md). Parallel to <see cref="BookBookmark"/>
/// but a range rather than a point - <see cref="StartOffset"/>/<see cref="EndOffset"/> are both
/// character offsets within <see cref="ChapterIndex"/>, same reflow-stable addressing scheme
/// <see cref="BookBookmark"/> already uses.
/// </summary>
public class BookHighlight
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public Book? Book { get; set; }

    public int ChapterIndex { get; set; }

    public int StartOffset { get; set; }

    public int EndOffset { get; set; }

    public BookHighlightColor Color { get; set; }

    public string? Note { get; set; }

    /// <summary>Snippet of the highlighted text, so a highlights list is recognizable without re-opening the book.</summary>
    public string Excerpt { get; set; } = string.Empty;

    public DateTime CreatedTime { get; set; }
}
