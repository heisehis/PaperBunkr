using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Books;

/// <param name="Sessions">How many times the book was opened.</param>
/// <param name="ActiveTime">Total active reading time across sessions that recorded it.</param>
/// <param name="CharsPerMinute">Reading pace from sessions long enough to count; null until at least two qualify.</param>
/// <param name="TimeLeft">Estimated time to finish, rounded to 5 minutes; null when it cannot be told honestly.</param>
public sealed record BookReadingStats(
    int Sessions,
    TimeSpan ActiveTime,
    DateTime? FirstReadUtc,
    DateTime? LastReadUtc,
    double? CharsPerMinute,
    TimeSpan? TimeLeft)
{
    public static BookReadingStats Empty { get; } = new(0, TimeSpan.Zero, null, null, null, null);

    public bool HasAny => Sessions > 0;
}

/// <summary>
/// The Book Detail reading dashboard's numbers (docs/superpowers/specs/2026-10-27-books-slice2b-detail-design.md §4), computed from a book's
/// <see cref="ReadingEvent"/> rows. Only <b>Opened</b> rows are sessions (a Finished row repeats the same session's pages). A session counts toward pace
/// only with at least a minute of active time and some pages read; the estimate needs two such sessions and a known length, so it is never shown on a guess.
/// </summary>
public static class BookReadingStatsResolver
{
    public const int MinSessionSeconds = 60;
    public const int MinSessionsForEstimate = 2;

    public static BookReadingStats Compute(IEnumerable<ReadingEvent> events, int? characterCount, double progress, bool finished)
    {
        var sessions = events
            .Where(e => e.ItemType == ReadingItemType.Novel && e.Kind == ReadingEventKind.Opened)
            .ToList();
        if (sessions.Count == 0)
        {
            return BookReadingStats.Empty;
        }

        long activeSeconds = sessions.Sum(s => (long)(s.ActiveSeconds ?? 0));

        var qualifying = sessions.Where(s => s.ActiveSeconds is >= MinSessionSeconds && s.PagesRead is > 0).ToList();
        double? pace = null;
        if (qualifying.Count >= MinSessionsForEstimate)
        {
            double minutes = qualifying.Sum(s => s.ActiveSeconds!.Value) / 60d;
            double chars = qualifying.Sum(s => (double)s.PagesRead!.Value) * ReadingPageMath.EpubCharsPerPage;
            pace = minutes > 0 ? chars / minutes : null;
        }

        TimeSpan? left = null;
        if (pace is > 0 && !finished && characterCount is > 0)
        {
            double remaining = characterCount.Value * (1d - Math.Clamp(progress, 0d, 1d));
            double minutesLeft = remaining / pace.Value;
            double rounded = Math.Round(minutesLeft / 5d, MidpointRounding.AwayFromZero) * 5d;
            if (rounded > 0)
            {
                left = TimeSpan.FromMinutes(rounded);
            }
        }

        return new BookReadingStats(
            sessions.Count,
            TimeSpan.FromSeconds(activeSeconds),
            sessions.Min(s => s.TimestampUtc),
            sessions.Max(s => s.TimestampUtc),
            pace,
            left);
    }
}
