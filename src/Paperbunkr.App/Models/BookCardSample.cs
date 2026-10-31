using System;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Books grid card (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §1) - simpler
/// than <see cref="SeriesCardSample"/>. An <see cref="ObservableObject"/> / <see cref="ISelectableCard"/>
/// since B3 (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md) so
/// <see cref="IsSelected"/> can drive the grid's selection checkboxes via
/// <see cref="Paperbunkr.App.Services.TileSelectionController{T}"/>; every other field stays init-only.
/// </summary>
public sealed partial class BookCardSample : ObservableObject, ISelectableCard, ISpineSource, ICoverKeyProvider, ITileProgressSource
{
    public int BookId { get; init; }

    /// <summary><see cref="ISelectableCard"/> - the grid selects by book id.</summary>
    public int Id => BookId;

    [ObservableProperty]
    private bool _isSelected;

    public string Title { get; init; } = string.Empty;

    public string? Author { get; init; }

    public string? SeriesName { get; init; }

    /// <summary>The owning <see cref="BookSeries"/> id, or null for a standalone book - lets the
    /// grouped Books grid route a "Series" section header to that series' Details view
    /// (docs/superpowers/specs/2026-08-27-book-details-screen-design.md).</summary>
    public int? BookSeriesId { get; init; }

    public bool HasSeries => !string.IsNullOrWhiteSpace(SeriesName);

    public BookFormat Format { get; init; }

    /// <summary>Sort keys for the Books screen chrome (docs/superpowers/specs/2026-08-27-books-
    /// screen-chrome-and-home-strip-design.md).</summary>
    public DateTime AddedTime { get; init; }

    public DateTime? LastOpenedTime { get; init; }

    /// <summary>Read to the end at least once - carried for a future dim/badge (Piece B); not
    /// rendered on the grid in Piece A.</summary>
    public bool Finished { get; init; }

    // --- Books slice 2a (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md): browse axes carried on the card ---

    public BookReadingStatus Status { get; init; }

    public float? Rating { get; init; }

    /// <summary>0-1, <see cref="Paperbunkr.Data.Books.BookProgress.Compute"/>.</summary>
    public double Progress { get; init; }

    public string? Publisher { get; init; }

    public string? Language { get; init; }

    public DateTime? PublishedDate { get; init; }

    public float? SeriesIndex { get; init; }

    public int? CharacterCount { get; init; }

    /// <summary>Genre then Tags values, for search; <see cref="FirstGenre"/> is the grouping key.</summary>
    public string? FirstGenre { get; init; }

    /// <summary>A "#3 missing" placeholder between owned numbers in a series. Never selectable, clickable or counted.</summary>
    public bool IsGhost { get; init; }

    // --- Bookshelf view (the Library's spine tile reads these) ---

    public double SpineWidth => BookSpineWidth.For(CharacterCount, Format);

    public double SpineHeight => SpineMetrics.HeightFor(BookId);

    public string SpineTitle => Title;

    public string? SpineNumber => SeriesIndex is float index ? $"#{index:0.##}" : null;

    public bool IsSpineGhost => IsGhost;

    public string? CoverKey => SpineColorCache.BookKeyPrefix + BookId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public double ReadFraction => Progress;

    public bool IsFinished => Status == BookReadingStatus.Finished;

    public bool IsRightToLeft => false;

    /// <summary>The Library's one-badge rule: a progress ring while Reading, a check when Finished, nothing otherwise.</summary>
    public bool ShowProgressRing => Status == BookReadingStatus.Reading && Progress > 0d;

    public bool ShowFinishedCheck => Status == BookReadingStatus.Finished;

    public bool HasRating => Rating is > 0f;

    /// <summary>Estimated time to finish (Slice 2b's pace), filled by the Books screen for Reading books; null when it can't be told.</summary>
    public TimeSpan? TimeLeft { get; init; }

    /// <summary>The cover's bottom-edge progress bar (one-badge rule, docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2).</summary>
    public bool ShowProgressBar => Status == BookReadingStatus.Reading && Progress > 0d;

    /// <summary>The cover's single corner badge when no progress bar shows.</summary>
    public BookStatusBadge StatusBadge => ShowProgressBar ? BookStatusBadge.None : Status switch
    {
        BookReadingStatus.Finished => BookStatusBadge.Finished,
        BookReadingStatus.WantToRead => BookStatusBadge.WantToRead,
        BookReadingStatus.Abandoned => BookStatusBadge.Abandoned,
        _ => BookStatusBadge.None,
    };

    public bool IsFinishedBadge => StatusBadge == BookStatusBadge.Finished;

    public bool IsWantToReadBadge => StatusBadge == BookStatusBadge.WantToRead;

    public bool IsAbandonedBadge => StatusBadge == BookStatusBadge.Abandoned;

    /// <summary>The line under the author: "37% · 2h left", "Finished · rated 4.5", "Want to read", "Abandoned", or the series for an
    /// unread book. Empty when there is nothing to say.</summary>
    public string StatusCaption => Status switch
    {
        BookReadingStatus.Reading when Progress > 0d => TimeLeft is { } left && left > TimeSpan.Zero
            ? $"{Math.Round(Progress * 100):0}% · {Services.Books.BooksSpreadResolver.FormatDuration(left)} left"
            : $"{Math.Round(Progress * 100):0}%",
        BookReadingStatus.Reading => "Reading",
        BookReadingStatus.Finished => Rating is float r && r > 0f ? $"Finished · rated {r:0.#}" : "Finished",
        BookReadingStatus.WantToRead => "Want to read",
        BookReadingStatus.Abandoned => "Abandoned",
        _ => HasSeries ? (SeriesIndex is float i ? $"{SeriesName} #{i:0.##}" : SeriesName ?? string.Empty) : string.Empty,
    };

    public bool HasStatusCaption => StatusCaption.Length > 0;

    /// <summary>The <c>StarRating</c> control's value.</summary>
    public double? RatingValue => Rating is > 0f ? Rating : null;

    public string GhostLabel { get; init; } = string.Empty;

    public Avalonia.Media.IBrush CoverBrush { get; init; } = SeriesCardSample.CoverBrushFor(string.Empty);

    /// <summary>Real generated cover (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §3), null until "Generate Covers" has processed this book.</summary>
    public Bitmap? CoverImage { get; init; }

    /// <summary>The "#3 missing" placeholder (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §6): not a book, so BookId is 0.</summary>
    public static BookCardSample Ghost(int? bookSeriesId, string seriesName, int number) => new()
    {
        BookId = 0,
        Title = $"#{number} missing",
        SeriesName = seriesName,
        BookSeriesId = bookSeriesId,
        SeriesIndex = number,
        IsGhost = true,
        GhostLabel = $"#{number}",
    };

    public static BookCardSample FromBook(Book book, TimeSpan? timeLeft = null)
    {
        return new BookCardSample
        {
            TimeLeft = timeLeft,
            BookId = book.Id,
            Title = book.Title,
            Author = book.Author,
            SeriesName = book.BookSeries?.Name,
            BookSeriesId = book.BookSeriesId,
            Format = book.Format,
            AddedTime = book.AddedTime,
            LastOpenedTime = book.LastOpenedTime,
            Finished = book.Finished,
            Status = book.Status,
            Rating = book.Rating,
            Progress = Paperbunkr.Data.Books.BookProgress.Compute(book),
            Publisher = book.Publisher,
            Language = book.LanguageISO,
            PublishedDate = book.PublishedDate,
            SeriesIndex = book.SeriesIndex,
            CharacterCount = book.CharacterCount,
            FirstGenre = book.Tags.Where(t => t.Field == BookTagField.Genre).OrderBy(t => t.Id).Select(t => t.Value).FirstOrDefault(),
            // Reuses SeriesCardSample's deterministic per-name gradient palette rather than a
            // second copy of the same hash-to-color logic - purely a visual placeholder helper,
            // not a data coupling between the comic and book schemas.
            CoverBrush = SeriesCardSample.CoverBrushFor(book.Title),
            CoverImage = BookCoverImageCache.Get(book.Id, book.FilePath),
        };
    }
}
