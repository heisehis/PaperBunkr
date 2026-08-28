using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read/display-time comic-age classification for an <see cref="Issue"/> (docs/superpowers/specs/
/// 2026-08-27-metadata-model-phase4g-age-progression-design.md). Not a stored/backfilled column and
/// not a reviewable <c>MetadataProposal</c> - runs on demand wherever an age is displayed.
/// </summary>
public static class BookAgeResolver
{
    private const string DisputedWindowReason =
        "1980-84 is Modern per ComicRack CE's own boundaries, but commonly cited elsewhere as still Bronze Age";

    public static (ComicAge? Age, decimal Confidence, string? Reason) Resolve(Issue issue)
    {
        // 1. An explicit user/CE-migrated BookAge label is authoritative - match on the leading
        //    word, ignoring CE's parenthetical year range (e.g. "Golden (1938-55)" -> "Golden").
        if (!string.IsNullOrWhiteSpace(issue.BookAge))
        {
            int paren = issue.BookAge.IndexOf('(');
            string label = (paren >= 0 ? issue.BookAge[..paren] : issue.BookAge).Trim();
            foreach (var age in Enum.GetValues<ComicAge>())
            {
                if (string.Equals(label, age.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return (age, 1.0m, null);
                }
            }
        }

        // 2. Fall back to year inference.
        if (issue.Year is int year)
        {
            var age = ComicAgeCatalog.FromYear(year);
            if (age is null)
            {
                return (null, 0m, null);
            }

            if (year is >= 1980 and <= 1984)
            {
                return (ComicAge.Modern, 0.6m, DisputedWindowReason);
            }

            return (age, 1.0m, null);
        }

        // 3. Nothing to go on - no guess.
        return (null, 0m, null);
    }
}
