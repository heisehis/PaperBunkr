using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read-only query/compute layer for the Stats dashboard (docs/superpowers/specs/2026-09-08-
/// stats-v2-mangabaka-design.md §7). Split out of <see cref="InsightsResolver"/>, which used to hold
/// this content before Insights and Stats became separate nav-rail screens - same shape:
/// static, no persistence of its own, a pure function of <c>(db state, range, now)</c> so every
/// tile is independently unit-testable against an in-memory context.
/// </summary>
public static class StatsResolver
{
    public static StatsSnapshot Build(PaperbunkrDbContext context, InsightsRange range, DateTime nowUtc)
    {
        var issues = context.Issues.AsNoTrackingWithIdentityResolution()
            .Include(i => i.Series)
            .Include(i => i.Tags)
            .ToList();
        var books = context.Books.AsNoTracking()
            .Include(b => b.BookSeries)
            .ToList();
        var events = context.ReadingEvents.AsNoTracking().ToList();

        DateTime? rangeStart = RangeStartUtc(range, nowUtc);
        var inRange = rangeStart is { } start
            ? events.Where(e => e.TimestampUtc >= start).ToList()
            : events;

        var realSpans = ComputeRealSpans(events);

        return new StatsSnapshot(
            Range: range,
            GeneratedUtc: nowUtc,
            Lifetime: ComputeLifetime(issues, books, events),
            ReadingDayStreak: ComputeStreak(events, nowUtc, finishOnly: false),
            FinishStreak: ComputeStreak(events, nowUtc, finishOnly: true),
            FinishedInRange: ComputeFinishedInRange(inRange),
            Pace: ComputePace(inRange, range, nowUtc),
            Breakdown: ComputeBreakdown(issues, books),
            Composition: ComputeComposition(issues, books),
            Ratings: ComputeRatings(issues),
            Highlights: ComputeHighlights(issues, events, realSpans, nowUtc),
            ReadingActivity: ComputeReadingActivity(realSpans, inRange, range, nowUtc),
            Heatmap: ComputeHeatmap(events),
            LibraryGrowth: ComputeLibraryGrowth(issues, books),
            ContentRating: ComputeContentRating(issues),
            PublicationYear: ComputePublicationYear(issues),
            TopAuthors: ComputeTopCreators(issues, i => new[] { i.Writer }),
            TopArtists: ComputeTopCreators(issues, i => new[] { i.Penciller, i.Inker, i.Colorist }));
    }

    internal static DateTime? RangeStartUtc(InsightsRange range, DateTime nowUtc) => range switch
    {
        InsightsRange.Days30 => nowUtc.AddDays(-30),
        InsightsRange.Days90 => nowUtc.AddDays(-90),
        InsightsRange.Months12 => nowUtc.AddMonths(-12),
        InsightsRange.AllTime => null,
        _ => nowUtc.AddDays(-90),
    };

    // --- At a glance (moved from InsightsResolver unchanged) -------------------------------

    private static LifetimeTotals ComputeLifetime(List<Issue> issues, List<Book> books, List<ReadingEvent> events)
    {
        var finishedComicIds = events.Where(e => e.Kind == ReadingEventKind.Finished && e.ItemType == ReadingItemType.Comic)
            .Select(e => e.ItemId).ToHashSet();
        var finishedNovelIds = events.Where(e => e.Kind == ReadingEventKind.Finished && e.ItemType == ReadingItemType.Novel)
            .Select(e => e.ItemId).ToHashSet();

        var issueById = issues.ToDictionary(i => i.Id);
        var bookById = books.ToDictionary(b => b.Id);

        int itemsRead = 0;
        long pages = 0;
        var seriesRead = new HashSet<string>();

        foreach (int id in finishedComicIds)
        {
            itemsRead++;
            if (issueById.TryGetValue(id, out var issue))
            {
                pages += issue.PageCount ?? 0;
                if (issue.Series is { } s)
                {
                    seriesRead.Add("c:" + s.Id);
                }
            }
        }

        foreach (int id in finishedNovelIds)
        {
            itemsRead++;
            if (bookById.TryGetValue(id, out var book))
            {
                pages += EstimateBookPages(book);
                if (book.BookSeriesId is { } sid)
                {
                    seriesRead.Add("n:" + sid);
                }
            }
        }

        return new LifetimeTotals(itemsRead, pages, seriesRead.Count);
    }

    private static StreakInfo ComputeStreak(List<ReadingEvent> events, DateTime nowUtc, bool finishOnly)
    {
        var days = events
            .Where(e => !finishOnly || e.Kind == ReadingEventKind.Finished)
            .Select(e => e.TimestampUtc.ToLocalTime().Date)
            .ToHashSet();

        if (days.Count == 0)
        {
            return new StreakInfo(0, 0);
        }

        DateTime today = nowUtc.ToLocalTime().Date;
        int current = 0;
        DateTime probe = days.Contains(today) ? today : today.AddDays(-1);
        while (days.Contains(probe))
        {
            current++;
            probe = probe.AddDays(-1);
        }

        int longest = 0;
        foreach (var day in days)
        {
            if (days.Contains(day.AddDays(-1)))
            {
                continue;
            }

            int run = 1;
            var d = day.AddDays(1);
            while (days.Contains(d))
            {
                run++;
                d = d.AddDays(1);
            }

            longest = Math.Max(longest, run);
        }

        return new StreakInfo(current, Math.Max(longest, current));
    }

    private static FinishedInRange ComputeFinishedInRange(List<ReadingEvent> inRange)
    {
        var finished = inRange.Where(e => e.Kind == ReadingEventKind.Finished).ToList();
        int count = finished.Count;
        long pages = finished.Sum(e => (long)(e.PagesRead ?? 0));
        return new FinishedInRange(count, pages);
    }

    private static IReadOnlyList<PaceBucket> ComputePace(List<ReadingEvent> inRange, InsightsRange range, DateTime nowUtc)
    {
        bool monthly = range is InsightsRange.Months12 or InsightsRange.AllTime;
        var finished = inRange.Where(e => e.Kind == ReadingEventKind.Finished)
            .Select(e => (Local: e.TimestampUtc.ToLocalTime(), Pages: e.PagesRead ?? 0))
            .ToList();

        DateTime nowLocal = nowUtc.ToLocalTime();
        var buckets = new List<PaceBucket>();

        if (monthly)
        {
            int months = range == InsightsRange.Months12 ? 12 : MonthsSpan(finished, nowLocal);
            for (int i = months - 1; i >= 0; i--)
            {
                var monthStart = new DateTime(nowLocal.Year, nowLocal.Month, 1).AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1);
                var hits = finished.Where(f => f.Local >= monthStart && f.Local < monthEnd).ToList();
                buckets.Add(new PaceBucket(monthStart, monthStart.ToString("MMM"), hits.Count, hits.Sum(h => h.Pages)));
            }
        }
        else
        {
            int weeks = range == InsightsRange.Days30 ? 5 : 13;
            DateTime thisWeekStart = nowLocal.Date.AddDays(-(int)nowLocal.DayOfWeek);
            for (int i = weeks - 1; i >= 0; i--)
            {
                var weekStart = thisWeekStart.AddDays(-7 * i);
                var weekEnd = weekStart.AddDays(7);
                var hits = finished.Where(f => f.Local >= weekStart && f.Local < weekEnd).ToList();
                buckets.Add(new PaceBucket(weekStart, weekStart.ToString("MMM d"), hits.Count, hits.Sum(h => h.Pages)));
            }
        }

        return buckets;
    }

    private static int MonthsSpan(List<(DateTime Local, int Pages)> finished, DateTime nowLocal)
    {
        if (finished.Count == 0)
        {
            return 1;
        }

        var earliest = finished.Min(f => f.Local);
        return Math.Max(1, ((nowLocal.Year - earliest.Year) * 12) + nowLocal.Month - earliest.Month + 1);
    }

    /// <summary>Replaces the old 3-bucket Read/InProgress/Unread Completion donut - the real
    /// <see cref="ReadingStatus"/>/<see cref="ContentType"/> enums, series-level, all values shown
    /// including Unknown (design §6.5 - honest for a freshly-migrated CE library where almost every
    /// series is Unknown, rather than hiding or folding that bucket away).</summary>
    private static BreakdownData ComputeBreakdown(List<Issue> issues, List<Book> books)
    {
        var seriesSeen = issues.Where(i => i.Series is not null).Select(i => i.Series!).Distinct().ToList();

        var byStatus = Enum.GetValues<ReadingStatus>()
            .Select(v => new CompositionSlice(v.ToString(), seriesSeen.Count(s => s.ReadingStatus == v)))
            .Where(s => s.Count > 0)
            .OrderByDescending(s => s.Count)
            .ToList();

        var byType = Enum.GetValues<ContentType>()
            .Select(v => new CompositionSlice(v.ToString(), seriesSeen.Count(s => s.ContentType == v)))
            .Where(s => s.Count > 0)
            .OrderByDescending(s => s.Count)
            .ToList();

        return new BreakdownData(byStatus, byType);
    }

    private static CompositionData ComputeComposition(List<Issue> issues, List<Book> books)
    {
        IReadOnlyList<CompositionSlice> Top(IEnumerable<string?> raw)
        {
            return raw
                .Select(v => string.IsNullOrWhiteSpace(v) ? "Unknown" : v!.Trim())
                .GroupBy(v => v)
                .Select(g => new CompositionSlice(g.Key, g.Count()))
                .OrderByDescending(s => s.Count)
                .Take(12)
                .ToList();
        }

        var publisher = Top(issues.Select(i => string.IsNullOrWhiteSpace(i.Publisher) ? i.Series?.Publisher : i.Publisher));
        var genre = Top(issues.SelectMany(i => i.Tags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value)));
        var tags = Top(issues.SelectMany(i => i.Tags.Where(t => t.Field == IssueTagField.Tags).Select(t => t.Value)));
        var format = Top(
            issues.Select(i => i.EffectiveFormat() ?? "Comic")
                .Concat(books.Select(b => b.Format.ToString())));
        var decade = Top(
            issues.Select(i => DecadeLabel(i.EffectiveYear()))
                .Concat(books.Select(b => DecadeLabel(b.PublishedDate?.Year))));

        return new CompositionData(publisher, genre, tags, format, decade);
    }

    private static string DecadeLabel(int? year)
        => year is > 0 ? $"{year.Value / 10 * 10}s" : "Unknown";

    private static IReadOnlyList<RatingBucket> ComputeRatings(List<Issue> issues)
    {
        var counts = new int[6]; // index 1..5
        foreach (var issue in issues)
        {
            if (issue.Rating is { } r and > 0)
            {
                int star = Math.Clamp((int)Math.Round(r, MidpointRounding.AwayFromZero), 1, 5);
                counts[star]++;
            }
        }

        return Enumerable.Range(1, 5).Select(s => new RatingBucket(s, counts[s])).ToList();
    }

    private static long EstimateBookPages(Book book)
    {
        if (book.Format == BookFormat.Pdf)
        {
            return book.ChapterCount > 0 ? book.ChapterCount : 0;
        }

        return ReadingPageMath.EstimatePagesFromChars(book.CharacterCount ?? 0);
    }

    // --- New in v2 (design §6) ---------------------------------------------------------------

    /// <summary>
    /// One real (non-backfilled) Opened→Finished journey. The backfill migration
    /// (<c>ReadingEventBackfill</c>) writes a backfilled item's Opened and Finished rows at the
    /// identical <see cref="ReadingEvent.TimestampUtc"/> - that equality is the reliable signal a
    /// pair is backfilled, not a real reading session, since two real events at the exact same
    /// instant is not a realistic reading act.
    /// </summary>
    private readonly record struct RealSpan(ReadingItemType ItemType, int ItemId, DateTime OpenedUtc, DateTime FinishedUtc)
    {
        public double Days => (FinishedUtc - OpenedUtc).TotalDays;
    }

    private static IReadOnlyList<RealSpan> ComputeRealSpans(List<ReadingEvent> events)
    {
        var spans = new List<RealSpan>();
        foreach (var group in events.GroupBy(e => (e.ItemType, e.ItemId)))
        {
            var opens = group.Where(e => e.Kind == ReadingEventKind.Opened).OrderBy(e => e.TimestampUtc).ToList();
            var finishes = group.Where(e => e.Kind == ReadingEventKind.Finished).OrderBy(e => e.TimestampUtc).ToList();

            int openIdx = 0;
            foreach (var finish in finishes)
            {
                // Pair each Finished with the most recent Opened at or before it (re-reads walk
                // forward through their own Opened rows rather than always reusing the first one).
                while (openIdx + 1 < opens.Count && opens[openIdx + 1].TimestampUtc <= finish.TimestampUtc)
                {
                    openIdx++;
                }

                if (opens.Count == 0)
                {
                    continue;
                }

                var open = opens[openIdx];
                if (open.TimestampUtc == finish.TimestampUtc)
                {
                    continue; // backfill signature - not a real journey
                }

                spans.Add(new RealSpan(group.Key.ItemType, group.Key.ItemId, open.TimestampUtc, finish.TimestampUtc));
            }
        }

        return spans;
    }

    private static HighlightsData ComputeHighlights(List<Issue> issues, List<ReadingEvent> events, IReadOnlyList<RealSpan> realSpans, DateTime nowUtc)
    {
        HighlightGroup? HighestRated()
        {
            var bySeries = issues.Where(i => i.Series is not null && i.Rating is > 0)
                .GroupBy(i => i.Series!)
                .Select(g => (Series: g.Key, Avg: g.Average(i => i.Rating!.Value)))
                .ToList();
            if (bySeries.Count == 0)
            {
                return null;
            }

            double max = bySeries.Max(x => x.Avg);
            var top = bySeries.Where(x => Math.Abs(x.Avg - max) < 0.001).Select(x => x.Series.Name).ToList();
            return new HighlightGroup(top, $"Score: {max:0}");
        }

        HighlightGroup? MostReread()
        {
            var counts = events.Where(e => e.Kind == ReadingEventKind.Finished)
                .GroupBy(e => (e.ItemType, e.ItemId))
                .Select(g => (Key: g.Key, Count: g.Count()))
                .Where(x => x.Count > 1)
                .ToList();
            if (counts.Count == 0)
            {
                return null;
            }

            int max = counts.Max(c => c.Count);
            var names = counts.Where(c => c.Count == max)
                .Select(c => ResolveTitle(c.Key.ItemType, c.Key.ItemId, issues))
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList();
            return names.Count == 0 ? null : new HighlightGroup(names, $"{max} rereads");
        }

        HighlightGroup? Journey(bool longest)
        {
            var byItem = realSpans.GroupBy(s => (s.ItemType, s.ItemId))
                .Select(g => (Key: g.Key, Days: longest ? g.Max(s => s.Days) : g.Min(s => s.Days)))
                .Where(x => x.Days > 0)
                .ToList();
            if (byItem.Count == 0)
            {
                return null;
            }

            double target = longest ? byItem.Max(x => x.Days) : byItem.Min(x => x.Days);
            var names = byItem.Where(x => Math.Abs(x.Days - target) < 0.5)
                .Select(x => ResolveTitle(x.Key.ItemType, x.Key.ItemId, issues))
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList();
            return names.Count == 0 ? null : new HighlightGroup(names, $"{target:0} days");
        }

        var seriesWithIssues = issues.Where(i => i.Series is not null).Select(i => i.Series!).Distinct().ToList();
        var openedComics = events.Where(e => e.ItemType == ReadingItemType.Comic).Select(e => e.ItemId).ToHashSet();

        int planToRead = seriesWithIssues.Count(s => s.ReadingStatus == ReadingStatus.Planned);
        int zeroProgress = seriesWithIssues.Count(s =>
            s.ReadingStatus != ReadingStatus.Planned &&
            issues.Where(i => i.Series == s).All(i => i.OpenCount == 0 && !openedComics.Contains(i.Id)));

        return new HighlightsData(HighestRated(), MostReread(), Journey(longest: true), Journey(longest: false), planToRead, zeroProgress);
    }

    private static string? ResolveTitle(ReadingItemType type, int itemId, List<Issue> issues)
        => type == ReadingItemType.Comic ? issues.FirstOrDefault(i => i.Id == itemId)?.Series?.Name : null;

    private static ReadingActivityData ComputeReadingActivity(IReadOnlyList<RealSpan> realSpans, List<ReadingEvent> inRange, InsightsRange range, DateTime nowUtc)
    {
        double avgDays = realSpans.Count > 0 ? realSpans.Average(s => s.Days) : 0;

        DateTime? rangeStart = RangeStartUtc(range, nowUtc);
        double rangeDays;
        if (rangeStart is { } start)
        {
            rangeDays = Math.Max(1, (nowUtc - start).TotalDays);
        }
        else
        {
            // AllTime: span from the earliest event ever logged, not an arbitrary fixed window.
            DateTime earliest = inRange.Count > 0 ? inRange.Min(e => e.TimestampUtc) : nowUtc;
            rangeDays = Math.Max(1, (nowUtc - earliest).TotalDays);
        }

        int finishedInRange = inRange.Count(e => e.Kind == ReadingEventKind.Finished);
        double avgPerDay = finishedInRange / rangeDays;

        return new ReadingActivityData(avgDays, avgPerDay);
    }

    private static IReadOnlyDictionary<DateOnly, int> ComputeHeatmap(List<ReadingEvent> events)
    {
        return events
            .Select(e => DateOnly.FromDateTime(e.TimestampUtc.ToLocalTime().Date))
            .GroupBy(d => d)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Cumulative growth by add-date, stacked by the item's *current* status/type/rating -
    /// design §6.4's accepted simplification (no historical status-change tracking exists).</summary>
    private static LibraryGrowthData ComputeLibraryGrowth(List<Issue> issues, List<Book> books)
    {
        var points = issues.Where(i => i.AddedTime is not null)
            .Select(i => new GrowthPoint(
                i.AddedTime!.Value.Date,
                ReadingStatus: i.Series?.ReadingStatus.ToString() ?? "Unknown",
                MediaType: i.Series?.ContentType.ToString() ?? "Unknown",
                ContentRating: string.IsNullOrWhiteSpace(i.AgeRating) ? "Unknown" : i.AgeRating!.Trim(),
                SeriesId: i.SeriesId))
            .Concat(books.Select(b => new GrowthPoint(
                b.AddedTime.Date,
                ReadingStatus: b.Finished ? "Completed" : "Unknown",
                MediaType: "Novel",
                ContentRating: "Unknown",
                SeriesId: null)))
            .OrderBy(p => p.AddedDate)
            .ToList();

        return new LibraryGrowthData(points);
    }

    private static IReadOnlyList<CompositionSlice> ComputeContentRating(List<Issue> issues)
    {
        return issues
            .Select(i => string.IsNullOrWhiteSpace(i.AgeRating) ? "Unknown" : i.AgeRating!.Trim())
            .GroupBy(v => v)
            .Select(g => new CompositionSlice(g.Key, g.Count()))
            .OrderByDescending(s => s.Count)
            .ToList();
    }

    private static IReadOnlyList<YearBucket> ComputePublicationYear(List<Issue> issues)
    {
        return issues.Where(i => i.Year is > 0)
            .GroupBy(i => i.Year!.Value)
            .Select(g => new YearBucket(g.Key, g.Count()))
            .OrderBy(y => y.Year)
            .ToList();
    }

    /// <summary><paramref name="fieldSelector"/> returns one issue's raw field(s) to parse (Writer
    /// for Authors; Penciller+Inker+Colorist for Artists). Comma-split, trimmed, de-duped
    /// per-issue-per-name first (so one person credited in two of the three Artist fields on the
    /// same issue counts once for that issue), then counted across the library. Top 10.</summary>
    private static IReadOnlyList<CompositionSlice> ComputeTopCreators(List<Issue> issues, Func<Issue, IEnumerable<string?>> fieldSelector)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in issues)
        {
            var namesOnThisIssue = fieldSelector(issue)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .SelectMany(f => f!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var name in namesOnThisIssue)
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        return counts
            .Select(kv => new CompositionSlice(kv.Key, kv.Value))
            .OrderByDescending(s => s.Count)
            .Take(10)
            .ToList();
    }
}

public enum InsightsRange
{
    Days30 = 0,
    Days90 = 1,
    Months12 = 2,
    AllTime = 3,
}

public sealed record StatsSnapshot(
    InsightsRange Range,
    DateTime GeneratedUtc,
    LifetimeTotals Lifetime,
    StreakInfo ReadingDayStreak,
    StreakInfo FinishStreak,
    FinishedInRange FinishedInRange,
    IReadOnlyList<PaceBucket> Pace,
    BreakdownData Breakdown,
    CompositionData Composition,
    IReadOnlyList<RatingBucket> Ratings,
    HighlightsData Highlights,
    ReadingActivityData ReadingActivity,
    IReadOnlyDictionary<DateOnly, int> Heatmap,
    LibraryGrowthData LibraryGrowth,
    IReadOnlyList<CompositionSlice> ContentRating,
    IReadOnlyList<YearBucket> PublicationYear,
    IReadOnlyList<CompositionSlice> TopAuthors,
    IReadOnlyList<CompositionSlice> TopArtists);

public sealed record LifetimeTotals(int ItemsRead, long PagesRead, int SeriesRead);

public sealed record StreakInfo(int Current, int Longest);

public sealed record FinishedInRange(int Items, long Pages);

public sealed record PaceBucket(DateTime Start, string Label, int Finished, int Pages);

public sealed record BreakdownData(IReadOnlyList<CompositionSlice> ByReadingStatus, IReadOnlyList<CompositionSlice> ByMediaType);

public sealed record CompositionSlice(string Label, int Count);

public sealed record CompositionData(
    IReadOnlyList<CompositionSlice> ByPublisher,
    IReadOnlyList<CompositionSlice> ByGenre,
    IReadOnlyList<CompositionSlice> ByTags,
    IReadOnlyList<CompositionSlice> ByFormat,
    IReadOnlyList<CompositionSlice> ByDecade);

public sealed record RatingBucket(int Stars, int Count);

/// <summary>One Highlights card (design §6.1). <see cref="Titles"/> has more than one entry when
/// several items tie for the top spot - rendered as "Title and N others".</summary>
public sealed record HighlightGroup(IReadOnlyList<string> Titles, string Detail)
{
    /// <summary>"Title" or "Title and N others" (matches MangaBaka's own copy for a tie) - computed
    /// rather than stored so XAML can bind it directly without a converter.</summary>
    public string DisplayTitle => Titles.Count switch
    {
        0 => string.Empty,
        1 => Titles[0],
        2 => $"{Titles[0]} and 1 other",
        _ => $"{Titles[0]} and {Titles.Count - 1} others",
    };
}

public sealed record HighlightsData(
    HighlightGroup? HighestRated,
    HighlightGroup? MostReread,
    HighlightGroup? LongestJourney,
    HighlightGroup? FastestCompletion,
    int PlanToReadCount,
    int ZeroProgressCount);

public sealed record ReadingActivityData(double AvgDaysToComplete, double AvgIssuesPerDay);

public sealed record GrowthPoint(DateTime AddedDate, string ReadingStatus, string MediaType, string ContentRating, int? SeriesId);

public sealed record LibraryGrowthData(IReadOnlyList<GrowthPoint> Points);

public sealed record YearBucket(int Year, int Count);
