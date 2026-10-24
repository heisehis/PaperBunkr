using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// "Where you stop" (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md §2): the share of series you stopped versus
/// finished, by publisher, genre and content type, plus how far in stopped series stalled. Complements <see cref="DropOffResolver"/>
/// (Today's single most common stall point) and reuses its definitions so the two never disagree: stalled = at least one issue read,
/// issues left, no touch for <see cref="InsightsResolver.StalledDays"/>; read = the 95% rule via <see cref="SeriesProgress"/>.
/// </summary>
public static class DropOffBreakdownResolver
{
    /// <summary>A bucket needs this many decided (stopped + finished) series to get its own row; smaller ones fold into "Other".</summary>
    public const int MinDecidedPerBucket = 5;

    public const string UnknownLabel = "Unknown";

    public const string OtherLabel = "Other";

    private static readonly (string Label, int From, int To)[] StallBuckets =
    {
        ("1", 1, 1), ("2", 2, 2), ("3", 3, 3), ("4-5", 4, 5), ("6-10", 6, 10), ("11+", 11, int.MaxValue),
    };

    public static DropOffBreakdown Build(PaperbunkrDbContext context, DateTime nowUtc)
    {
        var issues = context.Issues.AsNoTrackingWithIdentityResolution()
            .Include(i => i.Series)
            .Include(i => i.Tags)
            .Where(i => !i.IsPlaceholder)
            .ToList();
        var lastEvent = context.ReadingEvents.AsNoTracking()
            .Where(e => e.ItemType == ReadingItemType.Comic && e.SeriesId != null)
            .GroupBy(e => e.SeriesId!.Value)
            .Select(g => new { SeriesId = g.Key, Last = g.Max(e => e.TimestampUtc) })
            .ToDictionary(x => x.SeriesId, x => (DateTime?)x.Last);

        var series = new List<DropOffSeriesInput>();
        foreach (var group in issues.Where(i => i.Series is not null).GroupBy(i => i.Series!))
        {
            var s = group.Key;
            var progress = SeriesProgress.Of(group, lastEvent.TryGetValue(s.Id, out var last) ? last : null);
            series.Add(new DropOffSeriesInput(
                s.Id,
                s.ReadingStatus,
                s.ContentType,
                Mode(group.Select(i => i.Publisher)) ?? s.Publisher,
                Mode(group.SelectMany(i => i.Tags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value))),
                progress.ReadCount,
                progress.UnreadCount,
                progress.LastReadUtc));
        }

        return Compute(series, nowUtc);
    }

    internal static DropOffBreakdown Compute(IReadOnlyCollection<DropOffSeriesInput> series, DateTime nowUtc)
    {
        var stopped = new List<(DropOffSeriesInput S, bool Stopped)>();
        foreach (var s in series)
        {
            bool? isStopped = Classify(s, nowUtc);
            if (isStopped is { } v)
            {
                stopped.Add((s, v));
            }
        }

        var stoppedSeries = stopped.Where(x => x.Stopped).Select(x => x.S).ToList();
        var stall = StallBuckets
            .Select(b => new SessionLengthBucket(b.Label, stoppedSeries.Count(s => s.ReadCount >= b.From && s.ReadCount <= b.To)))
            .ToList();

        return new DropOffBreakdown(
            Rates(stopped, s => Label(s.Publisher)),
            Rates(stopped, s => Label(s.Genre)),
            Rates(stopped, s => s.ContentType == ContentType.Unknown ? UnknownLabel : s.ContentType.ToString()),
            stall,
            stoppedSeries.Count,
            stopped.Count - stoppedSeries.Count);
    }

    /// <summary>True = stopped, false = finished, null = neither (in progress and recent, or never started).</summary>
    internal static bool? Classify(DropOffSeriesInput s, DateTime nowUtc)
    {
        if (s.Status == ReadingStatus.Dropped)
        {
            return true;
        }

        if (s.Status == ReadingStatus.Completed || (s.ReadCount >= 1 && s.UnreadCount == 0))
        {
            return false;
        }

        if (s.ReadCount >= 1 && s.UnreadCount >= 1 && s.LastReadUtc is { } last
            && (nowUtc - last).TotalDays > InsightsResolver.StalledDays)
        {
            return true;
        }

        return null;
    }

    private static IReadOnlyList<DropOffRate> Rates(List<(DropOffSeriesInput S, bool Stopped)> decided, Func<DropOffSeriesInput, string> key)
    {
        var groups = decided.GroupBy(x => key(x.S))
            .Select(g => new DropOffRate(g.Key, g.Count(x => x.Stopped), g.Count(x => !x.Stopped)))
            .ToList();

        var kept = groups.Where(g => g.Decided >= MinDecidedPerBucket).OrderByDescending(g => g.Decided).ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var folded = groups.Where(g => g.Decided < MinDecidedPerBucket).ToList();
        if (folded.Count > 0)
        {
            kept.Add(new DropOffRate(OtherLabel, folded.Sum(g => g.Stopped), folded.Sum(g => g.Finished)));
        }

        return kept;
    }

    private static string Label(string? value) => string.IsNullOrWhiteSpace(value) ? UnknownLabel : value.Trim();

    private static string? Mode(IEnumerable<string?> values) => values
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.Key)
        .FirstOrDefault();
}

internal sealed record DropOffSeriesInput(
    int Id, ReadingStatus Status, ContentType ContentType, string? Publisher, string? Genre, int ReadCount, int UnreadCount, DateTime? LastReadUtc);

public sealed record DropOffBreakdown(
    IReadOnlyList<DropOffRate> ByPublisher,
    IReadOnlyList<DropOffRate> ByGenre,
    IReadOnlyList<DropOffRate> ByContentType,
    IReadOnlyList<SessionLengthBucket> StallPoints,
    int StoppedTotal,
    int FinishedTotal)
{
    public bool HasData => StoppedTotal + FinishedTotal > 0;

    public static DropOffBreakdown Empty { get; } = new(
        Array.Empty<DropOffRate>(), Array.Empty<DropOffRate>(), Array.Empty<DropOffRate>(), Array.Empty<SessionLengthBucket>(), 0, 0);
}

public sealed record DropOffRate(string Label, int Stopped, int Finished)
{
    public int Decided => Stopped + Finished;

    /// <summary>0..1 share of decided series that were stopped.</summary>
    public double StoppedRate => Decided == 0 ? 0 : (double)Stopped / Decided;
}
