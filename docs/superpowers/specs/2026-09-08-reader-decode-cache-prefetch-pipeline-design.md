# Reader decode / cache / prefetch pipeline — design

**Status:** draft for review
**Date:** 2026-09-08
**Supersedes/extends:** `docs/onboarding.md` §8 (the original decode-pipeline vision — this is the
concrete engineering of it), the ad-hoc decoders added in
`docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md` §2 and
`docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md` §3.
**Branch:** `claude/reader-pipeline` (off `master`).

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
| `IReaderByteSource` — wraps an engine `IComicAccessorSession` (archives) or `IPdfDocumentSession` (PDF); `ProviderByteSource` for exotic single-image formats | Raw compressed/encoded page bytes by index, from a **handle opened once per session**. §4. | Single-threaded access, serialized by the pipeline's reader lock (none of the underlying libraries — SharpZipLib, SharpCompress, 7z.dll COM, PDFium — are thread-safe). |
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

### 4.2 Solid-archive strategy

For solid `.cbr`/`.cb7`, decompression is forward-only from the block start. The session keeps a
high-water mark of the furthest index extracted. `ReadPageBytes(i)`:

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

### 4.3 `IPdfDocumentSession` (new)

Parallel to the archive session, for the default `PdfiumReaderEngine` path:

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

- `DisplayCache.SizeCapacity` = `budgetBytes − thumbnailReserve`.
- `ThumbnailCache.SizeCapacity` = `thumbnailReserve` = `min(32 MiB, budgetBytes / 8)`.
- `RawBytesCache.SizeCapacity` = a separate, smaller allowance (`min(64 MiB, budgetBytes / 4)`) —
  compressed bytes are ~10–20× smaller than decoded, so this holds far more pages than the
  display cache and is what makes eviction cheap to recover from.

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
- Action: `GetDetail(index, neededSize)` decodes the **full page** (not a region — region/tile
  decode is Phase 3) from `RawBytesCache` at the needed resolution. Held by the caller, drawn in
  place of the display-tier bitmap.
- Release: dropped as soon as effective scale falls back below the trigger, or the page leaves
  the window. Never enters `DisplayCache`.
- Interaction with `RawBytesCache`: because the compressed bytes are still cached, a detail
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

- **1** archive-read thread (sequential-archive-friendly; one reader lock).
- **N = clamp(ProcessorCount − 1, 1, 4)** Skia decode/resample workers, pulling already-read
  compressed bytes from `RawBytesCache`.
- Read and decode are decoupled: the reader thread races ahead filling `RawBytesCache`; workers
  consume it in parallel.

---

## 9. Cross-issue behaviour

On issue switch (or reading-mode switch, which forces a reload):

1. Dispose every `DisplayCache` + `ThumbnailCache` bitmap immediately (don't wait for GC —
   existing discipline).
2. Keep the **previous** container's `RawBytesCache` entries until the new container's first
   window has decoded, then flush them. Gives instant back-navigation (very common — "wrong
   issue, go back") without holding two volumes of decoded pages at once.
3. Dispose the previous `IComicAccessorSession` / `IPdfDocumentSession` handle.

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
- **`ArrayPool<byte>`** for the read/extract buffers in the accessor-session implementations.
- **`ReaderFrameStats`** — opt-in overlay behind the existing reader diagnostics toggle:
  compositor draw time p50/p99, dropped-frame count, cache hit ratio, current budget use. This
  is how the brief's "< 1 ms render" target is *observed*; it is not a CI gate (compositor
  timing is too environment-variable).

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

---

## 13. Phases

| Phase | Scope | Ship criterion |
|---|---|---|
| **1 — Foundation** | §3 pipeline, §4 `IComicAccessorSession` (zip/tar/rar/7z) + `IPdfDocumentSession`, §5 budget + setting, §8 prefetch, paged+PDF moved async, §7 thumbnails, §12.1 + §12.2 harness. | All modes decode off the UI thread; container opened once/session (archive **and** PDF); budget respected in tests; benchmark numbers recorded as the baseline; no functional regression. |
| **2 — Tiers** | §6 display-tier downsampling for paged + §6.2 detail tier, §9 cross-issue retention. | Paged mode holds no native-res bitmaps; zoom-in stays sharp; memory flat across issue switches. |
| **3 — Big strips** | §11. | A 12 000 px webtoon strip scrolls without a decode spike; peak memory for a webtoon volume within budget. |
| **4 — Render cleanup** | §10. | WebP path has no PNG round-trip; `ReaderFrameStats` overlay; no per-frame pixel copy with adjustment on. |

---

## 14. Review decisions

All three open questions resolved with the drafted recommendations (review, 2026-09-09):

1. **Byte-budget Auto formula** — `clamp(25% RAM, 128, 512) MiB` stays as-is; no RAM-scaled
   ceiling term. Rationale in §5.1.
2. **Detail-tier trigger** — 1.15× / 150 ms ship as constants, tuned from feel after Phase 2.
   §6.2.
3. **Phase 3** — its own mini-design doc when reached; §11 is direction only.

No open questions remain. The four grilling rounds and this review close the design frontier.
