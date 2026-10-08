# Performance and memory — design

Date: 2026-10-07. Status: awaiting review.

## 1. Problem

Continuous use pushes Paperbunkr's working set to 2.3–2.6 GB (Task Manager, and the 0.7.7-beta crash log: "Working set : 2617 MB"). Benchmark machine (the user's own):
i3-1005G1 (2 cores / 4 threads), 7.8 GB RAM, Intel UHD sharing system memory, ~5,000 comics. Windows sits at ~87% memory use with the app open, so it pages and the UI thread stalls.
Heavy jobs seen running in the background during the creep: guided view, library scan, Verify, scraping/organizing, Metron.

The crash in the log is a separate, small defect: a cancelled Verify (`LibraryHealthService.Verify`, `ct.ThrowIfCancellationRequested`) surfaced as an unhandled `OperationCanceledException`. `PreferencesScreenViewModel.VerifyLibraryHealthNow` already has a `catch (OperationCanceledException)` in current source, so the reporting build was probably older. Step 0 confirms this against the running build and, if it still reproduces, fixes it.

## 2. Findings (from the code, not yet measured)

| # | Finding | Where |
|---|---------|-------|
| F1 | `CoverImageCache` and `BookCoverImageCache` are count-bounded at 5,000 full-size bitmaps (its own doc: up to ~4.8 GB). The whole library fits. ~34 call sites in 20 files use `CoverImageCache`. | `Services/CoverImageCache.cs`, `BookCoverImageCache.cs`, `LruCache.cs` |
| F2 | Evicted bitmaps are deliberately never disposed (an earlier version crashed on `ObjectDisposedException`); native memory returns only when the GC finalizes the small managed wrapper, which may be a long time. | `LruCache.cs` |
| F3 | The Library grid already has a byte-bounded display-size cache (`GridCoverCache`, fixed 300 MB), but it is a fixed constant, not scaled to RAM. | `Services/GridCoverCache.cs` |
| F4 | `CoverDecodeQueue` runs up to 8 decode workers (`Clamp(ProcessorCount*2, 4, 8)`) on 4 hardware threads. | `Services/CoverDecodeQueue.cs:99` |
| F5 | `SyncMetadata` (with `Include(Tags)`), `ResyncSeriesFromFile` and `Verify` each load every issue as a tracked EF entity and hold them until the end. | `LibraryFolderScanner.cs:484,539`, `LibraryHealthService.cs:71` |
| F6 | Nothing prevents scan, Verify, scrape, Metron and guided view from running at once. | — |
| F7 | `PanelDetectionService` creates the ONNX `InferenceSession` once and keeps it for the process lifetime. ONNX Runtime's CPU arena grows and does not normally shrink. (Behaviour to confirm in Step 1.) | `Services/Reader/Panels/PanelDetectionService.cs` |
| F8 | No runtime GC settings (`runtimeconfig`) and no memory-pressure response anywhere. | `Paperbunkr.App.csproj` |
| F9 | 47 timer constructions (`DispatcherTimer`/`PeriodicTimer`/`Timer`) in `src/Paperbunkr.App`; which ones tick while idle is unknown. (An earlier draft said "~107"; that was a count of matching lines, not timers.) | `src/Paperbunkr.App` |
| F10 | The on-disk cover thumbnails are already 400 px on the longest edge, and the grid decoder already scales while decoding (`Bitmap.DecodeToWidth`). Decode-time downscaling is therefore already in place for the grid; only the legacy full-thumbnail path (F1) lacks it. | `CoverThumbnailService.cs:32`, `GridCoverDecoder.cs:35` |

## 3. Approach

Measure first, then fix in layers, checking each against a number. Rejected: config-only tuning (leaves F4–F7 in place); a separate worker process for heavy jobs (IPC and crash handling, too large before we know where the memory goes).

## 4. Design

### 4.0 Crash check
Reproduce the cancelled-Verify exception on the current build (start Verify, cancel it). If it still escapes, catch `OperationCanceledException` at the task boundary in the command, the same way the scan jobs do.

### 4.1 Measurement (do first)
- A `PerformanceSnapshot` service reads three memory numbers kept separate, because they answer different questions: **managed heap** (`GC.GetTotalMemory(false)` plus `GC.GetGCMemoryInfo` size and fragmentation), **private committed bytes** (`PrivateMemorySize64`, the real footprint: native Skia buffers show up here, file-backed maps do not), and **working set** (what Task Manager shows, which also counts file-backed pages Windows can reclaim). Private minus managed is the native estimate, which is how F2/F7 get confirmed or ruled out. It also reads gen-2 collection count, `GridCoverCache` bytes/count, `CoverImageCache` and `BookCoverImageCache` counts, busy heavy jobs, and thread count.
- Shown in Preferences > About/Diagnostics and appended to the crash log header next to "Working set".
- Three repeatable scenarios, each recorded as a baseline before any change: (a) scroll the whole Library grid top to bottom twice; (b) run Scan + Verify; (c) read 30 pages in guided view. Idle-5-minutes CPU is a fourth measurement.
- Success targets on the benchmark machine, in **private bytes** (working set is reported but not the target): steady-state 600–800 MB after scenario (a); no scenario above 1.2 GB; idle CPU under 1%.

### 4.2 Image memory
- **Deterministic disposal where it is safe (grid path).** The review is right that "never dispose" lets evicted native buffers sit until a late gen-2 finalization, so a byte cap on the cache does not cap memory. The grid path has one consumer (`AsyncCoverImage`), so it gets a lease count: the control takes a lease when it sets `Image.Source` and releases it when the container is recycled or detached; `GridCoverCache` disposes an evicted bitmap when its lease count is zero, and disposes it at release time if it was evicted while leased. No other code holds these bitmaps. (`WeakReference` does not solve this and is not used.)
- **Legacy path (`CoverImageCache`, `BookCoverImageCache`) keeps no-dispose for now**, because its bitmaps are bound into long-lived view models at ~34 sites and a lease there is a large refactor. Instead: (1) move the card/strip sites that don't need full size onto the leased grid path; (2) replace the 5,000 cap with a byte budget so the cache itself stays small; (3) Step 4.1 tells us how much native memory the remainder actually retains, and a lease-based `CoverImageCache` is added only if that number is still significant.
- Image budget derived from RAM and clamped: `Math.Clamp((long)(TotalPhysicalRam * 0.05), 150 MB, 500 MB)`, split across the three caches (≈390 MB here), replacing the fixed 300 MB constant.
- Decode-time downscaling already exists for the grid (F10), so no change there. The legacy path decodes the 400 px thumbnail whole; the sites moved to the grid path get downscaling for free.
- **Memory-pressure trim: no forced `GC.Collect`.** When system memory use stays above ~85%, flush cache entries on a background thread (leased bitmaps are disposed as they release; legacy ones are dropped and left to the collector). At most one non-blocking background gen-2 request, rate-limited to once per 2 minutes, and only if private bytes have not fallen after the flush. No blocking compacting collection on the UI thread. No toast.
- `CoverDecodeQueue` concurrency becomes `max(2, logical cores − 1)` (3 here), not up to 8.

### 4.3 Heavy-job lane
- One `HeavyJobLane` with a single slot, a `SemaphoreSlim(1, 1)` gate behind a small priority queue (the semaphore alone gives no ordering). Scan, Verify, metadata sync, resync, scraping/organizing, Metron sync and the ONNX warm-up go through it. Scheduled jobs queue; a job the user starts jumps the queue.
- **No thread-priority changes.** Setting `Thread.Priority` on thread-pool threads leaks to later pooled work and does not limit I/O, so the earlier "BelowNormal" idea is dropped. Throttling is by concurrency instead: any job that parallelises uses `MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)` (3 here); the page-loop jobs are sequential already.
- **Yield between pages** (end of each ~250-issue page): `await Task.Delay(…)` of a few ms, not `Task.Yield()` (which does nothing useful on the pool). Windows timer granularity makes `Delay(1)` about 15 ms, which is acceptable per page and is the knob if the UI still stutters.
- Surfaces in the Activity Center (existing `ActivityJobKind`s) as "Waiting" until its turn.

### 4.4 EF in the jobs
- `Verify`, `SyncMetadata`, `ResyncSeriesFromFile`: **keyset pagination**: `Where(i => i.Id > lastId).OrderBy(i => i.Id).Take(250)`, never `Skip/Take`. Each page runs in its own `DbContext`, so the tracked set never exceeds one page. These three jobs write (Verify sets flags, the sync jobs map fields), so they keep change tracking within a page; the page's `SaveChanges` runs before the context is disposed. Any job that only reads uses `AsNoTracking()` and `AutoDetectChangesEnabled = false`. Results (counts, events) are unchanged, and `SyncMetadata` keeps its `Include(Tags)` per page.
- No SQLite `PRAGMA cache_size` change: the default is already −2000 (about 2 MB per connection), so it is a no-op, and `temp_store = MEMORY` would add memory. Revisit only if 4.1 shows SQLite memory is significant.

### 4.5 Runtime and ONNX
- `runtimeconfig` (via csproj): `System.GC.ConserveMemory = 1` (try 2 only if 1 is not enough), Workstation GC (already the default for this app, stated explicitly), concurrent GC kept on. **`TieredPGO` stays on** (default); it is only tested off as a measured experiment, and kept off only if the 4.1 numbers show a gain and background jobs are not slower. Each setting is kept only if it helps against the baseline.
- `OnnxPanelDetector`: `IntraOpNumThreads = 2` to cap core use on this 2-core machine. **`EnableCpuMemArena` is decided by measurement**: the guided-view harness (`Paperbunkr.ScrollHarness`, the numbers recorded in the guided-view ONNX work) runs both settings; arena stays on unless it holds meaningful private memory after a reading session and turning it off costs little speed. (The review claims 2–3× slower with it off; that is unmeasured here and not assumed.)
- **Unload the session when the user leaves the Reader**, not on an idle timer, so a reader paused mid-page never hits a reload stall. The next entry to guided view reloads it (~1 s, once).

### 4.6 UI-thread audit
- With the app idle and during each background scenario, capture `dotnet-trace`/counters, list what runs on the UI thread by cost, and take the top offenders off it (timers that tick while idle, per-tick allocations, collection rebuilds). The 47 timers (F9) are the starting list. This step's scope is set by the trace, not decided here.
- **Timer consolidation is conditional.** If the trace shows several timers waking the UI thread while idle, they are consolidated onto one shared tick service that views subscribe to only while visible. It uses a plain `DispatcherTimer`/`PeriodicTimer`: the project does not use ReactiveUI/Rx (CLAUDE.md), so `Observable.Interval` is not adopted. If idle CPU already meets the target, the timers are left alone.

## 5. Testing
- Unit tests: byte-budget eviction for the two caches; lease counting in `GridCoverCache` (evict-while-leased disposes on release, evict-while-free disposes at once, a leased bitmap is never disposed); the budget clamp at 4, 8 and 32 GB; `HeavyJobLane` ordering and user-job priority; keyset-paged jobs return the same results as the old full-load versions on a seeded database, including ids with gaps; the ONNX unload-on-Reader-exit/reload path; the pressure-trim trigger with an injected memory reader and no `GC.Collect` on the UI thread.
- Run the fast set (`--filter "Speed!=Slow"`) plus the touched classes; slow classes before release.
- Before/after snapshots from the 4.1 scenarios go into the plan's results section.

## 6. Out of scope
Scroll smoothness (own spec 2026-09-19), reader decode/prefetch pipeline internals, a worker-process split, any change to the data model.

## 7. Decisions
- One heavy job at a time, user-started jobs first (user: yes).
- Image budget from RAM, clamped 150–500 MB (user: yes to ~5%; clamp from review).
- Quiet auto-trim above ~85% system memory use (user: yes), now without forced GC (review).
- ONNX session unloaded when leaving the Reader, not on an idle timer (supersedes the earlier "reload after idle" answer, per review).
- Background jobs throttled by concurrency and per-page yields, not thread priority (supersedes the earlier "lower priority" answer; the mechanism changed, the intent, a smooth UI during jobs, did not).

## 8. Review notes (external review, 2026-10-07)
Adopted: lease-based disposal for the grid path; budget clamp; no forced GC; no pool-thread priority; keyset paging; three-way memory telemetry; Workstation GC and `ConserveMemory`; PGO left on; ONNX thread cap and unload on Reader exit.
Not adopted, with reasons: SQLite `cache_size` pragma (already the default) and `temp_store = MEMORY` (adds memory); Rx/`Observable.Interval` (not used in this project); "decode 4K covers down to 400 px" (thumbnails are already 400 px and the grid already decodes to width, F10); the 2–3× arena slowdown claim (measure instead); a project-wide lease/`WeakReference` rewrite of the legacy cover cache (deferred until 4.1 shows it is needed).
