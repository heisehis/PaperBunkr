#!/usr/bin/env python3
"""
Generates the reader canvas background textures (docs/superpowers/specs/2026-09-10-reader-
backlog-batch-b-design.md Item 1) - 3 bundled, seamlessly-tiling PNGs consumed by
ReaderBackgroundTextures (id -> avares://Paperbunkr.App/Assets/Textures/<id>.png).

Run once, output committed alongside this script (not generated at build/run time - see the
design doc's "Approach note - texture assets"). Re-run and re-commit if the look needs tuning.

Seamlessness: every noise layer is a sum of sines/cosines with an *integer* number of full
periods across the tile (sin(2*pi*k*x/N) for integer k is exactly equal at x=0 and x=N), so the
composite tiles exactly with no visible seam - no blend-the-edges trick needed.

    python gen_textures.py [--size 256] [--out ../../src/Paperbunkr.App/Assets/Textures]
"""
from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


def periodic_noise(size: int, rng: np.random.Generator, n_waves: int, k_min: int, k_max: int,
                    anisotropy: tuple[float, float] = (1.0, 1.0)) -> np.ndarray:
    """Sum of n_waves random sine gratings with integer wavenumbers in [k_min, k_max] - exactly
    periodic over `size`, so the sum is too. anisotropy scales each wave's x/y wavenumber
    independently (e.g. (1, 0.15) => mostly-horizontal grain, for a brushed/woven look)."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float64)
    field = np.zeros((size, size), dtype=np.float64)
    ax, ay = anisotropy
    for _ in range(n_waves):
        kx = rng.integers(k_min, k_max + 1) * ax
        ky = rng.integers(k_min, k_max + 1) * ay
        phase = rng.uniform(0, 2 * np.pi)
        amp = rng.uniform(0.4, 1.0)
        field += amp * np.sin(2 * np.pi * (kx * x + ky * y) / size + phase)
    return field / max(field.max() - field.min(), 1e-9)  # normalize to roughly [-0.5, 0.5]


def to_rgb(base: tuple[int, int, int], field: np.ndarray, strength: float) -> np.ndarray:
    field = np.clip(field * strength, -1, 1)
    out = np.empty((*field.shape, 3), dtype=np.uint8)
    for c in range(3):
        channel = np.clip(base[c] + field * 255, 0, 255)
        out[:, :, c] = channel.astype(np.uint8)
    return out


def neutral_dark(size: int, rng: np.random.Generator) -> Image.Image:
    """Near-black, barely-there fine grain - reads as "not flat black" under the dark UI."""
    fine = periodic_noise(size, rng, n_waves=14, k_min=18, k_max=40)
    arr = to_rgb((13, 14, 17), fine, strength=0.05)
    return Image.fromarray(arr, "RGB")


def carbon(size: int, rng: np.random.Generator) -> Image.Image:
    """Dark, directional brushed weave - a mostly-horizontal grating plus fine isotropic grain."""
    weave = periodic_noise(size, rng, n_waves=5, k_min=2, k_max=6, anisotropy=(1.0, 0.12))
    fine = periodic_noise(size, rng, n_waves=10, k_min=20, k_max=48)
    arr = to_rgb((32, 36, 43), weave, strength=0.10)
    arr = np.clip(arr.astype(np.int16) + (fine * 255 * 0.06)[..., None].astype(np.int16), 0, 255).astype(np.uint8)
    return Image.fromarray(arr, "RGB")


def linen(size: int, rng: np.random.Generator) -> Image.Image:
    """Soft light woven grey - two perpendicular low-frequency gratings (a criss-cross weave) plus
    light grain."""
    warp = periodic_noise(size, rng, n_waves=3, k_min=3, k_max=5, anisotropy=(1.0, 0.08))
    weft = periodic_noise(size, rng, n_waves=3, k_min=3, k_max=5, anisotropy=(0.08, 1.0))
    fine = periodic_noise(size, rng, n_waves=10, k_min=20, k_max=48)
    weave = 0.6 * warp + 0.6 * weft + 0.3 * fine
    arr = to_rgb((216, 212, 200), weave, strength=0.06)
    return Image.fromarray(arr, "RGB")


TEXTURES = {
    "neutral-dark": neutral_dark,
    "carbon": carbon,
    "linen": linen,
}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--size", type=int, default=256, help="tile width/height in pixels")
    parser.add_argument("--seed", type=int, default=20260910, help="RNG seed, for reproducible output")
    parser.add_argument("--out", type=Path,
                         default=Path(__file__).parent / "../../src/Paperbunkr.App/Assets/Textures",
                         help="output directory")
    args = parser.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    for texture_id, builder in TEXTURES.items():
        rng = np.random.default_rng(args.seed + hash(texture_id) % 1000)
        img = builder(args.size, rng)
        out_path = (args.out / f"{texture_id}.png").resolve()
        img.save(out_path, optimize=True)
        print(f"{texture_id}: {out_path} ({out_path.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
