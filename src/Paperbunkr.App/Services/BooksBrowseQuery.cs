using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>Everything that decides which books the Books screen shows and in what order.</summary>
/// <param name="ShelfIds">Non-null while a shelf (built-in, smart list or collection) is chosen: only these book ids pass.</param>
/// <param name="Author">Non-null while an "Author: X" chip is active (case-insensitive exact match).</param>
public sealed record BooksBrowseOptions(
    BooksStatusTab Tab = BooksStatusTab.All,
    string Search = "",
    IReadOnlySet<int>? ShelfIds = null,
    string? Author = null,
    BooksSortField Sort = BooksSortField.Title,
    SortDirection Direction = SortDirection.Ascending,
    BooksGroupField Group = BooksGroupField.None);

/// <summary>
/// The Books screen's filter, sort and grouping as pure functions over <see cref="BookCardSample"/>
/// (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §2-3). The original four sort axes and the Series/Author
/// grouping behave exactly as they did inside <c>BooksScreenViewModel</c>; the new axes put books without a value last in both directions.
/// </summary>
public static class BooksBrowseQuery
{
    public const string Standalone = "Standalone";
    public const string UnknownAuthor = "Unknown author";
    public const string UnknownPublisher = "Unknown publisher";
    public const string UnknownLanguage = "Unknown language";
    public const string NoGenre = "No genre";

    public static bool InTab(BookCardSample card, BooksStatusTab tab) => tab switch
    {
        BooksStatusTab.All => true,
        BooksStatusTab.Reading => card.Status == BookReadingStatus.Reading,
        BooksStatusTab.WantToRead => card.Status == BookReadingStatus.WantToRead,
        BooksStatusTab.Unread => card.Status == BookReadingStatus.Unread,
        BooksStatusTab.Finished => card.Status == BookReadingStatus.Finished,
        BooksStatusTab.Abandoned => card.Status == BookReadingStatus.Abandoned,
        _ => true,
    };

    /// <summary>Counts per tab over all books (not search-filtered, so the tabs don't jitter while typing). Ghost cards never count.</summary>
    public static IReadOnlyDictionary<BooksStatusTab, int> TabCounts(IEnumerable<BookCardSample> cards)
    {
        var real = cards.Where(c => !c.IsGhost).ToList();
        return Enum.GetValues<BooksStatusTab>().ToDictionary(t => t, t => real.Count(c => InTab(c, t)));
    }

    public static IReadOnlyList<BookCardSample> Filter(IEnumerable<BookCardSample> cards, BooksBrowseOptions options)
    {
        IEnumerable<BookCardSample> result = cards.Where(c => !c.IsGhost && InTab(c, options.Tab));

        if (options.ShelfIds is not null)
        {
            result = result.Where(c => options.ShelfIds.Contains(c.BookId));
        }

        if (!string.IsNullOrWhiteSpace(options.Author))
        {
            result = result.Where(c => string.Equals(c.Author?.Trim(), options.Author.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        string query = options.Search.Trim();
        if (query.Length > 0)
        {
            result = result.Where(c => Contains(c.Title, query) || Contains(c.Author, query) || Contains(c.SeriesName, query));
        }

        return result.ToList();
    }

    public static IReadOnlyList<BookCardSample> Sort(IEnumerable<BookCardSample> cards, BooksSortField field, SortDirection direction)
    {
        var list = cards.ToList();

        // New axes: books without a value go last whichever way the sort runs.
        if (field is BooksSortField.Rating or BooksSortField.Progress or BooksSortField.Publisher or BooksSortField.Published or BooksSortField.SeriesOrder)
        {
            return SortWithMissingLast(list, field, direction);
        }

        IOrderedEnumerable<BookCardSample> ordered = field switch
        {
            BooksSortField.Author => list.OrderBy(c => c.Author ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            BooksSortField.RecentlyAdded => list.OrderBy(c => c.AddedTime),
            BooksSortField.LastOpened => list.OrderBy(c => c.LastOpenedTime ?? DateTime.MinValue),
            _ => list.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
        };

        // Stable secondary key so equal primaries (e.g. two books with no author) don't shuffle.
        ordered = ordered.ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase);

        return (direction == SortDirection.Descending ? ordered.Reverse() : ordered).ToList();
    }

    private static IReadOnlyList<BookCardSample> SortWithMissingLast(List<BookCardSample> list, BooksSortField field, SortDirection direction)
    {
        bool desc = direction == SortDirection.Descending;

        IComparable? KeyOf(BookCardSample c) => field switch
        {
            BooksSortField.Rating => c.Rating is > 0f ? c.Rating : null,
            BooksSortField.Progress => c.Progress > 0d ? c.Progress : null,
            BooksSortField.Publisher => string.IsNullOrWhiteSpace(c.Publisher) ? null : c.Publisher!.Trim().ToLowerInvariant(),
            BooksSortField.Published => c.PublishedDate,
            BooksSortField.SeriesOrder => c.SeriesIndex,
            _ => null,
        };

        var withValue = list.Select(c => (Card: c, Key: KeyOf(c))).Where(x => x.Key is not null).ToList();
        var without = list.Where(c => KeyOf(c) is null).OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase);

        IOrderedEnumerable<(BookCardSample Card, IComparable? Key)> sorted = desc
            ? withValue.OrderByDescending(x => x.Key)
            : withValue.OrderBy(x => x.Key);

        // Series order groups by series name first so a "series order" list reads series after series.
        if (field == BooksSortField.SeriesOrder)
        {
            sorted = (desc
                ? withValue.OrderBy(x => x.Card.SeriesName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenByDescending(x => x.Key)
                : withValue.OrderBy(x => x.Card.SeriesName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key));
        }

        return sorted.ThenBy(x => x.Card.Title, StringComparer.OrdinalIgnoreCase).Select(x => x.Card).Concat(without).ToList();
    }

    private static readonly BookReadingStatus[] StatusOrder =
    {
        BookReadingStatus.Reading, BookReadingStatus.WantToRead, BookReadingStatus.Unread, BookReadingStatus.Finished, BookReadingStatus.Abandoned,
    };

    public static string StatusHeader(BookReadingStatus status) => status switch
    {
        BookReadingStatus.WantToRead => "Want to read",
        _ => status.ToString(),
    };

    public static string LanguageName(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return UnknownLanguage;
        }

        try
        {
            return CultureInfo.GetCultureInfo(iso.Trim()).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return iso.Trim();
        }
    }

    public static string GroupKey(BookCardSample c, BooksGroupField group) => group switch
    {
        BooksGroupField.Series => string.IsNullOrWhiteSpace(c.SeriesName) ? Standalone : c.SeriesName!,
        BooksGroupField.Status => StatusHeader(c.Status),
        BooksGroupField.Publisher => string.IsNullOrWhiteSpace(c.Publisher) ? UnknownPublisher : c.Publisher!.Trim(),
        BooksGroupField.Language => LanguageName(c.Language),
        BooksGroupField.Genre => string.IsNullOrWhiteSpace(c.FirstGenre) ? NoGenre : c.FirstGenre!,
        _ => string.IsNullOrWhiteSpace(c.Author) ? UnknownAuthor : c.Author!,
    };

    private static bool IsFallback(string key) => key is Standalone or UnknownAuthor or UnknownPublisher or UnknownLanguage or NoGenre;

    /// <summary>Groups an already sorted list; books keep the flat order inside each group. Fallback buckets last.</summary>
    public static IReadOnlyList<BookCardGroup> Group(IReadOnlyList<BookCardSample> sorted, BooksGroupField group, BooksSortField sortField)
    {
        var groups = sorted.GroupBy(c => GroupKey(c, group)).ToList();

        IEnumerable<IGrouping<string, BookCardSample>> ordered;
        if (group == BooksGroupField.Status)
        {
            ordered = groups.OrderBy(g => Array.FindIndex(StatusOrder, s => StatusHeader(s) == g.Key));
        }
        else
        {
            ordered = sortField switch
            {
                BooksSortField.RecentlyAdded => groups.OrderBy(g => IsFallback(g.Key)).ThenByDescending(g => g.Max(c => c.AddedTime)),
                BooksSortField.LastOpened => groups.OrderBy(g => IsFallback(g.Key)).ThenByDescending(g => g.Max(c => c.LastOpenedTime ?? DateTime.MinValue)),
                _ => groups.OrderBy(g => IsFallback(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase),
            };
        }

        return ordered.Select(g => new BookCardGroup
        {
            Header = g.Key,
            // Only a real Series section carries an id its header can navigate to.
            BookSeriesId = group == BooksGroupField.Series && !IsFallback(g.Key)
                ? g.Select(c => c.BookSeriesId).FirstOrDefault(id => id is not null)
                : null,
            Items = new ObservableCollection<BookCardSample>(g),
        }).ToList();
    }

    /// <summary>Ghost cards for the holes in one series' numbering (empty when any book in it is unnumbered).</summary>
    public static IReadOnlyList<BookCardSample> GhostsFor(IReadOnlyCollection<BookCardSample> seriesBooks)
    {
        var real = seriesBooks.Where(c => !c.IsGhost).ToList();
        if (real.Count == 0)
        {
            return Array.Empty<BookCardSample>();
        }

        string name = real.Select(c => c.SeriesName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty;
        int? seriesId = real.Select(c => c.BookSeriesId).FirstOrDefault(id => id is not null);
        return BookSeriesGaps.Find(real.Select(c => c.SeriesIndex)).Select(n => BookCardSample.Ghost(seriesId, name, n)).ToList();
    }

    /// <summary>
    /// Adds each Series group's ghost cards, computed from <paramref name="allCards"/> (every owned book, so a filter can never make a book look missing).
    /// Under the Series-order ascending sort a ghost sits in its numeric slot; under any other sort the ghosts follow the group's books.
    /// </summary>
    public static void AddGhosts(IReadOnlyList<BookCardGroup> groups, IReadOnlyList<BookCardSample> allCards, BooksSortField sortField, SortDirection direction)
    {
        var bySeries = allCards.Where(c => !c.IsGhost && c.BookSeriesId is not null).GroupBy(c => c.BookSeriesId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var group in groups.Where(g => g.BookSeriesId is not null))
        {
            if (!bySeries.TryGetValue(group.BookSeriesId!.Value, out var owned))
            {
                continue;
            }

            foreach (var ghost in GhostsFor(owned))
            {
                int at = group.Items.Count;
                if (sortField == BooksSortField.SeriesOrder && direction == SortDirection.Ascending)
                {
                    int i = 0;
                    while (i < group.Items.Count && (group.Items[i].SeriesIndex ?? float.MaxValue) < ghost.SeriesIndex) i++;
                    at = i;
                }

                group.Items.Insert(at, ghost);
            }
        }
    }

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
