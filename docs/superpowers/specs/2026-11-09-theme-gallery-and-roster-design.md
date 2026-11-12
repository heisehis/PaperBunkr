# Theme gallery layout and theme roster — Design

Grilled 2026-11-09 in three rounds (visual companion: layout, roster tiles, tab mockups under `.superpowers/brainstorm/1479-1794233748/`). Every
recommended answer was accepted. Status: **design for review - nothing built.**

## Summary

Two changes to Preferences > Appearance:

1. **Layout.** The Theme tab becomes two panes that scroll on their own: a filtered gallery of bigger tiles on the left, the active theme's
   inspector and options on a fixed right rail. Personalize and Customize move to a new **Personalize** tab with the same two-pane shape,
   its live preview pinned on the right. Today's page is a fixed 330 px gallery + 370 px inspector in one long scroll, which leaves about
   330 px empty at 1366×768 and scrolls the inspector away while you browse.
2. **Roster.** Personalize (2026-11-06) made several themes reproducible from another theme plus an accent or corner change. Those go; the
   OLED variants leave the gallery (True black already selects them); 18 new looks arrive, mostly as the missing light/dark half of an
   existing theme or as new light/dark pairs. Result: **18 dark + 18 light** in the gallery, plus **4 accessibility** themes outside the count.

## CE parity

ComicRack CE has no theme gallery or theme roster beyond a handful of fixed colour schemes; this is Paperbunkr's own feature. Nothing to verify
against `_reference/ComicRackCE`.

## Existing anchors (verified 2026-11-09)

- `Views/Preferences/AppearanceSection.axaml`: Theme tab = `WrapPanel` of gallery (`Width="330"`, 100×64 tiles via `ThemeGroups`) and
  inspector (`Width="370"`), then AUTO SWITCH, `pref:PersonalizeGroup`, CUSTOMIZE in the same `ScrollViewer`. Tabs come from
  `AppearanceTabs` (`theme` / `covers` / `interface`); `PreferenceIndex` entries name the tab of each `Tag` (`appearance.skin`,
  `.themeOptions`, `.autoSwitch`, `.personalize`, `.customize`).
- `PreferencesScreenViewModel.Appearance.cs`: `ThemeGroupOrder = { "Dark", "Light", "Accessibility" }`, `RefreshThemeGallery`,
  `ActiveFamilyVariants`.
- `Theming/Runtime/ThemeCatalog.BuiltInKeys`: 10 legacy v1 themes + 10 v2 families; `LegacyBuiltInCount = 10`.
- `ThemeDocument.ThemeKeyFor`: a single-variant family's key is the bare family key, otherwise `family:variant`; a bare key on a
  multi-variant family resolves to `DefaultVariant` (`Variants[0]`).
- `ThemeService.AutoSwitchTarget`: Auto mode switches to a variant of the active family with the wanted mode, else
  `LastLightThemeKey` / `LastDarkThemeKey`.
- `ThemeService.ResolveTheme`: when True black is on and the family ships an OLED variant, that variant is used; otherwise the generic
  true-black override.
- `ThemeComponentRegistry`: five roles (`button.primary`, `button.secondary`, `button.ghost`, `chip`, `panel.floating`) with `background`,
  `foreground`, `border`, `radius` only - no border thickness, no shadow.
- `ThemeMaterialBuilder`: `solid`, `acrylic` (tint fallback), `linearGradient`, `radialGradient`, `image`, `texture` (tiled, from the theme
  package); the Performance effect tier returns the flat fallback.
- `settings.windowBackdrop` is a theme token, so a variant can set it.
- Golden snapshots `src/Paperbunkr.App.Tests/Goldens/theme-resources/*.json` exist for the legacy built-ins (including `cool_technical`,
  `cool_technical.trueblack`, `overcast`, `vibrant_pop`, `vintage_paperback`).

## Decisions

| # | Topic | Decision |
|---|---|---|
| Q1 | Theme tab layout | Two panes, each with its own `ScrollViewer`: gallery left (`*`), rail right (fixed ~380 px). |
| Q2 | Personalize / Customize | Move to a new **Personalize** tab, second of four (Theme · Personalize · Covers & Detail · Home & Interface). |
| Q3 | Gallery grouping | A `segGroup` switch: **Dark · n / Light · n / Accessibility · n / Installed · n**, opening on the active theme's group. Replaces the stacked group captions. |
| Q4 | Tiles | Bigger tiles, 4 per row in the left pane, each drawing the theme's own canvas material (gradient/texture), corner radius, primary button and chip, and "Aa" in its heading font. |
| Q5 | What counts | Only Dark and Light count toward 18/18. Accessibility is its own group. |
| Q6 / Q18 | Accessibility | Maximum Contrast and Color-Blind Safe each gain a **Light** half: Maximum Contrast · Light (pure white, black text, deep blue accent, 2 px control borders), Color-Blind Safe · Light (the same Okabe-Ito-based palette on white). Accessibility shows 4. |
| Q7 | OLED variants | Abyss, Tokyo Midnight and Graphite keep their OLED variants as files, hidden from the gallery and the variant switch. True black selects them as today. |
| Q8 | Redundant / unique | Redundant = another theme plus Personalize (accent, corners, fonts, card depth, sidebar, ring) reproduces it. Unique = its own surface identity (hue, temperature, material) **and** at least one of: texture/material, component skin, motion, special effect, window backdrop. |
| Q9 | Removals | Remove **Cool & Technical, Vibrant & Pop, Vintage Paperback, Overcast, Graphite · Light**. **Rework Crimson Ink** (ink-grain texture, light partner) rather than remove it. |
| Q10 | Users on a removed theme | Move to the nearest kept theme; carry the removed theme's accent into `AccentOverrideHex` (only if the user had no accent override); one Activity Center note. Overrides scoped to a removed family stay in `theme-overrides.json`, inert. |
| Q11 | New roster | The 18 themes below. |
| Q12 | Theme Home layouts | None for the new themes; layouts stay experimental and off by default. |
| Q13 | Personalize tab layout | Two panes: Looks + controls + Customize scroll on the left; the live preview card is pinned on the right. |
| Q14 | Engine additions | Component property `borderThickness` (buttons, chip, floating panel; 0-3 px, clamped) and `shadow` (static `BoxShadows`, never transitioned) on the five roles; `selection.background` may name a material (texture). Dropped: Crimson Ink · Paper's heading rule, Blueprint's dimension-line dividers. |
| Q15 | Textures | Small tiled PNGs generated by a checked-in script (`tools/themes/generate_textures.py`, run with `py`), written into each theme's folder and referenced as `texture` materials. Deterministic (fixed seeds) so regeneration is reproducible. |
| Q16 | One-mode themes gaining a half | Today's look is `Variants[0]`, so bare saved keys keep resolving to it. A one-time settings fix-up rewrites `ActiveThemeKey`, `LastLightThemeKey`, `LastDarkThemeKey` to the `family:variant` form. Legacy v1 files that gain a half (Windows 11, Matrix, Maximum Contrast, Color-Blind Safe) are rewritten as v2 families whose first variant resolves to exactly today's resources (golden check). Matrix rain stays on Matrix's dark variant only. |
| Q17 | Installed themes | Shown in their own Dark/Light group after the built-ins with an "Installed" mark; the Installed segment shows only them plus Install… and Open folder. Segment counts include them (so "Dark · 19" with one installed dark theme); the 18/18 target is about built-ins. |
| Q19 | Contrast floor | A test over every built-in variant: body text ≥ 4.5:1 and accent ≥ 3:1 against the canvas. |
| Q20 | OLED for new dark themes | None. True black's generic override covers them. |

## The roster

### Kept (18)

| Dark (11) | Light (7) |
|---|---|
| Default | Windows 11 |
| Matrix | Daylight |
| Nordic Frost · Dark | Nordic Frost · Light |
| Tokyo Midnight · Dark | Reading Room · Paper |
| Reading Room · Lamplight | Canopy · Light |
| Canopy · Dark | Sunset Boulevard · Dawn |
| Abyss · Dark | Newsprint |
| Sunset Boulevard · Dusk | |
| Arcade | |
| Graphite · Dark | |
| Crimson Ink (reworked: adds a fine ink-grain `texture` on the canvas) | |

### New (18)

Colours are the starting values from the companion tiles; the contrast test (Q19) has the last word. Fonts are Windows system fonts only
(packaged fonts are not loaded). "Partner" = a new variant of an existing family; "family" = a new built-in family.

**Light (11)**

| Theme | Kind | Canvas / material | Ink | Accent / second | Radius | Heading font | What makes it unique |
|---|---|---|---|---|---|---|---|
| Abyss · Shallows | partner | `#E6F6FB`→`#CBE9F3` gradient + soft caustic radial highlights | `#0B2A3C` | `#0A84B8` / `#6C5BD4` | 12 | Segoe UI | Tide-pool water: light caustics over pale aqua |
| Tokyo Midnight · Daybreak | partner | sky gradient `#FFE9F2`→`#F3E8FF`→`#E6F7FB` | `#2A1838` | `#D6247A` / `#0096A8` | 10 | Bahnschrift | Dawn sky, magenta + cyan signage, keeps the neon fade-in motion |
| Arcade · Pastel | partner | lilac→mint gradient with a faint neon grid texture | `#2B1650` | `#E0359E` / `#00A8C6` | 14 | Bahnschrift | Daylight vaporwave, 2 px chunky buttons, power-on motion |
| Crimson Ink · Paper | partner | `#FAF8F5` + paper-grain texture | `#141414` | `#C8102E` / `#141414` | 1 | Georgia | Editorial red-and-black, hairline black control borders |
| Matrix · Printout | partner | green-bar texture (white / `#DDEFDB` 12 px bands); sprocket-hole texture on the navigation surface | `#1C2A1C` | `#2E7D32` | 0 | Consolas | Tractor-feed printer paper, monospace throughout |
| Golden Age | family | `#F2E6C8` + red Ben-Day dot texture; blue dots on navigation | `#231A10` | `#D7261E` / `#1D4E9E`, badge `#F5C518` | 3 | Impact | Pulp comic newsprint, 2 px black panel borders |
| Screentone | family | `#FFFFFF`; halftone texture on chrome and on the selected row | `#000000` | `#000000` / spot `#E4002B` | 2 | Arial Black | Manga page: 3 px ink borders, screentone selection |
| Risograph | family | `#F4F1EA` + grain texture | `#1F2A55` | `#FF48B0` / `#0078BF` | 6 | Trebuchet MS | Fluoro pink + blue inks; primary button has a 2 px off-register blue `shadow` |
| Blueprint · Whiteprint | family | `#F5F9FD` + fine blue grid texture | `#0B3A6E` | `#1565C0` / `#C2410C` | 0 | Bahnschrift | Drafting sheet, square corners, technical headings |
| Dune | family | `#FBF1E1`→`#F3E3C7` with layered dune-curve texture | `#3B2414` | `#C0562B` / `#2F6F73` | 8 | Palatino Linotype | Midday desert: sand, terracotta, oasis teal |
| Kraft | family (light only) | `#CFAE7E` + fibre texture | `#2A1B0E` | `#B3261E` / `#1F3A93` | 4 | Courier New | Brown card stock, rubber-stamp inks, typewriter headings |

**Dark (7)**

| Theme | Kind | Canvas / material | Ink | Accent / second | Radius | Heading font | What makes it unique |
|---|---|---|---|---|---|---|---|
| Newsprint · Night Edition | partner | `#141412` + newsprint grain | `#EDEAE2` | `#EDEAE2` / `#7FA7D1` | 0 | Georgia | Charcoal late edition, off-white ink as accent |
| Windows 11 · Dark | partner | `#202020`, `windowBackdrop: Mica` | `#FFFFFF` | `#60CDFF` / `#9F8CFF` | 8 | Segoe UI | Native Windows dark with Mica |
| Golden Age · Midnight | family | `#141A30` + dim yellow Ben-Day dots; red dots on navigation | `#F2E6C8` | `#F5C518` / `#E2382F` | 3 | Impact | Pulp night cover, 2 px cream panel borders |
| Screentone · Ink | family | `#0A0A0A`; white halftone on chrome and selected row | `#FFFFFF` | `#FFFFFF` / spot `#FF3355` | 2 | Arial Black | Inverted manga page, 3 px white borders |
| Risograph · Black Stock | family | `#1A1A1C` + grain | `#F3EFE6` | `#FF48B0` / `#FFE800` | 6 | Trebuchet MS | Fluoro inks on black paper, same off-register shadow |
| Blueprint | family | `#0F3460` + white grid texture | `#E8F1FF` | `#FFFFFF` / `#FFB454` | 0 | Bahnschrift | Prussian-blue cyanotype, white line-art controls |
| Dune · Night | family | indigo sky gradient `#1B1A30`→`#2A1D20` + dark dune-curve texture | `#F3E3C7` | `#E0A458` / `#D2693C` | 8 | Palatino Linotype | Desert after sundown |

Golden Age, Screentone, Risograph, Blueprint and Dune are one family each with a light and a dark variant (light first). Kraft is a
single-variant family.

### Totals

| Group | Count |
|---|---|
| Dark | 11 kept + 7 new = **18** |
| Light | 7 kept + 11 new = **18** |
| Accessibility | Maximum Contrast, Maximum Contrast · Light, Color-Blind Safe, Color-Blind Safe · Light = **4** |
| Hidden (True black only) | Abyss · OLED, Tokyo Midnight · OLED, Graphite · OLED |

## Layout details

### Theme tab

- Root: `Grid ColumnDefinitions="*,24,380"`, each column its own `ScrollViewer`; the page no longer scrolls as a whole. 24 px gutter.
- **Left pane:** header row = the group `segGroup` (left) and `Install…` + `iconButton` Open themes folder (right). Install errors and
  install notes keep their current place under the header. The grid is an `ItemsControl` with a `UniformGrid`-style 4-column panel
  (`ItemsRepeater`/`WrapPanel` with item width bound to the pane width / 4; the plan picks the panel). Each tile is a `Button.clickCard`-family
  card: the mini-mockup (material, radius, button, chip, "Aa" in the heading font) and the name trimmed with `CharacterEllipsis`.
  Active theme = accent ring + success check, as today. Installed themes carry a small "Installed" `statusBadge`.
- **Right rail:** inspector card (name, subtitle, preview switch Library / Reader / Bookshelf as a real `segGroup` instead of today's
  unstyled `Button Classes=""`, the preview, palette, contrast badges), VARIANTS (OLED variants excluded), THEME OPTIONS (True black, Matrix
  rain), AUTO SWITCH (moved here from below the gallery).
- The 1366×768 minimum means no stacking breakpoint is needed; the rail's 380 px and the 4-column grid fit the ~1050 px content width.
- Preview tile drawing must use the theme's own brushes (as today), not app tokens; everything else on the page uses tokens only.

### Personalize tab

- Same `*,24,380` two-pane grid. Left pane: `PersonalizeGroup` (Looks row, accent, corners, text size, fonts, card depth, sidebar,
  focus ring, ring strength) then CUSTOMIZE (scope, rows, boost contrast, effect quality, theme layouts, export/import, notes). Right rail:
  the live preview card, moved out of `PersonalizeGroup`'s own `*,Auto` grid into the rail so it stays visible.
- `AppearanceTabs` gains `personalize` second; `PreferenceIndex` entries for `appearance.personalize` and `appearance.customize` point at it.
  Search keywords for removed theme names ("overcast") are dropped; new theme names are added.

## Engine additions (Q14)

- `ThemeComponentRegistry`: add `borderThickness` (`Thickness`, clamped 0-3 px, default = today's per-family value) and `shadow`
  (`BoxShadows`, default none) to `button.primary`, `button.secondary`, `button.ghost`, `chip`, `panel.floating`. Each needs an always-present
  default resource (a setter bound to a missing `DynamicResource` blanks the value). The shared families in `Styles/Controls.axaml` read the
  new resources; no view restyles them locally. Thickness changes are layout changes of at most 2 px per side; check the 28 px control height
  still fits the label at 3 px.
- `selection.background` accepts a material name; the material builder already produces an `ImageBrush` for `texture`.
- Theme documentation (`docs/themes/`) gains the two properties and the selection material.
- No `BoxShadowsTransition` anywhere (lint `NoBoxShadowsTransition`).

## Textures (Q15)

- `tools/themes/generate_textures.py` writes PNGs into `src/Paperbunkr.App/Assets/Skins/<key>/textures/`: Ben-Day dots (red, blue, yellow),
  halftone (black, white), paper grain (light, dark), kraft fibre, drafting grid (blue, white), green-bar bands, sprocket holes, caustics,
  neon grid, dune curves. Small tiles (≤ 128 px), fixed seeds, PNG only.
- Each is declared as a `texture` material with a `fallback` colour, so Balanced/Performance and any load failure keep a flat, readable surface.
- The csproj's `<AvaloniaResource Include="Assets\**" />` already embeds them; texture `source` paths are relative to the theme folder.

## Migration (Q10, Q16)

One idempotent step at startup, after the catalog loads and before the first theme apply:

| Saved key | Becomes | Accent carried (if no override set) |
|---|---|---|
| `cool_technical` | `graphite:dark` | `#5EA7F0` |
| `vibrant_pop` | `default` | `#E85D4E` |
| `vintage_paperback` | `reading_room:lamplight` | `#8C3B2E` |
| `overcast` | `nordic_frost:light` | `#3469A6` |
| `graphite:light` | `windows_11:light` | `#1A5FC4` |
| `abyss:oled`, `tokyo_midnight:oled`, `graphite:oled` | the family's `:dark`, `TrueBlackDark = true` | - |
| `matrix`, `newsprint`, `windows_11`, `crimson_ink`, `arcade`, `maximum_contrast`, `colorblind_safe` | `family:<first variant id>` | - |

- Applied to `ActiveThemeKey`, `LastLightThemeKey`, `LastDarkThemeKey`. A key that is not in the table is left alone (an unknown key already
  falls back to `default` in `ThemeService`).
- If the active theme changed, post one Activity Center alert: "Your theme *X* was retired; you're now on *Y* with its accent kept." No
  dialog.
- `splash-theme.json` is rewritten by the next theme apply, as today; no separate migration.
- No database schema change: these are values in existing `AppSettings` columns.

## Testing

- **Goldens:** the legacy themes converted to v2 (Windows 11, Matrix, Maximum Contrast, Color-Blind Safe) must resolve their first variant to
  exactly their current golden. The v2 families that gain a variant (Abyss, Tokyo Midnight, Arcade, Newsprint, Crimson Ink) have no goldens
  today: **capture a baseline golden for each existing variant before touching its file**, then require it unchanged - except Crimson Ink,
  whose ink grain is an intended change (regenerate that one with `PB_UPDATE_GOLDENS=1`). Delete the goldens of removed themes. Add goldens
  for each new variant.
- **Contrast test** (Q19) over every built-in variant.
- **Catalog/gallery:** counts per group (18 / 18 / 4), OLED variants absent from `ThemeGroups` and `ActiveFamilyVariants`, Installed segment
  filtering, the default segment follows the active theme.
- **Migration:** each row of the table, idempotence, accent carried only when no override exists, the Activity Center alert once.
- **Engine:** `borderThickness` clamping, `shadow` default none, selection material fallback; resource defaults present.
- **Auto switch:** a new pair flips between its halves; Kraft (single) falls back to the last light/dark key.
- **Renders:** `FoundationStyleTests.WithFoundation` PNGs of the Theme and Personalize tabs at 1366×768 (default, daylight, matrix) and a
  contact sheet of all 40 gallery tiles.
- Lint trio (`XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`). Update the existing tests that name
  removed themes: `CoverWallRendererTests`, `InsightsChartPolishTests`, `LibraryLensTests`, `ThemeGoldenResourceTests`, `ThemeServiceTests`,
  `ThemeOverrideTests`, `ThemeResolverTests`, `ThemeV2BuiltInTests`.
- Then the user's screen check.

## Slices

1. **Layout:** two-pane Theme tab, segmented gallery, bigger tiles, Personalize tab, `PreferenceIndex`. The existing roster is unchanged, so this ships
   and can be checked on its own.
2. **Groundwork:** component `borderThickness` / `shadow`, selection material, texture generator, v1→v2 conversion of the themes gaining a half,
   OLED hiding, removals + migration, contrast test.
3. **New themes:** the 18 new looks, the two accessibility lights, the Crimson Ink rework, goldens, contact sheet.

## Approaches considered

1. **One spec, three slices (accepted).** Each slice can be seen on screen before the next.
2. **Two specs (layout, roster).** Rejected: the tile design exists to show the new themes; splitting duplicates the gallery work.
3. **One pass.** Rejected: hardest to verify; every UI pass on this project has needed an on-screen check.

## Avalonia / design-language constraints

- `paperbunkr-design-language`: tokens only outside the tile mockups; `segGroup`/`segItem` for the switches, `clickCard` tiles, `iconButton`
  with names, type classes instead of `FontSize` attributes (the current inspector uses literal sizes - fix while moving it), 4 px grid,
  24 px gutter, `CharacterEllipsis` on names, `controls:Replace.On` for the segment change, `PbMotionEase` only.
- Load the `avalonia` subskills from disk (layout-patterns, styling, data-templates, accessibility) before XAML; run
  `avalonia-pro-max/review-checklist` at the end.
- Tiles live in a list of ~20 visible items per segment: no virtualization needed, but keep each tile a light template (no per-tile
  effects beyond the brushes).
- Theme switch from a tile rebuilds `ThemeGroups`: if the rebuild detaches the clicked tile while its `Click` is routing, defer it one
  dispatcher tick (CLAUDE.md "don't remove/detach a control from inside a routed event").
- Every new resource has a default in `AppTokens.axaml`; process-wide statics reset in test ctor/Dispose.

## Accessibility

Tiles keep `AutomationProperties.Name` = theme name (+ ", installed", ", active"); the segment switch exposes counts in its names; the
contrast floor is enforced by test; the light accessibility halves widen the accessible choices; textures never carry meaning.

## Risks

- **Font availability:** Bahnschrift, Impact, Arial Black, Palatino Linotype, Trebuchet MS, Courier New ship with Windows 10/11, but a
  stripped install falls back to the default font; the tile still shows the theme's colours and texture.
- **Thicker borders** may crowd 22 px `.sm` buttons at 3 px; check Screentone on the Library toolbar at 1366×768.
- **Textures on large surfaces** at high DPI may moiré (halftone, grid); generate at 1× and 2×, pick per scale if needed.
- **Converting v1 themes to v2** risks drifting today's look; the golden check is the guard.
- The working tree is shared with other sessions; the Personalize slice (2026-11-06) is uncommitted and touches the same files.

## Implementation notes

**Slice 1 (layout), built 2026-11-09, not yet seen on screen.**

- Theme tab: `*,24,380` grid, each pane its own `ScrollViewer`; gallery = `segGroup` of `ThemeGallerySegments` + `UniformGrid Columns="4"` of
  `Button.clickCard` hosting the new `Views/Preferences/ThemeGalleryTile` (mockup drawn from `ThemeSummary.CanvasBrush` / `NavigationBrush` /
  `HeadingFontFamily` / `MiniRadius` / `MiniButtonRadius`). Rail: inspector (preview switch now a `segGroup`, name/subtitle on type classes),
  VARIANTS (now a `segGroup`), THEME OPTIONS, AUTO SWITCH.
- Personalize tab: `PersonalizeGroup` + CUSTOMIZE on the left; the preview card moved out of `PersonalizeGroup` into the new
  `Views/Preferences/PersonalizePreview`, pinned on the right. `AppearanceTabKeys.Personalize`; `PreferenceIndex` routes `appearance.personalize` /
  `appearance.customize` there.
- Deviation: the mockup's small pieces use the theme radius scaled ×0.4 and capped at 3 px (`MiniRadius`), because the full radius made
  12 px cover blocks into ovals on Arcade/Abyss/Tokyo (seen in the contact sheet).
- Selecting a theme no longer rebuilds the gallery: `ThemeSummary.IsActive` is observable and `SelectTheme` flips flags in place;
  `SyncCollection` keeps shared items so a clicked tile/variant button is never detached mid-Click. An active OLED variant stays visible until
  slice 2's migration moves it.
- `ThemeGroup` record and `ThemeGroups` removed. New `ThemeSkinProjector.ComponentValue` gives previews a component property as the live apply
  would resolve it.
- Tests: `PreferencesScreenViewModelTests.Appearance_*` (segments, sticky segment, in-place select, OLED hiding, `SyncCollection`),
  `PreferenceIndexTests.EveryAppearanceEntryNamesARealAppearanceTab`, new `ThemeGalleryTileRenderTests` (+ `PB_RENDER_OUT` contact sheet). Lint:
  `XamlConsistencyLintTests.FamilyButtonsCarryNoSizeOverrides` fails only on `Scraper/ComicVine*ReviewDialogView.axaml` (untracked files from other
  work, not this change).

**Slice 2 (groundwork), built 2026-11-09, not yet seen on screen.**

- Baseline goldens captured first for the 17 existing v2 variants (`ThemeGoldenResourceTests.ApplyV2Theme_*`), plus `abyss.dark.trueblack`; the
  retired themes' goldens deleted. Every legacy and v2 golden still passes after the changes below.
- Engine (Q14): `ThemeComponentValueKind.Shadow`; `borderThickness` on the three button roles, `chip` (the outlined filter chip) and
  `panel.floating`, capped at `ThemeComponentRegistry.MaxBorderThickness` = 3; `shadow` on the three button roles only (deviation: chip and
  floating panel get no shadow property - the panel already has its elevation shadow and no new theme needs one). Defaults in `AppTokens.axaml`;
  `Controls.axaml`/`Overlays.axaml`/`Primitives.axaml` read them; button shadows via `Button.x /template/ ContentPresenter`.
  `surfaces.selection` may name a material; the selected-row states (`sideItemButton`/`prefNavItem`/`menuRow` `.active`) now read
  `PbSelectionBrush` (equal to the accent tint on every theme except Tokyo Midnight, whose own `selection.background` now shows).
- Textures (Q15): `tools/themes/generate_textures.py` (pure Python, deterministic - verified byte-identical on a second run) writes 23 opaque
  textures into the theme folders. Deviation: a material replaces the surface brush outright, so each texture has its surface colour baked in
  and is per theme variant, not a shared transparent overlay.
- Windows 11, Matrix, Maximum Contrast and Color-Blind Safe rewritten as v2 single-variant families keeping `id: legacy.<key>` (user overrides
  stay attached); their goldens are unchanged. Matrix rain is detected by `ThemeService.IsMatrixRainTheme` (bare `matrix` or `matrix:dark`).
- Removed: Cool & Technical, Vibrant & Pop, Vintage Paperback, Overcast, Graphite · Light. `ThemeKeyMigration` runs first in
  `ApplyPersistedSettings`; `MainViewModel` raises its one note as an Activity Center Info alert ("Your theme changed").
- Found and fixed: `ThemeCatalog` cached a failed built-in read; a test's MemberData that loaded the catalog during discovery (before
  Avalonia's asset loader exists) poisoned every later theme test in the process. A failed read is no longer cached.
- New tests: `ThemeKeyMigrationTests`, `Theming/ThemeBuiltInContrastTests` (every built-in passes), three in `ThemeSkinTests`,
  `ThemeResolverTests.ConvertedLegacyThemes_*`. Fixtures that named removed themes moved to kept ones (scheduling tests to `default`/`daylight`,
  which stay single-variant in slice 3).

**Slice 3 (new themes), built 2026-11-09, not yet seen on screen.**

- Every value is in `tools/themes/build_roster_2026_11.py` (re-runnable; it rewrote the 15 theme files). A family that had one look keeps it as its
  first, empty variant, so saved bare keys resolve to it and every pre-existing golden still passes (verified), except Crimson Ink's dark
  `PbBgBrush`, now the intended ink-grain texture (golden edited by hand to that one value).
- Gallery: **18 dark + 18 light + 4 accessibility**, 3 OLED hidden (43 entries, 22 families) - asserted in `PreferencesScreenViewModelTests`.
- Deviations from the tables above:
  - Variant names: Golden Age · Day / · Midnight, Screentone · Paper / · Ink, Risograph · White Stock / · Black Stock, Blueprint · Whiteprint /
    · Cyanotype, Dune · Day / · Night. Families that gained a half now show their first look with a suffix too (Arcade · Dark, Newsprint · Paper,
    Matrix · Dark, Windows 11 · Light, Crimson Ink · Dark, Maximum Contrast · Dark, Color-Blind Safe · Dark).
  - Contrast tuning so every variant passes `ThemeV2BuiltInTests`' strict check (no readability notes, text ≥ 7:1): Shallows accent #086A96,
    Pastel #BA2283, Golden Age · Midnight badge #C42A22, Risograph white accent #C2186F / badge #006BA8, Dune · Night and Screentone · Ink
    badge text dark, Kraft accent #7E1812 with explicit muted (#5C452C) and accent text (#6E1510).
  - Screentone headings are `Arial` at weight `Black` (the font lookup takes family + weight; "Arial Black" / "Segoe UI Black" as a family
    name read as not installed). The gallery tile's "Aa" shows the family, not the weight.
  - Windows 11 · Light's pre-existing accent (4.08:1) and border (1.19:1) notes are the only allowed exceptions (`KnownLegacyNotes`).
  - Kraft fibres were halved after the contact sheet showed them reading as wood grain.
- Tests: `ThemeGoldenResourceTests.ApplyRosterTheme_*` (20 new goldens), `ThemeV2BuiltInTests` now covers all 20 v2 families (variant counts,
  strict readability, Auto switch has both halves in 18 families), contrast floor over all 22 families, key-name expectations moved to
  `family:variant`. Theme set: 305 tests, all pass except the pre-existing scraper-dialog lint.
