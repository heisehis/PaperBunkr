"""Turns a seeded sample of Manga109-s pages into panel-only YOLO training data
(docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md, Trainer; runs inside the Kaggle notebook).

Reads the dataset zip directly (Manga109s_released_2026_05_21.zip, about 3.3 GB) and extracts only the sampled pages. Panel boxes are
the <frame> elements of each book's XML in the zip's current `annotations/` folder (older `annotations.vYYYY...` folders are ignored).
Books are split into train and val (whole books, so val pages are never from a book the model trained on).

    python manga109_to_yolo.py ZIP_OR_FOLDER OUT [--pages 1500] [--val-share 0.1] [--seed 1]
    python manga109_to_yolo.py --selfcheck

Writes OUT/images/{train,val}/m109_<book>_<page>.jpg and OUT/labels/{train,val}/....txt. Manga109-s may not be redistributed: OUT stays
inside the training session.
"""

from __future__ import annotations

import argparse
import io
import os
import posixpath
import random
import sys
import xml.etree.ElementTree as ET
import zipfile


class DirSource:
    """An unpacked copy of the dataset, read like the zip (Kaggle unpacks a zip that is uploaded as a dataset)."""

    def __init__(self, root: str):
        self.root = root
        self._names = [os.path.relpath(os.path.join(d, f), root).replace(os.sep, "/")
                       for d, _, files in os.walk(root) for f in files]

    def namelist(self) -> list[str]:
        return self._names

    def read(self, name: str) -> bytes:
        with open(os.path.join(self.root, *name.split("/")), "rb") as f:
            return f.read()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False


def open_source(path: str):
    """The dataset zip, or a folder holding its unpacked contents."""
    return DirSource(path) if os.path.isdir(path) else zipfile.ZipFile(path)


def annotation_files(z) -> dict[str, str]:
    """Book title -> its XML path, from the folder named exactly 'annotations'."""
    out = {}
    for name in z.namelist():
        parts = name.split("/")
        if len(parts) >= 2 and parts[-2] == "annotations" and parts[-1].endswith(".xml"):
            out[parts[-1][:-4]] = name
    return out


def image_root(z) -> str:
    """The zip folder that holds images/<book>/NNN.jpg (it may sit under a top-level folder)."""
    for name in z.namelist():
        parts = name.split("/")
        if "images" in parts and name.lower().endswith(".jpg"):
            return "/".join(parts[: parts.index("images") + 1])
    raise ValueError("no images/ folder in the zip")


def pages_of(xml_bytes: bytes):
    """[(page index, width, height, [(xmin, ymin, xmax, ymax), ...])] for one book."""
    root = ET.fromstring(xml_bytes)
    out = []
    for page in root.iter("page"):
        w, h = int(page.get("width")), int(page.get("height"))
        frames = [tuple(int(f.get(k)) for k in ("xmin", "ymin", "xmax", "ymax")) for f in page.iter("frame")]
        out.append((int(page.get("index")), w, h, frames))
    return out


def yolo_lines(frames, w: int, h: int) -> list[str]:
    lines = []
    for x1, y1, x2, y2 in frames:
        x1, x2 = max(0, min(x1, x2)), min(w, max(x1, x2))
        y1, y2 = max(0, min(y1, y2)), min(h, max(y1, y2))
        if x2 - x1 < 2 or y2 - y1 < 2:
            continue
        lines.append(f"0 {(x1 + x2) / 2 / w:.6f} {(y1 + y2) / 2 / h:.6f} {(x2 - x1) / w:.6f} {(y2 - y1) / h:.6f}\n")
    return lines


def convert(zip_path: str, out: str, pages: int, val_share: float, seed: int) -> dict[str, int]:
    rng = random.Random(seed)
    counts = {"train": 0, "val": 0}
    with open_source(zip_path) as z:
        books = annotation_files(z)
        if not books:
            raise ValueError("no annotations/<book>.xml in the zip")
        images = image_root(z)
        titles = sorted(books)
        rng.shuffle(titles)
        n_val = max(1, round(len(titles) * val_share))
        split_of = {t: ("val" if i < n_val else "train") for i, t in enumerate(titles)}
        candidates = []
        for title in titles:
            for index, w, h, frames in pages_of(z.read(books[title])):
                candidates.append((title, index, w, h, frames))
        rng.shuffle(candidates)
        names = set(z.namelist())
        for title, index, w, h, frames in candidates:
            if counts["train"] + counts["val"] >= pages:
                break
            src = posixpath.join(images, title, f"{index:03d}.jpg")
            if src not in names:
                continue
            split = split_of[title]
            stem = f"m109_{title}_{index:03d}"
            for kind in ("images", "labels"):
                os.makedirs(os.path.join(out, kind, split), exist_ok=True)
            with open(os.path.join(out, "images", split, stem + ".jpg"), "wb") as f:
                f.write(z.read(src))
            with open(os.path.join(out, "labels", split, stem + ".txt"), "w", encoding="utf-8") as f:
                f.writelines(yolo_lines(frames, w, h))
            counts[split] += 1
    return counts


def selfcheck() -> int:
    import tempfile

    failures = 0

    def check(name, ok):
        nonlocal failures
        if not ok:
            failures += 1
            print("FAIL:", name)

    xml = (b'<book title="Test"><pages>'
           b'<page index="0" width="1000" height="800"><frame id="a" xmin="10" ymin="20" xmax="510" ymax="420"/>'
           b'<text id="t" xmin="1" ymin="1" xmax="5" ymax="5"/></page>'
           b'<page index="1" width="1000" height="800"/></pages></book>')
    with tempfile.TemporaryDirectory() as tmp:
        zpath = os.path.join(tmp, "m.zip")
        with zipfile.ZipFile(zpath, "w") as z:
            z.writestr("Manga109s/annotations/Test.xml", xml)
            z.writestr("Manga109s/annotations.v2021.12.30/Test.xml", b"<book/>")
            z.writestr("Manga109s/images/Test/000.jpg", b"jpg0")
            z.writestr("Manga109s/images/Test/001.jpg", b"jpg1")
        with zipfile.ZipFile(zpath) as z:
            check("current annotations folder only", annotation_files(z) == {"Test": "Manga109s/annotations/Test.xml"})
            check("image root found under a top folder", image_root(z) == "Manga109s/images")
        p = pages_of(xml)
        check("frames read, text ignored", p[0][3] == [(10, 20, 510, 420)] and p[1][3] == [])
        line = yolo_lines(p[0][3], 1000, 800)[0].split()
        check("YOLO normalisation", line[0] == "0" and abs(float(line[1]) - 0.26) < 1e-6 and abs(float(line[4]) - 0.5) < 1e-6)
        counts = convert(zpath, os.path.join(tmp, "out"), pages=10, val_share=0.1, seed=1)
        check("both pages written (one book -> val)", counts == {"train": 0, "val": 2})
        empty = os.path.join(tmp, "out", "labels", "val", "m109_Test_001.txt")
        check("a page without frames gets an empty label file", os.path.exists(empty) and os.path.getsize(empty) == 0)
        unpacked = os.path.join(tmp, "unpacked")
        with zipfile.ZipFile(zpath) as z:
            z.extractall(unpacked)
        check("an unpacked folder works like the zip", convert(unpacked, os.path.join(tmp, "out2"), pages=10, val_share=0.1, seed=1) == counts)
    print(f"manga109_to_yolo selfcheck: {'ok' if failures == 0 else f'{failures} failed'}")
    return 0 if failures == 0 else 1


def main() -> int:
    if "--selfcheck" in sys.argv:
        return selfcheck()
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("zip")
    p.add_argument("out")
    p.add_argument("--pages", type=int, default=1500)
    p.add_argument("--val-share", type=float, default=0.1)
    p.add_argument("--seed", type=int, default=1)
    a = p.parse_args()
    counts = convert(a.zip, a.out, a.pages, a.val_share, a.seed)
    print(f"Manga109-s: {counts['train']} train and {counts['val']} val pages -> {a.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
