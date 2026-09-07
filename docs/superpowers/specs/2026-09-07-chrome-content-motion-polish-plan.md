# Chrome & Content Motion Polish — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-07-chrome-content-motion-polish-design.md*

## Working-tree note

The shared working tree currently has substantial uncommitted work from other sessions (ToggleSwitch
adoption, Connections dialog redesign, Library Health, Feedback & Notification System follow-ups),
including on several files this plan touches (`App.axaml`, `MainWindow.axaml`, `MainWindow.axaml.cs`,
`LibraryScreen.axaml`, `ActivityDrawerView.axaml`, `StatusBar.axaml`, `LibraryScreenViewModel.cs`,
`MainViewModel.cs`). Unlike the 2026-09-04 navigation-transition plan, there's no evidence of a
*concurrently running* session right now, and the dirty tree already builds clean — proceeding in
place rather than forking a worktree, to avoid a separate reconciliation burden against a moving
uncommitted base. Re-check `git status` before each step below in case that assumption stops holding.

## Subskills to load during implementation (per CLAUDE.md)

- `avalonia/avalonia-pro-max/motion/SKILL.md` — already read this session; re-check for `Transitions`
  vs `Animation` shape, easing table, "no reduced-motion path" / "animating on every recycle" mistakes.
- `avalonia/avalonia-custom-controls/SKILL.md` — `AnimatedStackPanel`'s custom `Panel` override.
- `avalonia/avalonia-property-system/SKILL.md` — `EntranceAnimation`'s attached-property registration.
- `avalonia/avalonia-graphics-animation/SKILL.md` — `RenderTransform`/`TransformOperations` animation
  for the parallax offset and `AnimatedStackPanel`'s FLIP reflow.
- `avalonia/avalonia-pro-max/review-checklist/SKILL.md` — run before calling UI work done.
- `avalonia-docs` MCP for any Avalonia 11 API shape these don't pin down (currently unreachable —
  ENOTFOUND at plan-writing time; retry, or fall back to source-level verification if still down).

---

## Step 1: `EntranceAnimation` attached properties + entrance style

**Files:** `src/Paperbunkr.App/Controls/EntranceAnimation.cs` (new)
**What:** Static class mirroring [`SharedElement`](../../../src/Paperbunkr.App/Controls/SharedElement.cs)'s
shape: `AttachedProperty<bool> EnabledProperty`, `AttachedProperty<int> IndexProperty` (Get/Set pairs).
On `AttachedToVisualTree`, if `GetEnabled(element)` is true, compute `delay = TimeSpan.FromMilliseconds(GetIndex(element) * PerItemDelayMs)`
(a `private const int PerItemDelayMs = 24` starting point, tunable) and schedule
`element.Classes.Add("entered")` after that delay via `DispatcherTimer` (one-shot,
`Stop()`+dispose after firing — don't leak a running timer per container). If `Enabled` is false or
unset, no scheduling happens at all (container just renders in its final state with no `entrance`
class ever added — see Step 2's style contract below). Track per-element timers the same
`ConditionalWeakTable<Visual, ...>` way `SharedElement` tracks subscriptions, so a recycled/detached
container's pending timer is stopped on `DetachedFromVisualTree` (prevents a class-flip firing on a
container that's since been recycled for a different item).

Also add, in `src/Paperbunkr.App/Styles/Primitives.axaml` (edit), one reusable style pair:
```xml
<Style Selector=":is(Control).entranceReady:not(.entered)">
  <Setter Property="Opacity" Value="0"/>
  <Setter Property="RenderTransform" Value="translateY(8px)"/>
</Style>
<Style Selector=":is(Control).entranceReady">
  <Setter Property="Transitions">
    <Transitions>
      <DoubleTransition Property="Opacity" Duration="{DynamicResource PbMotionStandard}" Easing="{StaticResource PbMotionEase}"/>
      <TransformOperationsTransition Property="RenderTransform" Duration="{DynamicResource PbMotionStandard}" Easing="{StaticResource PbMotionEase}"/>
    </Transitions>
  </Setter>
</Style>
<Style Selector=":is(Control).entranceReady.entered">
  <Setter Property="Opacity" Value="1"/>
  <Setter Property="RenderTransform" Value="none"/>
</Style>
```
A container that never gets `EntranceAnimation.Enabled=true` never gets the `entranceReady` class
either (consumers add both together, see Step 2), so this is fully opt-in and touches nothing outside
Library's item containers.
**Depends on:** none
**Verify:** No standalone test for the Avalonia plumbing itself (same call sub-project 1 made for
`SharedElement`) — covered by Step 2's `LibraryScreenViewModelTests` (flag behavior) and the manual
on-screen check. `dotnet build`.

---

## Step 2: Library grid entrance wiring

**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit), `src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit),
`src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit)
**What:**
- `LibraryScreenViewModel`: new `[ObservableProperty] private bool _playEntranceAnimation;` Set it
  `true` at the end of `RebuildView()` (line ~988 — confirm this is genuinely the common path for
  `LoadFromDatabase` (line 860, called on every Library nav-in per `MainViewModel.GoLibrary` at
  `MainViewModel.cs:710-720`) and every filter/sort/group change; if any filter/sort/group setter
  bypasses `RebuildView`, add the flag-set there too) and in `OnViewModeChanged` (line 3174, view-mode
  switch doesn't route through `RebuildView` today). Consumers (Step below) read it once via
  `AttachedToVisualTree` timing, so no explicit "consumed" reset is needed — the next `RebuildView`/
  view-mode change simply sets it `true` again, and Avalonia's own property-changed notification on
  an unchanged `true→true` value is a no-op the container doesn't re-observe (containers only look at
  the *count* at the moment they attach, not a subscribed toggle — see the container-prepared wiring
  below).
- `LibraryScreen.axaml.cs`: for each of the 5 `ItemsControl`/`ListBox` instances (Poster/Panorama/
  List/Details/Tiles), subscribe to `ContainerPrepared` (Avalonia 11's event carries the realized
  container and its `Index`). In the handler: `container.Classes.Add("entranceReady");
  EntranceAnimation.SetIndex(container, e.Index); EntranceAnimation.SetEnabled(container,
  viewModel.PlayEntranceAnimation);` — reading the VM's current flag value at prepare-time (not a
  live binding) is deliberate: it's what gives "one-shot per trigger, not per scroll-recycle" — a
  container prepared well after the triggering event (e.g. scrolling into a not-yet-realized row
  during ordinary scroll, long after `PlayEntranceAnimation` was set) reads whatever the flag's
  *current* value is, which will usually still be `true` right after a real reload/filter/sort/
  view-mode change and irrelevant otherwise since virtualization only ever prepares containers for
  what's about to be visible.
- `LibraryScreen.axaml`: no markup change needed if `entranceReady`/`EntranceAnimation` are applied
  purely from code-behind per above; if the `ItemContainerTheme` approach reads cleaner for a given
  view mode, use it instead — implementer's call per view mode, per the motion skill's two documented
  approaches.
**Depends on:** Step 1
**Verify:** `LibraryScreenViewModelTests` — new tests: `PlayEntranceAnimation` becomes `true` after
`LoadFromDatabase()`, after a sort/filter/group change, and after `ViewMode` changes. `dotnet build`.
Manual: navigate into Library, confirm items fade/slide in staggered by realization order, across all
5 view modes; change filter/sort/group/view-mode and confirm replay; scroll and confirm **no** replay
on ordinary recycling.

---

## Step 3: `DetailHero` parallax

**Files:** `src/Paperbunkr.App/Views/DetailHero.axaml` (edit), `src/Paperbunkr.App/Views/DetailHero.axaml.cs` (edit)
**What:** Implemented entirely inside `DetailHero` itself — not in any of its four hosting screens —
since `DetailScreen.axaml:45`, `MangaDetailScreen.axaml:114`, `BookDetailScreen.axaml:68` (twice, two
`DetailHero` instances at lines 80 and 197), and `HomeScreen.axaml`'s spotlight all wrap it in their
own unnamed `ScrollViewer` with no shared code today. This is what makes it apply to every consumer
"for free" per the design.
- `DetailHero.axaml`: add `x:Name="Backdrop"` to the backdrop `Image` (line 42).
- `DetailHero.axaml.cs`: on `OnAttachedToVisualTree`, walk up via `this.FindAncestorOfType<ScrollViewer>()`
  to find the hosting `ScrollViewer` (present in all four consumers per the survey above — if absent,
  no-op, no crash). Subscribe to its `ScrollChanged`; on each event, compute
  `offset = scrollViewer.Offset.Y * 0.32` (constant tunable at implementation time, ~30-35% per the
  design) and set `Backdrop.RenderTransform = new TranslateTransform(0, offset)` — **unless** Reduced
  Motion is active, checked via `Application.Current!.TryGetResource("PbMotionStandard", out var v) &&
  v is TimeSpan { Ticks: 0 }` (the same live-resource proxy the rest of the app effectively relies on
  via `{DynamicResource}` bindings, just read from code-behind instead of XAML) — when true, leave
  `RenderTransform` at identity regardless of scroll offset (parallax fully off, not just instant).
  Unsubscribe in `OnDetachedFromVisualTree`.
**Depends on:** none
**Verify:** New test in `src/Paperbunkr.App.Tests/` (headless Avalonia, mirroring existing view-level
tests) — mount a `DetailHero` inside a `ScrollViewer` with content tall enough to scroll, set a scroll
offset, assert the backdrop's `RenderTransform` translate Y is a non-zero fraction of the offset; with
the `PbMotionStandard` resource forced to `TimeSpan.Zero`, assert it stays `0` regardless of offset.
Manual: scroll each of comic/manga/book detail and Home's spotlight, confirm the backdrop visibly lags
the foreground; toggle Reduced Motion, confirm the lag disappears.

---

## Step 4: Contextual sidebar slide

**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:** The 236px contextual sidebar `Border` (`MainWindow.axaml:262-266`) gains a `Width`
`Transitions` setter, mirroring the nav rail's own existing width-transition style
(`Border.navRail`, `MainWindow.axaml:64-74,166-171`) but bound to `{DynamicResource PbMotionStandard}`
instead of the rail's `PbMotionFast`. `IsVisible="{Binding ShowContextualSidebar}"` today is a hard
on/off with no width in between — since `Border`'s `Width` isn't animatable through a plain
`IsVisible` toggle (the element leaves/enters the layout tree instantly either way), the actual
mechanism needs the sidebar to stay present in the tree with `Width` bound to
`ShowContextualSidebar ? 236 : 0` (a converter or a `[ObservableProperty]`-backed computed width on
`MainViewModel`) plus `ClipToBounds="True"` so collapsing content doesn't overflow, rather than
`IsVisible` doing the showing/hiding — this is the same shape the nav rail itself already uses for
its own 64↔200 expand/collapse. Confirm this against the nav rail's exact binding before wiring, since
it's the direct precedent to mirror, not `IsVisible`.
**Depends on:** none
**Verify:** `dotnet build`. Manual: switch between screens that show/hide the contextual sidebar
(Library ↔ Home, say), confirm it slides open/closed rather than popping; toggle Reduced Motion,
confirm it snaps instantly.

---

## Step 5: Breadcrumb transition + discard/tour-offer modal animation

**Files:** `src/Paperbunkr.App/Views/Breadcrumb.axaml` (edit), `src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:**
- `Breadcrumb.axaml`: add a `Transitions` setter (`Opacity` + `RenderTransform` translateY,
  `PbMotionStandard`/`PbMotionEase`) to the root `Segments` `StackPanel` (or its wrapping container).
  `Breadcrumb.axaml.cs`'s `Rebuild()` (lines 45-70) stays a clear-and-readd; the transition plays as a
  side effect of the container's own property changes around each rebuild — implementer's call
  whether that needs an explicit opacity toggle bracketing `Rebuild()` (drop to 0, rebuild, back to 1)
  or whether the existing `Clear()`+re-`Add()` already produces enough of a visible size/content jump
  for the transition to read naturally; verify on screen either way.
- `MainWindow.axaml`: the discard-changes modal (`IsDiscardConfirmOpen`, lines 989-1008) and the
  structurally identical tour-offer modal (`IsTourOfferOpen`, lines 1028-1048) each get a fade+scale
  entrance/exit — reuse the same `Transitions` pattern already used for `FloatingPanel` chrome per the
  2026-08-24 design-language spec (`PbMotionFast`/`PbMotionEase`, `Opacity` 0→1 + `RenderTransform`
  `scale(0.96)`→`scale(1)`). Since both are `IsVisible`-toggled `Border`s (not `Classes.open`-driven
  like the Activity drawer), either switch them to the `Classes.open` pattern (keeping them in the
  tree, `IsHitTestVisible` bound to the same flag instead of `IsVisible`) or confirm `Transitions` on
  an `IsVisible`-toggled element still plays on the way in (it won't play on the way out, since the
  element leaves the tree instantly — same caveat Step 6/7's peek-popover note calls out for `Popup`).
  Prefer the `Classes.open` conversion for a real in-and-out animation, consistent with the drawer.
**Depends on:** none
**Verify:** `dotnet build`. Manual: drill through several levels and back, confirm the breadcrumb bar
transitions rather than snapping; trigger the discard-changes modal (edit an issue's properties, try
to navigate away) and the tour-offer modal, confirm both fade+scale in and out.

---

## Step 6: `AnimatedStackPanel` + `PbToastHost`

**Files:** `src/Paperbunkr.App/Controls/AnimatedStackPanel.cs` (new),
`src/Paperbunkr.App/Views/PbToastHost.axaml` + `.axaml.cs` (new),
`src/Paperbunkr.App/Views/MainWindow.axaml` (edit — host it),
`src/Paperbunkr.App/Views/MainWindow.axaml.cs` (edit — replace `_notificationManager` wiring),
`src/Paperbunkr.App.Tests/AnimatedStackPanelTests.cs` (new)
**What:**
- `AnimatedStackPanel : StackPanel` — override `ArrangeOverride`: before calling `base.ArrangeOverride`,
  snapshot each visible child's current `Bounds` position; after arranging, for any child whose
  position changed, set `RenderTransform = new TranslateTransform(0, oldY - newY)` then immediately
  animate it to `TranslateTransform(0, 0)` via a `TransformOperationsTransition` already declared on
  the panel's children (or driven imperatively via `Avalonia.Animation.Animation` if a declarative
  `Transitions` binding doesn't fit the code-only arrange path — implementer's call, staying within
  "`RenderTransform` only, never `Margin`/`Height`" either way). Honor Reduced Motion the same
  resource-check way as Step 3.
- `PbToastHost` (new `UserControl` or plain `Panel`-hosting control): positioned bottom-right, hosts
  an `ItemsControl` over an `ObservableCollection<ToastRequest>` with `ItemsPanel` = `AnimatedStackPanel`,
  `MaxItems`-equivalent cap of 3 enforced in code (oldest dropped or newest refused — match
  `WindowNotificationManager`'s existing `MaxItems = 3` behavior, verify which it actually does before
  picking). Item template = the existing `PbToastView`, unchanged. Entrance: `translateY` (from
  below) + fade, `PbMotionStandard`/`PbMotionEase`. Exit: same, ~70% duration, `CubicEaseIn`.
- `MainWindow.axaml`: add `<views:PbToastHost x:Name="ToastHost" .../>` positioned the same way
  `WindowNotificationManager`'s `BottomRight` reads today (likely just docked/aligned in the root
  `Grid`, `IsHitTestVisible` only where a toast actually is).
- `MainWindow.axaml.cs` (`OnDataContextChanged`, lines 420-467): replace
  `_notificationManager ??= new WindowNotificationManager(this) {...}` and its `Show`/`Close` calls
  with `ToastHost.Show(view, persistent, expiration)` / `ToastHost.Close(view)` methods on the new
  host, preserving the exact same `_persistentToasts` dictionary and close-by-reference logic
  unchanged — only the two calls that currently talk to `_notificationManager` change, per the design's
  explicit "hosting-layer swap only" scope.
**Depends on:** none (independent of Steps 1-5)
**Verify:** `AnimatedStackPanelTests` (headless) — arrange 3 children, remove the middle one, assert
the survivor's `RenderTransform` animates from the correct delta toward identity; with
`PbMotionStandard` forced to zero, assert it snaps immediately. Manual: trigger 1, 2, and 3+ toasts
(e.g. a multi-file import completing, or a scan finishing), confirm entrance/exit look and feel
equivalent to today's, and that dismissing one causes the others to slide rather than jump. Existing
toast-content call sites need no changes — spot-check that update-ready's persistent/actionable toast
(Restart/Later/What's New buttons) still closes correctly via `ToastCloseRequested`.

---

## Step 7: Activity Center drawer audit

**Files:** `src/Paperbunkr.App/Views/ActivityDrawerView.axaml` (edit), `src/Paperbunkr.App/Views/StatusBar.axaml` (edit)
**What:**
- `ActivityDrawerView.axaml:60-75`: change `Duration="0:0:0.22"` → `Duration="{DynamicResource PbMotionStandard}"`
  and `Duration="0:0:0.18"` → `Duration="{DynamicResource PbMotionFast}"` on the two `Transitions`
  entries. No other change — `Easing="{StaticResource PbMotionEase}"` is already correct.
- `StatusBar.axaml:100-119`: add a `Transitions` setter (`Opacity`, `RenderTransform` translateY,
  `PbMotionFast`/`PbMotionEase`, matching the drawer's faster leg since this is a smaller/lighter
  surface) to the `Popup`'s content `Border`. **Verify on screen whether this actually plays on
  close** — `Popup` unmounts its child when `IsOpen` flips false, which may mean the exit leg never
  gets a chance to run (see the design doc's explicit implementation-time-verification note). If it
  doesn't, document that outcome in this plan's own follow-up rather than spending more time forcing
  it — entrance-only animation (still a strict improvement over today's un-token'd default) is an
  acceptable fallback for this specific surface.
**Depends on:** none
**Verify:** `dotnet build`. Manual: toggle Reduced Motion, confirm the Activity drawer's open/close
now snaps instantly (it doesn't today); open/close the peek popover and note in this plan whether its
entrance/exit both play or only entrance does.

---

## Step 8: Full targeted verification + roadmap

**Files:** `docs/Paperbunkr-Roadmap.md` (edit)
**What:** Run `avalonia-pro-max/review-checklist` over every new/edited `.axaml`/`.cs` from Steps 1-7
(hardcoded hex, missing `DynamicResource`, missing Reduced-Motion path, focus handling). Add a
Roadmap entry noting sub-project 2 shipped, closing out the "full app chrome animations" effort
alongside sub-project 1's existing entry (`docs/Paperbunkr-Roadmap.md:1261`).
**Depends on:** Steps 1-7
**Verify:** `dotnet build` clean; targeted `dotnet test` runs for `LibraryScreenViewModelTests`,
the new `DetailHero`/`AnimatedStackPanel` tests, and any suite touching `MainWindow`/`MainViewModel`
navigation (per `[[project_paperbunkr_full_suite_headless_flake]]`, run these as small targeted
`--filter` groups, not the whole suite at once). App smoke-launched; full manual pass over every
on-screen check listed in Steps 2-7 plus the design doc's own Testing section.
