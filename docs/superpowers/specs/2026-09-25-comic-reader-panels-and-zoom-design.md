# Comic reader — Panels & zoom (slice D) design

Date: 2026-09-25. Status: **built 2026-09-26** (uncommitted; on-screen check by the user pending, see "Implementation notes" at the end).
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, slice D: **#1 guided panel view** and **#14 double-click smart zoom**, which share one panel detector. The user added a
requirement while approving: the zoom range becomes a **smooth 25%-400%** with no preset steps (ComicRack-style 100/150/200/250 sequences are gone), for paged and continuous mode alike.
Slices A, B, F and G are built and uncommitted; this builds on the same shared working tree.

## Facts this design rests on (verified 2026-09-25)

- **No CE parity to check.** `_reference/ComicRackCE` has no panel, balloon or smart-zoom code (searched); its double-click (`ComicDisplayControl.OnDoubleClick` -> `HandleClick(doubleClick: true)`) runs a
  user-mapped command. Panel view is Paperbunkr-original, like the other slices.
- **Zoom today.** `ZoomLevel` is a multiplier over the fit-mode base scale, so **100% = fit** (`ZoomPanMath.ComputeBaseScale * zoom`). `ZoomPanMath.MinZoom = 1.0`, `MaxZoom = 4.0`,
  `DoubleClickZoom = 2.0`. The view model's setter clamps 1.0-4.0 paged and 0.5-4.0 continuous and zeroes pan when it lands on exactly 1.0. `PageCanvas` has its own `ContinuousMinZoom = 0.5` /
  `ContinuousMaxZoom = 4.0`. The zoom flyout (View cluster and drawer, `ReaderScreen.axaml` ~761-785 and ~950-975) has a Slider **0.5-4** (which the paged clamp silently truncates to 1-4) and five
  preset buttons (`SetZoom100/125/150/200/400`). Keys step `ZoomStep = 0.25` additively, Ctrl+wheel `WheelZoomStep = 0.25` additively, pinch is already continuous. `MinZoom` is also used as "the fit
  state" in `CanPan()`, `ToggleZoom` (paged double-click) and the continuous double-tap. The Novels PDF reader shares `PageCanvas` but has its own view-model clamp.
- **Split-page part navigation is the hook for guided view.** `PageCanvas.ExecuteTurn(forward)` first calls `TryStepPart` (`PagePartMath`: a uniform grid of viewport-sized parts of a zoomed page, "Part X/Y"
  label), and only falls through to a real page turn when there is no further part; `LandOnPartAfterPageTurn` puts a newly reached page on its first (forward) or last (backward) part. Reading-order turns
  from keys, clickers, tap zones and the gamepad (F1) all end in `ExecuteTurn`, so panels can replace parts without touching those inputs.
- **Pixels are reachable.** Avalonia 12 `Bitmap.CreateScaledBitmap` and `Bitmap.CopyPixels` let the detector work on a ~480 px copy of the page the reader already holds; SkiaSharp is already a dependency
  (`PageHasher` from slice B is the precedent for pixel analysis). A downscaled analysis costs a few milliseconds, so no persistence is needed (the pitch's "cache per page" becomes an in-memory cache).
- **Motion.** `MotionTokens.IsReducedMotion()` reads the live reduced-motion state; `PageCanvas` already drives per-frame work with `RegisterForNextAnimationFrameUpdate`.
- **Shared working tree:** `PageCanvas.cs`, `ReaderScreenViewModel*.cs`, `ReaderScreen.axaml`, `KeyboardCommandRegistry.cs`, `AppSettings.cs`, `ReaderProfiles.cs`, the model snapshot and `MainViewModel.cs` are
  already modified by earlier slices; edits stay narrow.

## Scope

In: the smooth 25%-400% zoom range (paged and continuous); a local panel detector; guided panel view (paged); smart double-click zoom (paged); a "Show detected panels" tuning overlay; two settings and a
profile field.

Out (decided): balloon detection (a separate, harder detector); ML models; persisting detection results; manual panel editing or a per-page "no panels" override; guided view in double-page layout or in
continuous modes; smart zoom in continuous modes; the Books/PDF readers (the PDF reader keeps its own zoom clamp).

## 1. Smooth zoom, 25%-400%

- **One range everywhere:** `ZoomPanMath.MinZoom = 0.25`, `MaxZoom = 4.0`, and a new `FitZoom = 1.0` for every place that meant "the fit state" (`CanPan`, `ToggleZoom`, the continuous double-tap, the reset
  paths). Paged and continuous share it; `PageCanvas.ContinuousMin/MaxZoom` and the view-model setter's two ranges collapse into it. 100% still means "fit". Below 100% the page is simply smaller than the
  viewport (centred, pan forced to 0); `ZoomLevel` at or below 1.0 zeroes pan (today only exactly 1.0 did).
- **No preset sequence.** The five preset buttons and `SetZoom100/125/150/200/400` are removed; one **"Fit (100%)"** reset stays in the flyout and the palette ("Zoom: reset to fit").
- **Slider:** the flyout slider becomes a **logarithmic** slider (`ZoomSlider` = log2 of the zoom, -2..2, so 100% sits exactly in the middle and 25% to 100% gets the same travel as 100% to 400%), bound through a
  view-model proxy that converts both ways; step 0.01 in log space (about 0.7%). The label keeps `{0:P0}`.
- **Every input is smooth:** Ctrl+wheel multiplies by `exp(delta x 0.25)` instead of adding 0.25; the `Reader.ZoomIn` / `ZoomOut` keys multiply by 1.1 / 1/1.1 (about 10% a press, smooth when held);
  pinch and the F1 gamepad triggers already are; the slider takes any value. Nothing snaps to a step.
- **Continuous mode at 25%:** the layout window shows many more pages at once (about four screens' worth at 25%). The decode pipeline already sizes decodes to the viewport and honours
  `ReaderMemoryLimitMb`; a test asserts the virtualization window stays bounded at 25%, and the slice G overlay's blank-frame counter is the on-screen check.
- **The PDF reader** keeps its own clamp (its view model is untouched), so it does not gain the 25% floor.

## 2. Panel detector (`PanelDetector`, pure)

Input: a luminance grid of the page downscaled to at most 480 px on its long side, plus the reading direction. Output: `PagePanels` = panel rectangles normalized to 0-1 of the page, **in reading order**, and
a confidence flag. No ML, no new dependency.

- **Gutter model:** a row or column is *gutter* when at least 98.5% of its pixels are "background-like". Background is chosen per page as the dominant of white-ish (luma > 235), black-ish (luma < 20) or the
  page's own border colour, so white-gutter comics and black-border manga both work.
- **Recursive cut:** find the widest run of gutter rows (a horizontal cut) or gutter columns (a vertical cut) at least 1.2% of the dimension thick; split; recurse into each part, alternating preference by
  the longer side; stop when a part has no cut or falls under 4% of the page. Each leaf is trimmed to its content bounds.
- **Order:** the cut tree gives the order: a horizontal cut reads top before bottom; a vertical cut reads left before right, **right before left for right-to-left reading**. Top-to-bottom mode reads as
  left-to-right.
- **Fallback:** fewer than two leaves, or leaves that together cover under 30% of the page (art without gutters, splash pages, textured pages), yields one rectangle = the whole page, `Confident = false`.
  Guided view then shows the page whole and steps on; smart zoom falls back to 200%.
- All thresholds are named constants, tuned on the user's own pages using the tuning overlay (section 6).

## 3. Guided panel view (#1)

- **Toggle:** `G` (`Reader.ToggleGuidedView`, remappable, `ConflictContext.Paged`), a palette entry and a drawer button. Off when a reader opens unless `AppSettings.GuidedViewOnOpen` is on; a profile may
  carry it (`ReaderProfileState.GuidedView`). Flipping it with `G` is per-visit, like the stats chip. In double-page layout it does nothing but show a toast, "Guided view needs single-page layout";
  continuous modes ignore it.
- **Stepping:** with guided view on and panels known, `TryStepPart` is replaced by `TryStepPanel`: next/previous moves to the next/previous panel in reading order (a pan **and zoom** to that panel);
  past the last panel the turn falls through to a real page turn, which lands on the new page's **first** panel going forward and its **last** going back (`LandOnPartAfterPageTurn` generalised). Entering a
  page goes straight to panel 1 (no whole-page overview). Until detection for a newly shown page finishes the whole page shows, then it eases to the landing panel.
- **Panel to view:** `PanelViewMath.ForPanel(rect, contentSize, viewport, fitMode, fitOnlyIfOversized)` returns the zoom and pan that fit the panel in the viewport with a 4% margin, zoom clamped to
  `[FitZoom, MaxZoom]` (a small panel gets at most 400%), pan clamped by the existing `ZoomPanMath.ClampPan`.
- **Motion:** the step is a tween (`ZoomPanTween`, pure: interpolates zoom in log space and pan linearly, 180 ms ease-out) driven by the canvas's animation-frame callback; any user gesture (drag, wheel, key,
  slider) cancels it; reduced motion snaps.
- **Label:** the existing `PartLabel` reads "Panel 3/7" while guided view is on (and "Part X/Y" otherwise, as today).
- **Data flow:** `PagePanelService` computes panels lazily off the UI thread for the current and next page from the displayed bitmap, keeps the last 8 pages in an in-memory LRU keyed by issue and page, and
  raises a change the view model forwards as `CurrentPagePanels` (and `PageCanvas.Panels`).

## 4. Smart double-click zoom (#14)

- **Paged mode, mouse double-click and touch double-tap:** zoom to the panel under the pointer (`PanelViewMath.ForPanel`, tweened); a second double-click returns to the zoom and pan it left. With
  no panel under the pointer (a gutter) or detection unsure, it behaves as today (200% centred on the click). In guided view a double-click toggles between the current panel and the whole page.
- **Setting:** `AppSettings.SmartDoubleClickZoom` (default **on**; off restores the old 200% behaviour exactly). Continuous modes keep their own double-tap zoom (now toggling to `FitZoom` instead of
  `MinZoom`).

## 5. Tuning overlay

Palette command **"Show detected panels"**: a `PanelDebugOverlay` control flashes numbered rectangles over the page for about 3 seconds (same lifetime as the tap-zone flash), drawn from the canvas's current
page-to-screen rectangle and the page's panels; confident detections in the accent brush, an unsure fallback dashed. It exists so the user can see what the detector found on any real page and report bad ones.

## Settings and migration

One migration `AddGuidedViewSettings`, **no-op `Down()`**, `HasDefaultValue`: `GuidedViewOnOpen` (false), `SmartDoubleClickZoom` (true). Preferences -> Reader gets the two toggles in a new
"PANELS & ZOOM" group (`Tag="reader.panelsZoom"` plus its `PreferenceIndex` entry). Both raise `ReaderDisplaySettingsChanged`. `ReaderProfileState` and `ReaderProfileOverlay` gain `GuidedView`
(nullable); `SmartDoubleClickZoom` stays global.

## Build order

1. Zoom range: constants, view-model setter, canvas clamps and `FitZoom` call sites, multiplicative wheel and keys, log slider proxy, preset removal, palette reset entry. Tests, then on-screen check.
2. `PanelDetector` (pure, synthetic pages) and the order and fallback rules.
3. `PanelViewMath`, `ZoomPanTween`, `PanelStepper` (pure).
4. `PagePanelService`, view-model and canvas integration: guided view, stepping, landing, label, `G`, palette, drawer button, spread toast, settings, migration, Preferences group, profile field.
5. Smart double-click.
6. Tuning overlay, docs, wiki (`Reading.md`, `Preferences.md`, `Keyboard-Shortcuts.md`), memory, review checklist over the new XAML.

The user checks on screen after steps 1, 4 and 6 (the smooth zoom and slider, guided view on real comics and manga, the tuning overlay) and reports pages the detector gets wrong.

## Testing

- **Pure:** `ZoomPanMath` clamps and `FitZoom` behaviour; log-slider conversion round-trips and pins 100% to the middle; multiplicative wheel and key steps; `PanelDetector` on synthetic pages (2x2 grid, staggered rows, an L-shaped layout, black-gutter manga, RTL order, a splash page, a gutterless page, sliver noise, tiny panels); `PanelViewMath` (margin, clamp, aspect); `ZoomPanTween` (endpoints, monotonic, log-space midpoint); `PanelStepper` (next, previous, across pages, landing first/last, empty and single-panel pages).
- **View model / canvas:** zoom setter zeroes pan at or below 1.0; presets are gone; guided toggle rules (spread toast, continuous ignored, setting and profile default); stepping falls through to a page turn after the last panel; smart double-click and its restore, and the fallback; the continuous virtualization window stays bounded at 25%.
- **Data:** migration defaults and round trip.
- **Not testable here:** how well the detector reads the user's real pages, the feel of the tween and the slider. Those are the user's on-screen checks; the tuning overlay is the tool.

## Open risks

- **Detector quality is the whole feature.** Comics with borderless art, overlapping panels, diagonal gutters and heavily textured pages will fall back to "whole page"; the conservative fallback makes that harmless rather than wrong, and the overlay makes it visible.
- **`MinZoom` semantic split.** Every place that meant "fit" must move to `FitZoom`; missing one shows up as a page that won't reset or a drag that will not start below 100%. The constants get a test that pins them.
- **Continuous mode at 25%** raises the number of visible pages; watch the memory limit and blank frames.
- **Shared canvas:** the PDF reader binds the same `ZoomLevel`; its clamp stays where it is and a test opens it to confirm it still stops at 100%-400%.

## Implementation notes (2026-09-26)

Built in the order given under "Build order" (plan: `2026-09-25-comic-reader-panels-and-zoom-plan.md`). Where the code differs from the design:

- **Zoom range.** `ZoomPanMath` gained `MinZoom 0.25`, `FitZoom 1.0`, the log-slider helpers (`ZoomToSlider`/`SliderToZoom`, -2..2), `WheelZoomFactor`, `KeyZoomFactor` (1.1) and `IsFit`. The canvas has a new
  `MinZoomLevel` styled property (default 100%, the comic reader binds 25%), so the **Novels PDF reader keeps its 100%-400% range without touching its view model**; `ContinuousMinZoom` is gone. The view
  model exposes `ZoomSlider` (log2 proxy) and `ResetZoomCommand`; `SetZoom100/125/150/200/400` and `ZoomStep` are removed. Every "is it unzoomed" check uses `FitZoom`/`IsFit`, and a double-click on a page
  zoomed *out* (below 100%) now resets to fit as well. Tests pin the range, the slider round trip (100% exactly in the middle), proportional wheel and key steps, and that the part grid and continuous
  virtualization window stay sane at 25%.
- **Panel detector.** As designed, with one refinement found by a test: **rows are cut before columns whenever a full-width gutter exists**, so a regular grid reads row by row even when its vertical gutter is
  wider (a tall panel beside a stack still cuts the column first, because no row cut exists). Polarity (white or black gutters) is judged from the page border, with the other tried as a fallback.
- **`PagePanelAnalyzer`** reads pixels by scaling the displayed bitmap to about 480 px and round-tripping it through PNG into Skia (the same no-assumptions route `SpreadComposer` uses); a bitmap the pipeline
  disposed meanwhile yields the whole page. `PagePanelCache` keeps the last 8 pages in memory keyed by issue, page and reading direction. Nothing is stored on disk.
- **Guided view** lives in `PageCanvas.Panels.cs` (the canvas is now `partial`) and `ReaderScreenViewModel.Panels.cs`. `TryStepPart` hands over to `TryStepPanel` while guided view is active; landing on a new
  page is deferred until that page's panels arrive (the whole page shows meanwhile), and the view model clears the old page's panels *before* it swaps the page. A page detected as unconfident keeps the plain
  fit view (one "panel" that is just the page, no "Panel 1/1" label). Manual zoom, pan or the slider mark the framed panel stale, so the next step continues from the nearest panel. The tween is a
  `TopLevel.RequestAnimationFrame` loop over `ZoomPanTween`; reduced motion snaps.
- **Smart double-click found a pre-existing flaw and fixes it.** The first press of a double-click lands on a tap zone and acts at once, so on the right half the page turned (or a panel stepped) *before* the
  double-click zoom, which is also what the old 200% double-click did. Delaying every page turn by the double-click interval would make the reader sluggish, so instead the canvas remembers the first press when it
  really turned the page or stepped a panel, and the second press puts that back (a page turn is turned back; a panel step restores the exact previous view at once) before zooming. Single clicks are unchanged
  and still act immediately. This also applies with smart zoom turned off.
- **Tuning overlay:** `PanelDebugOverlay` (numbered rectangles, dashed whole-page outline and a caption for an unconfident page), `ReaderScreenViewModel.ShowDetectedPanelsCommand` (three seconds, the
  reader's own timer seam), palette entry "Show detected panels", a toast in continuous or spread layout. The code-behind hands the overlay the canvas's page rectangle when it opens.
- **Settings:** `AppSettings.GuidedViewOnOpen` (off) and `SmartDoubleClickZoom` (on), migration `AddGuidedViewSettings` (no-op `Down()`), Preferences group `reader.panelsZoom`, `ReaderProfileState.GuidedView`
  (a profile can switch guided view on; the Preferences default only re-applies when it changes, so a visit's own `G` is not undone by an unrelated setting). `G` is `Reader.ToggleGuidedView`
  (`ConflictContext.Paged`); the drawer's PAGE grid gained a Guided view button (the grid now grows rows).
- **Test isolation:** `ReaderScreenKeyTests` and the new `ReaderScreenPanelTests` redirect the database to a temp file, because the palette, profiles and settings read it and another session had left the real
  per-user database in a broken state.
- **Not verified:** how the detector reads real comics and manga (the tuning overlay is the tool), the look and feel of the eased steps and the log slider, and the double-click undo on a real mouse.

## Follow-up after the first on-screen use (2026-09-26)

The user tried slice D and reported three things: the zoom "doesn't feel smooth at all", guided view on `G` "just zooms out and nothing more", and a zoomed-in page could not be turned. What was found and changed:

- **Zoom jumped instead of gliding.** Every wheel notch (about 22%), key press (10%) and button press set the new zoom instantly, so however fine the steps were it read as stepping. Now each of those moves a zoom *goal* and the canvas eases towards it a frame at a time (`PageCanvas.Smooth.cs`: `SmoothZoomBy`/`SmoothZoomTo`, exponential approach in log space with a 70 ms time constant, so a run of notches blends into one glide; the point under the cursor, or the middle for keys and buttons, stays put in paged and continuous mode; reduced motion jumps). The buttons, `Z` and the palette reach the canvas through `ReaderScreenViewModel.ZoomStepRequested`/`ZoomResetRequested` (Fit glides to the fit view); with nobody listening they still change the zoom at once. Dragging the slider, pinch and the gamepad trigger were already continuous and are untouched; a zoom change from any of them ends a glide in progress.
- **A zoomed page was a dead end.** The arrow keys, the wheel and the D-pad only ever panned once zoomed. Now, at the edge of the page, the same key or a fresh scroll turns the page (through the split-page part grid first, so a page zoomed to several screenfuls reads on part by part, like PageDown). A wheel notch needs 350 ms of quiet before it may turn the page and an arrow press 120 ms (a key repeat is 30 ms), so scrolling or holding a key down to the edge does not carry on through the book.
- **Guided view had three problems.** (1) The arrow keys panned the framed panel instead of stepping, because the page is zoomed while a panel is framed; now, while guided view is on and you have not moved the view yourself, the arrows and the wheel step panels (up/down step in reading order). Zooming or dragging by hand hands the keys back to panning until the next step. (2) A panel bigger than the screen at 100% (any tall panel on a long strip, and every whole page in fit-width mode, the reader's default) could not be shown: the zoom is clamped at 100% and the panel was only centred, so the view barely changed. `PanelViewMath.SplitOversized` now cuts such a panel into overlapping (10%) screen-sized slices in reading order, and guided view steps through those ("Panel 3/9"). (3) A page the detector gave up on looked identical to "nothing happened"; the label now says "No panels found", and switching guided view on shows a toast saying how it works.
- **Tried on real pages.** The detector was run (read-only, from a scratch harness) on a chapter of the user's library, a long-strip webtoon (800x15000 pages, 5 to 8 panels each, plausible). Regular comic and manga pages were not available to test on and remain the user's check.
