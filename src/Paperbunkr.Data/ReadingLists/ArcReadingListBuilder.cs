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

    public int RetainedLocalCount { get; init; }

    public int RemovedCount { get; init; }

    public int RelinkedCount { get; init; }
}

/// <summary>A list just built from a source, and what became of every entry.</summary>
public sealed record ArcCreateResult(ReadingList List, ImportReport Report);

/// <summary>
/// Create-from-arc and Refresh (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md
/// §4). Entries are matched through <see cref="ReadingListResolver"/>, which uses a provider id the source supplied before anything else.
/// </summary>
public static class ArcReadingListBuilder
{
    public static async Task<PreparedReadingList> PrepareAsync(IReadingListSource source, ArcSearchResult arc, CancellationToken cancellationToken)
    {
        var fetched = await source.GetArcEntriesAsync(arc.Id, cancellationToken).ConfigureAwait(false);
        if (fetched.Issues.Count == 0) throw new InvalidOperationException($"'{arc.Name}' has no indexed issues on {source.DisplayName}.");
        var overview = await TryGetOverviewAsync(source, arc.Id, cancellationToken).ConfigureAwait(false);
        return new PreparedReadingList(arc.Name, fetched.Issues.Select(a => a.ToReference(source.SourceKey)).ToList())
        {
            Source = source.SourceKey, ArcId = arc.Id, ArcName = arc.Name, Description = overview?.Description, CoverImageUrl = overview?.CoverImageUrl,
            ArcIssues = fetched.Issues, Skipped = fetched.SkippedLines, Reported = fetched.ReportedCount,
        };
    }

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

        string sourceKey = list.Source;
        string arcId = list.ArcId;
        var source = sourceOverride ?? ReadingListSourceRegistry.Get(context, sourceKey)
            ?? throw new InvalidOperationException($"'{sourceKey}' is unavailable - check its credentials in Preferences.");

        var fetched = await source.GetArcEntriesAsync(arcId, cancellationToken).ConfigureAwait(false);
        if (fetched.SkippedLines.Count > 0 || (fetched.ReportedCount is int reported && reported > fetched.Issues.Count))
        {
            string count = fetched.ReportedCount is int expected ? $"The source reported {expected} entries but returned {fetched.Issues.Count}. " : string.Empty;
            string skipped = fetched.SkippedLines.Count > 0 ? $"{fetched.SkippedLines.Count} source entr{(fetched.SkippedLines.Count == 1 ? "y was" : "ies were")} skipped. " : string.Empty;
            throw new InvalidOperationException($"Refresh stopped because the source response was incomplete. {count}{skipped}The reading list was left unchanged.");
        }

        var overview = await TryGetOverviewAsync(source, arcId, cancellationToken).ConfigureAwait(false);

        // No network work is done while holding the database write transaction. Drop the pre-fetch
        // snapshot and reload inside the transaction so changes made during the fetch are not lost.
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var current = await context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue)
            .Include(r => r.Items).ThenInclude(i => i.Source)
            .FirstOrDefaultAsync(r => r.Id == readingListId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("This reading list was deleted while its source was refreshing. No changes were made.");
        if (!string.Equals(current.Source, sourceKey, StringComparison.Ordinal)
            || !string.Equals(current.ArcId, arcId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("This reading list's source changed while it was refreshing. No changes were made; try again.");
        }

        // The existing conservative healer joins this transaction. Entries still waiting for a comic
        // are rechecked before the provider's latest order is resolved.
        int healed = ReadingListHealer.RecheckList(context, current.Id).Relinked;

        // Two arc entries can resolve to the same local Issue (e.g. a punctuation-variant series name
        // folding to the same series) - the first wins and later entries are reported as duplicates.
        var (resolution, resolved) = Resolve(context, source.SourceKey, fetched.Issues);
        context.SaveChanges(); // allocate IDs for new placeholders while still inside the transaction

        var planned = ArcReadingListRefreshPlanner.Apply(context, current, resolved);
        context.SaveChanges(); // persist the complete new order before checking whether old placeholders are orphaned

        foreach (int placeholderId in planned.FreedPlaceholderIds.Distinct())
        {
            ReadingListItemLinker.DeleteIfUnreferencedPlaceholder(context, placeholderId);
        }

        var roles = DetectRoles(planned.Members);
        if (!string.IsNullOrEmpty(overview?.Description)) current.Description = overview.Description;
        if (!string.IsNullOrEmpty(overview?.CoverImageUrl)) current.CoverImageUrl = overview.CoverImageUrl;

        var changeKind = (planned.AddedIssueIds.Count > 0 ? ReadingListChangeKind.Added : ReadingListChangeKind.None)
            | (planned.RemovedIssueIds.Count > 0 ? ReadingListChangeKind.Removed : ReadingListChangeKind.None)
            | (planned.Reordered ? ReadingListChangeKind.Reordered : ReadingListChangeKind.None);
        ReadingListManager.Record(context, current, changeKind, planned.AddedIssueIds, planned.RemovedIssueIds);
        context.SaveChanges();

        int stillMissingCount = await context.ReadingListItems
            .Include(i => i.Issue)
            .CountAsync(i => i.ReadingListId == current.Id && i.Issue!.IsPlaceholder, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ArcRefreshResult(current, planned.AddedRowCount, planned.ReplacedPlaceholderCount + healed, stillMissingCount, roles)
        {
            Report = resolution.Report(fetched.SkippedLines, fetched.ReportedCount),
            RetainedLocalCount = planned.RetainedLocalCount,
            RemovedCount = planned.RemovedRowCount,
            RelinkedCount = planned.RelinkedCount + healed,
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

    internal static RoleDetectionSummary DetectRoles(IReadOnlyList<(ArcIssue Arc, Issue Issue, ReadingListItem Item)> members)
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
