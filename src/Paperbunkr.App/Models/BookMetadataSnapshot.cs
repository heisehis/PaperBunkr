using System;
using System.Collections.Generic;
using System.Globalization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// The undo/redo snapshot of a single <see cref="Book"/>'s editable metadata
/// (docs/superpowers/specs/2026-08-27-book-properties-editor-design.md) - the book-row equivalent of
/// <see cref="Paperbunkr.App.Services.MetadataEditHistoryService.CaptureSnapshot"/> for issues.
///
/// Deliberately scoped to the fields the Book Properties overlay writes onto the <see cref="Book"/>
/// row itself: title, author, summary, published date, and series *membership*
/// (<see cref="Book.BookSeriesId"/>). Series-level Author/SortName edits and cover changes are
/// out of scope for undo/redo (a shared-row edit and a file-cache write respectively) - see the
/// design doc's "Undo/redo" decision.
/// </summary>
public static class BookMetadataSnapshot
{
    private const string TitleKey = "Title";
    private const string AuthorKey = "Author";
    private const string SummaryKey = "Summary";
    private const string PublishedDateKey = "PublishedDate";
    private const string BookSeriesIdKey = "BookSeriesId";

    public static Dictionary<string, string?> Capture(Book book) => new()
    {
        [TitleKey] = book.Title,
        [AuthorKey] = book.Author,
        [SummaryKey] = book.Summary,
        [PublishedDateKey] = book.PublishedDate?.ToString("O", CultureInfo.InvariantCulture),
        [BookSeriesIdKey] = book.BookSeriesId?.ToString(CultureInfo.InvariantCulture),
    };

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
    }
}
