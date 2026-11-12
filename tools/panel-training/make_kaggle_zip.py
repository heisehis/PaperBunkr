"""Builds the private Kaggle dataset zip from the corrected training splits
(docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md, Trainer; plan Step 9).

    py -I tools/panel-training/make_kaggle_zip.py batch1            # after correcting batch 1
    py -I tools/panel-training/make_kaggle_zip.py batch1 batch2     # final run

- Refuses the test split: test pages must never be trained on.
- Refuses to run if any page's labels don't match its latest X-AnyLabeling save (run xany_to_yolo.py first), or a page was never opened.
- Holds out about 10% of issues (whole issues, per content group) as the validation set used for early stopping.
- Tall webtoon strips go in as tiles (tile_webtoons.py), the same tiles the harness scores; 3 per strip by default, tiles with panels
  first, so webtoons don't outnumber the Western pages.
- Puts manga109_to_yolo.py in the zip for the notebook. No source paths, issue names or manifest go in.

Writes DATA/kaggle/paperbunkr-panels.zip with images/{train,val}/ and labels/{train,val}/.
"""

from __future__ import annotations

import argparse
import csv
import io
import os
import random
import sys
import zipfile
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tile_webtoons  # noqa: E402
import xany_to_yolo  # noqa: E402

DEFAULT_DATA = r"C:\Users\DeeDee\PaperBunkr-panel-data"
IMAGE_EXT = (".jpg", ".jpeg", ".png", ".webp")


def group_of(content_type: str) -> str:
    return "Manga" if content_type == "Manga" else "Webtoon" if content_type in ("Manhwa", "Manhua") else "Western"


def read_labels(path: str) -> list[tuple[float, ...]]:
    with open(path, encoding="utf-8") as f:
        return [tuple(float(x) for x in line.split()[1:5]) for line in f if line.strip()]


def rounded(lines_or_boxes) -> list[tuple[float, ...]]:
    out = []
    for item in lines_or_boxes:
        values = item.split()[1:5] if isinstance(item, str) else item
        out.append(tuple(round(float(v), 4) for v in values))
    return out


def same_boxes(a: list[tuple[float, ...]], b: list[tuple[float, ...]], tol: float = 0.004) -> bool:
    """True when two label sets are the same boxes, allowing for rounding in the round trip through X-AnyLabeling."""
    a, b = sorted(a), sorted(b)
    return len(a) == len(b) and all(max(abs(x - y) for x, y in zip(p, q)) < tol for p, q in zip(a, b))


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("splits", nargs="+")
    p.add_argument("--data", default=DEFAULT_DATA)
    p.add_argument("--out", default=None)
    p.add_argument("--val-share", type=float, default=0.1)
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--limit", type=int, default=0, help="only the first N pages (for a smoke-test zip)")
    p.add_argument("--tiles-per-strip", type=int, default=3, help="tiles kept per webtoon strip")
    p.add_argument("--skip-untouched-strips", action="store_true",
                   help="leave out webtoon strips whose boxes are still the pre-drawn ones (labels-prelabel)")
    a = p.parse_args()

    if "test" in a.splits:
        print("refusing: the test split is never trained on")
        return 1
    from PIL import Image

    with open(os.path.join(a.data, "manifest.csv"), newline="", encoding="utf-8") as f:
        manifest = {r["id"]: r for r in csv.DictReader(f)}

    pages, problems, skipped = [], [], 0
    for split in a.splits:
        images = os.path.join(a.data, split, "images")
        for name in sorted(os.listdir(images)):
            stem, ext = os.path.splitext(name)
            if ext.lower() not in IMAGE_EXT:
                continue
            json_path = os.path.join(images, stem + ".json")
            label_path = os.path.join(images, "labels", stem + ".txt")
            if not os.path.exists(json_path):
                problems.append(f"{stem}: no X-AnyLabeling save (.json)")
                continue
            if not os.path.exists(label_path) or rounded(xany_to_yolo.convert_file(json_path)) != rounded(read_labels(label_path)):
                problems.append(f"{stem}: labels don't match the latest save")
                continue
            if a.skip_untouched_strips and manifest[stem]["tall"] == "True":
                pre_path = os.path.join(images, "labels-prelabel", stem + ".txt")
                if os.path.exists(pre_path) and same_boxes(read_labels(pre_path), read_labels(label_path)):
                    skipped += 1
                    continue
            pages.append((split, stem, os.path.join(images, name), label_path, manifest[stem]))
    if skipped:
        print(f"left out {skipped} untouched webtoon strips")
    if problems:
        print(f"{len(problems)} pages aren't ready. Run: py -I tools/panel-training/xany_to_yolo.py DATA\\<split> for each split, then try again.")
        for line in problems[:15]:
            print("  " + line)
        return 1
    if a.limit:
        pages = pages[: a.limit]

    # Validation: whole issues, about val_share of each content group.
    rng = random.Random(a.seed)
    issues_by_group = defaultdict(set)
    for _, _, _, _, row in pages:
        issues_by_group[group_of(row["content_type"])].add(row["issue_id"])
    val_issues = set()
    for group, issues in issues_by_group.items():
        ordered = sorted(issues)
        rng.shuffle(ordered)
        val_issues.update(ordered[: max(1, round(len(ordered) * a.val_share))])

    out = a.out or os.path.join(a.data, "kaggle", "paperbunkr-panels.zip")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    counts = Counter()
    with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_STORED) as z:
        for split, stem, image_path, label_path, row in pages:
            sub = "val" if row["issue_id"] in val_issues else "train"
            group = group_of(row["content_type"])
            boxes = read_labels(label_path)
            if row["tall"] == "True":
                with Image.open(image_path) as im:
                    # A strip makes about ten tiles; all of them would outnumber the Western pages several times over, and webtoons
                    # aren't the target (they stay on the strip detector). Keep a few per strip, tiles with panels first.
                    all_tiles = list(enumerate(tile_webtoons.tile_page(im.convert("RGB"), boxes)))
                    pick = random.Random(f"{a.seed}-{stem}")
                    with_panels = [t for t in all_tiles if t[1][1]]
                    without = [t for t in all_tiles if not t[1][1]]
                    pick.shuffle(with_panels)
                    pick.shuffle(without)
                    for n, (tile, tile_boxes) in sorted((with_panels + without)[: a.tiles_per_strip], key=lambda t: t[0]):
                        buf = io.BytesIO()
                        tile.save(buf, "JPEG", quality=92)
                        z.writestr(f"images/{sub}/{stem}_t{n:02d}.jpg", buf.getvalue())
                        z.writestr(f"labels/{sub}/{stem}_t{n:02d}.txt", "".join(
                            f"0 {cx:.6f} {cy:.6f} {w:.6f} {h:.6f}\n" for cx, cy, w, h in tile_boxes))
                        counts[(sub, group + " tiles")] += 1
            else:
                z.write(image_path, f"images/{sub}/{os.path.basename(image_path)}")
                z.write(label_path, f"labels/{sub}/{stem}.txt")
                counts[(sub, group)] += 1
        z.write(os.path.join(os.path.dirname(os.path.abspath(__file__)), "manga109_to_yolo.py"), "manga109_to_yolo.py")
        summary = "".join(f"{sub} {kind}: {n}\n" for (sub, kind), n in sorted(counts.items()))
        z.writestr("SUMMARY.txt", f"splits: {' '.join(a.splits)}\n" + summary)

    print(summary, end="")
    print(f"-> {out} ({os.path.getsize(out) / 1e6:.0f} MB). Upload it to Kaggle as a PRIVATE dataset named paperbunkr-panels.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
