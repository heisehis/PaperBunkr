# Panel model retraining — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md*

Steps marked **(you)** need the user; everything else is code or scripts. The data folder is `C:\Users\DeeDee\PaperBunkr-panel-data`
(below, `DATA`). Nothing is ever written to D:.

Conventions this plan follows:
- **Harness:** `tools/Paperbunkr.PanelHarness` stays out of `Paperbunkr.sln`. It links the Avalonia-free detector sources and has no test project, so
  its own checks are a `--selftest` mode. Build it with `dotnet build tools/Paperbunkr.PanelHarness`. Never run it at the same time as another
  `dotnet build`/`dotnet test`, and stop the build servers afterwards.
- **Python:** scripts run with `py -I`. Local scripts use only the standard library plus numpy and Pillow, which are already installed. ultralytics and
  onnxruntime are used only inside the Kaggle notebook.
- **App tests:** targeted classes only (`--filter "FullyQualifiedName~..."`), never the whole suite.

## Step 0: Check the unverified tool details
**Files:** none (findings go into the design's Risks section).
**What:** confirm four things before writing code that depends on them:
- **X-AnyLabeling:** the folder layout and `classes.txt` it expects when importing and exporting YOLO labels.
- **Kaggle:** the private-dataset size limit and the current weekly GPU quota.
- **Manga109-s on Hugging Face:** its annotation format (frame boxes in per-book XML is expected) and that a token can download it from a notebook.
- **ultralytics:** the version that exports YOLO26 end-to-end ONNX with output `(1, 300, 6)` at opset 17.

**Depends on:** none. **Verify:** each answer recorded with its source.

## Step 1: Split the harness into modes
**Files:**
- `tools/Paperbunkr.PanelHarness/Program.cs` (edit)
- `Modes/SurveyMode.cs` (new; today's sampling run, moved unchanged)
- `Common/Args.cs` (new; `IntArg`/`StrArg`)
- `Common/Overlay.cs` (new; `WriteOverlay`/`WriteTallOverlay`, plus a variant that draws given boxes in a given colour)

All new files are under `tools/Paperbunkr.PanelHarness/`.

**What:** `Program.cs` becomes a dispatcher:
- `--probe`, `--selftest`, `--prelabel`, `--order-sheet` and `--score` each run their mode.
- Anything else runs `SurveyMode` with today's arguments, so existing commands keep working.
- Top-level statements stay in `Program.cs` only.

**Depends on:** none. **Verify:** the harness builds. `--probe <model>` prints the same input/output shapes as before. A short survey run
(`--issues 2 --pages 2`) prints the same table format.

## Step 2: Scorer and self-test
**Files:**
- `tools/Paperbunkr.PanelHarness/Scoring/YoloLabels.cs` (new): reads and writes YOLO txt (class, cx, cy, w, h normalised) and converts to and from `PanelRect`.
- `Scoring/PageTruth.cs` (new): a page's true boxes plus its true order (box indices), loaded from `<image>.txt` and `<image>.order`.
- `Scoring/PanelMatcher.cs` (new): greedy IoU matching (highest IoU first, threshold 0.5), then `BoxesRight`, `OrderRight`, `PageRight`, matched/extra/missed counts.
- `Scoring/StripTiler.cs` (new): tiles a tall strip (width x 1.5 width, 25% overlap), runs `OnnxPanelDetector.Run` per tile, maps boxes back, and merges boxes cut by a tile seam (same column, overlapping or touching across the overlap band).
- `Modes/ScoreMode.cs` (new)
- `Modes/SelfTest.cs` (new)

**What:**
- **Inputs:** `--score DATA/test --model PATH [--webtoon-model] [--label NAME]` reads `DATA/manifest.csv` for content type and direction.
- **Run:** `PanelDetectionService.Detect` on each test image, timed.
- **Rules:**
  - A page with no true boxes is right when the result is unconfident.
  - Detected order is the result's `Rects` order mapped through the matching.
  - Box-right pages with a different order count as order-wrong.
- **Output:** per-page CSV (`DATA/scores/<label>.csv`) and a Markdown table (`DATA/scores/<label>.md`) per content type: pages, page-right %,
  box-right %, order-right % (of box-right pages), panel precision and recall, median and p95 ms.
- **Webtoon comparison:** `--webtoon-model` adds a second webtoon row scored with `StripTiler`.
- **Self-test cases:** exact match; one missed box; one extra box; IoU 0.49 vs 0.51; a swapped order; a no-panel page with an unconfident and a
  confident result; two tile halves merged into one box.

**Depends on:** Step 1. **Verify:** `--selftest` passes every case and exits non-zero on any failure.

## Step 3: Page sampler
**Files:** `tools/panel-training/sample_pages.py` (new), `tools/panel-training/README.md` (new: what each script does and the order to run them).
**What:**
- **Database:** copies `%APPDATA%\Paperbunkr\paperbunkr.db` to `DATA/lib.db` and never opens the live file.
- **Issues:** `.cbz` only; publisher; series content type; year; `Issues.ReadingModeOverride` / `Series.ReadingMode` (handle both int and string storage of `ReadingMode`).
- **Strata:** content type x publisher group (top 8 publishers, then "other") x decade. The seed is fixed, default 1.
- **Splits:**
  - test: 90 Western, 30 manga, 30 webtoon/manhwa pages;
  - batch1 and batch2: about 300 pages each, in the same proportions as the library but with manga and webtoon raised to at least 20% each.
- **Issues and pages:**
  - Each issue goes to one split only.
  - At most 3 pages per issue, avoiding the first two and last two pages (covers and ads), as the harness does today.
  - Pages are copied byte for byte into `DATA/<split>/images/<id>.<ext>`.
- **Manifest:** `DATA/manifest.csv` with columns `id, split, issue_id, source_path, entry, content_type, publisher, year, direction, width, height, tall`.
- **Direction:** right to left when the stored mode is `RightToLeft` or `HorizontalContinuousRightToLeft`, or when the content type is Manga and no
  mode is stored; otherwise left to right.
- **Failures:** an archive that fails to open is logged to `DATA/sample.log` and replaced from the same stratum.
- **Options:**
  - `--dry-run` prints the counts per split and stratum without touching D:.
  - `--veto <id>` removes a page and draws a replacement from the same stratum.

**Depends on:** none. **Verify:** `--dry-run` first, showing the counts. Then the real run, checking that the manifest row count matches the image
count, that no issue appears in two splits, and that `DATA` is under 1.5 GB.

## Step 4: Pre-labeller
**Files:** `tools/Paperbunkr.PanelHarness/Modes/PrelabelMode.cs` (new).
**What:**
- `--prelabel DATA/<split> --model PATH` runs `PanelDetectionService.Detect` (direction from the manifest) on each image.
- For each image it writes `<id>.txt` in YOLO format, or an empty file when the result is unconfident, plus `classes.txt` (`panel`), in the layout found in Step 0.
- It never overwrites an existing label file unless `--force` is given, so corrected labels are safe.

**Depends on:** Steps 1, 2 (`YoloLabels`), 3. **Verify:** run it on the test split. Then open it in X-AnyLabeling **(you)** and check that boxes
appear where the overlay PNGs show them.

## Step 5: Order sheet
**Files:** `tools/Paperbunkr.PanelHarness/Modes/OrderSheetMode.cs` (new).
**What:**
- `--order-sheet DATA/test` sorts each page's corrected boxes with `PanelOrdering.Sort` in the manifest direction.
- It draws the numbers with `Overlay` into `DATA/test/order-review/<id>.png` and writes `DATA/test/order.csv` (`id, direction, order`), with
  `order` = `ok`.
- `--order-sheet DATA/test --apply` reads `order.csv` and writes `<id>.order`, one line of box indices in true reading order. It uses the app order for
  `ok` rows, and the typed sequence (overlay numbers) otherwise.
- It rejects a sequence that isn't a permutation of the page's boxes, and lists those pages.
- A changed `direction` in `order.csv` is written back to the manifest.

**Depends on:** Steps 1, 2. **Verify:** the self-test gains a case for applying a typed sequence. A hand check on 3 pages (you).

## Step 6: Labelling guide
**Files:** `tools/panel-training/LABELLING.md` (new; the guide from the design, plus how to open a split in X-AnyLabeling and save as YOLO, from Step 0).
**Depends on:** Step 0. **Verify:** read-through (you).

## Step 7: Model version in bad-detection reports
**Files:**
- `src/Paperbunkr.App/Services/Reader/Panels/PanelDetectionService.cs` (edit)
- `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.Panels.cs` (edit)
- `src/Paperbunkr.App.Tests/ReaderScreenPanelTests.cs` (edit)

**What:**
- `PanelDetectionService` gets `public const string ModelName = "manga-panel-detector-yolo26n-onnx1024"`. It changes whenever the model file changes.
- It also gets `public static string? ModelSha256`: the SHA-256 of the file at `ModelPath`, computed once, lazily, off the UI thread inside
  `GetOnnx` when the model loads. It is reset when `ModelPath` changes, and null when the model isn't loaded.
- `ReportBadPanelsAsync` adds `model = new { name = ModelName, sha256 = ModelSha256 }` to `detection.json`.
- `ReportBadPanels_SavesThePageAndTheDetection_AndIsInThePalette` asserts that `model.name` is present.

**Depends on:** none (independent of every other step). **Verify:** `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~ReaderScreenPanelTests"`,
and the harness still builds (it links `PanelDetectionService.cs`).

## Step 8: Baseline (you, then me)
**What:**
1. Run Steps 3–5 for the test split.
2. **(You)** Correct the 150 test pages in X-AnyLabeling and review `order-review/`.
3. Run `--order-sheet --apply`, then `--score DATA/test --model <bundled> --webtoon-model --label baseline`.
4. Paste `DATA/scores/baseline.md` into the design's Results section.

**Stop check:** if Western page-right is 90% or more, stop and bring the numbers to the user before any training.
**Depends on:** Steps 2–6.

## Step 9: Training notebook and data prep
**Files:** all new, under `tools/panel-training/`:
- `train_panels.ipynb`
- `manga109_to_yolo.py`
- `tile_webtoons.py`
- `dataset.yaml.template`
- `runs/.gitkeep`

**What:**
- `tile_webtoons.py` (local) turns labelled tall pages into tiles plus clipped labels in `DATA/<split>/tiles/`. Boxes under 3% of the tile side are dropped.
- `manga109_to_yolo.py` (runs on Kaggle) converts Manga109-s frame annotations to panel-only YOLO labels for a seeded sample of 1,500 pages.
- **The notebook:**
  1. Installs the pinned ultralytics from Step 0.
  2. Downloads `manga_panel_detector_fp32.pt`, and Manga109-s with the user's HF token stored as a Kaggle secret.
  3. Mounts the private dataset (zip of `DATA/batch*/images`, labels and tiles, without `test`).
  4. Writes the dataset YAML with `nc: 1`, `names: [panel]`.
  5. Trains: `imgsz=1024`, `epochs=60`, `patience=15`, `hsv_h=0`, `hsv_s=0`, `fliplr=0`, `mosaic=0.5`, `degrees=0`.
  6. Exports end-to-end ONNX at 1024 and 640 (opset 17).
  7. Prints the output shapes.
- Run outputs (config, `results.csv`, the command) are saved to `runs/<date>/`. ONNX files go to `DATA/models/`, not git.
- `tools/panel-training/make_kaggle_zip.py` (new) builds the upload zip from `DATA` and refuses to include `test`.

**Depends on:** Step 0. **Verify:** `tile_webtoons.py` round-trips a hand-made strip (a self-check in the script). The notebook runs end to end on a
20-page smoke dataset before the real run.

**Built 2026-11-07.** Changes from the plan above:
- **Tile cap:** `make_kaggle_zip.py` keeps 3 tiles per webtoon strip (tiles with panels first). All tiles would have been 667 for batch 1,
  four times the Western pages; capped, it's 171 against 147 Western and 54 manga.
- **Notebook smoke switch:** `SMOKE = True` (1 epoch, 40 Manga109 pages) replaces the separate 20-page smoke dataset.
- **Manga109-s source:** the notebook takes it from an attached copy (zip or unpacked) or downloads it with `HF_TOKEN`.
- **640 px exports:** `OnnxPanelDetector` now reads its input size from the model file (`ModelInputSize`). It used to be hard-wired to
  1024, so a 640 export couldn't be scored or used. The bundled model re-scored identically.
- **Checks run:** tile and Manga109 self-checks pass; the zip builder refuses `test` and was tried on batch 1. The notebook's cells parse,
  but it hasn't run on Kaggle yet.

## Step 10: Batch 1 and batch 2 (you, then me)
**What:**
1. Batch 1: pre-label with the bundled model, **(you)** correct, tile, upload, train, then score both exports
   (`--label batch1-1024`, `--label batch1-640`).
2. Batch 2: pre-label with the better batch-1 export, **(you)** correct, train on batch 1 and batch 2, then score both exports.
3. **(You)** Delete the Kaggle dataset after the final run.
4. Add every score table to Results.

**Depends on:** Steps 8, 9.

## Step 11: Decide and ship
**Files:**
- `src/Paperbunkr.App/PanelModel/panel-detector.onnx` (replace)
- `src/Paperbunkr.App/Services/Reader/Panels/PanelDetectionService.cs` (`ModelName`)
- `THIRD-PARTY-NOTICES.md`
- `src/Paperbunkr.App/Paperbunkr.App.csproj` (comment)
- the design's Results section

**What:**
- Apply the bar to the 640 export if it passes, else the 1024 export: Western page-right +10 points or more; manga −2 points at most; CPU time no
  more than 10% slower; order-right not lower for any content type.
- **If it passes:**
  - Swap the model file and set `ModelName` to a new value.
  - THIRD-PARTY-NOTICES: a Paperbunkr fine-tune, AGPL-3.0, the base model and Manga109-s credited, a link to `tools/panel-training/`.
  - Record which webtoon row won. If the model beat the strip detector, add tiled-strip detection in the app as a new design/plan item; it is not
    built here.
- **If it fails:** record the scores, keep the bundled model, and stop (Q17).

**Depends on:** Step 10. **Verify:**
- `--score` with the shipped file.
- Build `Paperbunkr.App` into bin.
- `ReaderScreenPanelTests` and `PanelPipelineTests`.
- Open guided view on a few test pages in the real reader. Ask before any UI automation.

## Step 12: Reading-order fixes
**Files:** `src/Paperbunkr.App/Services/Reader/Panels/PanelOrdering.cs` (edit, if needed), `src/Paperbunkr.App.Tests/PanelPipelineTests.cs` (edit).
**What:**
- From the chosen model's score, list the box-right but order-wrong pages and group them by cause. Expected causes:
  - row grouping (`RowOverlap`);
  - column tolerance (`SameColumnTolerance`);
  - stacks beside a tall panel.
- Fix one rule at a time. Each fix gets a unit test built from a real failing page's boxes, and is re-scored after.
- Keep a fix only if order-right goes up and no other page loses its order.
- Record the before/after as its own Results row.

**Depends on:** Step 11 (the model must be chosen first, so the effects stay separate). **Verify:** `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~PanelPipelineTests"`,
then the harness score.

## Step 13: Docs
**Files:** `docs/paperbunkr-todo.md` (an entry with status and what was verified), the guided-view memory note.
**Depends on:** Step 11 or 12. **Verify:** none beyond a read-through.

## Order and parallel work

- **Code that can start now:** Steps 0, 1, 3, 6 and 7 are independent. Step 2 needs Step 1. Steps 4 and 5 need Steps 1–3.
- **Your time** is the long pole: Step 8's 150 test pages, then 300 pages each in Step 10.
- **Gate:** nothing after Step 8 starts until the baseline numbers are in and the stop check has been applied.
