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
abbreviations (`ANN`, `TPB`, `GN`, `B/W`…). The design doc renders those as a FluentIcons glyph +
letter chip (`format-aliases.tsv`), which is the same information. Only **two** CE format icons
are real pictograms, so only those two are bundled here, redrawn by hand from the CE raster:

| File | CE source | Note |
|---|---|---|
| `crossover.svg` | `Formats/Crossover.png` | two crossing arrows; monochrome, tinted by `BrandMark` |
| `nsfw.svg` | `Formats/NSFW.png` | prohibition sign; colour mark, used as-is (warning red `#E1251B`) |

`format-aliases.tsv` gained an `asset` column — non-blank only on these two rows; every other
format stays glyph + chip.

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
