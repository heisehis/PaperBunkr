using System;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Books grid card (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §1 Phase
/// 1) - deliberately simpler than <see cref="SeriesCardSample"/> (a single density, no
/// grouping/sort-aggregate fields yet) since Phase 1 is library-only, no reader.
/// </summary>
public sealed class BookCardSample
{
    public int BookId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Author { get; init; }

    public string? SeriesName { get; init; }

    public bool HasSeries => !string.IsNullOrWhiteSpace(SeriesName);

    public BookFormat Format { get; init; }

    /// <summary>Sort keys for the Books screen chrome (docs/superpowers/specs/2026-08-27-books-
    /// screen-chrome-and-home-strip-design.md).</summary>
    public DateTime AddedTime { get; init; }

    public DateTime? LastOpenedTime { get; init; }

    /// <summary>Read to the end at least once - carried for a future dim/badge (Piece B); not
    /// rendered on the grid in Piece A.</summary>
    public bool Finished { get; init; }

    public Avalonia.Media.IBrush CoverBrush { get; init; } = SeriesCardSample.CoverBrushFor(string.Empty);

    /// <summary>Real generated cover (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §3), null until "Generate Covers" has processed this book.</summary>
    public Bitmap? CoverImage { get; init; }

    public static BookCardSample FromBook(Book book)
    {
        return new BookCardSample
        {
            BookId = book.Id,
            Title = book.Title,
            Author = book.Author,
            SeriesName = book.BookSeries?.Name,
            Format = book.Format,
            AddedTime = book.AddedTime,
            LastOpenedTime = book.LastOpenedTime,
            Finished = book.Finished,
            // Reuses SeriesCardSample's deterministic per-name gradient palette rather than a
            // second copy of the same hash-to-color logic - purely a visual placeholder helper,
            // not a data coupling between the comic and book schemas.
            CoverBrush = SeriesCardSample.CoverBrushFor(book.Title),
            CoverImage = BookCoverImageCache.Get(book.Id),
        };
    }
}
