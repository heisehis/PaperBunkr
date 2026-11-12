# Personalization — Design

Slice 4 of [cosmetics pitch 3](2026-11-06-cosmetics-pitch-3.md): items 19 (accent), 21 (corners), 22 (card elevation), 23 (font scale + heading font), 24 (sidebar style) and 28 (focus ring
thickness only). Grilled 2026-11-06; all eight recommended answers accepted. Status: **design for review - nothing built.**

## Summary

Paperbunkr already has a theme system with user overrides, an accent override, a font family and a density preset, but they live behind a hex text box and a long "Customize" token list.
This slice adds a **Personalize** group at the top of Appearance whose controls are friendly front-ends over what exists (accent swatches + picker, a Corners slider, a text scale, a
heading font) plus three genuinely new looks (card elevation, sidebar selection style, focus-ring thickness). No parallel theme system; one migration for the new settings.

## Goals / non-goals

Goals: the six items below, a live sample strip, a single "Reset personalization", reuse of `HsvColorPicker`, `AccentColorInput`, `ThemeService`, `UserThemeOverrides`.

Non-goals (decided): per-library accent (item 20, Slice 5); cover mat width (no mat style found in the shared styles - confirm before any later work); selection **treatment** on tiles (wash / edge /
corner check) - deferred because it touches the virtualized tile templates; an icon-only sidebar rail (the nav rail already expands on hover); font scale above 110 % (breaks the 1366x768
minimum); named presets (item 30, Slice 5); a Theme Studio.

## CE parity

ComicRack CE has no theming beyond a few colours; these are deliberate Paperbunkr features. Nothing to verify against `_reference/ComicRackCE`.

## Existing anchors (verified 2026-11-06)

- `AppSettings.AccentOverrideHex` (global, null = theme accent; derived accentText/accentSoft/glow are luminance-aware); Appearance row "Accent color override" is a `TextBox` bound to `AccentOverrideHexText`.
- `AppSettings.SelectedFontFamily` (body font; Appearance "Font family" `SuggestBox` over `FontFamilies`); typography roles (`PbDisplayFontFamily`, `PbHeadingFontFamily`, sizes `PbFontSize*`) are written by `ThemeService` from the theme.
- `ThemeService` (`ApplyDensity`, `ApplyGlowTier` / `GetGlowTier`): the pattern for a setting that swaps app resources; `ThemeService.Overrides.SetOverride(scope, path, value)` writes `theme-overrides.json`.
- `ThemeVocabulary` roles: `metrics.radius`, `metrics.radiusSm`, `metrics.radiusLg` (defaults 7 / 5 / 14), `metrics.spacingUnit`, `selection.*`, `focus.ring`; the Customize list (`ThemeCustomizationViewModel.Rows`) edits any role as text, with Reset per row and per scope.
- `Views/Preferences/AppearanceSection.axaml` (tile gallery + inline pane): CUSTOMIZE, FONT, density, glow, motion groups; `SettingsRow` + `SuggestBox` idiom; `PersistBehaviorSetting` for plain settings.
- Library sidebar rows: `Button.sideItemButton` (3 px accent bar + tint when `.active`), collection rows `Border.colRow` in `MainWindow.axaml`; `FocusRingAdorner` draws the app-wide focus ring (glow tier already scales it).

## Decisions

| Topic | Decision |
|---|---|
| Placement | New **PERSONALIZE** group first in Appearance, above CUSTOMIZE, with a live sample strip: one poster-style card, a primary button, a chip and a sidebar row, all real controls. Changes apply live (the app already re-applies themes live), so the strip is a convenience, not a separate render path. |
| Accent | Replace the hex `TextBox` row with: the six preset swatches + a "+" swatch opening the `HsvColorPicker` flyout + hex field (as in the Edit Collection dialog), a "Reset to theme" link, and the low-contrast hint against the active surface. Storage stays `AccentOverrideHex`; selecting a swatch/applying a colour calls the existing apply path. |
| Corners | One slider, Square to Round, 0-100, default theme at 50. It writes the three **Global** overrides `metrics.radius`, `metrics.radiusSm`, `metrics.radiusLg` proportionally (default 5 / 7 / 14 at 50; 0 at 0; double at 100, capped at sane maxima), through `SetOverride(Global, ...)`. "Reset" clears those three. Chips/pills stay squircles; nothing here can create an oval. |
| Card elevation | `CardElevation` (Flat / Soft / Lifted), default Soft = today's look. Implemented like `GlowTier`: `ThemeService.ApplyCardElevation(int)` swaps a `BoxShadows` resource (`PbCardShadow`) that Library poster cards and `Border.card` consume. Independent of the hover glow. High contrast and Reduced Motion are respected (no animated shadow; no `BoxShadowsTransition`). |
| Text scale | `FontScale` 90 / 100 / 110 %, default 100. `ThemeService.ApplyFontScale` multiplies the `PbFontSize*` tokens (10-22 scale and hero) and the related line heights; icon sizes and fixed-height controls (`PbControlHeight`) do not scale. Verified at 1366x768 with the sidebar and preview panel open at 110 %. |
| Heading font | `HeadingFontFamily` (string, null = the theme's own). A `SuggestBox` over the existing installed-fonts list, applied by overriding the heading and display font roles; the body font (`SelectedFontFamily`) is unchanged. A sample line shows it. |
| Sidebar style | `SidebarStyle` (Standard = bar + tint, Pill = rounded fill, Tint = tint only). Applied by one class on the sidebar host (`sidebarPill` / `sidebarTint`) that adjusts the **state** styles (`.active`) of `sideItemButton`, `prefNavItem` and the collection rows; no family restyle, no size changes. Selection stays visible without colour (the label weight changes too). |
| Focus ring | `FocusRingThickness` (Thin 1.5 / Normal 2 / Thick 3 px), default Normal. Scales the existing ring through the same resource mechanism as the glow tier; colour stays the `focus.ring` role. Selection treatment on tiles is deferred. |
| Persistence | Accent and corners: no schema change (`AccentOverrideHex`, `theme-overrides.json`). One migration `AddPersonalization` adds `AppSettings.FontScale` (int, 100), `HeadingFontFamily` (string?), `CardElevation` (int, 1), `SidebarStyle` (enum, Standard), `FocusRingThickness` (int, 1 = Normal); no-op `Down()`. |
| Reset | A "Reset personalization" button: clears the accent override and the three corner overrides, sets the five new settings to their defaults, and applies. It does not touch theme choice, density, glow, motion or the Customize overrides the user typed for other tokens. |

## Behaviour details

- **Order of application at startup:** theme -> overrides (corners, accent) -> font scale -> heading font -> card elevation -> sidebar style -> focus thickness, so a reset restores exactly the theme's own look.
- **Live apply:** each control writes its setting then calls the matching `ThemeService` method; `ThemeApplied` refreshes the sample strip and any cached overlay brush (the overlays redraw on `ThemeApplied`).
- **Accent flyout reuse:** the flyout view model logic from the collection dialog (hex parse, HSV round-trip, contrast check) is `AccentColorInput` / `HsvColorPicker` already; only the host differs.
- **Corners vs the Customize rows:** the Corners slider and the `metrics.radius*` Customize rows edit the same Global overrides; the slider shows "Custom" when the three values are not on its proportional curve.
- **Scale and the lint:** the type-scale lint (10/11/12/13/15/18/22) looks at classes and tokens, not rendered size, so scaling the tokens at runtime stays within it.
- **Sidebar style and the Collections rows:** `colRow.active` already tints from the collection accent; Pill keeps that tint as the fill; Tint drops the 3 px bar only.
- **Keyboard:** all new controls are plain `SuggestBox` / `Slider` / swatch buttons with names; no hotkeys are added.

## Approaches considered

1. **Front-ends over the existing override store and settings (recommended, accepted).** Smallest change, one source of truth, Customize rows stay the expert view.
2. **A new personalization store separate from `theme-overrides.json`.** Cleaner to reset but two sources for the same radii/accent and drift risk; rejected.
3. **Ship presets first (item 30) and no individual controls.** Faster but not what was asked and needs the individual controls anyway; deferred to Slice 5.

## Avalonia / design-language constraints

- `paperbunkr-design-language`: tokens only (no hex in views; the swatch hex strings are data), shared families, `PbMotionEase`, squircles, 1366x768; sample strip uses real controls.
- Load the `avalonia` styling/data-template/accessibility subskills from disk before XAML; run `avalonia-pro-max/review-checklist` at the end.
- A `Style` setter bound to a missing `DynamicResource` blanks the value (project memory): every new resource (`PbCardShadow`, sidebar/focus tokens) must exist in `AppTokens.axaml` with a default.
- Process-wide statics (`_glowTier`-style) leak between tests: reset new statics in test constructors/Dispose.
- Do not remove a `Style` at runtime to switch sidebar style (stale template-part values): switch by classes bound to a mutable property on the sidebar host.
- New `.axaml` views (if any, e.g. a `PersonalizeGroup` user control) ship with code-behind in the same change; verify `CompileAvaloniaXaml` ran.

## Accessibility

Slider, swatches, pickers carry names and expose value text ("Corners: 60 percent", "Text size 110 percent"); the contrast hint is text; sidebar selection does not rely on colour alone; focus ring Thick is the more visible option, not less; high contrast keeps its own ring and selection.

## Verification

Unit: `ThemeService` apply methods (elevation, scale, ring thickness) with the process-wide statics reset; corners curve (slider <-> three overrides, "Custom" state); `AccentColorInput` reuse; reset restores defaults; migration (defaults, no pending changes). View-model tests for the Personalize group (each setting persists and applies, Reset). Golden resource snapshots (`Goldens/theme-resources`) must not change at defaults - run that test and regenerate only for an intended change. Headless renders of Appearance (default / daylight / matrix) and of the sample strip at 90 / 110 % and at 1366x768; lint trio. Then the user's screen check.

## Risks

- Scaling `PbFontSize*` at runtime may clip fixed-height controls and toolbars at 110 %; check the toolbar (~750 px) and the Library state row at 1366x768.
- The shared radii overrides affect every control at once; the extremes (0 and 100) need a look on all three themes.
- `ThemeService` is a large partial-class hub with process-wide statics and golden snapshots; new apply methods must be additive and default-neutral.
- Card elevation touches the poster card template's shadow source; keep tile overlays a single custom-rendered control (scroll-smoothness constraint).

## Implementation notes (2026-11-06)

Built per the plan; everything folds into the theme's own apply transaction (`ThemeService.Personalization.cs`). Deviations: card depth applies to the cover cards, not `Border.card`;
the ring thickness also scales the hover glow; hover edges follow the theme's glow colour; the sidebar style covers the contextual sidebar rows, not the Preferences nav; the heading
font overrides the display font too; the Corners slider is debounced. Goldens unchanged at defaults. Not yet seen on screen.
