# Comic reader — Performance (slice G) — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md*

**Status 2026-09-25:** Steps 1-7 built and unit tested; **on-screen check by the user outstanding**; uncommitted (see the spec's "Implementation notes" for deviations). The working tree is shared with
uncommitted slice B work (`ReaderScreenViewModel.cs`, `PageCanvas.cs`, `MainViewModel.cs`, `PaperbunkrDbContext.cs`, the model
snapshot) and other sessions' work: edit narrowly, `git diff` shared files before and after, never revert another hunk. Test runs:
wait for a foreign `testhost` to exit instead of killing it, redirect `dotnet test` output to a log (`*> $log`), and kill only
`testhost` processes created after your own start time. Each new migration's `Up()` is read before it is kept.

## Survey corrections (fold into the spec's "Implementation notes" at the end)

- `PageCanvas` passes `SetVirtualizationWindow(layout[0].Index, layout[^1].Index)`, i.e. the visible pages **plus the ±2 layout
  radius**, so the pipeline cannot tell visible pages from radius pages. Step 3 passes the true visible range separately.
- `SetVirtualizationWindow` enqueues the window in ascending index order, so when scrolling forward the pages *behind* the viewport
  are queued before the ones ahead of it (FIFO channel). Ordering by distance to the viewport centre fixes that for new requests.
- The user's report (steady pace, no adjustments, no margin) points at causes 1 (thumbnail churn), 3 (layout shift) and 4 (position
  save) more than the compositor costs, which is why those are steps 2 and 3 and the compositor items stay deferred.

## Step 1: Metrics
**Files:** `Services/Reader/ReaderPerfStats.cs` (edit), `Services/Reader/ReaderImagePipeline.cs` (edit: once-per-page lookup
counting, decode latency), `Views/PageCanvas.cs` (edit: blank-page count), `ViewModels/ReaderScreenViewModel.cs` (edit: boundary
handler and position-save timings), `App.Tests/Reader/ReaderPerfStatsTests.cs` (edit)
**What:** rings for boundary-handler ms, decode latency (page entering the window → decode landed) and position-save ms with
p50/p99; counters for frames drawn with a blank visible page (and the worst count), layout-shift events and the max shift in
pixels; a per-page-once hit/miss (`RecordLookupOnce`) so `TryGetCachedPage`'s per-frame calls stop inflating the ratio; the
snapshot record and overlay text extended with all of them.
**Depends on:** none
**Verify:** stats tests (rings, once-per-page, reset, snapshot text), pipeline test that a window page's decode records a latency.

## Step 2: Thumbnail churn + off-thread position save
**Files:** `ViewModels/ReaderScreenViewModel.cs` (edit: `UpdateThumbnailSelection`, `FlushPendingPositionSave`, new
`PositionSaver`), `App.Tests/ReaderScreenViewModelTests.cs` (edit)
**What:** `UpdateThumbnailSelection` replaces only the previously selected and the newly selected thumbnail (tracks the last
selected index; full rebuild only when the collection was just rebuilt). The debounced save does its DB write on a background thread
with a fresh context, single-flight and coalescing (latest pending wins), then runs `TrackSessionProgress` back on the UI thread; the
save function is injectable for tests. `Load`/`GoBack` keep flushing synchronously.
**Depends on:** Step 1 (timings)
**Verify:** a page change touches at most 2 `Thumbnails` slots (collection-changed count); the debounced save never runs on the
calling thread and coalesces; synchronous flush on `Load` still persists; existing position tests stay green.

## Step 3: Scroll anchoring, earlier sizes, direction-aware decode, throttled fringe
**Files:** `Views/ScrollAnchor.cs` (new, pure), `Views/PageCanvas.cs` (edit: page-size-available handler, push), `Views/ReaderLayoutModel.cs` (no change expected),
`Services/Reader/IReaderPageSource.cs` + `ReaderImagePipeline.cs` (edit: `SetVirtualizationWindow` overload with visible range,
direction and `sustainedScroll`; distance ordering; throttle), `Services/PageDecodeService.cs`/other implementers of
`IReaderPageSource` (edit if any), tests `App.Tests/ScrollAnchorTests.cs` (new), `Reader/ReaderImagePipelineTests.cs` (edit)
**What:** `ScrollAnchor.Adjust` returns the `ScrollOffset` correction when page sizes above the first visible page change; the
handler applies it and records the shift metric. The push passes the visible range, the scroll direction (sign of the offset change,
held ~250 ms) and `sustainedScroll: true`. The pipeline enqueues visible pages first by distance to the visible centre, then up to
3 pages ahead in the scroll direction and 1 behind at high priority, and size peeks for window+3 ahead at high priority. With
`sustainedScroll` the fringe pass is a throttle (armed once, ~100 ms, not re-armed by later calls) instead of a trailing debounce;
paged mode keeps the 30 ms debounce.
**Depends on:** Step 1
**Verify:** anchor math cases; request order and look-ahead through `OnBeforeBackgroundDecode`; throttle vs debounce through
`OnFringeRecomputed`; existing pipeline/band tests green.

## Step 4: `PreOpenNextIssue` setting
**Files:** `Data/Entities/AppSettings.cs`, `Data/PaperbunkrDbContext.cs` (shared/dirty: one `HasDefaultValue`), migration
`AddPreOpenNextIssue` (+ Designer + snapshot; no-op `Down()`), `ViewModels/PreferencesScreenViewModel.cs`,
`Views/Preferences/ReaderSection.axaml`, tests `Data.Tests/AddPreOpenNextIssueMigrationTests.cs`, `PreferencesScreenViewModelTests.cs`
**What:** bool default **true**, toggle "Pre-open the next issue" in Preferences → Reader with the disk/battery note.
**Depends on:** none
**Verify:** migration default + round trip, Preferences persistence.

## Step 5: Stager
**Files:** `Services/Reader/NextIssueStager.cs` (new), `Services/Reader/ReaderImagePipeline.cs` (edit: `RecordStats` flag gating every
`ReaderPerfStats.Current` call, `ViewportWidth` getter, `ContainerStamp`), tests `App.Tests/Reader/NextIssueStagerTests.cs` (new)
**What:** owns at most one staged pipeline for one issue id. `EnsureStaged(currentIssueId, readingListId)`: background thread,
`ReadingOrderResolver.ResolveNeighbour(forward)`, skip no file / missing / remote, `TryOpen`, seed pages 0-1. `TryTake(issueId)`
returns it (stamp verified) and hands ownership over. Discard rules as a pure decision type: adopted, other issue loaded, reader
closed, position below the last 5 pages, 2 minute idle, stamp changed, setting off, shutdown. Injectable opener/clock/resolver for tests.
**Depends on:** Step 4
**Verify:** state-machine tests for every discard rule; staged open happens once; failure disposes and swallows; stats flag gates recording.

## Step 6: Reader integration
**Files:** `ViewModels/ReaderScreenViewModel.cs` (edit: trigger from position changes in both modes, adoption in `Load`, discard on
`GoBack`/`Load` of another issue), `ViewModels/MainViewModel.cs` (shared/dirty: only if the stager needs shell wiring/shutdown
disposal), `App.Tests/ReaderScreenViewModelTests.cs` (edit)
**What:** position at the last 3 pages (paged `GoToPage`, continuous handler, `Load`) calls `EnsureStaged` (setting on, local, has
next); `Load` asks `TryTake` first and skips only `TryOpen`; the viewport width is re-applied on adoption.
**Depends on:** Steps 2, 5
**Verify:** staging triggers at the 3-page edge only, in both modes; `Load` of the staged id adopts with no second open; a different
issue or a stamp change disposes; the setting off never stages.

## Step 7: Docs and close-out
Spec "Implementation notes", `docs/paperbunkr-todo.md`, `docs/Paperbunkr-Roadmap.md`, wiki Preferences/Reading lines, memory. Full
build + review checklist over `ReaderSection.axaml`; test runs split as before.

## Checklist
- [x] 1 Metrics  - [x] 2 Thumbnail/save  - [x] 3 Anchoring/decode  - [x] 4 Setting  - [x] 5 Stager  - [x] 6 Integration  - [x] 7 Docs
