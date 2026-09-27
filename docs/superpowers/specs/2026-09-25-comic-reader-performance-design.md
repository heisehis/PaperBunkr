# Comic reader — Performance (slice G) design

Date: 2026-09-25. Status: ~~design settled in conversation (grilling rounds 1–2), awaiting
written-spec review.~~ **Steps 1-7 built and unit tested, uncommitted** (see the matching `-plan.md`'s
own status line, confirmed 2026-09-26). On-screen check by the user still outstanding.
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, slice G (items #23 and #24). Slices A and B are built; this is the
next slice. Two workstreams in one spec: **A** (#23) a staged pipeline for the next issue, **B** (#24, retargeted) removing the
hitch when continuous scrolling crosses from one page to the next.

## Facts this design rests on (verified 2026-09-25)

- **The pitch's #24 premise was stale.** `ReaderImagePipeline` already widens its forward prefetch fringe from 2 to 6 pages from a
  rolling average of decode times (`RecordDecodeMs`, `_forwardFringe`), keeps a back fringe of 2 and debounces the fringe
  recompute 30 ms (design `2026-09-08-reader-decode-cache-prefetch-pipeline-design.md` §8.1, §8.4). That spec explicitly chose
  "not velocity-scaled". So #24 is **not** "scale prefetch by flip rate"; the user's real complaint is different (below).
- **User-reported symptom (2026-09-25):** in continuous mode, at a steady scroll pace, with no image adjustments and no page margin
  on, crossing from page to page is jarring: you notice the page change instead of a seamless scroll.
- **What happens at a boundary** (traced through `PageCanvas.cs`, `ReaderImagePipeline.cs`, `ReaderScreenViewModel.cs`, read-only,
  nothing measured): the write of `CurrentContinuousPageIndex` runs `OnCurrentContinuousPageIndexChanged` synchronously inside the
  frame callback (`ReaderScreenViewModel.cs` ~561). That handler runs `UpdatePageLabelAndProgress`, `UpdateThumbnailSelection`
  (replaces **every** `Thumbnails[i]`, up to `MaxThumbnails` = 200, into two non-virtualized ItemsControls; the rail is only faded
  to `Opacity=0` in continuous mode, not removed) and `SchedulePositionSave` (a 500 ms `DispatcherTimer`). When the timer fires,
  `FlushPendingPositionSave` runs on the UI thread: a new context (4 PRAGMAs including `synchronous=FULL`), `Find`, `SaveChanges`.
- **Decode look-ahead is thin during steady scrolling.** The high-priority window is the visible pages ±2 (`ReaderLayoutModel`
  default radius). The wider fringe is low priority and `FringeDebounceMs` is a trailing-edge debounce that
  `SetVirtualizationWindow` re-arms every frame (16 ms), so `RecomputeFringe` only runs after a ≥30 ms pause: it is starved while
  the user keeps scrolling. One consumer thread decodes serially in FIFO index order, high channel first, so an off-screen ±2 page
  can sit ahead of a visible one. The spec's N decode workers were not built (pipeline spec §17).
- **No scroll anchoring.** Unknown pages are laid out at an estimated 660×1010 (`PageCanvas` `DefaultEstimatedPageSize`); when the
  real size arrives from a size peek or a decode, nothing corrects `ScrollOffset`. `_knownPageSizes` is never evicted.
- **Instrumentation that exists:** `ReaderPerfStats` (process-wide singleton, reset on each `Load`) feeds the Ctrl+Shift+P overlay:
  a 240-frame compose-time ring (p50/p99) and counters for cache hits/misses, background and synchronous decodes, session and
  archive reads. It has no timing for the boundary handler, decode latency, pages drawn blank, or layout shift, and
  `TryGetCachedPage` counts a miss on every frame for every uncached page, so the hit ratio is misleading.
- **Cross-issue today.** `SharedRawCache` (compressed page bytes, 16–64 MB, keyed by container path + write stamp) outlives a
  pipeline, so going *back* to an issue is fast. Going forward is always cold: `Load` calls `ReaderImagePipeline.TryOpen` (provider
  open, archive parse, session open, consumer thread start) synchronously on the UI thread, then seeds the window. Each pipeline is
  built with the full `ReaderMemoryBudget` (auto 128–512 MB) but those are ceilings, not preallocations.
- **CE parity:** CE has no keep-open archive concept and no next-book pre-open (`ImagePool` plus the OS file cache hid the cost); both
  workstreams are deliberate additions, as the pipeline itself was. Tachiyomi/Mihon pre-loads the next chapter in the background and
  swaps it in on transition, which is the model for A.

## Scope

In: A (#23) staged pipeline for the next issue with a Preferences toggle; B (#24) fix the continuous-mode boundary hitch by removing
unconditional UI-thread work, giving decode a direction-aware head start, adding scroll anchoring, and adding the missing perf
metrics.

Out: pre-opening the *previous* issue; remote-library issues (they go through `RemoteReader.TryOpen`); a second decode worker (only
if the new metrics show the serial consumer is the bottleneck); changes to the paged-mode per-turn save; compositor-side costs
(colour filter, margin scaling: not on in the reported case); Books/PDF reader.

## A. Staged pipeline for the next issue (#23)

- **Staging.** A new `NextIssueStager` (in `Paperbunkr.App/Services/Reader`) owns at most one staged
  `ReaderImagePipeline` for one issue id. It resolves the next issue with `ReadingOrderResolver.ResolveNeighbour(forward: true)` on a
  background thread (its own context), skips issues with no `FilePath`, a missing file, or a remote source, then calls
  `ReaderImagePipeline.TryOpen(path, memoryLimitMb)` and seeds the pipeline's window for pages 0–1 (`SetViewportWidth` from the live
  pipeline's last value, then `SetVirtualizationWindow(0, 1)`), so the archive is open, the entry list parsed, the session held and
  the first two pages read and decoded before the user gets there.
- **Trigger.** The reader tells the stager its position whenever `_currentPageIndex` changes (`GoToPage`, the debounced continuous
  path, and `Load`): when `PageCount - 1 - position <= 2` (the last 3 pages) it calls `EnsureStaged(currentIssueId, readingListId)`.
  Both paged and continuous mode, forward only. It does **not** depend on `AutoNavigateComics`, because the end card can always be
  clicked through. Nothing happens if the setting below is off or the issue is a remote one.
- **Adoption.** `NavigateToAdjacentIssue` / end-card Continue / any `Load` of the staged issue id first asks
  `NextIssueStager.TryTake(issueId, stamp)`. On a hit `Load` uses the returned pipeline as `_decoder` instead of calling `TryOpen`,
  and skips only that open; everything else `Load` does (DB reads, thumbnails, position, overlays) is unchanged. On adoption the
  pipeline is told the real viewport width and the window is re-seeded from the target page; if the width differs from the width
  the staged pages were decoded at, they simply fall out of the display cache and are re-decoded (correctness over reuse).
- **Discard.** The staged pipeline is disposed when: it was adopted (ownership moves to the reader); the reader loads a different
  issue, or closes (`GoBack`); the reader position drops below the last 5 pages (hysteresis, so hovering at the 3-page edge does not
  churn); 2 minutes pass unused; the file's write stamp differs from the one recorded at staging; the setting is turned off; or
  the app shuts down. At most one staged pipeline exists.
- **Memory.** No budget change: the staged pipeline gets the normal `ReaderMemoryBudget`, its real footprint is the two decoded
  pages plus a few compressed pages until adopted or dropped.
- **Perf stats.** `ReaderPerfStats.Current` is process-wide and reset on `Load`, so a staged pipeline must not feed it. The pipeline
  gets a `RecordStats` flag (false while staged, true on adoption) that gates its `RecordCacheHit/Miss/...` calls.
- **Setting.** `AppSettings.PreOpenNextIssue`, default **true**, migration with a **no-op `Down()`**, a toggle in Preferences → Reader
  ("Pre-open the next issue", with the note that it reads the next file in the background, which matters on spinning disks and
  battery), persisted through `PersistBehaviorSetting` like the other reader toggles.
- **Failure.** Everything in the stager is best-effort: any exception disposes the staged pipeline and is swallowed; `Load` then opens
  the issue exactly as it does today. The stager never touches the visible page, `LastPageRead`, or the reading-event log.

## B. Continuous-mode boundary hitch (#24, retargeted)

Approach: fix the two unconditional, clearly-wrong costs now, add the missing metrics, and tune the rest against real numbers.

1. **Thumbnail selection churn.** `UpdateThumbnailSelection` updates only the previously selected and newly selected items (two
   `Thumbnails[i]` replacements, or an in-place selected flag if `ReaderThumbnailSample` allows it), never all of them. A test
   asserts a page change causes at most 2 collection changes. Applies to paged mode too (same method).
2. **Position save off the UI thread.** The debounced save (`FlushPendingPositionSave`) keeps its 500 ms debounce and its
   `_pendingPositionSaveIssueId/Index` state but does the DB write on a background thread with a fresh context, one save at a time
   (single-flight: a save requested while one runs is coalesced to the latest), then calls `TrackSessionProgress` back on the UI
   thread. `Load` and `GoBack` still flush synchronously before switching issues (correctness over latency there). Continuous mode
   only: the paged per-turn save in `GoToPage` is noted, not changed.
3. **Direction-aware decode head start.** `SetVirtualizationWindow` gets the scroll direction (from the sign of the change in the
   first visible page or `ScrollOffset`, held for a short time so it does not flap). Requests go on the high channel ordered by
   distance to the viewport centre (visible pages first, then about 3 ahead in the scroll direction, then 1 behind) instead of by
   index. The low-priority fringe stops depending on a trailing-edge debounce: it runs on a **throttle** of about 100 ms while calls
   keep arriving (leading and trailing), so it is not starved by steady scrolling. `FringeDebounceMs`'s original purpose (a burst of
   rapid paged flips enqueuing superseded work) is kept for paged mode by making the throttle interval paged-mode-longer or by
   keeping the debounce for paged calls; the plan picks one and tests both modes.
4. **Scroll anchoring and earlier real sizes.** When a page **above** the first visible page learns its real size, `ScrollOffset` is
   adjusted by the size difference so the visible content does not move. Size peeks for the window plus about 3 ahead are requested at
   high priority so estimates are replaced before pages scroll into view. Estimated-to-real changes for pages below the viewport need
   no correction. A pure function `ScrollAnchor.Adjust(oldSizes, newSizes, firstVisible, offset)` holds the math.
5. **Metrics (added before/with the fixes, so the user can compare).** `ReaderPerfStats` and the Ctrl+Shift+P overlay gain: boundary
   handler ms (the VM handler, as a p50/p99 ring), decode latency (a page entering the layout window to its decode landing), pages
   drawn blank per frame (count and worst), layout shift (pixels a visible page moved when a size arrived), and the position-save
   duration. The misleading per-frame miss count from `TryGetCachedPage` is replaced by counting a miss once per page per window
   entry. `TryGetCachedBand` starts recording too.
6. **Deferred until measured:** a second decode worker, strip-eviction lock contention, the synchronous centre-page decode after a
   big jump, GC pressure from LOH buffers in the band converter, and the compositor first-draw costs. If the metrics after 1–5 still
   show a stall at the boundary, the next fix is chosen from the numbers and gets its own short addendum here.

## Build order

1. Metrics (B5), so a baseline reading exists before anything changes.
2. B1 thumbnail churn, B2 off-thread position save.
3. B4 scroll anchoring, then B3 direction-aware decode and the throttled fringe.
4. A: `PreOpenNextIssue` setting + migration + Preferences toggle, `RecordStats` flag, `NextIssueStager`, `Load` adoption, triggers
   and discard rules.

Each step is built, tested and verified before the next. On-screen check by the user after steps 3 and 4 (the user compares the
overlay numbers and the feel on their real content, a steady-pace continuous scroll for B and a real solid `.cbr` for A).

## Testing

- **Pure logic:** `ScrollAnchor.Adjust` (page above the viewport grows/shrinks, page below, first visible page itself, several
  changes at once, no change); request ordering by distance and direction; the throttle/debounce timing through the existing
  `OnFringeRecomputed` seam; discard-rule decisions in the stager as a pure state machine (staged, adopted, position rule, idle
  timeout, stamp change, setting off).
- **View model:** a page change replaces at most 2 thumbnails; the debounced save runs its DB work off the UI thread and coalesces
  (asserted through an injectable save callback, not real threads); `Load` flushes before switching; staging triggers at the 3-page
  edge in both modes and not before; `Load` of the staged id adopts without a second `TryOpen`; a stamp change or another issue
  disposes it; the setting off never stages.
- **Pipeline:** existing `ReaderImagePipelineTests`/band tests stay green; the stats flag gates recording; the seeded staged window
  decodes pages 0–1 without touching `ReaderPerfStats`.
- **Data:** migration test for `PreOpenNextIssue` (default true, round-trips) and a Preferences VM persistence test.
- Headless tests assert state synchronously; timer behaviour goes through `internal void On…Tick` seams; check `Get-Process testhost`
  before each run and never kill another session's host. No FlaUI scripting without asking.

## Open risks

- **The hitch may have a cause the code reading missed.** Nothing has been measured. That is why the metrics come first and why the
  deferred list exists; if the user still sees the jank after steps 1–3 the next fix follows from the numbers.
- **Staging reads a whole next file in the background.** On a spinning disk while the current issue is still being read, that can
  compete for I/O; mitigated by the last-3-pages trigger, one staged pipeline at most, and the toggle.
- **Adoption after a viewport resize** re-decodes the staged pages (correct but the head start is lost); acceptable.
- **`RecordStats` gating** touches every `ReaderPerfStats.Current` call in the pipeline; a missed call would let a staged pipeline
  pollute the overlay (cosmetic, not a correctness risk).

## Implementation notes (2026-09-25)

Built in the order given under "Build order" (plan: `2026-09-25-comic-reader-performance-plan.md`). Where the code differs from the design:

- **The window hint is a new interface overload, not a change to the old one.** `IReaderPageSource.SetVirtualizationWindow(min, max, ScrollWindowHint)`
  carries the true visible range, the scroll direction and a "sustained scroll" flag; the two-argument method (paged mode, tests) is unchanged and
  keeps the 30 ms trailing debounce. `PageCanvas` was passing the visible pages **plus the ±2 layout radius** as the window, so the pipeline could not
  tell visible pages from radius pages; the visible range is now computed from the layout rects.
- **Decode order is by enqueue order.** The channel is FIFO, so `ReaderImagePipeline.OrderWindow` (pure, tested) decides the order: visible pages nearest the
  visible centre first, then the pages ahead in the scroll direction (plus one more, beyond the radius), then the pages behind. In steady scrolling only a
  page or two is new per call, so this mainly helps jumps and window shifts; the throttled fringe is the larger effect.
- **Fringe throttle is 100 ms, armed once.** While `SustainedScroll` is set the fringe timer is armed by the first call and not re-armed by later ones;
  `RecomputeFringe` clears the flag. A test drives a 500 ms simulated scroll and requires at least three passes (a debounce would give none until it stopped).
- **Scroll anchoring** (`ScrollAnchor`, pure) is applied wherever a page size is recorded (`SetKnownPageSize`: a header peek arriving, or a decoded bitmap
  whose aspect ratio differs). An update with the same aspect ratio (a decoded bitmap replacing a peeked native size) only stores the value and never moves
  the scroll. The layout-shift metric records the correction that was applied (the shift the reader was spared), not a measured jump.
- **Blank-page metric counts whole pages only** (a strip's band slots are not counted), and only pages actually intersecting the viewport.
- **Position save.** The timer path (`FlushPendingPositionSaveInBackground`) hands the write to a background thread, one at a time, coalescing to the newest
  position; a sequence number stops an older background write overwriting a newer synchronous one. `Load`/`GoBack` still call the synchronous
  `FlushPendingPositionSave`. `PositionWriter` is a test seam. The paged per-turn save in `GoToPage` is unchanged, as planned.
- **Thumbnail selection** replaces only the items whose selected flag changes (a diff pass over the existing list, at most two replacements normally); the
  `CurrentPageIndexChanged` event and the bookmark state are unchanged.
- **`NextIssueStager` takes the next-issue lookup as a delegate**, because `RemoteRowIsolationTests.OptInSites_AreExplicit_AndMatchTheAllowlist` allows
  `includeRemote: true` only in a fixed list of files; the reader view model (already on it) supplies `ResolveStagingTarget`. Staging is opt-in: `MainViewModel`
  calls `Reader.EnableNextIssueStaging()`, so tests never stage anything as a side effect. `TryAdopt` also flips `RecordStats` back on; the pipeline gained
  an internal `RecordStats` flag (all its `ReaderPerfStats.Current` calls now go through a gated property) and an internal `ViewportWidth` getter.
- **Idle expiry** uses a 30 s timer that calls `ExpireIfIdle` (a staged pipeline is dropped after 2 minutes unused); asking again for the same issue while the
  reader is still in the end zone counts as use. Disposal of a discarded pipeline runs on a thread-pool thread because `Dispose` waits for the consumer thread.
- **`PreOpenNextIssue`** default true, migration `AddPreOpenNextIssue` with a no-op `Down()`, toggle in Preferences → Reader → Zoom & Navigation.
- **Not verified:** none of this has been felt or seen on screen. The user needs to scroll a continuous issue at a steady pace and compare the Ctrl+Shift+P
  overlay's new boundary line (boundary handler, decode latency, blank frames, layout shift, save time) against how it feels, and read a real solid `.cbr` to
  the last pages and continue. Deferred exactly as designed: a second decode worker, strip-eviction contention, the synchronous centre-page decode after a big
  jump, LOH pressure in the band converter and compositor first-draw costs; they are chosen from the overlay numbers if the jank remains.
