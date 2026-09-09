# Reader webtoon-strip band decode — design (Phase 3)

**Status:** design only — not started
**Date:** 2026-09-09
**Parent:** `docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md` §11
(this is the focused mini-design that §14 decision #3 said Phase 3 would get).

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

## 2. Goal

Decode a tall strip in horizontal **bands** as it scrolls into view, so peak memory is bounded by
what's actually on screen (± a small margin) and no single decode call exceeds a viewport-ish
amount of work.

Non-goals: any change to normal (non-strip) pages; any change to the paged reader; horizontal
webtoon modes (rare).

## 3. What counts as a "strip"

At decode time, after reading the image header: `height / width > StripAspectThreshold` (start at
**3.0** — a normal manga page is ~1.5, a double-page spread ~0.77, a strip is 8–30). Only strips
get the band path; everything else stays on the Phase 1 whole-page path unchanged.

## 4. Mechanism

### 4.1 Band decode

`SKCodec` supports incremental / subset decode. For a strip:

- Open `SKCodec` on the raw bytes (already in `RawBytesCache` from Phase 1).
- Decode in bands of `BandHeight` source pixels (start at **2048**, tuned later), each into its own
  small `SKBitmap` → downsampled to display width → wrapped as a `ReaderBitmap`.
- Cache key gains a band ordinal: `PageId` → `PageId { StripBand = n }` (or a parallel
  `StripBandId(pageIndex, band)`), so each band is an independent cache entry under the same byte
  budget.

`SKCodec.GetPixels` with an options struct carrying a subset rect is the intended API; if the
bundled SkiaSharp 3.119's managed binding doesn't expose subset decode cleanly, fall back to
`SKCodec.StartIncrementalDecode` + `IncrementalDecode` reading N rows at a time and slicing.

### 4.2 Which bands to decode

The layout model tells the pipeline the visible main-axis range within the strip (it already
computes per-page rects for continuous mode). The pipeline decodes the bands intersecting
`[visibleTop − margin, visibleBottom + margin]` (margin = one `BandHeight`), evicts bands outside
`[visibleTop − 2·BandHeight, visibleBottom + 2·BandHeight]`.

### 4.3 Layout model

`ReaderLayoutModel` today places a page from its full `Size`. For a strip it needs:

- the strip's **total** height (from the header — no full decode needed), for scroll extent;
- per-band offsets (trivial: `band * BandHeight * displayScale`);
- a "which bands are resident" query so it can draw a gap for a not-yet-decoded band.

New: `ContinuousPageEntry` becomes either a whole-page entry (today) or a `StripEntry` with a list
of `(Rect, ReaderBitmap?)` band slots. `ReaderPageVisualHandler.RenderContinuous` iterates band
slots the same way it iterates pages, drawing a gap for `null`.

### 4.4 Progressive display

Draw resident bands immediately; `BackgroundDecodeCompleted` already re-pushes the frame when a
new band lands (extend the event to carry `(pageIndex, band)` or just re-push on any completion —
the render pass is cheap). Prefetch the next 1–2 bands in the scroll direction at low priority.

## 5. Seam already in place

Phase 1 deliberately shaped the pipeline so this doesn't need a rewrite:

- `IReaderPageSource.TryGet` can grow a `TryGetBand(index, band)` sibling.
- `RawBytesCache` already holds the encoded strip bytes, so band decodes cost no container I/O.
- The `_window` staleness check generalises to `(pageIndex, band)` tuples.

## 6. Testing

- `SKCodec` band decode unit test: a synthetic 400×6000 strip, assert band N's pixels match a
  full-decode's corresponding rows.
- Memory bound: scroll a 800×24000 strip end to end at a scripted velocity, assert resident band
  bytes stay within `~4·BandHeight·width·4` + budget slack.
- Layout: a strip with only bands 3–5 resident still reports the correct total scroll extent.
- Benchmark: add `WebtoonScrollSimulation` to `Paperbunkr.Benchmarks` — decoded MP/sec at fixed
  scroll velocity, whole-strip vs. banded.

## 7. Open questions

1. `BandHeight` — fixed source pixels, or fixed *display* pixels (so it scales with viewport)?
   Lean fixed source (simpler cache keys), tune the constant.
2. Does the bundled SkiaSharp 3.119 expose subset `SKCodec` decode in managed code, or is it
   incremental-only? (Spike first.)
3. Strip rotation / per-band fit — continuous mode has neither today; keep out of scope.
