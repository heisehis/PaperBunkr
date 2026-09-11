# Reader webtoon-strip band decode — design (Phase 3 / reader backlog Batch C)

**Status:** design settled, grilling complete 2026-09-12 — ready for `writing-plans`.
**Date:** 2026-09-09 (rev 2: 2026-09-12).
**Parent:** `docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md` §11
(this is the focused mini-design that §14 decision #3 said Phase 3 would get).

### Revision history

| Rev | Date | Change |
|---|---|---|
| 1 | 2026-09-09 | Initial design, written design-only with §7 open questions. |
| 2 | 2026-09-12 | Grilling pass against the *current* pipeline code (not assumed) — closed all of §7's open questions, corrected one wrong assumption (§4.3's "height from the header needs no new work" — it does), and added the zoom/display-pixel-band interaction §4.1 didn't originally anticipate. See §7 (renamed "Resolved decisions") for the full record. |

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

**A second, related problem found during this design's grilling pass (not in the original v1
doc):** continuous mode's layout (`ReaderLayoutModel.ComputeContinuousLayout`, driven by
`PageCanvas._knownPageSizes`) only learns a page's *true* pixel size after that page has been fully
decoded at least once — before that it estimates every undecoded page at a fixed
`DefaultEstimatedPageSize` of 660×1010 (a normal manga-page guess). For a real 800×24000 strip that
estimate is off by ~24×: the scroll extent is badly wrong the moment a strip scrolls near the
viewport, then jumps hard once it's decoded. Band decode alone doesn't fix this — it needs its own
fix (§4.3).

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

### 4.1 Band decode

`SKCodec` supports subset decode in managed code — **confirmed directly against the bundled
SkiaSharp 3.119.2** (`SkiaSharp.dll` reflected, not assumed from docs): `SKCodecOptions` has a
`Subset` (`SKRectI?`) property and a `SKCodecOptions(SKRectI subset)` constructor, and
`SKCodec.GetPixels(SKImageInfo, IntPtr, SKCodecOptions)` accepts it directly. No fallback to
`StartIncrementalDecode`/`IncrementalDecode` needed — v1's open question #2 is closed.

For a strip:

- Open `SKCodec` on the raw bytes (already in the shared raw-bytes cache from Phase 1 —
  `ReaderImagePipeline.ReadRawBytes`/`SharedRawCache`).
- Decode in bands of `BandHeight` **display** pixels (§4.1.1 — a rev-2 change from v1's "source
  pixels" leaning), each into its own small `SKBitmap` via a `Subset`-bearing `SKCodecOptions`,
  wrapped as a `ReaderBitmap`.
- Cache key gains a band ordinal **and a zoom bucket** (§4.1.2): `PageId` → a new
  `StripBandId(Container, ContainerStamp, PageIndex, Band, ZoomBucket)`, parallel to `PageId` (not
  a field grafted onto it — a strip's bands and a normal page's whole bitmap are never the same
  cache row), so each band+zoom-bucket combination is an independent cache entry under the same
  byte budget.

#### 4.1.1 BandHeight is in display pixels, not source pixels (rev 2 decision)

v1 leaned toward fixed *source* pixels for simpler cache keys. Rejected: the point of banding is
bounding memory to what's on screen, and a fixed source-pixel band means a heavily zoomed-in view
could still have a huge on-screen footprint per band (defeating the goal), while a zoomed-out view
wastes decode effort on bands far larger than what's visible. **`BandHeight` = one viewport height,
in display pixels** — directly ties band granularity to "what's actually on screen," matching the
feature's own stated goal. Converted to a source-pixel range per band via the strip's own
native-to-display scale factor (the same scale `ComputeDrawPlan`/`ResolveContinuousDrawBitmap`
already compute for a whole page — reused here, not a new formula). Read live off the current
viewport at decode time rather than cached at strip-entry time, so a mid-scroll window resize picks
up the new height for any band decoded after the resize (already-resident bands keep their old
size until evicted normally — no special resize-triggered re-band pass).

#### 4.1.2 Zoom bucketing (rev 2 decision)

Because `BandHeight` is display-pixel-sized, a band's *source*-pixel range depends on the current
zoom level — zoom changes which source rows band N covers. Rather than evicting and re-decoding
every resident band on every zoom tick (cheap but churns the decode queue on a slow wheel-zoom
drag), cache entries carry a **zoom bucket**: `zoom` rounded to the nearest `0.1` (finer than the
existing `ZoomStep = 0.25` button-driven step in `ReaderScreenViewModel`, coarse enough that small
wheel/pinch increments land in the same bucket and hit the cache instead of re-decoding). A bucket
change evicts that strip's now-stale bands the same way any other cache eviction works today — no
new eviction mechanism, just a wider key.

### 4.2 Which bands to decode

The layout model tells the pipeline the visible main-axis range within the strip (it already
computes per-page rects for continuous mode). The pipeline decodes the bands intersecting
`[visibleTop − BandHeight, visibleBottom + BandHeight]` (one `BandHeight` margin either side —
falls directly out of §4.1.1's viewport-height sizing, not a separately-tuned constant), evicts
bands outside `[visibleTop − 2·BandHeight, visibleBottom + 2·BandHeight]`.

**Prefetch reuses the pipeline's existing adaptive fringe (rev 2 decision, v1 had a separate fixed
"1-2 bands").** `ReaderImagePipeline` already widens/narrows its forward prefetch reach (2→6 pages)
off a rolling decode-time average (`_recentDecodeMs`/`RecordDecodeMs`/`_forwardFringe`). Band
prefetch reuses that same mechanism scaled to a band-appropriate range (1→3 bands in the scroll
direction) rather than a second, parallel prefetch policy — one decode-time-driven adaptation rule
for the whole pipeline, not two to keep in sync.

### 4.3 Layout model gets real strip heights without a full decode (rev 2 addition)

**This is new work, not something the existing pipeline already does** (v1 assumed it was "trivial
from the header" — verified against `PageCanvas.cs` during grilling: it isn't, `_knownPageSizes` is
populated only from an actual decoded `Bitmap.PixelSize`, with a `DefaultEstimatedPageSize` of
660×1010 filling in until then).

Fix: the same header peek §3 needs to classify a page as a strip (`SKCodec.Info`, no pixel decode)
also reports true width/height. `ReaderImagePipeline` exposes this as a new lightweight
`IReaderPageSource` member (working name `PeekPageSize(int pageIndex)`, returns the header-declared
`PixelSize` or `null` on failure/unsupported format) that:

- Reads the already-cached raw bytes (`ReadRawBytes` — no extra container I/O beyond what strip
  classification already needs).
- Opens an `SKCodec` on them and reads `.Info.Width`/`.Info.Height` only — no `GetPixels` call.
- Is called by `PageCanvas` as each page enters the continuous virtualization window (the same
  point `_knownPageSizes` gets updated post-decode today), so a strip's true height is known and
  fed into `_knownPageSizes` **before** its first band ever decodes — the scroll extent is correct
  from the moment the strip is scrolled near, not after.

Non-strip pages could use this too (it would tighten the "estimate until decoded" gap for every
page, not just strips) but that's explicitly **out of scope here** — this fix exists because a
webtoon strip's estimate error is uniquely severe (~24×) and directly undermines this feature's own
memory-bound goal; extending it to ordinary pages is a separate, much lower-value follow-up.

`ContinuousPageEntry` becomes either a whole-page entry (today) or a `StripEntry` with a list of
`(Rect, ReaderBitmap?)` band slots. `ReaderPageVisualHandler.RenderContinuous` iterates band slots
the same way it iterates pages, drawing a gap for `null`.

### 4.4 Progressive display

Draw resident bands immediately; `BackgroundDecodeCompleted` already re-pushes the frame when a
new band lands (extend the event to carry `(pageIndex, band)` or just re-push on any completion —
the render pass is cheap).

## 5. Seam already in place

Phase 1 deliberately shaped the pipeline so this doesn't need a rewrite:

- `IReaderPageSource.TryGet`-family can grow a `TryGetBand(index, band, zoomBucket)` sibling
  alongside the new `PeekPageSize` (§4.3).
- `RawBytesCache`/`SharedRawCache` already holds the encoded strip bytes, so band decodes *and* the
  header peek cost no container I/O.
- The `_window` staleness check generalises to `(pageIndex, band, zoomBucket)` tuples.

## 6. Testing

- `SKCodec` band decode unit test: a synthetic 400×6000 strip, assert band N's pixels match a
  full-decode's corresponding rows.
- `PeekPageSize` unit test: asserts it returns the correct width/height without triggering a full
  pixel decode (e.g. assert on a corrupt-past-the-header fixture that a full decode would throw on
  but the header peek still succeeds).
- Memory bound: scroll a 800×24000 strip end to end at a scripted velocity, assert resident band
  bytes stay within `~4·BandHeight(display)·width·4` (converted to source-pixel terms via the
  scroll's zoom) + budget slack.
- Layout: a strip with only bands 3–5 resident still reports the correct total scroll extent, and
  that extent is correct *immediately* (from `PeekPageSize`) even before any band decodes.
- Zoom-bucket eviction: changing zoom by more than one bucket width evicts the affected strip's
  bands; changing zoom within the same bucket does not (cache hit, no re-decode).
- Benchmark: add `WebtoonScrollSimulation` to `Paperbunkr.Benchmarks` — decoded MP/sec at fixed
  scroll velocity, whole-strip vs. banded.

## 7. Resolved decisions (was "Open questions" in v1)

1. **BandHeight units** — display pixels, one viewport height (§4.1.1). *Closed 2026-09-12, user
   decision.*
2. **Does the bundled SkiaSharp expose subset `SKCodec` decode in managed code?** — Yes, confirmed
   by reflecting on the actual bundled 3.119.2 `SkiaSharp.dll`: `SKCodecOptions.Subset`/`HasSubset`
   plus the `SKCodecOptions(SKRectI subset)` constructor. *Closed 2026-09-12, verified fact, not a
   judgment call.*
3. **Strip rotation / per-band fit** — kept out of scope, unchanged from v1 (continuous mode has
   neither today).
4. **Zoom-dependent band boundaries** (not anticipated in v1 — surfaced by decision #1 above) —
   cache key carries a zoom bucket (nearest 0.1) rather than evicting on every zoom tick. *Closed
   2026-09-12, user decision: zoom-bucketed caching over simple re-decode-on-zoom.*
5. **Strip height for layout, before any decode** (v1 assumed this was already free; it isn't) —
   new `PeekPageSize` header-only probe, feeds `_knownPageSizes` immediately. *Closed 2026-09-12,
   user decision: build it, scoped to strips only (not a general page-estimate improvement).*
6. **Band prefetch policy** — reuses the pipeline's existing adaptive fringe mechanism (scaled to
   1→3 bands) rather than a separate fixed "1-2 bands" policy. *Closed 2026-09-12, user decision.*
