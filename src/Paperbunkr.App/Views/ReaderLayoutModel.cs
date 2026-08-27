using System;
using System.Collections.Generic;
using Avalonia;

namespace Paperbunkr.App.Views;

/// <summary>
/// The layout-model layer (docs/onboarding.md §8's layout-model/render-layer split, docs/
/// superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §2) -
/// pure geometry, sibling to <see cref="ZoomPanMath"/>, deliberately agnostic to how its output
/// gets decoded/drawn. Given a reading mode's shape (one page, or a scroll axis + page sizes) and a
/// position, computes which pages are visible/near-visible and each one's target rect. Adding a
/// future reading mode is a change here, not to whatever renders the result.
/// </summary>
public static class ReaderLayoutModel
{
    /// <summary>The scroll axis for continuous mode - <see cref="Data.Entities.ReadingMode.VerticalContinuous"/>/<see cref="Data.Entities.ReadingMode.HorizontalContinuous"/>.</summary>
    public enum Axis
    {
        Vertical,
        Horizontal
    }

    /// <summary><paramref name="Index"/>'s target <paramref name="Rect"/> is in viewport space (0,0 = top-left of the visible canvas), not stack space - already accounts for the current scroll offset.</summary>
    public readonly record struct LayoutPage(int Index, Rect Rect);

    /// <summary>Paged mode's layout: trivially one page, filling the whole viewport - matches <see cref="ReaderPageVisualHandler"/>'s existing single-page behavior, expressed through this shared shape for interface parity with <see cref="ComputeContinuousLayout"/> (spec §2).</summary>
    public static IReadOnlyList<LayoutPage> ComputeSinglePageLayout(int pageIndex, Size viewportSize)
        => new[] { new LayoutPage(pageIndex, new Rect(viewportSize)) };

    /// <summary>
    /// Continuous mode's layout (spec §2/§5): every page fit to the viewport's cross-axis size
    /// (width for <see cref="Axis.Vertical"/>, height for <see cref="Axis.Horizontal"/>) - the only
    /// coherent base scale for a stacked flow, matching spec §5's "no fit-mode picker in continuous
    /// mode." Each page's main-axis size (the scroll direction) is derived from its own aspect
    /// ratio, not uniform - a tall page takes more scroll-axis space than a short one. Returns every
    /// page whose rect intersects the viewport, expanded by <paramref name="virtualizationRadius"/>
    /// pages on each side (matching <see cref="Services.PageDecodeService"/>'s own ±2 window, §3) -
    /// the direct input to <see cref="Services.PageDecodeService.SetVirtualizationWindow"/>.
    ///
    /// <paramref name="zoom"/>/<paramref name="crossAxisPanOffset"/> back spec §5's "zoom is free
    /// and unclamped upward... layered on top" - zoom scales the cross-axis size every page fits to
    /// (so 2x zoom makes each page twice as wide, in vertical mode), <paramref name="crossAxisPanOffset"/>
    /// shifts the whole (possibly now-overflowing) stack sideways within the viewport, same
    /// centered-by-default shape as paged mode's pan.
    ///
    /// <paramref name="mainAxisGap"/> (user direction, not in the original spec - <see cref="Data.Entities.ReadingMode.Webtoon"/>
    /// merges pages edge-to-edge with 0 gap, <see cref="Data.Entities.ReadingMode.VerticalContinuous"/>/
    /// <see cref="Data.Entities.ReadingMode.HorizontalContinuous"/> insert a visible gap between
    /// pages) is added between each page's main-axis span, including in the "does this intersect the
    /// viewport" test, so a gap never accidentally counts as page content.
    ///
    /// <paramref name="reverseMainAxis"/> (user direction) supports <see cref="Data.Entities.ReadingMode.HorizontalContinuousRightToLeft"/> -
    /// page 0 starts at the viewport's trailing edge and the stack grows toward negative X instead
    /// of positive, mirroring every rect's main-axis position rather than re-deriving the cumulative
    /// math for a second direction.
    ///
    /// All four default to their original spec §2 values, so every pre-existing caller is unaffected.
    /// </summary>
    public static IReadOnlyList<LayoutPage> ComputeContinuousLayout(
        IReadOnlyList<Size> pageNativeSizes,
        double scrollOffset,
        Size viewportSize,
        Axis axis,
        int virtualizationRadius = 2,
        double zoom = 1.0,
        double crossAxisPanOffset = 0.0,
        double mainAxisGap = 0.0,
        bool reverseMainAxis = false)
    {
        int count = pageNativeSizes.Count;
        if (count == 0 || viewportSize.Width <= 0 || viewportSize.Height <= 0)
        {
            return Array.Empty<LayoutPage>();
        }

        double viewportCrossSize = axis == Axis.Vertical ? viewportSize.Width : viewportSize.Height;
        double crossAxisSize = viewportCrossSize * zoom;
        double crossAxisStart = ((viewportCrossSize - crossAxisSize) / 2) + crossAxisPanOffset;
        double viewportMainSize = axis == Axis.Vertical ? viewportSize.Height : viewportSize.Width;

        // Cumulative main-axis offset (stack space) for every page - needed up front since a page's
        // on-screen position depends on the summed size of every page before it. Always computed
        // left-to-right/top-to-bottom regardless of reverseMainAxis - the mirror happens only at the
        // final rect-placement step below, so this cumulative math stays single-direction.
        var mainSizes = new double[count];
        var stackOffsets = new double[count];
        double cumulative = 0;
        for (int i = 0; i < count; i++)
        {
            var native = pageNativeSizes[i];
            double nativeCross = axis == Axis.Vertical ? native.Width : native.Height;
            double nativeMain = axis == Axis.Vertical ? native.Height : native.Width;
            double scale = nativeCross > 0 ? crossAxisSize / nativeCross : 0;

            mainSizes[i] = nativeMain * scale;
            stackOffsets[i] = cumulative;
            cumulative += mainSizes[i] + mainAxisGap;
        }

        int firstVisible = -1;
        int lastVisible = -1;
        for (int i = 0; i < count; i++)
        {
            bool intersectsViewport = stackOffsets[i] + mainSizes[i] > scrollOffset && stackOffsets[i] < scrollOffset + viewportMainSize;
            if (!intersectsViewport)
            {
                continue;
            }

            if (firstVisible < 0)
            {
                firstVisible = i;
            }

            lastVisible = i;
        }

        if (firstVisible < 0)
        {
            // Scroll offset is past every page (or before page 0, or the stack is empty at this
            // viewport size) - nothing intersects, nothing to virtualize around.
            return Array.Empty<LayoutPage>();
        }

        int windowStart = Math.Max(0, firstVisible - virtualizationRadius);
        int windowEnd = Math.Min(count - 1, lastVisible + virtualizationRadius);

        var result = new List<LayoutPage>(windowEnd - windowStart + 1);
        for (int i = windowStart; i <= windowEnd; i++)
        {
            double mainPosition = stackOffsets[i] - scrollOffset;
            if (reverseMainAxis)
            {
                // Real bug, found via manual testing: mirroring around X=0 (the old
                // `-mainPosition - mainSizes[i]`) put page 0 entirely off-screen to the left (its
                // right edge landing at X=0 instead of at the viewport's own right edge) - a blank
                // screen for every scroll position, not just an off-by-a-bit placement. Mirroring
                // around the viewport's own main-axis size instead puts page 0's trailing edge flush
                // against the viewport's trailing edge, as RTL requires.
                mainPosition = viewportMainSize - mainPosition - mainSizes[i];
            }

            var rect = axis == Axis.Vertical
                ? new Rect(crossAxisStart, mainPosition, crossAxisSize, mainSizes[i])
                : new Rect(mainPosition, crossAxisStart, mainSizes[i], crossAxisSize);
            result.Add(new LayoutPage(i, rect));
        }

        return result;
    }

    /// <summary>
    /// Total main-axis size of the whole stack at the given zoom (including gaps, per
    /// <paramref name="mainAxisGap"/>) - lets a caller clamp scroll position to
    /// <c>[0, max(0, total - viewportMainSize)]</c> rather than letting it run away past the last
    /// page. Same per-page scale formula as <see cref="ComputeContinuousLayout"/>, kept separate
    /// rather than having that method also return a total, since most callers (the render path)
    /// don't need it every frame - only scroll-input clamping does.
    /// </summary>
    public static double ComputeTotalMainAxisSize(IReadOnlyList<Size> pageNativeSizes, Size viewportSize, Axis axis, double zoom = 1.0, double mainAxisGap = 0.0)
    {
        double viewportCrossSize = axis == Axis.Vertical ? viewportSize.Width : viewportSize.Height;
        double crossAxisSize = viewportCrossSize * zoom;

        double total = 0;
        foreach (var native in pageNativeSizes)
        {
            double nativeCross = axis == Axis.Vertical ? native.Width : native.Height;
            double nativeMain = axis == Axis.Vertical ? native.Height : native.Width;
            double scale = nativeCross > 0 ? crossAxisSize / nativeCross : 0;
            total += nativeMain * scale + mainAxisGap;
        }

        return Math.Max(0, total - mainAxisGap); // no trailing gap after the last page
    }

    /// <summary>
    /// "Current page" for continuous mode (spec §6) - whichever page's main-axis midpoint is
    /// nearest the viewport's center, recomputed off the same rects <see cref="ComputeContinuousLayout"/>
    /// already produced. Returns -1 for an empty list (defensive - a 0-page issue shouldn't reach
    /// here in practice).
    /// </summary>
    public static int NearestPageToViewportCenter(IReadOnlyList<LayoutPage> pages, Size viewportSize, Axis axis)
    {
        if (pages.Count == 0)
        {
            return -1;
        }

        double viewportCenter = axis == Axis.Vertical ? viewportSize.Height / 2 : viewportSize.Width / 2;

        int nearestIndex = -1;
        double nearestDistance = double.PositiveInfinity;
        foreach (var page in pages)
        {
            double pageCenter = axis == Axis.Vertical
                ? page.Rect.Y + (page.Rect.Height / 2)
                : page.Rect.X + (page.Rect.Width / 2);
            double distance = Math.Abs(pageCenter - viewportCenter);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = page.Index;
            }
        }

        return nearestIndex;
    }

    /// <summary>
    /// Main-axis stack offset where <paramref name="targetIndex"/> begins - the direct
    /// <c>ScrollOffset</c> target for "scroll this page into view" (spec §6's thumbnail-rail/
    /// bookmark jump behavior in continuous mode: "they instead scroll the target page's top edge
    /// into view"), using the same per-page scale formula as <see cref="ComputeContinuousLayout"/>/
    /// <see cref="ComputeTotalMainAxisSize"/>. Deliberately ignores <c>reverseMainAxis</c> - unlike
    /// a page's on-screen <see cref="LayoutPage.Rect"/>, <c>ScrollOffset</c> itself is always plain
    /// stack-space (see its doc comment on <see cref="ViewModels.ReaderScreenViewModel.ScrollOffset"/>);
    /// the RTL mirror only happens at <see cref="ComputeContinuousLayout"/>'s final rect-placement
    /// step, which this doesn't need to reproduce.
    /// </summary>
    public static double ComputeStackOffsetOfPage(IReadOnlyList<Size> pageNativeSizes, int targetIndex, Size viewportSize, Axis axis, double zoom = 1.0, double mainAxisGap = 0.0)
    {
        double viewportCrossSize = axis == Axis.Vertical ? viewportSize.Width : viewportSize.Height;
        double crossAxisSize = viewportCrossSize * zoom;

        double offset = 0;
        int count = Math.Min(targetIndex, pageNativeSizes.Count);
        for (int i = 0; i < count; i++)
        {
            var native = pageNativeSizes[i];
            double nativeCross = axis == Axis.Vertical ? native.Width : native.Height;
            double nativeMain = axis == Axis.Vertical ? native.Height : native.Width;
            double scale = nativeCross > 0 ? crossAxisSize / nativeCross : 0;
            offset += (nativeMain * scale) + mainAxisGap;
        }

        return offset;
    }

    /// <summary>
    /// Rubber-band damping for continuous mode's chapter-boundary overscroll bump
    /// (docs/superpowers/specs/2026-08-23-reader-chapter-transition-design.md) - diminishing-returns
    /// curve (same shape iOS-style overscroll uses) so the visual bump approaches but never exceeds
    /// <paramref name="maxBump"/> however far past the clamp <paramref name="pullDistance"/> grows,
    /// rather than tracking it 1:1 (which would let the content run away visually). Both parameters
    /// are expected non-negative; a non-positive <paramref name="maxBump"/> returns 0 rather than
    /// dividing by zero.
    /// </summary>
    public static double ComputeOverscrollBump(double pullDistance, double maxBump)
    {
        if (maxBump <= 0 || pullDistance <= 0)
        {
            return 0;
        }

        return maxBump * pullDistance / (pullDistance + maxBump);
    }
}
