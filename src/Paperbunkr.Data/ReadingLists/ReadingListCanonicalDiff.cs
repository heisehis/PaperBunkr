using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>One entry of a provider's canonical order: its 0-based position, the provider's issue, and the library issue it matched (if any).</summary>
public sealed record CanonicalEntry(int Position, ArcIssue Arc, int? LocalIssueId);

/// <summary>
/// A reading list compared with a provider's canonical order. <see cref="Missing"/> are provider entries not in the list, in provider order;
/// <see cref="OutOfOrderCount"/> is how many of the list's provider-known items would have to move to match (list length minus the
/// longest run already in provider order).
/// </summary>
public sealed record CanonicalDiff(string SourceKey, string SourceName, int ListId, IReadOnlyList<CanonicalEntry> Entries, IReadOnlyList<CanonicalEntry> Missing, int OutOfOrderCount)
{
    public bool Matches => Missing.Count == 0 && OutOfOrderCount == 0;
}

/// <summary>
/// "Check against ComicVine/Metron" for a list linked to a Story Event (docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-
/// design.md §3). Compute only matches issues already in the library (<see cref="ReadingListMatcher.FindExisting"/>) and changes nothing;
/// <see cref="InsertMissing"/> and <see cref="ReorderToMatch"/> are the only writes, and neither ever removes an item (decision Q20).
/// </summary>
public static class ReadingListCanonicalDiff
{
    /// <summary>
    /// Which provider to check a list against: the linked event must carry that provider's arc id and the provider must be usable
    /// (credentials set). ComicVine is preferred unless <paramref name="preferMetron"/>. Null when neither qualifies.
    /// </summary>
    public static (IReadingListSource Source, string ArcId)? ChooseSource(StoryEvent storyEvent, Func<string, IReadingListSource?> getSource, bool preferMetron = false)
    {
        var candidates = new List<(string Key, string? ArcId)> { ("ComicVine", storyEvent.ComicVineArcId), ("Metron", storyEvent.MetronArcId) };
        if (preferMetron)
        {
            candidates.Reverse();
        }

        foreach (var (key, arcId) in candidates)
        {
            if (!string.IsNullOrEmpty(arcId) && getSource(key) is { } source)
            {
                return (source, arcId);
            }
        }

        return null;
    }

    /// <summary>The providers a list could be checked against, in preference order - for the "Use Metron instead" switch.</summary>
    public static IReadOnlyList<string> AvailableSourceKeys(StoryEvent storyEvent, Func<string, IReadingListSource?> getSource) =>
        new[] { ("ComicVine", storyEvent.ComicVineArcId), ("Metron", storyEvent.MetronArcId) }
            .Where(c => !string.IsNullOrEmpty(c.Item2) && getSource(c.Item1) is not null)
            .Select(c => c.Item1)
            .ToList();

    public static async Task<IReadOnlyList<ArcIssue>> FetchAsync(IReadingListSource source, string arcId, CancellationToken cancellationToken) =>
        await source.GetArcIssuesInOrderAsync(arcId, cancellationToken).ConfigureAwait(false);

    /// <summary>Compares the list with an already fetched provider order. Read-only.</summary>
    public static CanonicalDiff Compute(PaperbunkrDbContext context, int listId, string sourceKey, IReadOnlyList<ArcIssue> providerOrder)
    {
        var entries = providerOrder
            .Select((arc, position) => new CanonicalEntry(
                position,
                arc,
                ReadingListMatcher.FindExisting(context, arc.Series, arc.Number, year: arc.Year > 0 ? arc.Year : null)?.Id))
            .ToList();

        var listIssueIds = context.ReadingListItems.Where(i => i.ReadingListId == listId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => i.IssueId)
            .ToList();
        var inList = listIssueIds.ToHashSet();
        var positionOf = FirstPositions(entries);

        var missing = entries.Where(e => e.LocalIssueId is not int id || !inList.Contains(id))
            .GroupBy(e => e.LocalIssueId ?? -1 - e.Position)      // one entry per local issue; unmatched entries stay distinct
            .Select(g => g.First())
            .ToList();

        var knownPositions = listIssueIds.Where(positionOf.ContainsKey).Select(id => positionOf[id]).ToList();
        int outOfOrder = knownPositions.Count - LongestIncreasingRun(knownPositions);

        return new CanonicalDiff(sourceKey, ReadingListSourceRegistry.GetDisplayName(sourceKey), listId, entries, missing, outOfOrder);
    }

    /// <summary>
    /// Inserts every missing entry at its provider position: right after the list item holding the nearest earlier provider position (at
    /// the top when none does). An entry not in the library becomes a placeholder issue. Returns how many were inserted.
    /// </summary>
    public static int InsertMissing(PaperbunkrDbContext context, CanonicalDiff diff, LibraryEvents? events = null)
    {
        var list = context.ReadingLists.Include(r => r.Items).First(r => r.Id == diff.ListId);
        context.MarkReadingListManaged(list);

        var ordered = list.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        var positionOf = FirstPositions(diff.Entries);
        var present = ordered.Select(i => i.IssueId).ToHashSet();
        var added = new List<int>();

        foreach (var entry in diff.Missing.OrderBy(e => e.Position))
        {
            int issueId = entry.LocalIssueId
                ?? ReadingListMatcher.ResolveOrCreatePlaceholder(context, entry.Arc.Series, entry.Arc.Number, year: entry.Arc.Year > 0 ? entry.Arc.Year : null).Id;
            if (!present.Add(issueId))
            {
                continue;
            }

            positionOf.TryAdd(issueId, entry.Position);
            int anchor = -1;
            for (int k = 0; k < ordered.Count; k++)
            {
                if (positionOf.TryGetValue(ordered[k].IssueId, out int p) && p < entry.Position
                    && (anchor < 0 || p >= positionOf[ordered[anchor].IssueId]))
                {
                    anchor = k;
                }
            }

            var item = new ReadingListItem { ReadingListId = list.Id, IssueId = issueId };
            ordered.Insert(anchor + 1, item);
            list.Items.Add(item);
            added.Add(issueId);
        }

        Renumber(ordered);
        list.UpdatedAt = DateTime.UtcNow;
        ReadingListManager.Record(context, list, added.Count > 0 ? ReadingListChangeKind.Added | ReadingListChangeKind.Reordered : ReadingListChangeKind.None,
            added, Array.Empty<int>(), events);
        return added.Count;
    }

    /// <summary>
    /// Puts the items the provider knows into provider order, within the slots those items already occupy; items the provider doesn't
    /// list stay where they are. Nothing is added or removed. Returns how many items moved.
    /// </summary>
    public static int ReorderToMatch(PaperbunkrDbContext context, CanonicalDiff diff, LibraryEvents? events = null)
    {
        var list = context.ReadingLists.Include(r => r.Items).First(r => r.Id == diff.ListId);
        context.MarkReadingListManaged(list);

        var ordered = list.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        var positionOf = FirstPositions(diff.Entries);
        var slots = Enumerable.Range(0, ordered.Count).Where(k => positionOf.ContainsKey(ordered[k].IssueId)).ToList();
        var known = slots.Select(k => ordered[k]).OrderBy(i => positionOf[i.IssueId]).ToList();     // OrderBy is stable

        int moved = 0;
        for (int s = 0; s < slots.Count; s++)
        {
            if (!ReferenceEquals(ordered[slots[s]], known[s]))
            {
                moved++;
            }

            ordered[slots[s]] = known[s];
        }

        if (moved > 0)
        {
            Renumber(ordered);
            list.UpdatedAt = DateTime.UtcNow;
        }

        ReadingListManager.Record(context, list, moved > 0 ? ReadingListChangeKind.Reordered : ReadingListChangeKind.None, Array.Empty<int>(), Array.Empty<int>(), events);
        return moved;
    }

    private static Dictionary<int, int> FirstPositions(IEnumerable<CanonicalEntry> entries)
    {
        var positionOf = new Dictionary<int, int>();
        foreach (var e in entries)
        {
            if (e.LocalIssueId is int id)
            {
                positionOf.TryAdd(id, e.Position);
            }
        }

        return positionOf;
    }

    /// <summary>Length of the longest strictly increasing subsequence (patience sorting, O(n log n)).</summary>
    internal static int LongestIncreasingRun(IReadOnlyList<int> values)
    {
        var tails = new List<int>();
        foreach (int v in values)
        {
            int i = tails.BinarySearch(v);
            if (i < 0)
            {
                i = ~i;
            }

            if (i == tails.Count)
            {
                tails.Add(v);
            }
            else
            {
                tails[i] = v;
            }
        }

        return tails.Count;
    }

    private static void Renumber(List<ReadingListItem> ordered)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
        }
    }
}
