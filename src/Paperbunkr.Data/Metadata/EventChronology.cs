using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// An event's name and date span as <c>year*100+month</c> keys (month 0 when unknown); null when nothing is dated.
/// <paramref name="SortKey"/> is where the event is placed when an outside reading order moved it (see <see cref="EventChronology.ApplyReadingOrder"/>);
/// null = place it by its own dates. <paramref name="Rank"/> is its position in that reading order, used to break ties.
/// </summary>
public sealed record EventSpan(int EventId, string Name, int? Start, int? End, int? SortKey = null, int? Rank = null);

/// <summary>
/// Puts story events in chronological order (docs/superpowers/specs/2026-09-27-continuity-map-design.md §2). Directional relations
/// win; among events with nothing required before them the earliest goes first, then the name; a loop is broken by taking the
/// earliest-dated remaining event. Direction conventions: Prequel = source first; Sequel = source later; Continuation = source
/// continues target, so source later. Crossover, SameUniverse, SharedUniverse, Related and Other carry no order.
/// </summary>
public static class EventChronology
{
    /// <summary>(earlier, later) for a directional relation, or null when the type carries no order.</summary>
    public static (int Earlier, int Later)? Direction(int sourceEventId, int targetEventId, RelationType type) => type switch
    {
        RelationType.Prequel => (sourceEventId, targetEventId),
        RelationType.Sequel => (targetEventId, sourceEventId),
        RelationType.Continuation => (targetEventId, sourceEventId),
        _ => null,
    };

    public static bool IsDirectional(RelationType type) => type is RelationType.Prequel or RelationType.Sequel or RelationType.Continuation;

    /// <summary><c>year*100+month</c> for a dated issue (month 0 when unknown); null when the year is unknown.</summary>
    public static int? DateKey(int? year, int? month) => year is int y ? (y * 100) + (month is >= 1 and <= 12 ? month.Value : 0) : null;

    public static int? DateKey(DateTime? date) => date is DateTime d ? (d.Year * 100) + d.Month : null;

    /// <summary>Order <paramref name="events"/> by the directional <paramref name="relations"/> among them, then by date and name.</summary>
    public static IReadOnlyList<int> Order(IReadOnlyCollection<EventSpan> events, IEnumerable<(int Source, int Target, RelationType Type)> relations)
    {
        var byId = events.ToDictionary(e => e.EventId);
        var successors = byId.Keys.ToDictionary(id => id, _ => new HashSet<int>());
        var predecessors = byId.Keys.ToDictionary(id => id, _ => new HashSet<int>());
        foreach (var (source, target, type) in relations)
        {
            if (Direction(source, target, type) is not { } direction)
            {
                continue;
            }

            var (earlier, later) = direction;
            if (earlier == later || !byId.ContainsKey(earlier) || !byId.ContainsKey(later))
            {
                continue;
            }

            successors[earlier].Add(later);
            predecessors[later].Add(earlier);
        }

        var remaining = new HashSet<int>(byId.Keys);
        var order = new List<int>(byId.Count);
        IEnumerable<EventSpan> Sorted(IEnumerable<int> ids) => ids.Select(id => byId[id])
            .OrderBy(e => e.SortKey ?? e.Start ?? e.End ?? int.MaxValue)
            .ThenBy(e => e.Rank ?? int.MaxValue)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.EventId);

        while (remaining.Count > 0)
        {
            var next = Sorted(remaining.Where(id => predecessors[id].Count == 0)).FirstOrDefault()
                       ?? Sorted(remaining).First();      // a loop: take the earliest-dated remaining event
            remaining.Remove(next.EventId);
            order.Add(next.EventId);
            foreach (int later in successors[next.EventId])
            {
                predecessors[later].Remove(next.EventId);
            }

            foreach (int id in remaining)
            {
                predecessors[id].Remove(next.EventId);
            }
        }

        return order;
    }

    /// <summary>
    /// The loops among the directional <paramref name="relations"/>: groups of events each of which is, by those relations, both before
    /// and after the others ("A is a prequel of B" and "B is a prequel of A"). <see cref="Order"/> still returns an order for these by
    /// breaking the loop at the earliest-dated event, silently; this is how a caller finds out and says so
    /// (docs/superpowers/specs/2026-10-06-smart-features-design.md §7.2). Each loop is listed once, smallest event id first.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<int>> FindCycles(IEnumerable<int> eventIds, IEnumerable<(int Source, int Target, RelationType Type)> relations)
    {
        var ids = eventIds.ToHashSet();
        var successors = ids.ToDictionary(id => id, _ => new HashSet<int>());
        foreach (var (source, target, type) in relations)
        {
            if (Direction(source, target, type) is { } d && d.Earlier != d.Later && ids.Contains(d.Earlier) && ids.Contains(d.Later))
            {
                successors[d.Earlier].Add(d.Later);
            }
        }

        // Tarjan's strongly connected components, iteratively (an event graph can be deep enough to make recursion a risk).
        var index = new Dictionary<int, int>();
        var low = new Dictionary<int, int>();
        var onStack = new HashSet<int>();
        var stack = new Stack<int>();
        var cycles = new List<IReadOnlyList<int>>();
        int next = 0;

        foreach (int root in ids.OrderBy(id => id))
        {
            if (index.ContainsKey(root))
            {
                continue;
            }

            var work = new Stack<(int Node, IEnumerator<int> Edges)>();
            index[root] = low[root] = next++;
            stack.Push(root);
            onStack.Add(root);
            work.Push((root, successors[root].OrderBy(id => id).GetEnumerator()));

            while (work.Count > 0)
            {
                var (node, edges) = work.Peek();
                if (edges.MoveNext())
                {
                    int to = edges.Current;
                    if (!index.ContainsKey(to))
                    {
                        index[to] = low[to] = next++;
                        stack.Push(to);
                        onStack.Add(to);
                        work.Push((to, successors[to].OrderBy(id => id).GetEnumerator()));
                    }
                    else if (onStack.Contains(to))
                    {
                        low[node] = Math.Min(low[node], index[to]);
                    }

                    continue;
                }

                work.Pop();
                if (work.Count > 0)
                {
                    int parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }

                if (low[node] == index[node])
                {
                    var component = new List<int>();
                    int member;
                    do
                    {
                        member = stack.Pop();
                        onStack.Remove(member);
                        component.Add(member);
                    }
                    while (member != node);

                    if (component.Count > 1)
                    {
                        component.Sort();
                        cycles.Add(component);
                    }
                }
            }
        }

        return cycles.OrderBy(c => c[0]).ToList();
    }

    /// <summary>
    /// An issue's date key: the Grand Comics Database on-sale (else key) date when the issue is matched and <paramref name="gcdDates"/>
    /// has it (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4), else its cover date.
    /// </summary>
    public static int? IssueDateKey(int? year, int? month, int? gcdIssueId, IReadOnlyDictionary<int, int>? gcdDates) =>
        gcdIssueId is int g && gcdDates is not null && gcdDates.TryGetValue(g, out int date) ? date : DateKey(year, month);

    /// <summary>
    /// Date spans for events: their own <c>StartDate</c>/<c>EndDate</c> when set, else the earliest/latest member date
    /// (<see cref="IssueDateKey"/>: GCD's on-sale dates when <paramref name="gcd"/> is given, cover dates otherwise).
    /// </summary>
    public static Dictionary<int, EventSpan> LoadSpans(PaperbunkrDbContext context, IReadOnlyCollection<int>? eventIds = null, Gcd.GcdDataStore? gcd = null)
    {
        // Comic Book Reading Orders' story order holds ranked events to its sequence (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4.2).
        // The whole ranked list is fitted, not just the requested events, so a subset (one continuity) is placed the same as the whole library.
        var ranked = context.EventOrderEvidence.AsNoTracking()
            .Where(e => e.Provider == EventOrderProvider.Cbro && e.Rank != null && e.ListKey != null)
            .Select(e => new { e.StoryEventId, e.ListKey, e.Rank })
            .ToList();
        if (ranked.Count == 0)
        {
            return LoadOwnSpans(context, eventIds, gcd);
        }

        var wanted = eventIds?.Concat(ranked.Select(r => r.StoryEventId)).Distinct().ToList();
        var fitted = ApplyReadingOrder(LoadOwnSpans(context, wanted, gcd), ranked.Select(r => (r.ListKey!, r.StoryEventId, r.Rank!.Value)));
        if (eventIds is null)
        {
            return fitted;
        }

        var only = eventIds.ToHashSet();
        return fitted.Where(kv => only.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Holds events ranked by an outside reading order to that order, without trusting any one of their dates: within each list, the events'
    /// own start dates are fitted to the nearest non-decreasing sequence (isotonic median regression, pool-adjacent-violators), and each ranked
    /// event's <see cref="EventSpan.SortKey"/> is its fitted value. A single mis-dated event (a reprint dated decades late) is outvoted by its
    /// neighbours instead of dragging every later event up with it; "Second Clone Saga (1994)" listed after "Age of Apocalypse (1995)" is placed
    /// next to it, while unranked events keep their own dates. An undated ranked event takes the place of the one before it in the list.
    /// </summary>
    public static Dictionary<int, EventSpan> ApplyReadingOrder(IReadOnlyDictionary<int, EventSpan> spans, IEnumerable<(string ListKey, int EventId, int Rank)> ranks)
    {
        var result = new Dictionary<int, EventSpan>(spans);
        foreach (var list in ranks.Where(r => spans.ContainsKey(r.EventId)).GroupBy(r => r.ListKey))
        {
            var ordered = list.OrderBy(r => r.Rank).ThenBy(r => r.EventId).ToList();
            var blocks = new List<(List<int> Values, List<int> Items)>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var span = spans[ordered[i].EventId];
                if ((span.Start ?? span.End) is not int own)
                {
                    continue;
                }

                blocks.Add((new List<int> { own }, new List<int> { i }));
                while (blocks.Count >= 2 && Median(blocks[^2].Values) > Median(blocks[^1].Values))
                {
                    var last = blocks[^1];
                    blocks.RemoveAt(blocks.Count - 1);
                    blocks[^1].Values.AddRange(last.Values);
                    blocks[^1].Items.AddRange(last.Items);
                }
            }

            var keys = new int?[ordered.Count];
            foreach (var (values, items) in blocks)
            {
                int median = Median(values);
                foreach (int item in items)
                {
                    keys[item] = median;
                }
            }

            int? previous = null;
            for (int i = 0; i < ordered.Count; i++)
            {
                keys[i] ??= previous;
                previous = keys[i];
                var span = spans[ordered[i].EventId];
                result[span.EventId] = span with { SortKey = keys[i], Rank = ordered[i].Rank };
            }
        }

        return result;
    }

    /// <summary>The lower median, so the result is always one of the values.</summary>
    private static int Median(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(sorted.Count - 1) / 2];
    }

    private static Dictionary<int, EventSpan> LoadOwnSpans(PaperbunkrDbContext context, IReadOnlyCollection<int>? eventIds, Gcd.GcdDataStore? gcd)
    {
        var events = context.StoryEvents.AsNoTracking()
            .Where(e => eventIds == null || eventIds.Contains(e.Id))
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.StartDate,
                e.EndDate,
                Dates = e.Members.Where(m => m.Issue!.Year != null || m.Issue.GcdIssueId != null)
                    .Select(m => new { m.Issue!.Year, m.Issue.Month, m.Issue.GcdIssueId }).ToList(),
            })
            .ToList();
        var gcdDates = gcd?.DateKeysFor(events.SelectMany(e => e.Dates).Select(d => d.GcdIssueId).OfType<int>());

        return events.ToDictionary(e => e.Id, e =>
        {
            var keys = e.Dates.Select(d => IssueDateKey(d.Year, d.Month, d.GcdIssueId, gcdDates)).OfType<int>().ToList();
            int? start = DateKey(e.StartDate) ?? (keys.Count > 0 ? keys.Min() : null);
            int? end = DateKey(e.EndDate) ?? (keys.Count > 0 ? keys.Max() : null);
            return new EventSpan(e.Id, e.Name, start, end);
        });
    }

    /// <summary>Every stored relation among <paramref name="eventIds"/> as (source, target, type).</summary>
    public static List<(int Source, int Target, RelationType Type)> LoadRelations(PaperbunkrDbContext context, IReadOnlyCollection<int> eventIds) =>
        context.EventRelations.AsNoTracking()
            .Where(r => eventIds.Contains(r.SourceEventId) && eventIds.Contains(r.TargetEventId))
            .Select(r => new { r.SourceEventId, r.TargetEventId, r.RelationType })
            .AsEnumerable()
            .Select(r => (r.SourceEventId, r.TargetEventId, r.RelationType))
            .ToList();
}
