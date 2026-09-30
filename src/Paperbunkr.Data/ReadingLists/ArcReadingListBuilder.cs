using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>Result of <see cref="ArcReadingListBuilder.RefreshAsync"/> - shown to the user via the screen's existing <c>StatusMessage</c> (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §4).</summary>
public sealed record ArcRefreshResult(ReadingList List, int AddedCount, int ReplacedPlaceholderCount, int StillMissingCount, RoleDetectionSummary? Roles = null);

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
        var created = new List<(ArcIssue Arc, Issue Issue, ReadingListItem Item)>();
        foreach (var arcIssue in arcIssues)
        {
            var issue = ResolveArcIssue(context, arcIssue);
            var item = new ReadingListItem { IssueId = issue.Id, SortOrder = sortOrder++ };
            list.Items.Add(item);
            created.Add((arcIssue, issue, item));
        }

        // Roles the source's own words (or the issues' titles and formats) make certain are applied as automatic; weaker guesses wait as
        // suggestions. The user's own edits are never touched (docs/superpowers/specs/2026-09-25-reading-list-role-detection-design.md).
        DetectRoles(created.Select(c => (c.Arc, c.Issue, c.Item)).ToList());

        context.ReadingLists.Add(list);
        // Lands at the end of the top level; the Reading screen moves it into the folder the user has selected (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §2).
        ReadingListFolders.PlaceNewList(context, list, null);
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

        var arcIssues = await source.GetArcIssuesInOrderAsync(list.ArcId, cancellationToken).ConfigureAwait(false);
        var overview = await TryGetOverviewAsync(source, list.ArcId, cancellationToken).ConfigureAwait(false);

        // Two arc entries can resolve to the same local Issue (e.g. a punctuation-variant series name both folding to the same
        // series via ReadingListMatcher's cascade) - the first occurrence wins, in the reconciler and for role detection alike.
        var resolved = arcIssues.Select(a => (Arc: a, Issue: ResolveArcIssue(context, a))).ToList();

        // Reorder/add/remove/orphaned-placeholder cleanup is shared with a continuity list's Rebuild; Role/Notes/GroupLabel are
        // never touched by refresh.
        var reconciled = ReadingListReconciler.Reconcile(context, list, resolved.Select(r => r.Issue.Id).ToList());

        var seen = new HashSet<int>();
        var detectable = resolved
            .Where(r => seen.Add(r.Issue.Id))
            .Select(r => (r.Arc, r.Issue, Item: reconciled.ItemsByIssue[r.Issue.Id]))
            .ToList();

        // Refresh never overwrites a role the user set: detection only fills an empty/automatic slot, otherwise it leaves a suggestion.
        var roles = DetectRoles(detectable);

        if (!string.IsNullOrEmpty(overview?.Description))
        {
            list.Description = overview.Description;
        }
        if (!string.IsNullOrEmpty(overview?.CoverImageUrl))
        {
            list.CoverImageUrl = overview.CoverImageUrl;
        }

        // What this refresh actually changed, announced once as a single compound change
        // (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §5.4).
        ReadingListManager.Record(context, list, reconciled.Kind, reconciled.AddedIssueIds, reconciled.RemovedIssueIds);

        context.SaveChanges();

        int stillMissingCount = context.ReadingListItems
            .Include(i => i.Issue)
            .Count(i => i.ReadingListId == list.Id && i.Issue!.IsPlaceholder);

        return new ArcRefreshResult(list, reconciled.AddedIssueIds.Count, reconciled.ReplacedPlaceholderCount, stillMissingCount, roles);
    }

    private static RoleDetectionSummary DetectRoles(IReadOnlyList<(ArcIssue Arc, Issue Issue, ReadingListItem Item)> members)
    {
        string? dominant = MemberRoleDetector.DominantSeries(members.Select(m => m.Arc.Series));
        var summary = RoleDetectionSummary.Empty;
        for (int position = 0; position < members.Count; position++)
        {
            var (arc, issue, item) = members[position];
            var suggestion = MemberRoleDetection.Suggest(issue, arc.Series, arc.Annotation, dominant, position, members.Count, arc.Title, arc.Summary);
            summary += MemberRoleDetection.Tally(MemberRoleApplier.Apply(item, suggestion));
        }

        return summary;
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
