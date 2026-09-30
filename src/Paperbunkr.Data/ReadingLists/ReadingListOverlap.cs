namespace Paperbunkr.Data.ReadingLists;

/// <summary>Two reading lists that share most of their issues. <see cref="ListA"/> is the smaller id.</summary>
public sealed record ReadingListOverlapPair(int ListA, int ListB, int Shared, int SmallerCount)
{
    public double Fraction => SmallerCount == 0 ? 0 : (double)Shared / SmallerCount;

    public int Other(int listId) => listId == ListA ? ListB : ListA;
}

/// <summary>
/// Cross-list overlap detection (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §7) - catches the same arc
/// pulled from two sources. Pure: runs over the issue-id sets the sidebar has already loaded. No ComicRack CE precedent (CE's closest is an
/// And-mode folder).
/// </summary>
public static class ReadingListOverlap
{
    /// <summary>Shared issues must be at least this fraction of the smaller list.</summary>
    public const double Threshold = 0.60;

    /// <summary>The smaller list must have at least this many issues, so two tiny lists don't match trivially.</summary>
    public const int MinimumSmallerCount = 5;

    public static IReadOnlyList<ReadingListOverlapPair> Find(
        IReadOnlyList<(int ListId, IReadOnlySet<int> IssueIds)> lists, IReadOnlySet<(int, int)> dismissed)
    {
        var sizes = lists.ToDictionary(l => l.ListId, l => l.IssueIds.Count);
        var listsByIssue = new Dictionary<int, List<int>>();
        foreach (var (listId, issueIds) in lists)
        {
            if (issueIds.Count < MinimumSmallerCount)
            {
                continue;       // can never be the smaller side of a qualifying pair, nor share enough with one
            }

            foreach (int issueId in issueIds)
            {
                if (!listsByIssue.TryGetValue(issueId, out var holders))
                {
                    listsByIssue[issueId] = holders = new List<int>();
                }

                holders.Add(listId);
            }
        }

        var shared = new Dictionary<(int, int), int>();
        foreach (var holders in listsByIssue.Values)
        {
            for (int i = 0; i < holders.Count; i++)
            {
                for (int j = i + 1; j < holders.Count; j++)
                {
                    var key = Normalize(holders[i], holders[j]);
                    shared[key] = shared.GetValueOrDefault(key) + 1;
                }
            }
        }

        var pairs = new List<ReadingListOverlapPair>();
        foreach (var ((a, b), count) in shared)
        {
            int smaller = Math.Min(sizes[a], sizes[b]);
            if (smaller >= MinimumSmallerCount && count >= Threshold * smaller && !dismissed.Contains((a, b)))
            {
                pairs.Add(new ReadingListOverlapPair(a, b, count, smaller));
            }
        }

        return pairs.OrderByDescending(p => p.Fraction).ThenBy(p => p.ListA).ThenBy(p => p.ListB).ToList();
    }

    public static (int, int) Normalize(int a, int b) => a < b ? (a, b) : (b, a);
}
