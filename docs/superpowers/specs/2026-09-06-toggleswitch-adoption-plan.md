# ToggleSwitch Adoption — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-06-toggleswitch-adoption-design.md*

## Step 1: ToggleSwitch style + motion wiring
**Files:** `src/Paperbunkr.App/Styles/FormControls.axaml` (edit)
**What:** New `ControlTheme` (or `Style Selector="ToggleSwitch"`) overriding only the thumb's
`Transitions` `Duration` to `{DynamicResource PbMotionFast}`. No color/track/sizing changes -
FluentAvalonia defaults stay. Confirm `FormControls.axaml` is actually included from `App.axaml`'s
resource merge before assuming it applies app-wide (check the `<Styles>`/`<ResourceDictionary>`
include list in `App.axaml`).
**Depends on:** none
**Verify:** `dotnet build`; manual - toggle Reduced Motion in Preferences, confirm any `ToggleSwitch`
(e.g. the existing `AutomationSection.axaml` one) snaps instantly vs. slides.

## Step 2: Convert Preferences CheckBoxes (24 across 6 files)
**Files:** `src/Paperbunkr.App/Views/Preferences/AboutSection.axaml` (1),
`AdvancedSection.axaml` (6), `LibrarySection.axaml` (4 - includes the two just-added
`AutoRemoveMissingOnScan`/`DontReimportRemovedFiles` toggles from this session's earlier work),
`AppearanceSection.axaml` (2), `ReaderSection.axaml` (5), `GeneralSection.axaml` (6)
**What:** Per row: `<CheckBox Content="Label" IsChecked="{Binding X}" .../>` becomes a `TextBlock`
("Label", matching the row's existing label styling) + `<ToggleSwitch IsChecked="{Binding X}" .../>`
side by side - preserve `AutomationProperties.AutomationId`, `ToolTip.Tip`, and any `IsEnabled`/
`IsVisible` binding exactly. Where a row already has a separate label `TextBlock` next to the
`CheckBox` (common in this codebase's existing layout), just swap the control, no new `TextBlock`
needed.
**Depends on:** Step 1
**Verify:** `dotnet build`; manual spot-check 2-3 rows per file.

## Step 3: Convert the 7 non-Preferences settings/filter files
**Files:** `src/Paperbunkr.App/Views/LibraryToolbar.axaml` (9), `ActivityDrawerView.axaml` (1),
`UpdateAvailableOverlay.axaml` (1), `ReaderSettingsSheet.axaml` (1), `IssuePropertiesScreen.axaml` (1),
`SmartScreen.axaml` (1), `PluginScreen.axaml` (1)
**What:** Same conversion as Step 2. Explicitly do **not** touch: `LibraryScreen.axaml`,
`BooksScreen.axaml`, `EventsScreen.axaml`, `ReadingScreen.axaml` (per-row/tile selection),
`BulkIssuePropertiesScreen.axaml`, `BulkSeriesPropertiesScreen.axaml`, `BulkBookPropertiesOverlay.axaml`
(stage/apply gates) - these stay `CheckBox` per the design doc's explicit non-goal.
**Depends on:** Step 1
**Verify:** `dotnet build`; manual spot-check `LibraryToolbar` filters and `ReaderSettingsSheet`.

## Step 4: Extend the Reduced Motion test
**Files:** `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:** No new test needed per the design doc's Testing section - the existing
`ReducedMotion_Change_PersistsToAppSettings_AndAppliesLive` (line ~264) already covers the resource-
level contract this style relies on. Confirm it still passes unchanged (it should - this plan adds a
consumer of `PbMotionFast`, not a new resource).
**Depends on:** Step 1
**Verify:** `dotnet test --filter FullyQualifiedName~PreferencesScreenViewModelTests`

## Step 5: Full targeted verification
**What:** `dotnet build` on `Paperbunkr.App.csproj`, then run the `PreferencesScreenViewModelTests`
and any other suite touching a converted screen (e.g. `LibraryScreenViewModelTests` if `IsChecked`
bindings there are exercised) to confirm zero behavior change - this whole plan is control-type +
styling only.
**Depends on:** Steps 2-4
**Verify:** targeted `dotnet test` runs; manual on-screen check of one row per bucket in the design
doc's table (already covered by Steps 2-3's spot-checks).
