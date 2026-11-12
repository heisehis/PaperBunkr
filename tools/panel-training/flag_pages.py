"""Lists a split's pages whose boxes look wrong, most suspicious first, into <split>/recheck.txt: overlapping boxes, 8 or more boxes,
thin slivers, or no boxes at all. A labelling aid only; a flagged page can be right (an inset panel overlaps its parent).

    py -I tools/panel-training/flag_pages.py C:\\Users\\DeeDee\\PaperBunkr-panel-data\\batch1 [--untouched-only]

--untouched-only skips pages whose labels differ from images/labels-prelabel (already corrected).
"""

from __future__ import annotations

import csv
import os
import sys


def read(path: str) -> list[tuple[float, ...]]:
    with open(path, encoding="utf-8") as f:
        return [tuple(float(x) for x in line.split()[1:5]) for line in f if line.strip()]


def rounded(boxes):
    return [tuple(round(x, 3) for x in b) for b in boxes]


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 1:
        print(__doc__)
        return 2
    split = args[0]
    untouched_only = "--untouched-only" in sys.argv
    labels = os.path.join(split, "images", "labels")
    pre = os.path.join(split, "images", "labels-prelabel")
    with open(os.path.join(split, "..", "manifest.csv"), newline="", encoding="utf-8") as f:
        manifest = {r["id"]: r for r in csv.DictReader(f)}

    out = []
    for name in sorted(os.listdir(labels)):
        if not name.endswith(".txt") or name == "classes.txt":
            continue
        page = name[:-4]
        boxes = read(os.path.join(labels, name))
        if untouched_only and os.path.exists(os.path.join(pre, name)) and rounded(boxes) != rounded(read(os.path.join(pre, name))):
            continue
        rects = [(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2) for cx, cy, w, h in boxes]
        overlaps = 0
        for i in range(len(rects)):
            for j in range(i + 1, len(rects)):
                a, b = rects[i], rects[j]
                iw, ih = min(a[2], b[2]) - max(a[0], b[0]), min(a[3], b[3]) - max(a[1], b[1])
                smaller = min((a[2] - a[0]) * (a[3] - a[1]), (b[2] - b[0]) * (b[3] - b[1]))
                if iw > 0 and ih > 0 and iw * ih > 0.10 * smaller:
                    overlaps += 1
        why = []
        if overlaps:
            why.append(f"{overlaps} overlapping pair(s)")
        if len(boxes) >= 8:
            why.append(f"{len(boxes)} boxes")
        thin = sum(1 for b in boxes if min(b[2], b[3]) < 0.06)
        if thin:
            why.append(f"{thin} thin sliver(s)")
        if not boxes:
            why.append("no boxes (check it really has no panels)")
        if why:
            row = manifest.get(page, {})
            kind = row.get("content_type", "?") + (" strip" if row.get("tall") == "True" else "")
            out.append((len(why), overlaps, page, kind, "; ".join(why)))

    out.sort(key=lambda t: (-t[0], -t[1]))
    target = os.path.join(split, "recheck.txt")
    with open(target, "w", encoding="utf-8") as f:
        f.write("Pages whose boxes look wrong, most suspicious first. Search the file name in X-AnyLabeling.\n\n")
        for _, _, page, kind, why in out:
            f.write(f"{page}  {kind}  {why}\n")
    print(f"{len(out)} pages listed in {target}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
