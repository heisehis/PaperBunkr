# Reader pipeline — rev-3 addenda — Implementation Plan
*Implements: `docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md` §15 (targeting the shipped types in §16)*

Branch: `claude/reader-rev3-addenda` (off `master` @ `959ec51`, which has PR #68).
Order is risk-first per §15: **1 → 4 → 3 → 2 → 5**. Steps are mostly independent; only Step 3's
`ReleaseDetail` seam is referenced by Step 3's own PageCanvas edit.

Surface-area notes from reading the shipped code (some narrower than §15 assumed):

- `SharpCompressAccessorSession.ReadAll` and `ZipSharpAccessorSession.ReadEntryBytes` **already**
  allocate one exact-size `byte[]` from `entry.Size` — no doubling. Item 4's only real churn is
  `MultiExtractToStreamsCallback`'s growing `MemoryStream` (7z path). Spec §4.7 / §15 #4 to be
  trimmed accordingly.
- `PageCanvas` **never disposes** the detail bitmap (drops the ref, GC reclaims) — so Item 3's
  reservation can't be released by "caller disposes". Needs an explicit `ReleaseDetail()` seam.
- `Cache<K,T>.SizeCapacity` setter calls `Trim()` immediately; `Trim` skips *locked* items. So
  holding an `IItemLock` on the active page across a `SizeCapacity` drop protects it.
- `SevenZipEngine.OpenSession` is gated on `libraryMode`.

---

## Step 1: 7z COM affinity — dedicated executor thread in `SevenZipAccessorSession`

**Files:**
- `src/Paperbunkr.Engine/IO/Provider/Readers/Archive/SevenZipEngine.cs` (edit — the nested `SevenZipAccessorSession`)
- `src/Paperbunkr.App.Tests/SevenZipAccessorSessionTests.cs` (edit — add affinity test)

**What:**
- Add a private `ComExecutor` to `SevenZipAccessorSession`: one long-lived `Thread`
  (`IsBackground = true`, `Name = "7z-session-com"`), `SetApartmentState(ApartmentState.STA)`
  **guarded by `OperatingSystem.IsWindows()`** (7z.dll is Windows-only; elsewhere a plain
  dedicated thread still gives affinity), draining a `BlockingCollection<Action>` in a loop.
  `Post(Action)` enqueues + waits on a per-call `ManualResetEventSlim`; `Post<T>(Func<T>)`
  returns the result (exceptions marshalled back and rethrown, or swallowed to match the
  existing return-null contract).
- Route **every** `IInArchive` touch through the executor:
  - `TryOpen`: `OpenArchive` + the `GetNumberOfItems` / `GetProperty` name-map loop run inside
    one posted action; the executor thread is created first, everything else posted to it.
  - `ReadEntryBytes`: the `archive.Extract(...)` + `callback.GetResults()` run in a posted
    action. `passedByBuffer` / `maxExtracted` are only mutated inside posted actions (single
    thread → no lock needed).
  - `Dispose`: post `handle.Dispose()` (→ `Marshal.ReleaseComObject`), then
    `_executor.CompleteAdding()` + `Join()` the thread.
- `SevenZipEngine.OpenSession` unchanged (still `libraryMode`-gated); `TryOpen` signature
  unchanged (it blocks on the executor internally).
- The pipeline's `_readerLock` stays as-is (caller serialisation + it still guards the
  non-session `GetByteImage` fallback).

**Depends on:** none

**Verify:**
- `SevenZipAccessorSessionTests`: new `Session_AllComCallsRunOnOneThread` — inject a probe
  (`internal static Action<int>? OnComThreadObserved` or capture `Thread.CurrentThread.ManagedThreadId`
  around each COM call via a test hook) and assert every recorded id is equal and ≠ the test
  thread's; issue two `ReadEntryBytes` from `Task.Run` and assert both marshalled.
- Existing `SevenZipAccessorSessionTests` (same-bytes-as-stateless, in/out of order, dispose)
  stay green — the affinity change must not alter read results.
- `--filter "FullyQualifiedName~SevenZipAccessorSession|FullyQualifiedName~ReaderImagePipeline|FullyQualifiedName~AccessorSession"`
- **GUI ceiling:** the fast-flip AccessViolation itself stays GUI-unverified (same as the
  original fast-flip chain — no single-instance guard, user tests the installed build). Note in
  the commit / alpha-todo.

---

## Step 2: `ArrayPool` / pre-size — `MultiExtractToStreamsCallback` (7z path only)

**Files:**
- `src/Paperbunkr.Common/Compression/SevenZip/MultiExtractToStreamsCallback.cs` (edit)
- `src/Paperbunkr.Engine/IO/Provider/Readers/Archive/SevenZipEngine.cs` (edit — pass sizes to the callback)
- `docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md` (edit — trim §4.7 / §15 #4 to match reality)

**What:**
- `MultiExtractToStreamsCallback` ctor takes `IReadOnlyDictionary<int, long> knownSizes` (or a
  `Func<int,long>`). `GetStream` does `new MemoryStream(capacity)` from the known size (one
  allocation, no doubling); when the size is unknown/0, fall back to `new MemoryStream()`.
- `GetResults`: when `ms.Length == ms.Capacity` (exact pre-size, the common case), hand back
  `ms.GetBuffer()` directly — no `.ToArray()` copy; otherwise `.ToArray()` as today.
- `SevenZipAccessorSession.ReadEntryBytes`: build the size dict for the requested `indices` from
  `owner`'s `ProviderImageInfo` list (the engine already has `GetEntryList` / cached entry
  infos — thread it in; if not readily available on the session, use `archive.GetProperty(i,
  kpidSize)` inside the same posted action).
- **Spec edit:** §4.7 and §15 #4 currently name all four session impls. Change to: "The two
  stream sessions (`SharpCompressAccessorSession`, `ZipSharpAccessorSession`) already allocate
  one exact-size `byte[]` from `entry.Size` — no change needed. The gap is
  `MultiExtractToStreamsCallback`'s growing `MemoryStream`."

**Depends on:** none (independent of Step 1; touches the same file so land after Step 1 to avoid
a merge dance in `SevenZipEngine.cs`)

**Verify:**
- New `MultiExtractToStreamsCallbackTests` (`Paperbunkr.Common.Tests` if it exists, else
  `App.Tests`): given pre-sized entries, `GetResults` returns arrays of exact length and does
  not over-allocate; unknown-size entry still round-trips correctly.
- `Paperbunkr.Benchmarks` `ReaderPipelineBenchmarks` `[MemoryDiagnoser]` on the `solid-cb7` /
  sequential-flip benchmark: LOH bytes/op drops vs. the recorded PR #68 baseline (record the
  new number; not a CI gate).
- Existing 7z session + pipeline tests stay green.

---

## Step 3: Detail-tier budget accounting

**Files:**
- `src/Paperbunkr.App/Services/Reader/IReaderPageSource.cs` (edit — add `ReleaseDetail()`)
- `src/Paperbunkr.App/Services/Reader/ReaderImagePipeline.cs` (edit — store budget, reserve/release around `GetDetailPage`)
- `src/Paperbunkr.App/Views/PageCanvas.cs` (edit — call `ReleaseDetail()` from `ClearDetail()` and the no-detail paths)
- `src/Paperbunkr.App.Tests/Reader/ReaderImagePipelineTests.cs` (edit — budget asserts)

**What:**
- `ReaderImagePipeline` stores `_budget` (the `ReaderMemoryBudget`, currently only its
  `.DisplayBytes` is read in the ctor) and adds `long _reservedDetailBytes` + `int
  _detailReservedForPage = -1` under `_sync`.
- `GetDetailPage(index, target)`:
  1. `ReleaseDetailReservation()` (drop any prior reservation → restore
     `_displayCache.SizeCapacity = _budget.DisplayBytes`).
  2. `long reserve = checked((long)target.Width * target.Height * 4)`.
  3. Under `_sync`: `using var pin = _displayCache.LockItem(DisplayId(_activePageIndex), (Func<PageId,ReaderBitmap>)null!)`
     — hold the active page's lock so `Trim` skips it; then
     `_displayCache.SizeCapacity = Math.Max(1L, _budget.DisplayBytes - reserve)` (setter trims
     LRU, out-of-window pages go first); record `_reservedDetailBytes = reserve`,
     `_detailReservedForPage = _activePageIndex`. Release the pin.
  4. Decode + scale as today, return the bare `Bitmap`.
- `ReleaseDetail()` (new interface member) → `ReleaseDetailReservation()`.
- `SetVirtualizationWindow`: after setting `_activePageIndex`, if `_detailReservedForPage >= 0 &&
  _detailReservedForPage != _activePageIndex`, `ReleaseDetailReservation()` (page changed under a
  stale reservation — belt-and-braces alongside the explicit `ReleaseDetail`).
- `Dispose`: `ReleaseDetailReservation()` before cache disposal (harmless, keeps invariants).
- `PageCanvas`: call `src.ReleaseDetail()` inside `ClearDetail()` (guard: only when
  `Decoder is IReaderPageSource src`) and in the two early-return no-detail branches around
  line 1748–1761. `ClearDetail` is already called on page turn / zoom-out / dispose.
- Fake `IReaderPageSource` in tests (if any) gets a no-op `ReleaseDetail`. (Only implementer is
  `ReaderImagePipeline`; test doubles are of `IPageImageDecoder`, so likely nothing to fix —
  confirm during the step.)

**Depends on:** none

**Verify:**
- `ReaderImagePipelineTests`: with `ReaderMemoryBudget` pinned small (via `TryOpen`'s
  `userMemoryLimitMb`) and the display cache filled by a scripted window walk, assert
  `GetDetailPage` for a large target drops `_displayCache` `DataSize` (expose via a test-only
  accessor or reuse `DecodedPageCount` + a size probe) so `display + reserved ≤ budget + 1`;
  the `ActivePageIndex` page survives even as LRU; `ReleaseDetail()` restores `DecodedPageCount`
  headroom / `SizeCapacity`.
- `--filter "FullyQualifiedName~ReaderImagePipeline|FullyQualifiedName~PageCanvas|FullyQualifiedName~ReaderCacheTypes"`
- Manual GUI (deferred, note it): zoom past 1.5× on a large scan in a memory-pinned session →
  no runaway; zoom out → capacity restored.

---

## Step 4: PDFium page-object cache in `PdfiumAccessorSession`

**Files:**
- `src/Paperbunkr.Engine/IO/Provider/Readers/Pdf/PdfiumReaderEngine.cs` (edit)
- `src/Paperbunkr.App.Tests/Reader/PdfPipelineSessionTests.cs` (edit — load-count assert)

**What:**
- Split `RenderPageToJpeg(PdfDocument, int, bool)` into `RenderPage(PdfPage page)` (size calc +
  `Bitmap` + `page.Render` + `ImageToBytes`) and keep a thin
  `RenderPageToJpeg(doc, index, disposeDoc)` for the stateless `ReadByteImage` path that loads
  and `using`-disposes the page around `RenderPage`.
- `PdfiumAccessorSession` gains an LRU of up to **3** `PdfPage` objects (`Dictionary<int,PdfPage>`
  + a `LinkedList<int>` access order, or a small array — 3 entries, keep it trivial):
  - `ReadEntryBytes(index)`: reuse the cached `PdfPage` if present (move to MRU), else
    `_doc.Pages[index]` and insert; on the 4th distinct page dispose (`FPDF_ClosePage`) the LRU
    entry. Call `RenderPage(page)`, leave the page open.
  - `Dispose`: dispose all cached `PdfPage`s, then `((IDisposable)_doc).Dispose()`.
- All calls stay on the pipeline reader thread (PDFium affinity) — no new threading; the
  pipeline already serialises via `_readerLock`.

**Depends on:** none

**Verify:**
- `PdfPipelineSessionTests`: a test PDF fixture (reuse whatever `PdfPipelineSessionTests`
  already builds); render sequence `[2,3,4,2]` loads a `PdfPage` exactly 3 times (probe via an
  `internal static Action<int>? OnPageLoaded` hook on `PdfiumReaderEngine`), a 4th distinct page
  disposes one; `Dispose` closes all. Bytes for a repeat render equal the first.
- `--filter "FullyQualifiedName~PdfPipelineSession|FullyQualifiedName~Pdf"`
- Existing PDF reader tests + `PdfPageReaderScreenViewModelTests` stay green.

---

## Step 5: Fringe-recompute debounce in `SetVirtualizationWindow`

**Files:**
- `src/Paperbunkr.App/Services/Reader/ReaderImagePipeline.cs` (edit)
- `src/Paperbunkr.App.Tests/Reader/ReaderImagePipelineTests.cs` (edit)

**What:**
- Split `SetVirtualizationWindow(min, max)`:
  - **Immediate, every call:** clamp; set `_activePageIndex`; (Step 3 release check);
    `DrainPendingDispose()`; ensure `_window` contains `[min..max]` and evict decoded bitmaps
    *outside a safe always-valid bound* `[min - BackFringe .. max + MaxForwardFringe]` (using
    the max possible forward fringe so a debounced-away precise pass never leaves an
    out-of-true-fringe page resident longer than one debounce interval); high-priority
    `TryEnqueue` for `[min..max]`.
  - **Debounced (~30 ms trailing, threadpool `Timer` reset each call):** `RecomputeFringe()` —
    read `_pendingMin/_pendingMax` (last values), compute `fringeMin/fringeMax` from
    `_forwardFringe`, precise eviction outside `[fringeMin..fringeMax]`, rebuild `_window`,
    `_enqueued` prune, low-priority `TryEnqueue` for the fringe pages.
- New fields: `int _pendingMin, _pendingMax`; `readonly System.Threading.Timer _fringeTimer`
  (created in ctor, `Change(30, Timeout.Infinite)` on each `SetVirtualizationWindow`);
  `_fringeTimer.Dispose()` in `Dispose`. Guard `RecomputeFringe` against a disposed pipeline
  (`_cts.IsCancellationRequested`).
- `_forwardFringe` adaptation in `RecordDecodeMs` is untouched.

**Depends on:** Step 3 only for the `ReleaseDetailReservation()` call placement (cosmetic — can
land in either order, reconcile the one shared method).

**Verify:**
- `ReaderImagePipelineTests`: a burst of 10 `SetVirtualizationWindow` calls within 30 ms
  triggers exactly one `RecomputeFringe` (probe via `internal Action? OnFringeRecomputed`);
  10 calls spaced 50 ms → 10. High-priority enqueue + out-of-safe-bound eviction happen on
  every call regardless (assert `_window`/`DecodedPageCount` behaviour). No page inside the
  final window is ever missing after the debounce settles.
- `--filter "FullyQualifiedName~ReaderImagePipeline"`
- `Paperbunkr.Benchmarks` sequential-flip: enqueue count / wasted-decode count drops (record).

---

## Cross-cutting

- **Test suite:** targeted `--filter` runs per step (the full `App.Tests` suite mass-flakes
  headless under load — memory `project_paperbunkr_full_suite_headless_flake`). Run
  `Data.Tests` in full at the end (no reader impact expected, but Step 2/3 touch nothing DB —
  skip unless a migration sneaks in, which it must not).
- **No migration.** None of these five items touches the schema (`ReaderMemoryLimitMb` already
  shipped in PR #68).
- **Spec sync:** Step 2 edits §4.7 / §15 #4 to match the as-found session code. After all five
  land, flip §15's "none are implemented yet" and the §13 "Phase 5" row to done, and add a
  rev-4 revision-history line.
- **`docs/alpha-todo.md`:** add a line under the reader section (rev-3 addenda shipped;
  fast-flip AV still GUI-unverified).
- **Build gotcha:** no new `.axaml` views here, so the AVLN2000 weave trap doesn't apply; still
  verify the exe launches once at the end if practical, else note GUI-unverified.
