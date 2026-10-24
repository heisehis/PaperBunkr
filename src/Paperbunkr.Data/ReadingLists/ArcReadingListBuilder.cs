using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.ReadingLists.Matching;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>Result of <see cref="ArcReadingListBuilder.RefreshAsync"/> - shown to the user via the screen's existing <c>StatusMessage</c> (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §4).</summary>
public sealed record ArcRefreshResult(ReadingList List, int AddedCount, int ReplacedPlaceholderCount, int StillMissingCount, RoleDetectionSummary? Roles = null)
{
    /// <summary>What became of every entry the source returned (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.7).</summary>
    public ImportReport? Report { get; init; }
}

/// <summary>A list just built from a source, and what became of every entry.</summary>
public sealed record ArcCreateResult(ReadingList List, ImportReport Report);

/// <summary>
/// Create-from-arc and Refresh (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md
/// §4). Entries are matched through <see cref="ReadingListResolver"/>, which uses a provider id the source supplied before anything else.
/// </summary>
public static class ArcReadingListBuilder
{
    public static async Task<ReadingList> CreateFromArcAsync(
        PaperbunkrDbContext context, IReadingListSource source, ArcSearchResult arc, CancellationToken cancellationToken) =>
        (await CreateFromArcWithReportAsync(context, source, arc, cancellationToken).ConfigureAwait(false)).List;

    public static async Task<ArcCreateResult> CreateFromArcWithReportAsync(
        PaperbunkrDbContext context, IReadingListSource source, ArcSearchResult arc, CancellationToken cancellationToken)
    {
        var fetched = await source.GetArcEntriesAsync(arc.Id, cancellationToken).ConfigureAwait(false);
        if (fetched.Issues.Count == 0)
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

        var (resolution, resolved) = Resolve(context, source.SourceKey, fetched.Issues);
        int sortOrder = 0;
        var created = new List<(ArcIssue Arc, Issue Issue, ReadingListItem Item)>();
        foreach (var (arcIssue, entry) in resolved)
        {
            var item = new ReadingListItem { Issue = entry.Issue, SortOrder = sortOrder++, Source = entry.Source };
            list.Items.Add(item);
            created.Add((arcIssue, entry.Issue, item));
        }

        // Roles the source's own words (or the issues' titles and formats) make certain are applied as automatic; weaker guesses wait as
        // suggestions. The user's own edits are never touched (docs/superpowers/specs/2026-09-25-reading-list-role-detection-design.md).
        DetectRoles(created);

        context.ReadingLists.Add(list);
        // Lands at the end of the top level; the Reading screen moves it into the folder the user has selected (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §2).
        ReadingListFolders.PlaceNewList(context, list, null);
        ReadingListManager.RecordCreatedWithItems(context, list);
        context.SaveChanges();
        return new ArcCreateResult(list, resolution.Report(fetched.SkippedLines, fetched.ReportedCount));
    }

    /// <param name="sourceOverride">Test-only seam - when provided, skips <see cref="ReadingListSourceRegistry"/> resolution entirely so tests can exercise reconciliation against a fake <see cref="IReadingListSource"/> without registering a real one.</param>
    public static async Task<ArcRefreshResult> RefreshAsync(
        PaperbunkrDbContext context, int readingListId, CancellationToken cancellationToken, IReadingListSource? sourceOverride = null)
    {
        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue)
            .Include(r => r.Items).ThenInclude(i => i.Source)
            .First(r => r.Id == readingListId);

        if (string.IsNullOrEmpty(list.Source) || string.IsNullOrEmpty(list.ArcId))
        {
            throw new InvalidOperationException("This reading list isn't linked to an external source.");
        }

        var source = sourceOverride ?? ReadingListSourceRegistry.Get(context, list.Source)
            ?? throw new InvalidOperationException($"'{list.Source}' is unavailable - check its credentials in Preferences.");

        var fetched = await source.GetArcEntriesAsync(list.ArcId, cancellationToken).ConfigureAwait(false);
        var overview = await TryGetOverviewAsync(source, list.ArcId, cancellationToken).ConfigureAwait(false);

        // Placeholders whose comic has arrived are relinked in place first, so they keep their role and notes; left to the reconciler
        // below they would be removed and re-added as new items (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.9).
        int healed = ReadingListHealer.RecheckList(context, list.Id).Relinked;

        // Two arc entries can resolve to the same local Issue (e.g. a punctuation-variant series name both folding to the same
        // series) - the first occurrence wins, in the resolver and for role detection alike; the later one is reported as a duplicate.
        var (resolution, resolved) = Resolve(context, source.SourceKey, fetched.Issues);
        context.SaveChanges();      // new placeholders need their ids before the order can be reconciled

        // Reorder/add/remove/orphaned-placeholder cleanup is shared with a continuity list's Rebuild; Role/Notes/GroupLabel are
        // never touched by refresh.
        var reconciled = ReadingListReconciler.Reconcile(context, list, resolved.Select(r => r.Entry.Issue.Id).ToList());

        var detectable = new List<(ArcIssue Arc, Issue Issue, ReadingListItem Item)>();
        foreach (var (arcIssue, entry) in resolved)
        {
            var item = reconciled.ItemsByIssue[entry.Issue.Id];

            // A new entry gets its source record; an existing one keeps what it was first asked for (and a link the user made).
            item.Source ??= entry.Source;
            detectable.Add((arcIssue, entry.Issue, item));
        }

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

        return new ArcRefreshResult(list, reconciled.AddedIssueIds.Count, reconciled.ReplacedPlaceholderCount + healed, stillMissingCount, roles)
        {
            Report = resolution.Report(fetched.SkippedLines, fetched.ReportedCount),
        };
    }

    /// <summary>The source's entries matched against the library in one pass, each kept entry paired with the arc issue it came from.</summary>
    private static (Resolution Resolution, List<(ArcIssue Arc, ResolvedItem Entry)> Resolved) Resolve(
        PaperbunkrDbContext context, string sourceKey, IReadOnlyList<ArcIssue> arcIssues)
    {
        var resolution = ReadingListResolver.Resolve(context, arcIssues.Select(a => a.ToReference(sourceKey)).ToList());
        var kept = arcIssues.Where((_, index) => resolution.Entries[index].Outcome != EntryOutcome.Duplicate).ToList();
        var items = ReadingListResolver.Materialize(context, resolution);
        return (resolution, kept.Zip(items, (arc, entry) => (arc, entry)).ToList());
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
