# Reader webtoon-strip band decode — design (Phase 3 / reader backlog Batch C)

**Status:** design settled, rev 3 — ready for `writing-plans`.
**Date:** 2026-09-09 (rev 2: 2026-09-12; rev 3: 2026-09-12).
**Parent:** `docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md` §11
(this is the focused mini-design that §14 decision #3 said Phase 3 would get).

### Revision history

| Rev | Date | Change |
|---|---|---|
| 1 | 2026-09-09 | Initial design, written design-only with §7 open questions. |
| 2 | 2026-09-12 | Grilling pass against the *current* pipeline code — closed §7's open questions, corrected one wrong assumption (strip height "from the header" needing no new work — it does), added display-pixel bands + a zoom-bucketed cache key. |
| 3 | 2026-09-12 | **External review caught rev 2's central mechanism was unbuildable.** `SKCodec.GetPixels(..., Subset)` — the API rev 2 verified *exists* and built the whole band-decode plan on — is rejected at runtime by Skia's own JPEG and PNG codecs (`onGetPixels` returns `kUnimplemented` whenever `options.fSubset` is set, for *both* formats — confirmed by reading Skia's actual `SkJpegCodec.cpp`/`SkPngCodec.cpp` source, not just SkiaSharp's C# surface). Rev 2 checked that the property existed, not that the decoder honors it — corrected here. Real row-range decode requires the **scanline API** (`StartScanlineDecode`/`SkipScanlines`/`GetScanlines`), which is stateful and forward-only per strip, not a stateless per-band call — this reshapes §4.1 substantially beyond the review's literal three points. Also adopted from the review: fixed source-pixel `BandHeight` (dropping display-pixel bands and the zoom bucket entirely — independently required now, not just simpler), asynchronous `PeekPageSize` (rev 2's version would have run synchronous container I/O on the UI thread), and queue handling for stale band requests during rapid scroll (extending the pipeline's existing, already-verified `_window` lazy-skip mechanism rather than adding new cancellation-token plumbing). See §7 for the full before/after per point. |

---

## 1. Problem

A webtoon / manhwa chapter is often **one image per "page"**, 800–1200 px wide and 10,000–30,000 px
tall. The Phase 1 pipeline decodes each such strip **whole**, at native resolution, on the single
background decode thread, then downsamples once. A 800×20000 strip is 64 MP → ~256 MiB decoded
(pre-downsample), decoded in one blocking shot. On the old-HDD box that is a multi-second stall and
a budget blow-out the moment two strips are near the viewport.

Phase 1 mitigations that already help: the display tier downsamples to viewport width immediately,
and the byte budget evicts. What's missing: the **decode itself** is still all-or-nothing per
strip.

**A second, related problem, found during rev 2's grilling pass:** continuous mode's layout
(`ReaderLayoutModel.ComputeContinuousLayout`, driven by `PageCanvas._knownPageSizes`) only learns a
page's *true* pixel size after that page has been fully decoded at least once — before that it
estimates every undecoded page at a fixed `DefaultEstimatedPageSize` of 660×1010 (a normal
manga-page guess). For a real 800×24000 strip that estimate is off by ~24×: the scroll extent is
badly wrong the moment a strip scrolls near the viewport, then jumps hard once it's decoded. Band
decode alone doesn't fix this — it needs its own fix (§4.3).

## 2. Goal

Decode a tall strip in horizontal **bands** as it scrolls into view, so peak memory is bounded by
what's actually on screen (± a small margin) and no single decode call exceeds a viewport-ish
amount of work. As a directly-related fix, give the layout model a strip's true height immediately
(no full decode needed) so the scroll extent doesn't lurch once decode starts.

Non-goals: any change to normal (non-strip) pages; any change to the paged reader; horizontal
webtoon modes (rare); strip rotation or per-band fit (continuous mode has neither today — stays out
of scope, unchanged from v1).

## 3. What counts as a "strip"

At decode time, after reading the image header: `height / width > StripAspectThreshold` (start at
**3.0** — a normal manga page is ~1.5, a double-page spread ~0.77, a strip is 8–30). Only strips
get the band path; everything else stays on the Phase 1 whole-page path unchanged.

The header read this requires (width/height, no pixel decode) is the same capability §4.3's
layout fix needs — built once, used by both.

## 4. Mechanism

### 4.1 Band decode is a stateful, forward-only scanline session per strip (rewritten rev 3)

**Rev 2's plan — one independent `SKCodec.GetPixels(SKImageInfo, IntPtr, SKCodecOptions{Subset})`
call per band — does not work.** Verified by reading Skia's actual codec source
(`src/codec/SkJpegCodec.cpp`, `src/codec/SkPngCodec.cpp`, both current `main`):

```cpp
// SkJpegCodec::onGetPixels
if (options.fSubset) {
    // Subsets are not supported.
    return kUnimplemented;
}
```
```cpp
// SkPngCodec::onGetPixels
if (options.fSubset) {
    return kUnimplemented;
}
```

Both codecs reject *any* subset through `GetPixels`, unconditionally — this has nothing to do with
MCU/block alignment; padding the rect to a 16×16 boundary changes nothing, the call fails before
alignment would ever matter. Rev 2's "confirmed by reflection" only checked that the C# property
`SKCodecOptions.Subset` exists on the binding — not that the native decoder honors it for this
entry point. Real row-range decode lives in a different API:

- **`SKCodec.StartScanlineDecode(SKImageInfo)`**, then **`SkipScanlines(int count)`** /
  **`GetScanlines(IntPtr dst, int countLines, int rowBytes)`** — all three confirmed present on
  `SkiaSharp.dll` 3.119.2 (the project's exact pinned version, reflected directly, not the docs).
- For JPEG specifically, `SkJpegCodec::onSkipScanlines` calls real libjpeg-turbo
  `jpeg_skip_scanlines()` — entropy-decodes skipped rows to keep decoder state consistent but skips
  the expensive IDCT + color-convert + output work for them. Not free, but materially cheaper than
  decoding-and-discarding through `GetScanlines`.
- PNG's zlib-stream row-by-row structure decodes at scanline granularity natively; the same
  Skip/Get pair is expected to work there too, verified for real (not just by reading source) in
  the implementation-time test this design already calls for (§6).

**Consequence: band decode is not "N independent decode calls," it's one ordered walk through a
strip.** To read band `k` (rows `[k·BandHeight, (k+1)·BandHeight)`), a session must have already
skipped/read every row before `k·BandHeight` since the session opened — scanline decode cannot seek
backward or jump forward without skipping through. `ReaderImagePipeline` therefore keeps one
**`StripDecodeSession`** per strip currently within reach of the window:

- Opened the first time any of a strip's bands enters the prefetch range (§4.2). Holds the open
  `SKCodec`, the scanline session state, and the next unread row index.
- **Forward band request, in order** (the overwhelmingly common case — the user scrolling down
  through a webtoon): `SkipScanlines` from the session's current row to the band's start, then
  `GetScanlines` for `BandHeight` rows. Cheap, matches the feature's whole goal.
- **Backward or far-forward request** (scrollbar drag, jump-to-position, fast reverse scroll): the
  existing session can't serve it — disposed and reopened fresh, skipping from row 0 to the target
  band's start. Bounded, non-pathological cost (same order of magnitude as this strip's very first
  band today), just not the cheap path. No special-cased "seek" optimization for v1 — YAGNI unless
  reverse-scroll-through-a-strip turns out to be a real, common pattern in practice.
- Disposed when every one of the strip's bands falls outside the eviction range (§4.2) — same
  lifetime shape as a decoded bitmap leaving `_displayCache`, just for the session object instead.
- **Single-threaded access, same as every other container read (§1 of the parent doc's own design):
  `_readerLock` guards a `StripDecodeSession`'s `SkipScanlines`/`GetScanlines` calls, not just the
  raw-bytes read.** A strip's very first band can hit the same synchronous UI-thread cold-miss path
  whole-page `GetPage` already has (§1's "decode never runs on the UI thread except a deliberate
  synchronous cold-miss"), so the session object must tolerate being touched from either the UI
  thread (a synchronous cold miss) or the background consumer loop, never both at once — `_readerLock`
  already exists for exactly this shape of problem, extended to cover session advancement too.

Decoded bands are downsampled once via the existing `Downsample` helper (viewport-width cap, same
as whole pages) and cached at **native/downsampled resolution — not scaled for zoom at decode
time.** The compositor scales each band bitmap at render time for the current zoom, exactly like
`ResolveContinuousDrawBitmap` already does for whole continuous-mode pages. One decode serves every
zoom level; zooming never triggers a re-decode.

Cache key: `StripBandId(Container, ContainerStamp, PageIndex, Band)` — a new type, parallel to
`PageId` (not a field grafted onto it — a strip's bands and a normal page's whole bitmap are never
the same cache row). **No zoom bucket** (§4.1.1).

#### 4.1.1 BandHeight is fixed source pixels, not display pixels (reverted from rev 2)

Rev 2 picked display pixels (one viewport height) to tie band granularity to "what's on screen."
Reverted, for reasons independent of the review's original MCU-alignment framing (which, per the
source above, wasn't the actual failure mode — subset via `GetPixels` fails regardless of
alignment):

1. **The scanline session (§4.1) needs band boundaries that don't move.** If `BandHeight` depended
   on zoom, every zoom change would shift which source rows every band index refers to, and an
   in-flight or already-decoded session's band numbering would go stale mid-strip — the session
   would need tearing down and restarting from row 0 on *every* zoom change, not just far jumps.
   Fixed source pixels means a band's identity never depends on viewport/zoom state.
2. **Decode-once-scale-at-render already covers the zoom case for whole pages** —
   `ResolveContinuousDrawBitmap` decodes a page once and re-samples the already-decoded bitmap at
   render time for zoom, never re-decoding. Fixed-source-pixel bands extend the exact same pattern;
   display-pixel bands would have been the *only* place in this pipeline where zoom drove a
   re-decode.
3. **Removes the zoom-bucket cache key entirely** — one band, one cache entry, valid at every zoom
   level. Simpler and strictly more cache-efficient than rev 2's bucketed approach (which still
   re-decoded on a bucket boundary crossing).

**`BandHeight` = a fixed constant, start at `4096` source pixels** (rev 1's "start at 2048, tune
later" doubled — a scanline session's per-band `SkipScanlines`+`GetScanlines` pair has some fixed
overhead per call, so fewer/larger bands trade a little more per-band memory for fewer session
calls; tune during the benchmark §6 already calls for). No 16×16 block-alignment padding — verified
unnecessary for decode correctness (Skia's scanline skip/read has no block-alignment requirement,
only whole-row granularity, which an integer `BandHeight` already satisfies).

#### 4.1.2 Seam prevention at render time

Two adjacent bands must never show a hairline gap or overlap where they meet. This is a
render-math concern, not a decode concern (and applies regardless of how `BandHeight` is defined):
each band's on-screen destination rect is computed from **one continuous coordinate mapping** —
`bandTop_display = bandIndex · BandHeight · scale`, `bandBottom_display = bandTop_display +
BandHeight · scale` for every band but the last (which clips to the strip's true remaining height)
— not independently rounded per band. Same pattern `SpreadLayoutMath.SplitSpread` already uses to
avoid a seam between a spread's two halves (one combined rect, split by a fraction, rather than two
independently-placed rects). `ReaderPageVisualHandler.RenderContinuous`'s band-slot iteration
(§4.3) computes every band's rect from the strip's single top-of-strip display offset plus this
formula, never from the previous band's already-rounded rect.

### 4.2 Which bands to decode, and queue handling for rapid scroll

The layout model tells the pipeline the visible main-axis range within the strip (it already
computes per-page rects for continuous mode). The pipeline decodes the bands intersecting
`[visibleTop − BandHeight, visibleBottom + BandHeight]` (one `BandHeight` margin either side),
evicts bands (and, once none of a strip's bands remain resident, that strip's `StripDecodeSession`)
outside `[visibleTop − 2·BandHeight, visibleBottom + 2·BandHeight]`.

**Prefetch reuses the pipeline's existing adaptive fringe**, unchanged from rev 2: `_recentDecodeMs`
/`RecordDecodeMs`/`_forwardFringe` already widen/narrow whole-page prefetch reach (2→6) off a
rolling decode-time average — band prefetch reuses the same mechanism scaled to a band-appropriate
range (1→3 bands in the scroll direction).

**Rapid scroll and stale band requests (rev 3, review point 3):** verified directly against the
live code (`ReaderImagePipeline.ProcessQueuedPage`/`SetVirtualizationWindow`/`_window`/`_enqueued`)
that whole-page decode already has exactly this problem solved, not hypothetically — every queued
page index is checked against `_window` when it's *dequeued*, and silently dropped (no decode, no
wasted work) if the window moved past it in the meantime. This is a real, already-proven
queue-flush mechanism, just implemented as cheap lazy-skip-on-dequeue rather than eager
channel-draining. Band decode extends the identical pattern: `_window`/`_enqueued` generalise to
`(PageIndex, Band)` tuples, and `ProcessQueuedBand` skips exactly the way `ProcessQueuedPage` does
today. No `CancellationToken` plumbing added — each band's actual decode step (one
`SkipScanlines`+`GetScanlines` call, bounded by construction, §4.1) is already short enough that
in-flight cancellation isn't worth the complexity; the problem `CancellationToken` would solve here
(not *starting* stale work) is already solved by the window check before a band decode step begins.

### 4.3 Layout model gets real strip heights without a full decode, asynchronously (rev 3: async)

**New work, not something the existing pipeline already does** (v1 assumed it was "trivial from
the header"; rev 2's grilling verified it isn't — `_knownPageSizes` is populated only from an
actually-decoded `Bitmap.PixelSize`, `DefaultEstimatedPageSize` (660×1010) filling in until then).

**Rev 2's version of this was itself a bug rev 3 fixes (review point 2, verified against
`ReaderImagePipeline.ReadRawBytes`):** rev 2 said `PeekPageSize` would be "called by `PageCanvas` as
each page enters the continuous virtualization window" without saying *how* — the obvious naive
implementation calls it synchronously from that UI-thread event. `ReadRawBytes` (which the header
peek needs, to get bytes to open an `SKCodec` on) takes `_readerLock` around the actual container
read and — on anything not already in `SharedRawCache` — does blocking archive/session I/O. That
lock is shared with the background consumer loop's own decode work; a synchronous UI-thread call
into it reintroduces exactly the "decode blocks the UI thread" anti-pattern the parent pipeline
design's Phase 1 was built to eliminate (§1 of that doc, problem #1).

**Fix: `PeekPageSize` is asynchronous, off the UI thread, same shape as the rest of the pipeline's
background work.**

- `ReaderImagePipeline` exposes it as `void RequestPageSize(int pageIndex)` (fire-and-forget,
  fire-and-forget matching `SetVirtualizationWindow`'s own shape) plus a new event —
  `PageSizeAvailable(int pageIndex, PixelSize size)` — raised off the background consumer loop once
  the header peek completes (routed through the *same* high/low-priority channel + `_window`/
  `_enqueued` machinery §4.2 already has, as a third request kind alongside whole-page and band
  decode, not a new thread).
- The peek itself: `ReadRawBytes` (already-cached bytes for anything the pipeline has touched
  before; a real but bounded I/O read otherwise — off the UI thread either way now), open an
  `SKCodec`, read `.Info.Width`/`.Info.Height` only — no `GetPixels`/scanline call.
- `PageCanvas` subscribes to `PageSizeAvailable`, updates `_knownPageSizes[pageIndex]`, and triggers
  a re-layout pass — the same shape `BackgroundDecodeCompleted` already drives for a landed page
  decode. Until that event fires, the existing `DefaultEstimatedPageSize` estimate is used, exactly
  as it is today for any undecoded page — no UI-thread call ever blocks on this.
- Requested by `PageCanvas` as each page enters the continuous virtualization window (unchanged
  trigger point from rev 2 — only the sync/async shape of the call changed), so a strip's true
  height is *usually* known well before its first band decodes, without ever risking a UI stall if
  it isn't.

Non-strip pages could use this too (tightening the "estimate until decoded" gap generally) but
that's explicitly **out of scope here**, unchanged from rev 2 — a webtoon strip's estimate error is
uniquely severe (~24×); extending this to ordinary pages is a separate, lower-value follow-up.

`ContinuousPageEntry` becomes either a whole-page entry (today) or a `StripEntry` with a list of
`(Rect, ReaderBitmap?)` band slots, rects computed per §4.1.2. `ReaderPageVisualHandler.
RenderContinuous` iterates band slots the same way it iterates pages, drawing a gap for `null`.

## 5. Seam (mechanism reuse) already in place

Phase 1 deliberately shaped the pipeline so this doesn't need a rewrite:

- `RawBytesCache`/`SharedRawCache` already holds the encoded strip bytes, so both the header peek
  (§4.3) and every scanline session (§4.1) cost no container I/O beyond the first touch of a given
  strip.
- The high/low-priority `Channel<int>` + `_window`/`_enqueued` request-and-skip machinery (§4.2)
  generalises to a third request kind (page-size peeks) and to `(pageIndex, band)` tuples (bands) —
  same shapes, same lazy-skip-on-dequeue staleness handling, verified already correct for whole
  pages rather than assumed to generalise cleanly.

## 6. Testing

- **Scanline-session band decode test**: a synthetic 400×6000 (or larger) strip; assert
  sequentially-requested bands' pixels match a full-decode's corresponding rows, for both a JPEG
  and a PNG fixture (the two codecs' scanline paths were verified independently in this design —
  confirm both for real, not just by reading source).
- **Backward-jump session restart test**: request band 5, then band 1 (out of order) — assert band
  1's pixels are still correct (the session tore down and restarted from row 0, not silently wrong
  output from a session that can't actually seek backward).
- **`RequestPageSize` async test**: asserts it never blocks the calling thread (e.g. request a page
  size while a slow/blocked fake container read is in flight on `_readerLock`, assert the request
  call itself returns immediately) and that `PageSizeAvailable` eventually fires with the correct
  header-declared size — without triggering a full pixel decode (assert on a fixture whose pixel
  data is corrupt past the header, which a full decode would throw on but a header-only peek
  wouldn't touch).
- **Stale band request test**: enqueue several band requests, then move the virtualization window
  far past them before the consumer loop processes them — assert they're skipped (no decode call
  made, mirroring the existing whole-page equivalent test).
- **Seam test**: assert adjacent bands' computed destination rects share an exact boundary
  (no gap, no overlap) across a range of `BandHeight`/zoom/viewport combinations — the render-math
  regression class §4.1.2 exists to prevent.
- **Memory bound**: scroll a 800×24000 strip end to end at a scripted velocity, assert resident band
  bytes stay within `~4·BandHeight·width·4` (fixed source pixels now, so no zoom conversion needed)
  + budget slack.
- **Layout**: a strip with only bands 3–5 resident still reports the correct total scroll extent,
  and that extent is correct once `PageSizeAvailable` has fired, even before any band decodes.
- **Benchmark**: add `WebtoonScrollSimulation` to `Paperbunkr.Benchmarks` — decoded MP/sec at fixed
  scroll velocity, whole-strip vs. banded; also measure forward-sequential vs. session-restart cost
  to sanity-check §4.1's "restart is bounded, not pathological" claim with real numbers.

## 7. Resolved decisions

Point-by-point against the 2026-09-12 external review (which caught rev 2's central mechanism was
unbuildable) and rev 2's own §7 (open questions from v1):

1. **`SKCodec.GetPixels(..., Subset)` for band decode** — **rejected, not just re-tuned.** Verified
   by reading Skia's actual `SkJpegCodec.cpp`/`SkPngCodec.cpp`: both codecs' `onGetPixels` return
   `kUnimplemented` for *any* subset request, unconditionally — not an alignment problem, the call
   fails before alignment matters. Real row-range decode is the scanline API
   (`StartScanlineDecode`/`SkipScanlines`/`GetScanlines`, confirmed on SkiaSharp 3.119.2), which is
   stateful and forward-only per strip (§4.1) — a materially different mechanism than either v1 or
   rev 2 designed around, not a parameter tweak to the same one.
2. **`BandHeight` units** — reverted to **fixed source pixels** (4096, no 16×16 padding — verified
   unnecessary), dropping rev 2's display-pixel bands *and* its zoom-bucketed cache key entirely
   (§4.1.1). Independently required once §4.1's scanline-session mechanism is accounted for, not
   only for the review's stated (and only partially accurate) MCU-alignment reason.
3. **Seam/tearing risk between adjacent bands** — real, but a render-math concern, not a decode
   concern; fixed by computing every band's rect from one continuous coordinate mapping rather than
   independently rounding each (§4.1.2), the same pattern `SpreadLayoutMath.SplitSpread` already
   uses for a spread's two halves.
4. **`PeekPageSize` UI-thread risk** — accepted in full. Verified against `ReadRawBytes`'s real
   locking/I-O shape that a synchronous UI-thread call was a genuine reintroduction of the parent
   pipeline's original problem #1, not a hypothetical. Made asynchronous, tolerating the existing
   `DefaultEstimatedPageSize` estimate until `PageSizeAvailable` fires (§4.3).
5. **Stale band requests under rapid scroll** — the requirement (don't do wasted/superseded decode
   work) is met by extending the pipeline's existing, already-verified `_window` lazy-skip-on-
   dequeue mechanism to band tuples, rather than adding `CancellationToken` plumbing — a real
   queue-flush effect (§4.2), just via the pattern already proven correct for whole pages.
6. **Does the bundled SkiaSharp expose *a* subset-shaped API surface?** — Yes (rev 2's finding
   stands as far as it went: `SKCodecOptions.Subset` is real C# API surface) — but, per point 1
   above, that surface isn't honored by `GetPixels` for either JPEG or PNG, which is the fact that
   actually matters for this design. Correction recorded so the distinction (surface exists vs.
   surface is functional for this call) isn't lost again.
7. **Strip rotation / per-band fit** — kept out of scope, unchanged from v1/rev 2.
8. **Band prefetch policy** — reuses the pipeline's existing adaptive fringe mechanism (1→3 bands),
   unchanged from rev 2.
