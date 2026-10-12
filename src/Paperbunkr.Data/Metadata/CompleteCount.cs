using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// ComicRack's "Complete Count" script (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §2): "complete the count of a finished series with the last number of the
/// series". Pure: given the books of the series involved, says what Count each target book of a <em>completed</em> series should get.
/// </summary>
public static class CompleteCount
{
    // The script ignores numbers of 1000 and over (legacy year-style numbering) when looking for the series' last number.
    private const int NumberCeiling = 1000;

    // Only a series that reaches this number is checked for numbering jumps.
    private const int JumpCheckFrom = 30;

    // A step of more than this many issues per month of publication is a numbering jump.
    private const int JumpIssuesPerMonth = 12;

    /// <summary>
    /// The Count each target should get, by issue id. <paramref name="library"/> is every book of the series the targets belong to (their numbers and dates decide the series' last
    /// number), <paramref name="targets"/> the books being completed; a target of a series that is not <see cref="Series.IsComplete"/> is left out, and so is a series with no numbered book.
    /// Needs <see cref="Issue.MetadataProposals"/> loaded where numbers or years only exist as proposals. Fileless placeholders do not count towards a series' last number.
    /// </summary>
    public static Dictionary<int, int> Compute(IReadOnlyList<Issue> library, IReadOnlyList<Issue> targets)
    {
        var result = new Dictionary<int, int>();
        var groups = library.Where(i => !i.IsPlaceholder).GroupBy(Key).ToDictionary(g => g.Key, g => Analyse(g.ToList()));

        foreach (Issue target in targets)
        {
            if (target.Series is not { IsComplete: true } || !groups.TryGetValue(Key(target), out Group? group) || group.Max is null)
            {
                continue;
            }

            int count = group.Max.Value;
            if (SeriesStatistics.ParseNumber(target.EffectiveNumber(), out float number))
            {
                int n = (int)number;
                if (group.Jump is int jump && n <= jump)
                {
                    count = jump;
                }
                else if (n > count + 1)
                {
                    // An issue numbered past the series' last number (an "out of numbering" one) is its own count.
                    count = n;
                }
            }

            // A book with no usable number gets the series' last number: the script sets that before it tries to read the number.
            result[target.Id] = count;
        }

        return result;
    }

    private static (int? SeriesId, string Volume) Key(Issue issue) => (issue.SeriesId, issue.EffectiveVolume() ?? string.Empty);

    private sealed record Group(int? Max, int? Jump);

    private static Group Analyse(List<Issue> books)
    {
        var numbered = new List<(int Month, int Number)>();
        foreach (Issue book in books)
        {
            if (SeriesStatistics.ParseNumber(book.EffectiveNumber(), out float number) && (int)number < NumberCeiling)
            {
                numbered.Add(((book.EffectiveYear() ?? -1) * 12 + (book.Month ?? -1), (int)number));
            }
        }

        if (numbered.Count == 0)
        {
            return new Group(null, null);
        }

        int max = numbered.Max(x => x.Number);
        int? jump = null;
        if (max > JumpCheckFrom)
        {
            var sorted = numbered.OrderBy(x => x.Number).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                // Whole-number division, as the script does it.
                int issuesPerMonth = Math.Abs(sorted[i + 1].Number - sorted[i].Number) / (Math.Abs(sorted[i + 1].Month - sorted[i].Month) + 1);
                if (issuesPerMonth > JumpIssuesPerMonth && (jump is null || jump > sorted[i].Number))
                {
                    jump = sorted[i].Number;
                }
            }
        }

        return new Group(max, jump);
    }
}
