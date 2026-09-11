# Reader background texture generator

Produces the 3 bundled reader-canvas background textures (docs/superpowers/specs/2026-09-10-
reader-backlog-batch-b-design.md Item 1): `neutral-dark`, `carbon`, `linen` — seamlessly-tiling
PNGs consumed by `Services/Reader/ReaderBackgroundTextures` at
`src/Paperbunkr.App/Assets/Textures/<id>.png`.

Run once; the output PNGs are committed alongside this script, not generated at build or run
time (see the design doc's "Approach note"). Re-run and re-commit the PNGs if the look needs
tuning — the script is deterministic (fixed seed) so a re-run without code changes reproduces
byte-identical output.

## Requirements

Python 3 + `numpy` + `Pillow`.

## Usage

```bash
python gen_textures.py
# writes src/Paperbunkr.App/Assets/Textures/{neutral-dark,carbon,linen}.png

python gen_textures.py --size 512 --out /some/other/dir   # tune tile size / output location
```

## How seamlessness works

Every noise layer is a sum of sine gratings with an *integer* number of full periods across the
tile — `sin(2*pi*k*x/size)` for integer `k` is exactly equal at `x=0` and `x=size`, so the sum of
any number of such layers tiles perfectly with no visible seam. No blend-the-edges post-process
needed. `anisotropy` scales a layer's x/y wavenumbers independently to get directional grain
(`carbon`'s vertical brushed weave) vs. a criss-cross weave (`linen`'s warp+weft layers) vs. fine
isotropic grain (`neutral-dark`).
