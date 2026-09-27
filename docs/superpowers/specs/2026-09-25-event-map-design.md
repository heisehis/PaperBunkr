# Event Map (swimlane reading-order view for Story Events) — design

*Status (2026-09-27): **built, not committed, not yet seen on screen** - plan in `2026-09-25-event-map-plan.md`,
deviations in "Implementation notes" at the end.*

*Original status: draft for user review, 2026-09-25. Grilled with the user the same day in four rounds (Q1–Q23) plus three
visual-companion screens (column packing, density stops, relay layout). Every decision below was either
recommended-and-accepted or explicitly chosen. Replaces the "Continuity Nexus" idea from an external Gemini
brainstorm and a v0 web mockup; where this spec differs from those, this spec wins.*

## Goal

Give a Story Event a map view: a horizontal swimlane timeline in which the event's issues sit on a fixed
grid, with one lane per series and, when the event has its own "spine" series, a trunk lane for it. Lines between
cards show reading order and tie-ins. Selecting a card opens an inspector; opening the reader from the map reads the
event in its own order.

## Facts this design rests on (verified in the repo, 2026-09-25)

- **Continuity is series-level only.** `Continuity` groups series through `ContinuityMembership`; it has no issue
  order. `ReadingOrderResolver`'s own doc says so. An ordered map therefore needs a `StoryEvent`.
- **Events are ordered and carry roles.** `EventMembership` has `Position` (no unique index, so ties are possible)
  and a non-nullable `Role` (`Prologue, Core, TieIn, Epilogue, Optional, Aftermath`).
- **`Core` is a default, not a signal.** The event-suggestion accept paths and the event screen's role picker
  (`EventsScreenViewModel.cs:191`) default to `Core`. The uncommitted role-detection work
  (`2026-09-25-reading-list-role-detection-design.md`) keeps writing `Core` with `RoleSource = User` when it detects
  nothing, and legacy rows (null source) are also treated as `User`. "User chose Core" and "nobody looked" can't be
  told apart. `Prologue`, `Epilogue` and `Optional` are never defaults, so they are meaningful when present, and
  `Optional` is only ever set by the user.
- **No link records which core issue a tie-in ties into.** Tie-in attachment has to be derived from order.
- **The Events screen switches views, not modes.** After the 2026-08-28 redesign an event's detail pane has a
  segmented **Members | Timeline** toggle (`EventsDetailView`). `EventsScreenMode` is only referenced from a doc
  comment now, so it's dead code. The detail content sits inside a vertical `ScrollViewer` (`EventsScreen.axaml`,
  row 2).
- **The reader's event context is ambiguous, and its paging never follows events.** `ReadingOrderResolver.ResolveContext`
  picks the *lowest-id* event containing the issue when no reading list is given. `ResolveNeighbour` walks the reading
  list or the series and deliberately never switches to event order. `NavigateToReaderCore(issueId, readingListId,
  startPage)` → `Reader.LoadIssue` stores `_activeReadingListId`. `ResolveNeighbour` is called from three places in
  `ReaderScreenViewModel`: manual next/previous, the staged next-issue pipeline, and the end card.
- **The app already has virtualized panels.** `VirtualizingWrapPanel` and `VirtualizingVariableWrapPanel` use
  `EffectiveViewportChanged`. `CoverImageCache` offers `TryGetCached` plus `DecodeFromDisk`, which is safe off the UI
  thread.
- **The skin system updates these brushes live:** `PbAccent`, `PbAccentText`, `PbChartBlue`, `PbChartViolet`,
  `PbSuccess`, `PbBadge`, `PbDanger`, `PbChrome`, `PbBorder`, `PbBg`. `ThemeService.ApplySkinResources` updates them.
- **Read state:** `Issue.ReadPercentage()` and `HasBeenRead()` in `IssueMetadataExtensions`.
- **`TitleNormalizer.NamesMatch(a, b, ignoreVolume)`** exists (CE-cascade port).
- **ComicRack CE has no events and no map.** This is a Paperbunkr-only feature with no CE parity to preserve.
- **No graph library fits.** GraphX (Westermo fork), AvaloniaGraphControl/MSAGL, GoDiagram, Nodify.Avalonia and
  NodeEditorAvalonia are all auto-layout or node-editor libraries. Positions here are fully determined by the data,
  so the view is custom. No new packages are needed.

## Non-goals (v1)

- A continuity-wide map. The layout engine is scope-agnostic, so this can be added later.
- Editing on the map (roles, positions, membership). Editing stays in the event's Members view.
- `EventRelation` edges between events.
- A minimap, "group by era" (the Timeline view already covers ages), and a "recommended order" picker (the event's
  `Position` already is the recommended order).
- Continuous zoom. Zoom snaps to three density stops.
- Insights changes. Insights stays on ScottPlot. A reusable swimlane for per-series reading history is a possible
  later idea, not part of this work.

## Design

### 1. Data and the layout engine

**Loading.** `EventMapLoader.Load(context, storyEventId)` in `Paperbunkr.App/Services/EventMap/` runs one query off
the UI thread and returns a plain `EventMapSource`:
- the event name and its `SpineSeriesId`
- one row per membership with:
  - `MembershipId`, `IssueId`, `Position` and `Role`
  - `SeriesId`, series name and the issue number label
  - `Year`, `Month` and `FileIsMissing`
  - `ReadState`: `Unread` when `ReadPercentage` is 0, `Read` when `HasBeenRead()`, otherwise `InProgress`

Rows are sorted by `(Position, MembershipId)`.

**Choosing the spine.** `SpineResolver.Resolve(source)` is a pure function that returns `(SpineSeriesId?, Source:
User | Auto | None)`:
1. If `StoryEvent.SpineSeriesId` is set and that series is still a member, use it (**User**).
2. Otherwise, use the member series whose name matches the event name via `TitleNormalizer.NamesMatch(eventName,
   seriesName, ignoreVolume: true)` (**Auto**). If several match, pick the one with the most rows, then the one that
   appears first.
3. Otherwise, **None**, and the map uses the relay layout.

A spine is never picked because a series has the most issues. On the relay screen, that fallback drew full chapters
of a crossover as tie-ins. The user rejected it.

**Filters** apply before layout, so hidden rows free up their columns:
- **All**
- **Spine only:** trunk rows only. Only offered in spine mode.
- **Hide optional:** drops rows whose role is `Optional`.

**Layout.** `EventMapLayout.Compute(source, spine, filter, densityStop)` is pure and returns `EventMapLayoutResult`.

*Spine mode:*
- **Trunk (track 0):** every row of the spine series, plus any row whose role is `Prologue` or `Epilogue`, whatever
  its series. `Core` never affects placement.
- **Lanes (tracks 1..N):** one per remaining series, ordered by that series' first `Position`. A series with no
  rows left after trunk promotion gets no lane.
- **Columns (compact packing, chosen on screen 1):** walk the rows in order.
  - A trunk row takes `maxColumnUsed + 1` and starts a new segment at `column + 1`.
  - A lane row takes `max(segmentStart, that lane's next free column)` and advances that lane's next free column.
  - Rows before the first trunk row form segment 0 (start 0), which has no anchor.
- **Edges:**
  - `Sequence`: consecutive rows within one lane, and consecutive trunk rows.
  - `TieIn`: from a segment's trunk row to the *first* row of each lane in that segment. Rows in segment 0 get none.
  - `Continuity` (drawn faint): from a promoted `Prologue` or `Epilogue` on the trunk to the next row of its own
    series in that series' lane, if one exists.

*Relay mode (no spine; chosen on the relay screen):*
- One lane per series, ordered by first `Position`. There is no trunk lane.
- **Columns:** strict, so each row gets its own column (`column = row index`).
- **Edges:** a single `Chain` edge between each pair of consecutive rows. It's drawn straight within a lane and
  routed orthogonally when the chain hops lanes.

**Density stops (chosen on screen 2):**

| Stop | Slot width | Track height | Card | Content |
|---|---|---|---|---|
| Compact | 118 | 44 | 100 × 30 | badge (e.g. "AB #2") and read state |
| Standard (default) | 170 | 84 | 150 × 64 | small cover, series #number, cover date, read state |
| Covers | 150 | 220 | 124 × 196 | full 2:3 cover, then badge and read state |

A card sits centered in its column × track cell. The total size is `columns × slotWidth` by `tracks × trackHeight`.

**Everything else the result carries, all computed here so it can be unit-tested:**
- **The visible range:** a viewport maps to `[firstColumn, lastColumn]`, with one column of overscan on each side,
  clamped to the grid.
- **Navigation:**
  - `Next` and `Prev` in reading order (row order)
  - `NearestInLane(track ± 1, column)`: the card in the neighbouring track with the closest column, the earlier
    column winning a tie
  - `First` and `Last`
  - `FirstUnread`: the first unread trunk card in spine mode, or the first unread card in relay mode, falling back to
    the first card
- **Inspector links:**
  - *Spine mode:* **Follows** and **Leads to** are the previous and next rows in the same lane (or on the trunk), and
    **Ties into** is the segment's trunk row. **Segment order** is every row in the segment in exact `Position` order.
  - *Relay mode:* **Follows** and **Leads to** are the chain neighbours, and **Previous in series** is the previous
    row of the same series.
- **The related set used for dimming:**
  - *Spine mode:* the selected card's whole lane, its segment's trunk card, and that trunk card's trunk neighbours.
  - *Relay mode:* the selected card's chain neighbours and its whole lane.
- **Edge geometry:** pure path builders return point lists with rounded-corner radius 7.
  - *TieIn:* trunk card bottom-centre → straight down that column → along the target lane's top gutter → down into
    the target card's top-centre.
  - *Chain hop:* source right-centre → the mid-gutter between the two columns → vertical to the target row →
    target left-centre.
  - *Sequence:* a straight line from right-centre to left-centre.

**Edge cases:**
- **No members:** an empty result.
- **Spine mode with no other series:** only the trunk, which is an ordered strip.
- **Missing file:** laid out as normal, with a flag. It is never dropped.
- **Stale `SpineSeriesId`:** ignored in favour of the auto rule. It isn't cleared.

### 2. Schema

One additive migration adds a nullable `StoryEvent.SpineSeriesId` (int, no FK, no index; the map only ever reads it for the one event it's showing). If
`20260925160001_AddRoleDetectionColumns` from the role-detection work is present, this migration has to be ordered
after it.

### 3. Rendering (approach B: a custom-drawn edge layer plus virtualized real card controls)

```
EventMapView (UserControl)
Grid 2×2
├─ [0,0] corner label ("Spine: …" / "Relay")
├─ [0,1] EventMapRuler        pinned; RenderTransform X = -ScrollViewer.Offset.X; ClipToBounds
├─ [1,0] EventMapLaneHeaders  pinned; RenderTransform Y = -ScrollViewer.Offset.Y; ClipToBounds
└─ [1,1] ScrollViewer (both axes)
         └─ EventMapSurface : Panel   (measure = layout total size)
              ├─ EventMapEdgeLayer : Control   (fills the surface; custom Render)
              └─ EventMapCard × realized       (visible cells only)
```

- **`EventMapSurface`** follows `VirtualizingWrapPanel`'s pattern. `EffectiveViewportChanged` → the visible range
  from the layout → cards are realized and recycled from a pool. A card that scrolls into a new cell is rebound, never
  recreated. It's a plain `Panel`, because cells are positioned by grid coordinate, not flowed.
- **`EventMapEdgeLayer`** draws, in a single `Render` pass, the alternating lane fills, the column grid lines and
  every edge whose column span meets the visible range. It calls `InvalidateVisual` on scroll, selection change,
  layout change, `ActualThemeVariantChanged` and resource changes. Brushes are resolved with `TryFindResource` at
  render time, never cached as hex.
- **`EventMapCard`** is a `TemplatedControl` with a `ControlTheme` per density stop, selected by the pseudoclasses
  `:density-compact`, `:density-standard` and `:density-covers`. It also has `:selected`, `:dimmed`, `:missing` and
  `:focus-visible`.
  - **Covers:** `CoverImageCache.TryGetCached` first. On a miss, `DecodeFromDisk` runs off the UI thread and the
    result is posted back, and dropped if the card was rebound in the meantime.
  - **Read state:** a dot plus a check glyph, and the state is also in the tooltip and the UIA name, so it isn't
    shown by color alone.
  - **Missing file:** reduced opacity plus the existing missing-file glyph.
- **Colors (no hex):**
  - the trunk uses `PbAccentBrush`
  - lanes cycle through `PbChartBlue`, `PbChartViolet`, `PbSuccess`, `PbBadge`, `PbDanger` and `PbAccentText`
  - card surfaces use `PbChromeBrush` and borders `PbBorderBrush`
  - lane fills alternate between `PbBgBrush` and `PbChromeBrush`
  - tie-in edges are dashed in the trunk color
  - sequence and chain edges use the lane color, or `PbTextMutedBrush` for a chain hop
  - continuity edges are the lane color at 35% alpha
- **Selection dimming:**
  - cards outside the related set get `:dimmed` (opacity 0.25)
  - edges outside it are drawn at 25% alpha, and edges inside it at 1.5× width
  - the opacity transition takes 120 ms, and 0 ms when reduced motion is on (`avalonia-pro-max/motion`)
- **Ruler:**
  - column numbers for the visible columns
  - in spine mode, the trunk card's badge above each segment start
- **Lane headers:** the series name (trimmed, with a tooltip), a color bar and an issue count. The trunk header reads
  "Spine · {series}".
- **Zoom:**
  - the density slider snaps to the three stops, and Ctrl+wheel steps between them
  - a stop change relays out the grid and scrolls so the selected card, or failing that the previous viewport centre,
    stays centred
  - a plain wheel scrolls vertically and Shift+wheel scrolls horizontally
- **Accessibility:**
  - each realized card is a focusable control with `AutomationProperties.Name` =
    "{Series} #{Number}, {role}, {read state}"
  - the surface itself is named "Event map for {event}"
  - these peers come for free because the cards are real controls (the reason approach B was chosen over a single
    custom-drawn control)

### 4. Screen integration

- **The view toggle:**
  - `EventsDetailView` gets `Map`, so the toggle becomes **Members | Map | Timeline**
  - the Map segment is visible only when `IsEventSelected`
- **Placement:**
  - `EventMapView` is a sibling in `EventsScreen.axaml` row 2, visible for `IsMapView`
  - the existing detail `ScrollViewer` is hidden while Map shows, because the map needs its own two-axis viewport
    and can't be nested in a vertical scroller
  - the Members-view action row is hidden in Map view
- **View model:**
  - a new `EventsScreenViewModel.Map.cs` partial owns an `EventMapViewModel`
  - it's built lazily the first time Map is chosen, and rebuilt when the selected event changes while Map is showing
- **Continuity entry point:**
  - the continuity detail gets a collapsible **Events in this continuity** section: distinct events with at least one
    member issue whose series is a member of the continuity, ordered by name
  - each event row has **Open map**, which selects the event and sets `DetailView = Map`
- **Toolbar** (above the map):
  - **Spine:** a `SuggestBox` (the app-wide `ComboBox` replacement) listing the member series, with " (auto)" after
    the auto match, plus "None (relay)". Choosing one writes `SpineSeriesId`. "None (relay)" writes a sentinel `0`,
    which the resolver treats as "force relay", so it's different from `null` ("auto"). Then the map relays out.
  - **Filter:** segmented All / Spine only / Hide optional. Spine only is hidden in relay mode.
  - **Density:** a three-stop slider.
  - **Jump to first unread:** an icon button with `AutomationProperties.Name`.
  - **Status text:** "{n} issues · {m} series · spine: {name} (auto|set)" or "relay: no series matches the event name".
- **Inspector:**
  - a right-hand drawer 340 px wide, opened by selecting a card, closed by × or Esc
  - contents:
    - the cover, then **Series #Number**, then the cover date
    - the role as a plain label, and the read state as glyph plus text
    - `Issue.Summary`, clipped at 6 lines with a tooltip
    - buttons: **Open reader** (primary), **Mark read / Mark unread** (via `IssueReadStateResolver`), and **Issue
      details** (the existing detail screen)
    - **Connections**, as described in section 1
    - in spine mode, **Segment order**
  - link buttons select and scroll to their card and never navigate away
  - **Required:** a link's command sets the new selection through `Dispatcher.UIThread.Post`, because the lists
    those buttons live in are rebuilt when the selection changes. See CLAUDE.md, "don't remove/detach a control from
    inside a routed event it's still raising".
  - Mark read/unread updates that card's and the inspector's read state in place, with no reload
- **Keyboard,** handled in `EventMapView.OnKeyDown` while the map has focus and not registered in
  `KeyboardCommandRegistry`:
  - ←/→: Prev/Next in reading order
  - ↑/↓: `NearestInLane`
  - Home/End: first and last card
  - Enter: open the inspector
  - Ctrl+Enter or double-click: open the reader
  - Esc: close the inspector, then clear the selection
  - every move scrolls the target into view
- **First view:** the Standard stop, scrolled to `FirstUnread`, which is selected, with the inspector closed.
- **Returning from the reader:** the same event and view. The card for the last-read issue is re-selected, and only
  the cards whose read state changed are refreshed.
- **Empty and error states:**
  - **No members:** `EmptyIllustration` with "Add issues to this event to see its map" and a button that switches to
    Members with Add issues open.
  - **Load failure:** an inline error in the map area plus an Activity Center alert. A successful load needs no job
    or toast, because it's a synchronous, sub-second query.

### 5. Reader hand-off: an explicit event anchor

- **Plumbing:**
  - `NavigateToReaderCore` and `ReaderScreenViewModel.LoadIssue` get an optional `storyEventId`, stored as
    `_activeStoryEventId`
  - a new `MainViewModel.GoReaderForIssueInStoryEvent(issueId, storyEventId)` mirrors
    `GoReaderForIssueInReadingList` (same drill transition, same history entry)
  - the reading-list and event anchors are mutually exclusive: passing both is guarded by `Debug.Assert`, and the
    reading list wins
- **`ReadingOrderResolver.ResolveNeighbour(..., storyEventId)`** gets an event branch that mirrors the list branch:
  - it walks memberships by `(Position, Id)` and skips missing files
  - it stops at the event's boundary, with no fallback to series order
  - its label is "Event: {name}"
  - all three reader call sites pass `_activeStoryEventId`
- **`ResolveContext(..., storyEventId)`** uses the given event when there is one. Without one, it keeps today's
  lowest-id lookup.
- **The class doc** changes to: "follows Event order only when the reader was explicitly opened from one".
- **The reader walks the event's full `Position` order.** Map filters and lanes never change reading order.
- **History replay doesn't restore the event anchor**, the same as reading lists today.

## Testing

**Pure (`Paperbunkr.App.Tests/EventMap/`):**
- **`SpineResolverTests`:**
  - a saved spine wins
  - a stale saved spine falls back to auto
  - the `0` sentinel forces relay
  - a name match ignores the volume or year
  - several matches: most rows wins, then first appearance
  - no match gives relay
- **`EventMapLayoutTests`:**
  - *Spine mode:*
    - trunk membership: spine rows plus Prologue and Epilogue from other series; `Core` never promotes a row
    - exact compact columns for the screen-1 sample (10 rows → 7 columns)
    - segment 0
    - lane order
    - a series with no remaining rows gets no lane
    - a tie-in edge only to the first row of each lane per segment
    - the continuity edge
  - *Relay mode:*
    - exact columns for the relay-screen sample (12 rows → 12 columns, 3 lanes)
    - the chain edges
  - *Filters:*
    - Spine only
    - Hide optional frees up columns
    - `(Position, Id)` tie ordering
  - *Density stops:* rectangles for each stop
- **Navigation:**
  - Next and Prev in both modes
  - `NearestInLane`, including grid edges and ties
  - First and Last
  - `FirstUnread`, including the all-read fallback
  - the inspector links in both modes, and segment order
  - the related set in both modes
- **Edge path builders:** exact points for a tie-in, an upward chain hop and a downward chain hop.
- **The visible range:** overscan, and clamping at both ends.

**Data (`Paperbunkr.Data.Tests`):**
- **The `SpineSeriesId` migration:** a forward-only test (no up-down-up; see
  `project_paperbunkr_migration_updown_up_test_antipattern`) in which existing rows read back as null.
- **`EventMapLoader`:**
  - read-state mapping
  - the missing-file flag
  - ordering
- **The events-in-continuity query:**
  - finds events through member series
  - lists an event only once when it spans several member series

**Reader (existing `ReadingOrderResolver` tests, extended):**
- **The event branch of `ResolveNeighbour`:**
  - order, and skipping missing files
  - it stops at the event boundary
- **`ResolveContext`:** an explicit event beats a lower-id event.
- **No anchor:** behaviour is unchanged.
- **Both anchors:** the reading list wins.

**View models (headless, `PinnedThreadTestFramework`, `TestDispatcher.Drain()`):**
- **Detail views:**
  - the Map segment shows only for events
  - Map loads lazily
- **Spine picker:** picking a spine persists it and relays out the map.
- **Filters and density:**
  - changing either relays out the map
  - a density change keeps the selection
- **Inspector:**
  - a link click changes the selection only after `Drain()`, which proves it was deferred
  - Mark read updates in place
- **Reader hand-off:** Open reader passes `storyEventId`.
- **Empty and error states:**
  - an empty event gives the empty state
  - a loader exception gives the inline error plus an Activity Center alert
- **Continuity:** Open map selects the event and switches to Map.

**Headless controls:**
- **Virtualization:**
  - the realized cards equal the visible cells plus overscan
  - scrolling recycles cards, and the pool never grows past the maximum visible cell count
  - a synthetic 500-row event stays bounded
- **Edge-layer skin reactivity:** swapping `PbChartBlueColor` changes the next render's pen.
- **Keyboard:** each key produces the expected selection.

**Not automated:**
- look and feel on screen, and scroll smoothness at the Covers stop (the user verifies on screen)
- FlaUI/UIA scripting (only with the user's permission)
- `avalonia-pro-max/review-checklist` runs before the UI work is called done (CLAUDE.md)

## Risks and shared-tree notes

- **Reader files are being changed by other work.** `ReaderScreenViewModel.cs` has uncommitted comic-reader slices B,
  F and G in the working tree, and G's staged next-issue pipeline is one of the three `ResolveNeighbour` call sites.
  The plan builds on whatever state those slices land in. Check `git status` before touching reader files.
- **The role-detection work is uncommitted** and touches `EventMembership`, `EventsScreenViewModel*`,
  `EventsScreen.axaml` and `wiki/Story-Events-and-Relations.md`. The map doesn't depend on it: it reads only the
  `Role` values Prologue, Epilogue and Optional. It does edit some of the same files, and the migration order is
  fixed as described in §2.
- **Name matching can miss.** Event and series names may not match, for example because of ComicVine and Metron
  naming differences. The result is then relay mode, which is still correct, only less structured. The Spine picker
  is the manual fix.
- **Wide relay maps.** A large crossover in relay mode is wide by design. Virtualization keeps it cheap, and the
  Compact stop fits about 12 columns per screen.

## Follow-ups (not in this spec)

- **A silent event resolver** that merges duplicate events (ComicVine and Metron name the same event differently) and
  replaces `ResolveContext`'s lowest-id fallback. Requested by the user 2026-09-25.
- **A continuity-scope map** that chains events via `EventRelation`.
- **An explicit "ties into" override** per membership, if the order-derived attachment proves wrong in practice.
- **A reusable swimlane control for Insights**, showing per-series reading history from `ReadingEvent`.
- **Delete the dead `EventsScreenMode` enum.**

## Docs to update when built

- `docs/paperbunkr-todo.md`: status, commit refs, and what was actually verified
- `docs/onboarding.md`: the Events section
- `wiki/Story-Events-and-Relations.md`: merge carefully with the role-detection session's edits

## Implementation notes (2026-09-27)

Built per 2026-09-25-event-map-plan.md. Where the build differs from the text above:

- **Name matching also strips a trailing year.** `TitleNormalizer.NamesMatch` ignores volume markers but not "(2015)", so
  `SpineResolver` removes a trailing `(19xx|20xx)` from both names first. Without it, "Secret Wars (2015)" never matched.
- **Picking the "(auto)" entry stores `null`**, not that series' id, so the event stays automatic (a rename keeps working).
  "None (relay)" stores `0`; any other series stores its id.
- **Forced relay has its own status text:** "relay: spine turned off". The spec's one relay text is kept for the no-match case.
- **Covers use the existing `AsyncCoverImage`** attached property instead of calling `CoverImageCache` by hand. It already does the
  spec's rule: cache hit synchronous, miss decoded off the UI thread, result dropped if the image was rebound.
- **Density themes:** a pseudoclass can't select a `ControlTheme`. So `EventMapSurface` assigns `EventMapCard{Density}Theme`
  (defined in `EventMapView.axaml`, sharing a base theme) and also sets the `:density-*` pseudoclasses. The lane colour bar
  uses a small attached property (`EventMapLaneBrush.ColorIndex`) that binds `Background` to the skin resource observable, so
  lane colours follow skin swaps live.
- **Dimming duration is `PbMotionFast` (150 ms)**, not a fixed 120 ms, because that token is what `ThemeService` zeroes for
  reduced motion.
- **The ruler draws with the scroll offset** instead of translating a full-width strip, so it only draws visible columns. Lane
  headers do use the `RenderTransform` translation.
- **The visible range clips tracks too** (one track of overscan), not only columns. This matters at the 220 px Covers stop.
- **"Issue details" is "Series details".** No entry point opens Detail focused on one issue (Detail is series-level), so the
  button opens the issue's series in Detail.
- **Mark read from the map doesn't trigger tracker auto-sync.** The Detail screen's mark-read does; the Events screen view model has
  no `TrackerAutoSync`. Follow-up if wanted.
- **Switching events from the sidebar keeps Map showing**, as §4 implies ("rebuilt when the selected event changes while Map is
  showing"). Timeline still resets to Members, as before.
- **The two-anchor `Debug.Assert` lives in the reader's `Load` only**, not in `ReadingOrderResolver`: the spec's own
  "both anchors: the reading list wins" test would otherwise fail-fast.
- **`ResolveContext`'s existing lowest-id path now breaks Position ties by membership id**, matching `ResolveNeighbour`'s event
  branch (it used to order by Position alone).
- **The loader's data tests live in `Paperbunkr.App.Tests/EventMap`**, not `Paperbunkr.Data.Tests`, because `EventMapLoader` is in the
  App project. The migration test is in `Paperbunkr.Data.Tests` as planned.
- **Lane order** uses each series' first Position counting promoted rows. That's the literal reading of "that series' first Position".
- **Test seam:** the map's load runs through an injectable runner (`Task.Run` by default). The headless suite only pins a test
  to the dispatcher thread up to its first real `await`. A real thread hop left later `TestDispatcher.Drain()` calls on a
  pool thread, and one run hung.
- **`docs/onboarding.md` has no Events section**, so it wasn't changed. The wiki got an "Event Map" section.
