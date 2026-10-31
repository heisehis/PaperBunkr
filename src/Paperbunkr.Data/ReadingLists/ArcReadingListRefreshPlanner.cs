using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Matching;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.ReadingLists;

internal sealed record ArcRefreshPlanResult(
    IReadOnlyList<(ArcIssue Arc, Issue Issue, ReadingListItem Item)> Members,
    IReadOnlyList<int> AddedIssueIds,
    IReadOnlyList<int> RemovedIssueIds,
    IReadOnlyList<int> FreedPlaceholderIds,
    int RetainedLocalCount,
    int RelinkedCount,
    int MovedCount,
    int AddedRowCount,
    int RemovedRowCount,
    int ReplacedPlaceholderCount)
{
    public bool Reordered => MovedCount > 0;
}

/// <summary>
/// Reconciles source-backed rows without changing the shared reading-list reconciler used by
/// Continuity rebuilds. A source entry's identity is separate from the issue it currently resolves
/// to, so a changed automatic match can reuse its row and preserve its annotations.
/// </summary>
internal static class ArcReadingListRefreshPlanner
{
    public static ArcRefreshPlanResult Apply(
        PaperbunkrDbContext context,
        ReadingList list,
        IReadOnlyList<(ArcIssue Arc, ResolvedItem Entry)> resolved)
    {
        context.MarkReadingListManaged(list);

        var original = list.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        var oldSourceRows = original.Where(i => i.Source is not null).ToList();
        var localRows = original.Where(i => i.Source is null).ToList();
        var retainedMissingRows = new List<ReadingListItem>();
        var usedOldRows = new HashSet<ReadingListItem>();
        var reservedIssues = new HashSet<int>(localRows.Select(i => i.IssueId));
        var members = new List<(ArcIssue Arc, Issue Issue, ReadingListItem Item)>();
        var added = new List<int>();
        var removed = new List<int>();
        var freedPlaceholders = new HashSet<int>();
        int relinked = 0, addedRows = 0, removedRows = 0, replacedPlaceholders = 0;
        int moved = 0;

        foreach (var item in oldSourceRows)
        {
            bool stillPresent = resolved.Any(incoming => StrictlySameSourceEntry(item.Source!, incoming.Entry.Source));
            if (!stillPresent && item.Source!.Method == MatchMethod.User)
            {
                retainedMissingRows.Add(item);
                reservedIssues.Add(item.IssueId);
            }
        }

        int position = 0;
        foreach (var incoming in resolved)
        {
            ReadingListItem? existing = FindExisting(oldSourceRows, usedOldRows, incoming.Entry.Source, resolved);
            if (reservedIssues.Contains(incoming.Entry.Issue.Id))
            {
                // A hand-added or retained manual row already owns this issue. Keep the local row
                // and do not manufacture a second source row for the same comic.
                if (existing is not null)
                {
                    usedOldRows.Add(existing);
                    RemoveRow(context, list, existing, removed, freedPlaceholders, ref removedRows, ref replacedPlaceholders);
                }

                continue;
            }

            ReadingListItem item;
            if (existing is null)
            {
                item = new ReadingListItem
                {
                    ReadingListId = list.Id,
                    ReadingList = list,
                    Issue = incoming.Entry.Issue,
                    IssueId = incoming.Entry.Issue.Id,
                    Source = incoming.Entry.Source,
                };
                context.ReadingListItems.Add(item);
                list.Items.Add(item);
                added.Add(incoming.Entry.Issue.Id);
                addedRows++;
            }
            else
            {
                usedOldRows.Add(existing);
                if (existing.IssueId != incoming.Entry.Issue.Id)
                {
                    removed.Add(existing.IssueId);
                    added.Add(incoming.Entry.Issue.Id);
                    if (existing.Issue is { IsPlaceholder: true } oldPlaceholder)
                    {
                        freedPlaceholders.Add(oldPlaceholder.Id);
                        replacedPlaceholders++;
                    }

                    existing.Issue = incoming.Entry.Issue;
                    existing.IssueId = incoming.Entry.Issue.Id;
                    relinked++;
                }

                if (existing.Source is null)
                {
                    existing.Source = incoming.Entry.Source;
                }
                else
                {
                    CopySource(existing.Source, incoming.Entry.Source);
                }

                item = existing;
            }

            if (item.SortOrder != position)
            {
                moved++;
            }

            item.SortOrder = position++;
            if (!reservedIssues.Add(incoming.Entry.Issue.Id))
            {
                // This is defensive; the resolver already removes duplicate target issues.
                RemoveRow(context, list, item, removed, freedPlaceholders, ref removedRows, ref replacedPlaceholders);
                continue;
            }

            members.Add((incoming.Arc, incoming.Entry.Issue, item));
        }

        // Keep local additions and provider rows the user manually linked after the provider order.
        // Do not let a local row lose its existing notes, role, group, or relative order.
        var retained = localRows.Concat(retainedMissingRows)
            .Distinct()
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToList();
        foreach (var item in retained)
        {
            if (!list.Items.Contains(item))
            {
                list.Items.Add(item);
            }

            if (item.SortOrder != position)
            {
                moved++;
            }

            item.SortOrder = position++;
        }

        foreach (var old in oldSourceRows.Where(i => !usedOldRows.Contains(i) && !retainedMissingRows.Contains(i)).ToList())
        {
            RemoveRow(context, list, old, removed, freedPlaceholders, ref removedRows, ref replacedPlaceholders);
        }

        list.UpdatedAt = DateTime.UtcNow;
        return new ArcRefreshPlanResult(members, added, removed, freedPlaceholders.ToList(), retained.Count, relinked, moved,
            addedRows, removedRows, replacedPlaceholders);
    }

    private static ReadingListItem? FindExisting(
        IReadOnlyList<ReadingListItem> candidates,
        IReadOnlySet<ReadingListItem> used,
        ReadingListItemSource incoming,
        IReadOnlyList<(ArcIssue Arc, ResolvedItem Entry)> allIncoming)
    {
        var matches = candidates.Where(row => !used.Contains(row)
                && StrictlySameSourceEntry(row.Source!, incoming)
                && allIncoming.Count(other => StrictlySameSourceEntry(row.Source!, other.Entry.Source)) == 1)
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool StrictlySameSourceEntry(ReadingListItemSource left, ReadingListItemSource right)
    {
        if (left.SourceKey is not null && right.SourceKey is not null
            && !string.Equals(left.SourceKey, right.SourceKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Conflicts(left.ComicVineIssueId, right.ComicVineIssueId)
            || Conflicts(left.ComicVineSeriesId, right.ComicVineSeriesId)
            || Conflicts(left.MetronIssueId, right.MetronIssueId)
            || Conflicts(left.MetronSeriesId, right.MetronSeriesId))
        {
            return false;
        }

        if (left.SourceEntryId is not null && right.SourceEntryId is not null)
        {
            return string.Equals(left.SourceEntryId, right.SourceEntryId, StringComparison.OrdinalIgnoreCase);
        }

        bool hasSharedProviderId = SameNonEmpty(left.ComicVineIssueId, right.ComicVineIssueId)
            || SameNonEmpty(left.MetronIssueId, right.MetronIssueId);
        if (hasSharedProviderId)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(left.Series)
            && !string.IsNullOrWhiteSpace(left.Number)
            && string.Equals(SeriesKeys.Of(left.Series), SeriesKeys.Of(right.Series), StringComparison.Ordinal)
            && string.Equals(IssueNumberKey.Of(left.Number), IssueNumberKey.Of(right.Number), StringComparison.Ordinal)
            && left.Year == right.Year
            && EqualOptional(left.Volume, right.Volume)
            && EqualOptional(left.Format, right.Format);
    }

    private static void CopySource(ReadingListItemSource target, ReadingListItemSource source)
    {
        target.Series = source.Series;
        target.Number = source.Number;
        target.Volume = source.Volume;
        target.Year = source.Year;
        target.Format = source.Format;
        target.Title = source.Title;
        target.SeriesKey = source.SeriesKey;
        target.NumberKey = source.NumberKey;
        target.SourceKey = source.SourceKey;
        target.SourceEntryId = source.SourceEntryId;
        target.ComicVineIssueId = source.ComicVineIssueId;
        target.ComicVineSeriesId = source.ComicVineSeriesId;
        target.MetronIssueId = source.MetronIssueId;
        target.MetronSeriesId = source.MetronSeriesId;
        target.Method = source.Method;
        target.State = source.State;
        target.LastCheckedUtc = source.LastCheckedUtc;
    }

    private static void RemoveRow(PaperbunkrDbContext context, ReadingList list, ReadingListItem item, ICollection<int> removed, ISet<int> freedPlaceholders,
        ref int removedRows, ref int replacedPlaceholders)
    {
        removedRows++;
        if (!removed.Contains(item.IssueId))
        {
            removed.Add(item.IssueId);
        }

        if (item.Issue is { IsPlaceholder: true } placeholder)
        {
            freedPlaceholders.Add(placeholder.Id);
            replacedPlaceholders++;
        }

        context.ReadingListItems.Remove(item);
        list.Items.Remove(item);
    }

    private static bool Conflicts(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SameNonEmpty(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool EqualOptional(string? left, string? right) =>
        string.Equals(string.IsNullOrWhiteSpace(left) ? null : left.Trim(), string.IsNullOrWhiteSpace(right) ? null : right.Trim(), StringComparison.OrdinalIgnoreCase);
}
