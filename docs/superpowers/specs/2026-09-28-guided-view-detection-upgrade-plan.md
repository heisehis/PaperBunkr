# Guided view: panel detection upgrade — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-28-guided-view-detection-upgrade-design.md*

Corpus for measurement: `D:\Users\Ehis\Documents\Comics` (about 2985 cbz, 23 cbr, 4 pdf; publisher folders plus `Manga` and `StoryArcs`). Harness is read-only against it.

Surface area found: `PanelDetector.cs` and `PagePanelService.cs` (`PagePanelAnalyzer`, `PagePanelCache`) in `src/Paperbunkr.App/Services/Reader/Panels/`; the only production caller is the `PanelAnalyzer` seam in `ReaderScreenViewModel.Panels.cs` (`Func<Bitmap, bool, PagePanels>`, tests swap in a fake). `PanelDetector.AnalysisSize` is also used by `PageImageProcessing.cs` (auto-levels/crop), so do not change that constant, add new ones. Existing tests: `PanelDetectorTests.cs`, `ReaderScreenPanelTests.cs`. Precedent for a dev-only harness: `src/Paperbunkr.ScrollHarness` (not in the sln, run by hand); `tools/` holds `Paperbunkr.GcdExtract` (in the sln).

## Step 1: Measurement harness
**Files:** `tools/Paperbunkr.PanelHarness/Paperbunkr.PanelHarness.csproj` (new), `tools/Paperbunkr.PanelHarness/Program.cs` (new). Not added to the sln (mirror ScrollHarness).
**What:** console app referencing `Paperbunkr.App`. Args: comics root, `--issues N` (default 150), `--pages K` (default 8), `--seed`, `--out DIR`. Stratified sample: pick issues evenly across top-level folders (so Manga/webtoon folders are represented), read cbz with `System.IO.Compression` (skip cbr/pdf for now), take K pages spread across the issue skipping the first and last 2, decode with SkiaSharp, run `PagePanelAnalyzer.Analyze(SKBitmap, rtl)` (rtl guessed from a `Manga` path segment). Report per folder and overall: confident vs whole-page %, panel-count histogram, ms/page, and pages whose aspect ratio is tall-strip (over 3:1). Write overlay PNGs (page + drawn rects + panel numbers) for every whole-page result and every result with under 2 or over 12 panels into `--out`, plus a `results.csv`. Never writes into the comics folder or any database.
**Depends on:** none.
**Verify:** run it against the corpus with the current detector; record the baseline in the spec's "Results" section. Open a sample of overlays by eye to sanity-check the drawing.

## Step 2: Heuristic upgrades
**Files:** `PanelDetector.cs` (edit), `PagePanelService.cs` (edit, tiling for tall pages), `PanelDetectorTests.cs` (edit, new synthetic cases).
**What:** per the spec: adaptive paper color and variance-based background test, bleed and skew tolerance in the gutter test, cut-order fix, and width-based tiled analysis for tall strips (new constants, `AnalysisSize` untouched). Thresholds stay named constants.
**Depends on:** Step 1 (baseline to compare against).
**Verify:** new synthetic tests (yellowed paper, a balloon crossing a gutter, a 2-degree skew, tall panel beside a stack, a 800x15000 strip) plus the existing `PanelDetectorTests`; re-run the harness after each sub-change and keep only those that improve the numbers.

## Step 3: ONNX detector (mandatory, primary)
**Files:** `src/Paperbunkr.App/Paperbunkr.App.csproj` (edit: `Microsoft.ML.OnnxRuntime` reference, model as content/resource), `src/Paperbunkr.App/Services/Reader/Panels/OnnxPanelDetector.cs` (new: session load, letterbox preprocess, YOLO output decode, NMS), `PanelOrdering.cs` (new: rows and RTL ordering shared with the heuristic path), `PagePanelService.cs` (edit: ONNX first, heuristic fallback), `Assets/Models/` (new, model file), `THIRD-PARTY-NOTICES.md` and the About legal viewer texts (edit: model license and Manga109-s credit), `installer/` script (edit only if the native library is not picked up).
**What:** first download the two candidate models and compare them in the harness on the corpus (add a `--detector heuristic|onnx|both` flag and an agreement column); pick one, or look at other models if Western and webtoon results are weak. Then integrate as primary; keep the `Func<Bitmap,bool,PagePanels>` seam so tests stay fast. Tall strips are tiled with overlap and merged. Performance work is deferred by the user.
**Depends on:** Steps 1 and 2 (harness, fallback path).
**Verify:** harness run of `both` vs the baseline against the accuracy bar; unit tests for `PanelOrdering` and for decode/NMS on hand-made tensors (no model file needed); check on the real app that an ONNX load failure falls back cleanly and that the installer build includes the runtime (only when the user asks for an installer build).

## Step 4: "Report bad page" in the tuning overlay
**Files:** `ReaderScreenViewModel.Panels.cs` (edit), `ReaderScreenViewModel.Palette.cs` (edit), keyboard/palette registration if needed, `ReaderScreenPanelTests.cs` (edit).
**What:** an action next to "Show detected panels" that saves the current page image and detector result (JSON) to a local folder (`%LOCALAPPDATA%\Paperbunkr\panel-reports`), confirmed through the Activity Center. Harness can read that folder as extra input.
**Depends on:** none (can be done any time; useful before fine-tuning if Step 3 is weak on Western or webtoon pages).
**Verify:** headless test with a temp folder; the on-screen check is the user's.

## Order and gates
1 then 2 then 3 (required regardless of Step 2's numbers) then 4. Update `docs/paperbunkr-todo.md` and the spec's Results when steps land.

## Status, 2026-09-29
Steps 1, 3 and 4 are done; step 2 was done in part (analysis resolution and gutter minimum, which took the heuristic from 14.3% to 34.9%; adaptive background and skew tolerance were dropped once the model became primary). Deviations from the plan: tall webtoon strips use a dedicated row-band detector (`PanelDetector.DetectStrip`), not tiled model runs (measured and rejected); a `PanelDetectionService` sits between the reader and the detectors; `PanelDetector.DetectionSize` (1000) was added next to `AnalysisSize` (480, still used by auto-levels/crop); the ONNX path adds a borderless-band pass. Verified by 65 panel tests plus 33 reader panel tests and harness runs on the user's library; not viewed in the running reader. Results are in the design doc.
