using System;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// Page-skipping walk for the paged reader (docs/superpowers/specs/2026-09-21-comic-reader-page-
/// intelligence-design.md §2, pitch #6). CE parity: <c>ComicBookNavigator.SeekNextPage</c> walks in a
/// direction and only counts a page that matches its page filter. The caller passes the page its normal
/// step would have landed on; the walk moves on from there while pages are skippable. The page the reader
/// is on is never an input, so the reader cannot get stuck on it.
/// </summary>
public static class PageSkipStepper
{
    /// <summary>
    /// The first page at or beyond <paramref name="landing"/> (in <paramref name="direction"/>, +1 or -1) that
    /// is not skippable, or <see langword="null"/> when every page from there to the end (start, going back) is
    /// skippable - the caller treats that as the end (start) of the issue.
    /// </summary>
    public static int? Resolve(int landing, int direction, int pageCount, Func<int, bool> isSkippable)
    {
        if (direction is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Direction must be +1 or -1.");
        }

        for (int page = landing; page >= 0 && page < pageCount; page += direction)
        {
            if (!isSkippable(page))
            {
                return page;
            }
        }

        return null;
    }
}
