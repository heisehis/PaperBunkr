# Guided view: panel detection upgrade

Status: draft for review, 2026-09-28. Follows [2026-09-25-comic-reader-panels-and-zoom-design.md](2026-09-25-comic-reader-panels-and-zoom-design.md) (slice D). CE has no panel detection, so there is no CE parity question; this is Paperbunkr-original.

## Problem

Guided view finds the right panels on too few real pages. `PanelDetector` (`src/Paperbunkr.App/Services/Reader/Panels/PanelDetector.cs`) is a recursive gutter-cut on a 480px luminance grid: a row/column is gutter only if >= 98.5% of it is >= luma 235 (or <= 20). It was only ever run on synthetic pages and one webtoon, never tuned on real comics, manga and manhwa. The user reports it working roughly a quarter of the time. That figure is unmeasured; this design measures it first.

Library: Western comics, manga, manhwa/webtoons; digital and scanned.

## Suspected causes (to confirm with the harness, not assumed)

1. Fixed background thresholds fail on yellowed or gray scan paper (about 215-230).
2. One balloon, effect or character crossing a gutter breaks the 98.5% test for that whole line.
3. Slightly skewed scans have no clean gutter rows.
4. Rows-first cutting mis-splits pages with a tall panel beside a stack.
5. Webtoon strips (about 800x15000) are scaled to 480 on the long side, about 25px wide, so nothing can be detected. Needs width-based analysis, in tiles.
6. Borderless and splash pages have no gutter: heuristics cannot fix these; they fall back to whole page (unchanged) or to the ML step below.

## Non-goals

- No OpenCV, Canny or contour pipeline (adds a native dependency, finds art edges rather than panels).
- No DB persistence of detections (in-memory cache stays).
- No change to guided-view navigation, zoom, or the reading-order rules except the cut-order fix in step 3.

## Plan, in order (steps 1-2 gated on measured numbers; step 3 is required regardless)

### 1. Measurement harness (before any detector change)
A read-only scratch console tool (not shipped) that runs `PagePanelAnalyzer` over a folder of archives and reports, per issue and overall: confident vs whole-page share, panel-count histogram, and time per page. It writes overlay PNGs for whole-page results and for pages with implausible panel counts. It touches no library database and no comic files. Baseline numbers get recorded in this spec's "Results" section.

### 2. Heuristic upgrades (all in `PanelDetector`, unit-tested with synthetic pages)
- **Adaptive background:** estimate paper tone from the page border and the thinnest full-length runs, then treat pixels within a tolerance of it (and low local variance) as background, instead of fixed 235/20.
- **Gutter tolerance:** allow a small bleed budget per gutter line and probe near-vertical/horizontal gutters over a few degrees of skew.
- **Cut order:** cut rows first only when the row gutter spans the region and the resulting halves stay consistent; otherwise try the column cut first.
- **Tall pages:** analyse at a width-based scale and process in overlapping tiles, stitching panel rects; keeps the `AnalysisSize` semantics for normal pages.
Every threshold stays a named constant. Re-run the harness after each change; keep a change only if the numbers improve.

### 3. ONNX panel detector (mandatory, primary detector)
Decided 2026-09-28: the ML detector is a required part of this work, not gated on the heuristic's numbers. Add `Microsoft.ML.OnnxRuntime` plus an Apache-2.0 panel model (candidates: `leoxs22/manga-panel-detector-yolo26n`, ONNX export `mednasserallah/manga-panel-detector-yolo26n-onnx`) and make it the primary detector, bundled with the app (recommended, since it is required; the model is small). The step-2 heuristic stays as the fallback when the model returns nothing or fails to load. Performance tuning (session reuse, GPU providers, caching, tile batching) is explicitly deferred by the user until detection is correct.
Must be verified with the harness before we call it working, not assumed: the candidate model is trained on Manga109-s (manga only), so Western comic and webtoon accuracy is unknown; if a candidate is weak on those, evaluate other models (e.g. YOLO comic-panel models on Hugging Face/Roboflow, checking each license) or fine-tune, using the step-4 reported pages as data. Also: the Manga109-s license requires crediting the dataset (About/legal viewer, THIRD-PARTY-NOTICES.md); check native-library packaging in the installer and that it does not affect the no-ReadyToRun rule.
Post-processing owns reading order (row grouping, RTL for manga), whole-page fallback, and tiling of tall webtoon strips.

### 4. "Report bad page" in the tuning overlay
The palette's "Show detected panels" overlay gets an action that saves the page image and the detector's result to a local folder, building a real regression set from the user's own pages. Per the activity-center rule, its confirmation goes through the Activity Center.

## Accuracy bar

Right panels on 90% of regular pages, whole-page fallback (never wrong panels) on the rest. "Right" = the harness's reviewed sample, judged by eye from overlay PNGs, not an automated metric. Splash and borderless pages count as correct when they fall back.

## Risks

- Tuning to the user's own library may over-fit; the reported-page set and synthetic tests guard against regressions on other content.
- ONNX adds a native dependency, installer size and licensing obligations (accepted by the user); the heuristic fallback limits the blast radius if it fails to load.
- The only ready-made model found is manga-trained; Western and webtoon accuracy is the main unknown, which the harness measures.

## Results

### Step 1 baseline, 2026-09-28 (current heuristic, `tools/Paperbunkr.PanelHarness`)
Sample: 78 issues across 14 folders of `D:\Users\Ehis\Documents\Comics`, 624 pages, seed 1. **Confident on 14.3% of pages** (535 of 624 fall back to whole page). Per folder: Dark Horse 1.2%, Dynamite 2.5%, DC 8.8%, Titan 10.0%, Marvel 18.8%, Image 20.0%, Boom! 41.2%, root-level loose files 0%. Panel counts on the confident pages: 2:46, 3:12, 4:19, 5:9, 6:2, 7:1. About 27 ms per page. "Confident" is not "correct": pages were only spot-checked, not scored.
Observed from overlays: the detector fails on ordinary, clean pages too (e.g. an Image page with obvious white gutters and black frames), so the cause is broader than the yellowed-paper and bleed hypotheses; the DC page with dark teal gutters and a scanner-site banner across the bottom is a "coloured gutter" case the fixed 235/20 luminance thresholds cannot represent. Root cause on the clean Image page still to be diagnosed in step 2.
Gaps in that first sample: the `Manga` folder holds only `.cbz.tmp` files (webtoon downloads, complete enough to read; the harness's `--tmp` flag includes them) and `StoryArcs` only `.cbl`, so the first run measured no manga or webtoon pages and no tall strips. The harness only reads `.cbz`; `.cbr` (23) and `.pdf` (4) are skipped.

### Steps 2 and 3, 2026-09-29 (`--detector both --tmp`, same sampling, 639 pages including 23 webtoon strips)
Progression on the 616 regular pages: 14.3% (baseline) -> **34.9%** after analysing at 1000 px with a finer minimum gutter (resolution was the largest single cause: a 480 px analysis grid made real gutters 3-5 px wide) -> **93.0%** with the ONNX model first and the heuristic as fallback. Webtoon strips (23 pages): **95.7%**, at about 150 ms per page with the strip detector; the model on tiled strips was tried first and rejected (it boxes speech balloons and misses the art blocks, and cost 3-8 s per page). Per folder (regular pages): Boom! 96.2%, Dark Horse 96.2%, DC 95.0%, Image 95.0%, Marvel 90.0%, Titan 87.5%. About 420 ms per page on CPU (unoptimised: model at 1024 px, PNG round trip from the reader bitmap, run on the UI thread by the smart double-click; performance is deferred by the user).
**What "confident" means now:** the pipeline found at least one panel, not that the panels are right. Correctness was judged only by eye on about a dozen overlays, not scored. What those showed: framed panels and inset panels are found well; splash pages and diagonal-panel spreads correctly fall back to the whole page; a borderless wide panel between framed rows is filled in by the band pass (`FindBorderlessBands`); the side-gap fill needed a 20% minimum width after a 10% setting produced a spurious sliver; the model sometimes misses a narrow panel or leaves a leftover bottom band as an extra panel; webtoons with a striped background (no empty bands) fall back to the whole page. Panel counts up to 9 are common now (7 or more on about 20% of pages), which could be over-splitting on some pages and has not been checked systematically. The heuristic alone is now confident on 230 pages of the sample (36%) and agrees with the model on panel count on only 67 of them.
Not verified: accuracy on your manga (no regular manga pages were in the corpus, only webtoons), scanned/yellowed pages, and on-screen feel. Not done: the heuristic's adaptive-background and skew upgrades (not needed while the model is primary), any performance work.
