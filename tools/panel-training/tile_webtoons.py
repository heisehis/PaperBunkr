"""Cuts a tall webtoon strip and its panel boxes into overlapping tiles for training
(docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md, Webtoon tiler).

Tiles match the harness's StripTiler: each is 1.5x the strip's width tall, consecutive tiles overlap by 25%, and the last ends at the
bottom of the strip. A box is clipped to each tile it reaches; a clipped piece is dropped when it's under 3% of the tile in either
direction. make_kaggle_zip.py uses tile_page(); run this file with --selfcheck to test it.

    py -I tools/panel-training/tile_webtoons.py --selfcheck
"""

from __future__ import annotations

import sys

TILE_ASPECT = 1.5
OVERLAP = 0.25
MIN_SIDE = 0.03
TALL_ASPECT = 2.4  # PanelDetectionService.TallAspect


def tiles(width: int, height: int) -> list[tuple[int, int]]:
    """(top, height) in pixels, like StripTiler.Tiles."""
    tile_h = max(1, round(width * TILE_ASPECT))
    if tile_h >= height:
        return [(0, height)]
    step = max(1, round(tile_h * (1 - OVERLAP)))
    out = []
    top = 0
    while True:
        if top + tile_h >= height:
            out.append((height - tile_h, tile_h))
            return out
        out.append((top, tile_h))
        top += step


def clip_boxes(boxes, width: int, height: int, top: int, tile_h: int):
    """Boxes (YOLO cx, cy, w, h normalised to the strip) -> the same boxes normalised to one tile, clipped, slivers dropped."""
    out = []
    for cx, cy, bw, bh in boxes:
        x1, x2 = (cx - bw / 2) * width, (cx + bw / 2) * width
        y1, y2 = (cy - bh / 2) * height - top, (cy + bh / 2) * height - top
        y1, y2 = max(0.0, y1), min(float(tile_h), y2)
        x1, x2 = max(0.0, x1), min(float(width), x2)
        if y2 <= y1 or x2 <= x1:
            continue
        if (x2 - x1) / width < MIN_SIDE or (y2 - y1) / tile_h < MIN_SIDE:
            continue
        out.append(((x1 + x2) / 2 / width, (y1 + y2) / 2 / tile_h, (x2 - x1) / width, (y2 - y1) / tile_h))
    return out


def tile_page(image, boxes):
    """A PIL image and its boxes -> [(tile image, tile boxes)]. Pages that aren't tall come back whole."""
    width, height = image.size
    if height <= TALL_ASPECT * width:
        return [(image, list(boxes))]
    return [(image.crop((0, top, width, top + tile_h)), clip_boxes(boxes, width, height, top, tile_h))
            for top, tile_h in tiles(width, height)]


def selfcheck() -> int:
    failures = 0

    def check(name, ok):
        nonlocal failures
        if not ok:
            failures += 1
            print("FAIL:", name)

    t = tiles(800, 6000)
    check("first tile at the top", t[0][0] == 0)
    check("last tile ends at the bottom", t[-1][0] + t[-1][1] == 6000)
    check("tiles overlap", all(b[0] < a[0] + a[1] for a, b in zip(t, t[1:])))
    check("short page is one tile", tiles(800, 1000) == [(0, 1000)])

    # A panel from y=1000 to 1600 on an 800x6000 strip; tiles are 1200 tall with steps of 900.
    panel = (0.5, 1300 / 6000, 0.8, 600 / 6000)
    first = clip_boxes([panel], 800, 6000, 0, 1200)
    check("panel clipped at the first tile's bottom", len(first) == 1 and abs(first[0][1] * 1200 + first[0][3] * 1200 / 2 - 1200) < 1e-6)
    second = clip_boxes([panel], 800, 6000, 900, 1200)
    check("panel whole in the second tile", len(second) == 1 and abs(second[0][3] * 1200 - 600) < 1e-6)
    check("panel outside a tile is absent", clip_boxes([panel], 800, 6000, 2700, 1200) == [])
    sliver = clip_boxes([(0.5, 1210 / 6000, 0.8, 20 / 6000)], 800, 6000, 0, 1200)
    check("a 10 px sliver at the seam is dropped", sliver == [])

    try:
        from PIL import Image
        img = Image.new("RGB", (800, 6000), "white")
        out = tile_page(img, [panel])
        check("tile_page cuts a strip", len(out) == len(t) and out[0][0].size == (800, 1200))
        check("tile_page keeps a regular page whole", len(tile_page(Image.new("RGB", (800, 1200)), [panel])) == 1)
    except ImportError:
        print("Pillow missing: image checks skipped")

    print(f"tile_webtoons selfcheck: {'ok' if failures == 0 else f'{failures} failed'}")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    sys.exit(selfcheck() if "--selfcheck" in sys.argv else (print(__doc__) or 2))
