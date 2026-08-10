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

    public List<BookBookmark> Bookmarks { get; set; } = new();
}
