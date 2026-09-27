using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Writes a page's <see cref="PageType"/> tag from outside the reader (Library Health's "Tag as Deleted", Needs
/// Review's ad-page Accept - docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §4, §5).
/// Mirrors the reader's own <c>SetPageOverride</c> row rules: <see cref="IssuePage"/> rows are sparse, so tagging a
/// page Story with no rotation or spread override removes its row, and an existing row's rotation and spread
/// position are kept when only the type changes.
/// </summary>
public static class IssuePageTagger
{
    /// <summary>Sets the tag and saves. Returns the row, or <see langword="null"/> when the page ended up untagged.</summary>
    public static IssuePage? SetPageType(PaperbunkrDbContext context, int issueId, int pageNumber, PageType type)
    {
        var row = context.IssuePages.FirstOrDefault(p => p.IssueId == issueId && p.PageNumber == pageNumber);

        if (type == PageType.Story && (row is null || (row.RotationDegrees == 0 && row.SpreadPosition is null or PageSpreadPosition.Default)))
        {
            if (row is not null)
            {
                context.IssuePages.Remove(row);
                context.SaveChanges();
            }

            return null;
        }

        if (row is null)
        {
            row = new IssuePage { IssueId = issueId, PageNumber = pageNumber };
            context.IssuePages.Add(row);
        }

        row.PageType = type;
        context.SaveChanges();
        return row;
    }
}
