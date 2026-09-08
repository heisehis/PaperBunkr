using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read-only query/compute layer for the Insights screen (docs/superpowers/specs/2026-09-08-
/// stats-v2-mangabaka-design.md §5) - the actionable "what should I read next" content only.
/// Everything else that used to live here (lifetime totals, streaks, pace, composition, ratings)
/// moved to <see cref="StatsResolver"/> when Insights and Stats became separate nav-rail screens;
/// see that file's own header for the split rationale.
///
/// Mirrors <see cref="HomeFeedResolver"/> / <see cref="RecommendationResolver"/>'s shape: static, no
/// persistence of its own, a pure function of <c>(db state, now)</c> so every tile is independently
/// unit-testable against an in-memory context. Unlike <see cref="StatsResolver"/> this has no date
/// range - none of Continue/AlmostDone/DiveIn/Gaps have ever varied by one.
/// </summary>
public static class InsightsResolver
{
    internal const int StalledDays = 21;
    internal const int AlmostDoneMax = 3;
    internal const int AttentionListLimit = 12;

    // "Reading" section (design §8.3 of the original v1 doc, reframed 2026-09-06) - for a big
    // mostly-unread library, the useful question is "what do I read next", not "what needs fixing".
    internal const double GapOwnershipFloor = 0.75;   // gaps: only flag a run you own most of
    internal const int GapMissingCap = 10;            // ...with just a handful of holes
    internal const int DiveInMinIssues = 5;           // "dive in": a run worth sitting down with
    internal const int DiveInStartsBy = 2;            // ...that you own the start of (#1 or #2)
    internal const double DiveInOwnershipFloor = 0.7; // ...and own most of, contiguously enough

    public static InsightsSnapshot Build(PaperbunkrDbContext context, DateTime nowUtc)
    {
        // AsNoTrackingWithIdentityResolution so every Issue that shares a Series row also shares the
        // same Series CLR instance - the attention tiles group issues by their Series, and plain
        // AsNoTracking would hand each issue its own Series copy (one group per issue).
        var issues = context.Issues.AsNoTrackingWithIdentityResolution()
            .Include(i => i.Series)
            .Include(i => i.Tags)
            .ToList();
        var events = context.ReadingEvents.AsNoTracking().ToList();

        return new InsightsSnapshot(
            GeneratedUtc: nowUtc,
            Continue: ComputeContinue(issues, events, nowUtc),
            AlmostDone: ComputeAlmostDone(issues),
            DiveIn: ComputeDiveIn(issues, events),
            Gaps: ComputeGaps(issues));
    }

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
}

public sealed record InsightsSnapshot(
    DateTime GeneratedUtc,
    IReadOnlyList<AttentionSeries> Continue,
    IReadOnlyList<AttentionSeries> AlmostDone,
    IReadOnlyList<AttentionSeries> DiveIn,
    IReadOnlyList<CollectionGap> Gaps);

public sealed record AttentionSeries(int SeriesId, string SeriesName, string Subtitle, int? ResumeIssueId);

public sealed record CollectionGap(int SeriesId, string SeriesName, IReadOnlyList<int> MissingNumbers);
