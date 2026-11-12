# Panel model training

Scripts for retraining guided view's panel detector on hand-labelled pages
([design](../../docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md),
[plan](../../docs/superpowers/specs/2026-11-07-panel-model-retraining-plan.md)). These are published with the app because the
model is AGPL-3.0. **Page images, labels and the manifest never go in git**: they live in `DATA`, which is
`C:\Users\DeeDee\PaperBunkr-panel-data` (never on D:).

## The tools

| Tool | What it does |
|---|---|
| `sample_pages.py` | Picks pages from a copy of the library DB, gives each issue to one split (`test`, `batch1`, `batch2`), copies the pages to `DATA/<split>/images`, and writes `DATA/manifest.csv`. `--dry-run` only counts. `--veto ID` swaps a page. |
| `LABELLING.md` | The labelling rules and the X-AnyLabeling steps. |
| harness `--prelabel DATA/<split>` | Pre-draws boxes with the app's own pipeline. It never overwrites labels unless `--force` is given. |
| harness `--order-sheet DATA/test` (then `--apply`) | Records the true reading order of the test pages. |
| harness `--score DATA/test --label NAME` | Scores the app's pipeline against the labels. Writes `DATA/scores/NAME.md` (paste it into the design's Results) and `NAME.csv`. `--model PATH` scores another ONNX file. `--webtoon-model` also scores the model on tiled strips. `--overlays` draws the wrong pages. |
| harness `--selftest` | Checks the scoring rules. Run it after changing anything under `Scoring/`. |
| `yolo_to_xany.py DATA/<split>` | Turns pre-drawn YOLO labels into X-AnyLabeling's own JSON, so the boxes appear when the folder is opened. It never overwrites a JSON. |
| `xany_to_yolo.py DATA/<split>` | Turns your saved corrections (the JSON) back into YOLO labels. Run it after every labelling session; it replaces the Export menu, which wrote nothing. |
| `flag_pages.py DATA/<split>` | Lists pages whose boxes look wrong in `<split>/recheck.txt`: overlaps, 8+ boxes, slivers, no boxes. |
| `tile_webtoons.py --selfcheck` | The tiling `make_kaggle_zip.py` uses for webtoon strips (same tiles as the harness's StripTiler), with its own check. |
| `make_kaggle_zip.py batch1 [batch2]` | Builds `DATA/kaggle/paperbunkr-panels.zip`. It refuses `test` and refuses pages whose labels don't match their latest save. It holds out about 10% of issues for validation and keeps 3 tiles per webtoon strip. `--skip-untouched-strips` leaves out strips whose boxes were never corrected. |
| `manga109_to_yolo.py` | Runs inside the notebook: a seeded 1,500-page Manga109-s sample as panel-only YOLO data, read straight from the zip or an unpacked copy. `--selfcheck` tests it. |
| `train_panels.ipynb` | The Kaggle notebook: fine-tune, export ONNX at 1024 and 640, and check the output shape. Run it once with `SMOKE = True` first. |

The harness is `tools/Paperbunkr.PanelHarness`. Build it once with `dotnet build tools/Paperbunkr.PanelHarness`, then run
`tools/Paperbunkr.PanelHarness/bin/Debug/net10.0/Paperbunkr.PanelHarness.exe`. Without `--model` it uses the repo's bundled
model, `src/Paperbunkr.App/PanelModel/panel-detector.onnx`.

## Order of work

1. Run `py -I tools/panel-training/sample_pages.py --dry-run`, then `py -I tools/panel-training/sample_pages.py`.
2. Run `--prelabel DATA/test`. Then correct the boxes in X-AnyLabeling, export, and do the order review (see `LABELLING.md`).
3. Run `--score DATA/test --label baseline --webtoon-model --overlays`. This is the baseline. **If Western page-right is already 90% or more, stop
   here**: the +10-point bar can't be met.
4. Batch 1:
   1. Run `--prelabel DATA/batch1`, then `yolo_to_xany.py`, and correct the boxes.
   2. Run `xany_to_yolo.py DATA/batch1`, then `make_kaggle_zip.py batch1`.
   3. Upload the zip to Kaggle as a **private** dataset named `paperbunkr-panels`.
   4. Import `train_panels.ipynb` into Kaggle and run it with `SMOKE = True`, then with `False`.
   5. Unzip the output into `DATA/models`, then score both ONNX files:
      `--score DATA/test --model DATA/models/panel-batch1-1024.onnx --label batch1-1024`, and the same with 640.
5. Batch 2: pre-label with the better batch-1 model (`--prelabel DATA/batch2 --model DATA/models/<best>.onnx`), correct, retrain on both batches, and score.
6. Delete the Kaggle dataset.

## Training settings (checked 2026-11-07)

- **Ultralytics:** pin `ultralytics==8.4.117`. Version 8.4.142 folded `end2end` into `nms` and changed the default export
  head. Export with `format="onnx", opset=17, end2end=True, nms=False, max_det=300` at `imgsz` 1024 and 640. Check the output
  is `(1, 300, 6)` with the harness's `--probe`.
- **One class:** fine-tune from `manga_panel_detector_fp32.pt` with `names: {0: frame}`. The base model's classes are
  `{0: frame, 1: text}`, and `cls_remap` (on by default) copies the pretrained head row **by class name**, so the name must be
  `frame`. With any other name the class head restarts from random weights: the log says `Transferred 696/708` and has no
  `Remapped 1/1 cls head rows` line (checked on the 2026-11-08 smoke run). The app only reads class 0. Don't use `single_cls`.
- **Manga109-s:** gated on Hugging Face (approval can take a week), a single 3.3 GB zip
  (`Manga109s_released_2026_05_21.zip`). Panel boxes are the `<frame>` elements in each book's XML. The user's copy is in the
  private Hugging Face bucket `Mrehis/Manga109-s-bucket`; the notebook reads it with the Kaggle secret `HF_TOKEN` (a read token)
  into `/tmp`. It is never redistributed.
- **Kaggle:** private datasets up to 200 GB; GPU sessions up to 12 h on a P100 or 2× T4; about 30 GPU hours a week (check
  your account settings).
