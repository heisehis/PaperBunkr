using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.Data.Entities;

/// <summary>
/// Diff-not-replace merge for <see cref="ReadingList.Tags"/>, mirroring
/// <see cref="IssueTagExtensions.MergeFrom(Issue, IssueTagField, IEnumerable{string?})"/> exactly
/// but without a <c>Field</c> discriminator - a Reading List has one tag concept, not a Genre-vs-
/// Tags split (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md).
/// </summary>
public static class ReadingListTagExtensions
{
    public static string? JoinedTags(this ReadingList list)
    {
        var values = list.Tags.Select(t => t.Value).ToList();
        return values.Count > 0 ? string.Join(", ", values) : null;
    }

    /// <summary>
    /// Diffs <paramref name="rawValues"/> against the list's existing tags: removes tags no longer
    /// present, adds tags for new values (<see cref="IssueTagWeight.Unset"/>, "Uncategorized"), and
    /// leaves every surviving tag's Category/Weight untouched.
    /// </summary>
    public static void MergeFrom(this ReadingList list, IEnumerable<string?> rawValues)
    {
        var incoming = SplitDistinct(rawValues);
        var existing = list.Tags.ToList();

        foreach (var tag in existing)
        {
            if (!incoming.Contains(tag.Value, StringComparer.OrdinalIgnoreCase))
            {
                list.Tags.Remove(tag);
            }
        }

        var existingValues = new HashSet<string>(existing.Select(t => t.Value), StringComparer.OrdinalIgnoreCase);
        foreach (string value in incoming)
        {
            if (!existingValues.Contains(value))
            {
                list.Tags.Add(new ReadingListTag
                {
                    ReadingListId = list.Id,
                    Value = value,
                    Category = "Uncategorized",
                    Weight = IssueTagWeight.Unset,
                });
            }
        }
    }

    /// <summary>Same comma-split convention as <see cref="IssueTagExtensions"/>'s own private helper.</summary>
    private static List<string> SplitDistinct(IEnumerable<string?> rawValues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
}
