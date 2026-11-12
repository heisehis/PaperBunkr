# Continuity map M1 "Room" — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-11-continuity-map-room-design.md*

## Ground rules for every step

- **Shared tree.** Run `git status` first. The working tree carries uncommitted work from other sessions (Series structure D1–D4, Smart
  fill, theme gallery, …). Touch only the files a step names. The newest migration in the tree is the uncommitted
  `20261110114835_AddEntityProfiles`. The migration in Step 7 is generated after it, and the model snapshot will contain the other
  sessions' uncommitted changes, which is expected.
- **Builds and tests.**
  - Never run two `dotnet build`/`dotnet test` at once.
  - Run only the targeted test classes plus `Speed!=Slow` where named, never the full App suite.
  - Kill `dotnet`/`VBCSCompiler`/`testhost` processes after each step's runs.
  - Build into `bin` before handing over for an on-screen check.
- **New views.** A new `.axaml` always lands together with its code-behind `.cs` (CLAUDE.md "Build gotcha"). If XAML compilation fails
  after `CoreCompile` succeeded, delete `obj/Debug/net*/Paperbunkr.App.dll`/`.pdb` before rebuilding.
- **Skills.** Before writing UI code load `paperbunkr-design-language` and the matching `avalonia` subskills by reading their `SKILL.md`
  off disk (`avalonia-custom-controls`, `avalonia-input-interaction`, `avalonia-graphics-animation`, `avalonia-pro-max/motion`,
  `avalonia-pro-max/accessibility`; `avalonia-pro-max/review-checklist` at the end).
- **The old surface stays** (`EventMapSurface`, `EventMapCard`, the card `ControlTheme`s, `EventMapEdgeLayer`, `EventMapRuler`) until
  Step 10.

Paths below are under `src/`.

## Step 1: Effective metrics and zoom maths (pure)
**Files:**
- `Paperbunkr.App/Services/EventMap/EventMapModels.cs` (edit)
- `Paperbunkr.App/Services/EventMap/EventMapLayout.cs` (edit)
- `Paperbunkr.App/Services/EventMap/EventMapZoom.cs` (new)
- `Paperbunkr.App/Services/EventMap/EventMapCardLook.cs` (new)
- `Paperbunkr.App.Tests/EventMap/EventMapZoomTests.cs` (new)
- `Paperbunkr.App.Tests/EventMap/EventMapLayoutTests.cs` (edit)

**What:**
- **New types:**
  - `EventMapCardLook { Dots, Chips, Compact, Standard, Covers }` with `For(double cardWidthOnScreen)` and the thresholds 24 / 80 / 130 /
    190 px.
  - `EventMapMetrics.ForScale(double scale)`: slot 170s and card width 150s. Track height is 84s, blending over the last 20 % of scale
    before the Covers threshold to `cardWidth × 1.5 + 28 + 12`. Card height follows the look (spec §2).
  - `EventMapZoom`: `FitScale(layoutGrid, viewport, railWidth, rulerHeight)`, `ClampScale`, `ScaleForLook`, the log slider mapping
    (`ToSlider` / `FromSlider`), `ZoomAbout(scale, offset, pointer, factor)`, and `FitBand(blockColumns, viewport)`.
- **Layout changes:**
  - `EventMapLayout.Compute(source, spine, filter, density)` and `ComputeBlocks(source, density)` drop the density parameter.
  - `CardRect(int index)` and `VisibleRange(Rect)` gain an `EventMapMetrics` argument.
  - The edge path builders take the metrics too.
  - `EventMapLayoutResult.Metrics` stays, fixed at Standard, so the old surface compiles until Step 3 replaces it.
- **Callers:** update `EventMapViewModel`, `EventMapViewModel.Continuity`, `ContinuityMapBuilder` and the old surface/edge layer to pass
  `EventMapMetrics.ForScale(1)`, or the Covers metrics where they used Covers.

**Depends on:** none.

**Verify:**
- `--filter "FullyQualifiedName~EventMapZoomTests|FullyQualifiedName~EventMapLayoutTests|FullyQualifiedName~EventMapNavigationTests|FullyQualifiedName~ContinuityMapTests"`
- `EventMapZoomTests` covers:
  - Fit for wide, tall and tiny maps, and the clamp
  - looks on both sides of each threshold
  - the slider round-trip
  - zoom-about-pointer keeps the world point fixed
  - FitBand
  - the Covers blend is continuous (no step bigger than one wheel notch)
- The density-stop rectangle cases in `EventMapLayoutTests` become effective-metrics cases.
- The app still runs the old map unchanged.

## Step 2: Hit-testing and overview mapping (pure)
**Files:**
- `Paperbunkr.App/Services/EventMap/EventMapHitTest.cs` (new)
- `Paperbunkr.App/Services/EventMap/EventMapOverviewMapping.cs` (new)
- `Paperbunkr.App.Tests/EventMap/EventMapHitTestTests.cs` (new)
- `Paperbunkr.App.Tests/EventMap/EventMapOverviewMappingTests.cs` (new)

**What:**
- `EventMapHitTest.At(point, layout, metrics, offset, railWidth, rulerHeight) → Hit(Kind: Card | Band | Lane | Corner | Empty, Index)`.
  The rail and ruler are pinned and unscaled, and the Dots hit rect equals the drawn rect.
- `EventMapOverviewMapping`:
  - `TickRect(cell, layout, stripSize)`, with tracks sharing pixel rows when there are more tracks than rows
  - `ViewportBox(offset, scale, viewport, layout, stripSize)`
  - `CenterOffsetFor(stripPoint, …)`

**Depends on:** Step 1.

**Verify:** the two new test classes: cards, bands, lane rail, corner and empty at three scales and two offsets; tick mapping; the
viewport box; click-to-centre.

## Step 3: `EventMapCanvas` drawing, replacing the surface in the view
**Files:**
- `Paperbunkr.App/Controls/EventMap/EventMapCanvas.cs` (new: properties, scroll state, measure/arrange, invalidation)
- `Paperbunkr.App/Controls/EventMap/EventMapCanvas.Render.cs` (new partial: fills, block washes, grid, edges, cards by look, focus ring)
- `Paperbunkr.App/Controls/EventMap/EventMapCanvas.Pinned.cs` (new partial: lane rail slim/wide, ruler bands, connectors, trunk badges and
  column labels, ported from `EventMapRuler.Render`)
- `Paperbunkr.App/Controls/EventMap/EventMapTextCache.cs` (new)
- `Paperbunkr.App/Views/EventMapView.axaml` (edit) and `Paperbunkr.App/Views/EventMapView.axaml.cs` (edit)
- `Paperbunkr.App/ViewModels/EventMapViewModel.cs` and `EventMapViewModel.Continuity.cs` (edit)
- `Paperbunkr.App.Tests/EventMap/EventMapCanvasTests.cs` (new)
- `Paperbunkr.App.Tests/EventMap/EventMapViewModelTests.cs` (edit)

**What:**
- **The canvas:**
  - A `Control` with `Layout`, `Cards`, `Connectors`, `LaneHeaders`, `RelatedSet`, `SelectedIndex`, `Scale`, `Offset` and
    `IsLaneRailWide` styled properties.
  - It draws per the spec §2 order, culled to the viewport. Brushes come from `TryFindResource` at render time.
  - It re-renders on `ActualThemeVariantChanged` and resource changes, and when card view models raise read-state changes.
  - Text uses `TextLayout` with `TextTrimming.CharacterEllipsis` and `maxHeight: double.PositiveInfinity`, cached in `EventMapTextCache`
    (cleared on theme or layout change).
  - Covers draw as placeholders in this step.
  - For tests it exposes `internal EventMapFrameSummary LastFrame` (cells drawn, edges drawn, column labels drawn).
- **The view:** in `EventMapView.axaml`, the 2×2 grid (corner, ruler, lane headers, ScrollViewer + surface) becomes one `EventMapCanvas`
  plus the corner-cell toggle button layered over its top-left. The toolbar's density `Slider` becomes the zoom slider (log, ticks at the
  four thresholds) plus a **Fit** `iconButton`. The inspector is untouched in this step.
- **The view model:**
  - `DensityIndex`/`Density`/`StepDensity` are replaced by `Scale`, `Look`, `SliderValue`, `ZoomBy(factor, anchor)`, `FitAll()` and
    `FitBand(blockIndex)`.
  - The first open uses `ScaleForLook(Standard)` and scrolls to `FirstUnread`.
  - `RevealRequested` stays, and the view turns it into a canvas glide.
- **Rail and ruler sizes:** `RulerHeight` stays (28 or 50). The rail width is 52 or 180.

**Depends on:** Steps 1–2.

**Verify:**
- `EventMapCanvasTests`:
  - only viewport cells are drawn
  - at Dots, only the selected card's edges are drawn
  - column labels drawn at Compact and above and not below
  - swapping `PbChartBlueColor` changes the next frame's pen
- `EventMapViewModelTests`, with the density cases rewritten as scale cases
- Build and launch: the map draws on both an event page and a continuity page.

## Step 4: Input and glides
**Files:**
- `Paperbunkr.App/Controls/EventMap/EventMapCanvas.Input.cs` (new partial)
- `Paperbunkr.App/Controls/EventMap/EventMapCanvas.Motion.cs` (new partial: per-frame dimming and hover, Offset/Scale glides on the
  `Acknowledge` / `Hover` / `Travel` roles through `MotionTokens` and `MotionPolicy`)
- `Paperbunkr.App/Views/EventMapView.axaml.cs` (edit)
- `Paperbunkr.App.Tests/EventMap/EventMapKeyboardTests.cs` (edit)
- `Paperbunkr.App.Tests/RealWindowKeyboardTests.cs` (edit: one map case)

**What:**
- **Pointer:**
  - press on a card → select
  - double-click → `OpenReader`
  - press on empty space, or the middle button → pan with capture
- **Wheel:** plain wheel scrolls vertically; Shift+wheel scrolls horizontally; Ctrl+wheel zooms about the pointer, 10 % per notch.
- **Gestures:** `Gestures.PointerTouchPadGestureMagnifyEvent` and `PinchGestureRecognizer` zoom.
- **Keyboard:** the existing `OnMapKeyDown` tunnel handler moves from the ScrollViewer to the canvas (←/→, ↑/↓, Home/End, Enter,
  Ctrl+Enter, Esc).
- **Focus:** the canvas is `Focusable` with `FocusAdorner = null`, and draws its ring only on keyboard focus.
- **Moves:** every move glides the target into view, honouring the inspector's footprint once Step 7 adds the floating inspector.
- **Reduced motion:** every animation jumps to its end value.

**Depends on:** Step 3.

**Verify:**
- `EventMapKeyboardTests`: each key gives the expected selection; the glide is instant under Reduced.
- A `RealWindowKeyboardTests` case: arrow keys move the selection on the real `MainWindow` and focus stays on the canvas.
- Manual: pan and zoom feel, checked on screen by the user at Step 9.

## Step 5: Covers
**Files:**
- `Paperbunkr.App/Controls/EventMap/EventMapCoverSource.cs` (new)
- `EventMapCanvas.Render.cs` (edit)
- `Paperbunkr.App.Tests/EventMap/EventMapCoverSourceTests.cs` (new)

**What:**
- `CoverImageCache.TryGetCached` first, else `DecodeFromDisk` off the UI thread at a bucketed width (48 px Standard / 256 px Covers),
  posted back.
- An LRU capped at 200 for the viewport plus one viewport of margin, which disposes evicted bitmaps.
- A late decode is dropped when its card left the margin.
- The slim rail's series thumbs use the same source.

**Depends on:** Step 3.

**Verify:** `EventMapCoverSourceTests`: hit, miss → off-thread decode → invalidate, a late decode dropped, the LRU never exceeds 200,
disposal on eviction.

## Step 6: Automation peers
**Files:**
- `Paperbunkr.App/Controls/EventMap/EventMapCanvasAutomationPeer.cs` (new)
- `Paperbunkr.App/Controls/EventMap/EventMapCardAutomationPeer.cs` (new)
- `EventMapCanvas.cs` (edit: `OnCreateAutomationPeer`)
- `Paperbunkr.App.Tests/EventMap/EventMapAutomationTests.cs` (new)

**What:**
- The canvas peer is a `ControlAutomationPeer`, named "Event map for {name}" / "Continuity map for {name}". Its `GetChildrenCore` returns
  one `EventMapCardAutomationPeer : AutomationPeer` per viewport card. Each card peer has:
  - today's name text, the ListItem control type and a bounding rect from the layout
  - selection-item support that calls the map's select
- Viewport changes raise `ChildrenChanged`; selection changes raise the property-changed event.

**Depends on:** Step 3.

**Verify:** `EventMapAutomationTests`: the children equal the viewport cards with the expected names, selecting through a peer selects the
card, and a scroll raises `ChildrenChanged`. Manual: a Narrator pass at Step 9.

## Step 7: Overview strip, floating inspector, hover peek, rail toggle, settings
**Files:**
- `Paperbunkr.App/Controls/EventMap/EventMapOverviewStrip.cs` (new)
- `Paperbunkr.App/Views/EventMapView.axaml` and `.axaml.cs` (edit)
- `Paperbunkr.App/ViewModels/EventMapViewModel.cs` (edit: `IsLaneRailWide`, `IsInspectorDocked`, `ShowOverviewStrip`, `PeekCard`)
- `Paperbunkr.Data/Entities/AppSettings.cs` (edit: `EventMapLaneRailWide`, `EventMapInspectorDocked`, both `bool` default false)
- the new migration `Paperbunkr.Data/Migrations/<timestamp>_AddEventMapViewSettings.cs` + `.Designer.cs` + `PaperbunkrDbContextModelSnapshot.cs`
- `Paperbunkr.Data.Tests/AddEventMapViewSettingsMigrationTests.cs` (new)
- `Paperbunkr.App.Tests/EventMap/EventMapOverviewStripTests.cs` (new)
- `Paperbunkr.App.Tests/EventMap/EventMapViewModelTests.cs` (edit)

**What:**
- **Overview strip:** 32 px, drawn with `EventMapOverviewMapping`, visible only when the map doesn't fit, shown with `Reveal`. Dragging
  the viewport box pans; a click centres (glide). Event-block washes and a hover tooltip on continuity maps. Not a focus stop; its peer is
  a named image.
- **Inspector:**
  - Floating by default: `Border.floatingPanel` with `controls:Reveal.IsOpen` and `Reveal.ReturnFocusTo` set to the canvas, 340 px with
    12 px margins.
  - Docked when the pin is set (today's `*,Auto` column).
  - The pin is an `iconButton sm` in the inspector header.
  - When it's floating, selecting a card under it glides the card clear.
- **Hover peek:** below Standard, a 400 ms rest on a card opens a `Popup` (`Border.dropdown` look, `PopupMotion`) with the cover, series
  #number, date and read state. It closes on exit or pan.
- **Rail toggle:** the corner-cell button switches slim and wide.
- **Settings:**
  - `IsLaneRailWide` and `IsInspectorDocked` read and write the two `AppSettings` fields through the existing settings-save path. They're
    read from the background with no `GetOrCreateAppSettings` race; follow the pattern other map settings use.
  - Generate the migration with `dotnet ef migrations add AddEventMapViewSettings`, then check `Down()`: if a stale snapshot makes it drop
    other `AppSettings` columns, hand-edit it to drop only these two.

**Depends on:** Steps 3–4 (strip and peek), Step 5 (peek cover).

**Verify:**
- `EventMapOverviewStripTests`: hidden when the map fits; dragging the box pans the canvas.
- `EventMapViewModelTests`:
  - the pin persists; floating vs docked placement
  - selecting under the floating inspector glides clear
  - an inspector link selects only after `TestDispatcher.Drain()`
- The migration test is forward-only: existing rows read back false. No up-down-up.
- `--filter "FullyQualifiedName~AddEventMapViewSettingsMigrationTests"` in Data.Tests.

## Step 8: Slim header, More menu, Maximize, Esc chain, F11
**Files:**
- `Paperbunkr.App/Views/MapHeaderStrip.axaml` + `MapHeaderStrip.axaml.cs` (new, same step)
- `Paperbunkr.App/Views/ContinuityScreen.axaml` and `.axaml.cs` (edit)
- `Paperbunkr.App/ViewModels/ContinuityScreenViewModel.cs` (edit)
- `Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
- `Paperbunkr.App/Views/MainWindow.axaml` (edit)
- `Paperbunkr.App/Services/Input/InputAction.cs` (edit: `InputActionIds.ContinuityToggleMapMaximize = "Continuity.ToggleMapMaximize"`)
- `Paperbunkr.App/Services/Input/InputActions.cs` (edit)
- `Paperbunkr.App/ViewModels/EventMapViewModel.cs` (edit: the Esc chain)
- tests: `Paperbunkr.App.Tests/ContinuityScreen/ContinuityScreenViewModelTests.cs` and `ContinuityScreenViewTests.cs` (edit),
  `Paperbunkr.App.Tests/RealWindowKeyboardTests.cs` (edit)

**What:**
- **`MapHeaderStrip`** binds to `ContinuityPageViewModel` or `EventPageViewModel`, through two `DataTemplate`s, or one strip with
  scope-specific More items. Its contents are spec §1:
  - the `BrandMark`, name, and stats line (plus the map's `StatusText`)
  - Continue reading
  - **More** as a `MenuFlyout`, reusing the heroes' existing commands and Manage `MenuItem`s, so no command is duplicated
  - the moved Overview | Map | Timeline `segGroup`
  - Retry on error
- **ContinuityScreen:** `ContinuityScreen.axaml` row 0 shows `MapHeaderStrip` when `ShowEventMap`, and the heroes otherwise. The floating
  switch `Border` moves into the strip for Map view and stays where it is over the heroes.
- **Chrome suppression:**
  - `MainViewModel` gets `IsChromeSuppressed`. The nav rail and status bar bind to `!IsInReader && !IsChromeSuppressed`, through a new
    `ShowAppChrome` property, so XAML stays a single binding.
  - `ShowContextualSidebar` adds `&& !IsChromeSuppressed`.
  - The screen-change block (`MainViewModel.cs:1335-1348`) resets `IsChromeSuppressed` and the Continuity screen's `IsMapMaximized`.
  - `ContinuityScreenViewModel.IsMapMaximized` sets `IsChromeSuppressed = IsMapMaximized && ShowEventMap`.
  - The session scale memory per scope (Q11) also lives on `ContinuityScreenViewModel`.
- **F11:**
  - `InputActions.cs` gets a new `MapGroup = "Map"` and the entry `App(InputActionIds.ContinuityToggleMapMaximize, MapGroup, "Maximize
    map", InputScope.Continuity, false, K(Key.F11))`.
  - `ContinuityScreen.axaml.cs`'s `ScreenInput.Attach` registers it, returning false unless `ShowEventMap`.
  - A toolbar Maximize `iconButton` (FullScreenMaximize / FullScreenMinimize).
- **Esc:** `EventMapViewModel.Escape()` gains the third step: with no selection and Maximize on, it restores.

**Depends on:** Step 3 (the toolbar slot). Independent of Steps 4–7.

**Verify:**
- *ContinuityScreenViewModelTests:*
  - Map shows the strip and Overview the hero
  - Maximize sets suppression
  - Overview restores and Map re-maximizes
  - leaving the screen clears it
  - the session scale per scope
- *ContinuityScreenViewTests:* every More item runs its existing command.
- *RealWindowKeyboardTests:*
  - F11 in Map view toggles
  - F11 on Library does nothing new
  - F11 in the comic reader still toggles fullscreen
  - Esc chain: inspector → selection → restore
- Keyboard Shortcuts preferences lists "Maximize map" (`PreferencesScreenViewModelTests` is Slow-tagged; run only its shortcut-list case
  by name).

## Step 9: Checks, docs, hand-over for the on-screen look
**Files:** none new beyond renders; docs edits:
- `docs/paperbunkr-todo.md`
- `wiki/Story-Events-and-Relations.md` (Event Map section)
- the spec's new "Implementation notes"
- the pitch doc's M1 row

**What:**
1. Run `--filter "FullyQualifiedName~XamlConsistencyLintTests|FullyQualifiedName~ThemeHardcodedColorLintTests|FullyQualifiedName~SharedControlsStyleTests"`.
2. Make `FoundationStyleTests.WithFoundation` renders (`PB_RENDER_OUT`) at 1366×768:
   - Default, Daylight and Matrix
   - maximized and not
   - at Dots, Compact and Covers
   - with the floating inspector open
   - at 1.5× DPI once
3. Read `avalonia-pro-max/review-checklist` and apply it.
4. Run the whole `EventMap` and `ContinuityScreen` test folders plus `Speed!=Slow` for those namespaces.
5. Build into `bin`.
6. Hand over for the user's on-screen check:
   - pan, zoom and glide feel
   - text trimming
   - a Narrator pass over a few cards
   - F11 / Esc
   - the overview strip
   - covers at Covers
   - a 2,000-issue continuity at Dots
7. Record deviations in the spec's "Implementation notes".

**Depends on:** Steps 1–8.

**Verify:** all of the above. Nothing is called done before the user has looked on screen.

## Step 10: Delete the old surface (only after the user signs off on screen)
**Files (delete):**
- `Paperbunkr.App/Controls/EventMap/EventMapSurface.cs`, `EventMapCard.cs`, `EventMapEdgeLayer.cs` and `EventMapRuler.cs`
- the three card `ControlTheme`s in `EventMapView.axaml`
- `EventMapDensity` and `EventMapDensityMetrics` in `EventMapModels.cs`, and `EventMapLayoutResult.Metrics`
- `Paperbunkr.App.Tests/EventMap/EventMapSurfaceTests.cs`

**What:** remove dead code and its tests. Grep for leftover references (`EventMapDensity`, `EventMapCard`, `Surface.`). `EventMapPalette`
and `EventMapLaneBrush` stay if the canvas or toolbar still use them; delete them otherwise.

**Depends on:** Step 9 and the user's sign-off.

**Verify:** build; the EventMap test folder; the lint set; one launch.
