using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Milestones timeline (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md §5). Derived from the append-only
/// <see cref="ReadingEvent"/> log every time, never stored, so a corrected history corrects the timeline and nothing re-announces on
/// launch. Backfilled rows (the import's fabricated single timestamp per item) are excluded from every rule, so the import cannot mint a
/// "100th finished" or a 30-day streak.
/// </summary>
public static class MilestoneResolver
{
    public static readonly int[] FinishedLadder = { 1, 10, 50, 100, 250, 500, 1000 };

    public static readonly int[] StreakLadder = { 7, 30, 100 };

    /// <summary>A series needs at least this many issues before finishing it counts as completing a run.</summary>
    public const int MinIssuesForCompleteRun = 2;

    public static MilestoneData Build(PaperbunkrDbContext context, DateTime nowUtc, TimeZoneInfo? zone = null)
    {
        var events = context.ReadingEvents.AsNoTracking().ToList();
        var issues = context.Issues.IgnoreQueryFilters().AsNoTracking()
            .Select(i => new { i.Id, i.SeriesId, i.IsPlaceholder, SeriesName = i.Series!.Name, i.Series.ContentType })
            .ToList();

        var issueInfo = issues.ToDictionary(i => i.Id, i => new MilestoneIssue(i.Id, i.SeriesId, i.SeriesName, i.ContentType, i.IsPlaceholder));

        var goalDates = GoalResolver.Build(context, nowUtc)
            .Where(g => g.Outcome == GoalOutcome.Completed && g.CompletedOn is not null)
            .Select(g => g.CompletedOn!.Value)
            .ToList();

        return Compute(events, issueInfo, goalDates, zone ?? TimeZoneInfo.Local);
    }

    internal static MilestoneData Compute(
        IReadOnlyCollection<ReadingEvent> allEvents,
        IReadOnlyDictionary<int, MilestoneIssue> issueInfo,
        IReadOnlyCollection<DateOnly> goalCompletionDates,
        TimeZoneInfo zone)
    {
        var events = ReadingEventFilters.WithoutBackfill(allEvents);
        if (events.Count == 0)
        {
            return new MilestoneData(Array.Empty<Milestone>(), Array.Empty<MilestoneProgress>(), allEvents.Count > 0);
        }

        DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ReadingEventFilters.ToLocal(utc, zone));

        var earned = new List<Milestone>();
        var nextUp = new List<MilestoneProgress>();

        // First finish per item, oldest first.
        var firstFinishes = events
            .Where(e => e.Kind == ReadingEventKind.Finished)
            .GroupBy(e => (e.ItemType, e.ItemId))
            .Select(g => g.OrderBy(e => e.TimestampUtc).First())
            .OrderBy(e => e.TimestampUtc)
            .ToList();

        // Finished-count ladder.
        foreach (int n in FinishedLadder)
        {
            if (firstFinishes.Count >= n)
            {
                var e = firstFinishes[n - 1];
                earned.Add(new Milestone(
                    $"finished-{n}",
                    n == 1 ? "First item finished" : $"{n} items finished",
                    e.SeriesTitle ?? string.Empty,
                    LocalDate(e.TimestampUtc),
                    MilestoneKind.Finished));
            }
        }

        int nextFinished = FinishedLadder.FirstOrDefault(n => n > firstFinishes.Count);
        if (nextFinished > 0)
        {
            nextUp.Add(new MilestoneProgress($"finished-{nextFinished}", $"{nextFinished} items finished", firstFinishes.Count, nextFinished));
        }

        // Streak ladder: walk the distinct local days in order, tracking the current run.
        var days = events.Select(e => LocalDate(e.TimestampUtc)).Distinct().OrderBy(d => d).ToList();
        var reached = new Dictionary<int, DateOnly>();
        int run = 0;
        int longest = 0;
        DateOnly? previous = null;
        foreach (var day in days)
        {
            run = previous is { } p && day == p.AddDays(1) ? run + 1 : 1;
            previous = day;
            longest = Math.Max(longest, run);
            foreach (int n in StreakLadder)
            {
                if (run >= n && !reached.ContainsKey(n))
                {
                    reached[n] = day;
                }
            }
        }

        foreach (int n in StreakLadder)
        {
            if (reached.TryGetValue(n, out var day))
            {
                earned.Add(new Milestone($"streak-{n}", $"{n}-day reading streak", string.Empty, day, MilestoneKind.Streak));
            }
        }

        int nextStreak = StreakLadder.FirstOrDefault(n => !reached.ContainsKey(n));
        if (nextStreak > 0)
        {
            nextUp.Add(new MilestoneProgress($"streak-{nextStreak}", $"{nextStreak}-day reading streak", longest, nextStreak));
        }

        // First complete series: every real issue of a series (>= MinIssuesForCompleteRun) has a finish; the date is when the last one first finished.
        var firstFinishByIssue = firstFinishes
            .Where(e => e.ItemType == ReadingItemType.Comic)
            .ToDictionary(e => e.ItemId, e => e.TimestampUtc);
        var completeCandidates = issueInfo.Values
            .Where(i => !i.IsPlaceholder)
            .GroupBy(i => i.SeriesId)
            .Where(g => g.Count() >= MinIssuesForCompleteRun && g.All(i => firstFinishByIssue.ContainsKey(i.Id)))
            .Select(g => (Name: g.First().SeriesName, At: g.Max(i => firstFinishByIssue[i.Id])))
            .OrderBy(x => x.At)
            .ToList();
        if (completeCandidates.Count > 0)
        {
            var first = completeCandidates[0];
            earned.Add(new Milestone("first-complete-series", "First complete series", first.Name, LocalDate(first.At), MilestoneKind.Series));
        }

        // First manga volume.
        var manga = firstFinishes.FirstOrDefault(e =>
            e.ItemType == ReadingItemType.Comic && issueInfo.TryGetValue(e.ItemId, out var i) && i.ContentType == ContentType.Manga);
        if (manga is not null)
        {
            earned.Add(new Milestone("first-manga-volume", "First manga volume", manga.SeriesTitle ?? string.Empty, LocalDate(manga.TimestampUtc), MilestoneKind.Manga));
        }

        // First book.
        var book = firstFinishes.FirstOrDefault(e => e.ItemType == ReadingItemType.Novel);
        if (book is not null)
        {
            earned.Add(new Milestone("first-book", "First book finished", book.SeriesTitle ?? string.Empty, LocalDate(book.TimestampUtc), MilestoneKind.Book));
        }

        // First goal completed.
        if (goalCompletionDates.Count > 0)
        {
            earned.Add(new Milestone("first-goal", "First goal completed", string.Empty, goalCompletionDates.Min(), MilestoneKind.Goal));
        }

        return new MilestoneData(earned.OrderByDescending(m => m.Date).ThenBy(m => m.Id, StringComparer.Ordinal).ToList(), nextUp, false);
    }
}

/// <summary>What <see cref="MilestoneResolver"/> needs to know about one local issue.</summary>
internal sealed record MilestoneIssue(int Id, int SeriesId, string SeriesName, ContentType ContentType, bool IsPlaceholder);

public sealed record MilestoneData(IReadOnlyList<Milestone> Earned, IReadOnlyList<MilestoneProgress> NextUp, bool OnlyBackfilledHistory);

public sealed record Milestone(string Id, string Title, string Detail, DateOnly Date, MilestoneKind Kind);

public sealed record MilestoneProgress(string Id, string Title, int Current, int Target);

public enum MilestoneKind { Finished, Streak, Series, Manga, Book, Goal }
