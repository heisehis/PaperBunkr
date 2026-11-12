# Looks and presets — Design

Slice 5 of [cosmetics pitch 3](2026-11-06-cosmetics-pitch-3.md): item 30 (presets), item 29 (export/import, finished) and item 27 (badges, reshaped). Grilled 2026-11-06; all five
recommended answers accepted. Status: **built 2026-11-06, not committed, not seen on screen** (see Implementation notes). Builds on [Slice 4](2026-11-06-personalization-design.md).

## Summary

Three built-in **presets** (Quiet, Collector, Vivid) set the look in one click from the top of Personalize, with Undo and a "Custom" label. The existing **customizations file**
now also carries the Personalize values, so "Export my look…" / "Import a look…" move a whole look between machines. Every cover **badge** switch lives in one place, the Library
View menu, by moving "Collection dots" there from Preferences. No migration.

## Goals / non-goals

Goals: the three items below, reusing `PersonalizeViewModel`, `ThemeService.ApplyPersonalization`, `ThemeService.ExportCustomizations` / `ImportCustomizations`, and the Library View menu.

Non-goals (decided): **item 20** (per-library accent) - there are no separate libraries (only the Library for comics/manga and the Books screen) and overrides are per theme, not per screen;
**item 25** (toolbar button style) - it would restyle the shared `iconButton` family, which the design language and its lint forbid; **item 26** (background treatment) - theme surface
materials, Home's atmosphere backdrop and Detail's hero backdrop already cover it; user-saved presets (export covers sharing a look); a badge position editor (the bottom-right overlap
that motivated it is gone since the progress ring became the bottom bar); a new file type.

## CE parity

ComicRack CE has no presets or look export; nothing to verify against `_reference/ComicRackCE`. (CE's own per-view display options are what the Library View menu mirrors; moving the collection
dots there follows that pattern.)

## Existing anchors (verified 2026-11-06)

- `ViewModels/PersonalizeViewModel` (Corners, Card depth, Sidebar selection, Focus ring, Ring strength via `PersonalizeLinks`, Reset), `Views/Preferences/PersonalizeGroup`.
- `ThemeService.ExportCustomizations(path)` / `ImportCustomizations(path)` write/read `ThemeOverrideSerializer` JSON (root `version` = `FileThemeOverrideStore.CurrentVersion`, scopes, layouts,
  contrast boost, effect quality). Buttons in Customize (`ThemeExportCustomizations` / `ThemeImportCustomizations`), handlers in `PreferencesScreenViewModel.Appearance.cs` (~L151/164) with `ThemeFileMessage`.
- Library View menu (`Views/LibraryToolbar.axaml` ~L655-700): Unread count, Publisher logo, Rating (`NumericRatingThumbnails`), Language, Dog-ear (`DogEarThumbnails`), each shown only for the view modes where it works.
- "Collection dots on covers" is a Preferences > Cover cosmetics toggle bound to `PreferencesScreenViewModel.ShowCollectionBadges` (`AppSettings.ShowCollectionBadges`, read by the Library load).

## Decisions

| Topic | Decision |
|---|---|
| Presets | Three built-ins shown as three small cards at the top of Personalize. **Quiet**: Corners 35, Card depth Flat, Sidebar Tint, Focus ring Thin, Ring strength Subtle. **Collector**: Corners 60, Lifted, Pill, Normal, Normal. **Vivid**: Corners 80, Lifted, Pill, Thick, Vivid. |
| What a preset touches | Only the look: corners, card depth, sidebar selection, ring width, ring strength. Never the accent, the fonts or the text size (personal and accessibility choices). |
| Applying | Clicking a card applies all five at once (one theme re-apply, not five) and marks that card selected. |
| Undo | An "Undo" button beside the cards restores the five values from just before the last preset; held in memory for the session, cleared by a manual change or a Reset. |
| Custom | When the five current values match no preset, a "Custom" label shows and no card is selected; the theme-default values (Corners 50, Soft, Standard, Normal, Normal) also show as Custom (no "Default" preset - Reset personalization is that). |
| Export/import content | The exported file keeps its current shape and `version`, plus a `personalization` object: `formatVersion` 1, accent hex (or null), corners are already in the Global overrides, `fontScale`, `headingFont`, `cardElevation`, `sidebarStyle`, `focusRingThickness`, `bodyFont`, `ringStrength` (glow tier). |
| Import | Applies the overrides as today, then the `personalization` block if present (each value validated and normalised; unknown keys ignored). A file without the block - every file exported so far - imports exactly as before. A newer `formatVersion` skips only the block and says so in `ThemeFileMessage`. |
| Buttons | Rename to "Export my look…" / "Import a look…" (tooltip: "Your theme customizations and Personalize settings"). Shown in Customize as today **and** next to "Reset personalization", both calling the same commands. JSON file, `.json` filter, no new extension. |
| Badges | Move "Collection dots" into the Library View menu with the other cover badges (shown for Poster and Panorama, like the dots themselves); remove the Preferences row. Cover *effects* (binding spine, progress bar, fade-in, tooltips, selection checkbox) stay in Preferences. The setting stays `AppSettings.ShowCollectionBadges`; toggling it reloads the Library so the dots appear without a restart. |
| Persistence | No migration. Presets are recognised by comparing values; Undo is in memory. |

## Behaviour details

- **One apply per preset:** `PersonalizeViewModel.ApplyPreset` writes the radius overrides and the four settings, and the glow tier, through one `ThemeService` call path that re-applies once
  (add `ApplyLook(...)` that sets the overrides without refreshing, then calls `ApplyPersonalization`, then the glow tier), so the window does not flash through intermediate looks.
- **Undo** snapshots (corners, depth, sidebar, ring width, ring strength) before applying; a later manual change in any of those five clears it.
- **Preset cards** are `Button.clickCard`-style cards with a tiny drawn sample (a corner, a shadow, a ring) and the name; the selected one uses the shared selected state. Names and descriptions in tooltips and
  automation names ("Quiet look: flat cards, tint sidebar, thin ring").
- **Search:** Preferences search finds "preset", "look", "quiet", "vivid", "collector", "export look", "import look" under Personalize; "collection dots" moves its keyword to the Library help text (the View menu
  is not in Preferences search).
- **Export message:** "Exported your look." / "Imported your look." (or the existing error texts).

## Approaches considered

1. **Presets as data over the existing Personalize setters, export extended in place (recommended, accepted).** No new store, no new file type.
2. **A separate `.pbskin` file and a preset store.** More to maintain; duplicates the customizations file; rejected.
3. **Presets that also set accent/fonts.** Rejected: those are personal and accessibility choices a "look" should not overwrite.

## Avalonia / design-language constraints

- `paperbunkr-design-language`: shared families only (`clickCard`, `ghost`), tokens, 4 px grid, PbMotionEase; preset samples drawn with live resources (`PbRadius*`, `PbPosterShadow`, `PbGlowRing`).
- Group `UserControl` keeps `ClipToBounds="False"` so edge buttons keep their focus rings (the focus-ring clipping test walks Preferences).
- No new `.axaml` file is required; if one is added it ships with its code-behind and the weave is verified.
- Library View menu items follow the existing row pattern (label + `CheckBox` with an automation name) and the same view-mode visibility rule.

## Verification

App tests: presets apply exactly their five values and leave accent/fonts/text size alone; one re-apply per preset; selection and "Custom" detection; Undo restores and is cleared by a manual change and by Reset;
export writes the `personalization` block; import of a new file restores it; import of an old file (no block) behaves as today; a newer `formatVersion` skips the block with a message; invalid values normalise;
the View menu shows Collection dots for Poster/Panorama and toggling it reloads; the Preferences row is gone; index tests; lint trio; focus-ring clipping test; a render of the preset cards (default/daylight/matrix).
Then the user's screen check.

## Risks

- Applying several overrides and settings in one go must not flash: the single-apply path needs a test that counts `ThemeApplied` events.
- Older builds reading a new export: the root `version` is unchanged and the extra block is ignored by the existing deserializer - confirm with a test that feeds the new file to `Deserialize`.

## Implementation notes (2026-11-06)

Built per the plan in seven steps. `Services/LookPresets` holds the three presets and `Match`; `ThemeService.ApplyLook` writes the radii without a refresh, saves the four settings and the glow
tier, and re-applies the theme once (one `ThemeApplied`). Deviations and additions:
- **Undo also restores radii edited in Customize.** The preset snapshot keeps the raw Global `metrics.radius*` values, not just the Corners number, so Undo after a preset brings back a
  hand-edited radius that the Corners curve cannot express.
- **Text size does not clear Undo** (it is not one of the five look values).
- **Mirrored Preferences values are reloaded** after a preset or an import (glow tier, accent hex, body font) under their suppress flags, so a stale copy is never written back.
- **Collection dots** have no Preferences search keyword to move (the index never had one); the View menu row carries a tooltip instead.
- The export file's root `version` is unchanged, so an older build still reads the theme part of a new file (tested with the old deserializer).
