# Continuity map M2 "Layout" — design

*Status: approved by the user 2026-11-11 (grilling round 4, Q22–Q27, with a visual-companion screen). Source: slice M2 of the
[continuity map pitch](2026-11-11-continuity-map-pitch.md) (items 6, 14, 20). Builds on [M1 Room](2026-11-11-continuity-map-room-design.md).
Implemented in the working tree at base HEAD `28e60a1`; on-screen review is still pending.*

## Implementation verification (2026-11-11)

The map and continuity checks plus the relevant UI lint set passed 306/306 (excluding the documented unrelated ComicVine button-size lint failure); the final map and continuity run passed 282/282. Relation/connector and migration checks passed 53/53, with a further two checks covering the generated migration and connector integration. The `AddEventMapRelayPacking` migration defaults existing settings to packed and its Down drops only that column; it was tested on a synthetic database, not applied to the user's database. Theme renders were inspected, but physical input, screen reader and on-screen feel still need review. M1's user sign-off and legacy cleanup remain pending.

## Goal

Stop no-spine events and continuity maps sprawling sideways, fold the clutter of one-issue tie-ins, let the user hide what they've read,
and use the user's own series relations to find an event's main series.

## Facts this rests on (verified 2026-11-11)

- **Relay spends a column per row.** `EventMapLayout.ComputeRelay` (`Services/EventMap/EventMapLayout.cs:157-181`):
  `rows.Select((row, i) => new EventMapCell(i, row, laneTrack[row.SeriesId], i, 0, IsTrunk: false))`. Chain edges join consecutive rows.
  Block relay (`ComputeBlocks`, :191-272) gives Event and Unplaced blocks one column per row. Between and Year blocks are already packed
  by month.
- **Reading positions never tie in practice.** No unique index (`PaperbunkrDbContext.cs:1660`), but every writer produces strictly
  increasing positions:
  - `EventMembershipResolver.AddMember` uses max+1
  - arc import uses `nextPosition++`
  - `StoryEventMerger` renumbers
  - `Reorder` swaps

  So packing can't rely on ties.
- **No lane folding** exists on the map. The filters are `EventMapFilter { All, SpineOnly, HideOptional, EventsOnly }`
  (`EventMapModels.cs:129-141`). On the event map they're applied in `Compute`; the continuity map uses
  `ContinuityMapOptions(HiddenEventIds, HideOptional, EventsOnly)`. There's no hide-read anywhere on the map.
- **Read state** is `ReadStateOf` (0 % Unread, `HasBeenRead()` Read, else InProgress; `EventMapLoader.cs:45-53`), live on
  `EventMapCardViewModel.ReadState`.
- **Series relations and where they come from:**
  - **Types:** `RelationType` has `TieIn` (source is a tie-in to target's event) and its inverse `MainEventSeries`
    (`RelationType.cs:48-54`). A "companion" is `SideStory` (`CompanionSuffixInference.cs:85`).
  - **Providers** (`RelationEvidence.Provider`): `User, Other, Inferred, Wikidata, Gcd, Metron`.
  - **Automatic TieIn:** `EventTieInInference` writes `TieIn` with Provider `Inferred`, the event id in `ProviderSourceId` and the spine from
    `EventSpineResolver.Resolve`. It is derived *from* the spine.
  - **MainEventSeries** is never written automatically; only the user can pick it.
  - **Metron** contributes only `Related` ("associated").
- **The spine resolvers:** `Paperbunkr.Data/Metadata/EventSpineResolver` returns `int?`. The App `SpineResolver` wraps it into
  `SpineChoice(SeriesId, Source User/Auto/None, AutoMatchSeriesId)`. Both treat `0` as force-relay and a saved member spine as winning.
- ComicRack CE has no events or map; no parity applies.

## Decisions

| # | Decision |
|---|---|
| Q22 | **Wave packing** for relay: the next row joins the current column unless its lane already has a card there. On by default; a "One per column" toggle restores today. |
| Q23 | The same rule applies inside each Event block on a continuity map. |
| Q24 | With 8+ lanes, series with 1–2 rows fold into one "Short tie-ins · N series" lane; cards keep their own colour and badge; click to unfold. |
| Q25 | A **Hide read** toolbar toggle, combinable with the filters; removes read cards and closes the gaps. |
| Q26 | No separate "next 10" mode. |
| Q27 | The user's own `TieIn`/`MainEventSeries` relations become a spine signal between the saved pick and the name match. Lanes carry relation labels (tie-in, side story, main) from any provider. |

## Design

### 1. Wave packing

**Rule** (pure, in `EventMapLayout`): walk rows in `(Position, MembershipId)` order with a current column `c` and the set of tracks used
in `c`.
- If the row's track is already in the set, start a new column (`c + 1`) and clear the set.
- Place the row at `c` and add its track.
- Columns stay monotonic in reading order. A row is never placed left of its predecessor.

**Where it applies:**
- relay mode (`ComputeRelay`)
- Event and Unplaced blocks in `ComputeBlocks`

Spine mode keeps its existing segment packing, which is already compact. Between and Year blocks keep month packing.

**Edges.** Chain edges still join consecutive rows. A new path shape covers consecutive rows in the **same column**: a vertical hop from
source bottom-centre (or top-centre, if the target is above) to target top-centre (or bottom-centre), with radius-7 corners, in the muted
chain colour. The existing hop shape (right-centre → mid-gutter → left-centre) stays for column changes.

**Ruler labels.** A packed column shows its position range ("4–6"). A single-row column shows its number, as today.

**Navigation.** `Next`/`Prev` stay in row order, so the arrow keys still follow exact reading order, which may now step down a column.
`NearestInLane` uses the packed columns.

**Toggle.**
- The toolbar gets a "Packed" `segItem` pair (Packed | One per column), shown only when the map has relay rows (relay event or any
  continuity map).
- The choice is persisted as `AppSettings.EventMapRelayPacked` (bool, default true).

### 2. Lane folding

**When.**
- After filters, a map with **8 or more lanes** folds every series lane that has **1 or 2 rows** and isn't the trunk.
- The folded series share one track, placed **last**, labelled "Short tie-ins · N series".
- With fewer than 3 such series nothing folds, since folding two lanes saves too little to be worth it.

**Placement.**
- In the folded track each row keeps its column from the normal pass.
- A collision (two folded series wanting the same column) bumps the later row to the next free column. In relay packing that means
  re-running the wave rule with the folded track counted as one track.

**Cards.**
- Each card keeps its series' own colour index (`EventMapCell` gains `ColorIndexOverride`, since the track no longer gives the colour)
  and always shows its badge ("WW #1") at Chips and above.
- In spine mode the tie-in edge goes to the first row of each *series* in the segment, as before, even inside the folded track.

**Unfolding.**
- The folded lane's rail cell draws a ChevronRight/ChevronDown and is a hit target; clicking it toggles.
- Keyboard users get "Unfold short tie-ins" / "Fold short tie-ins" in the map toolbar's More menu.
- The state is per map and lasts for the session (kept on `ContinuityScreenViewModel` by map key). Default is folded.

The roll-call panel (M3) lists folded series individually.

### 3. Hide read

**The toggle.** A `Button.secondary` toggle ("Hide read", FluentIcon EyeOff) in the toolbar, independent of the segment filters:
- `EventMapLayout.Compute` gains `hideRead` (event map)
- `ContinuityMapOptions` gains `HideRead` (continuity map)

Read rows are dropped before layout, so freed columns close up.

**Behaviour.**
- `FirstUnread` becomes the first card.
- If no cards are left, the map shows "Everything here is read" with a "Show read" ghost button.
- **Mark read while hiding:** the card stays visible and selected until the selection moves or the inspector closes. Then the layout
  recomputes once, deferred through `Dispatcher.UIThread.Post` (CLAUDE.md "don't detach inside the routed event").
- The toggle lasts for the session, per scope (event / continuity), on `ContinuityScreenViewModel`.

### 4. Relations on the map

**Spine signal (Data, `EventSpineResolver`).** A new step between "saved spine" and "name match":
- Consider only relations whose evidence includes `Provider == User`.
- A member series S is a candidate when another member series has a user `TieIn` → S, or S has a user `MainEventSeries` → another member
  series.
- The candidate with the most such links wins, then the one with the most rows, then the one that appears first.

Inferred, Wikidata, GCD and Metron evidence is ignored here. The automatic `TieIn` rows are derived from the spine, so using them would be
circular.

**App `SpineResolver`.** It gains `Source = Relation`. The status text reads "spine: {series} (from your relations)", and the spine picker
marks that entry " (from relations)". The relation connector keeps calling the Data resolver, so its inferred tie-ins follow the new
signal too.

**Lane labels.**
- `EventMapLoader` (and `ContinuityMapLoader`) load, for the map's series, relations to the spine series (event map) or to other lanes'
  series (continuity map), from any provider:
  - `TieIn` (lane → spine) gives "tie-in"
  - `SideStory` gives "side story"
  - the spine lane, or a user `MainEventSeries`, gives "main"
- `EventMapTrack` gains `RelationLabel` (`string?`). It's drawn as a `pbChip`-style label after the name in wide lane headers and in the
  slim rail's hover flag.
- It isn't drawn in the slim rail itself.

## Testing

**Pure (`EventMapLayoutTests`, new cases):**
- *Wave packing:*
  - the companion sample (12 rows, 4 lanes → 5 columns)
  - monotonic columns
  - a same-lane repeat forces a new column
  - one-per-column reproduces today's exact columns
  - Event blocks packed and Between/Year blocks unchanged
  - position-range labels
- *Edge paths:* vertical same-column hop (up and down) exact points.
- *Folding:*
  - 8-lane threshold
  - 3-series minimum
  - trunk never folds
  - folded track last
  - colour override per series
  - collision bump
  - tie-in edges per series
- *Hide read:*
  - columns close up
  - FirstUnread falls back to the first card
  - the empty state
  - a continuity `HideRead`
- *Navigation:* Next/Prev follow row order across a packed column.

**Data:**
- `EventSpineResolver`:
  - a user TieIn picks the spine
  - user MainEventSeries
  - inferred-only relations are ignored
  - the saved spine still wins
  - force-relay still wins
  - the tie-breaks
- Migration: forward-only test for `EventMapRelayPacked` (existing rows read true via the default). Check the generated `Down()` drops
  only this column.

**Headless:**
- the Packed toggle relayouts and persists
- clicking a folded lane unfolds it
- the More-menu fold item works
- Mark read under Hide read keeps the card until the selection moves (after `Drain()`)
- the status text shows the "from your relations" source
- lane labels draw in the wide headers

**UI checks:** the lint set, renders of a packed relay event and a folded lane in three themes, and the review checklist.

## Risks

- **Reading order in a packed column isn't left-to-right.** The chain line and Next/Prev carry the order; the inspector's Follows / Leads
  to links still say it in words. If users find it confusing, the toggle restores one-per-column.
- **The spine signal changes inferred tie-ins** for events whose spine was a name match and now comes from user relations. The connector's
  sweep already replaces stale inferred relations.
- **Shared tree:** `EventMapLayout`, the loaders and the M1 canvas. Build after M1.
