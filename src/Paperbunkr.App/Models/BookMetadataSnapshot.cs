using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// The undo/redo snapshot of a single <see cref="Book"/>'s editable metadata
/// (docs/superpowers/specs/2026-08-27-book-properties-editor-design.md) - the book-row equivalent of
/// <see cref="Paperbunkr.App.Services.MetadataEditHistoryService.CaptureSnapshot"/> for issues.
///
/// Covers the fields the Book Properties overlay writes onto the <see cref="Book"/> row itself: title, author, summary, published date,
/// series *membership* (<see cref="Book.BookSeriesId"/>), and (Books slice 1) subtitle, publisher, language, ISBN, series index, rating,
/// reading status and the Genre / Tags sets. Series-level Author/SortName edits and cover changes are out of scope for undo/redo (a shared-row
/// edit and a file-cache write respectively), and so are the provenance rows.
///
/// The tag keys are only captured when the caller loaded <see cref="Book.Tags"/> (<c>Include(b =&gt; b.Tags)</c>); a snapshot without them
/// leaves tags alone on <see cref="Apply"/>, so a capture from a book whose tags were not loaded can never wipe them.
/// </summary>
public static class BookMetadataSnapshot
{
    private const string TitleKey = "Title";
    private const string AuthorKey = "Author";
    private const string SummaryKey = "Summary";
    private const string PublishedDateKey = "PublishedDate";
    private const string BookSeriesIdKey = "BookSeriesId";
    private const string SubtitleKey = "Subtitle";
    private const string PublisherKey = "Publisher";
    private const string LanguageKey = "LanguageISO";
    private const string IsbnKey = "ISBN";
    private const string SeriesIndexKey = "SeriesIndex";
    private const string RatingKey = "Rating";
    private const string StatusKey = "Status";
    private const string GenreKey = "Genre";
    private const string TagsKey = "Tags";

    private const char TagSeparator = '\u001f';

    public static Dictionary<string, string?> Capture(Book book, bool includeTags = true)
    {
        var snapshot = new Dictionary<string, string?>
        {
            [TitleKey] = book.Title,
            [AuthorKey] = book.Author,
            [SummaryKey] = book.Summary,
            [PublishedDateKey] = book.PublishedDate?.ToString("O", CultureInfo.InvariantCulture),
            [BookSeriesIdKey] = book.BookSeriesId?.ToString(CultureInfo.InvariantCulture),
            [SubtitleKey] = book.Subtitle,
            [PublisherKey] = book.Publisher,
            [LanguageKey] = book.LanguageISO,
            [IsbnKey] = book.ISBN,
            [SeriesIndexKey] = book.SeriesIndex?.ToString("R", CultureInfo.InvariantCulture),
            [RatingKey] = book.Rating?.ToString("R", CultureInfo.InvariantCulture),
            [StatusKey] = book.Status.ToString(),
        };

        if (includeTags)
        {
            snapshot[GenreKey] = JoinTags(book, BookTagField.Genre);
            snapshot[TagsKey] = JoinTags(book, BookTagField.Tags);
        }

        return snapshot;
    }

    public static void Apply(Book book, IReadOnlyDictionary<string, string?> snapshot)
    {
        if (snapshot.TryGetValue(TitleKey, out var title))
        {
            book.Title = title ?? string.Empty;
        }

        if (snapshot.TryGetValue(AuthorKey, out var author))
        {
            book.Author = author;
        }

        if (snapshot.TryGetValue(SummaryKey, out var summary))
        {
            book.Summary = summary;
        }

        if (snapshot.TryGetValue(PublishedDateKey, out var published))
        {
            book.PublishedDate = string.IsNullOrEmpty(published)
                ? null
                : DateTime.Parse(published, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (snapshot.TryGetValue(BookSeriesIdKey, out var seriesId))
        {
            book.BookSeriesId = string.IsNullOrEmpty(seriesId)
                ? null
                : int.Parse(seriesId, CultureInfo.InvariantCulture);
        }

        if (snapshot.TryGetValue(SubtitleKey, out var subtitle))
        {
            book.Subtitle = subtitle;
        }

        if (snapshot.TryGetValue(PublisherKey, out var publisher))
        {
            book.Publisher = publisher;
        }

        if (snapshot.TryGetValue(LanguageKey, out var language))
        {
            book.LanguageISO = language;
        }

        if (snapshot.TryGetValue(IsbnKey, out var isbn))
        {
            book.ISBN = isbn;
        }

        if (snapshot.TryGetValue(SeriesIndexKey, out var index))
        {
            book.SeriesIndex = string.IsNullOrEmpty(index) ? null : float.Parse(index, CultureInfo.InvariantCulture);
        }

        if (snapshot.TryGetValue(RatingKey, out var rating))
        {
            book.Rating = string.IsNullOrEmpty(rating) ? null : float.Parse(rating, CultureInfo.InvariantCulture);
        }

        if (snapshot.TryGetValue(StatusKey, out var status) && Enum.TryParse<BookReadingStatus>(status, out var parsed))
        {
            book.Status = parsed;
        }

        if (snapshot.TryGetValue(GenreKey, out var genres))
        {
            SetTags(book, BookTagField.Genre, genres);
        }

        if (snapshot.TryGetValue(TagsKey, out var tags))
        {
            SetTags(book, BookTagField.Tags, tags);
        }
    }

    private static string JoinTags(Book book, BookTagField field) =>
        string.Join(TagSeparator, book.Tags.Where(t => t.Field == field).OrderBy(t => t.Value, StringComparer.OrdinalIgnoreCase).Select(t => t.Value));

    private static void SetTags(Book book, BookTagField field, string? joined)
    {
        var wanted = (joined ?? string.Empty).Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var stale in book.Tags.Where(t => t.Field == field && !wanted.Contains(t.Value, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            book.Tags.Remove(stale);
        }

        foreach (string value in wanted)
        {
            if (!book.Tags.Any(t => t.Field == field && string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                book.Tags.Add(new BookTag { Field = field, Value = value });
            }
        }
    }
}
