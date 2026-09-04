# Mark asset sources

Sourced per `docs/superpowers/specs/2026-08-28-brand-logo-sourcing-checklist.md` on 2026-08-28.
All files are clean vector SVG (no auto-traced rasters). Logos remain trademarks of their
respective owners; the license column is the copyright status of the SVG file itself.

## Services/

| File | Source | File license |
|---|---|---|
| `anilist.svg` | Simple Icons (`anilist`) | CC0-1.0 |
| `myanimelist.svg` | Simple Icons (`myanimelist`) | CC0-1.0 |
| `kitsu.svg` | Simple Icons (`kitsu`) | CC0-1.0 |
| `bangumi.svg` | Wikimedia Commons — `File:Logo riff.svg` (the bgm.tv `logo_riff` mark) | Public domain |
| `mangadex.svg` | mangadex.org media kit — `img/brand/mangadex-logo.svg` (brandmark, #ff6740) | brand asset |
| `mangaupdates.svg` | Simple Icons (`mangaupdates`) | CC0-1.0 |
| `metron.svg` | github.com/Metron-Project/metron — `static/site/img/metron.svg` | project asset (GPL-3.0 repo) |
| `comicvine.svg` | comicvine.gamespot.com — inline masthead SVG (`svg.symbol-logo-comicvine`), full-colour wordmark | brand asset |
| `animeplanet.svg` | anime-planet.com — the `#mobile` group of their inline `#logo` SVG (the `a‿p` smiley mark), recoloured to their `#FC5342` | brand asset |
| `shikimori.svg` | github.com/shikimori/shikimori — `app/assets/images/src/glyph_logo.svg` (赤SHIKIMORI wordmark) | project asset (repo is source-available) |

Not found as usable SVG (fall back to letter-mark): `mangabaka`, `gcd`, `locg`,
`cbro`, `readingorders`, `readthingsright`.
- MangaBaka: mangabaka.dev is behind a Cloudflare bot wall; the only public assets (app repo
  `Oazzies/MangaBaka-App`, `myanili`, etc.) are all PNG. No SVG anywhere.
- League of Comic Geeks: header logo is `logo-white.png` (raster).
- Grand Comics Database (comics.org): behind a Cloudflare bot wall; historically PNG-only anyway.
- CBRO / ReadingOrders.net / ReadThingsRight: small WordPress sites, no SVG branding — the
  checklist already marks these letter-mark-OK.
- Metron note: metron.cloud itself is behind the Anubis anti-bot wall; asset taken from the
  project's own GitHub repo instead.

## Publishers/

| File | Source | File license |
|---|---|---|
| `marvel.svg` | Wikimedia Commons — `File:Marvel Logo.svg` | Public domain (text/simple shapes) |
| `dc.svg` | Wikimedia Commons — `File:DC Comics logo.svg` (2005 "spin" bullet) | Public domain |
| `image.svg` | Wikimedia Commons — `File:Image Comics logo.svg` | Public domain |
| `dark-horse.svg` | Wikimedia Commons — `File:Dark Horse Comics wordmark.svg` | Public domain |
| `idw.svg` | Wikimedia Commons — `File:IDW Publishing logo.svg` | Public domain |
| `viz.svg` | Wikimedia Commons — `File:Viz Media 2017 logo.svg` | Public domain |
| `valiant.svg` | Wikimedia Commons — `File:Valiant-logo.svg` | **CC BY-SA 4.0** (needs attribution) |
| `titan.svg` | titan-comics.com — `static/img/tc_txt_logo.svg` | brand asset |
| `yen-press.svg` | yenpress.com — `images/header/logo.svg` | brand asset |
| `shueisha.svg` | Wikimedia Commons — `File:Shueisha Logo.svg` | Public domain |
| `kodansha.svg` | Wikimedia Commons — `File:Kōdansha logo.svg` | Public domain |
| `shogakukan.svg` | Wikimedia Commons — `File:Shogakukan logo.svg` | Public domain |
| `kadokawa.svg` | Wikimedia Commons — `File:Kadokawa logo.svg` | Public domain |
| `square-enix.svg` | Simple Icons (`squareenix`) | CC0-1.0 |

### Auto-traced from raster (no clean vector exists anywhere)

`boom.svg`, `dynamite.svg`, `oni.svg`, `seven-seas.svg` are **potrace auto-traces** of the only
raster the publisher ships. The checklist marks these letter-mark-OK, and the design doc says to
reject auto-traces — these are here only because the user explicitly asked for traced fallbacks.
Single black path each, meant to be tinted by `MarkResolver`. Swap for a real vector if one ever
surfaces. Trace recipe: `docs/superpowers/specs/` companion — script kept in the session scratchpad.

| File | Raster source | Quality |
|---|---|---|
| `dynamite.svg` | bigcommerce CDN `dynamite-logo-white…png` (864×150 white wordmark) | good — sharp "DYNAMITE" wordmark |
| `oni.svg` | onipress.com Squarespace `Standard Coin W300dpi.png` → header logo crop (1500px) | good — "ONI" crisp, "PRESS" soft |
| `boom.svg` | boom-studios.com `boom-logo-header.webp` (only 105×51) | fair — "BOOM! STUDIOS" legible but soft |
| `seven-seas.svg` | sevenseasentertainment.com `SevenSeas-logo-sm.png` (only 48×74), framed-ship box cropped, script text dropped | rough — recognisable tall-ship-in-frame, blobby |

### Still letter-mark only

- `dstlry` — dstlry.co blocks automated access; nothing on Commons.

## AgeRatings/

The design doc (§4) renders age ratings as a glyph + text chip; the user chose to also bundle the
ESRB rating icons CE historically shipped (`_reference/.../Icons/AgeRatings/*.png`). These 8 are
the current **ESRB "2013" flat vector** from Wikimedia Commons (all Public domain — ESRB rating
symbols are `{{PD-textlogo}}` there), monochrome, tinted by `BrandMark`. `age-rating-aliases.tsv`
maps CE + ComicInfo v2.1 vocabulary onto them; values with no ESRB equivalent (G, PG, M, MA15+,
R18+, X18+) stay coloured text chips.

| File | Commons source | Note |
|---|---|---|
| `everyone.svg` | `File:ESRB 2013 Everyone.svg` | E |
| `everyone-10.svg` | `File:ESRB 2013 Everyone 10+.svg` | E10+ |
| `teen.svg` | `File:ESRB 2013 Teen.svg` | T |
| `mature-17.svg` | `File:ESRB 2013 Mature.svg` | M (17+) |
| `adults-only-18.svg` | `File:ESRB 2013 Adults Only 18+.svg` | AO |
| `early-childhood.svg` | `File:ESRB 2013 Early Childhood.svg` | EC |
| `rating-pending.svg` | `File:ESRB 2013 Rating Pending.svg` | RP |
| `kids-to-adults.svg` | `File:ESRB 1998 Kids to Adults.svg` | KA — retired 1998, older style is all that exists |

Chip colours in the tsv (green family / amber teen / red adult) are a first pass — reviewable.

## Formats/

CE's `Formats/*.png` (39 icons) are **not icons** — they're stylised yellow-gradient letter
abbreviations (`ANN`, `TPB`, `GN`, `B/W`…). The design doc originally rendered every format as a
FluentIcons glyph + letter chip (`format-aliases.tsv`), which is the same information; only the
**two** CE format icons that are real pictograms (`crossover`, `nsfw`) were redrawn by hand from
the CE raster.

**Deviation (2026-09-04, at the user's request):** every *comic*-format row now also ships a
hand-drawn pictogram — 38 files total, `viewBox="0 0 64 64"`, transparent ground. These are
original geometric glyphs (not traced from CE, which had no pictograms for them). The `asset`
column of `format-aliases.tsv` names the stem for each; the FluentIcons `symbol` / label columns
stay as the fallback when an asset file is missing.

**Colour (2026-09-04):** the marks carry their own colour and `MarkResolver.ResolveFormat` no
longer tints them — they render as-is, like the age-rating boxes. Each is a flat mid-tone fill
from a category-hued palette (chosen to stay legible on both the light and dark app themes, so
they do *not* react to the runtime skin — same trade-off already accepted for publisher logos and
ESRB marks):

| Hue | Hex | Category | Stems |
|---|---|---|---|
| amber | `#C77F1A` | collected / bound editions | `anthology` `box-set` `graphic-novel` `hardcover` `omnibus` `trade-paperback` `series` `magazine` |
| blue | `#3B7BC4` | issue numbering | `half` `point-one` `minus-one` `annual` `main` `one-shot` |
| violet | `#8257E0` | story position | `prologue` `epilogue` `year-one` `year-zero` `limited-series` |
| pink | `#D14E86` | events | `special` (and `crossover`, two-tone pink/violet) |
| gold | `#D9A521` | premium / energy | `king` `giant` `event` |
| teal | `#1F9E9E` | editorial | `annotation` `reference` `script` `sketch` `reviewed` `directors-cut` |
| green | `#4C9E4E` | promo / preview | `preview` `flyer` `fcbd` |
| indigo | `#5866D6` | fan / web / distribution | `web-comic` `fan-made` `scanlation` |
| grey | `#9A9A9A` | (literally monochrome) | `black-and-white` |

`crossover` is CE-derived art, recoloured two-tone. `nsfw` is unchanged — CE-derived, stays the
warning-red (`#E1251B`) prohibition sign.

**v2 redraws (2026-09-04):** the user supplied two AI-generated ("v0") icon sets for comparison.
Neither was adopted wholesale — set 1 was one template SVG copy-pasted 38 times with a tiny
distinguishing `<text>` label (illegible at mark size, verified by rendering through
`Svg.Skia`); set 2 was a genuinely well-drawn monochrome stroke-icon family, but had no colour
and ~15 of its 38 shared a "page + fine strokes" skeleton that also collapsed to an
indistinguishable grey box once downscaled to the ~16-20px this control actually renders at.
Four of *this* app's own icons were weak for the same reason and got redrawn, borrowing shape
ideas where a v0 attempt suggested a better direction:
- **`trade-paperback`** — v1's wavy bottom edge was invisible small; now a spine + a large curled
  cover (bold enough to survive downscaling).
- **`year-one`** — v1 was a pennant with a numeral cut into it (invisible <24px, and looked
  identical to `year-zero` at mark size); now a solid up/launch arrow.
- **`year-zero`** — same pennant problem; now a fat solid ring (reads as "0" at any size) - a
  different silhouette from `year-one`, not just a swapped digit.
- **`king`** — was a 3-peak zigzag; borrowed a v0 redraw's 5-peak proportions (filled solid
  instead of stroked) for a clearer crown silhouette.

All four verified through the same `Svg.Skia` render path as every other mark, at true ~20px mark
size, on both light and dark backgrounds.

The **six book / container formats** — `EPUB`, `PDF`, `FB2`, `MOBI`, `CBZ`, `CBR` — deliberately
have **no** pictogram (blank `asset`) and still render as glyph + chip.

Full list of pictogram stems: `annotation`, `annual`, `anthology`, `black-and-white`, `box-set`,
`crossover`, `directors-cut`, `epilogue`, `event`, `fan-made`, `fcbd`, `flyer`, `giant`,
`graphic-novel`, `half`, `hardcover`, `king`, `limited-series`, `magazine`, `main`, `minus-one`,
`nsfw`, `omnibus`, `one-shot`, `point-one`, `preview`, `prologue`, `reference`, `reviewed`,
`scanlation`, `script`, `series`, `sketch`, `special`, `trade-paperback`, `web-comic`,
`year-one`, `year-zero`.

### `valiant.svg` attribution

CC BY-SA 4.0 — if these marks are surfaced with credits, attribute the Valiant SVG to its
Commons uploader (see `https://commons.wikimedia.org/wiki/File:Valiant-logo.svg`). Swap for a
public-domain or brand-kit version if that's a problem.

## Flags/

The real **`flag-icons`** 4x3 SVGs (github.com/lipis/flag-icons, **MIT**), pulled from the jsdelivr
CDN — accurate, not the earlier hand-drawn approximations. One file per ISO 3166-1 alpha-2 region
in `language-regions.tsv`, plus `bd.svg` (Bangladesh) held for a possible Bengali `bn` mapping.

`es.svg` (~81 KB) and `mx.svg` (~85 KB) carry full coats of arms — invisible at 12-16px mark size
but kept for fidelity and any larger rendering; everything else is 0.2-10 KB. All verified clean
vector (no embedded rasters). To refresh: `cdn.jsdelivr.net/gh/lipis/flag-icons@main/flags/4x3/<cc>.svg`.
