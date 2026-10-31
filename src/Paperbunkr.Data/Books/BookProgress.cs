using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Books;

/// <summary>How far through a book the reader is, 0-1 (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §3).</summary>
public static class BookProgress
{
    public static double Compute(Book book)
    {
        if (book.Status == BookReadingStatus.Finished)
        {
            return 1d;
        }

        double fraction = Math.Clamp(book.LastProgressionFraction ?? 0d, 0d, 1d);
        if (book.ChapterCount > 0)
        {
            return Math.Clamp((book.LastChapterIndex + fraction) / book.ChapterCount, 0d, 1d);
        }

        return fraction;
    }
}
