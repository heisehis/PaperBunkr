# Navigation Transition System — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-04-navigation-transition-system-design.md*

## Working-tree note

At planning time the shared working tree has ~73 files modified by a concurrent session
(`2026-09-04-detail-screen-icons-and-glyphs`), **including every core file this feature touches**:
`App.axaml`, `MainViewModel.cs`, `MainWindow.axaml`, `DetailHero.axaml`, `LibraryScreen.axaml`,
`ReaderScreen.axaml`. This plan is written to be executed **in a fresh worktree branched from
`master`** (see `[[project_paperbunkr_concurrent_sessions]]`), not in the shared tree. The
resulting conflicts on merge are all *additive* (two new token blocks in `App.axaml`, two new
members in `MainViewModel.cs`) — trivial to resolve, unlike editing the same files live.

## Subskills to load during implementation (per CLAUDE.md)

- `avalonia/avalonia-pro-max/motion/SKILL.md` — transitions, `TransitioningContentControl`,
  `PageSlide`/`CompositePageTransition`, reduced-motion (already read this session)
- `avalonia/avalonia-graphics-animation/SKILL.md` — `RenderTransform` / `TransformOperations`
  animation, the flying-clone mechanics
- `avalonia/avalonia-custom-controls/SKILL.md` — the overlay-host control + `SharedElement` attached
  properties
- `avalonia/avalonia-property-system/SKILL.md` — `AttachedProperty` registration for `SharedElement`
- `avalonia/avalonia-pro-max/review-checklist/SKILL.md` — run before calling UI work done
- `avalonia-docs` MCP for any API shape the subskills don't pin down (`TransitioningContentControl`
  transition-selection, `Visual.TransformToVisual` return shape)

---

## Step 1: Motion tokens

**Files:** `src/Paperbunkr.App/App.axaml` (edit), `src/Paperbunkr.App/Services/SkinService.cs` (edit),
`src/Paperbunkr.App.Tests/SkinServiceTests.cs` (edit)
**What:**
- `App.axaml`: add `<x:TimeSpan x:Key="PbMotionStandard">0:0:0.22</x:TimeSpan>` and
  `<x:TimeSpan x:Key="PbMotionLarge">0:0:0.32</x:TimeSpan>` next to `PbMotionFast`/`PbMotionSlow`,
  with a comment matching the existing style.
- `SkinService.cs`: add `DefaultMotionStandard = TimeSpan.FromMilliseconds(220)` /
  `DefaultMotionLarge = TimeSpan.FromMilliseconds(320)` consts; in **both** `ApplyPersistedSettings`
  (~line 128) and `ApplyReducedMotion` (~line 251) set the two new resource keys to
  `TimeSpan.Zero` when reduced-motion is on, `Default*` otherwise — same two lines the existing
  `PbMotionFast`/`PbMotionSlow` handling uses.
**Depends on:** none
**Verify:** `SkinServiceTests` — extend the existing reduced-motion assertion (mirrors
`PbMotionFast` zeroing) to cover `PbMotionStandard` / `PbMotionLarge`. `dotnet build` the App project.

---

## Step 2: `SharedElementFlightMath` (pure)

**Files:** `src/Paperbunkr.App/Services/SharedElementFlightMath.cs` (new),
`src/Paperbunkr.App.Tests/SharedElementFlightMathTests.cs` (new)
**What:** pure static class, no Avalonia-visual dependency (uses `Avalonia.Rect` / `Avalonia.CornerRadius`
value types only, which are fine in a test).
```csharp
public readonly record struct SharedElementFlight(
    double TranslateX, double TranslateY, double ScaleX, double ScaleY,
    CornerRadius StartRadius, CornerRadius EndRadius, bool IsNoOp);

public static SharedElementFlight ComputeFlight(Rect source, Rect destination,
    CornerRadius sourceRadius, CornerRadius destRadius);
```
Transform assumes `RenderTransformOrigin` top-left: `TranslateX = destination.X - source.X`, scale =
ratio of widths/heights. Zero or non-finite source/destination width or height → `IsNoOp = true`.
**Depends on:** none
**Verify:** `SharedElementFlightMathTests` — square→wide rect, identical rects (no-op-ish),
zero-size guard, off-screen negative coords. Pure unit tests, `xUnit` like the rest of
`Paperbunkr.App.Tests`.

---

## Step 3: `SharedElement` attached properties + registry

**Files:** `src/Paperbunkr.App/Controls/SharedElement.cs` (new)
**What:** static class with two `AttachedProperty<string?> KeyProperty` and
`AttachedProperty<IImage?> ImageSourceProperty` (+ GetX/SetX). A `KeyProperty` change handler
subscribes the owning `Visual` to `AttachedToVisualTree` / `DetachedFromVisualTree` and
registers/unregisters `(key, visual, () => GetImageSource(visual))` with
`ISharedElementTransitionService` (resolved from `Application.Current` service locator / a static
the service sets, matching how this app wires app-level services — check `SkinService.Shared`
pattern first). No behavior yet if the service isn't registered (design-time / tests) — guard null.
**Depends on:** Step 4 (interface must exist to reference — build Step 4's interface first, or land
them together)
**Verify:** covered via Step 4/8 tests (registration count). No standalone test — it's Avalonia
attached-property plumbing, exercised by the service tests + on-screen.

---

## Step 4: `ISharedElementTransitionService` + implementation

**Files:** `src/Paperbunkr.App/Services/ISharedElementTransitionService.cs` (new),
`src/Paperbunkr.App/Services/SharedElementTransitionService.cs` (new),
`src/Paperbunkr.App.Tests/SharedElementTransitionServiceTests.cs` (new)
**What:** interface + impl per the design's signature block:
`RegisterOverlayHost(Panel)`, `Register/Unregister(key, Visual, Func<IImage?>)`,
`CaptureOutgoing(string key)`, `Task<bool> FlyToIncomingAsync(key, TimeSpan, Easing, CancellationToken)`,
`Cancel()`.
- `CaptureOutgoing`: look up the registered visual, `visual.TransformToVisual(overlayHost)` →
  source `Rect`, snapshot `Func<IImage?>()` + the visual's `CornerRadius` if it's a `Border`
  (else `default`). Store in a `_pending` field.
- `FlyToIncomingAsync`: poll (dispatcher timer, ~16ms tick, ~250ms budget) for a registered visual
  under `key` with a laid-out non-zero `Bounds`. Found → build a `Border { CornerRadius, ClipToBounds,
  Child = new Image { Source, Stretch=UniformToFill } }`, add to overlay at source rect via
  `RenderTransform`, then animate `TransformOperations` (translate+scale) + `CornerRadius` to the
  destination over `duration`/`easing` using an Avalonia `Animation` run on the clone; await
  completion; remove clone; return `true`. Not found within budget → return `false`.
- `Cancel`: cancel the running animation token, remove any clone, clear `_pending`.
- Uses `SharedElementFlightMath.ComputeFlight` for the numbers.
**Depends on:** Step 2, Step 3
**Verify:** `SharedElementTransitionServiceTests` under Avalonia headless (`Paperbunkr.App.Tests`
already has a headless `AppBuilder` fixture — mirror `ReaderCanvas` / existing headless view tests):
- register two visuals, `CaptureOutgoing` then `FlyToIncomingAsync` with a laid-out destination →
  returns `true`, overlay empty at end
- destination never laid out → returns `false` after the poll budget, no exception
- `Cancel()` mid-flight → overlay empty, no throw
Per `[[project_paperbunkr_full_suite_headless_flake]]` run this with a targeted `--filter`, not the
whole suite.

---

## Step 5: Overlay host in `MainWindow`

**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (edit),
`src/Paperbunkr.App/Views/MainWindow.axaml.cs` (edit)
**What:** add `<Panel x:Name="TransitionOverlay" IsHitTestVisible="False" ClipToBounds="False"/>`
to the root `Grid` (line ~153), positioned **after** the `DockPanel` (so above the screens +
breadcrumb) but **before** the Activity drawer / Migration / properties overlays (so modal chrome
stays on top). In `MainWindow.axaml.cs` `OnLoaded` (or the ctor after `InitializeComponent`), call
`sharedElementService.RegisterOverlayHost(this.FindControl<Panel>("TransitionOverlay"))` — resolve
the service the same way MainWindow already gets its dependencies (check current ctor).
**Depends on:** Step 4
**Verify:** app launches (`dotnet run`), `TransitionOverlay` present in the visual tree, nothing
visually changed at rest. Manual.

---

## Step 6: Drill-down shell — one `TransitioningContentControl`

**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (edit),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit),
`src/Paperbunkr.App/App.axaml` (edit),
`src/Paperbunkr.App.Tests/MainViewModelTests.cs` (edit)
**What:**
- `MainViewModel`: add
  `public object? ActiveDrillDownContent => CurrentScreen switch { "detail" => Detail, "mangaDetail" => MangaDetail, "reader" => Reader, "bookReader" => BookReader, "pdfReader" => PdfReader, "bookDetail" => BookDetail, _ => null };`
  and `public enum DrillTransitionKind { None, Push, Pop }` +
  `[ObservableProperty] private DrillTransitionKind _drillTransitionKind;`. Raise
  `OnPropertyChanged(nameof(ActiveDrillDownContent))` in `OnCurrentScreenChanged` (alongside the
  existing `ActiveScreenContent` raise).
- `MainWindow.axaml`: replace the six drill-down `ContentControl`s (lines ~798–839) with one
  `<TransitioningContentControl Grid.Row="1" IsVisible="{Binding !IsLateralScreen}"
  Content="{Binding ActiveDrillDownContent}" PageTransition="{...}">` carrying the six
  `DataTemplate`s (move them from the deleted `ContentControl.ContentTemplate`s into
  `TransitioningContentControl.DataTemplates`, matching the lateral control's existing shape at
  lines 773–795).
- `App.axaml`: add `PbDrillPush` / `PbDrillPop` transitions (a `CompositePageTransition` of
  `CrossFade Duration={StaticResource PbMotionStandard}` + a `PageSlide`
  `Orientation=Vertical`/small offset, or a custom `IPageTransition` — implementer's call per the
  design's open questions). **Reduced-motion:** bind the `TransitioningContentControl.PageTransition`
  to a converter/selector that returns `null` when `DrillTransitionKind == None`, so the coordinator
  setting `None` genuinely skips the animation (the existing `PbScreenSlide` reduced-motion gap
  documented in `App.axaml` is avoided here by routing through `None`, not by hoping the token is
  re-read live).
- Direction: a tiny `DrillTransitionKind` → `IPageTransition` selection (selector class or a
  `PageTransition` binding + converter).
**Depends on:** Step 1
**Verify:** `MainViewModelTests` — `ActiveDrillDownContent` returns the right VM per `CurrentScreen`
(all six + null for lateral). Existing navigation/history tests still green (targeted `--filter
MainViewModelTests`). Manual: drill in/out of each of the six screens, confirm no crash, content
correct. **This step alone (no shared element yet) is a shippable increment** — drill nav just
cross-fades.

---

## Step 7: `NavigationTransitionCoordinator` + wire into `MainViewModel`

**Files:** `src/Paperbunkr.App/Services/NavigationTransitionCoordinator.cs` (new),
`src/Paperbunkr.App.Tests/NavigationTransitionCoordinatorTests.cs` (new),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit),
`src/Paperbunkr.App/App.axaml.cs` or `MainWindow.axaml.cs` (edit — composition wiring)
**What:**
- Coordinator class per the design's `RunAsync(DrillTransitionKind kind, string? sharedKey, Action swapContent)`
  body: reduced-motion → `swapContent()` and return; else `CaptureOutgoing` (if key), `swapContent()`,
  then `await FlyToIncomingAsync`. Ctor takes `ISharedElementTransitionService` + `Func<bool> isReducedMotion`
  + `Func<TimeSpan> largeToken` (read the live `PbMotionLarge` resource) + the easing.
- `MainViewModel`: new ctor param `Func<DrillTransitionKind, string?, Action, Task>? runDrillTransition = null`,
  stored, default `(k, key, swap) => { swap(); return Task.CompletedTask; }`.
- Refactor the drill-down navigation methods to route through it. Central helper:
  ```csharp
  private void RunDrill(DrillTransitionKind kind, string? sharedKey, Action swap)
      => _ = _runDrillTransition(kind, sharedKey, () => { DrillTransitionKind = kind; swap(); });
  ```
  - `GoDetailForSeries` / `GoReaderForIssue` / other fresh push wrappers → `RunDrill(Push, sharedKey, () => { core(); PushHistory(entry); })`. `sharedKey` = `$"cover:{issueId}"` for `GoReaderForIssue` and for `GoDetailForSeries` **only when an issue id is in hand** (Detail is series-keyed — see open question below); else `null`.
  - `NavigateBack` / `NavigateForward` / `NavigateToBreadcrumbIndex` → resolve `Push` vs `Pop` from
    the history cursor move (`Pop` when the replayed entry is shallower than current, `Push` when
    deeper), `sharedKey` from the target entry when it's an issue.
- Composition: build the real `NavigationTransitionCoordinator` where `MainViewModel` is constructed,
  pass `coordinator.RunAsync`.
**Depends on:** Step 4, Step 6
**Verify:** `NavigationTransitionCoordinatorTests` (mock `ISharedElementTransitionService`):
reduced-motion → one `swapContent`, zero service calls; normal → `CaptureOutgoing` then
`FlyToIncomingAsync`; `FlyToIncomingAsync`→false still completes. `MainViewModelTests` — existing
history/back-forward tests green with the default no-op; add one asserting `DrillTransitionKind`
is `Push` after `GoDetailForSeries` and `Pop` after `NavigateBack`.

---

## Step 8: Cover participants — Library tile, Detail hero, Reader first page

**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit),
`src/Paperbunkr.App/Views/DetailHero.axaml` (edit),
`src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit) + possibly
`src/Paperbunkr.App/Controls/AsyncCoverImage.cs` (edit),
`src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit)
**What:**
- Determine the shared key strategy first (see open question). Assuming `cover:{issueId}` keyed on
  the **cover issue** for Detail:
  - `LibraryScreen.axaml` poster tile cover element: `controls:SharedElement.Key="{Binding CoverIssueIdForTransition, StringFormat='cover:{0}'}"`, `controls:SharedElement.ImageSource="{Binding <decoded bitmap>}"`. If `AsyncCoverImage` doesn't expose its decoded `Bitmap` as a bindable/readable property, add one.
  - `DetailHero.axaml` hero art element: same two attached props keyed on the detail's cover issue id.
  - `ReaderScreen.axaml` first-page surface: `ImageSource` fed from `ReaderScreenViewModel` exposing the first decoded page `Bitmap` (`PageImageDecoder` already holds it); key `cover:{issueId}`.
  - `LibraryScreen.axaml.cs`: on a "navigate back into me" signal (new callback param on
    `LibraryScreenViewModel`, or reuse the existing A-Z `ScrollIntoView` path), `ScrollIntoView`
    the target issue so its tile realizes before the service polls.
**Depends on:** Step 3, Step 7
**Verify:** manual/on-screen (no unattended GUI automation — `[[feedback_no_computer_use]]`):
Library→Detail cover flies and grows into the hero; back → flies home to the grid slot (scroll the
grid first, confirm it still lands); Detail→Reader→back. Toggle reduced-motion → instant, no clone.
FlaUI (`Paperbunkr.App.UiTests`): after each drill nav the `TransitionOverlay` panel has zero
children at rest (no stuck clone) — add to the existing UiTests project, serialized per its
`xunit.runner.json`.

> **As implemented (2026-09-05):** the shared key strategy landed as `"issue-cover:{issueId}"` /
> `"series-cover:{seriesId}"` — computed from ids already in hand at each call site (no DB lookup;
> see Step 7's own commit note), not the plan's original `cover:{issueId}` guess. `LibraryScreen.axaml`
> only got the **default Poster grid** templates wired (`PosterGridIssueTemplate`/
> `PosterGridSeriesTemplate`) — Tiles/Panorama/List/Details view modes are unwired this pass (their
> covers are small/cropped enough that the morph would look questionable, and wiring all ~11 tile
> templates blind, without on-screen verification, was judged a worse risk/reward than shipping the
> primary view mode correctly). `AsyncCoverImage` needed no change — `ImageSource` reads the sibling
> `Image`'s own `Source` via an ElementName binding instead of adding a new bindable bitmap property.
> **`ReaderScreen` first-page participation was cut**, not attempted — `PageCanvas`/
> `ReaderScreenViewModel` expose no public first-page `Bitmap` or fit-rect, and building that seam
> blind was judged disproportionate to a first-page cover morph's payoff; Reader keeps Step 6's
> plain push/pop cross-fade. The back-trip `ScrollIntoView` realization was **not** wired either -
> a cover-morph back into a scrolled-away grid position currently just falls through to the
> already-designed "destination never registers → cross-fade" edge case. Both are real, scoped
> follow-ups, not silently dropped.

---

## Step 9: Review + roadmap

**Files:** `docs/Paperbunkr-Roadmap.md` (edit), `docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md` (edit)
**What:** run `avalonia-pro-max/review-checklist` over every new/edited `.axaml` + `.cs`
(hardcoded hex / DynamicResource / focus / reduced-motion). Add a "superseded/extended by
2026-09-04-navigation-transition-system" pointer to the 2026-08-24 spec's deferred-drill-down
bullet. Add a Roadmap entry under the app-chrome section noting sub-project 1 shipped, sub-project 2
(stagger/parallax/sidebar/breadcrumb/toast) still open.
**Depends on:** Steps 1–8
**Verify:** checklist pass documented; `dotnet build` clean; targeted test suites green
(`SkinServiceTests`, `SharedElementFlightMathTests`, `SharedElementTransitionServiceTests`,
`NavigationTransitionCoordinatorTests`, `MainViewModelTests`); app smoke-launched.

---

## Open questions to settle at Step 7/8 (implementation-time, not design)

1. **Detail's shared key.** Detail/MangaDetail navigation is *series*-keyed (`GoDetailForSeries(seriesId)`),
   but the hero shows the **cover issue's** art and the Library tile the user clicked is a specific
   issue/series card. Resolve: key on whatever cover bitmap the Library card actually displays vs.
   what `DetailHero` actually displays — if both are "the series' cover issue," key on
   `cover:{coverIssueId}` and have `GoDetailForSeries` look that id up (it already loads the series;
   the cover issue is known). If they can diverge, fall back to no shared element for Detail (drill
   cross-fade only) and keep the morph for Reader, revisiting in sub-project 2.
2. **`AsyncCoverImage` bitmap exposure** — read the control first; it may already hold the decoded
   `Bitmap` in an accessible field (`[[project_paperbunkr_thumbnail_decode_off_ui_thread]]` —
   threadpool decode landed in `a9d1a69`), in which case just bind it.
3. **Push vs Pop for `NavigateForward`** — Forward re-deepening should read as `Push`; Forward to a
   sibling at the same depth, `Pop`-ish. Use depth comparison against the pre-move cursor.

## Not in this plan (deferred, per the design's out-of-scope)

Sub-project 2 (stagger, parallax, sidebar slide, breadcrumb/banner slides, toast, Activity Center
audit); `RenderTargetBitmap` snapshots for non-image shared elements; fork B2 unification; reader
page-turn system; BookReader/PdfReader internal paging.
