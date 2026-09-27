# Comic reader — Image quality (slice C) design

Date: 2026-09-26. Status: **built 2026-09-26** (uncommitted; on-screen check by the user pending, see "Implementation notes" at the end). Design settled in one grilling round ("recommended" on all seven questions).
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, slice C: **#2 image adjustments and night mode**, **#3 auto-crop margins**, **#25 moiré/halftone-aware downscaling**. (#26 upscaling stays deferred.)
Slices A, B, D, F and G are built and uncommitted; this builds on the same shared working tree.

## Facts this design rests on (verified 2026-09-26)

- **#2 is mostly built.** Brightness, contrast, saturation and gamma exist end to end: global defaults (`AppSettings.DefaultBrightness/Contrast/Saturation/Gamma`), per-issue overrides stored as deltas
  (`Issue.BrightnessOverride` and siblings), the drawer's ADJUST section, and reader profiles (`ReaderProfileState`). The warm tint from F3 (#18) is the "warm/sepia night filter". They are applied
  at **render time**: `PageCanvas` pushes an `AdjustmentVisualData(Brightness, Contrast, Saturation, Gamma, Warmth)` message to `ReaderPageVisualHandler`, which builds an `SKColorFilter` from
  `ImageAdjustmentMath` (colour matrix, plus a table filter for gamma). So **the pitch's "must be part of the decode-pipeline cache key" is stale for these**: nothing here touches the cache.
- **CE has two adjustments Paperbunkr skipped on purpose** (`ImageAdjustmentMath`'s header lists them as the original spec's named deviations): **AutoContrast** and **Sharpen**
  (`_reference/ComicRackCE/cYo.Common/Drawing/BitmapAdjustment.cs`, `ImageProcessing.ApplyAdjustment`, `Histogram.cs`, `PreferencesDialog.cs`).
  - *AutoContrast:* a histogram of the page's grey levels; `blackPoint = min(lowest level holding 0.5% of pixels, 0.25)`, `whitePoint = max(highest level holding 0.5% of pixels, 0.75)`
    (`Histogram.defaultThreshold = 0.005`, `range = 0.25`). It only acts when the page is washed out (`whitePoint < 0.95` or `blackPoint > 0.05`); otherwise the matrix is untouched.
    CE scales by `(contrast + 1) / (whitePoint - blackPoint)` and adds `brightness - blackPoint` *after* the scale, which is not a true levels stretch (`(x - bp) / (wp - bp)`). It is a per-page
    computation, not a per-book one.
  - *Sharpen:* an integer 0-3 (`tbSharpening.Maximum = 3`) in Preferences, applied last as a 3x3 cross kernel: centre weight `(4 - s) * 5`, the four neighbours `-1`, divisor `centre - 4`
    (s = 1, 2, 3 gives centre 15, 10, 5).
  - CE keeps both in `Settings.GlobalColorAdjustment`, and also a per-comic adjustment that adds to it; Paperbunkr's global + per-issue-override layering already mirrors that.
- **#3 has no CE counterpart** (searched: no crop/trim/margin code in `ComicDisplayControl` or the engine). The pitch is Paperbunkr-original.
- **Where geometry comes from.** Fit, pan clamp, the part grid, guided panels and the continuous layout all read the page bitmap's `PixelSize` (`PageCanvas.EffectivePixelSize`). A page whose *bitmap* is
  cropped therefore needs no changes anywhere in that code. The bitmap is produced in `ReaderImagePipeline`: the decode reads the full-size image, then `Downsample(decoded, _viewportWidth)` scales it
  to the viewport width (`BitmapInterpolationMode.HighQuality`) and that is what the cache (`PageId(container, stamp, index, tier)`) keeps. A separate detail tier (`GetDetailPage`, zoom-triggered)
  decodes again at a larger size; thumbnails (`ThumbnailLongestEdge`) and slice B's ad-page hashing decode on their own.
- **Per-page data precedent.** Slice B added `PageReport` (issue id + 0-based page number, unique per page, migration with a no-op `Down()`). Per-page rotation and tags live in the file's ComicInfo
  page list, which is the wrong home for a Paperbunkr-only preference, so the crop override follows `PageReport`'s shape instead.
- **Pixels are reachable.** The panel detector (slice D, `PagePanelAnalyzer`) already reduces a page to a ~480 px luminance grid and finds gutters and borders on it; the crop detector reuses that reduction.
- **Moiré is not yet located.** Skia's "high" quality downscales through mipmaps, so the pipeline's downscale is probably decent already, and the shimmer the pitch describes may come from the compositor
  scaling the viewport-width bitmap a second time when drawing (fit-height, any zoom other than 100%). That is a hypothesis: step 1 measures it before anything is changed.
- **Shared working tree:** `ReaderImagePipeline.cs`, `ReaderPageVisualHandler.cs`, `PageCanvas.cs`, `ReaderScreenViewModel*.cs`, `ReaderScreen.axaml`, `ImageAdjustmentMath.cs`, `AppSettings.cs`,
  `Issue.cs`, `ReaderProfileState.cs`/`ReaderProfiles.cs`, the model snapshot and `PreferencesScreenViewModel*.cs` are already modified by earlier slices; edits stay narrow.

## Scope

In: auto-levels and sharpen (#2's remaining work); auto-crop of scan borders with a per-page override (#3); a measured fix for screentone shimmer (#25).
Out: a "sepia"/"night" preset (the warm tint covers it), a draggable custom crop rectangle, upscaling (#26), cropping webtoon strips, anything for the Books/PDF reader.

## Design

### Order of work

1. **Measure #25** (a test with a synthetic halftone), then fix what it shows. 2. **#2** auto-levels and sharpen. 3. **#3** auto-crop. One plan, three parts; #25 goes first because its outcome is the
least certain.

### #25 — screentone shimmer

A test builds a halftone page (a 45-degree dot grid at a period typical of print, about 6-8 px at 2400 px wide) and measures shimmer as the energy of the low-frequency beat left after downscaling,
against an area-averaged reference, at three stages: (a) the pipeline's `Downsample` to the viewport width; (b) the compositor's draw-time scale from that bitmap to the final on-screen size for a fit
that is not width (fit-height, fit-all) and for zooms of 150% and 200%; (c) the same after any fix.
- If (a) aliases: replace the pipeline's downscale with an area-averaged one (Skia mipmapped-linear or a stepwise halving pass) for every page. Always on, no setting.
- If (b) aliases: draw the display bitmap with mipmapped sampling, and for a downscale of more than about 1.5x from the cached bitmap request a cache bitmap sized for the actual draw scale instead
  of the viewport width (the detail-tier machinery already decodes at a target size).
- If neither is enough: add the opt-in **Soften screentone** setting (Preferences, off by default; a light Gaussian pass before the downscale), which then joins the cache key. Not built unless the
  measurement says it is needed.

The measurement and its numbers go into the spec's implementation notes, so a later reader can see what was actually wrong.

### #2 — auto-levels and sharpen

*Settings.* Same layers as the sliders. `AppSettings.DefaultAutoLevels` (bool, off) and `DefaultSharpen` (int 0-3, 0); `Issue.AutoLevelsOverride` (bool?) and `Issue.SharpenOverride` (int?), null meaning "use the
default" (these are absolute values, not deltas, because they are not additive); `ReaderProfileState.AutoLevels` and `Sharpen` (null = leave alone). Migration `AddImageQualitySettings`. The drawer's
ADJUST section gains an "Auto levels" switch and a Sharpen slider (0-3, integer), both persisting as per-issue overrides exactly like the sliders above them, and the ADJUST reset clears them. The Preferences
Reader page gets the two defaults. The palette gets "Auto levels" and "Sharpen".

*Auto-levels.* The black and white points are found per page, from a ~480 px luminance copy, with CE's histogram rule. They are computed once per page off the UI thread the first time the page is
shown with auto-levels on, and held in a small weak side table keyed by the page's bitmap (`PageLevels`), so they are dropped with the bitmap and never touch the cache identity. `PageCanvas` reads the
points when it pushes a page's render data and the visual handler builds the colour filter from `ImageAdjustmentMath.CreateColorMatrix(..., blackPoint, whitePoint)`. Continuous mode and spreads give
each page its own points. **Deviation from CE, on purpose:** CE adds `brightness - blackPoint` after the scale, which is not a levels stretch; Paperbunkr uses the true stretch `(x - blackPoint) / (whitePoint - blackPoint)`
and keeps CE's other parts (the 0.5% threshold, the 0.25/0.75 clamps, only acting on a washed-out page, contrast/brightness/saturation composed as before). Recorded in the code comment and here.

*Sharpen.* Applied by the visual handler as a Skia matrix-convolution image filter on the page paint, after the colour filter and gamma, using CE's kernel (centre `(4 - s) * 5`, neighbours `-1`, divisor
`centre - 4`) so 1, 2, 3 mean what they mean in CE. Being render-time it is live and invalidates nothing. If it proves too costly during zoom glides on a large window (measured with the existing perf
overlay), fall back to baking it into the display bitmap in the pipeline with the level added to the cache key; that is a contained change because the pipeline already owns the decode.

*Identity short-circuit.* Auto-levels off and sharpen 0 must leave the draw path exactly as it is today (no filter objects); `AdjustmentVisualData` gains the two fields and its record equality still
backs the filter cache.

### #3 — auto-crop margins

*Detection.* `PageCropDetector` (pure) works on the ~480 px luminance grid. It measures the page border colour (white or black polarity, as the panel detector does), then walks in from each edge until a
line of the grid contains more than a small fraction of non-border pixels, giving an inset per side. Safeguards: each side is capped at 15% of the page; if the trimmed page would be under 40% of the
original, or the border colour is not uniform (mid-grey, a photo), nothing is cropped; a 1% margin of the original is left on each side so panel edges are never clipped; a side with less than 1.5%
of margin is left alone (not worth a resample). The result is a `PageCropRect` in source-pixel terms.

*Application.* In `ReaderImagePipeline`, between the decode and `Downsample`: when auto-crop is on and the page is eligible, the decoded bitmap is cropped to the rect (Skia subset), then downsampled.
The rect is remembered per `PageId` in a small dictionary next to the cache, so the **detail tier and any later re-decode of the same page use the same rect** (a page never shows at two different
crops). The cache identity gains the crop mode (Off / On) so toggling the setting cannot serve a stale bitmap. Not applied to: thumbnails (they show the whole page), webtoon strip bands, or pages
already marked "Never crop". Spreads and continuous pages (that are not strips) crop like any page. Everything downstream (fit, pan, parts, panels, spreads, continuous layout) sees the cropped
bitmap size and needs no change; the one thing to watch is continuous mode's estimated page sizes, which come from ComicInfo (uncropped) until a page is decoded, so its scroll extent shifts slightly
as pages are cropped, the same shift that already happens whenever an estimate is replaced by a real size.

*Setting.* `AppSettings.AutoCropMargins` (bool, off by default: trimming pages is a visible change and should be chosen). Preferences → Reader, a `ReaderProfileState.AutoCrop` field, a palette entry "Auto-crop margins" that flips it for the visit (like the warm tint's session switch), and a
`Series` level is not added (the profile layer already covers "this series always crops").

*Per-page override.* New table `PageCropOverride` (`IssueId`, `PageNumber` 0-based, `Mode`: Auto / Never / Always), unique per page like `PageReport`; a row set back to Auto is deleted. "Never crop this page" and "Always crop this page" go on the page context menu and the palette;
"Always" crops even with the global setting off, "Never" leaves the page whole even with it on. The pipeline asks a lookup (loaded per issue, refreshed when the override changes) and the current page is
re-decoded when its override changes. No custom rectangle.

*Detector tuning.* The panel detector's lesson applies: the thresholds (`NonBorderFraction`, `MaxSideFraction`, `MinRetained`, `KeepMargin`) are named constants, and a palette entry "Show crop" can flash the detected rectangle over the uncropped page for tuning on real scans (reusing `PanelDebugOverlay`'s pattern). Real-page tuning is the user's check.

## Testing

- Pure tests: `ImageAdjustmentMathTests` (true levels stretch, identity when not washed out, sharpen kernel values and divisor for 1-3, matrix with black/white points composes with contrast, brightness, saturation and warmth); a histogram/`PageLevels` test on synthetic pages (washed-out, normal, black-heavy); `PageCropDetectorTests` (white border, black border, uneven borders, all four caps, no border, photo/mid-grey border, near-blank page, 1% margin kept, tiny margin ignored).
- Pipeline tests: crop applied before downsample; the same rect used by the detail tier; crop mode in the cache identity; a thumbnail is never cropped; a strip is never cropped; override Never/Always beats the setting.
- Screentone measurement test (described above), whose result decides which of the three fixes is built.
- Data tests: migration `AddImageQualitySettings` and `PageCropOverride`; `RemoteRowIsolationTests` allowlist reviewed for any file using `includeRemote: true`.
- View-model/headless tests: the ADJUST section's new controls persist overrides and reset; a profile carrying the fields; an auto-levels page draws through a matrix (a test seam on the handler's filter builder); the palette entries.

## Not verified by design

How the levels and sharpen look on real scans, the crop detector on real comics and manga (the "Show crop" tool is the check), and the shimmer before/after on a real screentone page; those are the user's on-screen checks.

## Implementation notes (2026-09-26)

Built in the planned order (plan: `2026-09-26-comic-reader-image-quality-plan.md`). Where the code differs from the design above:

- **#25 measured first, and the cause was the draw, not the downscale.** `ScreentoneDownscaleTests` runs a synthetic 45-degree halftone (period 5 and 7 px, 2400 px wide, 2.4x down to a 1000 px viewport) through each stage and
  measures shimmer as the spread of 8x8 block means (a flat result is ideal). Numbers: the pipeline's `CreateScaledBitmap` (Medium and High are identical in Avalonia) gave 4.8-5.2, *better* than my own box-average
  reference (6.4-6.6), so **stage (a) needed no change**; the unfiltered paged draw (`CreateScaledBitmap(HighQuality)` at draw size) is fine too (2.4-4.7). The leased-canvas draw used whenever a colour filter is active drew with a bare
  `SKPaint`, i.e. **nearest-neighbour**: 5.5-9.8, roughly twice the shimmer, and blocky when enlarged. That path is also the one every new feature needs (sharpen), so `ReaderPageVisualHandler.SamplingFor` now picks mipmapped
  linear when a page is drawn smaller than its bitmap (2.4-4.7 in the same test) and Mitchell cubic when enlarged (low quality: plain bilinear), and the test asserts the mipmapped draw is under 80% of the nearest one. The
  opt-in "Soften screentone" pre-blur was **not** built; the measurement did not call for it.
- **Auto-levels is baked into the display bitmap in the pipeline, not applied at render time** (deviation from "Q2"). The render-time route needed per-page levels threaded through the paged, spread, transition and continuous
  draw data and a per-draw colour filter; the pipeline route needs nothing in the compositor, the detail tier reuses the stored result, and it is a toggle, not a slider, so re-decoding on a change is acceptable. The design's
  own fallback for sharpen ("bake it into the pipeline with the level in the cache key") is therefore the mechanism for auto-levels and crop, while **sharpen stays render-time** (a paint-level Skia matrix convolution with
  CE's kernel, live on the slider). Cache identity: `PageId` gained a `Variant` (bit 0 auto-levels, bit 1 this page's crop); `ReaderImagePipeline.PageVariant`, `SetProcessing` (drops pages cached under another variant) and
  `IReaderPageProcessing`. The compressed-bytes tier is keyed without the variant. The colour-matrix stretch is a separate `CreateLevelsMatrix` applied while the page is copied, before contrast, brightness and saturation.
- **Order inside the pipeline:** decode, analyse (auto-crop rectangle first, then the levels histogram over the cropped region only, so a white margin cannot hold the white point at 1.0), one Skia draw that crops and applies the
  levels matrix, then the existing viewport-width downscale. The result is an immutable Avalonia `Bitmap` (`SkiaBitmapConverter.ToImmutableBitmap`): a `WriteableBitmap` cannot be passed to `CreateScaledBitmap`. Webtoon strips are
  skipped by both steps.
- **Crop constants** changed while checking a real page: `KeepMargin` is **2%** (1% left the art touching the edge of the trimmed page) and the "too little left" rule is `MinContent`: a page whose content covers under 25% of it (a mostly
  blank page with one small element) is left alone. The 15% per-side cap made the original "under 40% retained" rule unreachable.
- **Real-page check:** the detectors were run (read-only, from a scratch harness) on a chapter of the user's library. Its pages are long strips (no borders, correctly left alone), and its 720x1129 first page, a dark
  watermark page, was trimmed by about 5% a side with a sensible levels stretch (points 0.03 and 0.80). Regular scanned comic and manga pages were not available and remain the user's check.
- **"Show crop" is a toast, not an overlay** (deviation): the crop is applied to the bitmap, so there is no uncropped page on screen to draw a rectangle over. The palette entry "Show crop (what auto-crop finds)" reports the
  four trims as percentages of the page (or "Nothing to trim"), from `IReaderPageProcessing.DetectCrop`, which decodes the page again.
- **Settings and UI as designed:** `AppSettings.DefaultAutoLevels/DefaultSharpen/AutoCropMargins`, `Issue.AutoLevelsOverride/SharpenOverride`, table `PageCropOverrides` (`PageCropOverride`, unique per issue and page),
  migration `AddImageQualitySettings` (no-op `Down()`); drawer ADJUST gets a Sharpen slider (0-3) and an "Auto levels" button and its Reset clears both; Preferences → Reader → Image Adjustment gets Auto levels and Sharpen and a new
  "Auto-crop" group; `ReaderProfileState.AutoLevels/Sharpen/AutoCrop`; palette entries (Auto levels, Sharpen next level, Auto-crop this visit, Crop this page: follow the setting / never / always, Show crop); the page
  right-click menu's "Auto-crop this page" submenu (paged mode). Auto-crop can be switched for one visit from the palette; leaving the reader clears it.
- **Not verified:** how levels, sharpen and the crop look on real scans, the crop detector on regular comics and manga, sharpen's cost on a big window during zoom glides (if it hitches, bake it into the display bitmap too), and
  the shimmer before/after on a real screentone page.
