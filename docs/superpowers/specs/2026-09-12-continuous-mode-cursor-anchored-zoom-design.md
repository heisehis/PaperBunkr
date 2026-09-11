# Continuous-mode cursor-anchored zoom — design

**Status:** approved.
**Date:** 2026-09-12.
**Parent:** `docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md`
§5, which specified this ("reuses `ZoomPanMath` unchanged — same anchor-to-cursor behavior") but the
implementation shipped without it, leaving a code comment admitting the gap ("no cursor-anchor math
for continuous mode this pass"). Never tracked in any later backlog batch — found by direct user
report, not by re-reading old specs.

## 1. Problem

Paged mode's Ctrl+wheel zoom keeps the point under the cursor fixed on screen as zoom changes
(`ZoomPanMath.PanToKeepPointFixed`). Continuous/webtoon mode's Ctrl+wheel *and* pinch zoom don't —
both just scale `ZoomLevel` symmetrically, so the content visibly drifts out from under the
cursor/pinch center on every zoom step. Verified directly in `PageCanvas.OnPointerWheelChanged`
(line ~2187) and `OnPinch` (line ~2343): neither calls any anchor-computing function for continuous
mode, both just assign `ZoomLevel` and separately clamp scroll/pan.

## 2. Why this needs new math, not a reused call

`ZoomPanMath.PanToKeepPointFixed` operates on one image with two pan values (`PanOffsetX/Y`).
Continuous mode has no single image — `ScrollOffset` (main-axis) is *zoom-dependent stack-space*:
`ReaderLayoutModel.ComputeContinuousLayout` scales every page's main-axis size by the same
`crossAxisSize/nativeCross` factor derived from the current zoom, so the same `ScrollOffset` value
means a different logical position in the document at a different zoom. The cross-axis pan
(`PanOffsetX` in vertical mode) *does* behave like a single wide virtual page, exactly like paged
mode's own pan — that part reuses `PanToKeepPointFixed`'s formula shape directly.

## 3. Mechanism

New pure function, `ReaderLayoutModel.ComputeContinuousZoomAnchor` — same shape as
`PanToKeepPointFixed` (current state + anchor point + target zoom → new state), returning
`(double ScrollOffset, double CrossAxisPanOffset)`:

1. **Find the anchor page + fraction** (current zoom): run `ComputeContinuousLayout` at the
   *current* zoom/scroll/pan, then find the page whose `Rect` contains the anchor point's main-axis
   coordinate — or, if the anchor is over a gap or off either end of the stack, the *nearest* page
   (clamped to the first/last page's own edge — decision: "clamp to nearest page's edge," matching
   `PanToKeepPointFixed`'s own `Math.Clamp(..., 0, 1)` treatment of a cursor near an image's edge,
   not a separate no-anchor fallback). Fraction = `(anchorMain − pageRect.Top) / pageRect.Height`
   (or `.Left`/`.Width` for horizontal axis), clamped `[0, 1]`.
2. **Recompute that page's stack offset and main-axis size at the target zoom** via the already-
   existing `ComputeStackOffsetOfPage` (unmirrored stack-space, by design — same as `ScrollOffset`
   itself) plus the same per-page scale formula `ComputeContinuousLayout` uses.
3. **Solve for the new `ScrollOffset`** such that the same page+fraction lands back at the same
   viewport coordinate — inverting `ComputeContinuousLayout`'s own placement formula
   (`mainPosition = stackOffset − scrollOffset`, mirrored via `viewportMainSize − mainPosition −
   mainSize` when `reverseMainAxis` is set, so the RTL horizontal-continuous case is handled by
   solving through the same mirror, not a special case).
4. **Cross-axis pan**: identical derivation to `PanToKeepPointFixed`'s own math, with
   `crossAxisSize` (viewport cross size × zoom) standing in for `imagePixelSize × baseScale` — the
   cross-axis genuinely is "one wide virtual page," so this is the same formula, not a new one.

Returns unclamped values, same as `PanToKeepPointFixed` returning clamped-*by-calling-`ClampPan`*
— but here the caller (`PageCanvas`) already owns `ClampScrollOffset`/`ClampContinuousCrossAxisPan`
(private, instance-level, not visible to the static `ReaderLayoutModel` layer), so `PageCanvas` runs
the result through those after, exactly like every other `ScrollOffset`/`PanOffsetX/Y` writer in
that file already does.

## 4. Call sites (both approved for this pass — Q1)

- **`OnPointerWheelChanged`'s Ctrl+wheel branch**: anchor point = `e.GetPosition(this)`, same as
  paged mode's own branch right below it.
- **`OnPinch`**: anchor point = `e.ScaleOrigin` (the *current* frame's origin, not gesture-start).
  This **replaces** the existing separate origin-delta scroll/pan adjustment for continuous mode,
  not adds to it: solving "keep this content point under wherever the origin currently is" already
  produces the correct pan shift when zoom is ~unchanged (a two-finger drag with little pinch), so
  the old delta-based branch becomes dead code once this lands, and having two mechanisms compute
  overlapping pan values would double-count movement.
- **Not touched**: the toolbar zoom slider and any future ±/preset buttons — matching paged mode's
  own established precedent (`SetZoom100`/`ZoomIn`/etc. just assign `ZoomLevel`, no anchor math at
  all), not a new inconsistency introduced here.

## 5. Testing

- Pure-function tests in `ReaderLayoutModelTests.cs` (mirrors this file's own existing shape): a
  page + fraction under a fixed viewport point survives a zoom change (recomputed layout at the new
  `ScrollOffset` places the same page's same fraction back at the same viewport coordinate, within
  floating-point tolerance) — across a plain case, a cursor near a page boundary, a cursor over a
  gap (`mainAxisGap > 0`), a cursor above the first/below the last page, and `reverseMainAxis` on.
  Cross-axis pan gets the same "fixed point survives" check.
- `PageCanvas` wiring itself is on-screen-verification-only, same as every other gesture change in
  this project (no computer-use available) — flagged to the user once implemented: Ctrl+wheel zoom
  in webtoon/continuous mode keeps the point under the cursor visually still; pinch-zoom keeps the
  point under the pinch center visually still.
