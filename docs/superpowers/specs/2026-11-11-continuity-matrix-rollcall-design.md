# Continuity map M3 "Matrix, roll-call and progress" — design

*Status: approved by the user 2026-11-11 (grilling round 4, Q28–Q32 and Q43, with visual-companion screens). Source: slice M3 of the
[continuity map pitch](2026-11-11-continuity-map-pitch.md) (items 7, 11, 13). Builds on [M1 Room](2026-11-11-continuity-map-room-design.md)
and uses [M2 Layout](2026-11-11-continuity-map-layout-design.md)'s Hide read. Missing counts come from
[M4](2026-11-11-continuity-map-completeness-design.md) when that's built. Implemented in the working tree at base HEAD `28e60a1`
(Codex built §1–§4 on 2026-11-11; tests and docs finished 2026-11-12); on-screen review pending.*

## Goal

Answer "which series are in this, and have I got and read them all?" on one screen: a **Matrix** view (series × phases), a **roll-call**
panel listing every series with its counts, and progress shown on the map itself.

## Facts this rests on (verified 2026-11-11)

- **Segments come straight out of the layout.** `EventMapLayoutResult` exposes `Cells(Index, Row, Track, Column, Segment, IsTrunk)`,
  `Segments` as `EventMapSegment(Index, TrunkCell?, StartColumn)`, `Tracks` (with SeriesId and ColorIndex) and `CellsOnTrack`
  (`EventMapModels.cs:187, 196`; `EventMapLayout.cs:391`). So a series × segment grid can be read directly.
- **Relay and block modes have no segments** (everything is segment 0). On a continuity map, rows carry `BlockIndex`, `OwnerEventId`,
  `OwnerEventName` and `AlsoIn`. `EventMapBlock(Index, Kind, Label, EventId, FirstRow, LastRow)` has no read counts
  (`EventMapModels.cs:21-41`; `ContinuityMapLoader.cs:275-284`).
- **Event-level read counts exist only off the map**: `OverviewEventRow.ReadCount` and `EventOverview`.
- **The view switch** is `EventsDetailView { Primary, Timeline, Map }` with segment buttons in `ContinuityScreen.axaml:44-49` and
  `OnDetailViewChanged` (`ContinuityScreenViewModel.cs:168-184`). Q43 keeps that at three and adds a sub-switch inside Map.
- **The drawn foundation** is M1's `EventMapCanvas` pieces: pinned rail and ruler, zoom/hit-test helpers, virtual automation peers, cover
  source and motion.

## Decisions

| # | Decision |
|---|---|
| Q43 | The top switch stays Overview \| Map \| Timeline. The map toolbar starts with a **Map \| Matrix \| List \| Atlas** sub-switch (List and Atlas come with M6a; M3 ships Map \| Matrix). |
| Q28 | Matrix rows are series. Columns are spine segments on a spine event, one per block (event / between / year) on a continuity map, and chunks of 8 reading positions on a relay event. |
| Q29 | 28 px rows showing count + read ring; clicking a cell expands its covers below the row; double-clicking opens that issue on the Map. |
| Q30 | A drawn control on M1's foundation, for events and continuities. |
| Q31 | A 260 px roll-call panel docked left, opened from a "Series" toolbar button, shared by every map mode. Click scrolls to the lane; a checkbox hides it; hidden lanes reset when you leave the screen. |
| Q32 | Progress: a 2 px bar under each lane's count, "27 / 41 read · 3 missing" in the corner, and a fill on each continuity ruler band. |

## Design

### 1. The sub-switch

- `EventMapViewModel` gains `MapMode { Map, Matrix, List, Atlas }`.
  - List and Atlas stay hidden until M6a, and Atlas only shows on continuity scope.
  - The mode lasts for the session, per scope, on `ContinuityScreenViewModel`.
- The toolbar's first item is a `Border.segGroup` with `Button.segItem` entries.
  - The Continuity screen's `TabNext`/`TabPrevious` (`ContinuityScreen.axaml.cs:32-33`, `TabStrip.Step`) keep stepping the **top**
    switch.
  - The sub-switch gets no shared tab action. Its buttons are reachable by Tab and the arrow keys like any toolbar button.
  - The plan checks how `TabStrip.Step` picks a strip when two `segToggle` groups are on screen, and gives the sub-switch's buttons a
    different class if needed so the top switch stays the one stepped.
- All modes share the toolbar's filters, Hide read, the Events picker, the roll-call panel, the inspector and selection.
  - Selecting an issue in one mode keeps it selected in the others.
- Mode-specific toolbar items:
  - Map only: zoom/Fit.
  - Matrix: a "Covers" toggle in place of the zoom controls (see §2).

### 2. The Matrix (`EventMatrixCanvas`)

**Model (pure, `EventMatrixBuilder.Build(layout, scope, ghosts?) → EventMatrix`):**
- **Rows:** one per series in the layout's lanes, in lane order.
  - Folded tie-ins (M2) are **unfolded** here: every series gets its own row. That is the point of the Matrix.
  - The trunk series comes first in spine mode.
- **Columns:**
  - *Spine event:* one per segment. Segment 0 is "Before" (hidden when empty). Every other segment is labelled by its trunk card's role
    and badge: "Prologue", "#1", "#2", …, "Epilogue". A tooltip carries the full trunk title.
  - *Relay event:* chunks of 8 consecutive reading positions, labelled "1–8", "9–16", …
  - *Continuity:* one per block.
    - An event block is labelled with the event name, trimmed in the header, full name in a tooltip.
    - A between block is labelled "Between · 2004–06"; publication-mode year blocks by their year.
    - "Events only" hides between-blocks, as on the map.
  - A final **Total** column shows "9 · 7 read".
- **Cells:** the issues of that series in that column, in reading order, plus counts (total, read, in progress, missing from M4 ghosts).
- **Filters:** Hide optional, Hide read, Events-picker hiding and roll-call hidden rows all apply.
  - Under Hide read, counts show unread only and the ring shows nothing read.

**Drawing:**
- Rows are 28 px.
- The header row (40 px, horizontal labels, trimmed, 96 px minimum column width) and the series column (200 px: colour bar, name
  trimmed, count) are pinned, using M1's pinned rail/ruler pieces.
- A cell draws:
  - its count (`pbTextCaption` size)
  - a 14 px read ring: `PbSuccessBrush` arc for the read fraction, the rest `PbBorderBrush`
  - a **dashed** `PbDangerBrush` ring when the cell has M4 ghosts
  - nothing when empty
- Hover tints the cell with `PbSurface2Brush`; the selected cell gets the accent border.
- Rows alternate `PbBgBrush` / `PbChromeBrush`, as lanes do.
- Wide matrices scroll both ways; there's no zoom.
- **The Covers toggle** grows rows to 64 px and draws up to 3 stacked 36 × 54 covers per cell, through M1's cover source.
  - It lasts for the session; the default is off.

**Expansion:**
- Clicking a cell (or pressing Enter) opens an **expansion strip** under that row, 96 px tall, with that cell's issues as Standard-look
  cards (M1's card drawing), scrolling sideways when there are more than fit.
- Clicking a card selects it and opens the inspector; double-clicking opens the reader, as on the map.
- One strip is open at a time; Esc closes it first (it goes in front of the map's Esc chain).
- **Double-clicking a cell**, or Ctrl+Enter, switches to Map mode with that cell's first issue selected and scrolled into view (glide).

**Keyboard and accessibility:**
- Arrow keys move the focused cell; Home/End go to the first/last column; Ctrl+Home/End to the first/last cell.
- The canvas is one focus stop with a drawn focus ring.
- Virtual peers per visible cell, named "{Series}, {column label}: {n} issues, {r} read[, {m} not owned]".
  - The expansion strip's cards are child peers of their cell.

### 3. Roll-call panel (`EventMapRollCallPanel`, a UserControl)

- **Placement:** a 260 px panel docked at the left of the map area, between the toolbar and the canvas.
  - It slides in with `controls:Reveal.IsOpen`; `motion: reflow`, because the map's width really changes.
  - Opened by a "Series" `Button.secondary` toggle (FluentIcon TextBulletList) with `Reveal.ReturnFocusTo` on it.
  - Open/closed lasts for the session.
- **Rows** are a virtualizing list (`ListBox`, keyboard per CLAUDE.md's virtualizing-list rule).
  - Each row: 3 px lane colour bar, a CheckBox (shown), the series name (`sideItemLabel`, trimmed), and a `countPill` "9 · 7 read".
  - Under the name, a muted line "1 missing" (M4) and the relation label (M2) when present.
  - Order is first appearance. A `searchBox` filter appears above the list when there are more than 12 series.
- **Clicking a name:**
  - Map: scrolls the lane into view and plays a highlight (lane fill to `PbAccentSoftBrush`, fading back over `Acknowledge`).
  - Matrix: scrolls to the row.
  - List (M6a): scrolls to that series' first row.
- **The checkbox** hides that series everywhere (map lanes, matrix rows, list rows) by adding to `HiddenSeriesIds` on the map view
  model. The layout drops those rows before layout, like the filters.
  - Hidden series reset when the Continuity screen is left.
  - "Show all" (ghost) sits in the panel header when any are hidden.
- **Folded tie-ins** (M2) are listed individually; hiding one of them hides it inside the folded lane.

### 4. Progress on the map

- **Lane rail:** a 2 px bar under the count — `PbSuccessBrush` for read, `PbAccentBrush` for in progress — on a `PbBorderBrush` track.
  - It's drawn in both slim and wide rails, and hidden when the rail's track is under 24 px.
- **Corner label:** "27 / 41 read" plus " · 3 missing" when M4 ghosts exist, replacing today's corner text.
  - The spine/relay description moves into the slim header's stats line.
- **Continuity ruler bands:** each event band gets a 3 px fill along its bottom edge for the read fraction of its cards. Hovering the band
  adds "· 60 % read" to its tooltip.
- **Counts** come from the layout's cells and the live card read states, recomputed when a read state changes in place (no reload).

## Testing

**Pure (`EventMatrixBuilderTests`):**
- spine columns (Before hidden when empty; Prologue/#n/Epilogue labels)
- relay chunks of 8
- continuity blocks and Events-only
- folded series unfolded into rows
- the Total column
- counts under Hide read
- ghost counts (fake ghosts)
- hidden series dropped

**Headless:**
- *Sub-switch:* switches modes; Ctrl+PageUp/PageDown still step the top Overview | Map | Timeline switch, not the sub-switch.
- *Selection:* kept across modes; double-clicking a cell switches to Map with that issue selected after `Drain()`.
- *Expansion strip:* opens on Enter; Esc closes it before the inspector.
- *Covers toggle:* row height changes.
- *Automation:* cell peers carry the expected names.
- *Roll-call:*
  - the click highlights the lane (instant under Reduced)
  - the checkbox hides the series in Map and Matrix
  - "Show all"
  - search appears above 12 series
  - hidden series reset on leaving the screen
- *Progress:* the rail bar and corner label update in place after Mark read; band fill fractions.

**UI checks:** lint; renders of the Matrix (compact and covers), the roll-call panel open, and the progress rail in Default/Daylight/Matrix
at 1366×768; the review checklist.

## Risks

- **Very wide continuity matrices** (many events). Column labels trim, and the pinned series column keeps rows identifiable. There's no
  zoom by design.
- **Relay chunks of 8 are arbitrary.** They're labelled by position range so they don't pretend to be story phases.
- **Shared tree:** builds on the M1 canvas, the M2 layout options and the M4 ghost model (optional; counts just omit "missing" without it).

## Implementation notes (2026-11-12)

Built per `2026-11-11-continuity-matrix-rollcall-plan.md`. Codex wrote the code (builder, drawn Matrix with peers, sub-switch, roll-call,
progress) and stopped before the plan's tests and docs; this pass added those. Where the build differs from the text above:

- **No "missing" counts yet.** `EventMatrixCell.Missing` and the dashed ring exist but stay 0 until M4 supplies ghosts.
- **The sub-switch buttons are plain `segItem`s, not `segToggle`,** so `TabStrip.Step` (Ctrl+PageUp/PageDown) keeps stepping only the
  top Overview | Map | Timeline switch.
- **The corner label** shows "27 / 41 read" in the toolbar beside **Series** and over the wide lane rail; the spine/relay text stays in
  the status line.
- **Verified:** `EventMatrixBuilderTests`, `EventMatrixCanvasTests` and the new `EventMapMatrixModeTests` (mode switching keeps the
  selection, roll-call hide / Show all / reset on leaving / search above 12 / none below, Matrix → Map hand-off) pass, inside a 376-case
  map, Continuity, real-window keyboard and lint run whose only failures are the two pre-existing ones noted in the todo entry. Codex's
  Default/Daylight/Matrix Matrix renders exist; nobody has looked at the Matrix on screen yet.
