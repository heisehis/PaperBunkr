using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.Data.Entities;

/// <summary>
/// Read/write helpers bridging the old flat <c>Issue.Genre</c>/<c>Issue.Tags</c> CSV strings and
/// the structured <see cref="Issue.Tags"/> collection (docs/superpowers/specs/2026-08-23-weighted-
/// categorized-tags-design.md). Keeps every consumer that only ever wanted "the comma string"
/// (search, Smart Lists, ComicInfo.xml, token-overlap scoring) on the same external contract it
/// had before, and centralizes the diff-not-replace merge logic shared by ComicInfo.xml import/
/// rescan (<c>CeLibraryMigrator.MapStoryFields</c>) and the bulk/single issue editors
/// (<c>BulkFieldRegistry</c>) so both apply identical "preserve Category/Weight for values that
/// survive" semantics rather than duplicating it.
/// </summary>
public static class IssueTagExtensions
{
    public static string? JoinedGenre(this Issue issue) => Joined(issue, IssueTagField.Genre);

    public static string? JoinedTags(this Issue issue) => Joined(issue, IssueTagField.Tags);

    public static bool HasAny(this Issue issue, IssueTagField field) => issue.Tags.Any(t => t.Field == field);

    /// <summary>
    /// Diffs <paramref name="rawValues"/> (one or more comma-separated strings, e.g. a single
    /// ComicInfo.xml field or a submitted bulk-edit string) against the issue's existing tags for
    /// <paramref name="field"/>: removes tags whose value is no longer present, adds tags for new
    /// values (<see cref="IssueTagWeight.Unset"/>, default Category), and leaves every surviving
    /// tag's Category/Weight untouched. Never blind-overwrites - this is the one place that
    /// implements the spec's "diff, don't replace" rule.
    /// </summary>
    public static void MergeFrom(this Issue issue, IssueTagField field, IEnumerable<string?> rawValues)
    {
        var incoming = SplitDistinct(rawValues);
        var existing = issue.Tags.Where(t => t.Field == field).ToList();

        foreach (var tag in existing)
        {
            if (!incoming.Contains(tag.Value, StringComparer.OrdinalIgnoreCase))
            {
                issue.Tags.Remove(tag);
            }
        }

        var existingValues = new HashSet<string>(existing.Select(t => t.Value), StringComparer.OrdinalIgnoreCase);
        string defaultCategory = field == IssueTagField.Genre ? "Genre" : "Uncategorized";
        foreach (string value in incoming)
        {
            if (!existingValues.Contains(value))
            {
                issue.Tags.Add(new IssueTag
                {
                    IssueId = issue.Id,
                    Field = field,
                    Value = value,
                    Category = defaultCategory,
                    Weight = IssueTagWeight.Unset,
                });
            }
        }
    }

    private static string? Joined(Issue issue, IssueTagField field)
    {
        var values = issue.Tags.Where(t => t.Field == field).Select(t => t.Value).ToList();
        return values.Count > 0 ? string.Join(", ", values) : null;
    }

    /// <summary>
    /// Same comma-split convention as <c>Paperbunkr.App.Models.CsvFieldAggregator.Distinct</c> -
    /// duplicated rather than shared because <c>Paperbunkr.Data</c> doesn't reference
    /// <c>Paperbunkr.App</c>.
    /// </summary>
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
