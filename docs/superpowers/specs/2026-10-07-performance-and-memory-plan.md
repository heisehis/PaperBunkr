# Performance and memory — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-07-performance-and-memory-design.md*

## Step 1: Memory readout
**Files:** `Services/Performance/SystemMemory.cs` (new), `Services/Performance/PerformanceSnapshot.cs` (new), `Services/DiagnosticsService.cs` (edit: crash-log header)
**What:** `SystemMemory` reads installed RAM and system memory load (`GlobalMemoryStatusEx`, falling back to `GC.GetGCMemoryInfo`). `PerformanceSnapshot.Capture()` returns managed heap, private bytes, working set, native estimate, gen-2 count, cache bytes/counts, threads, waiting heavy jobs; `Describe()` formats it. The crash-log header gains the snapshot lines.
**Verify:** `PerformanceSnapshotTests`.

## Step 2: Image memory
**Files:** `Services/Performance/ImageMemoryBudget.cs` (new), `Services/BitmapByteCache.cs` (new), `Services/NativeBitmapPressure.cs` (new), `Services/GridCoverCache.cs`, `Services/GridCoverDecoder.cs`, `Services/CoverImageCache.cs`, `Services/BookCoverImageCache.cs`, `Views/AsyncCoverImage.cs` (edits)
**What:** Budget = `Clamp(5% of RAM, 150 MB, 500 MB)`, split grid 60% / comics 30% / books 10%. `GridCoverCache` gains leases (`TryLease`/`Release`) and disposes an unleased entry on eviction, removal or clear; a leased one is disposed on release. `AsyncCoverImage`'s grid path takes a lease per `Image` and releases it when the image is re-pointed. The two legacy caches move from a 5,000-entry cap to a byte budget (still never dispose) and register their bitmaps' native size with the GC (`NativeBitmapPressure`) so unreferenced ones are collected sooner.
**Depends on:** Step 1 (`SystemMemory`).
**Verify:** `GridCoverCacheTests` (new lease tests), `BitmapByteCacheTests`, `ImageMemoryBudgetTests`, existing `CoverImageCacheTests`/`AsyncCoverImageTests`.

## Step 3: Memory-pressure trim
**Files:** `Services/Performance/MemoryPressureTrimmer.cs` (new), hooked from the cache add paths (no timer)
**What:** Rate-limited check (every ≥10 s, on a cache add). After two consecutive readings above 85% system memory load it flushes the cover caches on a pool thread. No `GC.Collect` on the UI thread; at most one non-blocking background gen-2 request per 2 minutes if private bytes did not fall.
**Verify:** `MemoryPressureTrimmerTests` with an injected memory reader and clock.

## Step 4: Heavy-job lane
**Files:** `Services/HeavyJobLane.cs` (new); edits at the heavy job start sites: `ViewModels/PreferencesScreenViewModel.cs` (scan, book scan, covers, sync, classify, verify), `Services/Scheduling/SchedulerService.cs`, `Scraper/ScrapeCoordinator.cs`, `Scraper/OrganizeCoordinator.cs`, `ViewModels/MetronSyncSettingsViewModel.cs`
**What:** One slot, user-started entries ahead of scheduled ones, re-entrant (a job already inside the lane that starts another does not wait on itself), cancellable while waiting. Callers follow the existing `startQueued` → wait → `Begin()` pattern (`LibraryScreenViewModel.Refresh`'s `_rereadGate`).
**Verify:** `HeavyJobLaneTests`; existing scheduler/preferences tests.

## Step 5: Keyset paging in the sweeps
**Files:** `Services/LibraryHealthService.cs`, `Services/LibraryFolderScanner.cs` (`SyncMetadata`, `ResyncSeriesFromFile`)
**What:** `Where(Id > lastId).OrderBy(Id).Take(250)`, one `DbContext` per page, `SaveChanges` per page, a short `Thread.Sleep` between pages (the sweeps are synchronous inside `Task.Run`). Same results and events.
**Verify:** existing `LibraryHealthServiceTests` and `LibraryFolderScannerTests`, plus a >1-page test for each.

## Step 6: Runtime and ONNX
**Files:** `Paperbunkr.App.csproj`, `Services/Reader/Panels/OnnxPanelDetector.cs`, `Services/Reader/Panels/PanelDetectionService.cs`, the Reader close path
**What:** `ConcurrentGarbageCollection`, `ServerGarbageCollector=false`, `System.GC.ConserveMemory=1`. ONNX `IntraOpNumThreads = 2`; `PanelDetectionService.Unload()` called when the Reader closes.
**Verify:** build; check the generated `runtimeconfig.json`; `PanelDetectionService` unload/reload test if the model file is present in the test output.

## Not done in this pass (needs the running app, on screen)
- Recording the 4.1 baselines and the before/after numbers.
- Moving legacy card/strip sites onto the grid pipeline (XAML changes across screens).
- The `TieredPGO`-off and ONNX arena experiments.
- The UI-thread trace and any timer consolidation (4.6).
- Reproducing the cancelled-Verify crash on the installed build (4.0); checked in source only.

## Deviation from the spec
`CoverDecodeQueue` keeps its current worker count. Its own comment records a measurement that more workers than cores helps on a slow disk, and the workers already run below normal priority; the spec's "cores − 1" was not based on a measurement.
