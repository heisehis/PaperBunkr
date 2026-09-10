# Reader decode / cache / prefetch pipeline — design

**Status:** Phases 1–4 implemented and merged to `master` via **PR #68** (`825f146`), 2026-09-09.
This document additionally carries a set of **post-implementation review addenda (rev 3,
2026-09-10)** — five concurrency / memory-accounting corrections from a technical review that are
**implemented 2026-09-10** (branch `claude/reader-rev3-addenda`) on top of the shipped
pipeline. Each is threaded into its home section below and summarised in §15.
**Date:** 2026-09-08
**Supersedes/extends:** `docs/onboarding.md` §8 (the original decode-pipeline vision — this is the
concrete engineering of it), the ad-hoc decoders added in
`docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md` §2 and
`docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md` §3.
**Branch:** merged from `claude/reader-pipeline-v2`.

### Revision history

| Rev | Date | Change |
|---|---|---|
| 1 | 2026-09-08 | Initial design; four grilling rounds. |
| 2 | 2026-09-09 | Review decisions (§14): byte-budget formula, detail-tier trigger constants, Phase 3 split to its own doc. Implemented as PR #68. |
| 3 | 2026-09-10 | Post-implementation review addenda (§15), targeting the shipped types (see §16 for design-vs-as-built): (1) 7z.dll COM on a dedicated executor thread, not a pooled thread + `_readerLock`; (2) `PdfiumAccessorSession` caches up to 3 `PdfPage` objects; (3) detail-tier bitmaps accounted against the reader byte budget; (4) pre-size `MultiExtractToStreamsCallback`'s streams (the two stream sessions were already exact-size); (5) ~30 ms debounce on the prefetch-fringe recompute. Also: **§16 "As-built deltas (PR #68)"** added. |
| 4 | 2026-09-10 | Rev-3 addenda **implemented** on branch `claude/reader-rev3-addenda`. Notes vs rev-3 spec: (4) turned out near-trivial — the two stream sessions already pre-sized from `entry.Size`; only `MultiExtractToStreamsCallback` needed the fix, `GetProperty` had to move before `Extract` (7z.dll forbids it reentrant — caught in test), and the exact bytes are captured in `SetOperationResult` before `OutStreamWrapper.Dispose()` closes the stream. §4.7 rewritten to match. |

---

## 1. Problem

The reader stutters in every mode, on every class of hardware, and the causes are structural, not
tuning:

| Symptom | Root cause (verified in code) |
|---|---|
| Paged fast-flip hitch; PDF page-turn hitch | Decode is 100% synchronous on the UI thread — `ReaderScreenViewModel.RefreshCurrentPage` → `PageImageDecoder.GetPage` → decode inline. No background decode, no prefetch beyond the double-page lookahead. `context.SaveChanges()` for `LastPageRead` also runs sync on the UI thread per turn. |
| First-open lag; every-turn cost on an HDD | The container is **reopened and re-parsed on every single page read** — the default `SevenZipEngine.GetFileData` does `File.OpenRead` + a fresh 7z.dll archive open per page (and `SharpCompressEngine`/`ZipSharpZipEngine` the same with their libs). For solid `.cbr`/`.cb7` that is O(n) decompression from the block start, every page. Only the *entry list* (names+sizes) is cached — process-wide, 100 entries. |
| Memory climbs until the app is sluggish | Paged mode holds decoded bitmaps at **native resolution** (`PageImageDecoder`, ±1 window) and re-runs `CreateScaledBitmap` on the render thread every gesture frame. Both decoders' thumbnail caches are **unbounded**. There is no byte budget anywhere in the reader. |
| Webtoon scroll jank | The continuous path (`PageDecodeService`) is the best-built one — background channel decode, non-blocking cache peek, frame-coalesced push — but prefetch radius is 1, and when image adjustment is on it does a full `SkiaBitmapConverter.ToSkImage` pixel copy of *every visible page every frame*. |
| Big webtoon-strip decode spike | A tall single strip (e.g. 800×12000) is decoded at native resolution on the single background thread, then one `CreateScaledBitmap` pass. No tiling, no region decode, no progressive display. |
| Exotic formats stutter worse (WebP/HEIF/JP2/JXL) | Decoded via `System.Drawing` then **re-encoded to PNG into a `MemoryStream` and re-decoded** by Avalonia (`PageDecodeCore.Decode` fallback) — two extra full-image passes per page. |

### What already exists and is reusable

- `Paperbunkr.Common` `Cache<K,T>` — thread-safe LRU, dual bound (item count + bytes via
  `IDataSize`), single-flight on misses (`pending` dict), ref-counted `IItemLock<T>` so a visible
  page can't be evicted mid-render, `MinimalTimeInCache` hysteresis, `ItemRemoved` event for
  disposal. `T : class`. Currently unused. **Adopt as the cache tier.**
- `PageDecodeService`'s existing `System.Threading.Channels` background-decode design (per the
  resolved decision in `docs/open_items_resolved.md` — "multiple bounded channels, one per
  priority tier"). **Keep the Channels approach**; do not adopt the older callback-based
  `ProcessingQueue<K>`. *(Deviation from the brainstorming Q2 recommendation, which named
  `ProcessingQueue<K>` — corrected here because Channels is already the project's resolved
  decode-queue primitive and `PageDecodeService` already uses it successfully.)*
- The CE engine's `ImagePool` / `ImageManager` / `MemoryOptimizedImage` / `ImageDiskCache` — a
  complete two-tier cache + background-queue subsystem — is **fully dead** in the port (nothing
  constructs `CacheManager`) and is `System.Drawing`-bound. **Do not resurrect it.** Its ideas
  (compressed-bytes-in-RAM tier, item+byte bounds, priority prefetch queues) are adopted; its
  code is not.

### CE parity note

ComicRack CE relied on `ImagePool` (in-memory bounded LRU of decoded pages, default
`MemoryPageCacheCount = 25`, plus `MemoryPageCacheOptimized` = keep JPEG bytes, drop the decoded
bitmap after ~5 s idle) plus the OS file cache to hide the per-page archive reopen. Its prefetch
*driver* lived in the WinForms `ComicDisplayControl` (`cacheUpdateTimer_Tick` walked
`±(MaximumMemoryItems−15)/2` pages from the current one on every navigation). We reproduce the
*effect* — bounded decoded cache, compressed-bytes tier, forward/backward prefetch — with a
modern async design. We drop CE's fixed item-count bound in favour of an adaptive byte budget
(§5).

**CE has no keep-open archive concept anywhere** — verified against `_reference/ComicRackCE`.
Every accessor (`SharpCompressEngine`, `SevenZipEngine`, `ZipSharpZipEngine`, `PdfiumReaderEngine`)
reopens and re-parses the file on every page read; the port is faithful to this. CE tolerated it
because `ImagePool` + the OS file cache hid the cost. The session-scoped archive/PDF handle in §4
is therefore a **deliberate net-new addition**, not a divergence from CE behaviour — it is added
as a new optional interface alongside the existing stateless `ReadByteImage`, which is left
untouched.

---

## 2. Goals / non-goals

**Goals**

1. Decode never runs on the UI thread — paged, continuous, and PDF.
2. The container (archive or PDF) is opened and parsed once per reading session, not once per page.
3. Every decoded bitmap counts against a single adaptive byte budget; memory is bounded on a
   16 GB machine and generous on a large one.
4. Prefetch in all modes, adaptive to measured decode throughput and archive type.
5. One decode/cache implementation, not three (`PageImageDecoder`, `PageDecodeService`, and the
   PDF reader's private copy of the paged path).
6. A repeatable benchmark harness with regression asserts.

**Non-goals**

- No decoded-page **disk** cache (`DiskCache<K,T>` stays unused for pages). On an HDD it is double
  I/O and usually slower than re-decoding from the in-RAM compressed-bytes tier. Revisit only if
  profiling after Phase 1 shows archive re-open still dominates.
- No `SKBitmap` object pooling / size-bucketed bitmap reuse. It fights the GC's LOH handling and
  the win is marginal next to the structural fixes. Targeted allocation cuts instead (Phase 4).
- No scroll-velocity-scaled prefetch window in v1. Directional radius that adapts to throughput
  and format is simpler and an HDD can't honour an aggressive velocity window anyway. Velocity
  scaling stays a documented future refinement.
- Continuous-mode rotation / per-page fit remain out of scope (unchanged from prior specs).

---

## 3. Architecture

### 3.1 The seam: `IReaderPageSource`

> **As-built (PR #68):** the shipped interface differs from the sketch below — it is
> `IReaderPageSource : IPageImageDecoder` with `SetVirtualizationWindow(int,int)`,
> `TryGetCachedPage(int) → Bitmap?`, `GetDetailPage(int, PixelSize)`, `BackgroundDecodeCompleted`,
> `ActivePageIndex`, `SetViewportWidth`, `DecodedPageCount`. The `PageRange` / `PagePriority` /
> `ReaderPageHandle` / `PageReady` names here were not used. Full table: **§16**. Treat the code
> below as the design intent, not the API.

Replaces `IPageImageDecoder`. Window-based rather than "get page N":

```csharp
public interface IReaderPageSource : IDisposable
{
    int PageCount { get; }

    /// Declares which pages matter right now and how urgently. The pipeline decodes the
    /// window at high priority and a prefetch fringe at low priority, and evicts everything
    /// outside [window ∪ fringe]. Idempotent; cheap to call every frame.
    void SetActiveWindow(PageRange window, PagePriority priority);

    /// Non-blocking. The display-tier bitmap if already decoded, else null (caller draws a
    /// gap and waits for PageReady). Takes a lock on the cache entry for as long as the
    /// returned handle is held.
    ReaderPageHandle? TryGet(int index);

    /// On-demand higher-resolution decode for zoom-past-100% (§6). Decoded from the
    /// compressed-bytes tier, never from the display cache; caller disposes when zoom settles.
    ReaderPageHandle GetDetail(int index, PixelSize target);

    /// Bounded thumbnail sub-cache (§7).
    ReaderPageHandle GetThumbnail(int index);

    /// Raised on the pipeline's thread when a queued page lands in the display cache. The
    /// continuous renderer re-pushes; the paged VM swaps CurrentPage. Subscribers marshal.
    event Action<int> PageReady;

    void SetViewportSize(PixelSize size);   // display-tier decode target (§6)
}
```

`ReaderPageHandle` wraps `Cache<K,T>`'s `IItemLock<ReaderBitmap>` — holding it pins the entry
against eviction; disposing it releases. `PageCanvas` / the VMs hold handles only for pages they
are actively drawing.

### 3.2 `ReaderImagePipeline` — the one implementation

Lives in `src/Paperbunkr.App/Services/Reader/`. Composed of:

| Component | Responsibility | Thread-safety |
|---|---|---|
| `IReaderByteSource` — wraps an engine `IComicAccessorSession` (archives) or `IPdfDocumentSession` (PDF); `ProviderByteSource` for exotic single-image formats | Raw compressed/encoded page bytes by index, from a **handle opened once per session**. §4. | **Thread-*affine*, not merely locked.** None of the underlying libraries (SharpZipLib, SharpCompress, 7z.dll COM, PDFium) are thread-safe, and 7z.dll's COM objects are additionally *apartment-bound* — a lock is insufficient. Every call into a given session is marshalled onto that session's **owning thread** (§4.2): for 7z that is a dedicated pinned thread the `IInArchive` is created, invoked, and released on; for the others it is the pipeline's single reader thread. |
| `RawBytesCache : Cache<PageId, RawPageBytes>` | Compressed page bytes in RAM. Feeds decode + detail re-decode without re-touching the archive. `RawPageBytes : IDataSize`. | `Cache<K,T>` (RW-lock, single-flight). |
| `DecodeWorkers` | N `Task`s draining a high- and a low-priority bounded `Channel<DecodeRequest>` (Channels, per §1). Each: get bytes from `RawBytesCache` → Skia decode → downsample to display target → store. Window-staleness check at dequeue and post-decode (the existing `PageDecodeService` pattern — no per-request tokens). | Channels; `_window` snapshot check. |
| `DisplayCache : Cache<PageId, ReaderBitmap>` | Decoded, display-tier (downsampled) bitmaps. **Byte-bounded** (§5). `ItemRemoved` → `ReaderBitmap.Dispose()`. | `Cache<K,T>`. |
| `ThumbnailCache : Cache<PageId, ReaderBitmap>` | ~200 px thumbnails, small independent byte bound. | `Cache<K,T>`. |
| `PrefetchCoordinator` | Turns `SetActiveWindow` into enqueue/evict decisions; owns the adaptive radius (§8) and the rolling decode-throughput estimate. | Called on UI thread; internal state guarded. |
| `ReaderMemoryBudget` | Computes the byte budget (§5), pushes it to `DisplayCache.SizeCapacity`. | Immutable snapshot per session + settings-change hook. |

`ReaderBitmap` = a thin wrapper over `Avalonia.Media.Imaging.Bitmap` implementing `IDataSize`
(`DataSize => PixelSize.Width * PixelSize.Height * 4`) so `Cache<K,T>`'s byte bound applies.

### 3.3 Per-mode window policy (thin, on top of the pipeline)

| Mode | `SetActiveWindow` call |
|---|---|
| Paged LTR/RTL/vertical | `window = [current−1 … current+1]` (or the spread pair), `priority = High`. Prefetch fringe from the coordinator. |
| Continuous / webtoon | `window = [firstVisible … lastVisible]` from `ReaderLayoutModel.ComputeContinuousLayout`, `priority = High`. Unchanged call site (`PageCanvas.PushContinuousVisualData`), just the new interface. |
| PDF | Same as paged. Backed by an `IPdfDocumentSession` holding one `PdfDocument` (`FPDF_DOCUMENT`) open for the session — §4.4. |

The paged VM stops calling `GetPage` synchronously. On a turn it: sets `_currentPageIndex`,
calls `SetActiveWindow`, then `TryGet(current)` — hit → swap `CurrentPage`; miss → leave the old
page up (or a light spinner past ~120 ms) and swap on `PageReady`. Same non-blocking discipline
continuous mode already uses. `LastPageRead` persistence moves to a fire-and-forget background
write (debounced, matching the continuous-mode `LastPageRead` throttle already in place).

---

## 4. Archive / PDF access — open once per session

Verified against `SharpCompress.dll` 0.48.0, SharpZipLib 1.4.2, the engine accessors, and
`_reference/ComicRackCE`. Today every page read reopens and re-parses the container
(`SharpCompressEngine.ReadByteImage`, `SevenZipEngine.GetFileData`, `PdfiumReaderEngine.ReadByteImage`
— all do `Open*(source)` inside a `using` per call). The *provider object* and entry index are
already session-scoped in `PageImageDecoder`/`PageDecodeService`; only the byte read reopens.

### 4.1 `IComicAccessorSession` (new, in the engine accessor layer)

A new optional interface **added alongside** `IComicAccessor.ReadByteImage` (which is left
untouched — the stateless path still backs `ComicScanner`, metadata read/write, export, and
anything that isn't an open reading session):

```csharp
public interface IComicAccessorSession : IDisposable
{
    int Count { get; }
    byte[] ReadPageBytes(int index);   // from the held-open handle; caller serialises
}

// on IComicAccessor:
bool SupportsSession { get; }
IComicAccessorSession OpenSession(string source);   // throws / returns null if unsupported
```

Which engine backs a given `.cbz`/`.cbr`/`.cb7` is `EngineConfiguration.Default.Cb*Uses` —
**unchanged**. The session implementation mirrors that choice; there is one session impl per
engine, not per extension:

| Backing engine (per `Cb*Uses`) | Held-open handle | Access characteristics |
|---|---|---|
| **7z.dll COM** — the default for `.cbz`, `.cbr`, `.cb7` | one `IInArchive` open for the session; `Extract(indices, …)` — 7z.dll positions within solid blocks internally, extracts a contiguous range per call | Non-solid: cheap any-order. Solid: forward-mark (§4.2); backward-far re-drives from a folder checkpoint. |
| **SharpZipLib `ZipFile`** (`.cbz` when configured) | one `ZipFile`; `GetInputStream(index)` per page | Cheap — seek + one inflate, any order |
| **SharpCompress** (`.cbr`/`.cb7` when configured) | held `IArchive` for non-solid (`OpenEntryStream()` per entry); forward-only `RarReader`/`IReader` for solid | Non-solid cheap; solid forward-mark (§4.2) |
| **`.cbt`** (tar, always) | SharpCompress `TarArchive` on the retained seekable stream (**not** the current forward-only `TarInputStream`) | Cheap — seek + raw copy |

The **7z.dll COM `IInArchive`** lifecycle (the `SevenZipFactory`, `InStreamWrapper`,
`Marshal.ReleaseComObject`) already lives in `SevenZipEngine` — the session impl reuses that
machinery rather than the App layer reimplementing it. This is the reason the session lives in
the engine, not the App (resolved in review — Round 4).

**COM apartment affinity (rev 3, implemented 2026-09-10) — targets `SevenZipEngine.SevenZipAccessorSession`.**
As shipped, `SevenZipAccessorSession` has no thread or lock of its own; `ReaderImagePipeline`
serialises every call with `_readerLock` on its `RunConsumerLoopAsync` pool thread. That is not
sufficient: `IInArchive`, `InStreamWrapper`, and the `MultiExtractToStreamsCallback` are COM
objects with apartment affinity — invoking them from a thread other than the one that created
them crosses an apartment boundary and can fault natively (no managed exception, matching the
class of fast-flip AccessViolation this whole effort chased). The 7z session therefore owns a
**dedicated, long-lived executor thread**:

- Created with `ApartmentState.STA` and a simple blocking work-queue (`BlockingCollection<Action>`
  drained in a loop), one per open 7z session.
- **Every** touch of the `IInArchive` — `SevenZipFactory.CreateInArchive`, `Open`, `Extract`,
  the `InStreamWrapper` / `MultiExtractToStreamsCallback` construction, `GetProperty` in
  `TryOpen`, and `Marshal.ReleaseComObject` — is posted to that thread and awaited. The
  `IInArchive` is never handed to another thread.
- `ReadEntryBytes(name)` from the pipeline's reader thread posts the range extract to the
  executor and blocks on the result; `_readerLock` still serialises *callers* (one outstanding
  request), but correctness now comes from thread affinity, not the lock.
- The `passedByBuffer` / `maxExtracted` forward-mark state (§4.2) stays owned by the executor
  thread — only ever touched inside a posted action.
- Session `Dispose()` posts the `Marshal.ReleaseComObject` calls to the executor, signals the
  work-queue to complete, and joins the thread (a .NET STA thread tears its COM apartment down
  on exit — no explicit `CoUninitialize`).

The zip / tar / rar / SharpCompress sessions (`ZipSharpAccessorSession`,
`SharpCompressAccessorSession`, the tar session) have no apartment constraint and stay on the
pipeline's single reader thread (§8.3) under `_readerLock` as shipped.

### 4.2 Solid-archive strategy

For solid `.cbr`/`.cb7`, decompression is forward-only from the block start. The session keeps a
high-water mark of the furthest index extracted (shipped: `maxExtracted`, `passedByBuffer`,
`ForwardBatch = 8`). Every `Extract(...)` call below runs on the 7z session's pinned executor
thread (§4.1, rev 3). For an entry at index `i` (resolved from the name `ReadEntryBytes` was
given):

- `i` already extracted → served from `RawBytesCache` (the pipeline checks that first anyway).
- `i` ahead of the mark → `Extract([mark+1 .. i])` in one COM call; **every** entry it produces
  is pushed into `RawBytesCache` (all wanted soon — prefetch is forward-biased).
- `i` behind the mark but evicted from `RawBytesCache` → re-`Extract` from a checkpoint (7z.dll
  handles folder positioning; for a single-folder solid archive this restarts the folder — rare,
  backward-far navigation only).

On the SharpCompress-configured path the same shape applies via a retained forward-only
`RarReader` / `IReader`: `MoveToNextEntry()` advances the mark, and a backward-far jump reopens
the reader from the start.

Net: the O(n) solid-decode cost is paid **once across a forward reading pass**, not per page.

### 4.3 PDF session

> **As-built (PR #68):** there is **no** separate `IPdfDocumentSession`. PDF reuses
> `IComicAccessorSession` via `PdfiumReaderEngine.PdfiumAccessorSession`, whose `ReadEntryBytes`
> calls `RenderPageToJpeg(_doc, index, disposeDoc: false)` against one `PdfDocument`
> (`FPDF_DOCUMENT`) held for the session. `targetLongEdgePx` render-to-size was not implemented —
> PDFium renders at its fixed path and the pipeline downsamples. §16.

The design below (a parallel `IPdfDocumentSession`) is the historical intent.

```csharp
public interface IPdfDocumentSession : IDisposable
{
    int Count { get; }
    byte[] RenderPageBytes(int index, int targetLongEdgePx);   // 0 = native DPI
}
```

Holds one `PdfDocument` (`FPDF_DOCUMENT`) open for the session; renders pages by index on demand.
`targetLongEdgePx` lets the pipeline ask PDFium to rasterise straight to the display-tier size
(§6.1) rather than always rendering at a fixed DPI and downsampling after.
Today `PdfiumReaderEngine.ReadByteImage` does `new PdfDocument(source)` **per page** — a full
xref/object-table re-parse (confirmed; PDFiumSharpV2 1.1.4 has no incremental open). PDFium is
**not thread-safe** — all `FPDF_*` calls serialised on the pipeline's reader thread, same as
archives. `PdfGhostScript` / `PdfNative` (non-default engines) keep their current per-page path;
only the default Pdfium engine gets a session.

**Page-object cache (rev 3, implemented 2026-09-10) — targets the shipped `PdfiumAccessorSession`.**
`RenderPageToJpeg` currently does `using (PdfPage pdfPage = doc.Pages[index])` — a load
(`FPDF_LoadPage`: content-stream + resource-dict parse) and a close on every call, so a prefetch
render and a subsequent detail render (§6.2) of the same page each pay it. Add a small **LRU of
up to 3 `PdfPage` objects** on the session (keyed by page index):

- `RenderPageToJpeg` reuses the cached `PdfPage` if present, else loads it and inserts it; the
  page is left open.
- On the 4th distinct page, the least-recently-used `PdfPage` is disposed (`FPDF_ClosePage`).
- Sized at 3 so the current page plus its immediate neighbours (the paged window, §3.3) stay
  resident during a forward flip without holding the whole document's pages open.
- All load/render/close calls stay on the pipeline's reader thread (PDFium affinity); `Dispose()`
  disposes every cached `PdfPage` before `((IDisposable)_doc).Dispose()`.

### 4.4 Cost model after this change

| | Today | After |
|---|---|---|
| Open container + parse directory / xref | every page | once per session |
| `.cbz` / `.cbt` / non-solid `.cbr` page read | full reopen + full entry enum | one seek + one inflate/copy |
| solid `.cbr` / `.cb7` sequential read | O(n) from block start, every page | amortised O(1) (forward mark); O(n) once total |
| solid `.cbr` / `.cb7` random jump | O(n) | O(n) once, then cached |
| PDF page render | `new PdfDocument` (full xref parse) + render | render only |

### 4.5 Fallback

If a held-open handle errors mid-session (file moved / locked — Library Health already tracks
this), the pipeline drops to the existing stateless `ReadByteImage` for that one read and
surfaces failure through the per-page error path. No crash, no silent blank. A session that
fails to open at all (`SupportsSession == false`, or `OpenSession` throws) → the pipeline uses
the stateless path throughout, with `RawBytesCache` still absorbing repeat reads (degraded but
correct — matches today's behaviour for that format).

### 4.6 Exotic single-image formats

**WebP/HEIF/AVIF/JPEG-XL/JPEG2000/DjVu** stay on the engine provider's format-specific path via
`ProviderByteSource`. It returns the *encoded* bytes where a Skia-native decode is possible (WebP
on the bundled SkiaSharp 3.119) and only falls back to the `System.Drawing` → re-encode path for
formats Skia genuinely can't read (Phase 4 replaces the PNG round-trip with a raw pixel buffer).

### 4.7 Buffer allocation (rev 3 — **implemented 2026-09-10, narrower than first specified**)

A decompressed page is routinely 200 KB–2 MB — over the **Large Object Heap** threshold (85 KB) —
and a growing `MemoryStream` reaches its final size through geometric doubling, allocating several
LOH arrays per page. Sustained over a continuous read that is LOH churn that fragments the heap
and forces gen-2 compaction pauses that read as reader stutter.

**As found (reading the PR #68 code), most of this was already right:**

- `SharpCompressAccessorSession.ReadAll` and `ZipSharpAccessorSession.ReadEntryBytes` already
  allocate **one exact-size `byte[]` from `entry.Size`** and read straight into it — no doubling,
  and that array is the artifact the pipeline caches, not scratch. The `new MemoryStream()` +
  `CopyTo` + `ToArray` fallback in each only fires when `entry.Size` is missing/0, which does not
  happen for well-formed zip/rar/7z. Left as-is.
- The *final* `byte[]` crossing the `ReadEntryBytes` boundary is a right-sized array the caller
  owns (copied into a `RawPageBytes` cache entry immediately). Pooling the return value would
  need an ownership protocol the interface deliberately avoids.

**The one real gap — fixed:** `MultiExtractToStreamsCallback` (the 7z solid/forward-batch path)
used a plain growing `new MemoryStream()` per entry. Now:

- The callback takes `IReadOnlyDictionary<int,long> knownSizes`, gathered by
  `SevenZipAccessorSession` from `kpidSize` **before** the `Extract` call (7z.dll forbids
  reentrant `GetProperty` from inside an extract callback — this was a real bug caught in test).
- `GetStream` creates `new MemoryStream(capacity: (int)size)` — one allocation, no doubling.
- `GetResults` hands back `MemoryStream.GetBuffer()` directly (no `ToArray` copy) when the stream
  filled exactly (`Length == Capacity`); falls back to `ToArray()` otherwise.

No `ArrayPool<byte>.Shared` rentals in the end — the exact-pre-size approach removes the churn
without a rent/return lifetime to manage across the COM callback boundary. Spec §15 #4 updated to
match.

---

## 5. Memory budget

### 5.1 The number

```
budgetBytes = clamp( 0.25 * physicalRamBytes, 128 MiB, 512 MiB )     // "Auto"
```

`physicalRamBytes` from `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` (reflects the machine /
container limit). On the 16 GB target box: `clamp(4 GB, 128 MiB, 512 MiB)` = **512 MiB**. On an
8 GB box: `clamp(2 GB, …)` = 512 MiB still — so the 512 MiB ceiling is the operative limit on
any machine with ≥ 2 GB RAM; the 128 MiB floor only engages below ~512 MiB RAM. **Resolved
(review):** keep this formula as-is — 512 MiB is conservative even beside a browser on the 16 GB
target box (≥ 9 GB typically free), and the Auto / 512 / 1024 setting (§5.2) is the escape hatch
if a specific machine needs less or more. No RAM-scaled ceiling term.

### 5.2 Override

`AppSettings.ReaderMemoryLimitMb` — nullable `int`, `null` = Auto. Preferences → Reader adds a
"Reader memory limit" control: **Auto / 512 MB / 1024 MB**. Migration `AddReaderMemoryLimitMb`,
**no-op `Down()`** (per the standing rule for any new `AppSettings` column —
`docs/alpha-todo.md` migration-rollback note).

### 5.3 Split

One budget, shared — only one reader (comic *or* PDF *or* book) is open at a time. Allocation
within it:

As shipped (`ReaderMemoryBudget`, an immutable snapshot — §16):

- `DisplayBytes` (`= _displayCache.SizeCapacity`) = `TotalBytes − ThumbnailBytes`.
- `ThumbnailBytes` = `min(32 MiB, TotalBytes / 8)`.
- `RawBytesBytes` = `min(64 MiB, TotalBytes / 4)` — backs the process-wide `SharedRawCache`;
  compressed bytes are ~10–20× smaller than decoded, so this holds far more pages than the
  display cache and is what makes eviction cheap to recover from.

**Detail-tier reservation (rev 3, implemented 2026-09-10).** The shipped detail tier
(`ReaderImagePipeline.GetDetailPage`) decodes a full-page high-res bitmap outside any cache and
outside the budget — a 3000×4600×4 detail bitmap is ~55 MiB that the budget never sees, so a
zoom-in while the display cache is at its cap pushes real memory past the budget. `ReaderMemoryBudget`
shipped immutable (no setter), so the fix is one of:

- add a mutable `ReservedDetailBytes` to `ReaderMemoryBudget` and derive
  `EffectiveDisplayBytes = DisplayBytes − ReservedDetailBytes`, pushed to
  `_displayCache.SizeCapacity` whenever it changes; **or**
- keep `ReaderMemoryBudget` immutable and have the pipeline lower `_displayCache.SizeCapacity`
  directly around the `GetDetailPage` call.

Either way: before decoding, reserve `targetSize.Width * targetSize.Height * 4`; that lowers the
display cache capacity and evicts to fit — **pages outside the current virtualization window
first, and never `ActivePageIndex`** (that bitmap is drawn under the detail overlay; the shipped
peek doesn't `IItemLock` it, so this exclusion is explicit). The reservation is best-effort — if
the window itself is bigger than the reduced capacity, the detail bitmap is allowed to briefly
exceed budget rather than fail the decode. At most one detail bitmap is live at a time; a new
`GetDetailPage` releases the previous reservation first. Disposing the returned bitmap restores
the capacity.

### 5.4 `Cache<K,T>` tuning

- `MinimalTimeInCache` — CE default 5000; **verify the unit** (the CE code compares
  `DateTime.Now.Ticks − LastAccess` against it while `LastAccess` is set from `Machine.Ticks` —
  a latent unit mismatch). Wrap or set explicitly; target ~1 s hysteresis so a fast flip-back is
  a hit but memory recovers quickly under pressure.
- `ItemCapacity` — set high (e.g. 512); the byte bound is the real limit for pages.

---

## 6. Display tier and detail tier

### 6.1 Display tier (both modes, Phase 2 for paged)

Every cached page is downsampled at decode time to the **fit target** — the size it will actually
be drawn at for the current viewport and fit mode — not native resolution and not raw viewport
width:

- Continuous: cross-axis viewport size (already done by `PageDecodeService.DecodeDisplayTier`).
- Paged: `ZoomPanMath.ComputeBaseScale(viewport, nativeSize, fitMode, fitOnlyIfOversized)` ×
  native → target. `FitWidth` on a 3000 px scan in a 1200 px viewport caches a 1200-wide bitmap,
  not 3000.
- Never upscale (matches CE `FitOnlyIfOversized` precedent): if native ≤ target, cache native.
- Re-scale on viewport resize is lazy — next decode after the resize uses the new target;
  already-cached pages are left until they're re-decoded or evicted (existing behaviour).

Render-thread `CreateScaledBitmap` in paged mode then becomes a near-1:1 GPU blit in the common
case (Phase 4 removes the residual per-frame CPU rescale).

### 6.2 Detail tier

When the user zooms in past what the display tier can show sharply:

- Trigger: effective on-screen scale > **1.15×** the display-tier bitmap's native size **and**
  the zoom/pan gesture has been idle for **~150 ms** (debounce — don't decode mid-pinch). These
  two constants ship as-is and get tuned from real feel after Phase 2 lands (resolved in review),
  not guessed harder now — they're isolated in one place for that.
- Action: `GetDetailPage(index, targetSize)` decodes the **full page** (not a region — region/tile
  decode is Phase 3) from the raw-bytes tier (`SharedRawCache`) at the needed resolution — shipped
  as `PageDecodeCore.TryDecodeBytes(bytes) → CreateScaledBitmap(targetSize)`. Returns a bare
  `Bitmap` the caller disposes; `PageCanvas` swaps it in for the display-tier bitmap and drops it
  on zoom-out (Phase 2 wired the trigger via a settle-timer keyed off `ActivePageIndex`).
- **Budget (rev 3, implemented 2026-09-10):** before decoding, reserve
  `targetSize.Width * targetSize.Height * 4` per §5.3, which shrinks the display cache's
  `SizeCapacity` and evicts unleased display entries to make room. At most one detail bitmap is
  live at a time (a new `GetDetailPage` releases the previous reservation first). The reservation
  is released the instant the caller disposes the returned bitmap.
- Release: dropped as soon as effective scale falls back below the trigger, or the page leaves
  the window. Never enters the display cache; its bytes are still counted (via the reservation)
  while alive.
- Interaction with `SharedRawCache`: because the compressed bytes are still cached, a detail
  decode after settling costs one Skia decode, no archive I/O.

---

## 7. Thumbnails

Fold both current unbounded `_thumbnailCache` dicts into `ThumbnailCache : Cache<PageId,
ReaderBitmap>` with the §5.3 byte reserve. ~200 px longest edge (unchanged). For a typical
200-page issue at ~200 px that's well under 32 MiB, so the rail stays fully cached; a
2000-"page" combined volume evicts LRU beyond the reserve. The reader's background thumbnail
generation loop (`ReaderScreenViewModel.StartThumbnailGeneration`) keeps its
`_loadGeneration` staleness guard and just calls `GetThumbnail`.

---

## 8. Prefetch

### 8.1 Radius

Directional, adaptive, **not** velocity-scaled:

- The **window** (§3.3) is what must be resident to draw the current view — paged: current ±1;
  continuous: first…last visible. The **prefetch fringe** extends beyond it.
- Fringe seed: **+2 / −1** pages beyond the window edge (forward / backward) — so paged forward
  reach is `current+3` at rest.
- The `PrefetchCoordinator` keeps a rolling average of the last ~8 decode wall-times and the
  archive-read wall-times. When decode throughput comfortably exceeds the page-turn / scroll
  rate, widen forward toward **+6**; when the read stage (solid archive on HDD) is the
  bottleneck, hold at **+2** — prefetching further just queues work the single reader thread
  can't clear before it's needed.
- Backward stays small (−1, −2 when throughput is ample). Re-reading is cheap when the bytes are
  still in `RawBytesCache`.

### 8.2 Priority and cancellation

- Window pages → high-priority channel; fringe → low. Consumer always drains high first (existing
  `PageDecodeService` loop).
- Cancellation = window-staleness check at dequeue and again post-decode against the current
  `_window` snapshot (existing pattern). No `CancellationToken` per request.
- Dedup: a page already in `DisplayCache` or already queued is not re-enqueued (existing
  `_enqueued` set).

### 8.3 Threads

- **1** archive-read thread (sequential-archive-friendly; one reader lock). For a 7z session, this
  thread posts to and awaits the session's dedicated COM executor thread (§4.1) rather than
  touching `IInArchive` directly.
- **N = clamp(ProcessorCount − 1, 1, 4)** Skia decode/resample workers, pulling already-read
  compressed bytes from `RawBytesCache`.
- Read and decode are decoupled: the reader thread races ahead filling `RawBytesCache`; workers
  consume it in parallel.

### 8.4 Fast-flip fringe debounce (rev 3, implemented 2026-09-10) — targets `ReaderImagePipeline.SetVirtualizationWindow`

`SetVirtualizationWindow(min, max)` is called on every page turn / scroll frame (from
`PageCanvas`). As shipped it does the full pass every call: recompute `[fringeMin..fringeMax]`
from `_forwardFringe`, evict decoded bitmaps outside it, rebuild `_window`, prune `_enqueued`,
enqueue the window on the high channel and the fringe on the low. There is **no `PrefetchCoordinator`
class** — this logic is inline in the method.

The **window** part must apply immediately — that is the page the user is looking at. The
**fringe** part does not: during a rapid sequential flip (holding the page-turn key, repeated
taps) the fringe shifts one page per turn and every fringe page the single reader thread starts
is superseded before it is read.

Split `SetVirtualizationWindow` so the window (evict-outside-window + high-priority enqueue)
still runs synchronously every call, but the **fringe recompute + low-priority enqueue is
debounced ~30 ms**: a burst of calls within that window schedules the fringe pass once, on the
trailing edge, against the final window position. A single deliberate turn (no follow-up within
30 ms) is unaffected. A threadpool timer, reset on each call, same shape as the shipped
`FlushPagedPush` render burst-guard — applied to the prefetch queue instead of the render push.
`_forwardFringe` adaptation (§8.1) stays where it is; only the recompute cadence changes.

---

## 9. Cross-issue behaviour

On issue switch (or reading-mode switch, which forces a reload):

1. Dispose every `DisplayCache` + `ThumbnailCache` bitmap immediately (don't wait for GC —
   existing discipline).
2. Keep the **previous** container's `RawBytesCache` entries until the new container's first
   window has decoded, then flush them. Gives instant back-navigation (very common — "wrong
   issue, go back") without holding two volumes of decoded pages at once.
3. Dispose the previous `IComicAccessorSession` handle (archives and PDF both — §16).

---

## 10. Render-thread cleanup (Phase 4)

Not the pipeline itself, but the same effort:

- **WebP/HEIF/JP2/JXL:** decode natively through Skia where the bundled SkiaSharp supports it
  (WebP does); keep the `System.Drawing` → PNG re-encode path only for formats it genuinely
  can't (HEIF/AVIF/JXL/JP2/DjVu), and even there re-encode to a **raw pixel buffer** Avalonia can
  wrap, not PNG.
- **`SkiaBitmapConverter` output cache:** keyed by source `Bitmap` reference, evicted with the
  window — fixes the per-visible-page, per-frame double pixel copy when image adjustment is on
  (`RenderContinuous` lease path).
- **Paged per-frame `CreateScaledBitmap`:** with the display tier now at draw size (§6.1), draw
  the source bitmap directly (GPU scale) in the common case, mirroring
  `ResolveContinuousDrawBitmap`.
- ~~**`ArrayPool<byte>`** for the read/extract buffers in the accessor-session implementations.~~
  **Reclassified as a Phase-1-class requirement, shipped in the §15 batch** — see §4.7 (rev 3).
- **`ReaderPerfStats`** (shipped name; drafted as `ReaderFrameStats`) — opt-in overlay, toggled
  with **Ctrl+Shift+P**: cache hit ratio, decode wall-times, budget use, decoded-page count. This
  is how the brief's "< 1 ms render" target is *observed*; it is not a CI gate (compositor timing
  is too environment-variable).

---

## 11. Big webtoon strips (Phase 3)

- `SKCodec.GetPixels` with a subset `SKImageInfo` / incremental decode: a 12 000 px strip decoded
  in horizontal **bands** as it scrolls into view.
- `ReaderLayoutModel` gains a notion of a partially-decoded page (band offsets + which bands are
  resident) so it can place a strip that isn't fully decoded.
- Progressive display: draw the resident bands, request the next as the scroll approaches it,
  evict bands that scroll far out of view (a strip's bands are individually LRU'd within the
  page's own byte allowance).
- **Resolved (review): Phase 3 gets its own focused mini-design doc** when we reach it
  (`docs/superpowers/specs/<date>-reader-webtoon-strip-band-decode-design.md`). This section is
  the direction, not the detailed design. The pipeline seam (`TryGet` could return a band-aware
  handle) is built in Phase 1 to accommodate it without a rewrite.

---

## 12. Testing

### 12.1 `Paperbunkr.Benchmarks` (new project, BenchmarkDotNet)

- **Fixtures generated in `[GlobalSetup]`** — procedural JPEG pages, nothing binary checked in:
  - `manga` — 1600×2560, 180 pages, `.cbz`
  - `comic` — 2048×3072, 24 pages, `.cbz`
  - `webtoon` — 800×12000, 20 pages, `.cbz`
  - `solid-cb7` — 1600×2560, 60 pages, generated as a solid `.cb7` (7-Zip can write solid 7z, so
    this fixture is procedurally generatable and exercises the held-open-`IInArchive` forward-mark
    path). RAR cannot be written by any bundled library; **one** tiny hand-made solid `.cbr`
    (~5 pages, a few KB) is checked in under `tests/fixtures/` for a correctness test of the
    same forward-mark logic on the RAR path, not a benchmark.
- **Benchmarks:**
  - cold open → first page visible
  - sequential flip (time/page, steady state)
  - random seek (time/page)
  - continuous scroll simulation (pages decoded/sec at a fixed scroll velocity)
  - `[MemoryDiagnoser]` allocations/op for each of the above
  - cache-hit ratio under a scripted navigation trace
- Bootstraps headless Avalonia exactly as `Paperbunkr.App.Tests` does (`TestAppBuilder`).

### 12.2 xUnit perf-regression asserts (`Paperbunkr.App.Tests`)

- Decode is never invoked on the UI thread (assert via a dispatcher-affinity probe in the
  pipeline's decode path).
- The accessor session opens the container once for a sequential read of all pages (assert
  open/parse count == 1 via an injected counter); solid-archive forward-mark never re-extracts an
  already-passed index during a forward pass.
- `DisplayCache` total `DataSize` never exceeds `SizeCapacity` + one in-flight page, under a
  scripted 200-page scroll with the budget pinned to 64 MiB.
- LRU correctness: after scrolling away, the pages left resident are exactly the window ∪ fringe.
- Detail tier: a decoded detail bitmap is never added to `DisplayCache`; is released when zoom
  returns to fit.
- No functional regression: existing reader tests (`PageImageDecoder`/`PageDecodeService` tests
  migrate to the new seam) stay green.

### 12.3 "Throttled mode" dev flag

`--reader-throttle` (or an env var): forces N=1 decode workers and injects ~15 ms latency per
archive read, simulating the old-HDD box for a manual pass on dev hardware.

### 12.4 Rev-3 addendum asserts (§15)

- **7z executor affinity:** in `SevenZipAccessorSessionTests`, the `IInArchive` and every COM
  wrapper it owns are created, used, and released on one thread — assert via a captured
  `Thread.ManagedThreadId` recorded at each COM touch through an injected probe; all recorded ids
  equal, and ≠ the calling thread's. An `Extract` issued from a pool thread is marshalled, not
  run inline.
- **PDFium page-object cache:** in `PdfPipelineSessionTests`, `RenderPageToJpeg` for the sequence
  `[2,3,4,2]` loads a `PdfPage` exactly 3 times (page 2 is a cache hit); a 4th distinct page
  disposes the LRU `PdfPage`. `Dispose` disposes all cached pages before the `PdfDocument`.
- **Detail-tier budget:** with the budget pinned to 64 MiB and the display cache full,
  `GetDetailPage` for a 40 MiB target evicts out-of-window display entries so that
  `display DataSize + reservedDetailBytes` ≤ budget + one in-flight; disposing the returned
  bitmap restores `_displayCache.SizeCapacity`. The `ActivePageIndex` entry is never evicted to
  make room (assert it survives even when it is the LRU entry).
- **ArrayPool:** a scripted 50-page sequential read through the pipeline allocates no `byte[]`
  > 85 KB on the LOH for scratch buffers (assert via BenchmarkDotNet `[MemoryDiagnoser]`
  LOH-bytes/op below a threshold, and/or a rented-vs-`new` audit in the session impls +
  `MultiExtractToStreamsCallback`).
- **Fringe debounce:** 10 `SetVirtualizationWindow` calls within 30 ms schedule exactly one
  fringe recompute (trailing edge, final window); 10 calls spaced 50 ms apart schedule 10. The
  evict-outside-window + high-priority window enqueue fires on every call regardless.

---

## 13. Phases

| Phase | Scope | Ship criterion |
|---|---|---|
| **1 — Foundation** | §3 pipeline, §4 `IComicAccessorSession` (zip/tar/rar/7z) + `IPdfDocumentSession`, §5 budget + setting, §8 prefetch, paged+PDF moved async, §7 thumbnails, §12.1 + §12.2 harness. | All modes decode off the UI thread; container opened once/session (archive **and** PDF); budget respected in tests; benchmark numbers recorded as the baseline; no functional regression. |
| **2 — Tiers** | §6 display-tier downsampling for paged + §6.2 detail tier, §9 cross-issue retention. | Paged mode holds no native-res bitmaps; zoom-in stays sharp; memory flat across issue switches. |
| **3 — Big strips** | §11. | A 12 000 px webtoon strip scrolls without a decode spike; peak memory for a webtoon volume within budget. |
| **4 — Render cleanup** | §10. | WebP/HEIF/etc route through the engine's `ConvertToJpeg`; `ReaderPerfStats` overlay (Ctrl+Shift+P); `SkiaBitmapConverter` output cached per-frame. |
| **Phases 1–4** | — | **Shipped: PR #68 (`825f146`), 2026-09-09.** |
| **5 — Review addenda** | §15 (items 1–5). | **Done 2026-09-10** (branch `claude/reader-rev3-addenda`): 7z COM executor thread; PDFium `PdfPage` LRU(3); detail tier counted against budget; `MultiExtractToStreamsCallback` pre-sized; fringe-recompute debounced. §12.4 asserts green; fast-flip AV still GUI-unverified. |

---

## 14. Review decisions

All three open questions resolved with the drafted recommendations (review, 2026-09-09):

1. **Byte-budget Auto formula** — `clamp(25% RAM, 128, 512) MiB` stays as-is; no RAM-scaled
   ceiling term. Rationale in §5.1.
2. **Detail-tier trigger** — 1.15× / 150 ms ship as constants, tuned from feel after Phase 2.
   §6.2.
3. **Phase 3** — its own mini-design doc when reached; §11 is direction only.

The four grilling rounds and the 2026-09-09 review closed the design frontier for the shipped
implementation (PR #68).

---

## 15. Post-implementation review addenda (rev 3 — **implemented 2026-09-10**, branch `claude/reader-rev3-addenda`)

A technical review of the shipped pipeline (PR #68) flagged five concurrency / memory-accounting
corrections. All five are now implemented; the "as-built" column records how each landed.

| # | Change | As built | Why it matters |
|---|---|---|---|
| 1 | **7z.dll COM on a dedicated executor thread**, not a pooled thread + `_readerLock`. | `SevenZipEngine.ComExecutor` — one long-lived background thread (STA on Windows, plain elsewhere), a `BlockingCollection<Action>` work queue. `SevenZipAccessorSession` posts **every** `IInArchive` touch to it: `OpenArchive` + the name-map loop in `TryOpen`, `Extract` + `MultiExtractToStreamsCallback` + `GetProperty(kpidSize)` in `ExtractOnComThread`, `handle.Dispose()` (→ `Marshal.ReleaseComObject`) in `Dispose`. `passedByBuffer` / `maxExtracted` are thread-confined (no lock). Pipeline `_readerLock` unchanged (caller gate + stateless `GetByteImage` path). | `IInArchive` + the extract callback are apartment-bound COM; invoking them off their creating thread can fault natively with **no managed exception** — the fast-flip AccessViolation signature. A lock serialises callers; it does not pin affinity. **Verified:** thread-affinity + concurrent-read tests green. The AV fix itself stays GUI-unverified (same ceiling as the original fast-flip chain). |
| 2 | **`PdfiumAccessorSession` caches up to 3 `PdfPage` objects** (LRU by index). | `RenderPageToJpeg` split into `RenderPage(PdfPage)` + a stateless loader. Session keeps `Dictionary<int,PdfPage>` + a `LinkedList<int>` LRU (front = MRU); `GetOrLoadPage` reuses or `FPDF_LoadPage`s, evicts+`Dispose()`s the LRU past 3; `Dispose` closes all then the doc. All on the pipeline reader thread. **Verified:** `[2,3,4,2]` → 3 loads; evicted page reloads. | A prefetch render and a later detail render of the same page each re-parse its content stream + resource dict otherwise. |
| 3 | **Detail-tier bitmaps accounted against the reader byte budget.** | `ReaderImagePipeline._budget` now stored; `_reservedDetailBytes` / `_detailReservedForPage` under `_sync`. `GetDetailPage` → `ReserveDetailBudget`: pins the requested page's cache entry with an `IItemLock`, drops `_displayCache.SizeCapacity` to `DisplayBytes − w*h*4` (the setter's `Trim` evicts LRU, skips the pinned page), records the reservation. `ReleaseDetail()` (new `IReaderPageSource` member, called from `PageCanvas.ClearDetail`) and a page-change check in `SetVirtualizationWindow` both restore full capacity. One reservation at a time. **Verified:** eviction happens, active page survives, `ReleaseDetail` restores headroom. | A ~55 MiB detail bitmap decoded entirely outside the budget pushes real memory past the cap on a zoom-in. |
| 4 | **Pre-size `MultiExtractToStreamsCallback`'s streams** (§4.7 — narrower than first specified). | The two stream sessions already allocate one exact `byte[]` from `entry.Size`; untouched. The callback now takes `knownSizes` (gathered from `kpidSize` **before** `Extract` — 7z.dll forbids it reentrant), does `new MemoryStream(capacity)`, and captures the bytes in `SetOperationResult` (`GetBuffer()` when exact, `ToArray()` otherwise) **before** `OutStreamWrapper.Dispose()` closes the stream. | Growing `MemoryStream` reaches size through geometric doubling — several LOH arrays per page. |
| 5 | **~30 ms debounce on the prefetch-fringe recompute.** | `SetVirtualizationWindow` split: immediate = set `_activePageIndex`, detail-release check, evict outside the widest bound `[min−BackFringe … max+MaxForwardFringe]`, ensure `_window` covers it, high-priority enqueue `[min…max]`, arm `_fringeTimer.Change(30, ∞)`. `RecomputeFringe` (trailing edge) = precise fringe from `_forwardFringe`, exact eviction, `_window` rebuild, low-priority fringe enqueue. **Verified:** a 12-call burst → 1 recompute; spaced calls → 1 each. | Every fringe decode a held-key flip starts is superseded before the single reader thread reads it. |

### Implementation notes

- Items 1 and 4: engine (`Readers/Archive/SevenZipEngine.cs`, `Common/Compression/SevenZip/MultiExtractToStreamsCallback.cs`).
  Items 2: engine (`Readers/Pdf/PdfiumReaderEngine.cs`). Items 3, 5: App (`Services/Reader/ReaderImagePipeline.cs`,
  `Services/Reader/IReaderPageSource.cs`, `Views/PageCanvas.cs`).
- Item 1 supersedes the `_readerLock`-only approach for the 7z path; `_readerLock` stays as the
  caller-serialisation gate and for zip/tar/rar (no apartment constraint on those).
- Item 3 added `IReaderPageSource.ReleaseDetail()` and kept `ReaderMemoryBudget` immutable — the
  pipeline drives `_displayCache.SizeCapacity` directly. `IComicAccessorSession` unchanged;
  `MultiExtractToStreamsCallback` gained an optional `knownSizes` ctor arg. `Paperbunkr.Engine`
  gained `[InternalsVisibleTo("Paperbunkr.App.Tests")]` for the two test seams
  (`SevenZipEngine.OnSessionComWork`, `PdfiumReaderEngine.OnPageLoaded`).
- Plan: `docs/superpowers/specs/2026-09-10-reader-rev3-addenda-plan.md`. Tests in
  `SevenZipAccessorSessionTests`, `PdfPipelineSessionTests`, `ReaderImagePipelineTests`
  (§12.4). Full-suite build + targeted reader tests green; the fast-flip AccessViolation
  remains GUI-unverified.

---

## 16. As-built deltas — what PR #68 shipped vs this design

PR #68 (`825f146` / squashed `ed758bb`, merged to `master` 2026-09-09) implemented Phases 1–4.
Several seams shipped differently from §3–§4 above; the design sections are kept as the historical
record and this section is the reconciliation. Nothing here is a defect — they are deliberate
simplifications made during implementation — but the rev-3 addenda (§15) and any future work must
target the shipped names.

| Design (§3–§4) | Shipped (PR #68) | Note |
|---|---|---|
| `IReaderPageSource : IDisposable`, clean-slate | `IReaderPageSource : IPageImageDecoder` — keeps `GetPage` / `GetThumbnail`, **adds** the window/peek/detail members | Kept `PageDecodeService`'s member names so `PageCanvas` changed only its `is`-check. |
| `SetActiveWindow(PageRange window, PagePriority priority)` | `SetVirtualizationWindow(int minIndex, int maxIndex)` | No `PageRange` / `PagePriority` types. Priority is implicit: window = high channel, fringe = low, decided inside the method. |
| `TryGet(int) → ReaderPageHandle?` that **holds** an `IItemLock` for the caller's draw | `TryGetCachedPage(int) → Bitmap?` — peeks, copies the reference out, releases the lock immediately | The "pinned against eviction while drawing" guarantee did **not** ship. Mitigation is the deferred-dispose sweep (evicted bitmaps disposed 4 s later + a 2 s timer), not a render-time lock. |
| `event Action<int> PageReady` | `event Action<int> BackgroundDecodeCompleted` | Same semantics, `PageDecodeService`'s name. |
| — | `int ActivePageIndex` (centre of last window), `void SetViewportWidth(int)`, `int DecodedPageCount` | Added members not in the §3.1 sketch. `ActivePageIndex` is what the detail-tier settle-timer reads. |
| `GetDetail(int, PixelSize) → ReaderPageHandle` | `GetDetailPage(int pageIndex, PixelSize targetSize) → Bitmap` | Caller disposes the bitmap directly. Wired in Phase 2 via a `PageCanvas` settle-timer keyed off `ActivePageIndex`. **No budget accounting** (§15 #3). |
| separate `IPdfDocumentSession { RenderPageBytes(int, targetLongEdgePx) }` | **no** `IPdfDocumentSession` — `PdfiumReaderEngine.PdfiumAccessorSession : IComicAccessorSession`, `ReadEntryBytes(name)` → `RenderPageToJpeg(_doc, index, disposeDoc:false)` | One session interface for archives and PDF. `targetLongEdgePx` render-to-size did not ship; PDF renders at its fixed path and the pipeline downsamples. No page-handle cache (§15 #2). |
| `IComicAccessorSession.ReadPageBytes(int index)` | `IComicAccessorSession.ReadEntryBytes(string entryName)` | By in-container name, so one contract spans index-addressed (7z) and name-addressed (SharpZipLib / SharpCompress) engines. `Count` unchanged. |
| session impls named per engine, unspecified | `SevenZipEngine.SevenZipAccessorSession` (nested), `SharpCompressAccessorSession`, `ZipSharpAccessorSession`, `TarSharpZipEngine` session; `SevenZip/MultiExtractToStreamsCallback` in `Paperbunkr.Common` | 7z forward-batch = **8** (`ForwardBatch`), with `passedByBuffer` holding the extra entries a range extract produced. |
| `ReaderMemoryBudget` "immutable snapshot per session + settings-change hook" | immutable snapshot, **no setter / no hook** — `Resolve(int? userLimitMb)` → `{ TotalBytes, DisplayBytes = Total − Thumbnail, ThumbnailBytes, RawBytesBytes }` | A settings change takes effect on the next issue open, not live. `RawBytesBytes` (`min(64 MiB, total/4)`) backs the process-wide `SharedRawCache`. |
| `RawBytesCache` per session | `SharedRawCache` — **process-wide** compressed-bytes tier | Gives instant issue-back-nav (§9) without a per-session copy; keyed by `PageId` including a container discriminator. |
| `AppSettings.ReaderMemoryLimitMb` + Preferences → Reader control | column + `20260909221449_AddReaderMemoryLimitMb` migration (no-op `Down()`) shipped; **Preferences control deferred** (Auto works without it) | §5.2's UI is still open work. |
| §11 big-strip band decode | design-only doc shipped: `docs/superpowers/specs/2026-09-09-reader-webtoon-strip-band-decode-design.md` | Phase 3 not implemented. |
| fast-flip AV fix (not in original design) | shipped: `_readerLock` serialising container reads; evicted-bitmap dispose deferred 4 s + 2 s timer sweep; paged render pushes coalesced to one per animation frame; transition bitmaps pre-converted to `SKImage` at message time; a coalesced multi-turn `FlushPagedPush` does an **instant swap** (no animate-from-recycled-`_lastRenderedPage`) | **GUI retest with transitions still pending** (per `docs/alpha-todo.md` and the memory note). §15 #1 and #5 harden two links of this chain. |

### Files (PR #68)

- **New (App):** `Services/Reader/{IReaderPageSource,ReaderImagePipeline,ReaderCacheTypes,ReaderMemoryBudget,ReaderPerfStats}.cs`
- **New (Engine):** `Readers/IComicAccessorSession.cs`, `Readers/Archive/{SharpCompressAccessorSession,ZipSharpAccessorSession}.cs`, `Common/Compression/SevenZip/MultiExtractToStreamsCallback.cs`
- **Deleted:** `Services/{PageImageDecoder,PageDecodeService}.cs` (+ their tests); one-shot callers → `PageDecodeCore.DecodeSinglePage`
- **Modified:** `PageCanvas.cs` (+291), `ReaderPageVisualHandler.cs` (+164), `ReaderScreenViewModel.cs`, `PdfPageReaderScreenViewModel.cs`, `PageDecodeCore.cs`, `SevenZipEngine.cs` (+124), `PdfiumReaderEngine.cs` (+68), `ReaderScreen.axaml`(.cs) (Ctrl+Shift+P perf overlay), `AppSettings.cs`
- **New (test/bench):** `Paperbunkr.Benchmarks/` (BenchmarkDotNet), `App.Tests/Reader/*`, `AccessorSessionTests`, `SevenZipAccessorSessionTests`, `PdfPipelineSessionTests`
