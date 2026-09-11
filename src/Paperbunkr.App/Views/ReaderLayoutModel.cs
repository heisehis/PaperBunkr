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

    /// <summary>
    /// Continuous mode's counterpart to <see cref="ZoomPanMath.PanToKeepPointFixed"/> (docs/
    /// superpowers/specs/2026-09-12-continuous-mode-cursor-anchored-zoom-design.md) - keeps the
    /// content under <paramref name="anchorPoint"/> visually fixed as zoom changes from
    /// <paramref name="currentZoom"/> to <paramref name="targetZoom"/>. Needs its own math (not a
    /// reused call) because <c>ScrollOffset</c> is zoom-dependent stack-space - the same value means
    /// a different logical document position at a different zoom - unlike paged mode's single image.
    ///
    /// Finds the anchor's zoom-invariant *identity* at the current zoom, then re-places that same
    /// identity at the target zoom:
    /// <list type="bullet">
    /// <item>On a page: <c>(pageIndex, fraction)</c> - a fraction of that page's own content, since
    /// every page scales uniformly with zoom.</item>
    /// <item>In a gap, or past either end of the stack: <c>(referencePageIndex, pixelOffset)</c> -
    /// <see cref="ContinuousMainAxisGap"/> doesn't scale with zoom at all (a flat pixel constant, not
    /// a fraction of anything), so a raw signed pixel offset past the last page whose trailing edge
    /// is at or before the anchor (or past the stack's own start, <c>referencePageIndex = -1</c>, if
    /// the anchor is before page 0) survives a zoom change exactly as read - no scaling needed. This
    /// is a deliberate design choice for the past-either-end case (there's no real content there to
    /// scale), not something forced by any existing formula - see the design doc §3.
    /// </list>
    ///
    /// Returns unclamped values (design §3) - <see cref="ComputeContinuousLayout"/>-shaped callers
    /// already own their own clamping (<c>PageCanvas.ClampScrollOffset</c>/
    /// <c>ClampContinuousCrossAxisPan</c>), not visible to this static layer.
    /// </summary>
    public static (double ScrollOffset, double CrossAxisPanOffset) ComputeContinuousZoomAnchor(
        IReadOnlyList<Size> pageNativeSizes,
        double currentScrollOffset,
        double currentZoom,
        double currentCrossAxisPanOffset,
        Size viewportSize,
        Axis axis,
        Point anchorPoint,
        double targetZoom,
        double mainAxisGap = 0.0,
        bool reverseMainAxis = false)
    {
        var identity = FindContinuousZoomAnchorIdentity(pageNativeSizes, currentScrollOffset, currentZoom,
            currentCrossAxisPanOffset, viewportSize, axis, anchorPoint, mainAxisGap, reverseMainAxis);
        return ResolveContinuousZoomAnchor(identity, pageNativeSizes, viewportSize, axis, anchorPoint, targetZoom, mainAxisGap, reverseMainAxis);
    }

    /// <summary>
    /// The anchor's zoom-invariant identity (design §3, step 1) - one of two shapes:
    /// <list type="bullet">
    /// <item>On a page: <c>PageIndex</c> is the real page, <c>OnPage</c> is true, <c>MainOffset</c>
    /// is a fraction [0,1] of that page's own content.</item>
    /// <item>In a gap, or past either end of the stack: <c>OnPage</c> is false, <c>PageIndex</c> is
    /// the last page whose trailing edge is at or before the anchor (-1 if the anchor is before page
    /// 0), <c>MainOffset</c> is a raw signed pixel offset past that page's trailing edge (or past the
    /// stack's own start, when <c>PageIndex</c> is -1) - unscaled, since a gap/off-stack margin never
    /// changes size with zoom.</item>
    /// </list>
    /// <c>CrossFraction</c> is the anchor's fraction of the cross-axis "virtual wide page," same
    /// meaning in both cases. See <see cref="ComputeContinuousZoomAnchor"/>'s own remarks for the
    /// full derivation this splits into two independently reusable halves - <see cref="FindContinuousZoomAnchorIdentity"/>
    /// (step 1) and <see cref="ResolveContinuousZoomAnchor"/> (steps 2-4) - so a caller (pinch) can
    /// find this identity once and reuse it across many frames, each resolved against that frame's
    /// own live target point/zoom, rather than re-finding it every frame.
    /// </summary>
    public readonly record struct ContinuousZoomAnchorIdentity(int PageIndex, bool OnPage, double MainOffset, double CrossFraction);

    /// <summary>Step 1 of <see cref="ComputeContinuousZoomAnchor"/> - see <see cref="ContinuousZoomAnchorIdentity"/> for the shape.</summary>
    public static ContinuousZoomAnchorIdentity FindContinuousZoomAnchorIdentity(
        IReadOnlyList<Size> pageNativeSizes,
        double currentScrollOffset,
        double currentZoom,
        double currentCrossAxisPanOffset,
        Size viewportSize,
        Axis axis,
        Point anchorPoint,
        double mainAxisGap = 0.0,
        bool reverseMainAxis = false)
    {
        double viewportMainSize = axis == Axis.Vertical ? viewportSize.Height : viewportSize.Width;
        double anchorMain = axis == Axis.Vertical ? anchorPoint.Y : anchorPoint.X;

        // Every page's rect is placed via `mainPosition = stackOffset - scrollOffset`, then (if
        // reverseMainAxis) overwritten to `viewportMainSize - mainPosition - mainSize`. Substituting
        // a point at stack position s = stackOffset + f*mainSize into that second formula and
        // simplifying shows the per-page stackOffset/mainSize terms cancel completely - the mirror
        // reduces to one *global* transform, independent of which page s falls in:
        //   unmirrored: viewportPos(s) = s - scrollOffset
        //   mirrored:   viewportPos(s) = viewportMainSize + scrollOffset - s
        // ToStackSpace is that transform's inverse, applied at the *current* zoom/scroll - used both
        // to convert the anchor itself and each candidate page's own rect edges into one consistent,
        // direction-agnostic stack-space frame, so "which page/fraction" reasoning below never has
        // to separately track which physical rect edge is which in mirrored vs. unmirrored layout
        // (an earlier draft tried that directly off rect.Top and had the on-page fraction backwards
        // for reverseMainAxis - caught while re-deriving this, not left for a test to find).
        double ToStackSpace(double viewportMainCoord) => reverseMainAxis
            ? viewportMainSize + currentScrollOffset - viewportMainCoord
            : viewportMainCoord + currentScrollOffset;

        var currentLayout = ComputeContinuousLayout(pageNativeSizes, currentScrollOffset, viewportSize, axis,
            virtualizationRadius: pageNativeSizes.Count, zoom: currentZoom, crossAxisPanOffset: currentCrossAxisPanOffset,
            mainAxisGap: mainAxisGap, reverseMainAxis: reverseMainAxis);

        double s = ToStackSpace(anchorMain);

        int onPageIndex = -1;
        double onPageFraction = 0;
        int referencePageIndex = -1;
        double referenceStackEnd = 0; // stack-space end of referencePageIndex's own range (0 = the stack's own start, when referencePageIndex is -1)

        foreach (var page in currentLayout)
        {
            double top = axis == Axis.Vertical ? page.Rect.Top : page.Rect.Left;
            double bottom = axis == Axis.Vertical ? page.Rect.Bottom : page.Rect.Right;
            double stackA = ToStackSpace(top);
            double stackB = ToStackSpace(bottom);
            double stackStart = Math.Min(stackA, stackB);
            double stackEnd = Math.Max(stackA, stackB);

            if (stackEnd > stackStart && s >= stackStart && s <= stackEnd)
            {
                onPageIndex = page.Index;
                onPageFraction = Math.Clamp((s - stackStart) / (stackEnd - stackStart), 0, 1);
            }

            if (stackEnd <= s && (referencePageIndex < 0 || page.Index > referencePageIndex))
            {
                referencePageIndex = page.Index;
                referenceStackEnd = stackEnd;
            }
        }

        double crossFraction = ComputeCrossAxisFraction(viewportSize, axis, anchorPoint, currentZoom, currentCrossAxisPanOffset);

        if (onPageIndex >= 0)
        {
            return new ContinuousZoomAnchorIdentity(onPageIndex, OnPage: true, onPageFraction, crossFraction);
        }

        double pixelOffset = s - referenceStackEnd; // zoom-invariant by design (§3 of the design doc) - a gap/off-stack margin never scales
        return new ContinuousZoomAnchorIdentity(referencePageIndex, OnPage: false, pixelOffset, crossFraction);
    }

    /// <summary>Steps 2-4 of <see cref="ComputeContinuousZoomAnchor"/> - re-places a previously-found <see cref="ContinuousZoomAnchorIdentity"/> at <paramref name="targetZoom"/>, solving for the <c>ScrollOffset</c>/cross-axis pan that puts it back under <paramref name="targetViewportPoint"/> (which need not be the same point the identity was originally found at - see pinch's gesture-start-locked usage in <c>PageCanvas.OnPinch</c>, where the identity is found once but resolved every frame against the gesture's live, moving origin).</summary>
    public static (double ScrollOffset, double CrossAxisPanOffset) ResolveContinuousZoomAnchor(
        ContinuousZoomAnchorIdentity identity,
        IReadOnlyList<Size> pageNativeSizes,
        Size viewportSize,
        Axis axis,
        Point targetViewportPoint,
        double targetZoom,
        double mainAxisGap = 0.0,
        bool reverseMainAxis = false)
    {
        double viewportMainSize = axis == Axis.Vertical ? viewportSize.Height : viewportSize.Width;
        double targetMain = axis == Axis.Vertical ? targetViewportPoint.Y : targetViewportPoint.X;

        double newStackPos;
        if (identity.OnPage)
        {
            double newPageMainSize = PageMainSizeAtZoom(pageNativeSizes[identity.PageIndex], viewportSize, axis, targetZoom);
            newStackPos = ComputeStackOffsetOfPage(pageNativeSizes, identity.PageIndex, viewportSize, axis, targetZoom, mainAxisGap) + (identity.MainOffset * newPageMainSize);
        }
        else
        {
            double referenceStackPosNew = identity.PageIndex < 0
                ? 0
                : ComputeStackOffsetOfPage(pageNativeSizes, identity.PageIndex + 1, viewportSize, axis, targetZoom, mainAxisGap) - mainAxisGap;
            newStackPos = referenceStackPosNew + identity.MainOffset;
        }

        // Invert the same ToStackSpace transform FindContinuousZoomAnchorIdentity uses, now at the
        // target zoom's viewport-space (newScrollOffset is what's being solved for, so this can't
        // reuse that closure, which is built over a possibly different scroll offset).
        double newScrollOffset = reverseMainAxis
            ? targetMain - viewportMainSize + newStackPos
            : newStackPos - targetMain;

        // --- Cross axis: same "one wide virtual page" derivation PanToKeepPointFixed uses --------

        double viewportCrossSize = axis == Axis.Vertical ? viewportSize.Width : viewportSize.Height;
        double targetCross = axis == Axis.Vertical ? targetViewportPoint.X : targetViewportPoint.Y;
        double crossAxisSize = viewportCrossSize * targetZoom;
        double newCrossAxisPanOffset = targetCross - (identity.CrossFraction * crossAxisSize) - ((viewportCrossSize - crossAxisSize) / 2);

        return (newScrollOffset, newCrossAxisPanOffset);
    }

    /// <summary>The anchor's fraction of the cross-axis "one wide virtual page" - same derivation <see cref="ZoomPanMath.PanToKeepPointFixed"/> uses, factored out since both <see cref="FindContinuousZoomAnchorIdentity"/> (finding it) and <see cref="ResolveContinuousZoomAnchor"/> (re-placing it) need the fraction/inverse-fraction shape of this formula.</summary>
    private static double ComputeCrossAxisFraction(Size viewportSize, Axis axis, Point anchorPoint, double zoom, double crossAxisPanOffset)
    {
        double viewportCrossSize = axis == Axis.Vertical ? viewportSize.Width : viewportSize.Height;
        double anchorCross = axis == Axis.Vertical ? anchorPoint.X : anchorPoint.Y;
        double crossAxisSize = viewportCrossSize * zoom;
        double crossAxisStart = ((viewportCrossSize - crossAxisSize) / 2) + crossAxisPanOffset;
        return crossAxisSize > 0 ? Math.Clamp((anchorCross - crossAxisStart) / crossAxisSize, 0, 1) : 0.5;
    }

    /// <summary>One page's main-axis size at a given zoom - the same per-page scale formula <see cref="ComputeContinuousLayout"/>/<see cref="ComputeStackOffsetOfPage"/> use, factored out for <see cref="ComputeContinuousZoomAnchor"/>'s own on-page case.</summary>
    private static double PageMainSizeAtZoom(Size nativeSize, Size viewportSize, Axis axis, double zoom)
    {
        double viewportCrossSize = axis == Axis.Vertical ? viewportSize.Width : viewportSize.Height;
        double crossAxisSize = viewportCrossSize * zoom;
        double nativeCross = axis == Axis.Vertical ? nativeSize.Width : nativeSize.Height;
        double nativeMain = axis == Axis.Vertical ? nativeSize.Height : nativeSize.Width;
        double scale = nativeCross > 0 ? crossAxisSize / nativeCross : 0;
        return nativeMain * scale;
    }
}
