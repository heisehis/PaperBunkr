# Motion system v2 — On-page (sub-project D) — Design

**Date:** 2026-10-20
**Status:** Implemented 2026-10-20. Build passes; new `OnPageMotionTests` (11) plus 346 targeted view/real-window/lint tests pass.
**User-verified on screen 2026-10-21**; avalonia-pro-max review checklist run the same day (see the reader spec's §5).

**Implementation deviations (deliberate):**
- Q19 the stagger tail (index ≥ 5) shares **one timer per list** at the last group's delay (100 ms), rather than "no delay" - so a long
  burst arms at most 6 timers; earlier groups keep a timer each.
- Q20 the adorner root is positioned by `AdornerLayer` itself (it overwrites the root's RenderTransform), so the bar lives inside a plain
  `Panel` adorner - found by the test, which would otherwise have shown the bar stuck at the strip's origin.
- Q21 tab content has no single host on these screens (each tab is an `IsVisible`-gated panel), so `Replace.On` sits on **each panel,
  bound to its own "shown" flag**, and fades only when it becomes true. Wired: Insights (4), Wanted (3), Reading list Path/Covers,
  Continuity Overview/Timeline/Map, Detail tabs (3), Manga Chapters. Preferences sections have no tab family - not wired.
- Q25 nine chevron pairs converted (Continuity timeline, Event overview ×3, Smart maintenance, Reader drawer ×2, Wanted ×2); the Wanted
  candidates chip keeps its deliberately inverted direction. Expander content fade via `Replace.On` not wired (no single host per
  expander) - the chevron turn is the feedback.
- Test harnesses: the 9 hand-stubbed view-test token sets now also pin every motion duration to zero - a real-window test left real
  durations in the shared app resources, so two Reading-list ring tests read a BrushTransition mid-flight (an order-dependent failure that
  the old 150 ms token would have produced too).
**Builds on:** Foundation/Overlays (roles, tokens) and Navigation (RouteHost keeps screen views, so "revisit" no longer rebuilds a view).

## 1. Audit (verified 2026-10-20)

| Finding | Where |
|---|---|
| Books, Home, Smart and the reading-list page set `PlayEntranceAnimation = true` and never pulse it, and they don't set `OneShot`. Their prepared containers replay the entrance (the recycle storm). Library alone is one-shot. Each card has a `DispatcherTimer`, and the stagger runs up to 480 ms. | `Controls/EntranceAnimation.cs`; the VMs' `PlayEntranceAnimation` |
| Tabs only crossfade the underline colour. Tab content swaps without a transition. | `Styles/Controls.axaml` `Button.tab` |
| The Spotlight accordion uses a hard-coded 450 ms `CubicEaseInOut` driven by a 16 ms UI-thread `DispatcherTimer`. It ignores the tokens, Reduced mode and `PB_MOTION_SCALE`. It does retarget correctly on rapid clicks. | `Controls/AccordionPanel.cs` |
| The donut sweeps around on every `Slices` change, the "spinning doughnut" the blueprint rules out. | `Views/Stats/CategoryDonut.cs` |
| Home's shelf cards scale the whole hit-tested tile. Library lifts only the artwork. | `Styles/Primitives.axaml` `posterTile.shelfCard` |
| About 30 expanders swap two chevron icons by `IsVisible`, so there is no rotation and the reveal is a cut. | Continuity, Event, Timeline, Detail views… |
| The cover fade-in already fades each cover once per session (CE `FadeInThumbnails`), but uses a linear curve and a legacy token. | `Views/AsyncCoverImage.cs` |

## 2. Decisions and design

**Q19: entrances.**
- `OneShot` becomes the default for every `EntranceAnimation`.
- Owners *pulse* the flag (false → true) only on first load and real result changes (search, filter, sort, view mode); never on scroll,
  recycling or a revisit.
- Stagger is 20 ms per group, at most 6 groups (index ≥ 5 starts with no delay), and the whole entrance finishes within 320 ms.
- Containers past the first group post their reveal instead of arming a timer, so at most 5 timers run per burst.
- Fade runs on `PbMotionReplace` and the 6 px travel on `PbMotionRevealMove`.
- Reduced: a 100 ms fade, no stagger, no travel. Off: none.

**Q20: tab indicator.**
- `Controls/TabIndicator.cs` installs app-wide (with `PopupMotion`). For every panel that holds `Button.tab` children, it draws one 2 px
  accent underline in the panel's adorner layer and slides it to the active tab's measured left edge and width.
- The movement is a `translate(x) scale(w,1)` transform on `PbMotionRevealMove` with `PbMotionMoveEase`.
- The first placement, a resize or a relayout snap into place; only a change of tab animates.
- The host panel gets class `tabIndicatorHost`, which hides the per-tab underline.
- *Deviation:* segmented groups (`segGroup`) are not given a sliding pill. An adorner draws above its control, so a solid pill would cover
  the labels. They keep their per-item colour transition.

**Q21: tab content.**
- `Controls/Replace.cs`: when the value bound to `controls:Replace.On` changes after the first value, the control's opacity animates 0 → 1
  over `PbMotionReplace`.
- It uses a keyframe opacity animation (keyframe opacity is safe; the known crash is keyframe *RenderTransform*), so it never touches the
  control's own `Transitions`.
- A newer change cancels the running one, and there is no outgoing animation.
- Applied to the tab content hosts of Insights, Detail tabs, Manga detail, Wanted, Preferences sections, the Reading list Path/Covers
  switch and Continuity Overview/Map/Timeline, wherever a single host element exists.

**Q22: Spotlight.**
- `AccordionPanel` takes its duration from the Ambient role and `PbMotionMoveEase`, and steps per frame through
  `TopLevel.RequestAnimationFrame` instead of a 16 ms timer.
- It keeps retargeting from the current widths.
- When Ambient may not move (Reduced, Performance), it jumps.

**Q23: charts.**
- The donut's sweep is removed.
- On a `Slices` change the donut replays the Q21 crossfade on itself; first appearance is the same fade.
- Totals and labels show their final values immediately.

**Q24: card hover.**
- Home's shelf cards lift their artwork (`Border.posterCover`: −3 px, 1.02, `PbMotionSpring` on `PbMotionHoverMove`) instead of scaling
  the tile, matching Library.
- Text cards (`clickCard`, `heroTile`) already change colour only.

**Q25: expanders.**
- A shared `fi|SymbolIcon.chevronToggle` style rotates a single `ChevronRight` 90° when it has class `open`, on
  `PbMotionAcknowledgeMove` with `PbMotionMoveEase`.
- Every icon pair (Right visible when collapsed, Down visible when expanded) becomes one `chevronToggle` bound through the Down icon's
  binding.
- Revealed content keeps its layout cut (height never animates) and fades in via `Replace.On` where the expander has a single content host.

**Also:** the cover fade-in moves to `PbMotionReplace` / `PbMotionEase`.

## 3. Testing

- `EntranceAnimation`: one-shot by default; the delay is capped at 5 groups; Reduced gives no delay.
- `TabIndicator`: placement maths (`IndicatorTransform`), and that the host gets its class and the indicator moves on an active change.
- `Replace`: the first value doesn't animate; a change starts the fade; a newer change cancels the older one.
- `AccordionPanel`: the duration comes from the policy.
- Lint, plus the Home, Insights, Library and Continuity view tests.
- On screen: Home and Books scroll without replays; tab underline slide; tab content crossfade; Spotlight at 5× speed; donut on a range
  switch; Home card hover; expander chevrons.
