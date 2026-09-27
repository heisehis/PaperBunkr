using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>What <see cref="SamePathEntryMerger.Merge"/> did: how many files had more than one library entry, and how many entries were folded away.</summary>
public sealed record SamePathMergeResult(int FilesWithSeveralEntries, int EntriesRemoved)
{
    public override string ToString() => EntriesRemoved == 0
        ? "No two library entries share a file."
        : $"Merged {EntriesRemoved} extra entr{(EntriesRemoved == 1 ? "y" : "ies")} that pointed at the same file as another ({FilesWithSeveralEntries} file{(FilesWithSeveralEntries == 1 ? string.Empty : "s")}). No files were touched.";
}

/// <summary>
/// Folds library entries that point at the SAME file into one. Nothing on disk is touched - the file stays exactly where it is - so this is safe
/// to run on a library where an organize, a rescan or a restore left two entries for one comic. The entry that is in the most reading lists and
/// events is kept (then the one with reading progress, then the oldest); the others hand over their list/event memberships, reading progress and
/// rating where the kept entry has none, and are removed. Entries that point at two DIFFERENT files (two real copies) are not touched here.
/// </summary>
public static class SamePathEntryMerger
{
    public static SamePathMergeResult Merge(PaperbunkrDbContext context)
    {
        var groups = context.Issues
            .Where(i => i.FilePath != null && !i.IsPlaceholder)
            .AsEnumerable()
            .GroupBy(i => i.FilePath!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        int removed = 0;
        foreach (var group in groups)
        {
            var ranked = group
                .OrderByDescending(i => References(context, i.Id))
                .ThenByDescending(i => (i.LastPageRead ?? 0) > 0 || i.OpenCount > 0 ? 1 : 0)
                .ThenBy(i => i.Id)
                .ToList();
            var keeper = ranked[0];
            foreach (var other in ranked.Skip(1))
            {
                HandOver(context, other, keeper);
                context.SaveChanges();

                // The keeper has the same path, so RemoveIssue leaves the file (and the do-not-re-import list) alone.
                LibraryDeletionHelper.RemoveIssue(context, other);
                context.SaveChanges();
                removed++;
            }
        }

        return new SamePathMergeResult(groups.Count, removed);
    }

    private static int References(PaperbunkrDbContext context, int issueId) =>
        context.ReadingListItems.Count(i => i.IssueId == issueId) + context.EventMemberships.Count(m => m.IssueId == issueId);

    /// <summary>Moves what the doomed entry knows onto the keeper: list/event memberships (unless the keeper is already in that list/event),
    /// the furthest reading progress, and a rating the keeper lacks.</summary>
    private static void HandOver(PaperbunkrDbContext context, Issue other, Issue keeper)
    {
        foreach (var item in context.ReadingListItems.Where(i => i.IssueId == other.Id).ToList())
        {
            if (!context.ReadingListItems.Any(k => k.ReadingListId == item.ReadingListId && k.IssueId == keeper.Id))
            {
                item.IssueId = keeper.Id;
            }
        }

        foreach (var member in context.EventMemberships.Where(m => m.IssueId == other.Id).ToList())
        {
            if (!context.EventMemberships.Any(k => k.StoryEventId == member.StoryEventId && k.IssueId == keeper.Id))
            {
                member.IssueId = keeper.Id;
            }
        }

        if ((other.LastPageRead ?? 0) > (keeper.LastPageRead ?? 0))
        {
            keeper.LastPageRead = other.LastPageRead;
        }

        keeper.OpenCount = Math.Max(keeper.OpenCount, other.OpenCount);
        keeper.Rating ??= other.Rating;
    }
}
