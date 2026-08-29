namespace Paperbunkr.Data.Entities;

/// <summary>
/// A single prose novel file (EPUB or PDF) — docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §2. Independent of the comic-schema
/// <see cref="Issue"/>: no shared columns, no FK crossing between the two schemas.
/// </summary>
public class Book
{
    public int Id { get; set; }

    /// <summary>Null for a standalone novel with no series.</summary>
    public int? BookSeriesId { get; set; }

    public BookSeries? BookSeries { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Author { get; set; }

    public BookFormat Format { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public string? CoverImagePath { get; set; }

    public string? Summary { get; set; }

    public DateTime? PublishedDate { get; set; }

    public DateTime AddedTime { get; set; }

    // --- Read-state, named to match Issue's OpenedTime/LastPageRead convention ---

    public DateTime? LastOpenedTime { get; set; }

    public int LastChapterIndex { get; set; }

    public int LastCharacterOffset { get; set; }

    /// <summary>Read to the end at least once (docs/superpowers/specs/2026-08-27-books-screen-chrome-
    /// and-home-strip-design.md). Set by the reader when paging past the last chapter; cleared when
    /// the book is reopened for reading. Drops the book off Home's "Continue Reading — Books" row.</summary>
    public bool Finished { get; set; }

    /// <summary>Total chapter count from the parsed source, populated lazily the first time the book
    /// is opened in the reader (0 until then). Feeds Home's rough progress bar
    /// (<see cref="LastChapterIndex"/> / <see cref="ChapterCount"/>); meaningless for
    /// <see cref="BookFormat.Pdf"/> (the page reader has no chapters).</summary>
    public int ChapterCount { get; set; }

    public List<BookBookmark> Bookmarks { get; set; } = new();

    /// <summary>Collections this book belongs to, via the polymorphic <see cref="CollectionItem"/> join (docs/superpowers/specs/2026-08-27-collections-design.md). First FK crossing from the library-org layer into the Book schema — see <see cref="CollectionItem"/>.</summary>
    public List<CollectionItem> CollectionItems { get; set; } = new();
}
