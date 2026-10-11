using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public enum UpNextReason
{
    /// <summary>One to three issues left in a series you have started.</summary>
    AlmostDone,

    /// <summary>The next unread entry of a reading list you have started.</summary>
    NextInList,

    /// <summary>You were reading this series but have not touched it for a while.</summary>
    Stalled,

    /// <summary>You read from this series recently.</summary>
    Recent,
}

/// <summary>One row of Home's Up Next list: the issue to open and the single reason it is here.</summary>
/// <param name="PagesLeft">For the time-left estimate; 0 when the page count is unknown.</param>
public sealed record UpNextItem(int IssueId, int SeriesId, string SeriesName, string IssueLabel, UpNextReason Reason, string ReasonText, int PagesLeft);

/// <summary>
/// Home's "Up Next" (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.4): for each series you are part-way through, the
/// next unread issue, ranked. Series with an issue in progress are left out - Continue Reading already shows those - so nothing appears
/// in both rows. The score is a sum of a few fixed weights; it orders the list and is never shown, only the strongest reason is.
/// </summary>
public static class UpNextResolver
{
    public const int DefaultLimit = 5;

    // The weights. Kept together so the ordering test and anyone tuning them look in one place.
    internal const double RecencyMax = 30;          // read today
    internal const int RecencyHorizonDays = 60;     // ...fading to nothing by here
    internal const double AlmostDoneBonus = 25;     // 1-3 issues left
    internal const double NextInListBonus = 20;     // the next entry of a started reading list
    internal const double StalledBonus = 12;        // resurfaces a series left for over three weeks
    internal const int AlmostDoneMaxUnread = 3;

    public static IReadOnlyList<UpNextItem> Build(PaperbunkrDbContext context, DateTime nowUtc, int limit = DefaultLimit)
    {
        var series = context.Series.AsNoTracking().Include(s => s.Issues).ThenInclude(i => i.MetadataProposals).AsSplitQuery().ToList();
        var lastEvent = context.ReadingEvents.AsNoTracking()
            .Where(e => e.ItemType == ReadingItemType.Comic && e.SeriesId != null)
            .GroupBy(e => e.SeriesId!.Value)
            .Select(g => new { SeriesId = g.Key, Last = g.Max(e => e.TimestampUtc) })
            .ToDictionary(x => x.SeriesId, x => x.Last);
        var lists = context.ReadingLists.AsNoTracking().Include(l => l.Items).AsSplitQuery().ToList();

        return Build(series, lists, id => lastEvent.TryGetValue(id, out var last) ? last : null, nowUtc, limit);
    }

    /// <summary><paramref name="series"/> must have their issues loaded and <paramref name="lists"/> their items.</summary>
    public static IReadOnlyList<UpNextItem> Build(
        IReadOnlyCollection<Series> series, IEnumerable<ReadingList> lists, Func<int, DateTime?> lastEventUtc, DateTime nowUtc, int limit = DefaultLimit)
    {
        var issueById = series.SelectMany(s => s.Issues).ToDictionary(i => i.Id);
        var listNextIssueIds = NextIssuesOfStartedLists(lists, issueById);

        var scored = new List<(UpNextItem Item, double Score, DateTime Last)>();
        foreach (var s in series)
        {
            if (s.ReadingStatus is ReadingStatus.Dropped or ReadingStatus.Paused)
            {
                continue;
            }

            // An issue in progress means the series is already in Continue Reading.
            if (s.Issues.Any(i => !i.IsPlaceholder && i.IsInProgress()))
            {
                continue;
            }

            var progress = SeriesProgress.Of(s.Issues, lastEventUtc(s.Id));
            var unread = s.Issues
                .Where(i => !i.IsPlaceholder && !i.FileIsMissing && !string.IsNullOrEmpty(i.FilePath) && !i.HasBeenRead())
                .OrderBy(i => i.NumberSortKey() ?? float.MaxValue)
                .ThenBy(i => i.Id)
                .ToList();
            if (unread.Count == 0)
            {
                continue;
            }

            // Next in reading order; a series you have not started is only here for the reading list that points into it.
            var listNext = unread.FirstOrDefault(i => listNextIssueIds.Contains(i.Id));
            var next = progress.ReadCount >= 1 ? unread[0] : listNext;
            if (next is null)
            {
                continue;
            }

            double recency = 0;
            int? days = progress.DaysSinceLastRead(nowUtc);
            if (days is int d)
            {
                recency = RecencyMax * Math.Max(0, 1 - (double)d / RecencyHorizonDays);
            }

            bool almostDone = progress.ReadCount >= 1 && progress.UnreadCount <= AlmostDoneMaxUnread;
            bool nextInList = listNextIssueIds.Contains(next.Id);
            bool stalled = progress.ReadCount >= 1 && days is > InsightsResolver.StalledDays;

            double score = recency
                + (almostDone ? AlmostDoneBonus : 0)
                + (nextInList ? NextInListBonus : 0)
                + (stalled ? StalledBonus : 0);

            // One reason only: the most specific thing that is true of this series. Recency moves almost every row, so as a reason it
            // is the fallback - otherwise every row would say "you read this recently" and none would say why it is ahead of the others.
            var reason = almostDone ? UpNextReason.AlmostDone
                : nextInList ? UpNextReason.NextInList
                : stalled ? UpNextReason.Stalled
                : UpNextReason.Recent;

            scored.Add((
                new UpNextItem(next.Id, s.Id, s.Name, IssueLabel(next), reason, ReasonText(reason, progress.UnreadCount, days), ReadingPaceResolver.PagesLeft(next)),
                score,
                progress.LastReadUtc ?? DateTime.MinValue));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Last)
            .ThenBy(x => x.Item.SeriesName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.Item)
            .ToList();
    }

    /// <summary>For every reading list with at least one read entry, the first entry not yet read.</summary>
    private static HashSet<int> NextIssuesOfStartedLists(IEnumerable<ReadingList> lists, Dictionary<int, Issue> issueById)
    {
        var next = new HashSet<int>();
        foreach (var list in lists)
        {
            var ordered = list.Items
                .OrderBy(i => i.SortOrder)
                .Select(i => issueById.TryGetValue(i.IssueId, out var issue) ? issue : null)
                .Where(i => i is not null)
                .Select(i => i!)
                .ToList();
            if (!ordered.Any(i => i.HasBeenRead()))
            {
                continue;
            }

            var first = ordered.FirstOrDefault(i => !i.HasBeenRead() && !i.IsPlaceholder);
            if (first is not null)
            {
                next.Add(first.Id);
            }
        }

        return next;
    }

    private static string IssueLabel(Issue issue) =>
        issue.EffectiveNumber() is { Length: > 0 } number ? $"#{number}" : issue.EffectiveTitle() ?? "Next issue";

    private static string ReasonText(UpNextReason reason, int unread, int? daysSinceLastRead) => reason switch
    {
        UpNextReason.AlmostDone => unread == 1 ? "Last issue of the series" : $"Almost done: {unread} issues left",
        UpNextReason.NextInList => "Next in your reading list",
        UpNextReason.Stalled => $"Stalled {daysSinceLastRead} days",
        _ => "You read this recently",
    };
}
