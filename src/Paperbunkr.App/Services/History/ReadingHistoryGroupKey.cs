using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.History;

/// <summary>What one Insights History row stands for (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md §2).</summary>
public enum ReadingHistoryGroupKind
{
    /// <summary>A comic/manga <see cref="Series"/>; <see cref="ReadingHistoryGroupKey.Id"/> is <see cref="Series.Id"/>.</summary>
    ComicSeries,

    /// <summary>A <see cref="Data.Entities.BookSeries"/>; the id is <see cref="Data.Entities.BookSeries.Id"/>.</summary>
    BookSeries,

    /// <summary>A standalone <see cref="Data.Entities.Book"/>; the id is <see cref="Data.Entities.Book.Id"/>.</summary>
    Book,
}

/// <summary>
/// Identity of a History row - one per comic series, book series or standalone book. The kind is part of
/// the key because comic and book series ids share one integer space in <see cref="ReadingEvent.SeriesId"/>.
/// </summary>
public sealed record ReadingHistoryGroupKey(ReadingHistoryGroupKind Kind, int Id)
{
    /// <summary>The group a <see cref="ReadingEvent"/> belongs to, from its frozen columns.</summary>
    public static ReadingHistoryGroupKey For(ReadingItemType itemType, int itemId, int? seriesId) => itemType switch
    {
        ReadingItemType.Comic => new ReadingHistoryGroupKey(ReadingHistoryGroupKind.ComicSeries, seriesId ?? 0),
        _ when seriesId is int bookSeriesId => new ReadingHistoryGroupKey(ReadingHistoryGroupKind.BookSeries, bookSeriesId),
        _ => new ReadingHistoryGroupKey(ReadingHistoryGroupKind.Book, itemId),
    };
}
