namespace Paperbunkr.Data.Entities;

/// <summary>
/// A manual bookmark within a <see cref="Book"/> (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §2/§6; anchor reworked by docs/superpowers/specs/
/// 2026-09-07-books-reader-pagination-and-position-fix-design.md to match the block-ID scheme
/// <see cref="BookHighlight"/> already uses). Anchored to <see cref="BlockId"/> (a
/// <c>BlockIdInjector</c>-assigned <c>id="pb-p&lt;n&gt;"</c> on a block-level element within
/// <see cref="ChapterIndex"/>) rather than a character offset - stable across font-size/theme changes
/// and window resizes, since the reflowable reader computes pagination at render time rather than
/// storing it. One bookmark per block, not per chapter - multiple bookmarks can coexist within the
/// same chapter.
/// </summary>
public class BookBookmark
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public Book? Book { get; set; }

    public int ChapterIndex { get; set; }

    public string BlockId { get; set; } = string.Empty;

    /// <summary>Coarse fallback (0-1, scroll/page fraction) for when <see cref="BlockId"/> can't be resolved on reload - see <see cref="Book.LastProgressionFraction"/>'s doc comment for the same idea applied to resume-position.</summary>
    public double? ProgressionFraction { get; set; }

    /// <summary>Snippet of text around the block, so a bookmarks list is recognizable without re-opening the book.</summary>
    public string Excerpt { get; set; } = string.Empty;

    public DateTime CreatedTime { get; set; }
}
