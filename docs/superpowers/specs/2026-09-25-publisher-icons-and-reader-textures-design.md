# Publisher icon rasters + reader background textures — design

Date: 2026-09-25. Status: ~~approved in chat (grilling round Q1–Q8 + Q3 changed to year-aware).~~
**Built, uncommitted** — confirmed 2026-09-26 via source: `PublisherIconIndex`/`PublisherIconBitmaps`/
`ReaderTextureChoice` present and untracked. On-screen check by the user still outstanding.

## Why / what changed from earlier decisions

The user dropped CE's publisher-icon pack (`Assets/Icons/Publishers/`, 735 PNG/JPG + `map.ini`, ~22 MB)
and CE's background textures (`Assets/Textures/`, 14 Backgrounds + 4 Papers) into the app and asked
for them to be used. Two earlier decisions are **deliberately reversed**:

- `2026-08-28-brand-metadata-iconography-design.md` §non-goals rejected "bundling CE's 751 publisher
  rasters / 24 MB". The files are already embedded (`AvaloniaResource Include="Assets\**"`), so the
  size cost is already paid; the remaining objection (light-theme rasters) is answered by the plate (§A3).
- `2026-09-10-reader-backlog-batch-b-design.md` chose a curated 3-texture set over CE's ~14.

## A. Publisher icons

CE lookup (verified in `_reference/ComicRackCE`): the pack filename is split on `#` and `,` into
keys, matched case-insensitively; a `Name(YYYY-YYYY)` key expands to `Name(YYYY)` per year
(`Program.SplitIconKeysWithYearAndMonth`); an issue looks up `Publisher(YYYY_MM)`, then
`Publisher(YYYY)`, then `Publisher` (`ComicBook.GetIconsInternal`). `map.ini` maps a filename to an extra key.

**A1. Index** — `PublisherIconIndex` (pure; built from filenames + `map.ini` text, so unit tests need no
assets). Runtime-built from `AssetLoader.GetAssets`, so dropping in more files just works. Keys are the
lower-cased alias plus its `MarkResolver.NormalisePublisher` form (so "Aftershock" ↔ "Aftershock Comics");
lookup names are expanded the same way. A key holds an optional undated file and a list of eras stored as
month ranges. **Month-aware, matching CE**: an issue tries its exact `year_month` first, then the bare
year. A range expands to a year key for every year it touches (so `(2016-2024_11)` still answers a
2024 issue with an unknown or out-of-range month), while a lone `(2024_12)` matches that month only —
which is what makes DC's December-2024 logo work (the pack ships exactly that pair).
Deviation from CE (deliberate, harmless): a spaced range `(2019 - 2021)` is accepted where CE's regex ignores it.

**A2. Precedence** — `MarkResolver.ResolvePublisher(publisher, year?)`:
1. an era file whose range contains `year` (this beats the curated SVG: the point of year-awareness is
   showing the DC/Dark Horse logo of that era);
2. the curated SVG (`publisher-aliases.tsv`, unchanged);
3. an undated raster for the name;
4. the newest era's raster (no/other year);
5. the existing coloured letter chip, then plain text.

Candidate names tried in order: raw, normalised, the TSV row's canonical name.

**A3. Rendering** — new `MarkKind.Raster` (`AssetPath` = the avares URI). `BrandMark` gains
`Year` and `Month` (styled `int?`) and draws rasters on a rounded **plate** picked from the bitmap: opaque images
draw plain; transparent images on a light plate, or a dark plate when their visible pixels are light
(measured on a 24×24 downscale at load, memoised with the bitmap). Plate colours are intentionally
theme-independent (they contrast with the logo, not the UI). Rasters are never tinted.
Contact sheets of all 735 on both plates showed the images are clean at 64 px, so **no SVG conversion**
was needed; only contrast varied, which the plate solves.

**A4. Year plumbing** — `IssueListRow.Year`/`Month` (already there) feed issue-level sites. Series-level sites
(`SeriesCardSample`, `DetailTabs`, the detail-hero badge) use the series' earliest release year
(`SeriesMetaFields.Year`), i.e. "the era the series started in". Plugin API `GetComicPublisherIcon` /
`GetComicImprintIcon` pass `issue.EffectiveYear()` and return the raw image bytes for rasters.

**Out of scope:** imprint marks in the UI (none exist today).

## B. Reader background textures

`ReaderBackgroundTextures.All` grows from 3 to **16** (the original 3 first, `neutral-dark` stays the
default/fallback): 13 of CE's 14 `Backgrounds`, ids kebab-cased from the filename (`brick-wall`,
`brushed-metal-2`, …). **`Black [S]` is dropped**: 2×2 tilings of all 14 showed it is one spotlight
vignette (four hot spots when tiled), i.e. meant to be stretched, not tiled. The 4 `Papers` are CE's
separate paper-overlay feature (deferred in the batch-B spec) and stay on disk unused. The Preferences swatch row becomes a wrapping
grid built from the catalog (name tooltip, active ring) instead of 3 hard-coded buttons + `IsTextureX`
properties. Stored ids are unchanged, so existing settings/profiles keep working. Tiling stays
static/unscaled (batch-B deviation from CE). Textures that seam badly when tiled are measured
(edge-difference) and dropped from the catalog rather than shipped looking wrong.

## Tests

`PublisherIconIndexTests` (aliases, `#`/`,` split, era expansion, single-year, spaced range, map.ini,
normalised keys, newest-era fallback), `MarkResolverTests` (raster precedence incl. year), plate
classifier tests, updated `ReaderBackgroundTexturesTests`, `PreferencesScreenViewModelTests`.
