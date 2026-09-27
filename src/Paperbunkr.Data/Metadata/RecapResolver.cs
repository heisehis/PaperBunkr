using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read-only query/compute layer for the Insights screen's "Recap" tab (docs/superpowers/specs/2026-09-23-
/// insights-year-in-review-recap-design.md) - a calendar-year-scoped "Wrapped"-style summary. Deliberately
/// separate from <see cref="StatsResolver"/>: <see cref="InsightsRange"/>'s rolling windows (30d/90d/12mo/
/// AllTime) and "a specific calendar year" are incompatible range models, and none of the tiles here need
/// <see cref="StatsResolver"/>'s private journey-duration machinery. Same shape as <see cref="StatsResolver.Build"/>
/// otherwise: a pure function of (context, year, now), no persistence, no caching of its own.
/// </summary>
public static class RecapResolver
{
    /// <summary>Distinct years with at least one <see cref="ReadingEvent"/>, newest first, bucketed by
    /// local calendar year (matching <see cref="StatsResolver.ComputeStreak"/>/<c>ComputeHeatmap</c>'s own
    /// local-time convention). Always includes the current year once it has &gt;=1 event, even mid-year.</summary>
    public static IReadOnlyList<int> AvailableYears(PaperbunkrDbContext context, DateTime nowUtc)
    {
        return context.ReadingEvents.AsNoTracking()
            .Select(e => e.TimestampUtc)
            .ToList()
            .Select(t => t.ToLocalTime().Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();
    }

    public static RecapSnapshot Build(PaperbunkrDbContext context, int year, DateTime nowUtc)
    {
        var issues = context.Issues.IgnoreQueryFilters().AsNoTrackingWithIdentityResolution()
            .Include(i => i.Series)
            .ToList();
        var books = context.Books.AsNoTracking().ToList();
        var events = context.ReadingEvents.AsNoTracking().ToList();

        DateTime yearStartLocal = new(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        DateTime yearEndLocal = new(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);

        var yearEvents = events.Where(e =>
        {
            var local = e.TimestampUtc.ToLocalTime();
            return local >= yearStartLocal && local <= yearEndLocal;
        }).ToList();

        var finished = yearEvents.Where(e => e.Kind == ReadingEventKind.Finished).ToList();
        var issueById = issues.ToDictionary(i => i.Id);
        var bookById = books.ToDictionary(b => b.Id);

        int itemsFinished = finished.Count;
        long pagesRead = finished.Sum(e => (long)(e.PagesRead ?? 0));

        var finishedComicIds = finished.Where(e => e.ItemType == ReadingItemType.Comic).Select(e => e.ItemId).ToHashSet();
        var finishedComicIssues = finishedComicIds
            .Select(id => issueById.TryGetValue(id, out var i) ? i : null)
            .Where(i => i is not null).Select(i => i!).ToList();
        var finishedBookIds = finished.Where(e => e.ItemType == ReadingItemType.Novel).Select(e => e.ItemId).ToHashSet();
        var finishedBooks = finishedBookIds
            .Select(id => bookById.TryGetValue(id, out var b) ? b : null)
            .Where(b => b is not null).Select(b => b!).ToList();

        bool isCurrentYear = year == nowUtc.ToLocalTime().Year;

        return new RecapSnapshot(
            Year: year,
            IsCurrentYear: isCurrentYear,
            ItemsFinished: itemsFinished,
            PagesRead: pagesRead,
            LongestStreakDays: ComputeYearStreak(yearEvents),
            BusiestDay: ComputeBusiestDay(yearEvents),
            TopSeries: ComputeTopSeries(finishedComicIssues),
            TopWriter: ComputeTopWriter(finishedComicIssues, finishedBooks),
            TopArtist: ComputeTopArtist(finishedComicIssues),
            HighestRatedSeries: ComputeHighestRatedSeries(finishedComicIssues),
            MostRereadItem: ComputeMostRereadItem(finished, issueById));
    }

    /// <summary>Longest run of consecutive local calendar days with >=1 event, capped at the year's own
    /// boundaries (the caller already filtered <paramref name="yearEvents"/> to one calendar year, so a run
    /// can never reach into the prior/next year) - same scan shape as <see cref="StatsResolver.ComputeStreak"/>'s
    /// "longest" loop, minus that method's separate "current streak" tracking (meaningless for a past year).</summary>
    private static int ComputeYearStreak(List<ReadingEvent> yearEvents)
    {
        var days = yearEvents.Select(e => e.TimestampUtc.ToLocalTime().Date).ToHashSet();
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

        return longest;
    }

    private static RecapBusiestDay? ComputeBusiestDay(List<ReadingEvent> yearEvents)
    {
        var byDay = yearEvents
            .GroupBy(e => DateOnly.FromDateTime(e.TimestampUtc.ToLocalTime().Date))
            .Select(g => new RecapBusiestDay(g.Key, g.Sum(e => (long)(e.PagesRead ?? 0))))
            .Where(d => d.Pages > 0)
            .ToList();

        return byDay.Count == 0 ? null : byDay.OrderByDescending(d => d.Pages).First();
    }

    private static HighlightGroup? ComputeTopSeries(List<Issue> finishedComicIssues)
    {
        var bySeries = finishedComicIssues.Where(i => i.Series is not null)
            .GroupBy(i => i.Series!)
            .Select(g => (Series: g.Key, Count: g.Count()))
            .ToList();
        if (bySeries.Count == 0)
        {
            return null;
        }

        int max = bySeries.Max(x => x.Count);
        var tied = bySeries.Where(x => x.Count == max).ToList();
        var names = tied.Select(x => x.Series.Name).ToList();
        // First tied series (docs/superpowers/specs/2026-09-23-insights-redesign-design.md) - matches
        // HighlightGroup.DisplayTitle's own "Titles[0] and N others" convention of privileging the
        // first tied entry, so the cover shown always matches the name shown.
        return new HighlightGroup(names, $"{max} finished", SeriesId: tied[0].Series.Id);
    }

    /// <summary>Comma-split/trim/per-issue-distinct exactly like <see cref="StatsResolver.ComputeTopCreators"/>,
    /// plus <see cref="Book.Author"/> for novels folded into the same tally (a novel's Author reads as a
    /// writer-equivalent role).</summary>
    private static HighlightGroup? ComputeTopWriter(List<Issue> finishedComicIssues, List<Book> finishedBooks)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var issue in finishedComicIssues)
        {
            foreach (var name in SplitCreators(issue.Writer))
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        foreach (var book in finishedBooks)
        {
            foreach (var name in SplitCreators(book.Author))
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        return TopTally(counts, "credited");
    }

    private static HighlightGroup? ComputeTopArtist(List<Issue> finishedComicIssues)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in finishedComicIssues)
        {
            var namesOnThisIssue = new[] { issue.Penciller, issue.Inker, issue.Colorist }
                .SelectMany(SplitCreators)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var name in namesOnThisIssue)
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        return TopTally(counts, "credited");
    }

    private static IEnumerable<string> SplitCreators(string? field)
        => string.IsNullOrWhiteSpace(field)
            ? Array.Empty<string>()
            : field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static HighlightGroup? TopTally(Dictionary<string, int> counts, string unit)
    {
        if (counts.Count == 0)
        {
            return null;
        }

        int max = counts.Values.Max();
        var names = counts.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();
        return new HighlightGroup(names, $"{max} {unit}");
    }

    private static HighlightGroup? ComputeHighestRatedSeries(List<Issue> finishedComicIssues)
    {
        var bySeries = finishedComicIssues.Where(i => i.Series is not null && i.Rating is > 0)
            .GroupBy(i => i.Series!)
            .Select(g => (Series: g.Key, Avg: g.Average(i => i.Rating!.Value)))
            .ToList();
        if (bySeries.Count == 0)
        {
            return null;
        }

        double max = bySeries.Max(x => x.Avg);
        var tied = bySeries.Where(x => Math.Abs(x.Avg - max) < 0.001).ToList();
        var names = tied.Select(x => x.Series.Name).ToList();
        return new HighlightGroup(names, $"Score: {max:0}", SeriesId: tied[0].Series.Id);
    }

    private static HighlightGroup? ComputeMostRereadItem(List<ReadingEvent> finished, Dictionary<int, Issue> issueById)
    {
        var counts = finished.GroupBy(e => (e.ItemType, e.ItemId))
            .Select(g => (Key: g.Key, Count: g.Count()))
            .Where(x => x.Count > 1)
            .ToList();
        if (counts.Count == 0)
        {
            return null;
        }

        int max = counts.Max(c => c.Count);
        var tiedComicIssueIds = counts.Where(c => c.Count == max && c.Key.ItemType == ReadingItemType.Comic)
            .Select(c => c.Key.ItemId)
            .Where(issueById.ContainsKey)
            .ToList();
        var names = tiedComicIssueIds.Select(id => issueById[id].Series?.Name).Where(n => n is not null).Select(n => n!).ToList();
        // First tied comic issue's own id, not a series id - "Most reread" is about one specific
        // issue/printing, unlike Top Series/Highest Rated above (docs/superpowers/specs/2026-09-23-
        // insights-redesign-design.md). Book rereads still get no cover, matching this function's
        // existing Comic-only name-resolution behavior above.
        return names.Count == 0 ? null : new HighlightGroup(names, $"{max} rereads", IssueId: tiedComicIssueIds[0]);
    }
}

public sealed record RecapSnapshot(
    int Year,
    bool IsCurrentYear,
    int ItemsFinished,
    long PagesRead,
    int LongestStreakDays,
    RecapBusiestDay? BusiestDay,
    HighlightGroup? TopSeries,
    HighlightGroup? TopWriter,
    HighlightGroup? TopArtist,
    HighlightGroup? HighestRatedSeries,
    HighlightGroup? MostRereadItem);

public sealed record RecapBusiestDay(DateOnly Date, long Pages);
