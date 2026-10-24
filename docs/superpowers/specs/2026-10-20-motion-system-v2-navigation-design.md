# Motion system v2 — Navigation (sub-project C) — Design

**Date:** 2026-10-20
**Status:** Implemented 2026-10-20 (plan alongside). Build passes; RouteHost (14), route intent (8), cover-flight (24 incl. coordinator),
MainViewModel (83), lint, and ~120 real-window/keyboard/Library/Compare/Wanted tests pass. **User-verified on screen 2026-10-21**; avalonia-pro-max review checklist run the same day (see the reader spec's §5).

**Implementation deviations (deliberate):**
- §3.1 + new: a kept view also keeps its scroll, so a **DrillIn (or Reader) into a reused view scrolls it to the top** (`RouteHost.ScrollToStart`)
  - the old host's rebuild had been silently giving every drill-in a fresh page. Back and rail switches keep scroll.
- §3.5 Library: a wire-once guard (`_wiredViewModel`) instead of unsubscribing - ~15 anonymous handlers; with RouteHost the view is built
  once and its DataContext never flips, so the guard is enough.
- §3.4 the breadcrumb segments already faded in on each rebuild (shared `entranceReady` pair) - the audit line overstated it; they now have
  their own 6 px Replace-role entrance (`StackPanel.crumbs`), no travel under Reduced.
- §3.3 a resize cancels a flight via the overlay host's Bounds; `MainWindow` cancels on any WindowState change.
**Builds on:** [Foundation + Overlays](2026-10-20-motion-system-v2-foundation-overlays-design.md): roles, tokens, `MotionPolicy`, `Reveal`.
§1 of that spec already fixed lateral navigation (fade + 12 px settle, 220 ms; shell stays still) and the reader (its page-turn
styles stay user choices).

## 1. Audit (verified 2026-10-20)

| Finding | Location | Consequence |
|---|---|---|
| Two transition hosts: one for lateral (rail) screens and one for drill-downs. Each host's content is `null` while the other group is active | `Views/MainWindow.axaml:818,863`, `MainViewModel.ActiveScreenContent` / `ActiveDrillDownContent` | Moving between the groups is a cut: one host vanishes while the other fades in from an empty background |
| **Every return rebuilds the screen's view.** A probe test confirms that a `TransitioningContentControl` builds a new view after content goes A → null → A or A → B → A | the same | Back to Library rebuilds `LibraryScreen` (its heavy XAML and virtualized grid); scroll, focus and selection exist only through explicit restore code |
| **That rebuild leaks.** `LibraryScreen.OnDataContextChanged` subscribes to `ScrollToIndexRequested` and `ScrollToTopRequested` with no unsubscribe; its comment assumes one view for the app's lifetime | `Views/LibraryScreen.axaml.cs:288` | Each Library ↔ Detail round trip leaves the old view tree alive (held by the view model's event), and its handlers keep running |
| Back from Detail plays the lateral host's **full-width horizontal slide**, from `null`, in whatever direction the last rail switch left behind | `PbScreenSlide`, `IsTransitionReversed` | The heaviest possible animation, in an arbitrary direction, on the most common return |
| The cover flight runs with `CancellationToken.None`, polls every 16 ms for its destination, and never hides the real source or destination art | `Services/NavigationTransitionCoordinator.cs:62`, `SharedElementTransitionService.cs` | A second click can't cancel it, and two copies of the cover are visible for 280 ms |
| The breadcrumb bar pops in and out | `MainWindow.axaml:900` | An unanimated layout jump on every drill |

## 2. Decisions

| Q | Decision |
|---|---|
| Q14 | One view per screen, cached by its view model, kept between visits, for the lateral screens and Detail / Manga Detail / Book Detail / Metadata entity / Compare. The three readers (comic, EPUB, PDF) are rebuilt on each visit: they own decode pipelines and the WebView. |
| Q15 | One route host and one route transition, chosen by the kind of navigation (§3.2). The rail, title bar and status bar never move. |
| Q16 | Flight hardening: hide the real source and destination art during the flight (restored in `finally`); wait for the destination's layout instead of polling; a new navigation, a resize or a minimise cancels the flight. The three existing pairs only. |
| Q17 | Reader enter/exit is a Travel fade only (no scale, no travel). The cover flight to the first page stays when it is available. |
| Q18 | The breadcrumb bar enters by sliding 6 px and fading (Replace role). It collapses instantly. *Refinement made while specifying:* it occupies a real layout row, so a delayed collapse would make the incoming screen jump about 120 ms after the swap. |

## 3. Design

### 3.1 `RouteHost`: one host, cached views

`Controls/RouteHost.cs` is a new `Panel`-derived control that replaces both `TransitioningContentControl`s in `MainWindow.axaml`. It has:

- `Content` (the active screen's view model), bound to a new `MainViewModel.ActiveRouteContent` (the lateral or drill-down view model,
  whichever is active);
- `Intent` (`RouteIntent`), bound to `MainViewModel.RouteIntent`;
- views built from the host's own `DataTemplates`, the same nine plus eight templates as today.

**View caching.** Views are cached per view-model instance in a dictionary. This is safe because every screen view model is a permanent
singleton owned by `MainViewModel`. A view model implementing the new marker interface `ITransientScreen` (the three readers) gets a fresh
view on every visit, and its old view is dropped when it leaves.

Each view is wrapped in a `RouteLayer` (a plain `Border`). The transition animates the layer, never the screen's own root, so a screen's own
`RenderTransform`/`Opacity` styling is untouched (the spec's one-owner-per-property rule).

**Lifecycle.**
- **Entering:** the layer is added to the panel's children, so the view attaches and its `OnAttachedToVisualTree` focus reclaim runs, exactly
  as today.
- **Leaving:** the layer is removed once the transition has finished, so the view detaches and its `Detached` clean-ups run, as today. The
  view is only re-parented into the same panel later, never into two parents.
- **During a transition:** the outgoing layer takes no pointer input and is out of tab order.

**Supersession.** A new `Content` while a transition runs:
1. finishes the oldest outgoing layer at once (removes it);
2. treats the current incoming layer as the new outgoing one;
3. starts the new transition from the layers' *current* visual values. Transitions retarget, so nothing jumps.

Rapid rail clicks never queue a backlog.

**Off, or a zero Travel duration:** the swap is synchronous.

### 3.2 `RouteIntent` and the route transition

`RouteIntent` is `Neutral, Lateral, DrillIn, Back, Reader`. `MainViewModel` computes it in `OnCurrentScreenChanging`:

1. Old and new screens both in the rail → **Lateral**.
2. Otherwise, a pending drill kind from `RunDrill`, consumed once, so a stale kind can't leak into a later rail click: Push → **DrillIn**,
   Pop → **Back**.
3. Either side is a reader (`reader`, `bookReader`, `pdfReader`) and the intent is DrillIn or Back → **Reader**.
4. Anything else → **Neutral**.

Timing comes from the Travel role: Full is fade 220 / move 220 / exit 140; Reduced is fade 120 / move 0; Performance is Reduced; Off is
instant. The curves are `PbMotionEase` in and `PbMotionExitEase` out.

| Intent | Incoming layer | Outgoing layer |
|---|---|---|
| Lateral | opacity 0→1 (fade), `translateY(12px)` → none (move) | opacity →0 (exit) |
| DrillIn | opacity 0→1, `scale(0.985)` → none | opacity →0 (exit) |
| Back | opacity 0→1, at rest | opacity →0 and → `scale(0.985)` (exit) |
| Reader, Neutral | opacity 0→1 | opacity →0 (exit) |

The incoming and outgoing layers overlap: a crossfade, not out-then-in. The outgoing layer finishes first because its exit is shorter.

### 3.3 Cover flight hardening (`SharedElementTransitionService`, `NavigationTransitionCoordinator`)

- **Supersession.** The coordinator owns a `CancellationTokenSource`; each `RunAsync` cancels the previous one first (and calls
  `Cancel()`), so a newer navigation removes the older flight's clone at once.
- **No double cover.** At capture, the source element is held at opacity 0. Once the destination resolves, the destination element is held at
  opacity 0 too. Both are Animation-priority values whose disposal in `finally` restores whatever the element had, on success,
  cancellation or exception.
- **Wait for layout, not a poll.** The destination is resolved on the overlay host's `LayoutUpdated` (each pass re-checks the
  registration and its bounds), with the existing 250 ms budget as a timeout. If it doesn't resolve, the flight is skipped and the route
  transition alone plays.
- **Cancel on resize / minimise.** A change to the overlay host's bounds cancels the flight. `MainWindow` cancels it when `WindowState`
  changes.
- Flight timing and curve stay as in A: `PbMotionFlight` (280 ms) and `PbMotionMoveEase`; none under Reduced or Off.

### 3.4 Breadcrumb entrance

`Breadcrumb` plays an entrance when it becomes visible: it fades in and slides 6 px down on `PbMotionReplace` / `PbMotionEase` (class
`enter`, then `entered` a tick later, the same pattern as `PopupMotion`). Hiding stays immediate.

### 3.5 Compatibility

- `ActiveScreenContent`, `ActiveDrillDownContent`, `IsLateralScreen`, `IsTransitionReversed` and `DrillTransitionKind` stay on
  `MainViewModel`, because tests and other bindings read them; `ActiveRouteContent` and `RouteIntent` are added.
- The `PbScreenSlide` and `PbDrillTransition` resources and `MotionAwarePageTransition` are deleted, since nothing uses them after the
  host swap.
- **Library's subscriptions** in `OnDataContextChanged` become guarded (unsubscribe from the previous view model on a DataContext change).
  Caching already removes the repeat builds; this guard is the belt-and-braces fix for a view that is rebuilt anyway.

## 4. Testing

- **`RouteHost` (headless, `ScheduleAfter` seam as in A):**
  - a view is built once per cached view model across A → B → A;
  - an `ITransientScreen` view is rebuilt;
  - the outgoing layer is removed after its exit, and is non-hit-testable meanwhile;
  - a new `Content` mid-transition leaves at most two layers and never puts one view in two parents;
  - Off swaps synchronously;
  - the intent picks the start transforms (§3.2 table).
- **`MainViewModel.RouteIntent`:** rail → rail is Lateral; Library → Detail is DrillIn; Back is Back; Detail → Reader is Reader; and a
  stale drill kind does not leak into a following rail click.
- **Shared element:**
  - a cancelled flight restores the source and destination opacity and removes the clone;
  - a second `RunAsync` cancels the first;
  - the destination resolves on a layout pass.
- **Existing:** the `MainViewModel`, `CompareTests`, `WantedNavigationTests`, `NavigationTransitionCoordinatorTests` and real-window
  keyboard suites (focus after navigation is the risk).
- **On screen** (the user, at `PB_MOTION_SCALE=5`):
  - rail clicks: settle up, no slide;
  - rapid rail clicks: no backlog;
  - Library → Detail → Back: depth in and out, with **Library's scroll position and focused card intact without any restore**;
  - Detail → Reader and back;
  - the breadcrumb entrance;
  - Reduced and Off.

## 5. Out of scope

Card, tab and Spotlight choreography (D). Reader chrome (E). New flight pairs.
