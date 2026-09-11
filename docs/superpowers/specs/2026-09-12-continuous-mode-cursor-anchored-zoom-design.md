# Continuous-mode cursor-anchored zoom — design

**Status:** approved, rev 2.
**Date:** 2026-09-12.
**Parent:** `docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md`
§5, which specified this ("reuses `ZoomPanMath` unchanged — same anchor-to-cursor behavior") but the
implementation shipped without it, leaving a code comment admitting the gap ("no cursor-anchor math
for continuous mode this pass"). Never tracked in any later backlog batch — found by direct user
report, not by re-reading old specs.

### Revision history

| Rev | Change |
|---|---|
| 1 | Initial design: clamp-to-nearest-page-edge for a gap/off-stack anchor; pinch re-derives the anchor page fresh every frame. |
| 2 | External review flagged two runtime edge cases. Investigated both against the actual code rather than accepting the framing as given: (1) the gap-clamp concern was real but for a different reason than "gaps scale differently" — `ContinuousMainAxisGap` doesn't scale with zoom *at all* (a flat pixel constant, confirmed in `ComputeContinuousLayout`'s own cumulative-offset math), which is exactly why a real, zoom-invariant gap-interpolated anchor is possible and clamping was throwing that away; §3 rewritten. Doesn't affect Webtoon mode specifically (gap is 0 there), but does for VerticalContinuous/HorizontalContinuous. (2) the "pinch jitter" framing didn't hold up — worked through the actual sensitivity and found the pre-existing delta-based code already tracks raw `e.ScaleOrigin` with the same gain as per-frame anchor-solving would, so switching approaches doesn't add noise amplification. The real risk is narrower: re-deriving "which page is under the origin" fresh every frame can flip between two adjacent pages for a sub-pixel origin change near a page boundary, a genuine discontinuity - a deadzone/low-pass filter on the raw origin wouldn't reliably prevent that (a large single movement can still land exactly on a boundary) and would make pinch feel laggy. Fixed instead by locking the anchor page+fraction once at gesture start (the gesture-start point is already captured, `_pinchStartOrigin`) and holding it for the gesture's duration - eliminates the flip by construction, no tunable magic number. §4's pinch call site rewritten. |

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

1. **Find the anchor's stack-space identity** (current zoom) — one of two shapes, not a single
   clamp-to-edge (rev 2):
   - **On a page**: run `ComputeContinuousLayout` at the *current* zoom/scroll/pan, find the page
     whose `Rect` contains the anchor point's main-axis coordinate. Identity = `(pageIndex,
     fraction)`, `fraction = (anchorMain − pageRect.Top) / pageRect.Height` (or `.Left`/`.Width` for
     horizontal axis) — a zoom-invariant fraction of that page's own content, since every page
     scales uniformly.
   - **In a gap, or off either end of the stack**: `ContinuousMainAxisGap` doesn't scale with zoom
     at all (a flat pixel constant, confirmed in `ComputeContinuousLayout`'s own cumulative-offset
     math — `cumulative += mainSizes[i] + mainAxisGap`, the gap term never multiplied by any
     zoom-derived scale). So this has its own well-defined, zoom-invariant identity too: let `A` be
     the last page whose trailing edge is at or before the anchor (`A = −1` if the anchor is before
     page 0's own top — there's no real page to reference there). Identity = `(A, pixelOffset)`,
     `pixelOffset = anchorMain − referenceViewportPos` where `referenceViewportPos` is page `A`'s own
     trailing edge in the current layout (`0` when `A = −1`, i.e. measured from the viewport's own
     origin) — taken as a raw signed pixel count, no fraction, no scaling, since neither a gap nor
     the empty margin past either end of the stack changes size with zoom. This single shape covers
     a real gap between two pages, before page 0, and after the last page alike — no separate
     clamping or edge-specific case, unlike rev 1's clamp-to-edge.
2. **Recompute the target zoom's stack position of that same identity**:
   - Page case: `ComputeStackOffsetOfPage(pageIndex, newZoom) + fraction × thatPage'sNewMainSize`.
   - Gap/off-stack case: `(A == −1 ? 0 : ComputeStackOffsetOfPage(A, newZoom) + thatPage'sNewMainSize
     at the new zoom) + pixelOffset` — page `A`'s own new trailing-edge stack position (or the
     stack's own origin, `0`, when there's no real `A`), plus the unscaled pixel offset read in step
     1. (Rev 2's first draft tried to derive this via `ComputeStackOffsetOfPage(A + 1, newZoom) −
     mainAxisGap` instead — algebraically equal to this for a real in-between-two-pages gap, but
     wrong for the `A = −1` case, since there's no real gap there to back out. Caught while writing
     this doc, not left for the implementation to discover.)
3. **Solve for the new `ScrollOffset`** such that this stack position lands back at the same
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
- **`OnPinch`**: the anchor's stack-space **identity** (page+fraction, or preceding-page+gap-offset
  per §3) is computed **once**, when the gesture starts (`_pinchActive` first goes true, alongside
  the already-existing `_pinchStartOrigin`/`_pinchStartZoom`/`_pinchStartScrollOffset` capture), from
  `_pinchStartOrigin` — not re-derived every frame from the live `e.ScaleOrigin` (rev 2: re-deriving
  per frame risked a visible jump if the origin sat near a page/gap boundary and a sub-pixel touch-
  sampling difference flipped which page/gap was "found" between two adjacent frames). Every
  subsequent frame of the same gesture reuses that locked identity, feeding the frame's *current*
  `e.ScaleOrigin` and `zoom` into steps 2-4 only — so the anchor still tracks the live origin
  position (the point stays under your fingers as they move), it just never re-decides *which*
  page/gap it's anchored to mid-gesture. This **replaces** the existing separate origin-delta
  scroll/pan adjustment for continuous mode, not adds to it: solving "keep this content point under
  wherever the origin currently is" already produces the correct pan shift when zoom is ~unchanged
  (a two-finger drag with little pinch), so the old delta-based branch becomes dead code once this
  lands, and having two mechanisms compute overlapping pan values would double-count movement.
- **Not touched**: the toolbar zoom slider and any future ±/preset buttons — matching paged mode's
  own established precedent (`SetZoom100`/`ZoomIn`/etc. just assign `ZoomLevel`, no anchor math at
  all), not a new inconsistency introduced here.

## 5. Testing

- Pure-function tests in `ReaderLayoutModelTests.cs` (mirrors this file's own existing shape): a
  page + fraction under a fixed viewport point survives a zoom change (recomputed layout at the new
  `ScrollOffset` places the same page's same fraction back at the same viewport coordinate, within
  floating-point tolerance) — across a plain on-a-page case, a cursor near a page boundary, a cursor
  genuinely *inside* a gap (`mainAxisGap > 0`, asserting the gap-offset survives exactly, not just
  clamped to the nearer edge), a cursor above the first/below the last page, and `reverseMainAxis`
  on. Cross-axis pan gets the same "fixed point survives" check.
- **Boundary-flip regression test (rev 2)**: two anchor points 1px apart straddling a page/gap
  boundary must resolve to two *visibly close* results (bounded difference, not a discontinuous
  jump) when found independently - proves the underlying identity-finding step itself doesn't have
  a sharp edge, which is what makes gesture-start-locking (rather than a runtime workaround) a
  sufficient fix for pinch.
- `PageCanvas` wiring itself is on-screen-verification-only, same as every other gesture change in
  this project (no computer-use available) — flagged to the user once implemented: Ctrl+wheel zoom
  in webtoon/continuous mode keeps the point under the cursor visually still; pinch-zoom keeps the
  point under the pinch center visually still, smoothly, with no jump when the pinch center happens
  to start near a page boundary or over a gap.
