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
    /// Returns an issue identity suitable for duplicate removal. A missing issue number is not an
    /// identity: multiple unnumbered issues all have the same null number/volume tuple, but that
    /// does not make them duplicates. Volume remains part of the key when the issue number exists.
    /// </summary>
    internal static bool TryGetDuplicateKey(Issue issue, out (string? Number, string? Volume) key)
    {
        string? number = issue.EffectiveNumber();
        if (string.IsNullOrWhiteSpace(number))
        {
            key = default;
            return false;
        }

        key = (number, issue.EffectiveVolume());
        return true;
    }

    /// <summary>
    /// Moves every <paramref name="source"/> issue into <paramref name="target"/>, skipping an
    /// unreferenced issue whose Number+Volume already exists on the target, then deletes the source
    /// series. A duplicate issue with a restrictive reference is retained in the target so curated
    /// reading-list/event data and series covers do not make the merge fail or get discarded.
    /// </summary>
    public static void MergeInto(PaperbunkrDbContext context, Series source, Series target)
    {
        var existingKeys = new HashSet<(string? Number, string? Volume)>();
        foreach (var issue in target.Issues)
        {
            if (TryGetDuplicateKey(issue, out var key))
            {
                existingKeys.Add(key);
            }
        }

        foreach (var issue in source.Issues.ToList())
        {
            if (!TryGetDuplicateKey(issue, out var key) || existingKeys.Add(key))
            {
                issue.SeriesId = target.Id;
                issue.Series = target;
            }
            else if (HasRestrictiveReference(context, issue.Id))
            {
                // ReadingListItems and EventMemberships intentionally restrict issue deletion, and
                // Series.CoverIssueId does too. Preserve this duplicate issue when any of those
                // rows still depends on it; the parent series is still merged successfully.
                issue.SeriesId = target.Id;
                issue.Series = target;
            }
            else
            {
                context.Issues.Remove(issue);
                CoverImageCache.Invalidate(issue.Id);
            }
        }

        // A series cover can itself be one of the issues removed above. Move that cover selection
        // to the survivor (if it has none) and clear the source FK before deleting the source row.
        if (source.CoverIssueId is int sourceCoverIssueId)
        {
            target.CoverIssueId ??= sourceCoverIssueId;
            source.CoverIssueId = null;
            source.CoverIssue = null;
        }

        CarrySeriesIdentity(context, source, target);
        context.Series.Remove(source);
    }

    /// <summary>
    /// What a merge keeps from the series it removes (docs/superpowers/specs/2026-10-22-scrape-series-split-and-series-fields-design.md Q5):
    /// its provider links where the target has none for that source (they used to be left pointing at a deleted row), its alternate titles and
    /// tags. The target is marked keep-together, so the next scrape doesn't split what the user just joined.
    /// </summary>
    private static void CarrySeriesIdentity(PaperbunkrDbContext context, Series source, Series target)
    {
        var sourceLinks = context.ComicMetadataExternalIds
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Series && e.EntityId == source.Id)
            .ToList();
        var targetProviders = context.ComicMetadataExternalIds
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Series && e.EntityId == target.Id)
            .Select(e => e.Provider)
            .ToHashSet();
        foreach (var link in sourceLinks)
        {
            if (targetProviders.Add(link.Provider))
            {
                link.EntityId = target.Id;
            }
            else
            {
                context.ComicMetadataExternalIds.Remove(link);
            }
        }

        foreach (var title in context.SeriesTitles.Where(t => t.SeriesId == source.Id).ToList())
        {
            if (!context.SeriesTitles.Any(t => t.SeriesId == target.Id && t.Type == title.Type && t.Value == title.Value))
            {
                title.SeriesId = target.Id;
                title.Series = target;
            }
        }

        foreach (var tag in context.Set<SeriesTag>().Where(t => t.SeriesId == source.Id).ToList())
        {
            if (!context.Set<SeriesTag>().Any(t => t.SeriesId == target.Id && t.Field == tag.Field && t.Value == tag.Value))
            {
                tag.SeriesId = target.Id;
                tag.Series = target;
            }
        }

        Paperbunkr.Data.ComicVine.Scraping.SeriesSplit.SetKeepTogether(target, keep: true);
    }

    private static bool HasRestrictiveReference(PaperbunkrDbContext context, int issueId) =>
        context.ReadingListItems.Any(item => item.IssueId == issueId)
        || context.EventMemberships.Any(member => member.IssueId == issueId)
        || context.Series.Any(series => series.CoverIssueId == issueId);
}
