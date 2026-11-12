"""Turns X-AnyLabeling's per-image JSON (where your corrections are auto-saved) into the YOLO label files the harness and the
trainer read. Replaces the Export menu, so an edit can't be lost by forgetting to export.

    py -I tools/panel-training/xany_to_yolo.py C:\\Users\\DeeDee\\PaperBunkr-panel-data\\test

Reads <split>/images/<id>.json, writes <split>/images/labels/<id>.txt. The first run copies the pre-drawn labels to
labels-prelabel/ so they're kept. Rectangles labelled anything other than "panel" are reported and left out.
"""

from __future__ import annotations

import json
import os
import shutil
import sys
from collections import Counter


def convert(doc: dict, page: str = "") -> tuple[list[str], list[str], Counter]:
    """One X-AnyLabeling document -> (YOLO lines, problems, labels other than 'panel' that were left out)."""
    lines: list[str] = []
    problems: list[str] = []
    other: Counter = Counter()
    w, h = doc.get("imageWidth"), doc.get("imageHeight")
    if not w or not h:
        return lines, [f"{page}: no image size in the json"], other
    for s in doc.get("shapes", []):
        if s.get("label") != "panel":
            other[s.get("label")] += 1
            continue
        if s.get("shape_type") != "rectangle":
            problems.append(f"{page}: a '{s.get('shape_type')}' shape labelled panel was left out (draw rectangles)")
            continue
        xs = [p[0] for p in s["points"]]
        ys = [p[1] for p in s["points"]]
        x1, x2 = max(0.0, min(xs)), min(float(w), max(xs))
        y1, y2 = max(0.0, min(ys)), min(float(h), max(ys))
        if x2 - x1 < 2 or y2 - y1 < 2:
            problems.append(f"{page}: a tiny box was left out")
            continue
        lines.append(f"0 {(x1 + x2) / 2 / w:.6f} {(y1 + y2) / 2 / h:.6f} {(x2 - x1) / w:.6f} {(y2 - y1) / h:.6f}\n")
    return lines, problems, other


def convert_file(json_path: str) -> list[str]:
    with open(json_path, encoding="utf-8") as f:
        return convert(json.load(f))[0]


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    images = os.path.join(sys.argv[1], "images")
    labels = os.path.join(images, "labels")
    backup = os.path.join(images, "labels-prelabel")
    if os.path.isdir(labels) and not os.path.exists(backup):
        shutil.copytree(labels, backup)
        print(f"pre-drawn labels kept in {backup}")
    os.makedirs(labels, exist_ok=True)
    with open(os.path.join(labels, "classes.txt"), "w", encoding="utf-8") as f:
        f.write("panel\n")

    written = 0
    other: Counter = Counter()
    problems: list[str] = []
    for name in sorted(os.listdir(images)):
        if not name.endswith(".json"):
            continue
        stem = name[:-5]
        with open(os.path.join(images, name), encoding="utf-8") as f:
            lines, page_problems, page_other = convert(json.load(f), stem)
        problems += page_problems
        other += page_other
        if any(p.endswith("no image size in the json") for p in page_problems):
            continue
        with open(os.path.join(labels, stem + ".txt"), "w", encoding="utf-8") as f:
            f.writelines(lines)
        written += 1

    print(f"{written} label files written to {labels}")
    if other:
        print("left out, not labelled 'panel': " + ", ".join(f"{k!r} x{v}" for k, v in other.items()))
    for p in problems:
        print("  " + p)
    return 0


if __name__ == "__main__":
    sys.exit(main())
