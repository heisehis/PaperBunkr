# Motion system v2 — Reader (sub-project E) — Design

**Date:** 2026-10-20/21
**Status:** Settled in three grilling rounds (Q26–Q38) and implemented. Build passes; `ReaderMotionTests` (18) plus about 145 reader,
continuous, Data and lint tests pass. The stale page-shadow test (`…TextureMode_SetsShowPageShadow…`, outdated by the 10-18
`ReaderPageFrameStyle` change) was fixed in house cleaning. **User-verified on screen 2026-10-21**; avalonia-pro-max review checklist run the same day (see the reader spec's §5).
**Builds on:** Foundation/Overlays (roles, `Reveal`), Navigation, On-page.

## 1. Audit (verified)

| Finding | Where |
|---|---|
| The Tools drawer and Info panel are gated by `IsVisible`, so they cut in and out | `Views/ReaderScreen.axaml` |
| The chapter cards' opacity transitions could never play, because the cards are also `IsVisible`-gated | the same |
| Chrome idle-hide: 4 s without pointer activity (already the case). Hidden chrome kept tab stops and focus | `ReaderScreenViewModel.OverlayAutoHideDelay` |
| The ambient letterbox's `BrushTransition` only interpolates solid colours; between two gradients it cut over halfway | the same |
| Continuous mode: wheel notches, page jumps and decode completion were all instant cuts. Only zoom glided | `PageCanvas` |
| **CE's page effects:** None, Fade, LeftRight, TopDown, Paging (`ComicDisplayControl.PageForward`: the new page sweeps over the old one with an inner-bow shade). CE has **no animated curl**; its curl is a static thumbnail dog-ear (`ThumbRenderer.DrawPageCurlOverlay`) | `_reference/ComicRackCE` |

## 2. Decisions and what was built

| Q | Decision | Built as |
|---|---|---|
| Q26 | The panels use the Reveal lifecycle: 20 px from their own edge, spring arrival, faster exit; focus returns to the opener | `controls:Reveal.IsOpen` + new `Reveal.ReturnFocusTo`; `Border.readerDrawer.fromLeft/.fromRight` |
| Q27 | **The user's rule:** hide only after 4 s without pointer activity | Unchanged timer. Hidden chrome leaves tab order, and focus on a hidden cluster moves to the page |
| Q28 | Chapter cards are a Reveal (fade in, settle from 0.98, faster exit) | `Border.chapterCard` with Reveal; three dead transitions removed |
| Q29/Q30 | **The user's call:** add the extras. New styles **Vertical** (CE TopDown), **Book** (CE Paging) and **Curl** (new), mirrored for right-to-left reading, forced off by Reduced/Off, saved per profile | `PageTransitionStyle` +3 (stored as strings, so no migration); geometry in `PageTransitionMath` (`BookOffset`, `CurlFold`, `ClipToSide`, `Reflect`); drawing in `ReaderPageVisualHandler.RenderPhysicalTransition` via the Skia lease; both pickers list the new styles |
| Q31 | The curl plays on a page turn only; it doesn't follow a drag | — |
| Q32 | Parallax: the ambient layer drifts up to 12 px against pan (paged) or scroll (continuous) | `Controls/AmbientBackdrop` (`ParallaxOffset`: factor 0.04, capped); fed from `ReaderScreen.ParallaxSourceFor` |
| Q33 | A real 360 ms crossfade, plus an optional slow drift (continuous Ambient: High quality only, paused on low battery) | A two-layer `AmbientBackdrop`; a 24 s sway driven through a `DriftPhase` double |
| Q34 | One toggle, "Ambient motion", off by default; the crossfade is always on | `AppSettings.ReaderAmbientMotion` + migration `AddReaderAmbientMotion` (model checked clean first); a Preferences > Reader row |
| Q35 | Continuous wheel glide | `PageCanvas.ScrollGlide.cs`: the scroll goal is chased with a 0.07 s time constant; outside scroll changes cancel it |
| Q36 | Page jumps glide (220–400 ms) within 3 screens; further jumps cut and fade in | `GlideOrJumpTo` / `JumpGlideSeconds`. The position restore when an issue opens stays instant (no animation in the first 0.5 s after the page count changes) |
| Q37 | A page decoded while on screen fades in, once | `ReaderPageVisualHandler`: gap → reveal tracking per page position, eased alpha over the Replace fade, the lease taken while a reveal runs |
| Q38 | Page-turn styles stay paged-only | — |

## 3. Things the tests caught

- **The slow drift crashed.** A keyframe animation of a `ScaleTransform` throws, because Avalonia's `TransformAnimator` only accepts a
  `Visual`. The fix animates a `DriftPhase` property on the backdrop and applies it to the scale.
- **Focus return was skipped under Motion Off**, because the synchronous collapse never ran it. Both paths now return focus.
- **Four reader test hosts stubbed unknown static resources by name**, so `PbMotionSpring` became a `TimeSpan` and threw. They now map
  `*Spring*` to the spring curve.

## 4. On-screen checks (the user, at `PB_MOTION_SCALE=5` then 1)

- **Panels:** Tools and Info open and close, and focus returns to the button that opened them.
- **Chrome:** it fades after 4 s idle; Tab can't reach it while hidden.
- **Chapter cards:** Loading, next-chapter and end cards appear and leave smoothly.
- **Page turns:** each of Vertical, Book and Curl, forward and back, in LTR and RTL, on a solo page and a spread. Curl is the visual call
  to judge (paper colour, shadow depth).
- **Ambient background:** the gradient crossfades between pages. With Ambient motion on: parallax while panning a zoomed page and scrolling
  continuous mode, and the slow sway at High quality.
- **Continuous mode:** wheel glide; Page Down and the dot strip glide; a long jump fades; a page that decodes on screen fades in.
- **Reduced and Off:** none of the travel above.

## 5. Review checklist (avalonia-pro-max/review-checklist, 2026-10-21, scoped to the Motion v2 surface)

| Area | Result |
|---|---|
| Tokens & theming | Pass: every duration/curve is a `PbMotion*` token (lint `TransitionTokensOnly`), colours via `Pb*` resources (`TabIndicator` binds `PbAccentBrush`). **Accepted:** the curl's paper and shadow colours are literal Skia colours in the page renderer. They draw over page art, not themed chrome, and the user approved them on screen. |
| Motion timing | Pass: press 90 ms, hover fade 120 ms, routes 220 ms, exits about 65 % of entrances, explicit easing everywhere (XAML lint plus every code-built transition). **Accepted:** the hover lift's move is 160 ms against the checklist's 150 ms, as set in spec A and approved on screen. |
| Layout animation | Pass with three documented exceptions (`motion: reflow`): nav rail, contextual sidebar, reader dot strip. |
| Reduced motion | Pass: Reduced/Off paths are unit-tested (`MotionPolicyTests`, `OverlayShellMotionTests`, `RouteHostTests`) and verified on screen. |
| Looping | Pass: the only loop (ambient drift) is opt-in, High quality only and battery-aware. |
| Accessibility | Pass: icon names (lint), the focus ring untouched, closing surfaces leave tab order and return focus, hidden chrome isn't focusable. **Fixed:** toasts lacked a live region and now announce politely. |
| Performance | Pass: opacity/transform only; kept views (RouteHost) remove the view rebuild and its leak; entrances are one-shot with at most 6 timers per burst; continuous reveal and glide are frame-driven. **Noted:** Book/Curl allocate a few Skia objects per frame during a turn only, disposed each frame. |
| Final polish | **Fixed:** CS0108 in `RouteHost` (helper renamed `LayerTransitions`); two unused `xmlns:easings` removed; three stale comments corrected. No new XAML warnings. |
| Platform parity | Windows only (the project's target); not applicable beyond that. |

## 6. Follow-up 2026-10-21: the curl on double-page spreads

Reported on screen: the curl folded a whole spread as one sheet from its outer corner. It now turns one leaf like a book
(`ReaderPageVisualHandler.RenderSpreadCurl`). The later page in reading order (the right page left-to-right, the left page right-to-left,
which is always the spread's secondary) lifts at its outer corner and folds over the spine. The next spread's second page is uncovered
beneath it; the leaf's back is the next spread's first page, which lands over the facing page as the turn finishes. Turning back plays
the same turn in reverse. A solo page on either side (a cover next to a spread) keeps the whole-sheet curl. New test:
`Curl_OnASpreadLeaf_EndsWithTheWholeLeafTurnedOverTheSpine`.

## 7. Curl polish and the book look for spreads (2026-10-21)

Grilling Q39–Q43, all recommendations accepted.

**Curl polish (no setting):**
- **Corner first:** the corner lifts before it sweeps (the sweep is eased in with `e^1.5`, and the lift leads).
- **Cast shadow:** the turning page throws a shadow on the uncovered page that widens and darkens with lift (`DrawCastShadow`).
- **Curvature:** a crease shade at the fold and a highlight band on the turned-over paper (`DrawFlapCurvature`), plus a fine light line
  along the flap's edges.
- **Spreads:** the lifted leaf darkens the spine on both pages (`DrawSpineShadow`).
- **Timing:** Curl and Book never run shorter than 420 ms (`PageCanvas.EffectiveTurnDuration`).

**Book look for spreads** (`AppSettings.ReaderBookLook`, on by default, migration `AddReaderBookLook` with a `true` default so existing
installs get it; overridable per reader profile; Preferences > Reader):
- **CE parity, "Realistic pages":** bell-shaped bows at the spine (deep) and at the outer edges (light), and a thin outline on each page.
- **Paperbunkr addition, page-stack edges:** 1–6 sheet edges outside each outer edge. The side already read grows as you read (left in
  left-to-right, right in right-to-left).
- **Where it shows:** spreads only, still, during every page-turn style, and on the still pages of a spread curl.

**Tests:** `PageStackEdges_FollowReadingProgress`, `PhysicalTurns_NeverRunShorterThan420ms`,
`BookLook_ShadesTheSpine_MoreThanThePageMiddle_AndDrawsStackEdgesOutside` (Skia render),
`AProfile_CanTurnTheBookLookOff`.

