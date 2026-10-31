using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>Display names for <see cref="BookReadingStatus"/> (the editors' closed Status drop-down) and back.</summary>
public static class BookStatusLabels
{
    public static IReadOnlyList<string> All { get; } = new[] { "Unread", "Want to read", "Reading", "Finished", "Abandoned" };

    public static string ToLabel(BookReadingStatus status) => status switch
    {
        BookReadingStatus.WantToRead => "Want to read",
        _ => status.ToString(),
    };

    public static bool TryParse(string? label, out BookReadingStatus status)
    {
        foreach (var candidate in Enum.GetValues<BookReadingStatus>())
        {
            if (string.Equals(ToLabel(candidate), label?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = BookReadingStatus.Unread;
        return false;
    }
}

/// <summary>Conversions between a book's tag rows and the comma-separated text the multi-value <c>SuggestBox</c> edits.</summary>
public static class BookTagText
{
    public static IReadOnlyList<string> Split(string? text) => BookMetadataParsing.SplitKeywords(text);

    public static string Join(IEnumerable<string> values) =>
        string.Join(", ", values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));

    public static string For(Book book, BookTagField field) =>
        Join(book.Tags.Where(t => t.Field == field).Select(t => t.Value));

    /// <summary>Makes the book's <paramref name="field"/> tags exactly <paramref name="wanted"/> (case-insensitive), keeping the casing already stored.</summary>
    public static void Set(Book book, BookTagField field, IReadOnlyList<string> wanted)
    {
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

    /// <summary>Adds <paramref name="add"/> and removes <paramref name="remove"/> without touching the rest (bulk editing).</summary>
    public static void AddRemove(Book book, BookTagField field, IReadOnlyList<string> add, IReadOnlyList<string> remove)
    {
        foreach (var stale in book.Tags.Where(t => t.Field == field && remove.Contains(t.Value, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            book.Tags.Remove(stale);
        }

        foreach (string value in add)
        {
            if (!book.Tags.Any(t => t.Field == field && string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                book.Tags.Add(new BookTag { Field = field, Value = value });
            }
        }
    }
}

/// <summary>Type-ahead lists for the book editors, built from what the library already holds.</summary>
public sealed record BookVocabulary(
    IReadOnlyList<string> Publishers,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> SeriesNames)
{
    public static BookVocabulary Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    public static BookVocabulary Load(PaperbunkrDbContext context)
    {
        static IReadOnlyList<string> Sorted(IEnumerable<string?> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

        var tags = context.BookTags.Select(t => new { t.Field, t.Value }).Distinct().ToList();
        var languages = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Select(c => c.TwoLetterISOLanguageName)
            .Concat(context.Books.Select(b => b.LanguageISO).Where(l => l != null).Distinct().ToList().Select(l => l!));

        return new BookVocabulary(
            Sorted(context.Books.Select(b => b.Publisher).Distinct().ToList()),
            Sorted(tags.Where(t => t.Field == BookTagField.Genre).Select(t => t.Value)),
            Sorted(tags.Where(t => t.Field == BookTagField.Tags).Select(t => t.Value)),
            Sorted(languages),
            Sorted(context.BookSeries.Select(s => s.Name).ToList()));
    }
}
