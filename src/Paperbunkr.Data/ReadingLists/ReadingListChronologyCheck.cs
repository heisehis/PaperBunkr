using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>Two entries the list has the wrong way round: <paramref name="First"/> comes first but belongs to the later event.</summary>
public sealed record ChronologyInversion(string First, string FirstEvent, string Second, string SecondEvent);

/// <summary>
/// How a reading list's order compares with the chronology of the story events its issues belong to.
/// </summary>
/// <param name="RankedCount">Entries that belong to an event with a place in a chronology.</param>
/// <param name="OutOfOrderCount">How many of those would have to move for the list to follow chronology.</param>
/// <param name="Inversions">The first few pairs that are the wrong way round.</param>
/// <param name="Loops">Event names of each loop in the chronology that this list touches ("A → B → A"); such events are left out of the check.</param>
public sealed record ReadingListChronologyResult(int RankedCount, int OutOfOrderCount, IReadOnlyList<ChronologyInversion> Inversions, IReadOnlyList<string> Loops)
{
    public bool HasFindings => OutOfOrderCount > 0 || Loops.Count > 0;

    /// <summary>Reordering is only offered when there is something to move and no loop makes "the right order" undefined.</summary>
    public bool CanReorder => OutOfOrderCount > 0 && Loops.Count == 0;
}

/// <summary>
/// Checks a reading list's order against story-event chronology (docs/superpowers/specs/2026-10-06-smart-features-design.md §7.2). Only
/// the directional relations between events count (Prequel, Sequel, Continuation, via <see cref="EventChronology.Direction"/>): two
/// events with no such path between them say nothing about each other, so entries are only ever compared within one connected chain of
/// events. Works on any list - an event-linked list orders issues inside one event, which this is not about. Advisory: a list in
/// publication order is skipped, and the user can mark any list's order as intended.
/// </summary>
public static class ReadingListChronologyCheck
{
    public const int InversionExamples = 4;

    private sealed record Entry(int ItemId, int IssueId, int SortOrder, string Label);

    private sealed record Analysis(
        IReadOnlyList<Entry> Entries,
        Dictionary<int, (int Chain, int Level, int EventId)> RankByItem,
        Dictionary<int, string> EventNames,
        IReadOnlyList<string> Loops);

    /// <summary>Null when the check does not apply: no such list, a publication-order list, or one the user marked as intended.</summary>
    public static ReadingListChronologyResult? Check(PaperbunkrDbContext context, int readingListId)
    {
        if (Analyze(context, readingListId, honourDismissal: true) is not { } a)
        {
            return null;
        }

        int outOfOrder = 0;
        var inversions = new List<ChronologyInversion>();
        foreach (var chain in a.Entries.Where(e => a.RankByItem.ContainsKey(e.ItemId)).GroupBy(e => a.RankByItem[e.ItemId].Chain))
        {
            var ordered = chain.ToList();
            var levels = ordered.Select(e => a.RankByItem[e.ItemId].Level).ToList();
            outOfOrder += levels.Count - LongestNonDecreasingRun(levels);

            for (int i = 0; i + 1 < ordered.Count && inversions.Count < InversionExamples; i++)
            {
                if (levels[i] > levels[i + 1])
                {
                    inversions.Add(new ChronologyInversion(
                        ordered[i].Label, a.EventNames[a.RankByItem[ordered[i].ItemId].EventId],
                        ordered[i + 1].Label, a.EventNames[a.RankByItem[ordered[i + 1].ItemId].EventId]));
                }
            }
        }

        return new ReadingListChronologyResult(a.RankByItem.Count, outOfOrder, inversions, a.Loops);
    }

    /// <summary>
    /// Puts the entries that have a place in a chronology into chronological order, each chain within the slots its own entries already
    /// occupy; every other entry stays exactly where it is. Entries of the same event keep their current relative order. False (nothing
    /// changed) when there is nothing to move or a loop makes the order undefined.
    /// </summary>
    public static bool ReorderToChronology(PaperbunkrDbContext context, int readingListId)
    {
        if (Analyze(context, readingListId, honourDismissal: false) is not { } a || a.Loops.Count > 0)
        {
            return false;
        }

        var newOrderByItem = new Dictionary<int, int>();
        foreach (var chain in a.Entries.Where(e => a.RankByItem.ContainsKey(e.ItemId)).GroupBy(e => a.RankByItem[e.ItemId].Chain))
        {
            var current = chain.ToList();
            var slots = current.Select(e => e.SortOrder).ToList();
            var sorted = current.OrderBy(e => a.RankByItem[e.ItemId].Level).ToList();     // stable: ties keep list order
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].SortOrder != slots[i])
                {
                    newOrderByItem[sorted[i].ItemId] = slots[i];
                }
            }
        }

        if (newOrderByItem.Count == 0)
        {
            return false;
        }

        foreach (var item in context.ReadingListItems.Where(i => i.ReadingListId == readingListId).ToList())
        {
            if (newOrderByItem.TryGetValue(item.Id, out int order))
            {
                item.SortOrder = order;
            }
        }

        context.SaveChanges();
        return true;
    }

    private static Analysis? Analyze(PaperbunkrDbContext context, int readingListId, bool honourDismissal)
    {
        var list = context.ReadingLists.AsNoTracking().FirstOrDefault(l => l.Id == readingListId);
        if (list is null || list.ContinuityOrderKind == ContinuityOrderKind.PublicationOrder)
        {
            return null;
        }

        if (honourDismissal && HealthDismissals.IsDismissed(context, HealthDismissals.ListOrder, DismissalKey(readingListId)))
        {
            return null;
        }

        var entries = context.ReadingListItems.AsNoTracking()
            .Where(i => i.ReadingListId == readingListId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.IssueId, i.SortOrder, SeriesName = i.Issue!.Series!.Name, i.Issue.Number })
            .ToList()
            .Select(i => new Entry(i.Id, i.IssueId, i.SortOrder, string.IsNullOrWhiteSpace(i.Number) ? i.SeriesName : $"{i.SeriesName} #{i.Number}"))
            .ToList();

        var issueIds = entries.Select(e => e.IssueId).Distinct().ToList();
        var memberships = context.EventMemberships.AsNoTracking()
            .Where(m => issueIds.Contains(m.IssueId))
            .Select(m => new { m.IssueId, m.StoryEventId })
            .ToList();
        var eventIds = memberships.Select(m => m.StoryEventId).Distinct().ToList();
        var eventNames = context.StoryEvents.AsNoTracking().Where(e => eventIds.Contains(e.Id)).Select(e => new { e.Id, e.Name }).ToDictionary(e => e.Id, e => e.Name);

        // The directed "comes before" graph among the events this list touches.
        var relations = EventChronology.LoadRelations(context, eventIds);
        var before = eventIds.ToDictionary(id => id, _ => new HashSet<int>());      // event -> events that come after it
        var linked = eventIds.ToDictionary(id => id, _ => new HashSet<int>());      // undirected, for chains
        foreach (var (source, target, type) in relations)
        {
            if (EventChronology.Direction(source, target, type) is { } d && d.Earlier != d.Later)
            {
                before[d.Earlier].Add(d.Later);
                linked[d.Earlier].Add(d.Later);
                linked[d.Later].Add(d.Earlier);
            }
        }

        // Events caught in a loop have no defined place; they are reported and left out.
        var cycles = EventChronology.FindCycles(eventIds, relations);
        var looped = cycles.SelectMany(c => c).ToHashSet();
        var loops = cycles
            .Select(c => string.Join(" → ", c.Select(id => eventNames.GetValueOrDefault(id, "?")).Append(eventNames.GetValueOrDefault(c[0], "?"))))
            .ToList();

        // Chains: connected groups of events with at least one directional relation. Level = longest path from the chain's start.
        var chainOf = new Dictionary<int, int>();
        int chainCount = 0;
        foreach (int id in eventIds.OrderBy(i => i))
        {
            if (chainOf.ContainsKey(id) || linked[id].Count == 0)
            {
                continue;
            }

            var queue = new Queue<int>();
            queue.Enqueue(id);
            chainOf[id] = chainCount;
            while (queue.Count > 0)
            {
                foreach (int other in linked[queue.Dequeue()])
                {
                    if (chainOf.TryAdd(other, chainCount))
                    {
                        queue.Enqueue(other);
                    }
                }
            }

            chainCount++;
        }

        var level = new Dictionary<int, int>();
        int LevelOf(int eventId, HashSet<int> visiting)
        {
            if (level.TryGetValue(eventId, out int known))
            {
                return known;
            }

            if (!visiting.Add(eventId))
            {
                return 0; // only reachable through a loop, and looped events are excluded below
            }

            int best = 0;
            foreach (int earlier in before.Where(kv => kv.Value.Contains(eventId)).Select(kv => kv.Key))
            {
                if (!looped.Contains(earlier))
                {
                    best = Math.Max(best, LevelOf(earlier, visiting) + 1);
                }
            }

            visiting.Remove(eventId);
            return level[eventId] = best;
        }

        var rankByItem = new Dictionary<int, (int Chain, int Level, int EventId)>();
        var eventsByIssue = memberships.GroupBy(m => m.IssueId).ToDictionary(g => g.Key, g => g.Select(m => m.StoryEventId).ToList());
        foreach (var entry in entries)
        {
            if (!eventsByIssue.TryGetValue(entry.IssueId, out var itsEvents))
            {
                continue;
            }

            // An issue in several events takes its earliest place.
            var ranked = itsEvents
                .Where(e => chainOf.ContainsKey(e) && !looped.Contains(e))
                .Select(e => (Chain: chainOf[e], Level: LevelOf(e, []), EventId: e))
                .OrderBy(r => r.Level).ThenBy(r => r.EventId)
                .ToList();
            if (ranked.Count > 0)
            {
                rankByItem[entry.ItemId] = ranked[0];
            }
        }

        // Only loops whose events actually appear in this list are this list's problem (eventIds is already limited to those).
        return new Analysis(entries, rankByItem, eventNames, loops);
    }

    public static string DismissalKey(int readingListId) => readingListId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Length of the longest run of values, not necessarily adjacent, that never goes down.</summary>
    private static int LongestNonDecreasingRun(IReadOnlyList<int> values)
    {
        var tails = new List<int>();
        foreach (int value in values)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (tails[mid] <= value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            if (lo == tails.Count)
            {
                tails.Add(value);
            }
            else
            {
                tails[lo] = value;
            }
        }

        return tails.Count;
    }
}
