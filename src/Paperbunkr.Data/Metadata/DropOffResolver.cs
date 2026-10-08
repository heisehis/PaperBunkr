using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>A series the reader is currently on that sits one issue short of where they usually stop.</summary>
public sealed record DropOffSeries(int SeriesId, string SeriesName, int IssuesRead, int? NextIssueId);

/// <summary>
/// Insights → Today's "Drop-off watch" (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.3).
/// <see cref="Cliff"/> is the number of issues after which the reader most often stops a series.
/// </summary>
public sealed record DropOffWatch(int Cliff, int StalledSeriesCount, IReadOnlyList<DropOffSeries> AtRisk)
{
    /// <summary>A nudge, not an alarm: "You often stop after about 3 issues. 4 series are at issue 2."</summary>
    public string Headline
    {
        get
        {
            string stop = Cliff == 1 ? "You often stop after about 1 issue." : $"You often stop after about {Cliff} issues.";
            string many = AtRisk.Count == 1 ? "1 series is" : $"{AtRisk.Count} series are";
            return Cliff == 1 ? $"{stop} {many} on a first issue." : $"{stop} {many} at issue {Cliff - 1}.";
        }
    }
}

/// <summary>
/// Finds the reader's own drop-off point and the series now approaching it. Pure over per-series progress, with no tracking of its own:
/// read state and the reading-event log already say where every series was left.
/// </summary>
public static class DropOffResolver
{
    /// <summary>Fewer stalled series than this is too little history to call a pattern.</summary>
    public const int MinStalledSeries = 5;

    public const int AtRiskLimit = 8;

    public static DropOffWatch? Build(PaperbunkrDbContext context, DateTime nowUtc)
    {
        var series = context.Series.AsNoTracking().Include(s => s.Issues).ThenInclude(i => i.MetadataProposals).AsSplitQuery().ToList();
        var lastEvent = context.ReadingEvents.AsNoTracking()
            .Where(e => e.ItemType == ReadingItemType.Comic && e.SeriesId != null)
            .GroupBy(e => e.SeriesId!.Value)
            .Select(g => new { SeriesId = g.Key, Last = g.Max(e => e.TimestampUtc) })
            .ToDictionary(x => x.SeriesId, x => x.Last);

        return Build(series, id => lastEvent.TryGetValue(id, out var last) ? last : null, nowUtc);
    }

    /// <summary><paramref name="series"/> must have their issues loaded. Null when there is no pattern yet, or nothing is near it.</summary>
    public static DropOffWatch? Build(IEnumerable<Series> series, Func<int, DateTime?> lastEventUtc, DateTime nowUtc)
    {
        DateTime staleCutoff = nowUtc.AddDays(-InsightsResolver.StalledDays);
        var stallPoints = new List<int>();
        var active = new List<(Series Series, SeriesProgress Progress)>();

        foreach (var s in series)
        {
            // A series the reader dropped or finished on purpose says nothing about where they drift away, and one fully read is not left.
            if (s.ReadingStatus is ReadingStatus.Dropped or ReadingStatus.Completed)
            {
                continue;
            }

            var progress = SeriesProgress.Of(s.Issues, lastEventUtc(s.Id));
            if (progress.UnreadCount == 0 || progress.LastReadUtc is not { } last)
            {
                continue;
            }

            if (last < staleCutoff)
            {
                if (progress.ReadCount >= 1)
                {
                    stallPoints.Add(progress.ReadCount);
                }
            }
            else if (s.ReadingStatus != ReadingStatus.Paused)
            {
                active.Add((s, progress));
            }
        }

        if (stallPoints.Count < MinStalledSeries)
        {
            return null;
        }

        // The most common stall point; a tie goes to the earlier one (the nudge should come sooner, not later).
        int cliff = stallPoints.GroupBy(p => p).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;

        var atRisk = active
            .Where(a => a.Progress.ReadCount == cliff - 1)
            .OrderByDescending(a => a.Progress.LastReadUtc)
            .Take(AtRiskLimit)
            .Select(a => new DropOffSeries(a.Series.Id, a.Series.Name, a.Progress.ReadCount, NextIssueId(a.Series)))
            .ToList();

        return atRisk.Count == 0 ? null : new DropOffWatch(cliff, stallPoints.Count, atRisk);
    }

    /// <summary>The issue to open: the one in progress, else the first unread in reading order.</summary>
    private static int? NextIssueId(Series series) =>
        series.Issues
            .Where(i => !i.IsPlaceholder && !i.FileIsMissing && !i.HasBeenRead())
            .OrderByDescending(i => i.IsInProgress())
            .ThenBy(i => i.NumberSortKey() ?? float.MaxValue)
            .ThenBy(i => i.Id)
            .FirstOrDefault()?.Id;
}
