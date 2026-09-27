using System.Globalization;
using System.Text.Json;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>One issue of a story arc as ComicVine lists it, with just what ordering needs.</summary>
public sealed record StoryArcIssue(int IssueId, string? IssueNumber, string? StoreDate, string? CoverDate);

/// <summary>An issue's reading-order position in each of its story arcs (<see cref="Numbers"/>, one per arc, aligned with the arcs it was computed for; empty where none could be found) plus the size of its first arc.</summary>
public sealed record StoryArcPositions(IReadOnlyList<string> Numbers, int? AlternateCount)
{
    public bool IsEmpty => AlternateCount is null && Numbers.All(string.IsNullOrEmpty);
}

/// <summary>
/// Reading order within a story arc, ported from the user's fork of the ComicVine Scraper plugin
/// (<c>book/arcorder.py</c> and <c>cvdb._query_story_arc_order</c>, read directly 2026-09-25). An arc's
/// issues are sorted by store date, then cover date, then natural issue number; an undated issue sorts
/// last. Positions are 1-based and the count is the whole arc. An optional <c>arc_overrides.json</c>
/// (<c>{ "&lt;arc id&gt;": { "&lt;issue id&gt;": position } }</c>) wins over the computed position for any
/// arc ComicVine has ordered wrongly. Each arc is fetched once per instance.
/// </summary>
public sealed class StoryArcOrderResolver(
    Func<int, CancellationToken, Task<IReadOnlyList<StoryArcIssue>>> fetchArcIssues,
    string? overridesPath = null)
{
    private readonly Dictionary<int, IReadOnlyList<(int IssueId, int Position, int Count)>> _orderByArc = new();
    private Dictionary<int, Dictionary<int, int>>? _overrides;

    /// <summary>Positions of <paramref name="issueId"/> in each of <paramref name="arcs"/>. A failed or empty arc lookup leaves that arc's entry empty rather than throwing.</summary>
    public async Task<StoryArcPositions> ComputeAsync(IReadOnlyList<ComicVineIdName> arcs, int issueId, CancellationToken cancellationToken)
    {
        var numbers = new List<string>();
        int? alternateCount = null;

        for (int i = 0; i < arcs.Count; i++)
        {
            (int position, int count) = arcs[i].ExternalId is int arcId
                ? await PositionAsync(arcId, issueId, cancellationToken).ConfigureAwait(false)
                : (-1, -1);

            numbers.Add(position > 0 ? position.ToString(CultureInfo.InvariantCulture) : string.Empty);
            if (i == 0 && count > 0)
            {
                alternateCount = count;   // AlternateCount is a single number: only the first arc's size fits
            }
        }

        return new StoryArcPositions(numbers, alternateCount);
    }

    private async Task<(int Position, int Count)> PositionAsync(int arcId, int issueId, CancellationToken cancellationToken)
    {
        IReadOnlyList<(int IssueId, int Position, int Count)> order = await OrderAsync(arcId, cancellationToken).ConfigureAwait(false);
        int count = order.Count > 0 ? order[0].Count : -1;

        if (LoadOverrides().TryGetValue(arcId, out var arcOverrides) && arcOverrides.TryGetValue(issueId, out int overridden))
        {
            return (overridden, count);
        }

        foreach (var entry in order)
        {
            if (entry.IssueId == issueId)
            {
                return (entry.Position, entry.Count);
            }
        }

        return (-1, -1);
    }

    private async Task<IReadOnlyList<(int IssueId, int Position, int Count)>> OrderAsync(int arcId, CancellationToken cancellationToken)
    {
        if (_orderByArc.TryGetValue(arcId, out var cached))
        {
            return cached;
        }

        IReadOnlyList<StoryArcIssue> issues;
        try
        {
            issues = await fetchArcIssues(arcId, cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException)
        {
            return Array.Empty<(int, int, int)>();   // not cached: a later book may succeed
        }

        var sorted = issues
            .OrderBy(i => DateKey(i.StoreDate))
            .ThenBy(i => DateKey(i.CoverDate))
            .ThenBy(i => NaturalKey.Of(i.IssueNumber))
            .ToList();

        var order = sorted.Select((issue, index) => (issue.IssueId, index + 1, sorted.Count)).ToList();
        _orderByArc[arcId] = order;
        return order;
    }

    private Dictionary<int, Dictionary<int, int>> LoadOverrides()
    {
        if (_overrides is not null)
        {
            return _overrides;
        }

        _overrides = new Dictionary<int, Dictionary<int, int>>();
        if (string.IsNullOrEmpty(overridesPath) || !File.Exists(overridesPath))
        {
            return _overrides;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(overridesPath));
            foreach (var arc in doc.RootElement.EnumerateObject())
            {
                if (!int.TryParse(arc.Name, out int arcId))
                {
                    continue;
                }

                var map = new Dictionary<int, int>();
                foreach (var entry in arc.Value.EnumerateObject())
                {
                    if (int.TryParse(entry.Name, out int overriddenIssueId) && entry.Value.TryGetInt32(out int position))
                    {
                        map[overriddenIssueId] = position;
                    }
                }

                _overrides[arcId] = map;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            _overrides = new Dictionary<int, Dictionary<int, int>>();   // a bad file is ignored, not fatal (as in the fork)
        }

        return _overrides;
    }

    /// <summary>Undated sorts last, as in the fork (9999-12-31); a partial date pads missing parts with 1.</summary>
    private static (int Year, int Month, int Day) DateKey(string? date)
    {
        if (!string.IsNullOrWhiteSpace(date) && date.Length > 1)
        {
            var parts = date.Split('-').Take(3).Select(p => int.TryParse(p, out int n) ? n : (int?)null).ToList();
            if (parts.All(p => p is not null))
            {
                while (parts.Count < 3)
                {
                    parts.Add(1);
                }

                return (parts[0]!.Value, parts[1]!.Value, parts[2]!.Value);
            }
        }

        return (9999, 12, 31);
    }

    /// <summary>Natural ordering for an issue number: letters, then the number, then any suffix ("1", "2", "10", "10A").</summary>
    private readonly record struct NaturalKey(string Prefix, double? Number, string Suffix) : IComparable<NaturalKey>
    {
        public static NaturalKey Of(string? issueNumber)
        {
            string s = (issueNumber ?? string.Empty).Trim().ToLowerInvariant();
            int start = 0;
            while (start < s.Length && !char.IsDigit(s[start]))
            {
                start++;
            }

            int end = start;
            while (end < s.Length && (char.IsDigit(s[end]) || (s[end] == '.' && end + 1 < s.Length && char.IsDigit(s[end + 1]))))
            {
                end++;
            }

            double? number = end > start && double.TryParse(s[start..end], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : null;
            return new NaturalKey(s[..start], number, s[end..]);
        }

        public int CompareTo(NaturalKey other)
        {
            int c = string.CompareOrdinal(Prefix, other.Prefix);
            if (c != 0)
            {
                return c;
            }

            c = Nullable.Compare(Number, other.Number);
            return c != 0 ? c : string.CompareOrdinal(Suffix, other.Suffix);
        }
    }
}
