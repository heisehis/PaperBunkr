"""Turns a split's pre-drawn YOLO labels into X-AnyLabeling's own per-image JSON, so the boxes show up as soon as the folder is
opened (no Upload dialog). Never overwrites an existing .json: those hold your corrections.

    py -I tools/panel-training/yolo_to_xany.py C:\\Users\\DeeDee\\PaperBunkr-panel-data\\test

Reads <split>/images/labels/<id>.txt, writes <split>/images/<id>.json. Image sizes come from the manifest.
"""

from __future__ import annotations

import csv
import json
import os
import sys

IMAGE_EXT = (".jpg", ".jpeg", ".png", ".webp")


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    split = sys.argv[1]
    images = os.path.join(split, "images")
    labels = os.path.join(images, "labels")
    with open(os.path.join(split, "..", "manifest.csv"), newline="", encoding="utf-8") as f:
        sizes = {r["id"]: (int(r["width"]), int(r["height"])) for r in csv.DictReader(f)}

    written = kept = missing = 0
    for name in sorted(os.listdir(images)):
        stem, ext = os.path.splitext(name)
        if ext.lower() not in IMAGE_EXT:
            continue
        target = os.path.join(images, stem + ".json")
        if os.path.exists(target):
            kept += 1
            continue
        label = os.path.join(labels, stem + ".txt")
        if not os.path.exists(label) or stem not in sizes:
            missing += 1
            continue
        w, h = sizes[stem]
        shapes = []
        with open(label, encoding="utf-8") as f:
            for line in f:
                parts = line.split()
                if len(parts) < 5 or parts[0] != "0":
                    continue
                cx, cy, bw, bh = (float(p) for p in parts[1:5])
                x1, y1 = (cx - bw / 2) * w, (cy - bh / 2) * h
                x2, y2 = (cx + bw / 2) * w, (cy + bh / 2) * h
                shapes.append({
                    "label": "panel",
                    "score": None,
                    "points": [[x1, y1], [x2, y1], [x2, y2], [x1, y2]],
                    "group_id": None,
                    "description": "",
                    "difficult": False,
                    "shape_type": "rectangle",
                    "flags": {},
                    "attributes": {},
                    "kie_linking": [],
                })
        doc = {"version": "4.3.1", "flags": {}, "shapes": shapes, "imagePath": name, "imageData": None,
               "imageHeight": h, "imageWidth": w}
        with open(target, "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2)
        written += 1

    print(f"{written} .json written, {kept} existing kept, {missing} without labels or manifest row")
    return 0


if __name__ == "__main__":
    sys.exit(main())
