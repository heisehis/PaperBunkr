using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The reader's own pace, per item type, in seconds per page - or null where there is not enough history to say
/// (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.2). Everything that shows a time-left estimate asks this, so
/// "nothing is shown" is one decision made in one place.
/// </summary>
public sealed record ReadingPace(double? ComicSecondsPerPage, double? NovelSecondsPerPage)
{
    public static readonly ReadingPace Unknown = new(null, null);

    public double? For(ReadingItemType type) => type == ReadingItemType.Comic ? ComicSecondsPerPage : NovelSecondsPerPage;

    /// <summary>How long <paramref name="pagesLeft"/> pages should take, or null with no pace or nothing left.</summary>
    public TimeSpan? TimeFor(ReadingItemType type, int pagesLeft) =>
        pagesLeft > 0 && For(type) is double secondsPerPage ? TimeSpan.FromSeconds(pagesLeft * secondsPerPage) : null;

    /// <summary>Time left in one comic issue: the pages from the current one to the end.</summary>
    public TimeSpan? TimeLeftInIssue(Issue issue) =>
        issue.PageCount is > 0 && !issue.HasBeenRead()
            ? TimeFor(ReadingItemType.Comic, ReadingPaceResolver.PagesLeft(issue))
            : null;

    /// <summary>
    /// Time to finish a series: the pages left across its unread issues. Only offered once at least
    /// <see cref="ReadingPaceResolver.MinUnreadIssuesForSeriesEstimate"/> are unread - below that the per-issue estimate already says it.
    /// </summary>
    public TimeSpan? TimeToFinishSeries(IEnumerable<Issue> issues)
    {
        var unread = issues.Where(i => !i.IsPlaceholder && !i.HasBeenRead()).ToList();
        if (unread.Count < ReadingPaceResolver.MinUnreadIssuesForSeriesEstimate)
        {
            return null;
        }

        return TimeFor(ReadingItemType.Comic, unread.Sum(ReadingPaceResolver.PagesLeft));
    }
}

public static class ReadingPaceResolver
{
    /// <summary>The pace is the median of this many of the most recent qualifying sessions...</summary>
    public const int SampleSessions = 30;

    /// <summary>...a session qualifying only with at least this many pages...</summary>
    public const int MinPagesPerSession = 3;

    /// <summary>...and this much active time, so a glance at a cover never counts.</summary>
    public const int MinSecondsPerSession = 30;

    /// <summary>Below this many qualifying sessions there is no pace, and nothing shows an estimate.</summary>
    public const int MinSessions = 5;

    public const int MinUnreadIssuesForSeriesEstimate = 3;

    public static ReadingPace Load(PaperbunkrDbContext context)
    {
        // Only rows that carry both halves of a pace sample; rows from before ActiveSeconds existed have none and drop out here.
        var samples = context.ReadingEvents.AsNoTracking()
            .Where(e => e.ActiveSeconds != null && e.PagesRead != null)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(SampleSessions * 8)
            .ToList();

        return new ReadingPace(
            MedianSecondsPerPage(samples, ReadingItemType.Comic),
            MedianSecondsPerPage(samples, ReadingItemType.Novel));
    }

    /// <summary>The median seconds per page of the newest <see cref="SampleSessions"/> qualifying sessions of <paramref name="type"/>, or null under <see cref="MinSessions"/>.</summary>
    public static double? MedianSecondsPerPage(IEnumerable<ReadingEvent> events, ReadingItemType type)
    {
        var rates = events
            .Where(e => e.ItemType == type && e.PagesRead >= MinPagesPerSession && e.ActiveSeconds >= MinSecondsPerSession)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(SampleSessions)
            .Select(e => (double)e.ActiveSeconds!.Value / e.PagesRead!.Value)
            .OrderBy(r => r)
            .ToList();

        if (rates.Count < MinSessions)
        {
            return null;
        }

        int mid = rates.Count / 2;
        return rates.Count % 2 == 1 ? rates[mid] : (rates[mid - 1] + rates[mid]) / 2;
    }

    /// <summary>Pages from the current one to the end: the whole issue when unread, none when the count is unknown.</summary>
    public static int PagesLeft(Issue issue) =>
        issue.PageCount is int pages and > 0 ? Math.Max(0, pages - Math.Max(0, issue.LastPageRead ?? 0)) : 0;
}

/// <summary>Words for a time estimate. Rounded to five minutes: the pace is a median, not a stopwatch.</summary>
public static class TimeLeftFormatter
{
    /// <summary>"&lt;5 min", "~25 min", "~1 h", "~1 h 20 min".</summary>
    public static string Approximate(TimeSpan span)
    {
        if (span.TotalMinutes < 5)
        {
            return "<5 min";
        }

        int minutes = (int)Math.Round(span.TotalMinutes / 5, MidpointRounding.AwayFromZero) * 5;
        if (minutes < 60)
        {
            return $"~{minutes} min";
        }

        int hours = minutes / 60;
        int rest = minutes % 60;
        return rest == 0 ? $"~{hours} h" : $"~{hours} h {rest} min";
    }

    /// <summary>"~25 min left", or null with no estimate.</summary>
    public static string? Left(TimeSpan? span) => span is { } s ? $"{Approximate(s)} left" : null;

    /// <summary>"~3 h to finish the series", or null with no estimate.</summary>
    public static string? ToFinishSeries(TimeSpan? span) => span is { } s ? $"{Approximate(s)} to finish the series" : null;
}
