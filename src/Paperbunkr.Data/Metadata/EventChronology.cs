using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>An event's name and date span as <c>year*100+month</c> keys (month 0 when unknown); null when nothing is dated.</summary>
public sealed record EventSpan(int EventId, string Name, int? Start, int? End);

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
            .OrderBy(e => e.Start ?? e.End ?? int.MaxValue)
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
