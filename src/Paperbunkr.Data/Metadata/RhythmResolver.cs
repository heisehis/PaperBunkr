using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Reading rhythm (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md §1): which weekday and hour you start reading
/// sessions, and how long they last. A session is one <see cref="ReadingEventKind.Opened"/> row, timestamped at its start. Backfilled
/// rows are excluded (their time of day is fabricated). Length figures use only sessions that carry
/// <see cref="ReadingEvent.ActiveSeconds"/> (written since 2026-10-06), and the result says how many that is, so the early-history gap shows.
/// </summary>
public static class RhythmResolver
{
    /// <summary>Fewer sessions than this and the card shows its empty state instead of a chart.</summary>
    public const int MinSessionsForChart = 5;

    private static readonly (string Label, int FromMinutes, int ToMinutes)[] Buckets =
    {
        ("<5 min", 0, 5), ("5-15", 5, 15), ("15-30", 15, 30), ("30-60", 30, 60), ("60+", 60, int.MaxValue),
    };

    public static RhythmData Build(PaperbunkrDbContext context, InsightsRange range, DateTime nowUtc, TimeZoneInfo? zone = null)
    {
        var events = context.ReadingEvents.AsNoTracking().ToList();
        return Compute(events, range, nowUtc, zone ?? TimeZoneInfo.Local);
    }

    internal static RhythmData Compute(IReadOnlyCollection<ReadingEvent> events, InsightsRange range, DateTime nowUtc, TimeZoneInfo zone)
    {
        var start = StatsResolver.RangeStartUtc(range, nowUtc);
        var sessions = ReadingEventFilters.WithoutBackfill(events)
            .Where(e => e.Kind == ReadingEventKind.Opened && (start is null || e.TimestampUtc >= start))
            .ToList();

        var grid = new int[7, 24];
        foreach (var e in sessions)
        {
            var local = ReadingEventFilters.ToLocal(e.TimestampUtc, zone);
            grid[(int)local.DayOfWeek, local.Hour]++;
        }

        var measured = sessions.Where(e => e.ActiveSeconds is > 0).Select(e => e.ActiveSeconds!.Value).ToList();
        var lengths = Buckets
            .Select(b => new SessionLengthBucket(b.Label, measured.Count(s => s / 60.0 >= b.FromMinutes && s / 60.0 < b.ToMinutes)))
            .ToList();

        int? peakDay = null, peakHour = null;
        int best = 0;
        for (int d = 0; d < 7; d++)
        {
            for (int h = 0; h < 24; h++)
            {
                if (grid[d, h] > best)
                {
                    best = grid[d, h];
                    peakDay = d;
                    peakHour = h;
                }
            }
        }

        return new RhythmData(
            grid,
            sessions.Count,
            measured.Count,
            lengths,
            measured.Count > 0 ? measured.Average() / 60.0 : null,
            peakDay,
            peakHour);
    }
}

/// <param name="SessionsByWeekdayHour">[weekday 0=Sunday .. 6, hour 0..23], local time.</param>
public sealed record RhythmData(
    int[,] SessionsByWeekdayHour,
    int SessionCount,
    int MeasuredSessionCount,
    IReadOnlyList<SessionLengthBucket> LengthBuckets,
    double? AverageSessionMinutes,
    int? PeakWeekday,
    int? PeakHour)
{
    public bool HasChart => SessionCount >= RhythmResolver.MinSessionsForChart;

    public static RhythmData Empty { get; } = new(new int[7, 24], 0, 0, Array.Empty<SessionLengthBucket>(), null, null, null);
}

public sealed record SessionLengthBucket(string Label, int Count);
