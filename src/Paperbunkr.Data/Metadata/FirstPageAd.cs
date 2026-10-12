using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The page tags behind ComicRack's "Remove Scanner Credits" and "Move First Page to End" scripts (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §3): the first
/// page is a scanner's credit page, so it is tagged <see cref="PageType.Advertisement"/> (the reader skips it) and the second page <see cref="PageType.Cover"/>. The scripts also moved
/// the first page to the end of the file's page list; archives are not rewritten here, so only the tags are set.
/// </summary>
public static class FirstPageAd
{
    /// <summary>Tags the first two pages of the issue; false (and nothing changed) when its page count is not known to be at least two.</summary>
    public static bool Apply(PaperbunkrDbContext context, int issueId)
    {
        int? pages = context.Issues.Where(i => i.Id == issueId).Select(i => i.PageCount).FirstOrDefault();
        if (pages is not >= 2)
        {
            return false;
        }

        // The tagger keeps a page's rotation and spread position when only its type changes.
        IssuePageTagger.SetPageType(context, issueId, 0, PageType.Advertisement);
        IssuePageTagger.SetPageType(context, issueId, 1, PageType.Cover);
        return true;
    }
}
