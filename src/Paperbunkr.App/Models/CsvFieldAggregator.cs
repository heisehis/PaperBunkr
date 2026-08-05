using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Models;

/// <summary>
/// Comic Vine/ComicInfo.xml-convention fields (Writer, Teams, Locations, etc.) are stored as
/// comma-separated strings per issue (docs/onboarding.md §6, §9). Since these promote to a
/// series-wide display (credits row, tag pills) rather than a per-issue one, this aggregates
/// distinct values across all of a series' issues, preserving first-seen order.
/// </summary>
public static class CsvFieldAggregator
{
    public static List<string> Distinct(IEnumerable<string?> rawValues)
    {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (string? raw in rawValues)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (string part in raw.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0 && seen.Add(trimmed))
                {
                    result.Add(trimmed);
                }
            }
        }

        return result;
    }

    public static string Join(IEnumerable<string?> rawValues, string fallback = "Unknown")
    {
        var distinct = Distinct(rawValues);
        return distinct.Count > 0 ? string.Join(", ", distinct) : fallback;
    }
}
