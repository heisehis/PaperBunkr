# Looks and presets — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-06-looks-and-presets-design.md*

**Status (2026-11-06):** Steps 1-7 built; not committed; the on-screen pass is the user's. See the spec's Implementation notes.

The tree carries many uncommitted edits from other work: touch only the files named here, run only targeted tests, never two `dotnet build`/`test` at once (check `Get-Process testhost`), never
kill a running Paperbunkr (build with `-p:UseAppHost=false` if it locks `bin`). Load `paperbunkr-design-language`; read `avalonia-pro-max/review-checklist` at the end. Each UI step ends with a build of
`src/Paperbunkr.App` and the lint trio (the ComicVine `FamilyButtonsCarryNoSizeOverrides` failure is known). `ThemeGoldenResourceTests` must stay green. Render tests must load
`FormControls.axaml` / `Typography.axaml` (the harness's `AppStyles()` does not). No migration in this slice.

## Step 1: Preset data and matching
**Files:** `src/Paperbunkr.App/Services/LookPresets.cs` (new), `src/Paperbunkr.App.Tests/LookPresetsTests.cs` (new)
**What:** `record LookValues(int Corners, int CardElevation, SidebarStyle Sidebar, int FocusThickness, int RingStrength)`; `LookPresets.All` = Quiet (35, 0, Tint, 0, Subtle=1), Collector (60, 2, Pill, 1, Normal=2),
Vivid (80, 2, Pill, 2, Vivid=3), each with name and one-line description; `LookPresets.Match(LookValues)` -> the preset or null ("Custom"). Ring strength uses the glow-tier numbers (`ThemeService.GlowTier*`).
**Depends on:** none
**Verify:** tests: each preset's values, Match finds each exactly, the theme defaults (50, 1, Standard, 1, 2) and an arbitrary mix are Custom, presets never carry accent/fonts/text size (the record has no such fields).

## Step 2: One re-apply for a whole look
**Files:** `Services/ThemeService.Personalization.cs` (edit), `Services/ThemeService.Overrides.cs` (edit: a non-refreshing batch setter), `ThemeServicePersonalizationTests.cs` (edit)
**What:** `ThemeService.ApplyLook(LookValues look)`: write the three Global `metrics.radius*` overrides (or clear them at 50) without refreshing, set `CardElevation` / `SidebarStyle` / `FocusRingThickness` / `GlowTier`,
save once, set `_glowTier`, then one `RefreshActiveTheme()`; returns the previous `LookValues` (for Undo). `GetLook()` reads the current five values (corners via `PersonalizationMath.RadiiToCorners`, Custom radii -> null corners).
**Depends on:** Step 1
**Verify:** tests: `ApplyLook` raises `ThemeApplied` exactly once; resources match the look (radii, poster shadow, ring spread, sidebar runtime); `GetLook` round-trips; accent, fonts and text size unchanged; goldens unchanged.

## Step 3: Presets in the Personalize view model
**Files:** `ViewModels/PersonalizeViewModel.cs` (edit), `Models/LookPresetCard.cs` (new: preset + `IsSelected`), `PersonalizeViewModelTests.cs` (edit)
**What:** `PresetCards`, `ApplyPresetCommand(name)` (snapshot for Undo via `ApplyLook`'s return, then `Refresh()`), `UndoPresetCommand` + `CanUndoPreset`, `LookLabel` ("Quiet" / "Collector" / "Vivid" / "Custom")
recomputed in `Refresh` from `ThemeService.GetLook()`; any manual change to Corners / Card depth / Sidebar / Focus ring / Ring strength clears the Undo snapshot; `Reset` clears it too.
**Depends on:** Step 2
**Verify:** tests: applying each preset selects its card and sets the five values; Undo restores the previous five and disables itself; a manual change after a preset gives "Custom" and no Undo; Reset clears Undo; accent/fonts/text size untouched.

## Step 4: Export / import of the Personalize values
**Files:** `Services/ThemeService.Export.cs` (edit), `Services/PersonalizationSnapshot.cs` (new: read/write the `personalization` JSON block, `formatVersion` 1, validation), `ThemeServicePersonalizationTests.cs` or a new `LookExportTests.cs`
**What:** `ExportCustomizations` adds `personalization` { formatVersion, accent, fontScale, headingFont, cardElevation, sidebarStyle, focusRingThickness, bodyFont, ringStrength } beside the existing root (root `version` unchanged);
`ImportCustomizations` applies overrides as today, then the block when present (values normalised; unknown keys ignored; a newer `formatVersion` skips the block and returns a note), applying accent, font family
and glow tier through their existing `ThemeService` setters and the rest through `ApplyPersonalization`, with one final refresh. An `out string? note` (or a result type) reports "look skipped: newer format".
**Depends on:** none (Step 2 helps for the single refresh)
**Verify:** tests: export then import on a fresh database restores all values; an old file (no block) imports exactly as before and leaves Personalize alone; a newer `formatVersion` imports the overrides and reports the skip;
invalid values normalise; `ThemeOverrideSerializer.Deserialize` accepts a new file (so an older build can still read the theme part).

## Step 5: Buttons and UI
**Files:** `ViewModels/PreferencesScreenViewModel.Appearance.cs` (edit: rename messages to "Exported your look." / "Imported your look.", after import reload `AccentOverrideHexText`, `SelectedFontFamily`, `GlowTierText` with their suppress flags and call `_personalize?.Refresh()`),
`PersonalizeLinks` (edit: `ExportLook` / `ImportLook` commands), `Views/Preferences/PersonalizeGroup.axaml` (edit: preset cards row with drawn samples, "Custom" label, Undo, "Export my look…" / "Import a look…" beside Reset),
`Views/Preferences/AppearanceSection.axaml` (edit: rename the Customize buttons and tooltips), `Models/PreferenceIndex.cs` (edit: preset / look keywords), tests: `PersonalizeRenderTests` (edit), `PreferenceIndexTests` (run)
**What:** preset cards are `clickCard`-family buttons with a tiny sample (corner radius, shadow, ring from the preset's own values) and the name; selected state from `IsSelected`; names in automation/tooltips; buttons share the Preferences commands.
**Depends on:** Steps 3, 4
**Verify:** build + lint; render (default/daylight/matrix) of the group with the cards; the focus-ring clipping test (`Screen_FocusRings_AreNotClipped`); index tests.

## Step 6: Collection dots into the Library View menu
**Files:** `ViewModels/LibraryScreenViewModel.cs` (edit: `ShowCollectionBadges` observable property loaded with the library settings, persisted to `AppSettings.ShowCollectionBadges`, toggling triggers a reload so the store fills/clears),
`Views/LibraryToolbar.axaml` (edit: a "Collection dots" row with `IsCoverOverlayScope` visibility), `Views/Preferences/AppearanceSection.axaml` + `ViewModels/PreferencesScreenViewModel.cs` (edit: remove the row and its property/load),
`Models/PreferenceIndex.cs` (edit: drop "collection dots" from Cover cosmetics), tests: `CollectionBannerTests` (edit the badge test to toggle through the Library view model), Preferences tests by name
**What:** the setting keeps its column; only its control moves.
**Depends on:** none
**Verify:** tests: the View menu toggle persists and fills/clears `CollectionBadgeStore`; the Preferences row is gone; lint; index tests.

## Step 7: Review, docs and on-screen pass
**Files:** `docs/paperbunkr-todo.md`, the spec's "Implementation notes", this plan's status, the memory file
**What:** `avalonia-pro-max/review-checklist`; run the new and edited classes, `ThemeGoldenResourceTests`, the focus-ring clipping test, index tests, lint trio - not the full suite. Record that items 20, 25 and 26 were dropped. Then the user's screen check:
each preset and Undo, Custom after a manual change, export on one profile and import on another (and an old export), Collection dots in the View menu.
**Depends on:** Steps 1-6
**Verify:** as above; do not claim done until the user has seen it.

## Risks to watch while building
- A preset must not flash through intermediate looks: test the `ThemeApplied` count.
- Import touches settings the Preferences screen mirrors (accent, body font, glow tier): reload them with their suppress flags or the screen writes the old values back.
- An older build reading a new export: keep the root `version` and confirm the deserializer ignores the extra block.
- Moving a toggle between view models: make sure only one place writes `ShowCollectionBadges`.
