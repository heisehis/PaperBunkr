# Chrome & Content Motion Polish

**Status:** Design. Approved via grilling round in chat (Q1-Q16 answered 2026-09-07). Not yet
implemented, not yet approved for `writing-plans`.
**Sub-project 2 of 2** in the "full app chrome animations" effort. **Sub-project 1** ([2026-09-04
Navigation Transition System](2026-09-04-navigation-transition-system-design.md)) shipped
2026-09-05: drill-down push/pop transitions + shared-element cover flight. That spec's own
"Out of scope / deferred" section named this sub-project's six items in one line with no detail —
this doc is where they get designed for real.

**Extends:** [2026-08-24 Navigation Shell & Motion System](2026-08-24-navigation-shell-motion-system-design.md)
(motion tokens, nav rail width transition) and the shipped [2026-09-06 Feedback & Notification
System](2026-09-06-feedback-notification-system-design.md) (`PbToastView` content shape, which this
spec's toast item builds on top of without changing).

---

## Background

Motion tokens today ([App.axaml](../../../src/Paperbunkr.App/App.axaml)):
`PbMotionFast` (0.15s), `PbMotionSlow` (0.7s), `PbMotionStandard` (0.22s), `PbMotionLarge` (0.32s),
`PbMotionEase` (`CubicEaseOut`). All four durations are zeroed live by
`SkinService.ApplyReducedMotion` when Preferences → Appearance's Reduced Motion toggle is on
(`src/Paperbunkr.App/Services/SkinService.cs:128-131`).

`App.axaml`'s own token comment (line 124) has, since sub-project 1, already named
`PbMotionStandard`'s intended consumers as "drill-down screen cross-fade (push/pop), sidebar,
breadcrumb" — the sidebar and breadcrumb wiring was never actually implemented until now. This
spec closes that gap alongside five other confirmed-unanimated (or unaudited) chrome/content
surfaces:

| # | Surface | Today | File(s) |
|---|---|---|---|
| 1 | Library grid item entrance | No animation; virtualized containers recycle instantly | [LibraryScreen.axaml](../../../src/Paperbunkr.App/Views/LibraryScreen.axaml) (5 view modes) |
| 2 | `DetailHero` backdrop | In-flow `Border`, scrolls with the page, no scroll-linked motion | [DetailHero.axaml](../../../src/Paperbunkr.App/Views/DetailHero.axaml) |
| 3 | Contextual sidebar (236px panel) | Hard `IsVisible` toggle, no transition | [MainWindow.axaml:262-266](../../../src/Paperbunkr.App/Views/MainWindow.axaml) |
| 4a | Breadcrumb bar | Imperative clear+rebuild on every change, no animation | [Breadcrumb.axaml.cs:45-70](../../../src/Paperbunkr.App/Views/Breadcrumb.axaml.cs) |
| 4b | "Unsaved changes" modal | Centered dialog (not a banner), hard `IsVisible` toggle | [MainWindow.axaml:989-1008](../../../src/Paperbunkr.App/Views/MainWindow.axaml) |
| 5 | Toast entrance | Avalonia's stock `WindowNotificationManager` — animation entirely outside app control, not `PbMotion*`-wired | [MainWindow.axaml.cs:447-459](../../../src/Paperbunkr.App/Views/MainWindow.axaml.cs) |
| 6 | Activity drawer open/close | Animated, but with **hardcoded** `0:0:0.22`/`0:0:0.18` literals — Reduced Motion has zero effect on it | [ActivityDrawerView.axaml:60-75](../../../src/Paperbunkr.App/Views/ActivityDrawerView.axaml) |

The `avalonia-pro-max/motion` subskill's performance rule ("animate only `Opacity`,
`RenderTransform`, brushes, `BoxShadow` — never `Width`/`Height`/`Margin`") and its explicit
common-mistake callouts ("no reduced-motion path," "animating on every virtualization recycle")
shape several decisions below.

## Goals

1. Staggered entrance for Library grid items (v1: Library only).
2. Scroll-linked parallax on `DetailHero`'s backdrop.
3. Slide transition for the contextual sidebar.
4. Fade/slide for the breadcrumb bar; fade+scale for the discard-changes modal.
5. Replace the toast host with an app-owned, `PbMotion*`-wired, Reduced-Motion-respecting one.
6. Fix the Activity drawer's hardcoded durations; best-effort the same for its peek popover.

## Non-goals (v1)

- Entrance stagger for any grid other than Library (Books, Smart Lists, Reading Lists, Home
  carousels) — tracked as a v2 follow-up.
- Hero sticky/shrink header — parallax is a backdrop offset only, not a layout restructure.
- Cross-fading the main content area when the sidebar toggles.
- Per-segment breadcrumb enter/exit diffing (`Rebuild()`'s clear-and-readd stays as-is).
- Converting the discard-changes modal into an actual top-of-screen banner — no UX rationale
  surfaced for changing it from a blocking confirm dialog to a dismissible banner.
- Toast hover-to-pause-timer behavior changes.
- Activity Center per-row stagger and Active/History/Scheduled tab cross-fade.

---

## Architecture

Two new reusable primitives, matching the shape sub-project 1 used for `SharedElement` (attached
properties + a small runtime class):

### `EntranceAnimation` (attached properties)

`Paperbunkr.App.Controls.EntranceAnimation`: `Enabled` (bool), `Index` (int), set on a grid item's
container. On `AttachedToVisualTree`, if `Enabled` is true, schedules `Classes.entered = true`
after a `TimeSpan` computed from `Index * <per-item delay, implementation-tuned ~20-30ms>`. The
actual visual states are declared once as an ordinary style — `.entrance` (initial: `Opacity=0`,
`RenderTransform=translateY(8px)`, with `Transitions` bound to `PbMotionStandard`/`PbMotionEase`)
vs `.entrance.entered` (`Opacity=1`, `RenderTransform=none`) — so it's Reduced-Motion-safe for
free (the transition duration token already zeroes) and needs no per-instance animation wiring
beyond the two attached properties.

`Enabled` is driven per-screen by a one-shot ViewModel flag (see item 1), not a global switch —
this is what prevents virtualization's constant container recycling from replaying the entrance on
every scroll, the motion skill's explicit common-mistake warning.

### `AnimatedStackPanel`

A `Panel` (used inside the new toast host) that, on re-arrange, computes each surviving child's
position delta between its previous and new arranged rect (FLIP technique) and animates it away
via `RenderTransform` translate only — never `Margin`/`Height` — over `PbMotionStandard`. This is
what makes "remaining toasts slide to close the gap" possible without the layout-jank the motion
skill's performance rule warns against. Reduced Motion zeroes the same token, collapsing this to an
instant snap.

### Token wiring

`PbMotionStandard` (220ms) is used for: sidebar slide, breadcrumb transition, toast entrance/stack
reflow — matching `App.axaml:124`'s pre-existing (previously unimplemented) documented intent.
`PbMotionFast` (150ms) continues to mean "micro" (press/hover-scale territory, nav rail width) and
is what the Activity drawer's fix targets for its faster opacity leg. No new motion tokens needed.

---

## Per-item design

### 1. Staggered Library grid entrance

`EntranceAnimation.Index` is set to each container's realization index (via the `ItemContainerTheme`
or a container-prepared behavior, per view mode) across all 5 Library view modes (Poster/Panorama/
List/Details/Tiles), both issue and series template variants.

`EntranceAnimation.Enabled` binds to a new `LibraryScreenViewModel.PlayEntranceAnimation` bool:
- Set `true` on: fresh navigation into the Library screen, any filter/sort/group change, and any
  view-mode switch.
- Set back to a "consumed" state once played (one-shot per trigger — not per scroll/recycle).

No artificial cap on how many items animate — virtualization already bounds realization to
whatever's on-screen, so the stagger's total visible duration scales with viewport size, not
library size (a 2,000-item library and a 20-item one animate identically for what's visible).

### 2. `DetailHero` parallax

`DetailHero`'s backdrop `Image` (inside `DetailScreen.axaml`'s hosting `ScrollViewer`) gets a
`ScrollViewer.ScrollChanged` handler (or attached behavior) that applies a `translateY` to the
backdrop at roughly 30-35% of the scroll offset (classic parallax fraction, tuned at
implementation time) — the backdrop appears to scroll slower than the rest of the page. No scrim
fade, no height/scale change to the hero itself.

Reduced Motion: the offset is pinned to 0 (backdrop scrolls 1:1 with the page, i.e. no parallax)
rather than merely being instant — this is continuous scroll-linked depth motion, exactly what
vestibular-disorder accessibility guidance targets, so "reduced" here means "off," not "faster."

Applies automatically to every `DetailHero` consumer (comic detail, manga detail, book detail,
Home's spotlight) since they all share the one control — no per-consumer wiring needed.

### 3. Contextual sidebar slide

The existing 236px `Border` (`MainWindow.axaml:262-266`) gains a `Width` transition — same
mechanism as the nav rail's existing 64↔200 expand/collapse — but using `PbMotionStandard`
(220ms) rather than the rail's `PbMotionFast`, per `App.axaml:124`'s documented intent for this
specific surface. `ShowContextualSidebar` continues to drive it; no other binding changes. Main
content area is untouched (no cross-fade).

### 4. Breadcrumb + discard-changes modal

**Breadcrumb:** rather than diffing individual segments, the whole `Breadcrumb` bar gets a
fade+slide `Transitions` pair (`Opacity`, `RenderTransform` translateY) using `PbMotionStandard`,
triggered as a unit on every `Rebuild()` call (`Breadcrumb.axaml.cs:45-70` stays a clear-and-readd;
only the container-level transition is new).

**Discard-changes modal:** the existing centered dialog (`MainWindow.axaml:989-1008`) gains an
entrance/exit fade+scale, reusing the same pattern already established for `FloatingPanel`-style
overlays elsewhere in the app (`PbMotionFast`/`PbMotionEase`, per the 2026-08-24 design-language
spec). The structurally identical tour-offer modal (`IsTourOfferOpen`, lines 1028-1048) gets the
same treatment for consistency, since it already shares the `discardConfirm` button classes.

### 5. Toast host replacement

New `PbToastHost` (a small custom `Panel`, built on `AnimatedStackPanel`) replaces the
`WindowNotificationManager` field in `MainWindow.axaml.cs:447-459`. Same bottom-right position,
same max-3-visible cap, same `PbToastView` content (unchanged — this spec only touches the hosting/
animation layer), same `_persistentToasts` dictionary and close-by-reference mechanism for
actionable/persistent toasts (e.g. update-ready).

- **Entrance:** `translateY` (from below) + fade in, `PbMotionStandard`/`PbMotionEase`.
- **Exit:** reverse, at ~70% of the entrance duration (motion-skill convention for exits).
- **Stack reflow:** handled by `AnimatedStackPanel` — when a toast is removed, survivors slide
  into the gap via the FLIP-technique `RenderTransform` described above, not an instant snap.
- Existing expiration/auto-dismiss timing is preserved unchanged; this spec doesn't touch when a
  toast closes, only how its entrance/exit/reflow look.

### 6. Activity Center drawer audit

**Confirmed bug fix:** `ActivityDrawerView.axaml:60-75`'s hardcoded `Duration="0:0:0.22"` and
`Duration="0:0:0.18"` become `{DynamicResource PbMotionStandard}` and
`{DynamicResource PbMotionFast}` respectively — the only functional change needed for Reduced
Motion to finally reach this drawer.

**Peek popover (best-effort):** `StatusBar.axaml:100-119`'s `Popup` wraps its content `Border` with
no `Transitions` today, riding Avalonia's own default `Popup` animation. Attempt the same
token-wired `Transitions` approach on the inner `Border`. Flagged here as an **implementation-time
verification**, not a guaranteed outcome: Avalonia's `Popup` unmounts its content when `IsOpen`
flips to `false` (rather than toggling a `Classes`/`IsVisible` on an always-mounted element the way
the drawer's own `Border` does), so a plain exit `Transitions` setter may never get a chance to
play before the content is gone. If it doesn't work cleanly, that's a deferred follow-up (tracked
in the plan, not silently dropped), not a blocker for the rest of this spec.

---

## Testing

- **`EntranceAnimation`:** pure logic (index → delay calculation) unit-tested with no visual tree.
  `LibraryScreenViewModelTests` — `PlayEntranceAnimation` flips true on navigation-in, on each of
  filter/sort/group/view-mode change, and resets to a consumed state after being read once.
- **`AnimatedStackPanel`:** headless Avalonia test — arrange with N children, remove one, assert
  survivors' `RenderTransform` animates from the correct delta to identity; Reduced Motion → delta
  resolves instantly (token zeroed).
- **Parallax:** manual/on-screen only (scroll-linked visual feel isn't meaningfully unit-testable);
  confirm Reduced Motion pins the offset to zero via a targeted `DetailHero`/`DetailScreen` test
  asserting the computed translate is 0 when the setting is on, for any non-zero scroll offset.
- **Sidebar/breadcrumb:** extend the existing `ReducedMotion_Change_PersistsToAppSettings_AndAppliesLive`
  pattern (`PreferencesScreenViewModelTests.cs`) — no new resource key, just new consumers of
  `PbMotionStandard`, so the existing resource-level assertion already covers the contract.
- **Toast host:** `PbToastHost`/`AnimatedStackPanel` covered by the `AnimatedStackPanel` tests
  above; existing toast-content and `_persistentToasts` close-by-reference behavior is unchanged,
  so existing call-site tests need no changes, only a manual pass confirming the new host renders/
  positions/dismisses identically to the old one.
- **Activity drawer:** manual — toggle Reduced Motion, confirm the drawer's open/close snaps
  instantly (it doesn't today); confirm the peek popover's outcome one way or the other and record
  it in the plan.
- **Manual/on-screen** (standing limitation — no unattended GUI automation in this environment):
  Library entrance stagger across all 5 view modes and each of the four triggers (nav-in, filter,
  sort/group, view-mode switch); hero parallax feel on comic/manga/book detail and Home; sidebar
  slide on each screen that shows it; breadcrumb transition on a multi-level drill-down; discard
  modal and tour-offer modal fade+scale; toast entrance/exit/reflow with 1, 2, and 3+ toasts
  stacked; Activity drawer + peek popover under Reduced Motion on/off.

## Out of scope / deferred

- v2: entrance stagger for Books, Smart Lists, Reading Lists, Home carousel grids.
- Hero sticky/shrink header.
- Sidebar-toggle content cross-fade.
- Per-segment breadcrumb diffing.
- Literal top-of-screen discard banner (UX change from the current blocking modal).
- Toast hover-to-pause-timer.
- Activity Center per-row stagger, tab cross-fade.
- Peek popover animation, if the implementation-time verification above finds it infeasible with a
  stock `Popup`.

## Open implementation-time calls (not design decisions)

- Exact per-item stagger delay (~20-30ms suggested) and whether it's a single app-wide constant or
  configurable — pick whichever reads best on screen.
- Exact parallax fraction (~30-35% suggested).
- Whether `EntranceAnimation`'s per-container index comes from the `ItemContainerTheme`'s own
  generated index, a converter, or a small code-behind hook — whichever fits each of the 5 view
  modes' existing container-generation shape most cleanly.
- Whether the peek popover's `Transitions` attempt (item 6) actually plays on close given `Popup`'s
  unmount behavior — resolve during implementation, document the outcome either way.
