# Guided view: retraining the panel model on the user's library

Status: design agreed in a grilling pass, 2026-11-07; awaiting user review. Follows
[2026-09-28-guided-view-detection-upgrade-design.md](2026-09-28-guided-view-detection-upgrade-design.md) (the ONNX detector) and
[2026-10-26-guided-view-performance-design.md](2026-10-26-guided-view-performance-design.md) (detection off the UI thread).
ComicRack CE has no panel detection, so there is no CE parity question; this is Paperbunkr-original.

## Problem

Guided view's primary detector is `leoxs22/manga-panel-detector-yolo26n` (YOLO26-nano), trained only on Manga109-s (Japanese manga).
The user's library is mostly not manga, and the 2026-09-29 "93% confident" figure only means the pipeline found at least one panel. Correctness was
judged by eye on about a dozen pages. Nobody knows how often guided view shows the right panels in the right order on Western comics, manga or webtoons.

This project first measures that, on hand-labelled pages from the user's own library. Then it fine-tunes the model on those pages. The new model ships only if it measurably wins.

## Facts this design rests on (checked 2026-11-05..07)

- **Library** (copy of `%APPDATA%\Paperbunkr\paperbunkr.db`): 9,068 issues, about 120k pages. Series content types: Comic 7,963 issues / 85k pages;
  Manhwa 823 / 30k; Manga 218 / 4.8k; Manhua 5; Unknown 59. Formats: 9,030 `.cbz`, 25 `.cbr`, 3 `.pdf`. Top publishers: Marvel, DC, Image, Daum, Naver
  Webtoon, WEBTOON, VIZ, Boom!. The comics live on `D:\Users\Ehis\Documents\Comics`. **D: is failing** (health at zero): light reads of sample files are
  fine, but no heavy I/O, temp files or writes there.
- **No bad-detection reports** exist yet (`%APPDATA%\Paperbunkr\panel-reports` is absent), so there is no ready-made failure set.
- **The model** (Hugging Face card): classes `0: panel`, `1: text`; 2.57M parameters; trained at 640 px for 66 epochs (best 48) on about 18k Manga109-s
  pages on a Colab T4 in about 4 hours. Augmentation: no hue/saturation shift, no horizontal flip, mosaic 0.5, no rotation. Panel mAP50 0.985. The
  fine-tunable `manga_panel_detector_fp32.pt` is published. The ONNX file we ship is a 1024 px re-export (ultralytics 8.4.117, opset 17), output
  `(1, 300, 6)` end-to-end, no NMS.
- **License:** the model card was changed to **AGPL-3.0** on 2026-09-24 (the author's earlier Apache-2.0 label "was a mistake"; it is a fine-tune of
  Ultralytics' AGPL weights). Ultralytics' position is that trained and fine-tuned weights are AGPL. Paperbunkr is AGPL-3.0, so this is compatible.
  `THIRD-PARTY-NOTICES.md` and the `.csproj` comment were corrected on 2026-11-07, separately from this project.
- **The app** loads the model only from `{baseDir}/Models/panel-detector.onnx` (`PanelDetectionService.ModelPath`; the harness can override it).
  It uses only class 0. Tall pages (height over 2.4x width) never reach the model; they go to `PanelDetector.DetectStrip`. Reading order is
  `PanelOrdering.Sort` (rows top to bottom; a row is panels overlapping the highest remaining one by at least half the shorter height; inside a row,
  columns left to right, or right to left for manga; a column reads top to bottom). Guided view reads right to left when the reader's effective
  reading mode is `ReadingMode.RightToLeft`. The library stores `Series.ReadingMode` and an optional `Issues.ReadingModeOverride`; the reader
  resolves its effective mode from these and other settings.
- **No usable ready-made alternative:** Magi is non-commercial; most "Apache" YOLO comic models are Ultralytics-derived (so AGPL) and large; no
  permissive Western-comic detector with clear provenance was found. Public Western datasets (eBDtheque, DCM772) have unverified licenses.
- **Hardware:** the user's laptop (i3-1005G1, 8 GB RAM, Intel UHD, about 19 GB free on C:) can label, score and export, but not train.

## Approaches considered

1. **Fine-tune the bundled YOLO26-nano (chosen).** Same ONNX output format, so `OnnxPanelDetector` is unchanged. Licensing is already AGPL and fits
   Paperbunkr. Proven to train in hours on a free GPU. Cost: it keeps us on AGPL weights, which rules out a future non-AGPL edition.
2. **Switch to D-FINE-N (Apache-2.0, about 4M parameters).** Keeps a permissive option open, but needs a new detector class and post-processing in the
   app, and its CPU speed is unmeasured. Kept as the fallback idea if approach 1 fails (not part of this project, see "If it fails").
3. **Don't train; label and score only.** This is the first step of approach 1 anyway. If today's model already scores well, the project stops there.

## Decisions (from the grilling rounds)

| # | Decision |
|---|---|
| Q1 | One model for regular pages (Western and manga). Webtoon tiles join the same training run, but webtoons switch from the strip detector to the model only if the model scores higher on webtoon test pages. |
| Q2 | A scored, held-out test set comes first: 150 pages (90 Western, 30 manga, 30 webtoon), never trained on. Today's pipeline is scored on it before any training. |
| Q3 | Fine-tune YOLO26-nano (approach 1). |
| Q4 | Panel-only model (one class). Text boxes are not labelled. |
| Q5 | Label by correcting pre-drawn boxes in X-AnyLabeling (offline, Windows). No labelling UI in Paperbunkr. About 600 training pages and 150 test pages. |
| Q6 | A script samples pages across publishers, content types and eras, keeps each issue wholly in one split, and lets the user veto pages. Pages are copied from D: with light reads. |
| Q7 | Train on Kaggle from a private dataset, which is deleted after training. |
| Q8 | Mix Manga109-s back in so manga accuracy doesn't drop. The user applies for access on Hugging Face. |
| Q9 | Fix the stale Apache notice now, separately (done 2026-11-07). |
| Q10 | A winning model ships in the public app under AGPL. Training scripts and settings are published; page images, labels and the list of sampled issues never are. |
| Q11 | Scripts in `tools/panel-training/`. Data in `C:\Users\DeeDee\PaperBunkr-panel-data` (outside the repo, never on D:, never in git). |
| Q12 | Replacement bar, on the held-out test set: Western page-right rate improves by at least 10 points; manga drops by no more than 2 points; CPU time per page is no more than 10% slower. |
| Q13 | Reading order is scored as well (changed from "boxes only" at the user's request). |
| Q13a | True order: the harness numbers the corrected boxes in the app's order and draws the numbers on each test page; the user types a corrected sequence in `order.csv` only where it is wrong. |
| Q13b | Direction per test page: the issue's `ReadingModeOverride` or its series' `ReadingMode` when either says right to left (`RightToLeft` or `HorizontalContinuousRightToLeft`); otherwise manga right to left, and Western, manhwa and webtoon left to right. Stored in the manifest, and the user can flip any page. |
| Q13c | A new model must also not lower the order-right rate for any content type. |
| Q13d | If the score shows `PanelOrdering` mistakes, fixing them is in scope as its own measured step after the model is chosen. |
| Q14 | Train at 1024 px. Also export and score a 640 px version, and prefer it if it still passes the bar. |
| Q15 | Labelling rules are written down in a one-page guide (below). |
| Q16 | Two labelling batches of about 300 training pages. Batch 1 is pre-labelled by today's model; batch 2 by the batch-1 model. Test pages are labelled first, pre-labelled by today's model and checked with extra care. |
| Q17 | If the new model fails the bar: keep the bundled model, record the scores here, stop. |
| Q18 | Bad-detection reports record the model version (name and file hash). |

## Labelling guide (also saved as `tools/panel-training/LABELLING.md`)

- A **panel** is one framed or visually separate image region that guided view should zoom to. One box per panel, tight to the panel's border or,
  if borderless, to its art.
- **Splash pages and borderless full-page art:** one box around the art.
- **Inset panels** (a small panel drawn on top of a larger one): their own box, and the larger panel keeps its full box.
- **Double-page spreads:** label the image as it is in the file (single page or stitched spread).
- **Webtoons:** a panel is an art block separated from the next by empty background. Speech balloons or text floating in the gaps are not panels.
- **Not panels:** captions, title or credits blocks, page numbers, scanner or site banners, text boxes outside panels.
- **Art that breaks out of its frame** (a figure crossing the border): box the frame, not the overflow.
- **Pages with no panels at all** (text pages, ads, blank pages): leave them with no boxes. These are scored as correct when the pipeline returns the
  whole page.
- When unsure, use what a reader would want the next tap to show.

## Design

### Units

All new code is in `tools/` except the two small app changes at the end. Each unit has one job and talks to the others only through files in the data folder.

1. **Page sampler** (`tools/panel-training/sample_pages.py`, run with `py`).
   - Reads a copy of the library DB, never the live file.
   - Picks issues stratified by series content type, publisher and decade.
   - Assigns each picked issue wholly to `test`, `batch1` or `batch2` (seeded, so it can be re-run).
   - Opens each `.cbz` read-only and copies a few random pages per issue into `PaperBunkr-panel-data/<split>/images/`.
   - Writes `manifest.csv`: page file, split, source path, page index, content type, publisher, year, direction.
   - `.cbr` and `.pdf` are skipped (28 files, under 0.5% of the library).
   - Tall webtoon pages are copied whole.
   - Writes nothing to D:. An archive that fails to open is logged and replaced by another issue from the same stratum.
   - A `--veto <page>` option drops a page and draws a replacement.
2. **Pre-labeller** (new `--prelabel <dir>` mode in `tools/Paperbunkr.PanelHarness`).
   - Runs the app's own pipeline (`PanelDetectionService` with the chosen `--model`) on every image.
   - Writes one YOLO label file per image (class 0, normalised boxes) plus `classes.txt`, in a layout X-AnyLabeling imports.
   - Reusing the harness keeps the letterboxing and band-fill exactly as the app does them.
3. **Labelling** (the user, in X-AnyLabeling): open a split's folder, correct boxes against the guide, and save as YOLO.
4. **Order review** (new `--order-sheet <dir>` mode in the harness, test split only).
   - Sorts each page's corrected boxes with `PanelOrdering.Sort` in the manifest direction.
   - Writes a numbered overlay PNG per page and an `order.csv` with every page marked `ok`.
   - The user edits the rows that are wrong to the correct sequence of overlay numbers, and can flip a page's direction in the manifest.
   - The harness then saves the true order as box indices next to the label file.
5. **Webtoon tiler** (in the trainer's data prep).
   - Cuts each labelled tall strip into overlapping tiles (width x 1.5 width, 25% overlap) for training.
   - Clips boxes to the tile and drops slivers under 3% of the tile.
   - Test strips stay whole; they are scored whole.
6. **Trainer** (`tools/panel-training/train_panels.ipynb`, run on Kaggle).
   - Pins the ultralytics version (decided in the plan, close to 8.4.117).
   - Starts from `manga_panel_detector_fp32.pt` with a one-class head.
   - Trains on about 1,500 sampled Manga109-s pages (converted to panel-only YOLO labels by `manga109_to_yolo.py`) plus the user's training splits, at
     `imgsz=1024`, with the original augmentation settings.
   - Exports end-to-end ONNX at 1024 and 640 (opset 17).
   - The run's config, `results.csv` and exact command are kept in `tools/panel-training/runs/<date>/` (no images or labels).
7. **Scorer** (new `--score <dir>` mode in the harness).
   - Runs the full app pipeline (model, heuristic fallback, strip detector for tall pages) on each test page.
   - Greedily matches detected boxes to labelled boxes at IoU of at least 0.5.
   - Reports per content type:
     - **page-right rate:** every labelled box matched, nothing extra, and the order right;
     - **box-right rate:** the same without order;
     - **order-right rate:** on box-right pages only, the detected order equals the true order;
     - panel precision and recall;
     - median and p95 time per page.
   - A page with no labelled boxes is right when the pipeline returns the whole page.
   - Output is a Markdown table for the Results section below.
   - `--webtoon-model` also scores the model on tiled strips (merging tile boxes across overlaps) against the strip detector.
   - A `--selftest` mode checks matching and order scoring on hand-built cases.

### Steps, in order

1. **Baseline.** Sample all splits, pre-label the test split, the user corrects it and reviews order, then score today's pipeline. Record it below.
   If Western page-right is already 90% or more, the +10 bar can't be met: stop and bring the numbers back to the user before training anything.
2. **Batch 1.** Pre-label with today's model, the user corrects, train on Kaggle, score both exports.
3. **Batch 2.** Pre-label with the batch-1 model, the user corrects, train the final model on batch 1 and batch 2, score both exports.
4. **Decide.** Apply the bar (Q12 + Q13c) to the better passing export (640 if it passes, else 1024). Webtoons switch to the model only if the
   model beats the strip detector on webtoon test pages; the code for tiled strip detection in the app is then planned as a follow-up step, not built
   blind.
5. **Ship (if it passes).**
   - Replace `src/Paperbunkr.App/PanelModel/panel-detector.onnx`.
   - Update `THIRD-PARTY-NOTICES.md`: a Paperbunkr fine-tune, AGPL-3.0, the base model credited, Manga109-s still credited, a link to the training scripts.
   - Update the `.csproj` comment.
6. **Reading order (Q13d).** Score order on the chosen model. If `PanelOrdering` causes misorders, fix the rule involved, update or add unit tests in
   `PanelPipelineTests`/`PanelDetectorTests`, and re-score. Its before and after are recorded separately from the model's.
7. **Report version (Q18).**
   - "Report bad panel detection" adds `"model": { "name": ..., "sha256": ... }` to `detection.json`.
   - The hash is computed once when the detector loads; `"name"` is a constant updated with the model file.
   - The existing report test gets the new field.

### What changes inside the app

Only the model file (step 5), the notices and `.csproj` comment, possibly `PanelOrdering` rules (step 6), and the report field (step 7). No UI, no
settings, no new dependencies.

### Error handling and safety

- The sampler opens archives read-only, works from a DB copy, and writes only to C:. A failing archive is skipped and logged.
- Kaggle holds the user's pages only while training: the dataset is private and deleted after the final run. The notebook never uploads anywhere else.
- If an exported ONNX file doesn't produce `(1, 300, 6)` output, or fails to load in the harness, it is rejected before scoring.
- The app's existing fallback (gutter heuristic, then whole page) is unchanged if the model is missing or fails to load.

### Testing

- Scorer `--selftest` covers: exact match, a missed box, an extra box, IoU just under and over 0.5, a wrong order, and a no-panel page.
- Ordering fixes (step 6) get unit tests in the existing panel test classes. Targeted test runs only.
- The shipped model is checked by the harness score and by opening guided view on a few test pages in the real reader. Ask before any UI automation.

## Your setup

- A **Kaggle** account, phone-verified (needed for GPU time): https://www.kaggle.com
- A **Hugging Face** account, then apply for **Manga109-s** and accept its terms yourself: https://huggingface.co/datasets/hal-utokyo/Manga109-s
- **X-AnyLabeling** for Windows: https://github.com/CVHub520/X-AnyLabeling
- Time: about 4 hours of box correction plus 15-20 minutes of order review, spread over the three labelling rounds.

## Risks

- **Overfitting to this library.** The held-out issues and the Manga109-s mix limit it. A bigger guard would be other people's libraries, which we don't have.
- **Test-set bias toward today's model.** Its boxes are pre-drawn on the test pages. Extra care on those pages is the guard; a test page is never
  pre-labelled by a model being tested.
- **Small test set.** 30 manga and 30 webtoon pages give coarse rates (one page is about 3 points), so the manga "-2 points" rule means "no more than
  one extra wrong page" in practice.
- **Copyright of training images.** The weights are trained partly on commercial comics without the publishers' permission. The user accepted
  shipping on that basis (Q10). Images and labels are never published.
- **Tool details (checked 2026-11-07, plan Step 0):**
  - **X-AnyLabeling 4.3.1:** imports and exports YOLO with a one-line `classes.txt`. Export goes to `images/labels` by default, which is where the
    harness reads and writes labels. Corrections are auto-saved as its own JSON next to each image and reach the YOLO files only on export, so the
    guide says to export after the last edit. Matching labels to images by file name is assumed; try it on 2-3 pages first.
  - **Kaggle:** private datasets up to 200 GB; 12 h GPU sessions on a P100 or 2x T4; about 30 GPU hours a week (not fixed by Kaggle's docs).
    Secrets go under Add-ons > Secrets.
  - **Manga109-s:** one 3.3 GB gated zip (`Manga109s_released_2026_05_21.zip`), approval can take a week. Per-book XML with `<frame>` boxes. The
    2026 revision changed text annotations; that frame boxes are unchanged is not verified.
  - **ultralytics:** pin `8.4.117`, the version that produced the current `(1, 300, 6)` export. 8.4.142 folded `end2end` into `nms` and changed the
    default head. A 2-class to 1-class fine-tune needs no flag (`cls_remap` keeps the panel row). Pass opset 17 explicitly.

## Results

### Baseline, 2026-11-07 (bundled model, `--score DATA/test --label baseline --webtoon-model`)

Test set: 150 pages sampled from the library (seed 1), pre-drawn by the bundled pipeline, then corrected by the user in two passes. 72 pages
were edited (Manga 22 of 30, Western 47 of 90, Webtoon 2 of 9 regular pages and 1 of 21 strips); boxes went from 778 pre-drawn to 688. The second pass
followed a list of 88 suspicious untouched pages (overlapping boxes, 8+ boxes, slivers), because the first pass had left obvious model errors in place.

| Group | Pages | Page right | Boxes right | Order right (of boxes-right) | Panel precision | Panel recall | Median ms | p95 ms |
|---|---|---|---|---|---|---|---|---|
| Western | 90 | 53.3% | 53.3% | 100.0% (48/48) | 84.0% | 98.7% | 240 | 340 |
| Manga | 30 | - | 36.7% | - | 84.1% | 96.8% | 218 | 268 |
| Webtoon | 30 | 93.3% | 93.3% | 100.0% (28/28) | 99.3% | 99.3% | 172 | 244 |
| Webtoon (model on tiles) | 21 | 0.0% | 0.0% | - | 30.9% | 34.1% | 3047 | 3664 |

What it shows:
- **Western pages** are right only about half the time. The model finds almost every panel (recall 98.7%), but about one box in six is extra
  (precision 84%): it splits panels and boxes fragments. **The stop check doesn't apply** (53.3% is far below 90%). The bar for a new model is
  Western page-right of at least 63.3%.
- **Manga** boxes are right on about a third of pages, for the same reason (precision 84%). Its reading order isn't recorded yet: the user found
  the app's order wrong on the manga pages, which are marked `todo` in `order.csv` until the right sequences are typed. So manga page-right and
  order-right are still blank, and the right-to-left ordering in `PanelOrdering` is the first suspect for Step 12.
- **Western and webtoon reading order** was confirmed right on every box-right page in the order review.
- **Webtoon** scores are high but biased upward: the strip labels were pre-drawn by the strip detector being scored, and only 3 of 30 pages were
  edited. **The model on tiled strips is far worse** (0% right, 3 s a page), so webtoons stay on the strip detector unless a retrained model
  changes that.

### Batch 1 model, 2026-11-08 (interim: the test set needs a clean-up before this decides anything)

Trained on Kaggle (T4), 60 epochs at 1024, batch 1 (147 Western, 54 manga, 12 manhwa pages and 171 strip tiles in training; 18 Western, 6 manga and
18 tiles held out) plus 1,500 Manga109-s pages. The smoke run showed the class head starting from random weights: the base model's classes are
`{0: frame, 1: text}`, and ultralytics carries a head row over only when the class *name* matches, so the notebook now names the class `frame`.
With that fix the real run's validation mAP50 was 0.98 after the first epoch and 0.987 at the end.

| Model | Western page right | Western precision / recall | Manga boxes right | Webtoon page right | Model on tiles | Western median ms |
|---|---|---|---|---|---|---|
| bundled (rescored today, same numbers as above) | 53.3% | 84.0% / 98.7% | 36.7% | 93.3% | 0.0% | 264 |
| batch1-1024 | 34.4% | 80.9% / 82.9% | 40.0% | 70.0% | 38.1% | 250 |
| batch1-640 | 40.0% | 81.5% / 82.9% | 36.7% | 70.0% | 33.3% | 94 |

On held-out **batch 1** pages (same labelling as its training data, never trained on) the new model is far better: Western boxes right 61.1% against
22.2% for the bundled model (18 pages), manga 66.7% against 100% (6 pages). So the test set and batch 1 don't follow the same conventions, and the
test set leans toward the bundled model:
- **17 of 150 test pages carry duplicate boxes** (IoU > 0.6) left from the pre-drawn labels, against 2 of 300 in batch 1. The bundled model draws
  the same duplicates and gets credit for them; 14 of the 44 pages the new model "lost" are on that list.
- Rule breaks kept from the pre-drawn labels: an ad with 4 boxes, a cover and two splashes cut into pieces, a balloon boxed, a whole row in one box.
- The new model also has faults of its own: it boxes text blocks on credits pages, draws an empty white box on one manga page, and merges a
  borderless multi-scene page into one box.

Next: clean the test set by the written rules only (`DATA/test/fix-list.txt`), rescore all three models, and only then compare against the bar.
The 640 export is about 2.8 times faster than the bundled model on CPU, so the time bar is met either way.
