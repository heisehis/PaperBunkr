# Theme gallery layout and theme roster — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md*

Three slices; each ends with a build into `bin` and a screen check before the next starts. Load the `paperbunkr-design-language` skill and the
`avalonia` subskills from disk (`layout-patterns`, `styling`, `data-templates`, `accessibility`; `avalonia-pro-max/review-checklist` at the end of
each XAML step) before touching AXAML. Targeted test classes only; never two `dotnet build`/`dotnet test` at once. The Personalize slice
(2026-11-06) is uncommitted in the same files - check `git status` first and edit around it.

Surface found 2026-11-09:
- `ViewModels/PreferencesScreenViewModel.Appearance.cs` - `AppearanceTabKeys`, `AppearanceTabs`, `ThemeGroupOrder`, `ThemeGroups`,
  `RefreshThemeGallery`, `ActiveFamilyVariants`.
- `ViewModels/PreferencesScreenViewModel.SectionTabs.cs` - tag → tab selection; `Models/PreferenceIndex.cs` lines ~49-64 (`appearance.*` → `"theme"`).
- `Models/ThemeSummary.cs` (+ `ThemeGroup` record); built only in `ThemeService.Engine.cs SummaryFrom` from `GetAvailableThemes()`.
- `ThemeService.Engine.cs` - `GroupOf`, `IsActiveKey`, `ApplyTheme` (writes `LastLight/DarkThemeKey`), `ApplyPersistedSettings` (startup),
  `AutoSwitchTarget`.
- `Theming/ThemeComponentRegistry.cs` (`ThemeComponentValueKind` Brush / CornerRadius / Thickness / Number), `Theming/Runtime/ThemeSkinProjector.cs`
  (kind → resource), defaults in `Styles/AppTokens.axaml` (`PbButtonPrimaryRadius`, `PbChipRadius` ...), consumers in `Styles/Controls.axaml`.
- Selected rows (`sideItemButton.active`, `prefNavItem.active`, `menuRow.active`, ...) use `PbAccentSoftBrush`; `selection.background`
  (`PbSelection*`) exists in `ThemeVocabulary` but nothing binds it.
- Matrix is detected by the exact key `"matrix"` in `PreferencesScreenViewModel.IsActiveThemeMatrix`, `MainViewModel` (~1283) and
  `Views/MatrixRainOverlay.cs` (`LoadTheme("matrix")`); its glyphs/font arrive via `LegacyThemeAdapter` as `settings.matrixGlyphs` /
  `typography.body.family`.
- `ThemeCatalog.BuiltInKeys` / `LegacyBuiltInCount`; goldens `src/Paperbunkr.App.Tests/Goldens/theme-resources/` driven by
  `ThemeGoldenResourceTests` (`BuiltInKeys` TheoryData, `.trueblack` and `.accent` cases).
- Alerts: `IActivityService.RaiseAlert(ActivityAlert)` (pattern: `Services/CeImportAlerts.cs`).
- Tests naming removed themes: `CoverWallRendererTests`, `InsightsChartPolishTests`, `LibraryLensTests`, `ThemeGoldenResourceTests`,
  `ThemeServiceTests`, `Theming/ThemeOverrideTests`, `Theming/ThemeResolverTests`, `Theming/ThemeV2BuiltInTests`. Wiki: `wiki/Preferences.md`.

---

## Slice 1 — Layout (roster unchanged)

### Step 1: Personalize tab and search routing
**Files:** `ViewModels/PreferencesScreenViewModel.Appearance.cs` (edit), `Models/PreferenceIndex.cs` (edit),
`ViewModels/PreferencesScreenViewModel.SectionTabs.cs` (check only), tests: the existing Preferences search/tab test class (find with
`grep -rl "appearance.customize" src/Paperbunkr.App.Tests`) (edit).
**What:** add `AppearanceTabKeys.Personalize = "personalize"` and the tab `new(Personalize, "Personalize")` second in `AppearanceTabs`. Point the
`appearance.personalize` and `appearance.customize` index entries at `"personalize"`. Leave `appearance.skin/.variants/.themeOptions/.autoSwitch` on `"theme"`.
**Depends on:** none.
**Verify:** targeted test asserting each tag opens its tab; tab order test.

### Step 2: Gallery view-model state
**Files:** `ViewModels/PreferencesScreenViewModel.Appearance.cs` (edit), `Models/ThemeSummary.cs` (edit), new
`src/Paperbunkr.App.Tests/ThemeGalleryViewModelTests.cs`.
**What:**
- `ThemeSummary`: `IsInstalled => !IsBuiltIn`, `IsGalleryHidden => VariantKind == ThemeVariantKind.Oled`.
- Replace the stacked `ThemeGroups` with: `ThemeGallerySegments` (Dark / Light / Accessibility / Installed, each with `Key`, `Label`, `Count`,
  `IsActive`), `SelectedGallerySegment`, `VisibleThemes` (built-ins of the segment first, then installed ones; Installed = installed only).
  OLED variants never enter `VisibleThemes`, counts or `ActiveFamilyVariants`.
- On each `RefreshThemeGallery` the segment defaults to the active theme's group the first time the tab opens; a later manual segment choice
  sticks for the session.
- `SelectGallerySegmentCommand`. `ThemeGroups` is deleted once nothing binds it.
- `SelectThemeCommand` triggers a rebuild of `VisibleThemes`: if that happens synchronously inside the tile's `Click`, post it with
  `Dispatcher.UIThread.Post` (CLAUDE.md routed-event rule).
**Depends on:** none.
**Verify:** `ThemeGalleryViewModelTests` - counts per segment with today's roster (17 dark incl. 3 OLED → 14 visible, 9 light, 2 accessibility), OLED
excluded, default segment follows the active theme, installed theme appears in its mode group and in Installed, variant list excludes OLED.

### Step 3: Tile data from the theme itself
**Files:** `Models/ThemeSummary.cs` (edit), `Services/ThemeService.Engine.cs` (`SummaryFrom`, edit), tests in `ThemeServiceTests` (edit).
**What:** add `CanvasBrush` (the resolved `surfaces.canvas` material via `ThemeMaterialBuilder` at the user's effect quality, falling back to the
solid canvas), `NavigationBrush` (same for `surfaces.navigation`), `TileRadius` (`CornerRadius` from `metrics.radiusSm`), `ButtonRadius`
(component `button.primary.radius`), `ButtonForegroundBrush` (`button.primary.foreground`), `HeadingFontFamily` (`typography.heading.family`,
null → app default), `Subtitle` kept. Materials are built once per summary (no per-frame work).
**Depends on:** none.
**Verify:** `ThemeServiceTests`: a gradient family yields a `LinearGradientBrush`, a legacy theme a `SolidColorBrush`, Performance quality a solid;
Newsprint's heading family is Georgia.

### Step 4: Theme tab XAML
**Files:** `Views/Preferences/AppearanceSection.axaml` (edit), new `Views/Preferences/ThemeGalleryTile.axaml` + `ThemeGalleryTile.axaml.cs`
(same step, minimal code-behind - CLAUDE.md AVLN2000 rule).
**What:**
- Theme panel = `Grid ColumnDefinitions="*,24,380"`; column 0 and column 2 each a `ScrollViewer`; the old outer `ScrollViewer`/`WrapPanel` go.
- Left: header `Grid *,Auto` = `Border.segGroup` of `Button.segItem` bound to `ThemeGallerySegments` (`Classes.active`, AutomationProperties.Name
  "Dark themes, 18") | `Install…` (`Button.ghost`) + `Button.iconButton` Open folder. Install error/notes below. Grid = `ItemsControl` over
  `VisibleThemes` with `UniformGrid Columns="4"`, item = `ThemeGalleryTile`, wrapped in `controls:Replace.On` keyed on the segment.
- `ThemeGalleryTile`: `Button.clickCard` (Command = `SelectThemeCommand`); mini-mockup drawn from the summary's own brushes (`CanvasBrush`,
  `NavigationBrush`, accent block, primary button with `ButtonRadius`/`ButtonForegroundBrush`, chip outline, "Aa" in `HeadingFontFamily`),
  `CornerRadius="{Binding TileRadius}"`, active ring + success check as today, `statusBadge` "Installed" when `IsInstalled`; name with
  `pbTextMeta` + `CharacterEllipsis`. Theme colours inside the mockup come from bindings (allowed: they are data, not view hex).
- Right rail `StackPanel Spacing="16"`: inspector card (`Border.card`; `pbTextTitle` name, `pbTextMeta` subtitle; preview switch as
  `segGroup`/`segItem` replacing `Button Classes=""`; the three previews unchanged except literal `FontSize` → type classes), VARIANTS,
  THEME OPTIONS, AUTO SWITCH (moved from below the gallery).
- Remove the now-moved Personalize/Customize blocks from this panel (they go in step 5).
**Depends on:** steps 2, 3.
**Verify:** lint trio (`XamlConsistencyLintTests|ThemeHardcodedColorLintTests|SharedControlsStyleTests`); new render test in
`PersonalizeRenderTests`-style (`FoundationStyleTests.WithFoundation`, `PB_RENDER_OUT`) of the Theme tab at 1080×646 for default / daylight / matrix;
check the PNGs; `RealWindowKeyboardTests`-style check that arrows move between tiles and Enter applies (tiles are buttons, no extra handler).

### Step 5: Personalize tab XAML
**Files:** `Views/Preferences/AppearanceSection.axaml` (edit), `Views/Preferences/PersonalizeGroup.axaml` (edit), new
`Views/Preferences/PersonalizePreview.axaml` + `.axaml.cs`, `src/Paperbunkr.App.Tests/PersonalizeRenderTests.cs` (edit).
**What:** a `personalize` panel with the same `*,24,380` grid. Left `ScrollViewer`: `PersonalizeGroup` (minus its preview card - its inner
`*,Auto` grid becomes a single column) then the CUSTOMIZE block moved verbatim from the Theme panel. Right `ScrollViewer`: `PersonalizePreview`
(the moved "Live sample" card, `DataContext` = `Personalize`, unchanged automation id `PersonalizeSample`), plus `controls:Replace.On` on the panel.
**Depends on:** step 1.
**Verify:** `PersonalizeRenderTests`, `PersonalizeViewModelTests`, lint trio; render PNG of the tab.

### Step 6: Slice 1 check
**Files:** none new.
**What:** `avalonia-pro-max/review-checklist`; build into `bin` (`dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`, confirm XAML weave ran
by launching); ask the user to look at both tabs at 1366×768. Write findings into the spec's Implementation notes.
**Depends on:** steps 1-5.
**Verify:** user's screen check.

---

## Slice 2 — Groundwork

### Step 7: Baseline goldens for v2 families (before touching any theme file)
**Files:** `src/Paperbunkr.App.Tests/ThemeGoldenResourceTests.cs` (edit), new goldens under `Goldens/theme-resources/`.
**What:** extend the golden theory to v2 keys that stay: `abyss:dark`, `abyss:oled`, `tokyo_midnight:dark`, `tokyo_midnight:oled`, `arcade`,
`newsprint`, `crimson_ink`, `graphite:dark`, `graphite:oled`, `nordic_frost:*`, `reading_room:*`, `canopy:*`, `sunset_boulevard:*`.
Generate with `PB_UPDATE_GOLDENS=1` for **only these new files**; existing goldens must not change.
**Depends on:** none (but must land before steps 10, 13, 14).
**Verify:** `ThemeGoldenResourceTests` green without the env var; `git status` shows only added golden files.

### Step 8: Component border thickness, shadow, selection material
**Files:** `Theming/ThemeComponentRegistry.cs`, `Theming/Runtime/ThemeSkinProjector.cs`, `Theming/ThemeVocabulary.cs`, `Styles/AppTokens.axaml`,
`Styles/Controls.axaml`, `docs/themes/` authoring doc (edit all); tests: the component-skin test class (`grep -rl ThemeComponentRegistry
src/Paperbunkr.App.Tests`) (edit).
**What:**
- New `ThemeComponentValueKind.Shadow` (parsed as `BoxShadows`, invalid → default with a diagnostic).
- Add `borderThickness` (Thickness, clamp each side 0-3, default literal = today's thickness per family) and `shadow` (default none) to the five
  roles → resources `Pb<Prefix>BorderThickness` / `Pb<Prefix>Shadow`, defaults in `AppTokens.axaml` equal to today's values.
- `Controls.axaml`: the five families set `BorderThickness` / `BoxShadow` from those resources (no transitions on `BoxShadow`).
- `selection.background` becomes `ThemeValueKind` brush-or-material (default still `action.soft`); the selected-row states (`sideItemButton.active`,
  `prefNavItem.active`, `menuRow.active`, and the other `PbAccentSoftBrush` *selection* setters - check each of the ~8 in `Controls.axaml`, leave
  non-selection uses alone) bind `PbSelectionBrush`.
**Depends on:** step 7.
**Verify:** all goldens unchanged (defaults reproduce today); component tests for clamping, shadow parsing, material selection; lint trio incl.
`NoBoxShadowsTransition`; `SharedControlsStyleTests`.

### Step 9: Texture generator
**Files:** new `tools/themes/generate_textures.py`, new PNGs under `src/Paperbunkr.App/Assets/Skins/<key>/textures/`.
**What:** `py -I tools/themes/generate_textures.py` writes, with fixed seeds, ≤128 px tiles: `benday-red`, `benday-blue`, `benday-yellow`,
`halftone-black`, `halftone-white`, `grain-light`, `grain-dark`, `kraft-fibre`, `grid-blue`, `grid-white`, `greenbar`, `sprocket`, `caustics`,
`neon-grid`, `dunes-day`, `dunes-night`, `ink-grain` - each into the folder of the theme that uses it (shared ones duplicated per theme so a
theme folder exports as a self-contained `.crpck`). Uses only Pillow if present, else a pure-Python PNG writer (no new dependency).
**Depends on:** none.
**Verify:** run twice, byte-identical output; step 16's load test.

### Step 10: Themes that gain a half - format and Matrix detection
**Files:** `Assets/Skins/windows_11/theme.json`, `matrix/theme.json`, `maximum_contrast/theme.json`, `colorblind_safe/theme.json` (rewrite as v2
with today's look as the first variant), `ViewModels/PreferencesScreenViewModel.cs` (`IsActiveThemeMatrix`), `ViewModels/MainViewModel.cs`
(~1283), `Views/MatrixRainOverlay.cs` (edit); `Theming/ThemeV2BuiltInTests.cs` (edit).
**What:** convert the four v1 files to `schemaVersion: 2` with `base` + a first variant (`light` for Windows 11, `dark` for the others) carrying every
v1 value (`colors` → palette/semantic, radii → metrics, `windowBackdrop` → `settings.windowBackdrop`, `matrixGlyphs` → `settings.matrixGlyphs`,
`defaultFontFamily` → `typography.body.family`, `category`). No second variant yet (slice 3). Replace the three `"matrix"` key comparisons
with one helper `ThemeService.IsMatrixActive()` = active family key `matrix` **and** the resolved theme has `settings.matrixGlyphs`; the overlay
loads the active Matrix variant instead of `LoadTheme("matrix")`.
**Depends on:** step 7.
**Verify:** the four legacy goldens unchanged (`ThemeGoldenResourceTests`, including `.trueblack`/`.accent` cases); Matrix rain still starts on
Matrix (existing Matrix tests); `ThemeV2BuiltInTests`.

### Step 11: Removals and key migration
**Files:** delete `Assets/Skins/cool_technical`, `vibrant_pop`, `vintage_paperback`, `overcast`; remove the `light` variant from
`graphite/theme.json`; `Theming/Runtime/ThemeCatalog.cs` (`BuiltInKeys`, `LegacyBuiltInCount`); new `Services/ThemeKeyMigration.cs`;
`Services/ThemeService.Engine.cs` (`ApplyPersistedSettings`); delete goldens `cool_technical*.json`, `overcast.json`, `vibrant_pop.json`,
`vintage_paperback.json`, `graphite:light` baseline from step 7; update the eight test files listed above; `Models/PreferenceIndex.cs` (drop
"overcast"); `wiki/Preferences.md`; new `src/Paperbunkr.App.Tests/ThemeKeyMigrationTests.cs`.
**What:**
- `ThemeKeyMigration.Apply(AppSettings, ThemeCatalog) → MigrationResult` (pure): the spec's table for `ActiveThemeKey`, `LastLightThemeKey`,
  `LastDarkThemeKey`; OLED keys → `:dark` + `TrueBlackDark = true` (only for the active key); bare keys of families that now have several
  variants → `family:<first>`; accent carried into `AccentOverrideHex` only when it is null and only for the active key. Idempotent.
- Called first thing in `ApplyPersistedSettings` (save if changed). `ThemeService` exposes `PendingRetiredThemeNotice` (old name, new name);
  the shell, once `IActivityService` is up, raises one `ActivityAlert` (Info) and clears it. Find where the shell first has both services
  (`MainViewModel` startup) and raise it there.
- The other test files: switch their fixtures from removed keys to kept ones with the same intent (e.g. `cool_technical` → `graphite:dark`).
**Depends on:** steps 7, 10.
**Verify:** `ThemeKeyMigrationTests` (every table row, idempotence, accent-only-when-unset, OLED → True black, unknown key untouched);
the eight updated classes; `ThemeGoldenResourceTests`.

### Step 12: Contrast floor test
**Files:** new `src/Paperbunkr.App.Tests/Theming/ThemeBuiltInContrastTests.cs`.
**What:** for every built-in family × variant: `text.primary` vs `surface.canvas` ≥ 4.5, `action.primary` vs `surface.canvas` ≥ 3.0, using
`ThemeColorMath.ContrastRatio`; for a texture canvas, against its `fallback` colour.
**Depends on:** step 11.
**Verify:** green on the remaining roster (fix any existing theme that fails, recorded in the spec notes).

### Step 13: Slice 2 check
**What:** build into `bin`; user checks that their current theme survived (Abyss · OLED → Abyss + True black), Matrix rain, and that nothing
visibly changed elsewhere. Notes into the spec.
**Depends on:** steps 7-12.

---

## Slice 3 — The new themes

### Step 14: Partners for existing families
**Files:** `Assets/Skins/abyss/theme.json` (+ `shallows`), `tokyo_midnight` (+ `daybreak`), `arcade` (+ `pastel`; becomes `variants` with today's as
`dark` first), `crimson_ink` (+ `paper`; base gains `ink-grain` texture - intended golden change), `matrix` (+ `printout`), `newsprint` (+ `night`;
today's as `paper` first), `windows_11` (+ `dark`); textures from step 9 in each folder.
**What:** values from the spec's tables; component skins via step 8 (Arcade · Pastel 2 px buttons, Crimson Ink · Paper 1 px black borders);
Tokyo Daybreak keeps `neonIn`, Arcade Pastel keeps `powerOn`; Windows 11 · Dark sets `settings.windowBackdrop: Mica`; Matrix · Printout has no
`settings.matrixGlyphs` (no rain), monospace body.
**Depends on:** steps 8, 9, 10, 11.
**Verify:** step 7's baselines for the existing variants unchanged except `crimson_ink` (regenerate that one); contrast test; new goldens.

### Step 15: New families
**Files:** new `Assets/Skins/golden_age`, `screentone`, `risograph`, `blueprint`, `dune` (each `light` first + `dark`), `kraft` (single);
`Theming/Runtime/ThemeCatalog.cs` (`BuiltInKeys`).
**What:** values from the spec. Screentone (both): `borderThickness` 3 on buttons/chip/panel, `selection.background` = halftone material.
Golden Age (both): `borderThickness` 2. Risograph (both): `button.primary.shadow` `2 2 0 0 <second ink>`. Blueprint (both): radius 0.
**Depends on:** steps 8, 9, 11.
**Verify:** contrast test; goldens; `ThemeV2BuiltInTests` (every built-in parses with no diagnostics; every texture source loads).

### Step 16: Accessibility lights and roster checks
**Files:** `Assets/Skins/maximum_contrast/theme.json` (+ `light`: white, black text, accent `#0037DA`, `borderThickness` 2), `colorblind_safe`
(+ `light`, same palette on white); `ThemeGalleryViewModelTests` (edit); `Models/PreferenceIndex.cs` (new theme names as keywords);
new render test writing a contact sheet of all gallery tiles.
**What:** both stay `category: accessibility`.
**Depends on:** steps 14, 15.
**Verify:** gallery counts **18 / 18 / 4**, OLED hidden; Auto switch flips within a new pair and falls back for Kraft (`ThemeServiceTests`);
contact-sheet PNG reviewed.

### Step 17: Finish
**What:** full theme test set (`--filter "FullyQualifiedName~Theme|FullyQualifiedName~Personalize|FullyQualifiedName~XamlConsistencyLint"`),
`avalonia-pro-max/review-checklist`, build into `bin`, user's screen check of all 40 tiles in Dark/Light/Accessibility at 1366×768.
Update `docs/paperbunkr-todo.md`, the spec's Implementation notes, and the theme-system memory.
**Depends on:** all.
