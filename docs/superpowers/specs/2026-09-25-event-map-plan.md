# Event Map — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-25-event-map-design.md*

Shared-tree note (2026-09-27): `EventsScreenViewModel.cs`, `EventsScreen.axaml`, `MainViewModel.cs`,
`ReaderScreenViewModel.cs`, the model snapshot and `docs/paperbunkr-todo.md` all carry other sessions'
uncommitted edits (role detection, comic-reader slices B/F/G, preview panel v2). Every edit below is
additive and local to the lines named; nothing is stashed, reverted or reformatted.

## Step 1: Schema — `StoryEvent.SpineSeriesId`
**Files:** `Paperbunkr.Data/Entities/StoryEvent.cs` (edit), `Paperbunkr.Data/Migrations/<ts>_AddStoryEventSpine.cs` +
`.Designer.cs` (new, `dotnet ef migrations add`), `PaperbunkrDbContextModelSnapshot.cs` (tool edit),
`Paperbunkr.Data.Tests/AddStoryEventSpineMigrationTests.cs` (new).
**What:** nullable `int? SpineSeriesId`, no FK, no index. `null` = auto, `0` = force relay (sentinel), `>0` = user
spine. The generated `Up` must contain exactly one `AddColumn` — if `ef` picks up another session's pending model
change, stop and hand-write instead. Timestamp is after `20260925160001_AddRoleDetectionColumns` by construction.
**Verify:** forward-only migration test (migrate → existing row reads back null; value round-trips). No up-down-up.

## Step 2: Pure model, spine resolver, layout engine
**Files (new, `Paperbunkr.App/Services/EventMap/`):** `EventMapModels.cs`, `SpineResolver.cs`, `EventMapLayout.cs`.
**What:**
- Models: `EventMapReadState`, `EventMapRow`, `EventMapSource`, `SpineSource`/`SpineChoice`, `EventMapFilter`,
  `EventMapDensity` + `EventMapDensityMetrics` (118/44/100×30, 170/84/150×64, 150/220/124×196), `EventMapEdgeKind`
  (`Sequence`, `TieIn`, `Continuity`, `Chain`), `EventMapCell`, `EventMapTrack`, `EventMapSegment`, `EventMapEdge`,
  `EventMapLayoutResult`, `EventMapLinks`.
- `SpineResolver.Resolve(source)` — saved (>0 and still a member) → User; `0` → None; else
  `TitleNormalizer.NamesMatch(event, series, ignoreVolume: true)` → Auto (most rows, then first appearance); else None.
  Never "most issues".
- `EventMapLayout.Compute(source, spine, filter, density)` — trunk promotion (spine rows + Prologue/Epilogue),
  filters before layout, compact spine packing / strict relay packing, edges, card rects, track colour indices.
  On the result: `VisibleRange(viewport)` (±1 column overscan, clamped; tracks clipped too), `Next/Prev`,
  `NearestInLane`, `First/Last`, `FirstUnread`, `LinksFor(i)`, `RelatedSet(i)`, `EdgePoints(edge)` (tie-in,
  chain hop, sequence, continuity), `CardRect(i)`.
**Depends on:** none (reads `StoryEvent.SpineSeriesId` only through `EventMapSource`).
**Verify:** `Paperbunkr.App.Tests/EventMap/SpineResolverTests.cs`, `EventMapLayoutTests.cs`,
`EventMapNavigationTests.cs` — every bullet in the spec's "Pure" test list, with hand-computed column/point
fixtures (the screen-1/relay-screen mockup samples aren't in the repo, so equivalent 10-row and 12-row samples
are built in the tests and their expected columns worked out in comments).

## Step 3: Loader and queries
**Files:** `Paperbunkr.App/Services/EventMap/EventMapLoader.cs` (new),
`Paperbunkr.App.Tests/EventMap/EventMapLoaderTests.cs` (new — the App project owns the loader, so its data tests
live beside it rather than in `Paperbunkr.Data.Tests`).
**What:** `Load(context, eventId)` → `EventMapSource` (one query with Series + MetadataProposals; `EffectiveNumber`,
`EffectiveYear`, read state from `ReadPercentage`/`HasBeenRead`, `CoverFingerprint.Stem` key, summary);
`SaveSpine(context, eventId, int?)`; `EventsInContinuity(context, continuityId)` (distinct events with a member
issue whose series is in the continuity, by name); `SetRead(context, issueId, bool)` via `IssueReadStateResolver`.
**Depends on:** Steps 1–2.
**Verify:** read-state mapping, missing-file flag, `(Position, Id)` ordering, events-in-continuity (found via member
series; listed once when spanning several series).

## Step 4: Reader hand-off — explicit event anchor
**Files:** `Services/Reader/ReadingOrderResolver.cs`, `Services/Reader/NextIssueStager.cs`,
`ViewModels/ReaderScreenViewModel.cs` (+ `.Info.cs`), `ViewModels/MainViewModel.cs`,
`Paperbunkr.App.Tests/ReadingOrderResolverTests.cs` (extend).
**What:** optional `storyEventId` on `ResolveNeighbour` (walks `(Position, Id)`, skips missing, stops at the
boundary, label "Event: {name}") and `ResolveContext` (explicit event beats lowest-id). Reading list wins when both
are passed (`Debug.Assert`). `_activeStoryEventId` in the reader, threaded through `LoadIssue`/`Load`, adjacent-issue
navigation, the context strip, the end card and the stager (stager gets an extra optional id; the existing 3-arg
constructor keeps working). `MainViewModel.GoReaderForIssueInStoryEvent` mirrors the reading-list variant; class doc
updated.
**Depends on:** none.
**Verify:** extended resolver tests (event order, missing skipped, boundary stop, explicit event wins, no anchor
unchanged, both anchors → list wins); existing reader/stager suites still green.

## Step 5: `EventMapViewModel`
**Files:** `ViewModels/EventMapViewModel.cs`, `ViewModels/EventMapCardViewModel.cs` (new),
`Paperbunkr.App.Tests/EventMap/EventMapViewModelTests.cs` (new).
**What:** async load off the UI thread (generation-guarded), spine picker (SuggestBox strings incl. "(auto)" and
"None (relay)"), filter, density (index 0–2, `StepDensity`), card VMs (badge, cover key, read state, UIA name,
`IsSelected`/`IsDimmed`), selection + related-set dimming, inspector (open/close, links, segment order — link
commands defer through `Dispatcher.UIThread.Post`), mark read/unread in place, open reader / issue details callbacks,
keyboard verbs (`MoveNext/Prev/Up/Down/Home/End`, `Escape`), first view = Standard + `FirstUnread`, refresh-on-return
(re-select the reader's issue, refresh only changed read states), empty and error states (inline + Activity alert).
**Depends on:** Steps 2–3.
**Verify:** headless VM tests from the spec's "View models" list.

## Step 6: Controls — surface, edge layer, card, ruler, lane headers
**Files (new, `Paperbunkr.App/Controls/EventMap/`):** `EventMapSurface.cs`, `EventMapEdgeLayer.cs`,
`EventMapCard.cs`, `EventMapRuler.cs`; lane headers are an `ItemsControl` in the view.
**What:** approach B. Surface is a plain `Panel`: measures to the layout's total size, `EffectiveViewportChanged` →
visible range → realizes/recycles `EventMapCard`s from a pool (rebinds `DataContext`, never recreates), edge layer
as child 0. Edge layer renders lane fills, grid lines, visible edges (rounded corners r=7, dashed tie-ins, faint
continuity, 25 % alpha / 1.5× width by related set), brushes resolved with `TryFindResource` at render time;
invalidates on scroll, selection, layout, theme variant and resource changes. Card = `TemplatedControl` with
`:density-*`, `:selected`, `:dimmed`, `:missing` pseudoclasses; covers through `AsyncCoverImage` (already does
cache-hit-sync / off-thread decode / drop-on-rebind — the spec's cover rule, reused instead of re-implemented).
Ruler custom-draws visible column numbers and segment-start trunk badges. Avalonia subskills: custom-controls,
graphics-animation, input-interaction; review with `avalonia-pro-max/review-checklist`.
**Depends on:** Step 2.
**Verify:** headless control tests (`Paperbunkr.App.Tests/EventMap/EventMapSurfaceTests.cs`): realized = visible
cells + overscan, recycling keeps the pool bounded, 500-row event stays bounded, swapping `PbChartBlueColor`
changes the next render's pen.

## Step 7: `EventMapView` + Events screen integration
**Files:** `Views/EventMapView.axaml` + `.axaml.cs` (new, same step — CLAUDE.md AVLN2000 gotcha),
`Models/EventsDetailView.cs` (add `Map`), `ViewModels/EventsScreenViewModel.Map.cs` (new partial),
`ViewModels/EventsScreenViewModel.cs` / `.Continuities.cs` (small hooks), `Views/EventsScreen.axaml` (toggle, sibling
placement, continuity section), `ViewModels/MainViewModel.cs` (constructor wiring).
**What:** toolbar (spine SuggestBox, filter segments, density slider, jump-to-first-unread, status text), 2×2 grid
(corner / pinned ruler / pinned lane headers / two-axis ScrollViewer), 340 px inspector drawer, keyboard
(`OnKeyDown`), Ctrl+wheel density, Shift+wheel horizontal, scroll-into-view on moves, recentring on density change,
reduced-motion 0 ms dim transition. **Members | Map | Timeline** (Map only for events); map sibling in row 2; detail
ScrollViewer hidden while Map shows; continuity "Events in this continuity" collapsible with **Open map**.
**Depends on:** Steps 4–6.
**Verify:** VM tests for toggle/lazy load/continuity open-map; keyboard tests; full build (with the
dll/pdb-delete rule if XAML compile ever fails after CoreCompile); launch the exe to prove the XAML weave ran.

## Step 8: Docs, review, verification
**Files:** `docs/paperbunkr-todo.md`, `docs/onboarding.md` (Events section), `wiki/Story-Events-and-Relations.md`,
this plan (implementation notes), design doc (deviations).
**Verify:** full `Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests` runs; `avalonia-pro-max/review-checklist` pass.
Not automated: on-screen look and Covers-stop scroll smoothness (user), FlaUI (only with permission).
