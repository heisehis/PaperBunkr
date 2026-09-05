# Reader Chapter Transition (Auto-Advance Feedback + Manual Chapter Nav)

**Date:** 2026-08-23
**Status:** Approved, pending implementation plan
**Source:** User request, inspired by Komikku's (Tachiyomi-family) inline chapter-transition card and
OpenComic's boundary "bump then loading spinner" continuous-scroll behavior — both referenced live
during the brainstorm.

## Context

`AutoNavigateComics` (`AppSettings`, default on) already carries the reader across an issue
boundary — `ReaderScreenViewModel.NavigateToAdjacentIssue` (`ReaderScreenViewModel.cs:1716`) loads
the next/previous issue and lands on its first/last page — but only from paged mode's
`NextPage`/`PreviousPage` commands, and silently: no visual feedback that a new issue just loaded.
Continuous/webtoon scroll mode has **no boundary-crossing logic at all** today — confirmed by
grep, `NavigateToAdjacentIssue` has no call site outside `NextPage`/`PreviousPage` — scrolling to
the bottom of the last page in continuous mode simply stops.

`PageCanvas` (`Views/PageCanvas.cs`) renders continuous-scroll content via its own internal
Skia/Composition layer, not a plain Avalonia `ScrollViewer` — a transition element can't be spliced
into that scrollable content as if it were a real page. `ReaderScreen.axaml` already has the right
pattern for this instead: fixed-position sibling overlays over `PageCanvas` (the `HasError` message
box, the fullscreen status/scrubber overlays), shown/hidden via `IsVisible` bindings. This design
reuses that pattern rather than reworking `PageCanvas`'s internals.

Cover art per issue is already cached (`CoverImageCache`/`CoverThumbnailService`) — showing the
incoming issue's real cover costs nothing new. `EffectiveNumber()` (`IssueMetadataExtensions`)
supplies the chapter-number labels. `ScrollOffset` is an existing two-way-bound property between
`ReaderScreenViewModel` and `PageCanvas`, clamped internally by `PageCanvas.ClampScrollOffset` —
the boundary-detection hook for continuous mode's bump.

No CE precedent applies here — this is a deliberate new addition inspired by modern manga readers
(Tachiyomi/Mihon/Komikku/OpenComic), same category as the Reader's other already-shipped
Tachiyomi-inspired features (RTL nav, continuous scroll, fit modes).

## Scope

### `ChapterTransitionOverlay` — shared component, both reading modes

New Avalonia control (or inline `Border` template reused twice in `ReaderScreen.axaml`, whichever
keeps the XAML cleaner at implementation time), with three states:

- **Loading** — spinner only, shown while the adjacent issue's decoder is starting up.
- **Card** — the incoming issue's cover thumbnail (`CoverImageCache.Get`) + "Previous: #{from}" /
  "Current: #{to}" labels (`EffectiveNumber()`), direction-aware (forward: from = the issue just
  finished, to = the new one; backward: reversed).
- **Hidden** (default).

Driven by new `ReaderScreenViewModel` state: `ChapterTransitionState` (enum: `Hidden | Loading |
Card`), `ChapterTransitionFromLabel`/`ChapterTransitionToLabel` (`string?`),
`ChapterTransitionCoverImage` (`Bitmap?`). The overlay `Border` gets its own Avalonia
`Transitions` (opacity) for the fade in/out — independent of `PageCanvas`'s internal
`PageTransitionStyle` pipeline, which only governs real page-to-page turns.

### Paged mode

`NextPage`/`PreviousPage` (`ReaderScreenViewModel.cs:1657,1673`) already detect "at the last/first
real page, boundary crossing eligible" before calling `NavigateToAdjacentIssue`. On that same
condition (and only when `AutoNavigateComics` is on): set `ChapterTransitionState = Card`
immediately (paged mode has no real load delay worth a spinner state — the current issue's last
page is already fully decoded, and the adjacent issue's first-page decode is fast enough that a
spinner would just flicker), populate the labels/cover, hold for ~1.2s via a timer, then run the
existing `NavigateToAdjacentIssue` body and set `ChapterTransitionState = Hidden`.

### Continuous mode — scroll-boundary bump

New `PageCanvas` behavior: `ClampScrollOffset` already establishes a max (forward) / min (backward)
`ScrollOffset`. When further wheel/drag/touch input arrives while already clamped at that
boundary, `PageCanvas` accumulates an "overscroll pull" distance instead of silently discarding the
input, and visually rubber-bands the content by a damped fraction of that pull (bump feedback,
matching OpenComic's described behavior) rather than staying perfectly rigid. Once accumulated
pull crosses a threshold, `PageCanvas` raises a new `ChapterBoundaryOverscrollRequestedProperty`-style
routed command (mirroring how `FullscreenToggleCommand` is already exposed outward) with a
`forward: bool` parameter; the pull accumulator resets if the user scrolls back away from the
boundary before crossing the threshold. The threshold/damping math is extracted as a pure function
on `ReaderLayoutModel` (matching that class's existing role for `ComputeContinuousLayout`), so it's
unit-testable without a real rendered `PageCanvas`.

`ReaderScreenViewModel` handles the raised command: set `ChapterTransitionState = Loading`
immediately, run `NavigateToAdjacentIssue`'s issue-load asynchronously, then set
`ChapterTransitionState = Card` with the resolved labels/cover for ~1.2s, then `Hidden` — the
continuous surface has already reset onto the new issue's own `ScrollOffset`/decoder by the time
the card state shows, so "the card briefly overlays the new issue's first page" rather than a
blank gap. Symmetric backward (top-of-scroll overscroll pull).

### Explicit chapter navigation

New `PreviousChapterCommand`/`NextChapterCommand` on `ReaderScreenViewModel`, added to the bottom
overlay bar (`ReaderScreen.axaml`, flanking the existing Previous/Next Page buttons around line
412-424) in both reading modes. Calls the same `NavigateToAdjacentIssue`-equivalent jump
unconditionally — **not** gated by `AutoNavigateComics`, since pressing an explicit "next chapter"
button is deliberate user action, not the automatic behavior that preference controls. Shows the
same `ChapterTransitionOverlay` Card state (skipping the paged-mode 1.2s auto-hold — a manual
button press dismisses immediately once the load resolves, there's no "reading flow" to preserve
continuity for).

## Testing

- `ReaderScreenViewModelTests`: paged-mode forward/backward boundary crossing sets
  `ChapterTransitionState`/labels/cover correctly and clears after the hold; `AutoNavigateComics`
  off suppresses the overlay entirely (matches today's existing off-path test); chapter-button
  commands fire the jump regardless of `AutoNavigateComics`.
- `ReaderLayoutModelTests` (or a new file): overscroll-pull/damping/threshold pure function -
  under-threshold pull resets, over-threshold pull returns "crossed," backward direction symmetric.
- On-screen verification (per this project's standing practice): both reading modes, forward and
  backward, confirm the overlay states/timing/cover art render correctly and the chapter buttons
  work with `AutoNavigateComics` toggled off.

## Explicitly out of scope

A genuinely unified continuous-scroll surface spanning two issues' real decoded image data in one
unbroken scroll (would need `PageCanvas`'s continuous layout/decode pipeline reworked to juggle two
issues' decoders/page-lists at once) — the reused-single-issue-scoped-architecture approach above
is visually equivalent to the user and dramatically lower-risk; revisit only if it turns out to
feel wrong once built. Configurable transition-card content (chapter title, "up next" preview
beyond the immediate adjacent issue) or a Preferences toggle to disable the card itself — no user
request for either yet, ship the fixed behavior first per this project's established pattern.
