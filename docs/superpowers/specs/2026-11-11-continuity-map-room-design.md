# Continuity map M1 "Room" — design

*Status: approved by the user 2026-11-11 after three grilling rounds (Q1–Q21) and two visual-companion screens (zoom levels, what the
drawn control holds). Source: slice M1 of the [continuity map pitch](2026-11-11-continuity-map-pitch.md) (items 1–5). Implemented in the working tree; on-screen review and legacy-surface removal remain pending.*

## Implementation notes (2026-11-11)

The new drawn canvas, map chrome, overview, zoom, inspector and maximize behavior are in the working tree at base HEAD `28e60a1`. Zoom actions use the shared input service. For a small map whose fit scale exceeds Standard, the minimum zoom is Standard (1×), while Fit still uses the calculated fit scale; this preserves both first-open Standard and the Fit action. The settings migration was tested on a synthetic database and has not been applied to the user's database.

Headless theme renders and real-window keyboard tests exercise the implementation, but the plan's on-screen review, physical touchpad/pinch and Narrator checks remain outstanding. A 2,101-card headless Debug bitmap render measured 5.77 ms median in the passing run and 9.07 ms in an earlier run; this includes window/bitmap rendering and is not a production frame measurement. The legacy surface remains until the plan's user sign-off.

## Goal

The map gets most of the screen, and every series in an event can be found at a glance. Today, at the user's 1366×768 screen, the cards
get about 546 × 450 px. That's 3 columns and 5 lanes at the Standard stop, with no way to see the whole map. M1 gives the map room
(slim header, Maximize, floating inspector, slim lane rail). It adds an overview strip and smooth zoom down to a whole-map "Dots" level.
To make that zoom affordable, it rebuilds the map surface as one custom-drawn control (**approach C**).

## Facts this rests on (verified 2026-11-11)

- **One map view for both scopes.** `EventMapView` is hosted only at `Views/ContinuityScreen.axaml:70`. The event page and the continuity
  page share it, and `EventMapViewModel` switches scope (`IsContinuityScope`). Everything here applies to both.
- **Chrome.**
  - The hero is `HeroBand`, used only by `ContinuityHero` and `EventHero`. It has a fixed padding of 28,22,28,18 and no compact
    variant, and both heroes reserve `Margin="0,0,250,0"` for the Overview | Map | Timeline switch.
  - The contextual sidebar is `MainViewModel.ShowContextualSidebar => IsLibrary || IsSmart || IsEvents` (`MainViewModel.cs:1210`).
    No toggle exists.
  - The nav rail and status bar hide only for `IsInReader` (`MainWindow.axaml:156`, `:828`). No screen can ask for that today.
  - `App.ToggleSidebar` (Shift+F6) toggles the nav-rail pin.
- **Shortcuts.** F11 and F belong only to `Reader.ToggleFullscreen` (`InputActions.cs:131`, reader scope). The Continuity screen registers
  its actions through `ScreenInput.Attach(this, InputScope.Continuity, …)` (`ContinuityScreen.axaml.cs:30`). The map handles its own
  arrow, Home, End, Enter and Esc keys in a tunnel `KeyDown` (`EventMapView.axaml.cs:37`, `:184`). That handler is the focused-item
  navigation that CLAUDE.md leaves to a control.
- **Today's surface** (`Controls/EventMap/EventMapSurface.cs`):
  - It realizes a templated `EventMapCard` per visible cell, with one column and one track of overscan.
  - Its pool never trims. At a 10 px slot with 2,000 cells in view it would create about 2,000 full card controls and keep them, so a
    whole-map zoom level can't be built from cards.
  - Density is a 3-value enum (`EventMapModels.cs:143-165`), clamped `0..2` in three places, with one `ControlTheme` per stop.
- **Already drawn by hand:** `EventMapEdgeLayer` (lane fills, grid, edges) and `EventMapRuler`. The ruler is 50 px on a continuity map and
  28 px on an event map; it draws column labels, trunk badges, and the band and connector rows in block mode, and is offset with
  `OffsetX`. The lane headers are an `ItemsControl` in a `Canvas`, shifted by a `TranslateTransform`, 180 px wide
  (`EventMapView.axaml:233`).
- **The inspector** is a docked `Grid.Column="1"`, 340 px, toggled by `IsVisible`. The reader's drawers (`ReaderScreen.axaml:950`, `:1072`,
  `floatingPanel readerDrawer` + `controls:Reveal.IsOpen`) and the Reading Lists drawer (`ReadingListPageView.axaml:653`) already float
  over content.
- **`EventMapLayoutResult` has everything a minimap needs:**
  - `Cells(Index, Row, Track, Column, Segment, IsTrunk)`
  - `Tracks[].ColorIndex` (-1 is the trunk)
  - `ColumnCount`, `TrackCount`, `Blocks`, `BlockColumns`, `Edges` and `CardRect`
  - Read state per row, but only as a snapshot. The live value is `EventMapCardViewModel.ReadState`.
- **Accessibility without controls is supported.** Avalonia 12.1.1 exposes `AutomationPeer` as a public abstract base and ships a
  non-control peer of its own (`Avalonia.Automation.Peers.UnrealizedElementAutomationPeer`). A drawn control can therefore return one
  virtual peer per visible card from its own peer's `GetChildrenCore`. Not yet tried with Narrator.
- **ComicRack CE has no events or map.** This is all Paperbunkr-only, with no parity to preserve.

## Decisions (from grilling)

| # | Decision |
|---|---|
| Q1 | Choosing Map shrinks the hero automatically to a 44 px slim header. Overview and Timeline keep the full hero. |
| Q2 | The slim header keeps the logo, name, stats line, the view switch and Continue reading. Everything else goes in one **More** menu. The description and chips drop out. |
| Q3 | **Maximize** (toolbar button, F11) also hides the nav rail, contextual sidebar and status bar. Esc steps back: inspector → selection → Maximize. |
| Q4 | Maximize doesn't touch the window state. |
| Q5 | Maximize lasts while you move between maps on the Continuity screen, ends when you leave the screen, and isn't persisted. |
| Q6 | The inspector floats over the right edge (340 px, Reveal). The map scrolls so the selected card isn't under it. A pin docks it as today, and the pin is persisted. |
| Q7 | A hover peek appears at Dots, Chips and Compact only. |
| Q8 | Lanes get a 52 px rail by default. Hover or focus shows the full name. A corner toggle switches to 180 px headers, and the choice is persisted. |
| Q9 | The overview strip is full width between the toolbar and the map, about 32 px, and shows only when the map doesn't fit. It has no toggle. |
| Q10 | The strip draws a tick per card (lane colour, filled if read, hollow if unread, selected marked) and compresses lanes when there are many. On a continuity map it shows event bands with a hover name. It has a draggable viewport box and click-to-jump. |
| Q11 → Q16 | Zoom is **smooth**. Dots, Chips, Compact, Standard and Covers are card looks at size thresholds, with ticks on the slider. **Fit** zooms to the exact scale where everything fits; double-clicking an event band fits that event. The map opens at Standard on the first unread card, and the zoom is remembered per scope for the session. |
| Q12 | Overview and Timeline show the full app chrome even while maximized. Back on Map, the map is maximized again. |
| Q13 | Below Compact the ruler hides column numbers. Bands and badges stay, with trimmed labels. |
| Q14 | Chips draws every edge. Dots draws only the selected card's edges. |
| Approach | **C**: one custom-drawn map control instead of virtualized card controls. |
| Q15 | The drawn control holds the ruler, lane rail, lane fills, edges and cards, scrolling as one. The overview strip is a second small drawn control. The header, toolbar and inspector stay real controls. |
| Q17 | Drag on empty space pans (left or middle button). The wheel scrolls vertically, Shift+wheel horizontally. Click selects, double-click opens the reader. |
| Q18 | The control is a single focus stop. The focus ring is drawn on the selected card. Screen readers get virtual items per visible card, and selection changes are announced. |
| Q19 | Dimming, hover and view glides (Fit, Jump to first unread, inspector links) use the motion tokens and are instant under Reduced motion. |
| Q20 | Covers come from the cover cache, decoded off the UI thread at the zoom's size, with a placeholder until they load. Only near-view covers are kept. |
| Q21 | The new control replaces the old surface in one go, with no switch. The old card controls are deleted only after the user's on-screen check. |

## Design

### 1. Chrome: slim header and Maximize

**Slim header (`MapHeaderStrip`, a new UserControl with its code-behind in the same step; see CLAUDE.md "Build gotcha").**
- It sits in `ContinuityScreen.axaml` row 0, visible when `ShowEventMap`. The existing `ContinuityHero` and `EventHero` are hidden in
  Map view.
- It is one 44 px row on `PbSurface1Brush` with a bottom `PbBorderBrush` hairline, 24 px side gutter, and these parts:
  - the `BrandMark` (24 px, image-only rule as in `ContinuityHero`)
  - the name (`pbTextTitle`, trimmed)
  - the stats line (`pbTextMeta`, muted, trimmed; it now ends with the map's status text, which leaves the toolbar)
  - **Continue reading** (`Button.secondary`)
  - **More** (`Button.secondary` with ChevronDown, `Border.dropdown` + `Button.menuRow`, `PopupFit.ToWindow` + ScrollViewer)
  - the existing Overview | Map | Timeline `segGroup`, moved into the strip's last column instead of floating over the hero
- The **More** items reuse the existing commands on `ContinuityPageViewModel` / `EventPageViewModel`:
  - *Continuity:* Add series, Find series on Metron, then a separator and the Manage items (Edit details, Continue setup, Compare &
    merge, Create reading list ›, Delete).
  - *Event:* Add issues, Smart fill, then a separator and the Manage items (Compare sources, Edit details, Continue setup, Detect roles,
    Delete).
- Narrow windows: the stats line trims first; below 1000 px Continue reading becomes an `iconButton` with its name.
- **Retry** (a failed load) appears in the strip in place of the stats line when the page has an error.

**Maximize.**
- **Main window:** `MainViewModel` gains `IsChromeSuppressed` (bool, not persisted). The nav rail and status bar become visible on
  `!IsInReader && !IsChromeSuppressed`. `ShowContextualSidebar` becomes `(IsLibrary || IsSmart || IsEvents) && !IsChromeSuppressed`, and
  its notification is raised when the flag changes. The sidebar keeps its existing width transition (`motion: reflow`, already justified
  in `MainWindow.axaml`), and the nav rail and status bar get no new animation.
- **Continuity screen:** `ContinuityScreenViewModel` owns `IsMapMaximized` (session state, Q5). It sets
  `IsChromeSuppressed = IsMapMaximized && ShowEventMap` whenever either changes. `MainViewModel` resets `IsChromeSuppressed` to false
  wherever it raises its own screen-change notifications (`MainViewModel.cs:1346`, the same place `ShowContextualSidebar` is
  re-raised) and clears the screen's `IsMapMaximized`, so suppression can never leak to another screen.
- **Shortcut:** a new action `Continuity.ToggleMapMaximize` (`InputActionIds`, one `InputActionInfo` in `InputActions.Core`,
  `InputScope.Continuity`, default F11, group "Map"). It's registered in `ContinuityScreen`'s `ScreenInput.Attach` and returns false
  unless the Map view is showing, so F11 elsewhere keeps its meaning. It shows up in Preferences › Keyboard Shortcuts with no other
  change.
- **Toolbar:** a toggle `Button.iconButton` (FullScreenMaximize / FullScreenMinimize, named "Maximize map" / "Restore map").
- **Esc:** the map's Esc handling gains a third step. When nothing is selected and the map is maximized, Esc restores. Esc stays a
  `FiresInTextInput`-free key handled by the map itself, as today.

### 2. The drawn map control (`EventMapCanvas`)

A new `Control` in `Controls/EventMap/` replaces `EventMapSurface`, `EventMapEdgeLayer`, the lane-header `ItemsControl`, `EventMapRuler` as
a separate pinned row, and the `ScrollViewer`. It owns its own scroll state.

**Coordinate model.**
- The layout engine places every card on a **grid** (column, track), and those grid positions don't depend on zoom. The canvas computes
  rects itself from the grid and the **effective metrics** for the current `Scale`, so zooming never relayouts. Scale 1.0 is today's
  Standard (slot 170, track 84, card 150 × 64).
- **Effective metrics(scale):**
  - slot width = 170 × scale, card width = 150 × scale
  - track height = 84 × scale up to the Covers threshold. Over the last 20 % of scale before it, the track height blends toward the
    Covers height (card width × 1.5 + 28 for the 2:3 cover plus its badge row, plus a 12 px gutter), so lanes grow smoothly instead of
    jumping when the look switches to Covers.
  - `EventMapLayout.Compute` drops its density parameter; `CardRect` and the edge path builders take the effective metrics as an
    argument (cached per frame).
- The control keeps `Scale` (clamped to `[ScaleMin, 2.0]`, where `ScaleMin` is the Fit scale for the whole map but no smaller than 0.06)
  and `Offset` (the content point at the viewport's top left, in current-scale pixels, re-anchored on every zoom).
- The lane rail (left) and the ruler (top) are pinned and unscaled: content is shifted right by the rail width and down by the ruler
  height.
- **Card looks by on-screen card width** (`EventMapCardLook.For(cardWidthOnScreen)`):

  | Look | On-screen card width | Draws |
  |---|---|---|
  | Dots | < 24 px | a filled squircle in the lane colour, hollow if unread; the selected one larger with the accent ring |
  | Chips | 24–80 px | lane bar and badge ("HU #92") |
  | Compact | 80–130 px | lane bar, badge and read glyph |
  | Standard | 130–190 px | lane bar, cover thumb, series, #number, read glyph, date |
  | Covers | ≥ 190 px | full cover, lane bar on top, badge and read glyph |

  - A card's on-screen height follows its look: Dots is a square of the card width (capped at 12 px), Chips through Standard are
    64/150 of the width, and Covers is the 2:3 cover plus the badge row, which is why the track blends taller just before Covers.
  - The threshold where the look switches is hysteresis-free. The look is a pure function of the scale.
- The slider maps logarithmically over `[ScaleMin, 2.0]` with ticks at the four threshold scales. Ctrl+wheel and pinch zoom around the
  pointer, in 10 % steps per notch.
- **Fit** animates to the scale where the whole map fits the viewport, or a band's column span fits on a band double-click, then
  centres. If the selected card would be off screen after Fit, the view keeps it in.

**Drawing (one `Render` pass, culled to the viewport).** Back to front:
1. lane fills, alternating `PbBgBrush` / `PbChromeBrush`
2. block washes (continuity, 5 % accent)
3. grid lines
4. edges: all edges at Chips and above; at Dots only the selected card's. They use today's pens, dimming and 1.5× width for the related
   set, built from the existing pure path builders scaled.
5. cards, by look
6. the drawn focus ring on the selected card when the control has keyboard focus
7. the pinned lane rail (Section 3)
8. the pinned ruler (bands, connectors, trunk badges; column labels only at Compact and above, Q13)

Notes:
- Brushes are resolved with `TryFindResource` at render time and never cached as hex. The control re-renders on
  `ActualThemeVariantChanged` and resource changes, as the edge layer does today.
- Text uses cached `TextLayout`s keyed by (text, size, max width, look) with `TextTrimming.CharacterEllipsis` and
  `maxHeight: double.PositiveInfinity` (CLAUDE.md gotcha). The cache is cleared on theme change and when the layout changes.
- Read state, missing state and selection are read live from the `EventMapCardViewModel`s, the same objects the inspector binds to. A
  change raises `InvalidateVisual`.

**Covers.**
- `EventMapCoverSource` asks `CoverImageCache.TryGetCached` first. On a miss it decodes off the UI thread with `DecodeFromDisk` at a
  bucketed width (48 px at Standard, 256 px at Covers), then posts back and invalidates.
- It keeps an LRU of decoded bitmaps for cards inside the viewport plus one viewport of margin, and disposes evicted bitmaps.
- Until a cover arrives the card draws its placeholder (`PbSurface2Brush`). A decode that finishes after its card left the margin is
  dropped.

**Input.**
- **Pointer:**
  - press on a card → select (deferred through `Dispatcher.UIThread.Post` only where a selection rebuilds inspector lists, as today)
  - double-click on a card → open the reader (`OpenReader` with the card's anchor, unchanged)
  - press on empty space or the middle button → pan with pointer capture
  - wheel → vertical scroll; Shift+wheel → horizontal; Ctrl+wheel, the touchpad magnify gesture or a touch pinch → zoom about the pointer
- **Hit-testing** is a pure function over the layout (`EventMapHitTest.At(point, scale, offset) → cell | band | lane | none`), so it's
  unit-tested. Hover sets the hovered cell for the hover style and peek.
- **Keyboard:** the existing map keys move into the canvas (←/→ reading order, ↑/↓ `NearestInLane`, Home/End, Enter inspector,
  Ctrl+Enter reader, Esc chain). Every move glides the target into view. Arrow keys are tunnelled, as `WantedScreen` does, because the
  canvas is not an `ItemsControl` but sits inside screens that use directional focus.
- The canvas is the single focus stop (`Focusable`, `FocusAdorner = null`, ring drawn inside, CLAUDE.md "focus ring is an adorner").
  `AppGamepadHost`'s D-pad sends the arrow keys it already handles, so no controller code is needed.

**Accessibility.**
- `EventMapCanvasAutomationPeer : ControlAutomationPeer` names the surface "Event map for {name}" / "Continuity map for {name}".
- `GetChildrenCore` returns one `EventMapCardAutomationPeer : AutomationPeer` per card in the current viewport. Each has the name
  "{Series} #{Number}, {role}, {read state}", the ListItem type, a bounding rect from the layout, and selection-item support (select =
  the map's select).
- A viewport change raises `ChildrenChanged`; a selection change raises the focus/selection property change so Narrator announces it.
- Off-screen cards are reachable with the keyboard (moving scrolls them in), as today.

**Motion.**
- Dimming (unrelated cards to 0.25 opacity, edges to 25 % alpha) animates per frame over `MotionTokens.Get(Acknowledge)` with
  `PbMotionEase`.
- Hover (card surface to `PbSurface2Brush`) uses `Hover`.
- View glides (Fit, Jump to first unread, inspector links, keyboard scroll-into-view) animate Offset and Scale over `Travel` with
  `PbMotionMoveEase`.
- All of them ask `MotionPolicy.AllowsMotion(role)` and jump straight to the end value when Reduced is on. No continuous animation runs
  while idle.

**Performance budget.**
- A 2,104-card continuity map at Dots renders a frame in under 8 ms on the dev machine (draw-list count bounded by visible cells).
- Panning and zooming allocate nothing per frame beyond the text-layout cache.
- At most 200 cover bitmaps are kept in memory at once.

### 3. Lane rail, overview strip, inspector, peek

**Lane rail (drawn in the canvas, pinned left).**
- *Slim (default, 52 px):* a 4 px lane-colour bar, a 32 × 48 series cover thumb (the series' first card in the map, through the same cover
  source), and the count (`pbTextCaption` size, muted) under it. The trunk row uses the accent and "Spine".
- *Wide (180 px):* today's header content (colour bar, name, count), drawn.
- Hovering or focusing a lane in slim mode draws a 220 px name flag over the map edge (`PbSurface3Brush`, `PbRadius`, border, fade on
  `Hover`).
- The corner cell above the rail holds today's corner label (trimmed) and the slim/wide toggle (`iconButton sm`, a real control layered
  over the canvas corner).
- The choice is persisted as `AppSettings.EventMapLaneRailWide` (bool, default false; one additive migration with a forward-only test).
- Rail rows scale with the zoom's track height: below 24 px per track the thumbs hide and the rail shows only colour bars.

**Overview strip (`EventMapOverviewStrip`, a drawn `Control`).**
- It sits between the toolbar and the canvas, full width, 32 px tall. It's visible only when the map doesn't fit at the current scale,
  and it fades with `Reveal`.
- It maps the whole layout to the strip:
  - x = column / ColumnCount × width
  - y = track mapped into the strip's 24 px drawing band (4 px top/bottom padding); when there are more tracks than pixel rows, several
    tracks share a row
- A tick (≥ 2 × 2 px) is drawn per card: lane colour, filled if read, outlined if unread, with the selected card as an accent mark 3 px
  wider.
- On a continuity map, event blocks draw as alternating 8 % accent washes behind the ticks. Hovering a block shows its event name as a
  tooltip.
- The viewport box is a `PbAccentBrush` 1.5 px outline. Dragging it pans the canvas; clicking elsewhere centres the canvas there (glide,
  `Travel`).
- It is keyboard-inert (not a focus stop); the canvas is the keyboard surface. Its automation peer is a named, non-interactive "Map
  overview" image.
- It reads the same layout and live read states as the canvas and invalidates with it.

**Inspector (floating).**
- Same content and view model as today (no changes to links, Mark read or Open reader).
- *Floating (default):* `Border.floatingPanel` aligned right over the map area, 340 px, full map height minus 12 px margins.
  `controls:Reveal.IsOpen="{Binding IsInspectorOpen}"` (Reveal fade plus a 20 px panel slide), `Reveal.ReturnFocusTo` the canvas.
- When a card is selected and would sit under the open inspector, the canvas glides so the card's right edge is at least 24 px left of
  the inspector.
- *Docked:* a pin `iconButton sm` ("Dock inspector" / "Float inspector") in the inspector header. Docked restores today's
  `ColumnDefinitions="*,Auto"` placement.
- The pin is persisted as `AppSettings.EventMapInspectorDocked` (bool, default false; same migration as the rail setting).

**Hover peek.**
- Below Standard (Dots, Chips, Compact), resting on a card for 400 ms opens a small `Border.dropdown`-style popup next to it: the cover
  (72 × 108), series #number (`pbTextMeta`), cover date and read state (glyph and text).
- It closes on pointer exit or pan. It's positioned by the hit-test rect and opens with `PopupMotion`. It isn't a focus stop; keyboard
  users get the same information from the screen-reader item and the inspector.

### 4. Toolbar

- The toolbar stays a `WrapPanel`. Its contents become: Spine (event, spine mode) · filter segments · Events… (continuity) · **zoom slider**
  (log scale, ticks, automation name "Map zoom") · **Fit** (`iconButton`, ZoomFit, "Fit map") · Jump to first unread · **Maximize** ·
  pending-duplicates note.
- The status text moves to the slim header (Section 1).
- `Ctrl+0` is **not** added; Fit stays a button and is reachable with the keyboard.

### 5. View model changes (`EventMapViewModel`)

- **Density becomes zoom.** `Density` and `DensityIndex` are replaced by `Scale` (double) and a derived `Look`. `StepDensity` becomes
  `ZoomBy(factor, anchor)`. `SetScale` and `FitAll` / `FitBand(blockIndex)` are added, computed by a pure `EventMapZoom` helper
  (`FitScale(layout, viewport, rail, ruler)`, `ClampScale`, `ScaleForLook(look)`).
- **Session memory:** the last scale per scope (event / continuity), kept on `ContinuityScreenViewModel` for the session (Q11). The first
  open uses `ScaleForLook(Standard)` and scrolls to `FirstUnread`, as today.
- `LaneHeaders` stays as the rail's data (label, count, colour index, cover key).
- New `IsLaneRailWide` and `IsInspectorDocked`, backed by the two settings.
- The pure logic stays where it is: layout, filters, inspector links, related set, navigation, reader anchors and the continuity loader.

## Testing

**Pure (`Paperbunkr.App.Tests/EventMap/`):**
- **`EventMapZoomTests`:**
  - Fit scale for wide, tall and tiny maps
  - clamping at `ScaleMin` and 2.0
  - the look per threshold (both sides of each boundary)
  - log slider mapping round-trips
  - zoom-about-pointer keeps the world point under the pointer fixed
  - FitBand spans exactly the band's columns
- **`EventMapHitTestTests`:** card, band, lane rail and empty at several scales and offsets; the pinned rail and ruler never scale; the Dots
  hit rect is the drawn rect.
- **`EventMapOverviewMappingTests`:** column and track mapping; tracks sharing pixel rows; the viewport box for a given offset and scale;
  click-to-centre maths.
- **The existing layout, spine, loader, navigation and builder tests stay**, adjusted only for `Compute` losing its density parameter.
  Density-stop rectangle tests are replaced by effective-metrics tests (slot, card and track at several scales, including the blend
  before Covers being continuous: no step larger than one scroll notch's worth).

**Headless controls (`PinnedThreadTestFramework`, `TestDispatcher.Drain()`):**
- **Canvas draw list:** a recording drawing context (or the control's own exposed `LastFrame` summary for tests) shows that only cells
  intersecting the viewport are drawn; Dots draws only the selected card's edges; Compact and above draw column labels and below doesn't.
- **Skin reactivity:** swapping `PbChartBlueColor` changes the next frame's pen.
- **Keyboard:** each key produces the expected selection and the view glides it in (instantly under Reduced).
- **Focus:** the canvas is one focus stop; the ring is drawn only with keyboard focus.
- **Automation:** the canvas peer's children equal the viewport's cards, with today's names; selecting through the peer selects the card;
  a viewport change raises ChildrenChanged.
- **Inspector:** a link click changes the selection only after `Drain()`; floating vs docked placement follows the pin; selecting a card
  under the floating inspector scrolls it clear.
- **Overview strip:** hidden when the map fits; dragging the box pans the canvas.
- **Covers:** a miss decodes off-thread and is dropped if the card left the margin; the LRU never exceeds 200.
- **Perf:** a synthetic 2,104-card continuity layout at Dots renders a frame within the budget (Slow-tagged if over 30 s total).

**Chrome (view model and real window):**
- Map shows the slim header and Overview the hero.
- Maximize sets `IsChromeSuppressed`; the nav rail, sidebar and status bar hide.
- Switching to Overview restores them, and back on Map they're maximized again.
- Leaving the screen clears suppression.
- F11 toggles only in Map view, through `IInputService` (`RealWindowKeyboardTests`: F11 on Library does nothing new; F11 in the reader
  still toggles reader fullscreen).
- The Esc chain is inspector → selection → restore.
- The More menu runs each existing command.

**Data:** a forward-only migration test for `EventMapLaneRailWide` and `EventMapInspectorDocked` (existing rows read back false). No
up-down-up test (`project_paperbunkr_migration_updown_up_test_antipattern`). Check the generated `Down()`: if it drops other
`AppSettings` columns from a stale snapshot, hand-edit it to drop only these two (`project_paperbunkr_migration_rollback_orphan_column_bug`).

**UI checks:**
- `XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests` and `SharedControlsStyleTests`
- `FoundationStyleTests.WithFoundation` renders of the map at 1366×768 in Default, Daylight and Matrix, maximized and not, at Dots,
  Compact and Covers, and with the inspector floating
- `avalonia-pro-max/review-checklist` before calling it done

**Not automated:** the feel of pan, zoom and glides, Narrator with real speech, touchpad pinch on hardware, and the look on screen. The user
checks these on screen.

## Implementation order (for the plan)

1. Pure helpers: `EventMapZoom`, `EventMapHitTest`, `EventMapCardLook`, overview mapping, with tests.
2. `EventMapCanvas` drawing: fills, edges, cards by look, ruler and rail pinned, focus ring; replace the surface in `EventMapView`. The
   old `EventMapSurface`, `EventMapCard`, the three card `ControlTheme`s, `EventMapEdgeLayer` and the lane-header `ItemsControl` stay
   in the repo, unused, until step 9.
3. Input: pointer, wheel, pinch, pan, keyboard; glides; motion policy.
4. Covers source and LRU.
5. Automation peers.
6. Overview strip, floating inspector with pin, hover peek, rail toggle; settings and migration.
7. Slim header, More menu, `IsChromeSuppressed`, Maximize action and button, Esc chain.
8. Lint, renders, review checklist, then the user's on-screen check.
9. After the user signs off on screen: delete the old surface, card themes, edge layer, `EventMapDensity` and their tests (Q21).

## Risks

- **Rebuilding a working, tested surface.** The layout engine, view-model logic and reader hand-off don't change, which keeps the risk to
  drawing and input. The old controls stay until the on-screen check.
- **Accessibility is now hand-built.** Virtual peers are supported by the API but untested with Narrator in this app. The user's
  on-screen check includes a Narrator pass over a few cards.
- **Text in a drawn control.** Trimming, DPI scaling (150/200 %) and RTL series names all need checking in the renders. `TextLayout`'s
  `maxHeight` must be `double.PositiveInfinity`.
- **Chrome suppression leaking.** It's set only by the Continuity screen and cleared on route change. A real-window test covers leaving
  the screen while maximized.
- **Shared tree.** The working tree has uncommitted work from other sessions (Series structure D1–D4, theme gallery, and more). This
  touches `MainWindow.axaml`, `MainViewModel`, `ContinuityScreen*`, `EventMapView*`, `InputActions` and the model snapshot. Check
  `git status` before each step.

## Docs to update when built

- `docs/paperbunkr-todo.md`: status, what was verified
- `wiki/Story-Events-and-Relations.md`: the Event Map section (zoom, Fit, Maximize, overview strip)
- The pitch doc: mark M1 items 1–5 as specced/built
