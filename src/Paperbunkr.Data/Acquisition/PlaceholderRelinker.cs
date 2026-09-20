using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Acquisition;

/// <summary>
/// Once an issue arrives in the library, every reading-list entry that was only a placeholder for it is pointed at the real file, in place
/// (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §8) - so an arc fills in at its own position instead of gaining a duplicate.
/// </summary>
public static class PlaceholderRelinker
{
    /// <summary>
    /// Relinks the reading-list rows whose placeholder is <paramref name="number"/> of a series named <paramref name="seriesName"/> (or of the local
    /// series <paramref name="seriesId"/>) to <paramref name="newIssueId"/>. Returns how many rows were relinked.
    /// </summary>
    public static int Relink(PaperbunkrDbContext context, string seriesName, int? seriesId, string number, int newIssueId)
    {
        var placeholderItems = context.ReadingListItems
            .Include(i => i.Issue).ThenInclude(i => i!.Series)
            .Where(i => i.Issue != null && i.Issue.IsPlaceholder && i.IssueId != newIssueId)
            .AsEnumerable()
            .Where(i => IssueNumbers.Equal(i.Issue!.Number, number)
                && (i.Issue.SeriesId == seriesId || SeriesNames.Same(i.Issue.Series?.Name, seriesName)))
            .ToList();

        foreach (var item in placeholderItems)
        {
            // Two rows in the same list may not point at one issue (the list forbids duplicates); leave such a row for the user.
            bool alreadyInList = context.ReadingListItems.Any(i => i.ReadingListId == item.ReadingListId && i.IssueId == newIssueId);
            if (!alreadyInList)
            {
                ReadingListItemLinker.Relink(context, item.Id, newIssueId);
            }
        }

        return placeholderItems.Count;
    }
}
