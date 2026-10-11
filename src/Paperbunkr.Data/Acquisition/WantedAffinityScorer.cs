using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Acquisition;

/// <summary>How likely the reader is to read more of a watched series, 0-100, and why in words (the Wanted screen's tooltip).</summary>
public sealed record WantedAffinity(double Score, string Why);

/// <summary>
/// Ranks watched series for the Wanted screen's "Most likely to read" sort (docs/superpowers/specs/2026-10-06-smart-features-design.md
/// §4.5). Three signals, all from what the reader already did: how much of the linked local series they finished, how recently they
/// read it, and how much of what they finish comes from the series' publisher. A watched series with no local series has only the
/// publisher signal. Wanted items carry no creator data, so creators play no part.
/// </summary>
public static class WantedAffinityScorer
{
    internal const double FinishWeight = 50;
    internal const double RecencyWeight = 30;
    internal const double PublisherWeight = 20;
    internal const int RecencyHorizonDays = 90;

    public static Dictionary<int, WantedAffinity> Score(PaperbunkrDbContext context, IReadOnlyCollection<WatchedSeries> watched, DateTime nowUtc)
    {
        var localIds = watched.Where(w => w.SeriesId != null).Select(w => w.SeriesId!.Value).Distinct().ToList();
        var issuesBySeries = context.Issues.AsNoTracking()
            .Where(i => localIds.Contains(i.SeriesId))
            .ToList()
            .GroupBy(i => i.SeriesId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var lastEvent = context.ReadingEvents.AsNoTracking()
            .Where(e => e.ItemType == ReadingItemType.Comic && e.SeriesId != null && localIds.Contains(e.SeriesId.Value))
            .GroupBy(e => e.SeriesId!.Value)
            .Select(g => new { SeriesId = g.Key, Last = g.Max(e => e.TimestampUtc) })
            .ToDictionary(x => x.SeriesId, x => x.Last);
        var finishedPublishers = context.ReadingEvents.AsNoTracking()
            .Where(e => e.ItemType == ReadingItemType.Comic && e.Kind == ReadingEventKind.Finished && e.Publisher != null && e.Publisher != "")
            .Select(e => e.Publisher!)
            .ToList();

        var progress = issuesBySeries.ToDictionary(
            kv => kv.Key,
            kv => SeriesProgress.Of(kv.Value, lastEvent.TryGetValue(kv.Key, out var last) ? last : null));

        return watched.ToDictionary(
            w => w.Id,
            w => Score(w.Publisher, w.SeriesId is int id && progress.TryGetValue(id, out var p) ? p : null, finishedPublishers, nowUtc));
    }

    /// <summary>Pure: one watched series from its local progress (null when it is not in the library) and the publishers of everything finished.</summary>
    public static WantedAffinity Score(string? publisher, SeriesProgress? local, IReadOnlyCollection<string> finishedPublishers, DateTime nowUtc)
    {
        var why = new List<string>();
        double score = 0;

        if (local is { } progress && progress.ReadCount + progress.UnreadCount > 0)
        {
            double finished = (double)progress.ReadCount / (progress.ReadCount + progress.UnreadCount);
            score += FinishWeight * finished;
            if (progress.ReadCount > 0)
            {
                why.Add($"You finished {Percent(finished)} of the issues you own");
            }

            if (progress.DaysSinceLastRead(nowUtc) is int days)
            {
                score += RecencyWeight * Math.Max(0, 1 - (double)days / RecencyHorizonDays);
                why.Add(days == 0 ? "read today" : days == 1 ? "read yesterday" : $"read {days} days ago");
            }
        }

        if (!string.IsNullOrWhiteSpace(publisher) && finishedPublishers.Count > 0)
        {
            double share = (double)finishedPublishers.Count(p => string.Equals(p, publisher, StringComparison.OrdinalIgnoreCase)) / finishedPublishers.Count;
            score += PublisherWeight * share;
            if (share >= 0.05)
            {
                why.Add($"{publisher} is {Percent(share)} of what you finish");
            }
        }

        return new WantedAffinity(Math.Round(score, 2), why.Count == 0 ? "Nothing read from this series or its publisher yet" : string.Join(" · ", why));
    }

    // Not the "P0" format: its spacing and symbol placement follow the OS culture ("80 %" in some), and this is one fixed UI string.
    private static string Percent(double fraction) => $"{(int)Math.Round(fraction * 100)}%";
}
