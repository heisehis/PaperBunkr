"""Generates the tiled textures and backdrop images used by the built-in themes.

docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md (Q15). Run from the repository root:

    py -I tools/themes/generate_textures.py

A theme material replaces the surface brush outright (ThemeMaterialBuilder), so every texture has its surface colour baked in: it is opaque,
and each theme folder holds its own files so a folder exports as a self-contained .crpck. Everything is drawn with fixed seeds and no
third-party packages, so running it twice writes byte-identical files. Keep patterns subtle: text contrast is checked against each
material's fallback colour, which is the texture's base colour.
"""

import math
import os
import random
import struct
import sys
import zlib

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src", "Paperbunkr.App", "Assets", "Skins")


# ----------------------------------------------------------------------------------------------------------------- PNG + colour helpers

def rgb(hex_text):
    h = hex_text.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def write_png(path, width, height, pixel):
    """pixel(x, y) -> (r, g, b). 8-bit RGB, no interlace, fixed compression level: deterministic output."""
    rows = bytearray()
    for y in range(height):
        rows.append(0)
        for x in range(width):
            rows.extend(pixel(x, y))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(rows), 9))
    png += chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(png)


# ----------------------------------------------------------------------------------------------------------------- pattern generators

def dots(base, ink, cell, radius, strength=1.0, offset_rows=True):
    """Ben-Day / halftone dots on a square grid (alternate rows offset by half a cell when offset_rows)."""
    def pixel(x, y):
        row = y // cell
        cx = (x % cell) - cell / 2 + 0.5
        if offset_rows and row % 2:
            cx = ((x + cell // 2) % cell) - cell / 2 + 0.5
        cy = (y % cell) - cell / 2 + 0.5
        coverage = max(0.0, min(1.0, radius + 0.5 - math.hypot(cx, cy)))
        return mix(base, ink, coverage * strength)
    return pixel, cell, cell * 2 if offset_rows else cell


def grain(base, seed, amount, size=128):
    """Fine paper / ink grain: per-pixel noise softened with a 2x2 average, tileable because it is drawn on a wrapped grid."""
    rnd = random.Random(seed)
    field = [[rnd.uniform(-1, 1) for _ in range(size)] for _ in range(size)]

    def pixel(x, y):
        v = (field[y][x] + field[y][(x + 1) % size] + field[(y + 1) % size][x] + field[(y + 1) % size][(x + 1) % size]) / 4
        target = (255, 255, 255) if v > 0 else (0, 0, 0)
        return mix(base, target, abs(v) * amount)
    return pixel, size, size


def fibre(base, seed, size=160):
    """Kraft card: grain plus long, faint horizontal fibres."""
    rnd = random.Random(seed)
    fibres = [(rnd.randrange(size), rnd.randrange(size), rnd.randrange(20, 70), rnd.choice((-1, 1)), rnd.uniform(0.03, 0.08)) for _ in range(70)]
    marks = {}
    for (fx, fy, length, tone, strength) in fibres:
        for i in range(length):
            marks[((fx + i) % size, fy)] = (tone, strength)
    noise, _, _ = grain(base, seed + 1, 0.10, size)

    def pixel(x, y):
        r = noise(x, y)
        if (x, y) in marks:
            tone, strength = marks[(x, y)]
            return mix(r, (255, 240, 210) if tone > 0 else (60, 40, 20), strength * 2)
        return r
    return pixel, size, size


def grid(base, line, minor, major_every, minor_alpha, major_alpha):
    """Drafting grid: 1 px minor lines every `minor` px, a stronger line every `major_every` cells."""
    size = minor * major_every

    def pixel(x, y):
        a = 0.0
        if x % minor == 0 or y % minor == 0:
            a = minor_alpha
        if x % size == 0 or y % size == 0:
            a = major_alpha
        return mix(base, line, a)
    return pixel, size, size


def greenbar(paper, band, height=12):
    """Tractor-feed printer paper: alternating white and green bands."""
    def pixel(x, y):
        return paper if (y // height) % 2 == 0 else band
    return pixel, 8, height * 2


def sprocket(paper, hole, rim, pitch=14, width=16):
    """The sprocket-hole strip down the side of continuous printer paper."""
    def pixel(x, y):
        d = math.hypot(x - width / 2 + 0.5, (y % pitch) - pitch / 2 + 0.5)
        if d < 3.2:
            return hole
        if d < 4.2:
            return mix(paper, rim, 4.2 - d)
        return paper
    return pixel, width, pitch


def backdrop(width, height, top, bottom, blobs=(), ridges=()):
    """A non-tiling image (type "image", stretched): vertical gradient, soft light blobs (caustics, moon) and dune ridges."""
    def pixel(x, y):
        c = mix(top, bottom, y / (height - 1))
        for (bx, by, br, colour, strength) in blobs:
            d = math.hypot(x - bx * width, y - by * height) / (br * width)
            if d < 1:
                c = mix(c, colour, (1 - d) ** 2 * strength)
        for (amp, freq, phase, level, colour) in ridges:
            edge = level * height + amp * height * math.sin(freq * x / width * 2 * math.pi + phase)
            if y >= edge:
                c = mix(c, colour, min(1.0, (y - edge) / 3))
        return c
    return pixel, width, height


def neon_grid(width, height, top, bottom, line_a, line_b, cell=24):
    """Arcade pastel: the lilac-to-mint gradient with a faint two-colour grid."""
    def pixel(x, y):
        c = mix(top, bottom, y / (height - 1))
        if y % cell == 0:
            c = mix(c, line_a, 0.18)
        if x % cell == 0:
            c = mix(c, line_b, 0.16)
        return c
    return pixel, width, height


# ----------------------------------------------------------------------------------------------------------------- the set

def textures():
    w, h = 512, 320
    return {
        # Golden Age: yellowed newsprint with Ben-Day dots; the night cover with dim dots.
        "golden_age/textures/day-canvas.png": dots(rgb("F2E6C8"), rgb("D7261E"), 7, 1.3, 0.30),
        "golden_age/textures/day-nav.png": dots(rgb("EADBB6"), rgb("1D4E9E"), 6, 1.3, 0.32),
        "golden_age/textures/night-canvas.png": dots(rgb("141A30"), rgb("F5C518"), 7, 1.3, 0.16),
        "golden_age/textures/night-nav.png": dots(rgb("111628"), rgb("E2382F"), 6, 1.3, 0.22),
        # Screentone: halftone on the chrome and on the selected row.
        "screentone/textures/light-tone.png": dots(rgb("FFFFFF"), rgb("000000"), 4, 0.9, 1.0, offset_rows=False),
        "screentone/textures/light-selection.png": dots(rgb("FFFFFF"), rgb("000000"), 4, 0.8, 0.45, offset_rows=False),
        "screentone/textures/dark-tone.png": dots(rgb("0A0A0A"), rgb("FFFFFF"), 4, 0.9, 0.6, offset_rows=False),
        "screentone/textures/dark-selection.png": dots(rgb("0A0A0A"), rgb("FFFFFF"), 4, 0.8, 0.35, offset_rows=False),
        # Risograph: grainy stock.
        "risograph/textures/light-grain.png": grain(rgb("F4F1EA"), 11, 0.10),
        "risograph/textures/dark-grain.png": grain(rgb("1A1A1C"), 12, 0.14),
        # Blueprint: drafting grid.
        "blueprint/textures/light-grid.png": grid(rgb("F5F9FD"), rgb("1565C0"), 12, 4, 0.08, 0.16),
        "blueprint/textures/dark-grid.png": grid(rgb("0F3460"), rgb("FFFFFF"), 12, 4, 0.08, 0.16),
        # Dune: layered sand by day, the same ridges under an indigo sky with a low moon by night.
        "dune/textures/day.png": backdrop(w, h, rgb("FBF1E1"), rgb("F3E3C7"),
                                          ridges=((0.05, 1.1, 0.4, 0.72, rgb("EDD5AC")), (0.04, 0.8, 2.1, 0.84, rgb("E7C99A")))),
        "dune/textures/night.png": backdrop(w, h, rgb("1B1A30"), rgb("2A1D20"),
                                            blobs=((0.78, 0.18, 0.04, rgb("FFF0C8"), 0.9),),
                                            ridges=((0.05, 1.1, 0.4, 0.74, rgb("33241C")), (0.04, 0.8, 2.1, 0.86, rgb("2A1E1A")))),
        # Kraft: brown card stock.
        "kraft/textures/canvas.png": fibre(rgb("CFAE7E"), 21),
        "kraft/textures/nav.png": fibre(rgb("C4A170"), 22),
        # Crimson Ink: ink grain on the dark stock, paper grain on its light half.
        "crimson_ink/textures/ink-grain.png": grain(rgb("121214"), 31, 0.08),
        "crimson_ink/textures/paper-grain.png": grain(rgb("FAF8F5"), 32, 0.06),
        # Matrix · Printout: green-bar paper and the sprocket strip.
        "matrix/textures/greenbar.png": greenbar(rgb("FFFFFF"), rgb("DDEFDB")),
        "matrix/textures/sprocket.png": sprocket(rgb("F7FAF6"), rgb("D5DED2"), rgb("9DBB98")),
        # Newsprint · Night Edition: charcoal newsprint grain.
        "newsprint/textures/night-grain.png": grain(rgb("141412"), 41, 0.07),
        # Abyss · Shallows: tide-pool water with soft caustics.
        "abyss/textures/shallows.png": backdrop(w, h, rgb("E6F6FB"), rgb("CBE9F3"),
                                                blobs=((0.3, 0.2, 0.35, rgb("FFFFFF"), 0.6), (0.8, 0.7, 0.4, rgb("A8DCEB"), 0.35))),
        # Arcade · Pastel: lilac to mint with a faint neon grid.
        "arcade/textures/pastel.png": neon_grid(w, h, rgb("F7EEFF"), rgb("E9FBF6"), rgb("E0359E"), rgb("00A8C6")),
    }


def main():
    only = set(sys.argv[1:])
    for rel, (pixel, width, height) in textures().items():
        if only and rel not in only:
            continue
        write_png(os.path.join(ROOT, rel.replace("/", os.sep)), width, height, pixel)
        print("wrote", rel, f"{width}x{height}")


if __name__ == "__main__":
    main()
