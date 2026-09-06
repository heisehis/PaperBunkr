using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read-only query/compute layer for the Insights dashboard (docs/superpowers/specs/
/// 2026-09-05-insights-dashboard-design.md §7). Mirrors <see cref="HomeFeedResolver"/> /
/// <see cref="RecommendationResolver"/>'s shape: static, no persistence of its own, a pure function
/// of <c>(db state, range, now)</c> so every tile is independently unit-testable against an
/// in-memory context.
///
/// One <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{T}"/> pass each over
/// <see cref="PaperbunkrDbContext.Issues"/>, <see cref="PaperbunkrDbContext.Books"/>, and the whole
/// (small, never-pruned) <see cref="PaperbunkrDbContext.ReadingEvents"/> table; all further work is
/// in memory. The App-side <c>InsightsScreenViewModel</c> caches the returned snapshot per range.
/// </summary>
public static class InsightsResolver
{
    internal const int StalledDays = 21;
    internal const int AlmostDoneMax = 3;
    internal const int AttentionListLimit = 12;

    // "Reading" section (design §8.3, reframed 2026-09-06) - for a big mostly-unread library, the
    // useful question is "what do I read next", not "what needs fixing".
    internal const double GapOwnershipFloor = 0.75;   // gaps: only flag a run you own most of
    internal const int GapMissingCap = 10;            // ...with just a handful of holes
    internal const int DiveInMinIssues = 5;           // "dive in": a run worth sitting down with
    internal const int DiveInStartsBy = 2;            // ...that you own the start of (#1 or #2)
    internal const double DiveInOwnershipFloor = 0.7; // ...and own most of, contiguously enough

    public static InsightsSnapshot Build(PaperbunkrDbContext context, InsightsRange range, DateTime nowUtc)
    {
        // AsNoTrackingWithIdentityResolution so every Issue that shares a Series row also shares the
        // same Series CLR instance - the attention tiles group issues by their Series, and plain
        // AsNoTracking would hand each issue its own Series copy (one group per issue).
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

        return new InsightsSnapshot(
            Range: range,
            GeneratedUtc: nowUtc,
            Continue: ComputeContinue(issues, events, nowUtc),
            AlmostDone: ComputeAlmostDone(issues),
            DiveIn: ComputeDiveIn(issues, events),
            Gaps: ComputeGaps(issues),
            Lifetime: ComputeLifetime(issues, books, events),
            ReadingDayStreak: ComputeStreak(events, nowUtc, finishOnly: false),
            FinishStreak: ComputeStreak(events, nowUtc, finishOnly: true),
            FinishedInRange: ComputeFinishedInRange(inRange, issues, books),
            Pace: ComputePace(inRange, range, nowUtc),
            Completion: ComputeCompletion(issues, books),
            Composition: ComputeComposition(issues, books),
            Ratings: ComputeRatings(issues));
    }

    internal static DateTime? RangeStartUtc(InsightsRange range, DateTime nowUtc) => range switch
    {
        InsightsRange.Days30 => nowUtc.AddDays(-30),
        InsightsRange.Days90 => nowUtc.AddDays(-90),
        InsightsRange.Months12 => nowUtc.AddMonths(-12),
        InsightsRange.AllTime => null,
        _ => nowUtc.AddDays(-90),
    };

    // --- Reading (what to read next) --------------------------------------------------------

    /// <summary>
    /// Every series with at least one in-progress issue - resume point, "X of Y read", and a
    /// "dropped off Nwk ago" tag for the ones untouched longer than <see cref="StalledDays"/>
    /// (absorbs the old separate "Stalled" card). Most-recently-touched first. Excludes
    /// <see cref="ReadingStatus.Dropped"/> series.
    /// </summary>
    private static IReadOnlyList<AttentionSeries> ComputeContinue(List<Issue> issues, List<ReadingEvent> events, DateTime nowUtc)
    {
        DateTime staleCutoff = nowUtc.AddDays(-StalledDays);
        var lastTouchByIssue = events
            .Where(e => e.ItemType == ReadingItemType.Comic)
            .GroupBy(e => e.ItemId)
            .ToDictionary(g => g.Key, g => g.Max(e => e.TimestampUtc));

        DateTime LastTouch(Issue i) => lastTouchByIssue.TryGetValue(i.Id, out var t) ? t : (i.OpenedTime ?? DateTime.MinValue);

        var result = new List<(AttentionSeries Row, DateTime Touch)>();
        foreach (var group in issues.Where(i => i.Series != null).GroupBy(i => i.Series!))
        {
            var series = group.Key;
            if (series.ReadingStatus == ReadingStatus.Dropped)
            {
                continue;
            }

            var inProgress = group.Where(i => i.IsInProgress()).ToList();
            if (inProgress.Count == 0)
            {
                continue;
            }

            var resume = inProgress.OrderByDescending(LastTouch).First();
            DateTime touch = LastTouch(resume);

            int read = group.Count(i => i.HasBeenRead());
            int total = group.Count();
            string subtitle = $"{read} of {total} read";
            if (touch != DateTime.MinValue && touch < staleCutoff)
            {
                int weeks = Math.Max(1, (int)Math.Round((nowUtc - touch).TotalDays / 7.0));
                subtitle += $" · dropped off {weeks}wk ago";
            }

            result.Add((new AttentionSeries(series.Id, series.Name, subtitle, resume.Id), touch));
        }

        return result
            .OrderByDescending(x => x.Touch)
            .Select(x => x.Row)
            .Take(AttentionListLimit)
            .ToList();
    }

    /// <summary>
    /// Series you've never opened where you own a run worth diving into: has issue #1 (or #2),
    /// at least <see cref="DiveInMinIssues"/> issues, and owns ≥ <see cref="DiveInOwnershipFloor"/>
    /// of its min→max numeric span. Biggest owned run first. Replaces the old "untouched arrivals".
    /// </summary>
    private static IReadOnlyList<AttentionSeries> ComputeDiveIn(List<Issue> issues, List<ReadingEvent> events)
    {
        var openedComics = events.Where(e => e.ItemType == ReadingItemType.Comic).Select(e => e.ItemId).ToHashSet();

        var result = new List<(AttentionSeries Row, int Owned)>();
        foreach (var group in issues.Where(i => i.Series != null).GroupBy(i => i.Series!))
        {
            var series = group.Key;
            if (series.ReadingStatus == ReadingStatus.Dropped)
            {
                continue;
            }

            // "Never opened" - nothing in the series has a reading event or an OpenCount.
            if (group.Any(i => i.OpenCount > 0 || openedComics.Contains(i.Id)))
            {
                continue;
            }

            var numeric = group
                .Where(i => i.NumberType() == IssueNumberType.Numeric && i.NumberSortKey() is { } k && k >= 0)
                .Select(i => (int)Math.Floor(i.NumberSortKey()!.Value))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            if (numeric.Count < DiveInMinIssues || numeric[0] > DiveInStartsBy)
            {
                continue;
            }

            int span = numeric[^1] - numeric[0] + 1;
            if ((double)numeric.Count / span < DiveInOwnershipFloor)
            {
                continue;
            }

            string subtitle = $"own {numeric.Count} issues · #{numeric[0]}–{numeric[^1]}, never opened";
            result.Add((new AttentionSeries(series.Id, series.Name, subtitle, ResumeIssueId: null), numeric.Count));
        }

        return result
            .OrderByDescending(x => x.Owned)
            .Select(x => x.Row)
            .Take(AttentionListLimit)
            .ToList();
    }

    private static IReadOnlyList<AttentionSeries> ComputeAlmostDone(List<Issue> issues)
    {
        var result = new List<AttentionSeries>();
        foreach (var group in issues.Where(i => i.Series != null).GroupBy(i => i.Series!))
        {
            var series = group.Key;
            if (series.ReadingStatus == ReadingStatus.Dropped)
            {
                continue;
            }

            bool started = group.Any(i => i.HasBeenRead());
            if (!started)
            {
                continue;
            }

            int remaining = group.Count(i => i.IsUnread());
            if (remaining is < 1 or > AlmostDoneMax)
            {
                continue;
            }

            var next = group
                .Where(i => i.IsUnread())
                .OrderBy(i => i.NumberSortKey() ?? float.MaxValue)
                .FirstOrDefault();

            result.Add(new AttentionSeries(
                series.Id, series.Name,
                remaining == 1 ? "1 issue left" : $"{remaining} issues left",
                next?.Id));
        }

        return result
            .OrderBy(a => a.Subtitle)
            .Take(AttentionListLimit)
            .ToList();
    }

    private static IReadOnlyList<CollectionGap> ComputeGaps(List<Issue> issues)
    {
        var result = new List<CollectionGap>();
        foreach (var group in issues.Where(i => i.Series != null).GroupBy(i => i.Series!))
        {
            var numeric = group
                .Where(i => i.NumberType() == IssueNumberType.Numeric && i.NumberSortKey() is { } k && k >= 0)
                .Select(i => (int)Math.Floor(i.NumberSortKey()!.Value))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            // Need a real run to talk about a "gap" in - two issues #1 and #400 is not one.
            if (numeric.Count < 3)
            {
                continue;
            }

            int span = numeric[^1] - numeric[0] + 1;
            double ownership = (double)numeric.Count / span;
            if (ownership < GapOwnershipFloor)
            {
                continue; // you own a scattering across a wide range - not a fill-the-holes situation
            }

            var owned = numeric.ToHashSet();
            var missing = new List<int>();
            for (int n = numeric[0]; n <= numeric[^1]; n++)
            {
                if (!owned.Contains(n))
                {
                    missing.Add(n);
                }
            }

            if (missing.Count is > 0 and <= GapMissingCap)
            {
                result.Add(new CollectionGap(group.Key.Id, group.Key.Name, missing));
            }
        }

        return result
            .OrderBy(g => g.MissingNumbers.Count) // closest-to-complete first
            .Take(AttentionListLimit)
            .ToList();
    }

    // --- At a glance -----------------------------------------------------------------------

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

        // Current streak: walk back from today (or yesterday, so a day with no reading yet doesn't
        // instantly zero a live streak) while consecutive days are present.
        DateTime today = nowUtc.ToLocalTime().Date;
        int current = 0;
        DateTime probe = days.Contains(today) ? today : today.AddDays(-1);
        while (days.Contains(probe))
        {
            current++;
            probe = probe.AddDays(-1);
        }

        // Longest streak ever.
        int longest = 0;
        foreach (var day in days)
        {
            if (days.Contains(day.AddDays(-1)))
            {
                continue; // not a run start
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

    private static FinishedInRange ComputeFinishedInRange(List<ReadingEvent> inRange, List<Issue> issues, List<Book> books)
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

    private static CompletionCounts ComputeCompletion(List<Issue> issues, List<Book> books)
    {
        int read = issues.Count(i => i.HasBeenRead());
        int inProgress = issues.Count(i => i.IsInProgress());
        int unread = issues.Count(i => i.IsUnread());

        foreach (var book in books)
        {
            if (book.Finished)
            {
                read++;
            }
            else if (book.LastOpenedTime != null && (book.LastChapterIndex > 0 || book.LastCharacterOffset > 0))
            {
                inProgress++;
            }
            else
            {
                unread++;
            }
        }

        return new CompletionCounts(read, inProgress, unread);
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
        var format = Top(
            issues.Select(i => i.EffectiveFormat() ?? "Comic")
                .Concat(books.Select(b => b.Format.ToString())));
        var decade = Top(
            issues.Select(i => DecadeLabel(i.EffectiveYear()))
                .Concat(books.Select(b => DecadeLabel(b.PublishedDate?.Year))));

        return new CompositionData(publisher, genre, format, decade);
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
            return book.ChapterCount > 0 ? book.ChapterCount : 0; // PDF ChapterCount is unused; real count unknown here
        }

        return ReadingPageMath.EstimatePagesFromChars(book.CharacterCount ?? 0);
    }
}

public enum InsightsRange
{
    Days30 = 0,
    Days90 = 1,
    Months12 = 2,
    AllTime = 3,
}

public sealed record InsightsSnapshot(
    InsightsRange Range,
    DateTime GeneratedUtc,
    IReadOnlyList<AttentionSeries> Continue,
    IReadOnlyList<AttentionSeries> AlmostDone,
    IReadOnlyList<AttentionSeries> DiveIn,
    IReadOnlyList<CollectionGap> Gaps,
    LifetimeTotals Lifetime,
    StreakInfo ReadingDayStreak,
    StreakInfo FinishStreak,
    FinishedInRange FinishedInRange,
    IReadOnlyList<PaceBucket> Pace,
    CompletionCounts Completion,
    CompositionData Composition,
    IReadOnlyList<RatingBucket> Ratings);

public sealed record AttentionSeries(int SeriesId, string SeriesName, string Subtitle, int? ResumeIssueId);

public sealed record CollectionGap(int SeriesId, string SeriesName, IReadOnlyList<int> MissingNumbers);

public sealed record LifetimeTotals(int ItemsRead, long PagesRead, int SeriesRead);

public sealed record StreakInfo(int Current, int Longest);

public sealed record FinishedInRange(int Items, long Pages);

public sealed record PaceBucket(DateTime Start, string Label, int Finished, int Pages);

public sealed record CompletionCounts(int Read, int InProgress, int Unread);

public sealed record CompositionSlice(string Label, int Count);

public sealed record CompositionData(
    IReadOnlyList<CompositionSlice> ByPublisher,
    IReadOnlyList<CompositionSlice> ByGenre,
    IReadOnlyList<CompositionSlice> ByFormat,
    IReadOnlyList<CompositionSlice> ByDecade);

public sealed record RatingBucket(int Stars, int Count);
