using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>One issue of a computed continuity order, with the group label it is read under (null = ungrouped).</summary>
public sealed record ContinuityOrderEntry(int IssueId, string? GroupLabel);

/// <summary>What a continuity list's Rebuild changed.</summary>
public sealed record ContinuityRebuildResult(int Added, int Removed, int Moved);

/// <summary>
/// Materializes a <see cref="ReadingList"/> from a <c>Continuity</c> (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-
/// browse-design.md's "continuity-scoped reading lists"), in one of two orders (docs/superpowers/specs/2026-09-28-reading-lists-build-from-
/// events-design.md §2): publication order, computed here, or story order, computed in the App from the Continuity map and passed in.
/// The list keeps <see cref="ReadingList.ContinuityId"/> so it can be rebuilt; a rebuild reconciles like an arc Refresh.
/// </summary>
public static class ContinuityReadingListBuilder
{
    /// <summary>Publication order (the original behaviour): every member-series issue, interleaved by release date.</summary>
    public static ReadingList CreateFromContinuity(PaperbunkrDbContext context, int continuityId, int? folderId = null) =>
        CreateFromOrder(context, continuityId, PublicationOrder(context, continuityId), ContinuityOrderKind.PublicationOrder, folderId);

    /// <summary>
    /// Every member-series issue, chronological across the series (interleaved by publication date), not series block by series block -
    /// that's what "publication order" means for a whole universe. No group labels: with runs grouped only when consecutive, a series
    /// name per item would put a header on nearly every row, and the row already shows its series.
    /// </summary>
    public static IReadOnlyList<ContinuityOrderEntry> PublicationOrder(PaperbunkrDbContext context, int continuityId)
    {
        var memberSeriesIds = context.ContinuityMemberships
            .Where(m => m.ContinuityId == continuityId)
            .Select(m => m.SeriesId)
            .ToList();
        var series = context.Series
            .Include(s => s.Issues)
            .Where(s => memberSeriesIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToList();

        return series
            .SelectMany(s => s.Issues.Select(i => (Issue: i, SeriesName: s.Name)))
            .OrderBy(x => x.Issue.EffectiveYear() ?? int.MaxValue)
            .ThenBy(x => x.Issue.Month ?? 0)
            .ThenBy(x => x.Issue.Day ?? 0)
            .ThenBy(x => x.SeriesName)
            .ThenBy(x => x.Issue.NumberSortKey() ?? float.MaxValue)
            .Select(x => new ContinuityOrderEntry(x.Issue.Id, null))
            .ToList();
    }

    /// <summary>Creates a continuity-linked list from a computed order. A repeated issue keeps its first position.</summary>
    public static ReadingList CreateFromOrder(
        PaperbunkrDbContext context, int continuityId, IReadOnlyList<ContinuityOrderEntry> order, ContinuityOrderKind kind, int? folderId = null)
    {
        var continuity = context.Continuities.FirstOrDefault(c => c.Id == continuityId)
            ?? throw new InvalidOperationException($"Continuity {continuityId} not found.");
        if (order.Count == 0)
        {
            throw new InvalidOperationException("This continuity has no issues to build from.");
        }

        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = kind == ContinuityOrderKind.StoryOrder ? $"{continuity.Name} (story order)" : $"{continuity.Name} (continuity)",
            CreatedAt = now,
            UpdatedAt = now,
            Type = kind == ContinuityOrderKind.StoryOrder ? ReadingListType.Chronological : ReadingListType.PublicationOrder,
            Description = string.IsNullOrWhiteSpace(continuity.Description) ? null : continuity.Description,
            ContinuityId = continuityId,
            ContinuityOrderKind = kind,
        };

        int sortOrder = 0;
        var seen = new HashSet<int>();
        foreach (var entry in order)
        {
            if (seen.Add(entry.IssueId))
            {
                list.Items.Add(new ReadingListItem { IssueId = entry.IssueId, SortOrder = sortOrder++, GroupLabel = entry.GroupLabel });
            }
        }

        context.ReadingLists.Add(list);
        ReadingListFolders.PlaceNewList(context, list, folderId);
        ReadingListManager.RecordCreatedWithItems(context, list);
        context.SaveChanges();
        return list;
    }

    /// <summary>
    /// Rebuilds a continuity-linked list against a freshly computed order (decision Q19): kept items move, new issues are added, issues
    /// that left the continuity are removed (as an arc Refresh does), notes and roles stay, and group labels are rewritten because the
    /// order generates them. Refuses an empty order rather than emptying the list.
    /// </summary>
    public static ContinuityRebuildResult RebuildFromOrder(PaperbunkrDbContext context, int readingListId, IReadOnlyList<ContinuityOrderEntry> order, LibraryEvents? events = null)
    {
        if (order.Count == 0)
        {
            throw new InvalidOperationException("This continuity has no issues to build from.");
        }

        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue)
            .First(r => r.Id == readingListId);

        var reconciled = ReadingListReconciler.Reconcile(
            context, list, order.Select(e => e.IssueId).ToList(), order.Select(e => e.GroupLabel).ToList());
        ReadingListManager.Record(context, list, reconciled.Kind, reconciled.AddedIssueIds, reconciled.RemovedIssueIds, events);
        context.SaveChanges();
        return new ContinuityRebuildResult(reconciled.AddedIssueIds.Count, reconciled.RemovedIssueIds.Count, reconciled.MovedCount);
    }
}
