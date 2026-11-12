using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>One event bar on the Atlas. Months are absolute (<c>year*12 + month-1</c>), inclusive at both ends.</summary>
public sealed record AtlasBar(int EventId, string Name, int StartMonth, int EndMonth, int Row, double Thickness, int IssueCount, int Read,
    int ColorIndex, string YearsLabel)
{
    public double ReadFraction => IssueCount == 0 ? 0 : (double)Read / IssueCount;
    public string Tooltip => $"{Name} · {YearsLabel} · {IssueCount} issue{(IssueCount == 1 ? string.Empty : "s")} · {Math.Round(ReadFraction * 100)} % read";
}

/// <summary>A directional relation between two dated bars, drawn earlier → later.</summary>
public sealed record AtlasConnector(int EarlierEventId, int LaterEventId, bool IsYours);

/// <summary>The continuity Atlas (docs/superpowers/specs/2026-11-11-continuity-list-atlas-views-design.md §2).</summary>
public sealed record ContinuityAtlas(IReadOnlyList<AtlasBar> Bars, IReadOnlyList<AtlasConnector> Connectors,
    IReadOnlyList<(int EventId, string Name)> Undated, IReadOnlyDictionary<int, int> LooseByYear, int FirstMonth, int LastMonth, int RowCount)
{
    public static ContinuityAtlas Empty { get; } = new(Array.Empty<AtlasBar>(), Array.Empty<AtlasConnector>(), Array.Empty<(int, string)>(),
        new Dictionary<int, int>(), 0, 0, 0);

    public int MonthSpan => Bars.Count == 0 ? 0 : LastMonth - FirstMonth + 1;
}

/// <summary>
/// Lays the continuity's events out against time. Each dated event is a bar from its first to its last month (a year-only date covers the
/// whole year; one-sided dates make a one-month bar). Bars are packed greedily into rows in <see cref="EventChronology.Order"/> order, an
/// event taking the first row where it doesn't overlap a bar there plus a two-month gap. Thickness is the issue-count tercile (6/10/14 px).
/// </summary>
public static class ContinuityAtlasBuilder
{
    public const int GapMonths = 2;

    public static ContinuityAtlas Build(ContinuityMapData data, IReadOnlySet<int>? hiddenEventIds, Func<int, bool> isRead)
    {
        var events = data.Events.Where(e => hiddenEventIds is null || !hiddenEventIds.Contains(e.Id)).ToList();
        var dated = new List<(ContinuityEventData Event, int Start, int End)>();
        var undated = new List<(int, string)>();
        foreach (var e in events)
        {
            if (Months(e.Start, e.End) is { } span)
            {
                dated.Add((e, span.Start, span.End));
            }
            else
            {
                undated.Add((e.Id, e.Name));
            }
        }

        var loose = new Dictionary<int, int>();
        foreach (var row in data.LooseIssues)
        {
            int? key = ContinuityMapBuilder.SortKey(row);
            if (key is int k && k / 100 > 0)
            {
                loose[k / 100] = loose.GetValueOrDefault(k / 100) + 1;
            }
        }

        if (dated.Count == 0)
        {
            return ContinuityAtlas.Empty with { Undated = undated, LooseByYear = loose };
        }

        var order = EventChronology.Order(dated.Select(d => d.Event.Span).ToArray(),
            data.Relations.Select(r => (r.SourceId, r.TargetId, r.Type)));
        var byId = dated.ToDictionary(d => d.Event.Id);
        var sequence = order.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        var counts = sequence.Select(d => d.Event.Members.Count).OrderBy(c => c).ToArray();
        // Tercile cut points: the largest count in the lowest third, and in the middle third.
        (int low, int high) = counts.Length < 3 ? (int.MaxValue, int.MaxValue)
            : (counts[(counts.Length / 3) - 1], counts[(2 * counts.Length / 3) - 1]);

        var rows = new List<List<(int Start, int End)>>();
        var bars = new List<AtlasBar>();
        for (int i = 0; i < sequence.Count; i++)
        {
            var (e, start, end) = sequence[i];
            int row = rows.FindIndex(r => r.All(b => end + GapMonths < b.Start || start > b.End + GapMonths));
            if (row < 0)
            {
                rows.Add(new List<(int, int)>());
                row = rows.Count - 1;
            }

            rows[row].Add((start, end));
            int count = e.Members.Count;
            double thickness = counts.Length < 3 ? 10 : count <= low ? 6 : count <= high ? 10 : 14;
            int read = e.Members.Count(m => isRead(m.IssueId));
            bars.Add(new AtlasBar(e.Id, e.Name, start, end, row, thickness, count, read, i % 6, YearsLabel(start, end)));
        }

        var visible = bars.Select(b => b.EventId).ToHashSet();
        var connectors = new List<AtlasConnector>();
        foreach (var r in data.Relations)
        {
            if (EventChronology.Direction(r.SourceId, r.TargetId, r.Type) is { } d && visible.Contains(d.Earlier) && visible.Contains(d.Later)
                && !connectors.Any(c => c.EarlierEventId == d.Earlier && c.LaterEventId == d.Later))
            {
                connectors.Add(new AtlasConnector(d.Earlier, d.Later, r.IsYours));
            }
        }

        return new ContinuityAtlas(bars, connectors, undated, loose, bars.Min(b => b.StartMonth), bars.Max(b => b.EndMonth), rows.Count);
    }

    /// <summary>Absolute first and last month for a <c>year*100+month</c> span; null when neither end is dated.</summary>
    public static (int Start, int End)? Months(int? startKey, int? endKey)
    {
        int? start = startKey is int s && s / 100 > 0 ? (s / 100 * 12) + (s % 100 is >= 1 and <= 12 ? s % 100 - 1 : 0) : null;
        int? end = endKey is int e && e / 100 > 0 ? (e / 100 * 12) + (e % 100 is >= 1 and <= 12 ? e % 100 - 1 : 11) : null;
        if (start is null && end is null)
        {
            return null;
        }

        // A one-sided date is a one-month bar; a year-only start with no end covers that year.
        if (start is null)
        {
            start = end;
        }
        else if (end is null)
        {
            end = startKey % 100 == 0 ? start + 11 : start;
        }

        return end < start ? (end!.Value, start!.Value) : (start!.Value, end!.Value);
    }

    private static string YearsLabel(int startMonth, int endMonth)
    {
        int first = startMonth / 12, last = endMonth / 12;
        return first == last ? first.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{first}–{last % 100:00}";
    }
}
