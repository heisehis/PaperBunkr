using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Manual read/unread toggle (docs/superpowers/specs/2026-08-23-mark-as-read-design.md) - real CE
/// precedent (<c>ComicBook.MarkAsRead</c>/<c>MarkAsNotRead</c>, <c>ComicBrowserControl.cs</c>'s
/// <c>miMarkRead</c>/<c>miMarkUnread</c> context-menu commands), ported onto <see cref="Issue"/>'s
/// own fields rather than CE's <c>CurrentPage</c>/<c>OpenedCount</c>/<c>OpenedTime</c> trio -
/// <see cref="Issue.OpenCount"/>/<see cref="Issue.OpenedTime"/> are deliberately real "was this
/// actually opened" history in this codebase (see <see cref="Issue.OpenCount"/>'s own doc comment),
/// not a read-state proxy the way CE's <c>OpenedCount</c> is, so a manual mark/unmark leaves both
/// alone - only <see cref="Issue.LastPageRead"/> (the sole input to
/// <c>IssueMetadataExtensions.ReadPercentage</c>) changes.
/// </summary>
public static class IssueReadStateResolver
{
    /// <summary>
    /// Sets <see cref="Issue.LastPageRead"/> to the last valid page index, matching
    /// <c>ReaderScreenViewModel</c>'s own 0-indexed convention (not <see cref="Issue.PageCount"/>
    /// itself - <c>ReadPercentage</c> is <c>LastPageRead / PageCount</c>, so the *index* of the last
    /// page is what a real full read leaves behind). CE's own "1-page book" hack carries over
    /// unchanged: a single-page issue's last index (0) divided by its count (1) is 0%, not "read",
    /// so that one case uses an out-of-bounds 1 instead, matching CE's own documented workaround.
    /// No-ops when <see cref="Issue.PageCount"/> isn't known yet (unscanned/fileless) - there is no
    /// real "last page" to mark, and <c>ReadPercentage</c> already returns 0 unconditionally in that
    /// case regardless of what <see cref="Issue.LastPageRead"/> holds.
    /// </summary>
    public static void MarkAsRead(Issue issue)
    {
        if (issue.PageCount is not int pageCount || pageCount <= 0)
        {
            return;
        }

        issue.LastPageRead = pageCount == 1 ? 1 : pageCount - 1;
    }

    public static void MarkAsUnread(Issue issue)
    {
        issue.LastPageRead = 0;
    }
}
