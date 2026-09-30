using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.ContinuityScreen;

/// <summary>A continuation you don't own, from a GCD series bond: the member series <see cref="OwnedSeriesId"/> continues as it
/// (<see cref="OwnedIsOlder"/>) or it continues as the member series.</summary>
public sealed record OverviewPlaceholder(int GcdSeriesId, string Name, int? YearBegan, int OwnedSeriesId, bool OwnedIsOlder);

/// <summary>A weak smart-connector inference between two of the continuity's events, offered in the attention flyout.</summary>
public sealed record OverviewConnection(int SourceEventId, int TargetEventId, RelationType Type, string Headline, string Reason);

/// <summary>Everything <see cref="ContinuityOverviewBuilder"/> needs, loaded by <see cref="ContinuityOverviewLoader"/>.</summary>
public sealed record ContinuityOverviewInput(
    ContinuityMapData Map,
    IReadOnlyList<(int Newer, int Older)> Continuations,
    IReadOnlyList<OverviewPlaceholder> Placeholders,
    IReadOnlyList<OverviewConnection> Connections,
    bool HasGcdMatch);

/// <summary>One poster in a run: a member series, or a dimmed placeholder for a continuation that isn't in the library.</summary>
public sealed record OverviewRunItem(int? SeriesId, string Name, string Years, bool IsPlaceholder, string? Url, bool ArrowBefore);

/// <summary>Series joined by Continuation relations, oldest first ("Hulk · 1968–2026 · 5 series").</summary>
public sealed record OverviewRun(string Label, IReadOnlyList<OverviewRunItem> Items);

/// <summary>A member series' own numbers: years (GCD on-sale dates where matched), issues and how many are read.</summary>
public sealed record OverviewSeriesStats(int SeriesId, int? Start, int? End, int IssueCount, int ReadCount)
{
    public string Years => ContinuityOverviewBuilder.YearsLabel(Start, End);
}

/// <summary>A row of "Events in order". <see cref="LinkBelow"/> is the faint line to the next row when the two are linked.</summary>
public sealed record OverviewEventRow(int EventId, string Name, string Years, int IssueCount, int ReadCount, string? LinkBelow)
{
    /// <summary>"14 issues · 9 read".</summary>
    public string CountLabel => $"{ContinuityOverviewBuilder.Plural(IssueCount, "issue", "issues")} · {ReadCount.ToString("N0", CultureInfo.CurrentCulture)} read";

    public double ReadFraction => IssueCount <= 0 ? 0 : (double)ReadCount / IssueCount;

    public bool HasLink => LinkBelow is not null;
}

public enum AttentionKind
{
    Duplicates,
    Connections,
    OutsideSeries,
    RoleSuggestions,
    IssueSuggestions,
    Duplicate,
}

/// <summary>One "Needs attention" item.</summary>
public sealed record AttentionItem(AttentionKind Kind, string Text, int Count);

/// <summary>A series that the continuity's events pull in but that isn't a member.</summary>
public sealed record OutsideSeriesInfo(int SeriesId, string Name, int IssueCount);

/// <summary>The next issue to read. <see cref="EventId"/> anchors the reader to that event's order; null reads in series order.</summary>
public sealed record ContinueTarget(int IssueId, string Label, int? EventId);

/// <summary>What a continuity's Overview shows.</summary>
public sealed record ContinuityOverview(
    string StatsLine,
    bool ShowGcdChip,
    IReadOnlyList<OverviewRun> Runs,
    IReadOnlyList<int> Standalone,
    IReadOnlyDictionary<int, OverviewSeriesStats> SeriesStats,
    IReadOnlyList<OverviewEventRow> Events,
    IReadOnlyList<AttentionItem> Attention,
    IReadOnlyList<OutsideSeriesInfo> OutsideSeries,
    IReadOnlyList<OverviewConnection> Connections,
    ContinueTarget? Continue,
    IReadOnlyList<string> CollageKeys);

/// <summary>
/// The continuity page's Overview (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md): runs of continued series with
/// placeholders for continuations you don't own, the stats line, events in the smart connector's order, the attention items and the
/// Continue target. Pure - the loader does the database and GCD work, so this is tested on its own.
/// </summary>
public static class ContinuityOverviewBuilder
{
    public const int CollageSize = 8;

    private static readonly Regex YearSuffix = new(@"\s*\((?:19|20)\d{2}\)\s*$", RegexOptions.Compiled);

    public static ContinuityOverview Build(ContinuityOverviewInput input)
    {
        var map = input.Map;
        var memberIds = map.MemberLanes.Select(l => l.SeriesId).ToHashSet();
        var names = map.MemberLanes.ToDictionary(l => l.SeriesId, l => l.Name);

        // Every member-series issue appears once: either loose or in an event (an event touching the continuity).
        var memberRows = map.LooseIssues
            .Concat(map.Events.SelectMany(e => e.Members))
            .Where(r => memberIds.Contains(r.SeriesId))
            .GroupBy(r => r.IssueId)
            .Select(g => g.First())
            .ToList();

        var stats = map.MemberLanes.ToDictionary(l => l.SeriesId, l =>
        {
            var rows = memberRows.Where(r => r.SeriesId == l.SeriesId).ToList();
            var years = rows.Select(YearOf).OfType<int>().ToList();
            return new OverviewSeriesStats(l.SeriesId, years.Count > 0 ? years.Min() : null, years.Count > 0 ? years.Max() : null,
                rows.Count, rows.Count(r => r.ReadState == EventMapReadState.Read));
        });

        var (runs, standalone) = BuildRuns(map.MemberLanes, names, stats, input.Continuations, input.Placeholders);
        var events = OrderEvents(map);
        var outside = map.Events.SelectMany(e => e.Members)
            .Where(r => !memberIds.Contains(r.SeriesId))
            .GroupBy(r => r.SeriesId)
            .Select(g => new OutsideSeriesInfo(g.Key, g.First().SeriesName, g.Select(r => r.IssueId).Distinct().Count()))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var attention = new List<AttentionItem>();
        if (map.PendingDuplicatePairs > 0)
        {
            attention.Add(new AttentionItem(AttentionKind.Duplicates,
                Plural(map.PendingDuplicatePairs, "possible duplicate event", "possible duplicate events"), map.PendingDuplicatePairs));
        }

        if (input.Connections.Count > 0)
        {
            attention.Add(new AttentionItem(AttentionKind.Connections,
                Plural(input.Connections.Count, "event connection to review", "event connections to review"), input.Connections.Count));
        }

        if (outside.Count > 0)
        {
            attention.Add(new AttentionItem(AttentionKind.OutsideSeries,
                outside.Count == 1 ? "1 series from its events isn't in this continuity"
                    : $"{outside.Count} series from its events aren't in this continuity", outside.Count));
        }

        var allYears = memberRows.Select(YearOf).OfType<int>().ToList();
        int read = memberRows.Count(r => r.ReadState == EventMapReadState.Read);
        var parts = new List<string>();
        if (allYears.Count > 0)
        {
            parts.Add(YearsLabel(allYears.Min(), allYears.Max()));
        }

        parts.Add(Plural(map.MemberLanes.Count, "series", "series"));
        parts.Add(Plural(memberRows.Count, "issue", "issues"));
        if (map.Events.Count > 0)
        {
            parts.Add(Plural(map.Events.Count, "event", "events"));
        }

        if (memberRows.Count > 0)
        {
            parts.Add($"{Percent(read, memberRows.Count)}% read");
        }

        var collage = map.MemberLanes
            .Select(l => memberRows.Where(r => r.SeriesId == l.SeriesId && r.CoverKey is not null)
                .OrderBy(r => ContinuityMapBuilder.SortKey(r) ?? int.MaxValue).Select(r => r.CoverKey!).FirstOrDefault())
            .OfType<string>()
            .Take(CollageSize)
            .ToList();

        return new ContinuityOverview(string.Join(" · ", parts), input.HasGcdMatch, runs, standalone, stats, events, attention, outside,
            input.Connections, ContinueFor(map), collage);
    }

    /// <summary>First unread (or part-read) issue you have, in the continuity map's own order; null once everything is read.</summary>
    public static ContinueTarget? ContinueFor(ContinuityMapData map)
    {
        var rows = ContinuityMapBuilder.Build(map, ContinuityMapOptions.Default).Rows;
        var next = rows.FirstOrDefault(r => r.ReadState != EventMapReadState.Read && !r.FileIsMissing);
        return next is null ? null : new ContinueTarget(next.IssueId, IssueLabel(next.SeriesName, next.Number), next.OwnerEventId);
    }

    internal static string IssueLabel(string seriesName, string number) =>
        string.IsNullOrWhiteSpace(number) ? seriesName : $"{seriesName} #{number}";

    /// <summary>"1968–2026", "2007", or empty when nothing is dated.</summary>
    public static string YearsLabel(int? start, int? end) => (start, end) switch
    {
        (int s, int e) when s != e => $"{s.ToString(CultureInfo.InvariantCulture)}–{e.ToString(CultureInfo.InvariantCulture)}",
        (int s, _) => s.ToString(CultureInfo.InvariantCulture),
        (null, int e) => e.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    internal static string Plural(int count, string one, string many) =>
        $"{count.ToString("N0", CultureInfo.CurrentCulture)} {(count == 1 ? one : many)}";

    internal static int Percent(int part, int whole) => whole <= 0 ? 0 : (int)Math.Round(100.0 * part / whole, MidpointRounding.AwayFromZero);

    private static int? YearOf(EventMapRow row) => ContinuityMapBuilder.SortKey(row) is int key ? key / 100 : null;

    /// <summary>
    /// Runs: the connected groups of two or more series (members plus placeholders) joined by Continuation links, each laid out older
    /// to newer - a topological order with ties (branches) and cycles settled by start year. Everything else is Standalone, by start
    /// year.
    /// </summary>
    internal static (List<OverviewRun> Runs, List<int> Standalone) BuildRuns(
        IReadOnlyList<ContinuityLane> members,
        IReadOnlyDictionary<int, string> names,
        IReadOnlyDictionary<int, OverviewSeriesStats> stats,
        IReadOnlyList<(int Newer, int Older)> continuations,
        IReadOnlyList<OverviewPlaceholder> placeholders)
    {
        // Node ids: member series ids; placeholders are the negative GCD id so the two never collide.
        var memberIds = members.Select(m => m.SeriesId).ToHashSet();
        var edges = new HashSet<(int Older, int Newer)>();
        foreach (var (newer, older) in continuations)
        {
            if (newer != older && memberIds.Contains(newer) && memberIds.Contains(older))
            {
                edges.Add((older, newer));
            }
        }

        var placeholderById = new Dictionary<int, OverviewPlaceholder>();
        foreach (var p in placeholders.Where(p => memberIds.Contains(p.OwnedSeriesId)))
        {
            int node = -p.GcdSeriesId;
            placeholderById.TryAdd(node, p);
            edges.Add(p.OwnedIsOlder ? (p.OwnedSeriesId, node) : (node, p.OwnedSeriesId));
        }

        var nodes = memberIds.Concat(placeholderById.Keys).ToHashSet();
        var parent = nodes.ToDictionary(n => n, n => n);
        int Find(int n) => parent[n] == n ? n : parent[n] = Find(parent[n]);
        foreach (var (older, newer) in edges)
        {
            parent[Find(older)] = Find(newer);
        }

        int? StartOf(int node) => node > 0 ? stats.GetValueOrDefault(node)?.Start : placeholderById[node].YearBegan;
        int? EndOf(int node) => node > 0 ? stats.GetValueOrDefault(node)?.End : placeholderById[node].YearBegan;
        string NameOf(int node) => node > 0 ? names.GetValueOrDefault(node, "Series") : placeholderById[node].Name;
        (int, string) Rank(int node) => (StartOf(node) ?? int.MaxValue, NameOf(node));

        var runs = new List<(int Start, OverviewRun Run)>();
        var inRun = new HashSet<int>();
        foreach (var group in nodes.GroupBy(Find).Where(g => g.Count() >= 2 && g.Any(n => n > 0)))
        {
            var groupNodes = group.ToHashSet();
            var groupEdges = edges.Where(e => groupNodes.Contains(e.Older) && groupNodes.Contains(e.Newer)).ToList();
            var ordered = TopologicalByYear(groupNodes, groupEdges, Rank);

            var items = ordered.Select((node, i) => node > 0
                    ? new OverviewRunItem(node, NameOf(node), stats.GetValueOrDefault(node)?.Years ?? string.Empty, false, null, i > 0)
                    : new OverviewRunItem(null, placeholderById[node].YearBegan is int y ? $"{placeholderById[node].Name} ({y})" : placeholderById[node].Name,
                        placeholderById[node].YearBegan?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, true,
                        GcdLinks.Series(placeholderById[node].GcdSeriesId), i > 0))
                .ToList();

            var starts = ordered.Select(StartOf).OfType<int>().ToList();
            var ends = ordered.Select(EndOf).OfType<int>().ToList();
            int firstOwned = ordered.First(n => n > 0);
            string label = string.Join(" · ", new[]
            {
                YearSuffix.Replace(NameOf(firstOwned), string.Empty),
                YearsLabel(starts.Count > 0 ? starts.Min() : null, ends.Count > 0 ? ends.Max() : null),
                Plural(items.Count, "series", "series"),
            }.Where(s => s.Length > 0));

            runs.Add((starts.Count > 0 ? starts.Min() : int.MaxValue, new OverviewRun(label, items)));
            inRun.UnionWith(groupNodes);
        }

        var standalone = members.Select(m => m.SeriesId).Where(id => !inRun.Contains(id))
            .OrderBy(id => stats.GetValueOrDefault(id)?.Start ?? int.MaxValue)
            .ThenBy(id => names.GetValueOrDefault(id, string.Empty), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return (runs.OrderBy(r => r.Start).ThenBy(r => r.Run.Label, StringComparer.CurrentCultureIgnoreCase).Select(r => r.Run).ToList(), standalone);
    }

    /// <summary>Kahn's algorithm, always taking the earliest-starting ready node; a cycle is broken at its earliest-starting node.</summary>
    private static List<int> TopologicalByYear(IReadOnlySet<int> nodes, IReadOnlyList<(int Older, int Newer)> edges, Func<int, (int, string)> rank)
    {
        var comparer = Comparer<(int, string)>.Create((a, b) =>
        {
            int c = a.Item1.CompareTo(b.Item1);
            return c != 0 ? c : StringComparer.CurrentCultureIgnoreCase.Compare(a.Item2, b.Item2);
        });
        var incoming = nodes.ToDictionary(n => n, n => edges.Count(e => e.Newer == n));
        var result = new List<int>();
        var remaining = nodes.ToHashSet();
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(n => incoming[n] == 0).ToList();
            int next = (ready.Count > 0 ? ready : remaining.ToList()).OrderBy(rank, comparer).ThenBy(n => n).First();
            remaining.Remove(next);
            result.Add(next);
            foreach (var edge in edges.Where(e => e.Older == next && remaining.Contains(e.Newer)))
            {
                incoming[edge.Newer]--;
            }
        }

        return result;
    }

    /// <summary>Events in <see cref="EventChronology"/> order, with "↓ prequel of" / "↓ continued by" between linked neighbours.</summary>
    private static List<OverviewEventRow> OrderEvents(ContinuityMapData map)
    {
        var relations = map.Relations.Select(r => (r.SourceId, r.TargetId, r.Type)).ToList();
        var order = EventChronology.Order(map.Events.Select(e => new EventSpan(e.Id, e.Name, e.Start, e.End)).ToList(), relations);
        var byId = map.Events.ToDictionary(e => e.Id);
        var rows = new List<OverviewEventRow>();
        for (int i = 0; i < order.Count; i++)
        {
            var e = byId[order[i]];
            string? link = null;
            if (i + 1 < order.Count)
            {
                int next = order[i + 1];
                foreach (var r in map.Relations)
                {
                    if (EventChronology.Direction(r.SourceId, r.TargetId, r.Type) is { } d && d.Earlier == e.Id && d.Later == next)
                    {
                        link = r.Type == RelationType.Continuation ? "↓ continued by" : "↓ prequel of";
                        break;
                    }
                }
            }

            var issues = e.Members.Select(m => m.IssueId).Distinct().Count();
            var read = e.Members.Where(m => m.ReadState == EventMapReadState.Read).Select(m => m.IssueId).Distinct().Count();
            rows.Add(new OverviewEventRow(e.Id, e.Name, YearsLabel(e.Start / 100, e.End / 100), issues, read, link));
        }

        return rows;
    }
}
