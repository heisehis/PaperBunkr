using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Books;

/// <summary>Field names used in <see cref="BookFieldSource.Field"/> and in the applier's change lists.</summary>
public static class BookFields
{
    public const string Subtitle = "Subtitle";
    public const string Publisher = "Publisher";
    public const string Language = "LanguageISO";
    public const string Isbn = "ISBN";
    public const string Series = "BookSeries";
    public const string SeriesIndex = "SeriesIndex";
    public const string Summary = "Summary";
    public const string Rating = "Rating";
    public const string CommunityRating = "CommunityRating";
    public const string Genre = "Genre";
    public const string Tags = "Tags";
    public const string Status = "Status";
}

/// <summary>A source-neutral bundle of descriptive values (from a file or a provider) that <see cref="BookMetadataApplier"/> can apply to a <see cref="Book"/>.</summary>
public sealed class BookFieldValues
{
    public string? Subtitle { get; init; }

    public string? Publisher { get; init; }

    public string? LanguageISO { get; init; }

    public string? ISBN { get; init; }

    public string? SeriesName { get; init; }

    public float? SeriesIndex { get; init; }

    public string? Summary { get; init; }

    public float? Rating { get; init; }

    public float? CommunityRating { get; init; }

    public int? CommunityRatingCount { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public static BookFieldValues FromFile(BookMetadata m) => new()
    {
        Subtitle = m.Subtitle,
        Publisher = m.Publisher,
        LanguageISO = m.LanguageISO,
        ISBN = m.ISBN,
        SeriesName = m.SeriesName,
        SeriesIndex = m.SeriesIndex,
        Summary = m.Description,
        Rating = m.Rating,
        Genres = m.Genres,
        Tags = m.Tags,
    };
}

public enum BookApplyMode
{
    /// <summary>Only fill fields that are empty and not marked <see cref="BookFieldSourceKind.Manual"/>. Never overwrites. Used by import, the file refresh and bulk matching.</summary>
    FillEmpty,

    /// <summary>The user explicitly accepted these values: overwrite (ignoring Manual protection), limited to <c>onlyFields</c> when given.</summary>
    Override,
}

/// <summary>
/// Applies descriptive metadata to a <see cref="Book"/> and records where each value came from
/// (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §3.4, §4). One implementation shared by import,
/// the "Refresh from files" job and the provider match dialog, so the "never overwrite a user edit" rule lives in one place.
/// </summary>
public static class BookMetadataApplier
{
    /// <param name="resolveSeries">Find-or-create the <see cref="BookSeries"/> with this name (the caller owns its cache and context).</param>
    /// <param name="onlyFields">When given, only these <see cref="BookFields"/> are considered.</param>
    /// <returns>The <see cref="BookFields"/> that changed, in application order.</returns>
    public static IReadOnlyList<string> Apply(
        Book book,
        BookFieldValues values,
        BookFieldSourceKind source,
        string? sourceRef,
        DateTime nowUtc,
        Func<string, BookSeries> resolveSeries,
        BookApplyMode mode = BookApplyMode.FillEmpty,
        ISet<string>? onlyFields = null)
    {
        var changed = new List<string>();

        bool Allowed(string field, bool currentEmpty)
        {
            if (onlyFields is not null && !onlyFields.Contains(field))
            {
                return false;
            }

            return mode == BookApplyMode.Override || (currentEmpty && !IsManual(book, field));
        }

        void SetString(string field, string? current, string? incoming, Action<string> set)
        {
            if (string.IsNullOrWhiteSpace(incoming))
            {
                return;
            }

            string value = incoming.Trim();
            if (!Allowed(field, string.IsNullOrWhiteSpace(current)) || string.Equals(current, value, StringComparison.Ordinal))
            {
                return;
            }

            set(value);
            Record(book, field, source, sourceRef, nowUtc);
            changed.Add(field);
        }

        SetString(BookFields.Subtitle, book.Subtitle, values.Subtitle, v => book.Subtitle = v);
        SetString(BookFields.Publisher, book.Publisher, values.Publisher, v => book.Publisher = v);
        SetString(BookFields.Language, book.LanguageISO, values.LanguageISO, v => book.LanguageISO = v);
        SetString(BookFields.Isbn, book.ISBN, values.ISBN, v => book.ISBN = v);
        SetString(BookFields.Summary, book.Summary, values.Summary, v => book.Summary = v);

        // Series membership, then the index within it.
        bool seriesJustSet = false;
        if (!string.IsNullOrWhiteSpace(values.SeriesName))
        {
            string name = values.SeriesName.Trim();
            bool hasSeries = book.BookSeriesId is not null || book.BookSeries is not null;
            bool sameSeries = hasSeries && string.Equals(book.BookSeries?.Name, name, StringComparison.OrdinalIgnoreCase);
            if (!sameSeries && Allowed(BookFields.Series, !hasSeries))
            {
                var series = resolveSeries(name);
                book.BookSeries = series;
                if (!series.Books.Contains(book))
                {
                    series.Books.Add(book);
                }

                Record(book, BookFields.Series, source, sourceRef, nowUtc);
                changed.Add(BookFields.Series);
                seriesJustSet = true;
            }
        }

        if (values.SeriesIndex is float index)
        {
            bool matchesSeries = values.SeriesName is null
                || seriesJustSet
                || string.Equals(book.BookSeries?.Name, values.SeriesName.Trim(), StringComparison.OrdinalIgnoreCase);
            if (matchesSeries && book.BookSeries is not null && Allowed(BookFields.SeriesIndex, book.SeriesIndex is null) && book.SeriesIndex != index)
            {
                book.SeriesIndex = index;
                Record(book, BookFields.SeriesIndex, source, sourceRef, nowUtc);
                changed.Add(BookFields.SeriesIndex);
            }
        }

        if (BookRating.Normalize(values.Rating) is float rating && Allowed(BookFields.Rating, book.Rating is null) && book.Rating != rating)
        {
            book.Rating = rating;
            Record(book, BookFields.Rating, source, sourceRef, nowUtc);
            changed.Add(BookFields.Rating);
        }

        if (values.CommunityRating is float community && community > 0f
            && Allowed(BookFields.CommunityRating, book.CommunityRating is null) && book.CommunityRating != community)
        {
            book.CommunityRating = community;
            book.CommunityRatingCount = values.CommunityRatingCount;
            Record(book, BookFields.CommunityRating, source, sourceRef, nowUtc);
            changed.Add(BookFields.CommunityRating);
        }

        // Tags are additive in both modes (never removes), and a Manual tag edit stops FillEmpty from re-adding.
        MergeTags(book, BookTagField.Genre, BookFields.Genre, values.Genres, source, sourceRef, nowUtc, mode, onlyFields, changed);
        MergeTags(book, BookTagField.Tags, BookFields.Tags, values.Tags, source, sourceRef, nowUtc, mode, onlyFields, changed);

        return changed;
    }

    private static void MergeTags(
        Book book, BookTagField tagField, string fieldName, IReadOnlyList<string> incoming, BookFieldSourceKind source,
        string? sourceRef, DateTime nowUtc, BookApplyMode mode, ISet<string>? onlyFields, List<string> changed)
    {
        if (incoming.Count == 0 || (onlyFields is not null && !onlyFields.Contains(fieldName)))
        {
            return;
        }

        if (mode == BookApplyMode.FillEmpty && IsManual(book, fieldName))
        {
            return;
        }

        bool added = false;
        foreach (string raw in incoming)
        {
            string value = raw.Trim();
            if (value.Length == 0 || book.Tags.Any(t => t.Field == tagField && string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            book.Tags.Add(new BookTag { Field = tagField, Value = value });
            added = true;
        }

        if (added)
        {
            Record(book, fieldName, source, sourceRef, nowUtc);
            changed.Add(fieldName);
        }
    }

    public static bool IsManual(Book book, string field) =>
        book.FieldSources.Any(s => s.Field == field && s.Source == BookFieldSourceKind.Manual);

    /// <summary>Upserts the provenance row for one field.</summary>
    public static void Record(Book book, string field, BookFieldSourceKind source, string? sourceRef, DateTime nowUtc)
    {
        var row = book.FieldSources.FirstOrDefault(s => s.Field == field);
        if (row is null)
        {
            book.FieldSources.Add(new BookFieldSource { Field = field, Source = source, FetchedUtc = nowUtc, SourceRef = sourceRef });
            return;
        }

        row.Source = source;
        row.FetchedUtc = nowUtc;
        row.SourceRef = sourceRef;
    }

    /// <summary>A user edit: marks each field Manual so no file refresh or bulk match will overwrite it.</summary>
    public static void MarkManual(Book book, IEnumerable<string> fields, DateTime nowUtc)
    {
        foreach (string field in fields)
        {
            Record(book, field, BookFieldSourceKind.Manual, null, nowUtc);
        }
    }
}
