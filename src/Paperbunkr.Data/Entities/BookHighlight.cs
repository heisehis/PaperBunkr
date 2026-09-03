namespace Paperbunkr.Data.Entities;

/// <summary>
/// A user-selected text range highlighted within a <see cref="Book"/> (docs/superpowers/specs/
/// 2026-09-01-books-reader-ergonomics-and-annotations-design.md, anchor model replaced by
/// docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-redesign-design.md). Anchored to
/// <see cref="BlockId"/> (a <c>BlockIdInjector</c>-assigned <c>id="pb-p&lt;n&gt;"</c> on a single
/// block-level element within <see cref="ChapterIndex"/>) + <see cref="StartOffset"/>/
/// <see cref="Length"/> within that block's own text content - replaces the old "global character
/// offset into flattened plain text" model, which stopped making sense once chapters render as real
/// HTML rather than a flattened paragraph list. Deliberately doesn't support a selection spanning
/// multiple blocks (a real, documented limitation of the WebView selection-capture script, not
/// silently dropped).
/// </summary>
public class BookHighlight
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public Book? Book { get; set; }

    public int ChapterIndex { get; set; }

    public string BlockId { get; set; } = string.Empty;

    public int StartOffset { get; set; }

    public int Length { get; set; }

    public BookHighlightColor Color { get; set; }

    public string? Note { get; set; }

    /// <summary>Snippet of the highlighted text, so a highlights list is recognizable without re-opening the book.</summary>
    public string Excerpt { get; set; } = string.Empty;

    public DateTime CreatedTime { get; set; }
}
