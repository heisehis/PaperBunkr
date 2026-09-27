using System.Collections.Generic;

namespace Paperbunkr.App.Views;

/// <summary>
/// Scroll anchoring for continuous mode (docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md B4). Pages the
/// reader has not decoded yet are laid out at an estimated size; when a page's real size arrives, every page after it moves. If the
/// page that changed is <b>above</b> the first visible page, the content on screen shifts under the reader's eyes. The fix is the one
/// browsers use: move the scroll offset by the same amount, so what is on screen stays where it was. A change to the first visible
/// page or anything below it cannot move the visible top edge and needs no correction.
///
/// Pure and axis-agnostic: sizes are the on-screen main-axis sizes (already scaled to the viewport), the same numbers
/// <see cref="ReaderLayoutModel.ComputeContinuousLayout"/> stacks.
/// </summary>
public static class ScrollAnchor
{
    /// <summary>
    /// Index of the first page that intersects the viewport at <paramref name="scrollOffset"/> (the first whose end lies beyond the
    /// offset), or -1 when the offset is past every page.
    /// </summary>
    public static int FirstVisibleIndex(IReadOnlyList<double> mainSizes, double gap, double scrollOffset)
    {
        double cumulative = 0;
        for (int i = 0; i < mainSizes.Count; i++)
        {
            if (cumulative + mainSizes[i] > scrollOffset)
            {
                return i;
            }

            cumulative += mainSizes[i] + gap;
        }

        return -1;
    }

    /// <summary>
    /// How far to move <paramref name="scrollOffset"/> when the page at <paramref name="changedIndex"/> changes from its current
    /// size in <paramref name="mainSizesBefore"/> to <paramref name="newMainSize"/>. Zero when that page is the first visible page
    /// or later, or when nothing is visible.
    /// </summary>
    public static double OffsetDelta(IReadOnlyList<double> mainSizesBefore, double gap, double scrollOffset, int changedIndex, double newMainSize)
    {
        if (changedIndex < 0 || changedIndex >= mainSizesBefore.Count)
        {
            return 0;
        }

        int firstVisible = FirstVisibleIndex(mainSizesBefore, gap, scrollOffset);
        if (firstVisible < 0 || changedIndex >= firstVisible)
        {
            return 0;
        }

        return newMainSize - mainSizesBefore[changedIndex];
    }
}
