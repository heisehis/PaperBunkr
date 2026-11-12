using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public sealed record CreatorRun(int CreatorId, string CreatorName, string Role, IReadOnlyList<Issue> Issues, int CreditedCount)
{
    public int ReadCount => Issues.Count(i => i.HasBeenRead());
    public string Range => $"#{Issues[0].EffectiveNumber()}–#{Issues[^1].EffectiveNumber()}";
}

/// <summary>Derived runs, beyond CE's flat credits: six credited issues, with up to two owned fill-ins between credits.</summary>
public static class CreatorRunDetector
{
    public static IReadOnlyList<CreatorRun> Find(IEnumerable<Issue> seriesIssues, IEnumerable<CreatorCredit> credits, string role)
    {
        var issues = seriesIssues.Where(i => !i.IsPlaceholder).OrderBy(i => i.NumberSortKey() ?? float.MaxValue)
            .ThenBy(i => i.EffectiveNumber(), StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id).ToList();
        bool artist = role.Equals("Artist", StringComparison.OrdinalIgnoreCase) || role.Equals("Penciller", StringComparison.OrdinalIgnoreCase);
        var byCreator = credits.Where(c => artist ? c.Role is "Penciller" or "Artist" : c.Role == role).GroupBy(c => c.CreatorId);
        var result = new List<CreatorRun>();
        foreach (var creator in byCreator)
        {
            var credited = creator.Select(c => c.IssueId).ToHashSet();
            int start = -1, last = -1, count = 0;
            void Finish()
            {
                if (count >= 6)
                    result.Add(new CreatorRun(creator.Key, creator.First().Creator?.Name ?? string.Empty, artist ? "Artist" : role,
                        issues.GetRange(start, last - start + 1), count));
                start = last = -1;
                count = 0;
            }
            for (int index = 0; index < issues.Count; index++)
            {
                if (!credited.Contains(issues[index].Id)) continue;
                if (last >= 0 && index - last > 3) Finish();
                if (start < 0) start = index;
                last = index;
                count++;
            }
            Finish();
        }
        return result.OrderBy(r => r.Issues[0].NumberSortKey() ?? float.MaxValue)
            .ThenBy(r => r.CreatorName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.CreatorId).ToList();
    }

    public static IReadOnlyList<CreatorRun> FindAll(IEnumerable<Issue> issues, IEnumerable<CreatorCredit> credits)
    {
        var allIssues = issues.ToList();
        var allCredits = credits.ToList();
        // A single writer/artist's whole-series manga is one run, not two overlapping sections for the same person.
        return Find(allIssues, allCredits, "Writer").Concat(Find(allIssues, allCredits, "Artist"))
            .GroupBy(r => (r.CreatorId, Key: string.Join(",", r.Issues.Select(i => i.Id))))
            .Select(g => g.First()).OrderBy(r => r.Issues[0].NumberSortKey() ?? float.MaxValue)
            .ThenBy(r => r.CreatorName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool ShouldOffer(IReadOnlyList<CreatorRun> runs, IEnumerable<Issue> issues)
    {
        if (runs.Count >= 2) return true;
        if (runs.Count == 0) return false;
        var inRun = runs[0].Issues.Select(i => i.Id).ToHashSet();
        return issues.Any(i => !i.IsPlaceholder && !inRun.Contains(i.Id));
    }
}
