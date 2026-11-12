# Continuity & map pitch — room, new views, catch-them-all, structure links, cosmetics (24 items)

Pitch only; nothing built. Asked for 2026-11-11: the map is too small to see every series in an event, so restructure it or redesign
it from scratch; add cosmetics; use what the Series structure work (`2026-11-10-series-structure-and-discovery-design.md`, D1–D4) added.
A quick code survey backs each premise; each item still gets a proper survey before it is specced. ComicRack CE has no events, continuities
or map, so this is all deliberate deviation with no parity to keep (checked for the original Event Map spec, 2026-09-25). Everything uses
tokens (no hex, no oval pills), `PbMotionEase`, the reduced-motion gate and the shared control families.

## Why the map feels small (measured from the XAML, 1366×768)

- **Width.** 1366 − nav rail 64 − contextual sidebar 236 − lane headers 180 (`EventMapView.axaml`, `ColumnDefinitions="180,*"`) −
  inspector 340 = **about 546 px** for cards. At the Standard stop (170 px slots) that is **3 columns**; 5 with the inspector closed.
- **Height.** The page hero (`HeroBand`, padding 22/18 plus title, stats, chips and buttons) stays on screen above the map, then the
  toolbar, then the ruler (50 px on a continuity map). That leaves roughly 450 px: **5 lanes** at Standard (84 px), **2** at Covers (220 px).
- **Relay mode spends a column on every card** (`EventMapLayout`, "strict, so each row gets its own column"). A 60-issue crossover with no
  spine is 60 columns, about 10,000 px, about 19 screens of sideways scrolling. A continuity map is the same thing over every event.
- **No overview.** The minimap and continuous zoom were explicit v1 non-goals (Event Map spec, "Non-goals"). There is no way to see the
  whole shape of an event, or to know which lanes are off screen.

So the problem is three things stacked: chrome eats the viewport, the layout wastes columns, and nothing shows the whole. The pitches
below hit each one; section B is the redesign-from-scratch option.

## Recommended direction

**Do A first (restructure), then add one view from B.** A fixes the viewport and the wasted columns without throwing away the engine,
which already handles spine/relay/block-relay, virtualization, edges, keyboard and the reader hand-off. Of the B views, **#7 Matrix** is
the one that actually answers "show me every series in this event at once": it fits a 40-series crossover on one screen at any size.
C makes the map useful for collecting, D ties in the new character/creator/arc data, E is polish.

## A. Make room — restructure the existing map (1–6)

1. **Map focus mode.** When Map is chosen, the hero collapses to a 44 px strip (name, stats, the Overview | Map | Timeline switch), the
   contextual sidebar slides away, and the toolbar folds into one row. A **Maximize** button (and F11 while the map has focus, as an
   `InputActionIds` action) hides the nav rail too. Returns the full ~1,100 × 650 px to the cards. Cheapest, biggest win.
2. **Inspector as an overlay, not a column.** Today the 340 px drawer pushes the map narrower. Make it float over the right edge
   (`DialogCard`-style surface with shadow), auto-placed on the side away from the selected card, with a pin toggle for people who
   want the old docked behaviour. Optionally a hover **peek** card (cover + title + read state) so the inspector isn't needed just to
   identify an issue.
3. **Slim lane headers.** Shrink the 180 px header column to a 52 px rail: a colour bar, the series' cover thumb or publisher
   `BrandMark`, and a count. Hover or focus expands it over the map to show the full name. Adds about 130 px of cards.
4. **Overview strip (minimap).** A thin full-width strip above the map that draws every card as a 3–4 px tick in its lane colour
   (read ones filled, unread hollow), event bands on top on a continuity map. The visible area is a draggable rectangle; clicking
   anywhere jumps there. One custom-drawn control, cheap because it's just rectangles from the existing layout result.
5. **Semantic zoom with "Fit".** Add two stops below Compact — **Dots** (each card a 10 × 10 square, the whole event fits) and **Chips**
   (badge only, 60 px) — and a **Fit** button that picks the largest stop where everything fits. Ctrl+wheel keeps stepping through stops.
   On a continuity map, double-clicking an event band zooms to fit that band.
6. **Smarter columns in relay mode.** Stop spending a column per card. Rows that can be read in any order relative to each other — tie-ins
   at the same `Position`, or consecutive rows from *different* series between two rows of the same series — share a column, the way spine
   mode already packs segments. A 60-issue relay event typically drops to 20–30 columns. Plus **lane folding**: series with one or two
   issues in the event collapse into a shared "One-shots & short tie-ins · 14 series" lane that expands on click.

## B. Redesign from scratch — alternative views (7–10)

Each is a new segment next to the map ("Map | Matrix | Path"), reusing `EventMapLoader` / `ContinuityMapLoader` data. They don't replace
the swimlane map; they answer different questions.

7. **Matrix (recommended).** Rows are series, columns are **reading phases**: Prologue, then one column per spine issue (each spine
   issue plus the tie-ins that come after it, i.e. the existing spine segments), then Aftermath. In relay mode, phases are groups of about
   8 positions. Each cell is a tiny cover stack with a count and a read ring ("3 · 2 read"), empty cells blank. A 40-series event is a
   40-row table that fits on screen at 24 px rows; click a cell to expand its issues inline or open them on the map. This is the "catch them
   all" view: every series visible, gaps obvious, nothing scrolled off sideways.
8. **Reading path.** A vertical list in exact reading order, grouped into collapsible phase sections with sticky headers and a phase
   jump bar on the left (discrete sections, not one endless scroll). Each row: lane-coloured stripe, cover, series #number, role chip,
   read state, "Ties into Secret Wars #3". The calm "just tell me what to read next" view; also the best fit at small window sizes.
9. **Continuity atlas.** For continuities only: a Gantt-style chart with years along the top and one bar per event (length = its date
   span, thickness = issue count, colour = read progress), relation connectors between bars, and loose-issue density shown as a faint band.
   Clicking a bar zooms into that event's map. Fixes the continuity map's real problem: thousands of issues as one strip of cards.
10. **Subway map (showpiece).** Each series is a coloured line; issues are stations; crossover chapters where lines meet are interchange
    stations. Read stations fill in. Beautiful and very shareable, but auto-layout is genuinely hard; worth a prototype only after 7–9.

## C. Catch them all — completeness (11–15)

11. **Series roll-call panel.** A collapsible left panel listing every series in the event with "4 issues · 3 read · 1 missing",
    sorted by first appearance; click to scroll to and highlight that lane, checkbox to hide it. Directly answers "which series are in
    this and have I got them all?" without scrolling the map.
12. **Ghost cards for issues you don't own.** Event Smart fill (2026-11-04) already fetches provider arc membership as read-only
    evidence. Show the issues it lists that aren't in the library as dashed placeholder cards ("Not in your library") in their lane and
    reading position, with **Add to Wanted**. Display only: membership is never written, per that spec. Off by default, toggled in the
    toolbar.
13. **Progress per lane and per event.** A thin progress bar under each lane header and a completion ring in the corner label
    ("27 / 41 read · 3 missing"). On a continuity map, each event band in the ruler gets its own small progress fill.
14. **Unread-only and "What's next" filters.** A **Hide read** toggle that removes read cards and closes the gaps (filters already
    run before layout, so freed columns collapse), and a "Next 10 to read" mode that shows only the next ten unread issues in order.
15. **Gap check.** A small "Possible gaps" note when a lane skips numbers inside the event (Hulk #92, #93, #95), linking to those issues
    on Detail or to Wanted. Uses the same number logic as the Wanted screen.

## D. Use the new Series structure data (16–20)

16. **Character and team filter.** A toolbar picker fed by the D1 entity index: "Only issues featuring Wolverine" dims everything else
    (or hides it, closing the gaps). The continuity Overview's "Key characters" strip gets an **Open on map** action that opens the map
    with that character selected, and the entity page's Continuities/Events cards get the same link.
17. **Character faces on cards.** At Standard and Covers, a 16–20 px squircle of the issue's main character or team (the D1 cached
    image, never fetched from the card), the same way Detail chips now show them. Turns lanes of near-identical "X-Men #N" into
    something scannable.
18. **Creator colouring and run bands.** A "Colour by" toolbar option: lane (today), writer, artist or read state. With writer selected,
    D2's `CreatorRunDetector` runs draw as a faint bracket along the lane ("Claremont · #94–#279"). Useful on a continuity map in
    publication mode, where lanes are whole series.
19. **Arc brackets inside lanes.** D3's `SeriesArcDetector` arcs drawn as labelled brackets under a lane's cards in publication mode
    and in the between-events blocks ("The Dark Phoenix Saga · #129–#138"), so loose issues aren't just a row of numbers.
20. **Lane badges from series relations, and a second spine signal.** The series relation connector (2026-10-29) stores `TieIn`,
    `MainEventSeries` and companion relations; badge lanes with theirs ("tie-in", "companion", "main event"). Caveat found in the survey:
    the connector derives its automatic `MainEventSeries` links *from* the spine rule (`Paperbunkr.Data/Metadata/EventSpineResolver.cs`),
    so only user-made or Metron-sourced ones are new information. Those, plus "the series every tie-in links to", could rank ahead of the
    name match, so fewer events fall back to relay, the layout that sprawls the most.

## E. Cosmetics (21–24)

21. **Read path line.** The chain/sequence edges up to your last-read issue draw solid in the accent; the rest stay thin and faded.
    A pulsing "You are here" ring sits on the next unread card (static under reduced motion). Progress becomes the shape of the map.
22. **Event band art and lane covers.** Ruler bands on a continuity map get a soft wash of the event's cover (`StoryEventCoverCache`,
    `HeroWash`-style blur, skin-tinted); lane headers show the series cover thumb. Era colours from the Timeline view tint the ruler's
    year row.
23. **Trace on select.** Selecting a card animates its connections outward (tie-in edges draw in along their path, related cards
    brighten in sequence) over `PbMotionStandard`, instead of the instant dim. Off under reduced motion.
24. **Export as poster.** "Save map as image" renders the whole map (or the Matrix) to a PNG at Covers or Standard density with the
    event title and a small Paperbunkr mark — a shareable checklist, and good material for the brag/social pipeline.

## Suggested slicing (if this goes ahead)

| Slice | Items | Why together |
|---|---|---|
| M1 Room | 1, 2, 3, 4, 5 | All viewport and chrome. **Implemented in the working tree, pending on-screen review** as [continuity-map-room-design](2026-11-11-continuity-map-room-design.md) (approach C: one drawn map control, smooth zoom) |
| M2 Layout | 6, 14, 20 | **Implemented in the working tree, pending on-screen review:** [continuity-map-layout-design](2026-11-11-continuity-map-layout-design.md) (wave packing, folding, Hide read, user-relation spine signal) |
| M3 Matrix + roll-call | 7, 11, 13 | **Implemented in the working tree, pending on-screen review:** [continuity-matrix-rollcall-design](2026-11-11-continuity-matrix-rollcall-design.md) (Map sub-switch, Matrix, roll-call, progress) |
| M4 Completeness | 12, 15 | **Implemented in the working tree (on the uncommitted Smart fill code), pending on-screen review:** [continuity-map-completeness-design](2026-11-11-continuity-map-completeness-design.md) (arc snapshot cache, ghosts, Wanted, gap check) |
| M5 Structure links | 16–19 | **Implemented in the working tree (on the uncommitted D1–D3 code), pending on-screen review:** [continuity-map-structure-links-design](2026-11-11-continuity-map-structure-links-design.md) |
| M6a List + Atlas | 8, 9 | **Implemented in the working tree, pending on-screen review:** [continuity-list-atlas-views-design](2026-11-11-continuity-list-atlas-views-design.md) |
| M6b Polish | 21–24 | **Implemented in the working tree, pending on-screen review:** [continuity-map-polish-design](2026-11-11-continuity-map-polish-design.md) (no era tint: the ruler has no year row) |
| — | 10 Subway map | Not specced; prototype idea only (Q46) |

Item 15 (gap check) moved into M4. Pitch premises corrected by the 2026-11-11 survey: reading positions never tie (so packing uses
"waves", not ties); provider arc lists are not stored anywhere (M4 adds a cache); automatic `MainEventSeries` relations don't exist
(only the user's own relations are a new spine signal); `MainCharacterOrTeam` is plain text, not an indexed entity.

**Build order:** M1 → M2 → M3 → M6a (needs M3's sub-switch) → M6b; M4 after the Smart fill code is committed; M5 after D1–D3 are
committed.

## Open questions for the design round

- Matrix phases in relay mode: fixed groups of positions, or use event roles plus cover-date months?
- Should focus mode (#1) be automatic whenever Map is chosen, or only on Maximize?
- Ghost cards (#12): fetch on map open when Smart fill evidence is stale, or only show what Smart fill already has?
- Is the subway map (#10) wanted at all, given the layout cost?
