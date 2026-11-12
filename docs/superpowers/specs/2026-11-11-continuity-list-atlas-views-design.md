# Continuity map M6a "List and Atlas views" — design

*Status: approved by the user 2026-11-11 (grilling round 4, Q43–Q46, with visual-companion screens). Source: slice M6 of the
[continuity map pitch](2026-11-11-continuity-map-pitch.md) (items 8, 9; item 10, the subway map, stays a prototype idea with no spec).
Plugs into the Map sub-switch from [M3](2026-11-11-continuity-matrix-rollcall-design.md). Implemented in the working tree at base HEAD
`28e60a1` (2026-11-12); on-screen review pending.*

## Goal

Two more ways to look at the same data, inside the Map sub-switch:
- **List:** a calm, sectioned reading-order list that works at any window size.
- **Atlas** (continuities only): a chart of the continuity's events across the years.

## Facts this rests on (verified 2026-11-11)

- **The sub-switch** `Map | Matrix | List | Atlas` lives on `EventMapViewModel.MapMode` (M3). Selection, filters, Hide read (M2), the
  roll-call (M3), Featuring (M5) and Show missing (M4) are shared across modes.
- **Event dates:**
  - `EventChronology.LoadSpans(context, eventIds?, gcd?) → Dictionary<int, EventSpan>`, with
    `EventSpan(EventId, Name, int? Start, int? End, SortKey?, Rank?)` as `year*100+month` ints (month 0 = unknown). Priority: date
    endpoints, then the event's own dates, then member min/max (GCD on-sale dates when the store is given).
  - `EventChronology.Order(spans, relations)` gives chronological order.
  - `LoadRelations` and `Direction` give the directional relations.
  - There's no confidence field and no "next event" API (`Data/Metadata/EventChronology.cs:14, 25, 41, 193, 306`).
- **Pinned headers:** Avalonia has no sticky headers. The app's pattern is `PinnedGroupHeader` + `UpdatePinnedGroupHeader` on
  `ScrollChanged` (`Views/LibraryScreen.axaml.cs:84-163`, `BooksScreen.axaml.cs:96-164`).
  - The jump-style controls are the A–Z `AlphabetRail` (`LibraryScreen.axaml:1938-1954`) and the Timeline histogram's `JumpToYearCommand`.
  - There's no generic section-index control.
- **Reading-list chapters** (`ReadingListPageView.axaml:433-449`) are collapsible but not pinned.
- **Virtualizing lists of focusable rows** must take arrow keys on the tunnel (CLAUDE.md, `WantedScreen`), and are tested in the real
  window.
- The user dislikes a single endless scroll for navigation (memory, 2026-09-07). The List uses sections and a jump bar.

## Decisions

| # | Decision |
|---|---|
| Q43 | Both views live in the Map sub-switch. Atlas shows on continuity scope only. |
| Q44 | List: collapsible sections with a pinned header and a jump bar with progress. Sections are spine chapters, events or groups of 10. |
| Q45 | Atlas: a bar per event placed by its dates. Thickness is issue count (3 sizes), the fill is read progress, plus relation connectors and a loose-issue band; clicking opens that event's map. |
| Q46 | No subway-map spec. |

## Design

### 1. List view

**Sections** (pure, `EventListBuilder.Build(layout, scope) → EventListModel`):
- *Spine event:* one section per segment. "Before" (if any), then "Prologue" / "Chapter 1 · Secret Wars #1" … "Epilogue", from the
  segment's trunk card.
- *Relay event:* groups of 10 reading positions ("1–10", "11–20").
- *Continuity:* one section per block, with the event name or "Between · 2004–06" / the year.
- Each section has its rows in exact reading order (ghosts from M4 included when Show missing is on), plus counts (n, read, missing).

**Layout:**
- **Jump bar:** a 180 px column on the left, a plain `ItemsControl` of `Button.sideItemButton` rows. Each row has the section label
  (`sideItemLabel`, trimmed) and a `countPill` "3/5".
  - The current section (the one under the pinned header) carries `.active`.
  - Clicking scrolls its header to the top (glide on `Travel`, instant under Reduced).
  - Fully read sections show a Checkmark.
- **List:** a virtualizing `ListBox` whose items are section headers and rows (one flat item list, headers as their own item type).
  - Collapsing a section removes its rows from the item list, a deferred change per CLAUDE.md's detach rule.
- **Pinned header:** the `PinnedGroupHeader` pattern copied from `LibraryScreen`: an overlay header showing the section the top visible
  row belongs to, updated on `ScrollChanged`.
- **Header row:** chevron (`chevronToggle`), label (`pbTextSection`), counts, and a "Read this section" `Button.ghost sm` that opens the
  first unread issue with the event anchor (the event scope's reader hand-off) or unanchored for loose continuity issues.
- **Issue row** (56 px, 8 px vertical padding):
  - a 3 px lane-colour stripe and a 32 × 48 cover (M1 cover source)
  - "Series #Number" (`pbTextBody`) and the title (`pbTextMeta`, trimmed)
  - a role chip (`pbChip`: core, tie-in, prologue…)
  - "ties into Secret Wars #3" (`pbTextCaption`, spine events only)
  - the cover date and the read glyph
  - ghost rows draw with a dashed border and "Not in your library"
- **Selection:** clicking a row selects it (shared selection) and opens the inspector; double-clicking opens the reader.

**Keyboard** (handled on the tunnel, as `WantedScreen` does):
- Up/Down move between rows and headers; Page Up/Down; Home/End.
- Enter opens the inspector (on a header: toggles it); Ctrl+Enter opens the reader.
- Left on a row goes to its header; Left/Right on a header collapse/expand.
- Esc follows the map's Esc chain.

**Accessibility:** rows are real controls, named "{Series} #{Number}, {role}, {read state}"; headers are named "{label}, {r} of {n} read,
expanded/collapsed".

**Responsive:** below 900 px the jump bar collapses to a 44 px column of progress dots with tooltips.

### 2. Atlas view (continuity only)

**Model** (pure, `ContinuityAtlasBuilder.Build(spans, order, relations, rows, readStates) → Atlas`):
- **Events:** those touching the continuity (minus hidden ones), with `EventSpan` dates.
  - An event whose month is 0 spans its whole year.
  - An event with only Start or only End is drawn as a 1-month bar.
  - Undated events go to an "Undated" list at the end.
- **Rows:** greedy interval packing in `EventChronology.Order` order (an event takes the first row where it doesn't overlap the previous
  bar plus a 2-month gap), so the chart is as short as possible.
- **Thickness:** issue-count terciles across the continuity's events give 6 / 10 / 14 px.
- **Read fraction:** from the event's member cards (live read states).
- **Connectors:** directional relations between visible events (`EventChronology.Direction`): Prequel, Sequel, Continuation. A thin
  curve from the earlier bar's end to the later bar's start; solid for the user's relations, dashed for inferred/Wikidata, as on today's
  continuity ruler.
- **Loose-issue band:** a 16 px histogram along the bottom of loose (between-block) issues per year, in `PbAccentSoftBrush`.

**Drawing** (`ContinuityAtlasCanvas`, on M1's drawn foundation):
- **Time axis:** years along the top, pinned, with the year labels thinning as space shrinks.
- **Bars:** a `PbRadiusSm` squircle in the event's lane-palette colour (by chronological index). The read part is solid; the unread part
  is the same colour at 30 %.
- **Labels** sit to the right of the bar, or inside it if it's wider than the label; they trim when they'd overlap the next bar.
- **Zoom:** horizontal only, through M1's zoom helper on the x axis (Ctrl+wheel, slider hidden here, Fit button).
  - The default fits the whole span; very long continuities (60+ years) start at 1 px per month and scroll.
- **Hover:** a tooltip "Civil War · 2006–07 · 104 issues · 60 % read". The hovered bar's connectors brighten.
- **Click / Enter:** opens that event on the Continuity screen in Map view (the existing band-click navigation), keeping Maximize state.
- **Keyboard:** Left/Right move between bars in chronological order, Up/Down move to the nearest bar in the row above/below. One focus
  stop, with the focus ring drawn on the bar.
- **Accessibility:** virtual peers per visible bar, named as the tooltip.
- **Undated:** a small "Undated · 3" chip at the right end of the axis, opening a `Border.dropdown` list of those events.

### 3. Shared behaviour

- **Filters:**
  - Hide read and Featuring apply to the List's rows.
  - In the Atlas they dim bars whose events have no remaining issues.
  - Hide optional applies to List rows only.
  - The Events picker hides bars.
- **The roll-call panel (M3)** works with the List (click scrolls to the series' first row). In the Atlas it's disabled, since series
  aren't shown there.
- **Session memory:** mode, list collapse state per map, and the Atlas zoom are kept on `ContinuityScreenViewModel` for the session.

## Testing

**Pure:**
- `EventListBuilder`:
  - spine/relay/continuity sections
  - counts
  - ghost rows included
  - the read-section checkmark rule
- `ContinuityAtlasBuilder`:
  - month-0 spans
  - one-sided dates
  - undated
  - greedy packing minimal-row cases
  - terciles
  - connector direction for Prequel/Sequel/Continuation
  - the loose-issue histogram

**Headless and real window:**
- *List:*
  - jump-bar click scrolls (instant under Reduced)
  - the pinned header follows scrolling
  - collapse is deferred and doesn't throw (detach rule)
  - Up/Down across a header in `RealWindowKeyboardTests`
  - "Read this section" opens the first unread with the event anchor
- *Atlas:*
  - click opens the event map
  - Left/Right order
  - tooltip text
  - Fit
- *Modes:* selection kept between List and Map.

**UI checks:** the lint set; renders of the List (spine event, continuity) and the Atlas (a 30-year continuity) in three themes at
1366×768; the review checklist.

## Risks

- **Event dates can be sparse** (no explicit dates, undated members). The Atlas shows those in "Undated" rather than guessing.
- **Pinned-header code is copied from Library/Books.** If a third copy appears, extract a shared helper in this slice instead.
- **Shared tree:** the Map sub-switch from M3; build after it.

## Implementation notes (2026-11-12)

Built directly from this design (no separate plan file; it followed M3's sub-switch). Code: `Services/EventMap/EventListBuilder.cs`,
`ContinuityAtlasBuilder.cs`, `ViewModels/EventMapViewModel.List.cs` / `.Atlas.cs`, `Views/EventListView.axaml(.cs)`,
`Controls/EventMap/ContinuityAtlasCanvas.cs` (with its automation peers). Where the build differs from the text above:

- **List keyboard:** the rows are a `ListBox`, which moves its own selection with Up/Down (no tunnel handler was needed: the
  directional-focus problem in CLAUDE.md is an `ItemsControl` of focusable rows). Enter on a row opens the inspector, Ctrl+Enter the
  reader; Enter/Space on a header toggles it; Left on a row selects its header; Left/Right on a header collapse/expand.
- **Counts update in place.** A Mark read changes the header and jump-bar counts and the **Next up** marker on the existing items; the
  list is only rebuilt when its sections, rows or collapsed set change, so the selection and scroll position survive.
- **Collapse state** lives on the map view model and resets when a different event or continuity is loaded, not on
  `ContinuityScreenViewModel`. The Atlas zoom is not remembered between visits (it fits on first show).
- **Narrow windows:** below 900 px the jump bar becomes a 44 px strip of progress pills/checkmarks with the label in a tooltip.
- **Atlas zoom** is the canvas's own (Ctrl+wheel about the pointer, **Fit atlas** button), not M1's zoom helper; the axis labels thin
  out (1/2/5/10/20/50-year steps) and the left-edge year is pinned.
- **Atlas connectors** dip below the bars instead of running from bar end to bar start on the centre line, so they never cross a label.
- **"Undated"** is a toolbar menu (shown in Atlas mode when there are undated events), not a chip at the end of the axis.
- **Filters on the Atlas:** the Events picker hides bars; Hide read and Featuring do not dim bars yet (Featuring is M5's).
- **Verified:** `EventListAtlasTests` (13: spine/relay/continuity sections, ties-into, deferred collapse, in-place counts and Next up,
  Read this section, Atlas only on continuities, month spans, chronology/rows/connectors/loose band, overlap stacking and undated,
  terciles, keyboard and peers) pass; `EventMapViewsRenderTests` rendered List and Atlas in Default/Daylight/Matrix at 1366×768 and
  the PNGs were checked by eye (that check led to bigger section chevrons, the dipped connectors and the pinned first year). Not
  verified: on screen, Narrator, a real long continuity.
