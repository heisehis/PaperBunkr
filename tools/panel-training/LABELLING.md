# Labelling guide: panels

For the panel-model retraining project
([design](../../docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md)). The test pages and the training pages
follow the same rules, or the scores mean nothing.

## What counts as a panel

- A **panel** is one framed or visually separate image region that guided view should zoom to. Draw one box per panel, tight
  to its border; for a borderless panel, tight to its art.
- **Splash pages and borderless full-page art:** one box around the art.
- **Inset panels** (a small panel drawn on top of a larger one): their own box. The larger panel keeps its full box.
- **Double-page spreads:** label the image as it is in the file, whether that's a single page or a stitched spread.
- **Webtoons:** a panel is an art block separated from the next one by empty background. Speech balloons or text floating in
  the gaps are not panels.
- **Not panels:** captions, title or credits blocks, page numbers, scanner or site banners, and text boxes outside panels.
- **Art that breaks out of its frame** (a figure crossing the border): box the frame, not the overflow.
- **Pages with no panels** (text pages, ads, blank pages): leave them with no boxes.
- When unsure, box what a reader would want the next tap to show.

## Doing it in X-AnyLabeling

Use version 4.3.1 or later (Windows CPU installer from
[its releases page](https://github.com/CVHub520/X-AnyLabeling/releases)). The harness has already written pre-drawn boxes
into `DATA\<split>\images\labels\`, where `DATA` is `C:\Users\DeeDee\PaperBunkr-panel-data`.

1. **Turn the pre-drawn boxes into X-AnyLabeling's own files** (once per split, before opening it):
   `py -I tools/panel-training/yolo_to_xany.py DATA\<split>`. This writes a `.json` next to each image and never overwrites
   one that's already there. (The Upload → YOLO HBB menu is meant to do the same, but didn't load anything on 2026-11-07.)
2. **Open** the folder `DATA\<split>\images` (File → Open Dir). The boxes appear on each page.
3. **Correct each page:** move, resize, delete or add rectangles, all with the label `panel`. X-AnyLabeling auto-saves your
   work as a `.json` file next to each image.
4. **No export needed.** Your corrections are auto-saved in the `.json` files, and
   `py -I tools/panel-training/xany_to_yolo.py DATA\<split>` turns them into the YOLO files in `images\labels` that the harness
   and the trainer read. Run it after every labelling session; it's safe to re-run. The first run keeps the pre-drawn labels in
   `images\labels-prelabel`. (The Export → YOLO HBB menu wrote nothing on 2026-11-07.)

## Test pages: reading order

Only for `DATA\test`, after all its boxes are corrected and `xany_to_yolo.py` has run:

1. Run the harness with `--order-sheet DATA\test`. It writes `DATA\test\order-review\<id>.png`, with your boxes numbered in
   the order the app would read them, plus `DATA\test\order.csv`.
2. Look through the pictures. Where the numbers are wrong, type the right sequence in that page's `order` column in
   `order.csv`. For example, if the app numbered the boxes 1-2-3-4 but the right order is first, third, second, fourth,
   type `1 3 2 4`. Leave `ok` where the order is right.
3. If a page reads in the wrong direction (manga that's left to right, or the other way round), change its `direction` to
   `ltr` or `rtl`. Leave its `order` as `ok` if the numbers would then come out right.
4. Run `--order-sheet DATA\test --apply`. It rejects any row it can't use and says why.

If you change a test page's boxes after this, run steps 1–4 again. The scorer refuses a page whose recorded order no
longer fits its boxes.
