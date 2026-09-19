using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// Merges one <see cref="Series"/> into another - extracted from
/// <c>NeedsReviewViewModel.MergeSeriesInto</c> (docs/superpowers/specs/2026-09-17-series-name-
/// matching-and-empty-row-cleanup-design.md) so both the existing Series Conflicts flow (same-issue
/// embedded-vs-filename mismatches) and the new "Find Similar Series" tool (punctuation-variant
/// duplicates) share one implementation instead of two copies of the same reassignment logic.
/// </summary>
internal static class SeriesMergeHelper
{
    /// <summary>
    /// Moves every <paramref name="source"/> issue into <paramref name="target"/>, skipping one
    /// whose Number+Volume already exists on the target (deleting the source's duplicate instead),
    /// then deletes the now-empty <paramref name="source"/> series.
    /// </summary>
    public static void MergeInto(PaperbunkrDbContext context, Series source, Series target)
    {
        var existingKeys = new HashSet<(string?, string?)>(target.Issues.Select(i => (i.EffectiveNumber(), i.EffectiveVolume())));

        foreach (var issue in source.Issues.ToList())
        {
            if (existingKeys.Add((issue.EffectiveNumber(), issue.EffectiveVolume())))
            {
                issue.SeriesId = target.Id;
                issue.Series = target;
            }
            else
            {
                context.Issues.Remove(issue);
                CoverImageCache.Invalidate(issue.Id);
            }
        }

        context.Series.Remove(source);
    }
}
