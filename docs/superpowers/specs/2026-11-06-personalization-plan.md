# Personalization — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-06-personalization-design.md*

The tree carries many uncommitted edits from other work: touch only the files named here, run only targeted tests, never run two `dotnet build`/`test` at once (check `Get-Process testhost`), never
regenerate a migration the user has launched, never kill a running Paperbunkr (if it locks `bin`, build with `-p:UseAppHost=false` or a scratch `-o`). Before XAML read the `avalonia` styling/data-template/accessibility
subskills and, at the end, `avalonia-pro-max/review-checklist` from `~/.claude/skills/avalonia/...`; load `paperbunkr-design-language`. Each UI step ends with a build of `src/Paperbunkr.App` (prove
`CompileAvaloniaXaml` ran) and the lint trio `--filter "FullyQualifiedName~XamlConsistencyLintTests|FullyQualifiedName~ThemeHardcodedColorLintTests|FullyQualifiedName~SharedControlsStyleTests"`
(the ComicVine `FamilyButtonsCarryNoSizeOverrides` failure is known). The theme-resource golden test (`ThemeGoldenResourceTests`) must stay green at default settings; regenerate goldens (`PB_UPDATE_GOLDENS=1`) only for an intended change and say so.
Process-wide statics in `ThemeService` leak between tests: reset every new static in test constructors/Dispose.

## Step 1: Settings + migration
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit: `FontScale` 100, `HeadingFontFamily` null, `CardElevation` 1, `SidebarStyle`, `FocusRingThickness` 1), `Entities/SidebarStyle.cs` (new enum: Standard, Pill, Tint, append-only),
`src/Paperbunkr.Data/Migrations/<ts>_AddPersonalization.cs` + `.Designer.cs` + snapshot (generated), `src/Paperbunkr.Data.Tests/AddPersonalizationMigrationTests.cs` (new)
**What:** add the properties with their defaults; scaffold with `dotnet ef migrations add AddPersonalization --project src/Paperbunkr.Data` (confirm `Up` is not empty); no-op `Down()` for the `AppSettings` columns.
**Depends on:** none
**Verify:** migration test (defaults, no pending model changes); the earlier collection migration tests still pass.

## Step 2: Pure helpers (corners curve, scale maths)
**Files:** `src/Paperbunkr.App/Services/PersonalizationMath.cs` (new), `src/Paperbunkr.App.Tests/PersonalizationMathTests.cs` (new)
**What:** `CornersToRadii(int slider)` -> (radius, radiusSm, radiusLg) with 0 -> 0/0/0, 50 -> 7/5/14, 100 -> 14/10/28 (linear each side, capped); `RadiiToCorners(r, sm, lg)` -> slider value or null ("Custom") when the three are not on the curve (tolerance 0.5);
`ScaleFontSize(double size, int percent)` (rounds to 0.5, never below 8); allowed scales {90, 100, 110}; `FocusRingStroke(int thickness)` -> 1.5 / 2 / 3.
**Depends on:** none
**Verify:** unit tests for the curve, round trip, custom detection, scale rounding, normalisation of invalid inputs.

## Step 3: ThemeService apply methods (elevation, scale, heading font, focus thickness)
**Files:** `Services/ThemeService.cs` or a new `Services/ThemeService.Personalization.cs` partial (edit/new), `Styles/AppTokens.axaml` (edit: defaults for `PbCardShadow`, `PbFocusRingThickness`), tests: `ThemeServicePersonalizationTests.cs` (new)
**What:** `ApplyCardElevation(int)` swaps the `PbCardShadow` `BoxShadows` resource (Flat none, Soft = today's value, Lifted = deeper, shadow colours from a neutral token, no inset); `ApplyFontScale(int)` rewrites the `PbFontSize*` tokens from their theme/base values (keep the base values so 100 restores exactly; line heights follow);
`ApplyHeadingFont(string?)` overrides the heading/display family roles (null restores the theme's own); `ApplyFocusRingThickness(int)` swaps the ring stroke resource used by `FocusRingAdorner`; each also persists the setting like `ApplyGlowTier`, and `ThemeService` re-applies all of them after a theme change (so they survive a theme switch). Add the missing resources with defaults to `AppTokens.axaml` first (a setter bound to a missing `DynamicResource` blanks the value).
**Depends on:** Steps 1, 2
**Verify:** tests with the statics reset: each apply changes the resource, default restores it exactly, theme change re-applies, invalid values normalise; `ThemeGoldenResourceTests` unchanged at defaults.

## Step 4: Consume the new resources
**Files:** `Styles/Controls.axaml` / the poster-card and `Border.card` shadow sources (edit: read `PbCardShadow`), `Controls/FocusRingAdorner*` (edit: stroke from `PbFocusRingThickness`), `Styles/Controls.axaml` + `Views/MainWindow.axaml` (edit: sidebar host classes), tests: lint trio + a render check
**What:** poster cards and `Border.card` take their shadow from `PbCardShadow` (default value equals today's, so goldens/looks do not change); the focus ring stroke follows the thickness resource; sidebar style as **state styles** only: `sidebarPill` / `sidebarTint` classes on the sidebar host adjust `.active` of `sideItemButton`, `prefNavItem` and `colRow.active` (fill radius from `PbRadiusSm`, tint from the accent soft token; label gets SemiBold so selection never relies on colour alone), switched by a class bound to a mutable property (never by removing a `Style`).
**Depends on:** Step 3
**Verify:** build + lint; headless render of the Library sidebar and a poster grid in default/daylight/matrix for Standard / Pill / Tint and Flat / Soft / Lifted; goldens unchanged at defaults.

## Step 5: Personalize view model
**Files:** `ViewModels/PersonalizeViewModel.cs` (new; hosted by `PreferencesScreenViewModel`), `ViewModels/PreferencesScreenViewModel.cs` (edit: create/load it, expose `Personalize`), tests: `PersonalizeViewModelTests.cs` (new)
**What:** properties for accent (swatch list with selected state, custom colour via `AccentColorInput`, flyout open state, contrast warning against the active surface, "Reset to theme"), Corners (0-100 slider, "Custom" label via `RadiiToCorners`, writes the three `metrics.radius*` Global overrides through `ThemeService.SetOverride`), card elevation, text scale, heading font (+ `FontFamilies`), sidebar style, focus thickness (each a `SuggestBox` string projection like `GlowTierText`), and `ResetCommand` (clears accent + the three corner overrides, resets the five settings, applies). Loading reads the current values; applying uses the Step 3 methods and the existing accent apply path; guard feedback loops (`_suppress` flag like `_suppressBehaviorApply`).
**Depends on:** Steps 2, 3
**Verify:** view-model tests: each control persists and applies, the slider/overrides round trip, "Custom" when a Customize row is edited, Reset restores defaults and leaves density/glow/motion/other overrides alone, contrast hint flips with the surface seam.

## Step 6: Personalize UI (group + sample strip)
**Files:** `Views/Preferences/PersonalizeGroup.axaml` + `.axaml.cs` (new), `Views/Preferences/AppearanceSection.axaml` (edit: host it above CUSTOMIZE; remove the old accent hex row), tests: `PersonalizeRenderTests.cs` (new)
**What:** a `StackPanel Tag="appearance.personalize"` with `settingsGroupCaption` "PERSONALIZE": accent swatch row + "+" flyout (`HsvColorPicker`, hex field, same popup rules as the collection dialog: `PopupFit.ToWindow`, one ScrollViewer, close deferred a tick), Corners `Slider` with value text, `SuggestBox` rows for elevation, text scale, heading font, sidebar style, focus ring, a "Reset personalization" ghost button, and the sample strip (a `PosterTile`-style card, a `primary` button, a `pbChip`, a sidebar row) that follows the live resources. Names and value text on every control; keep `PreferenceIndex` search entries for the new rows.
**Depends on:** Step 5
**Verify:** build + lint; render Appearance in default/daylight/matrix at 1366x768 and the sample strip at 90 / 110 % scale; keyboard Tab order; the old accent row is gone and `appearance.*` tags still resolve in `PreferenceIndex`.

## Step 7: Startup order and theme-change re-apply
**Files:** `Services/ThemeService.cs` (edit: startup path), `App.axaml.cs` or the theme bootstrap (edit if needed), tests: extend `ThemeServicePersonalizationTests`
**What:** at startup apply in the spec's order (theme -> overrides -> font scale -> heading font -> card elevation -> sidebar style -> focus thickness); a theme switch re-applies the personalization settings; `Reset` followed by restart gives exactly the theme's own look.
**Depends on:** Steps 3, 5
**Verify:** tests for order and for survival across a theme change; goldens unchanged at defaults.

## Step 8: Review, docs and on-screen pass
**Files:** `docs/paperbunkr-todo.md`, the spec's "Implementation notes", this plan's status, the memory file
**What:** run `avalonia-pro-max/review-checklist`; run the new Data/App test classes, the Theme golden test, the Preferences classes by name (not the Slow ones) and the lint trio - not the full suite. Then the user's 1366x768 screen check: each control live, the extremes of Corners (0 / 100) on all three themes, 110 % text with the toolbar and Library state row, Pill/Tint sidebar, Flat/Lifted cards, Thick focus ring, Reset, restart persistence; record verified vs unverified in the roadmap entry by hand.
**Depends on:** Steps 1-7
**Verify:** as above; do not claim done until the user has seen it.

## Risks to watch while building
- Runtime scaling of `PbFontSize*` may clip fixed-height controls and the ~750 px toolbar at 110 %; test at 1366x768 with the sidebar and preview panel open.
- `ThemeService` is a hub with statics and golden snapshots: keep new methods additive and default-neutral, and reset statics in tests.
- A missing `DynamicResource` blanks a setter: add every new resource to `AppTokens.axaml` before any style consumes it.
- Radii overrides hit every control at once; check 0 and 100 on default, daylight and matrix.
- Do not switch the sidebar style by removing a `Style` at runtime; use classes bound to a mutable property.
- New `.axaml` files need their code-behind in the same step; verify the weave, not just "0 Errors".

## Status (2026-11-06)

Steps 1-8 done in code; Step 7 needed no code beyond Step 3 (proven by the fresh-start and accent-change tests). Targeted tests, goldens and lint green apart from the known ComicVine failure. On-screen check outstanding.
