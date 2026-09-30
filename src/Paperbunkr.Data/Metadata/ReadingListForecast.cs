using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>A reading list's projected finish date at the reader's recent pace, and how many of its unread issues are not in the library yet.</summary>
public sealed record ReadingListForecastResult(DateTime FinishByUtc, int MissingUnread);

/// <summary>
/// Per-list completion forecast (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §5). The pace is the
/// whole library's: comics finished in the last <see cref="WindowDays"/> days, re-reads included (they took reading time - the
/// forecast is about throughput). No ComicRack CE precedent; CE only counts read/unread per list.
/// </summary>
public static class ReadingListForecast
{
    public const int WindowDays = 90;

    /// <summary>Below this many finished comics in the window the pace is too thin to project from, so no forecast is shown.</summary>
    public const int MinimumFinished = 5;

    /// <summary>Null when there is too little data or nothing is left to read.</summary>
    public static ReadingListForecastResult? Compute(IReadOnlyCollection<DateTime> finishedComicUtc, int unreadCount, int missingUnreadCount, DateTime nowUtc)
    {
        if (unreadCount <= 0)
        {
            return null;
        }

        var windowStart = nowUtc.AddDays(-WindowDays);
        int finished = finishedComicUtc.Count(t => t > windowStart && t <= nowUtc);
        if (finished < MinimumFinished)
        {
            return null;
        }

        double perDay = finished / (double)WindowDays;
        return new ReadingListForecastResult(nowUtc.AddDays(unreadCount / perDay), missingUnreadCount);
    }

    /// <summary>Timestamps of every comic <see cref="ReadingEventKind.Finished"/> event in the window - the input to <see cref="Compute"/>.</summary>
    public static IReadOnlyList<DateTime> LoadFinishedComicTimestamps(PaperbunkrDbContext context, DateTime nowUtc)
    {
        var windowStart = nowUtc.AddDays(-WindowDays);
        return context.ReadingEvents
            .Where(e => e.Kind == ReadingEventKind.Finished && e.ItemType == ReadingItemType.Comic && e.TimestampUtc > windowStart)
            .Select(e => e.TimestampUtc)
            .ToList();
    }

    /// <summary>"~Nov 2026", or "~in 5 days" / "~in 3 weeks" within 31 days.</summary>
    public static string FormatFinishBy(DateTime finishByUtc, DateTime nowUtc)
    {
        double days = (finishByUtc - nowUtc).TotalDays;
        if (days <= 31)
        {
            int d = Math.Max(1, (int)Math.Ceiling(days));
            if (d < 14)
            {
                return d == 1 ? "~tomorrow" : $"~in {d} days";
            }

            return $"~in {(int)Math.Round(d / 7.0)} weeks";
        }

        return "~" + finishByUtc.ToLocalTime().ToString("MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }
}
