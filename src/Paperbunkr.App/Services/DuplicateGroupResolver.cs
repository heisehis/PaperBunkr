using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// The two things that can be done to a group of duplicate library entries, shared by Library Health's duplicate review and the Compare screen (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md) so
/// neither carries its own copy of the delete: remove entries (through <see cref="LibraryDeletionHelper"/>: cross-reference cleanup, the removed-files list, the Recycle Bin for the file), or acknowledge the group
/// ("not a duplicate"), which hides it until a new file joins.
/// </summary>
public static class DuplicateGroupResolver
{
    /// <summary>Removes the given entries from the library. <paramref name="deleteFile"/> false keeps their files on disk (and remembers them so a rescan does not bring them back). Saves.</summary>
    public static void RemoveIssues(IEnumerable<int> issueIds, bool deleteFile = true)
    {
        using var context = PaperbunkrDb.CreateContext();
        foreach (int issueId in issueIds)
        {
            var issue = context.Issues.Find(issueId);
            if (issue is not null)
            {
                LibraryDeletionHelper.RemoveIssue(context, issue, deleteFile);
            }
        }

        context.SaveChanges();
    }

    /// <summary>Marks every given entry as reviewed-not-a-duplicate. Saves.</summary>
    public static void Acknowledge(IEnumerable<int> issueIds)
    {
        var ids = issueIds.ToList();
        using var context = PaperbunkrDb.CreateContext();
        foreach (var issue in context.Issues.Where(i => ids.Contains(i.Id)))
        {
            issue.DuplicateAcknowledged = true;
        }

        context.SaveChanges();
    }
}
