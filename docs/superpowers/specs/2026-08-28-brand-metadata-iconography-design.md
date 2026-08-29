# Brand & Metadata Iconography

**Date:** 2026-08-28
**Status:** Design approved, plan pending.
**Related:** UI rework (the "brand iconography" item flagged in
[[project_paperbunkr_ui_rework]], to build after Library 4a), and the just-completed
[FluentIcons migration](2026-08-28-fluenticons-migration-design.md) — this reuses its
`<fi:SymbolIcon>` for glyph marks.

## Background

Five kinds of value currently render as plain text (or not at all) and would read better as marks:

| Family | Source | Renders today |
|---|---|---|
| **A — External services** | `ExternalMetadataProvider` enum (10), `Data/Tracking/Adapters/*` (5), `Data/ReadingLists/Sources/*` (6) — ~16 distinct | Preferences → Connections, arc-source picker, tracker link badges (text) |
| **B — Publishers** | `Issue.Publisher` — free text | Detail band, DetailTabs credits, Issue-list column, Library grid overlay (9px), Library list column, Library caption rows (text) |
| **C — Format** | `Issue.Format` — free text | Issue Properties editor only |
| **C — Age rating** | `Issue.AgeRating` — free text | Issue Properties editor only |
| **C — Special** | derived — manga content type, `ColorMode` B&W, RTL reading direction | nowhere |
| **D — Language** | `Issue.LanguageISO` | Library grid overlay (9px, text) |

ComicRack CE already ships the C/B vocabularies as bundled icon packs under
`_reference/ComicRackCE/ComicRack/Output/Resources/Icons/`:
`Formats/` (~40, `#`-alias filenames + `map.ini`), `AgeRatings/` (8 + a 6-icon Australia set),
`Publishers/` (**751 files, 24 MB**), `Special/` (Manga, MangaRightToLeft, BlackAndWhite,
SeriesComplete). CE has **no** language/flag icons. Per the standing CE-parity rule the C/B
*vocabularies and alias maps* are taken from CE; the *rendering* is Paperbunkr's own (we just
deleted 39 raster PNGs for vector FluentIcons — bundling CE's 24 MB of light-theme rasters would
reverse that).

## Decisions

- **One spec, one branch, all five families**, behind one shared resolver + control.
- **Rendering is like-for-like** — a mark replaces text wherever the value renders today. The one
  new surface: Format / AgeRating / Special marks appended to the **Detail band inline meta row**,
  each shown only when its value is set. No new Library toggles, no attributes strip.

## 1. Shared architecture

### `MarkResolver` (`Paperbunkr.App.Services`)

One service, pure (no DB, no I/O beyond reading embedded resources once). Methods:

```
MarkSpec ResolveService(string serviceId)      // "AniList", "ComicVine", "MangaBaka", ...
MarkSpec ResolvePublisher(string? publisher)
MarkSpec ResolveFormat(string? format)
MarkSpec ResolveAgeRating(string? ageRating)
MarkSpec ResolveLanguage(string? iso)
```

```
record MarkSpec(
    MarkKind Kind,          // SvgAsset | Glyph | LetterMark | Flag | Raster | Text | None
    string? AssetKey,       // avares path for SvgAsset/Flag/Raster
    Symbol? Glyph,          // FluentIcons.Common.Symbol for Glyph
    string? Text,           // letter-mark text, or the passthrough text
    string? Background,     // hex, for LetterMark chips
    string? Foreground);
```

`Kind == Text` (or `None`) is the signal to a consumer that there's no mark — fall back to the
plain `TextBlock` it renders today.

### Alias maps

Embedded resources under `src/Paperbunkr.App/Assets/Marks/`:

- `publisher-aliases.tsv` — `canonical<TAB>alias1|alias2|...`, hand-built (~25 rows for the
  letter-mark tier + ~6 for the colour tier).
- `format-aliases.tsv` — generated once from CE's `Formats/*.png` `#`-split filenames + `map.ini`,
  then committed as data (we don't bundle CE's PNGs for formats either — see §4).
- `age-rating-aliases.tsv` — from CE's `AgeRatings/` filenames.
- `language-regions.tsv` — curated `iso<TAB>regioncode` (~20 rows).

All matching is case-insensitive and trims/normalises whitespace.

### `BrandMark` control (`Paperbunkr.App.Controls`)

Code-only `TemplatedControl` (like `SplitText` — no new `x:Class` axaml, template in
`Styles/Marks.axaml`). Properties: `Kind` (enum: Service/Publisher/Format/AgeRating/Language),
`Value` (string), `ShowText` (bool — whether to render the name beside the mark), `Size` (double,
default 16). Resolves via a static `MarkResolver` instance, renders the right visual, and when the
resolved `Kind` is `Text`/`None` it renders just the passthrough `TextBlock` so a call site can
always replace a bare `<TextBlock Text="{Binding X}"/>` with `<ctl:BrandMark Kind="…" Value="{Binding X}" ShowText="True"/>`
with no layout regression.

## 2. Service marks (A)

- `Assets/Marks/Services/{anilist,mangadex,kitsu,comicvine,myanimelist,mangabaka}.svg` — **user
  supplies** the SVGs.
- Every other service id → `LetterMark`: `MU` MangaUpdates, `SK` Shikimori, `BG` Bangumi,
  `AP` Anime-Planet, `GCD` Grand Comics Database, `LOCG` League of Comic Geeks, `MT` Metron,
  `CBRO` Comic Book Reading Orders, `RON` ReadingOrders.net, `RTR` ReadThingsRight — full table
  finalised in the plan.
- Surfaces: `Preferences/ConnectionsSection.axaml` rows, the arc-source picker in
  `NewReadingListOverlay.axaml`, tracker/link badges wherever `DetailTabsViewModel` /
  `MangaDetailScreenViewModel` show a provider name.

## 3. Publisher marks (B)

- `ResolvePublisher` tiers:
  1. **Colour marks** — Marvel / DC / Image / Dark Horse / IDW / VIZ. SVG asset if the user
     supplies one (`Assets/Marks/Publishers/*.svg`), else a coloured `LetterMark`
     (Marvel `#EC1D24`, DC `#0476F2`, Image `#4D4D4D`, …).
  2. **Letter-marks** — ~25 more via `publisher-aliases.tsv`, monochrome chip (`PbSurface3`
     background, `PbTextMuted` text).
  3. **Unknown** — `MarkSpec(Text, …)` → the call site keeps its current `TextBlock`.
- Surfaces (all like-for-like): `DetailBand.axaml:109`, `DetailTabs.axaml:363`,
  `IssueListScreen.axaml:22`, `LibraryScreen.axaml` 235 / 621 (9px overlay — `ShowText="False"`,
  mark only), 794 (list column), 951 / 1036 (caption rows). Editor `TextBox`es untouched.

## 4. Format / Age rating / Special (C)

- **No CE rasters bundled.** Format & AgeRating are rendered as a `Glyph` (a FluentIcons `Symbol`)
  + a `LetterMark`-style label, driven by `format-aliases.tsv` / `age-rating-aliases.tsv` (which
  carry CE's full alias vocabulary). e.g. `Annual → {Glyph: CalendarLtr, Text: "ANNUAL"}`,
  `Trade Paperback → {Glyph: Book, Text: "TPB"}`, `Mature 17+ → {Text: "17+", Background: amber}`,
  `Adults Only 18+ → {Text: "18+", Background: red}`. The per-value glyph/colour choices are a
  data table produced in the plan, reviewed once — not free-form.
- Special (manga / B&W / RTL / complete) — `ResolveSpecial` exists and returns specs, but the only
  wired surface is the Detail band (below); a manga series already has its own screen.
- **Surfaces:**
  - Issue Properties editor — a resolved `BrandMark` shown next to the Format and AgeRating fields
    (read-only preview of what the typed value resolves to).
  - **Detail band inline meta row** (`DetailBand.axaml` ~line 104) — `DetailBandViewModel` gains
    `FormatText`, `AgeRatingText`, and `SpecialMarks` (a small list), populated from the focused
    issue. Each renders as a `BrandMark` appended after `YearText`, `IsVisible` gated on the value
    being present. Nothing shows when nothing is set.

## 5. Language flags (D)

- `Assets/Marks/Flags/*.svg` — ~20, from `flag-icons` (MIT). `language-regions.tsv`:
  `ja→jp, en→us, ko→kr, zh→cn, zh-hant→tw, zh-hans→cn, es→es, fr→fr, de→de, it→it, pt→br, pt-pt→pt,
  ru→ru, nl→nl, pl→pl, id→id, th→th, vi→vn, tr→tr, ar→sa` (final list in the plan).
- Unknown / region-less iso → `MarkSpec(Text, "JA")` — the existing uppercase chip, restyled
  consistently.
- Surfaces: `LibraryScreen.axaml` 243 / 629 (9px overlay).

## 6. SVG rendering — open technical item

`Avalonia.Svg.Skia`'s newest is `11.3.0` (targets Avalonia 11.x); the app is on Avalonia 12.1.
**Plan-time spike:** does `Avalonia.Svg.Skia` 11.3.0 load and render under Avalonia 12.1?
- **If yes:** add the package; `MarkKind.SvgAsset`/`Flag` render via its `SvgImage` / `Svg` control.
- **If no:** rasterise the supplied brand + flag SVGs to PNG at build time (a `dotnet`/`resvg`
  script, mirroring how CE ships its own icons), and `MarkKind` becomes `Raster`. The
  `MarkResolver` / `BrandMark` API is identical either way — only the asset extension and the
  leaf renderer change.

Letter-marks and glyph marks need no SVG support (chips + FluentIcons), so families A/B/C degrade
gracefully to those if SVG support slips.

## 7. Out of scope

- New Library overlay toggles for Format / AgeRating / Special.
- A Detail "attributes strip" (rejected — the inline meta row carries the C marks instead).
- Bundling CE's 751 publisher rasters / 24 MB.
- Retroactively normalising stored `Publisher` / `Format` strings in the DB — the resolver
  normalises at render time; the data is left as the user/import set it.
- Australia age-rating set — the resolver can carry the aliases, but no UI toggle to select the
  rating system; defaults to the standard set.

## 8. Testing

- `MarkResolverTests` — every alias map resolves its known inputs (incl. case / whitespace
  variants), unknowns return `Kind == Text`, CE `#`-alias parsing is covered, the
  language-region map is total over its declared keys.
- `BrandMarkRenderSmokeTests` — a `BrandMark` of each `Kind` measures to a non-zero size (guards
  the SVG/flag asset pipeline the way `FluentIconRenderSmokeTests` guards the icon font).
- Build clean, full `dotnet test` green, crash-free launch, on-screen sweep of every surface in
  §§2–5.

## 9. Open questions

- The exact colour-mark hex values and the per-format / per-age-rating glyph choices — a data
  table produced and reviewed at plan time, not decided here.
- The SVG-vs-raster call in §6 — resolved by the plan-time spike.
- Which ~6 publisher colour marks the user has SVGs for vs. gets a coloured letter-mark — depends
  what the user supplies.
