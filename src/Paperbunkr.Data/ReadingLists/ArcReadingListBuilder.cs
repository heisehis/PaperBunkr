using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>Result of <see cref="ArcReadingListBuilder.RefreshAsync"/> - shown to the user via the screen's existing <c>StatusMessage</c> (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §4).</summary>
public sealed record ArcRefreshResult(ReadingList List, int AddedCount, int ReplacedPlaceholderCount, int StillMissingCount);

/// <summary>
/// Create-from-arc and Refresh (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md
/// §4) - both built on the existing <see cref="ReadingListMatcher"/>, no new matching logic.
/// </summary>
public static class ArcReadingListBuilder
{
    public static async Task<ReadingList> CreateFromArcAsync(
        PaperbunkrDbContext context, IReadingListSource source, ArcSearchResult arc, CancellationToken cancellationToken)
    {
        var arcIssues = await source.GetArcIssuesInOrderAsync(arc.Id, cancellationToken).ConfigureAwait(false);
        if (arcIssues.Count == 0)
        {
            throw new InvalidOperationException($"'{arc.Name}' has no indexed issues on {source.DisplayName}.");
        }

        var overview = await TryGetOverviewAsync(source, arc.Id, cancellationToken).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = arc.Name,
            SortOrder = context.ReadingLists.Count(),
            CreatedAt = now,
            UpdatedAt = now,
            Source = source.SourceKey,
            ArcId = arc.Id,
            ArcName = arc.Name,
            Description = overview?.Description,
            CoverImageUrl = overview?.CoverImageUrl,
            Type = ReadingListType.Community,
        };

        int sortOrder = 0;
        foreach (var arcIssue in arcIssues)
        {
            var issue = ResolveArcIssue(context, arcIssue);
            list.Items.Add(new ReadingListItem { IssueId = issue.Id, SortOrder = sortOrder++ });
        }

        context.ReadingLists.Add(list);
        ReadingListManager.RecordCreatedWithItems(context, list);
        context.SaveChanges();
        return list;
    }

    /// <param name="sourceOverride">Test-only seam - when provided, skips <see cref="ReadingListSourceRegistry"/> resolution entirely so tests can exercise reconciliation against a fake <see cref="IReadingListSource"/> without registering a real one.</param>
    public static async Task<ArcRefreshResult> RefreshAsync(
        PaperbunkrDbContext context, int readingListId, CancellationToken cancellationToken, IReadingListSource? sourceOverride = null)
    {
        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue)
            .First(r => r.Id == readingListId);

        if (string.IsNullOrEmpty(list.Source) || string.IsNullOrEmpty(list.ArcId))
        {
            throw new InvalidOperationException("This reading list isn't linked to an external source.");
        }

        var source = sourceOverride ?? ReadingListSourceRegistry.Get(context, list.Source)
            ?? throw new InvalidOperationException($"'{list.Source}' is unavailable - check its credentials in Preferences.");

        // Defensive cleanup: a prior bug (or race) could have left more than one ReadingListItem
        // pointing at the same Issue within this list, which crashes the ToDictionary below with
        // "An item with the same key has already been added." Keep the oldest (lowest Id), remove
        // the rest, before anything else reads list.Items.
        foreach (var duplicateGroup in list.Items.GroupBy(i => i.IssueId).Where(g => g.Count() > 1).ToList())
        {
            foreach (var extra in duplicateGroup.OrderBy(i => i.Id).Skip(1).ToList())
            {
                context.ReadingListItems.Remove(extra);
                list.Items.Remove(extra);
            }
        }

        var arcIssues = await source.GetArcIssuesInOrderAsync(list.ArcId, cancellationToken).ConfigureAwait(false);
        var overview = await TryGetOverviewAsync(source, list.ArcId, cancellationToken).ConfigureAwait(false);

        var oldByIssueId = list.Items.ToDictionary(i => i.IssueId);
        var keptIssueIds = new HashSet<int>();
        int addedCount = 0;
        int sortOrder = 0;

        // What this refresh actually changed, announced once at the end as a single compound change
        // (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §5.4).
        var addedIssueIds = new List<int>();
        var removedIssueIds = new List<int>();
        bool reordered = false;

        foreach (var arcIssue in arcIssues)
        {
            var resolved = ResolveArcIssue(context, arcIssue);
            if (!keptIssueIds.Add(resolved.Id))
            {
                // Two arc entries resolved to the same local Issue (e.g. a punctuation-variant
                // series name both folding to the same series via ReadingListMatcher's cascade) -
                // already handled by the first occurrence this pass; adding/touching it again
                // would create exactly the duplicate-key row this method just cleaned up above.
                continue;
            }

            if (oldByIssueId.TryGetValue(resolved.Id, out var existingItem))
            {
                // Already in the list (real or still a placeholder) - just move it to the arc's
                // current position. Role/Notes/GroupLabel are never touched by refresh.
                if (existingItem.SortOrder != sortOrder)
                {
                    reordered = true;
                }

                existingItem.SortOrder = sortOrder++;
            }
            else
            {
                context.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, IssueId = resolved.Id, SortOrder = sortOrder++ });
                addedCount++;
                addedIssueIds.Add(resolved.Id);
            }
        }

        int replacedPlaceholderCount = 0;
        foreach (var oldItem in list.Items.Where(i => !keptIssueIds.Contains(i.IssueId)).ToList())
        {
            var orphanedIssue = oldItem.Issue;
            int oldItemId = oldItem.Id;
            context.ReadingListItems.Remove(oldItem);
            removedIssueIds.Add(oldItem.IssueId);

            if (orphanedIssue is { IsPlaceholder: true })
            {
                replacedPlaceholderCount++;
                bool referencedElsewhere = context.ReadingListItems
                    .Any(i => i.IssueId == orphanedIssue.Id && i.Id != oldItemId);
                if (!referencedElsewhere)
                {
                    context.Issues.Remove(orphanedIssue);
                }
            }
        }

        if (!string.IsNullOrEmpty(overview?.Description))
        {
            list.Description = overview.Description;
        }
        if (!string.IsNullOrEmpty(overview?.CoverImageUrl))
        {
            list.CoverImageUrl = overview.CoverImageUrl;
        }
        list.UpdatedAt = DateTime.UtcNow;

        var kind = ReadingListChangeKind.None;
        if (addedIssueIds.Count > 0)
        {
            kind |= ReadingListChangeKind.Added;
        }

        if (removedIssueIds.Count > 0)
        {
            kind |= ReadingListChangeKind.Removed;
        }

        if (reordered)
        {
            kind |= ReadingListChangeKind.Reordered;
        }

        ReadingListManager.Record(context, list, kind, addedIssueIds, removedIssueIds);

        context.SaveChanges();

        int stillMissingCount = context.ReadingListItems
            .Include(i => i.Issue)
            .Count(i => i.ReadingListId == list.Id && i.Issue!.IsPlaceholder);

        return new ArcRefreshResult(list, addedCount, replacedPlaceholderCount, stillMissingCount);
    }

    private static Issue ResolveArcIssue(PaperbunkrDbContext context, ArcIssue arcIssue)
    {
        return ReadingListMatcher.ResolveOrCreatePlaceholder(
            context,
            arcIssue.Series,
            arcIssue.Number,
            volume: null,
            year: arcIssue.Year > 0 ? arcIssue.Year : null,
            format: null);
    }

    private static async Task<ArcOverviewInfo?> TryGetOverviewAsync(IReadingListSource source, string arcId, CancellationToken cancellationToken)
    {
        try
        {
            return await source.GetArcOverviewAsync(arcId, cancellationToken).ConfigureAwait(false);
        }
        catch (ReadingListSourceException)
        {
            // Best-effort enrichment only, per IReadingListSource's own contract - never blocks
            // list creation/refresh.
            return null;
        }
    }
}
