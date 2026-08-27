# Design Language Foundation — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-24-design-language-foundation-design.md*

## Step 1: Token schema — elevation, glow, hero gradient, radius scale
**Files:** `src/Paperbunkr.App/Models/SkinTheme.cs` (edit), `src/Paperbunkr.App/Assets/Skins/default/theme.json` (edit), `src/Paperbunkr.App/Assets/Skins/windows_11/theme.json` (edit), `src/Paperbunkr.App/App.axaml` (edit), `src/Paperbunkr.App/Services/SkinService.cs` (edit)

**What:** Add to `SkinColors`: `Surface0`/`Surface1`/`Surface2`/`Surface3`, `Glow`, `HeroGradientStart`/`HeroGradientEnd`. Add to `SkinTheme`: `RadiusSm`/`RadiusLg` (existing `Radius` stays as-is, becomes the "Md" tier). All new C# properties get dark-theme-appropriate defaults (so a third-party `.crpck` missing them still renders sanely) — matching `default` skin's own values.

Real values, chosen to reuse proven-contrast pairs where possible:
- `default` (Evolved Amber): `surface0 #0A0A0C`, `surface1 #14161B` (today's `bg`), `surface2 #1C1F26` (today's `chrome`), `surface3 #242833`, `glow #66E0995A`, `heroGradientStart #000A0A0C` (0% alpha), `heroGradientEnd #FF0A0A0C`, `radiusSm 5`, `radiusLg 14`.
- `windows_11` (light): `surface0 #F3F3F3` (today's `bg`), `surface1 #FFFFFF` (today's `chrome`), `surface2 #FAFAFA`, `surface3 #FFFFFF`, `glow #4D0078D4`, `heroGradientStart #00F3F3F3`, `heroGradientEnd #FFF3F3F3`, `radiusSm 6`, `radiusLg 16`.

Contrast check (WCAG AA, done by hand since no automated contrast tool exists in this repo): `PbText #ECE7DB` on `surface1 #14161B` ≈ 13.6:1; `PbTextMuted #B3ADA0` on `surface2 #1C1F26` ≈ 8:1; `PbAccentText #E0995A` on `surface2` ≈ 6.9:1 — all comfortably clear 4.5:1. Record these ratios as a code comment next to the new `App.axaml` tokens so the reasoning isn't silently lost.

Add matching `Color`/`SolidColorBrush` pairs to `App.axaml`'s `Application.Resources` (`PbSurface0Color/Brush` … `PbSurface3Color/Brush`, `PbGlowColor/Brush`, `PbHeroGradientStartColor`/`PbHeroGradientEndColor`, `PbRadiusSm`, `PbRadiusLg`) with the `default` skin's values as the compiled-in fallback (same role `PbBgColor` etc. already play). Extend `SkinService.ApplySkinResources` to set all of these live, same pattern as the existing `SetColorAndBrush` calls. `PbRadius` (existing key) is untouched — still resolves to the Md tier, so the 12 files already consuming it keep working unchanged.
**Depends on:** none
**Verify:** `dotnet build`; manually launch the app and confirm no visual regression (colors should look identical to today, since `surface1`/`surface2` reuse the exact old `bg`/`chrome` hex values — only the new `surface0`/`surface3`/`glow`/hero-gradient tokens are net-new, not yet consumed by anything).

## Step 2: Existing skin tests updated for the additive schema
**Files:** `src/Paperbunkr.App.Tests/SkinServiceTests.cs` (edit), `src/Paperbunkr.App.Tests/WindowsElevenSkinTests.cs` (edit)
**What:** Update whatever assertions enumerate `SkinColors` fields or expected resource keys to include the new ones, using the real values from Step 1.
**Depends on:** Step 1
**Verify:** `dotnet test --filter SkinServiceTests|WindowsElevenSkinTests`

## Step 3: Reduced-motion preference
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit), new EF migration under `src/Paperbunkr.Data/Migrations/` (generated via `dotnet ef migrations add AddReducedMotionPreference`), `src/Paperbunkr.App/App.axaml` (edit — add `PbMotionFast`/`PbMotionEase` tokens), `src/Paperbunkr.App/Services/SkinService.cs` (edit — `ApplyReducedMotion`/read persisted value, mirroring the existing `ApplyFont`/`GetSelectedFontFamily` pair), `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit), `src/Paperbunkr.App/Views/PreferencesScreen.axaml` (edit)
**What:** `AppSettings.ReducedMotion` (bool, default false). `PbMotionFast` (`TimeSpan`, 150ms) and `PbMotionEase` (a `CubicEaseOut`) become app resources; when reduced motion is on, `SkinService` overwrites `PbMotionFast` to `TimeSpan.Zero` live (same "set the resource, consumers just bind to it" pattern already used for `PbFontFamily`) — no consumer needs to check the flag itself. A new "Motion" `groupBox` goes into `PreferencesScreen.axaml`'s Appearance tab, right between the existing "Font" and "Future" groups (matching that section's established `groupBox`/`groupHeader` markup), with a single toggle bound to a new `ReducedMotion` property on the view model, following the exact load/apply/persist shape `SelectedFontFamily` already uses (`_suppressReducedMotionApply` guard, `OnReducedMotionChanged` partial method calling `_skinService.ApplyReducedMotion(value)`).
**Depends on:** Step 1 (shares the App.axaml token-block edit location)
**Verify:** `dotnet ef database update` applies cleanly against a scratch DB; new `PreferencesScreenViewModelTests` case toggling `ReducedMotion` and asserting persistence + `SkinService` call, mirroring the existing font-override test.

## Step 4: Font bundling — Bebas Neue + Source Serif 4
**Files:** new `src/Paperbunkr.App/Assets/Fonts/BebasNeue-Regular.ttf`, `SourceSerif4-Regular.ttf` (+ their OFL license text files, e.g. `Assets/Fonts/OFL-BebasNeue.txt`/`OFL-SourceSerif4.txt`), `src/Paperbunkr.App/Paperbunkr.App.csproj` (edit if the font files aren't picked up by the existing `AvaloniaResource` glob — check first, likely already covered by an `Assets/**` include), `src/Paperbunkr.App/App.axaml` (edit)
**What:** Source both fonts' actual `.ttf` files (OFL-licensed, redistribution permitted) and commit them under `Assets/Fonts/`. Add `PbDefaultFontFamily` (`avares://Paperbunkr.App/Assets/Fonts/#Source Serif 4, Georgia, serif`) and `PbDisplayFontFamily` (`avares://Paperbunkr.App/Assets/Fonts/#Bebas Neue, sans-serif`) as static `FontFamily` resources.

**Font-override interaction (deliberate, not accidental):** the existing Preferences → Appearance font picker (`PbFontFamily`) is a general body/UI-text override — it should keep affecting body text. Bebas Neue display type is a deliberate branded choice for hero/heading moments, not general UI text, so it does **not** go through the override mechanism. Concretely: change `SkinService.ApplyFontResource`'s null-branch from `resources.Remove("PbFontFamily")` to `resources["PbFontFamily"] = PbDefaultFontFamily` (a static field referencing the new bundled Source Serif 4) — this is the only place the override plumbing changes; every existing `{DynamicResource PbFontFamily}` selector in `App.axaml` (`TextBlock`, `Button`, `TextBox`, `ComboBox`, `ComboBoxItem`, `MenuItem`) keeps working unmodified and now defaults to Source Serif 4 instead of the OS font. `PbDisplayFontFamily` is referenced directly (`StaticResource`, not `DynamicResource`) by the new `PbTextHero`/`PbTextHeading` styles in Step 5 — it never goes through `SkinService` at all.
**Depends on:** none (can happen in parallel with Steps 1–3)
**Verify:** `dotnet build`; launch the app, confirm body text renders in Source Serif 4 by default and still swaps correctly when a Preferences font override is chosen (regression-check the existing feature, since its null-branch semantics changed).

## Step 5: Type scale + component primitive styles
**Files:** new `src/Paperbunkr.App/Styles/Typography.axaml`, new `src/Paperbunkr.App/Styles/Primitives.axaml` (both merged into `App.axaml`'s `Application.Styles` via `<StyleInclude Source="avares://Paperbunkr.App/Styles/....axaml"/>`)
**What:**
- `Typography.axaml`: `PbTextHero`, `PbTextHeading` (both `FontFamily="{StaticResource PbDisplayFontFamily}"`), `PbTextBody`, `PbTextCaption` (both inherit the global `{DynamicResource PbFontFamily}` default, just differ in size/weight/color) as named `Style` selectors (`Style Selector="TextBlock.pbTextHero"`, etc. — Avalonia doesn't have a first-class "named text style" concept beyond classes, so these are `Classes="pbTextHero"` consumers apply).
- `Primitives.axaml`: four `ControlTheme`s —
  - `PosterTileTheme` (surface2 background, `PbRadiusMd`, glow-ring `BoxShadow` on `:pointerover`/`:focus-visible` using `PbGlowColor`, badge/progress-bar template parts as named `Border`/`Grid` slots)
  - `ChipTheme` (extends the visual pattern already in `DetailPills.axaml`, generalized into a reusable `ControlTheme` rather than that file's local styles)
  - Three `Button` style classes (`primary`/`secondary`/`ghost`) replacing default `FluentTheme` button chrome with `Pb*` tokens + `PbMotionFast`/`PbMotionEase` transitions
  - `FloatingPanelTheme` (surface3 background, `PbRadiusLg`, `PbElevationShadow` — a new static `BoxShadows` resource defined alongside it — open/close `Transitions` using `PbMotionFast`/`PbMotionEase`)
**Depends on:** Step 1 (surface/glow/radius tokens), Step 4 (fonts)
**Verify:** Step 6's showcase view is the real verification; `dotnet build` alone only proves the XAML parses.

## Step 6: Internal showcase view
**Files:** new `src/Paperbunkr.App/Views/DesignShowcaseScreen.axaml` + `.axaml.cs`, new `src/Paperbunkr.App/ViewModels/DesignShowcaseScreenViewModel.cs`, `src/Paperbunkr.App/Views/PreferencesScreen.axaml` (edit — one `#if DEBUG`-gated nav entry, e.g. a small button in the existing "Future" group box), `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit — command to open it), `src/Paperbunkr.App/Views/MainWindow.axaml`/`.axaml.cs` or `MainViewModel.cs` (edit, wherever screen navigation is actually routed — confirm exact mechanism by reading `MainViewModel.cs` before writing this step's code)
**What:** A single screen placing sample `PosterTile`s (with/without badge, with/without progress bar), `Chip`s, all three `Button` variants, and a `FloatingPanel`-styled sample panel, purely for visual confirmation — not reachable in a Release build.
**Depends on:** Step 5
**Verify:** Manual on-screen check (launch via `dotnet run`, open the showcase from the debug-gated Preferences entry) — this is the actual proof the primitives look right, not a substitute for it.

## Step 7: Icon audit + vector conversion (7 icons: touched files only)
**Files:** new `src/Paperbunkr.App/Assets/Icons/Icons.axaml` (new shared `StreamGeometry` dictionary), new `src/Paperbunkr.App/Assets/Icons/icon-mapping.md` (the action → `PbIcon*` documentation the spec calls for)
**What:** Convert exactly the 7 icons actually used by the files Steps 8–9 touch — `Star`, `Copy`, `Close_Circle`, `Save`, `Circle_Check`, `Folder_Open`, `Circle_Warning` — from their existing 96×96 raster PNGs to hand-traced `StreamGeometry` path data in the thin-outline style, named `PbIconStar`, `PbIconCopy`, `PbIconCloseCircle`, `PbIconSave`, `PbIconCircleCheck`, `PbIconFolderOpen`, `PbIconCircleWarning`. Audited all 17 consuming files for these 7 specifically (already done during design — no conflicting dual-meaning usages found for any of them, each icon maps to exactly one action everywhere it currently appears). `icon-mapping.md` documents the 7 action→icon pairs now, structured so later phases append rather than restructure. The remaining 32 icons stay raster/`OpacityMask` for now, per the spec's incremental-rollout principle.
**Depends on:** none
**Verify:** Visual diff against the current raster icons once wired into Step 8/9's files — silhouette should read the same at the sizes those files actually use (14–20px).

## Step 8: FloatingPanel migration (5 overlays)
**Files:** `src/Paperbunkr.App/Views/ReadingListPropertiesOverlay.axaml`, `IssuePropertiesScreen.axaml`, `BulkIssuePropertiesScreen.axaml`, `QuickRateOverlay.axaml`, `MigrationOverlay.axaml` (all edit)
**What:** Swap each file's manually-styled root `Border` (`Background="{DynamicResource PbBgBrush}"`, ad-hoc `CornerRadius`/`BorderThickness`) for `Classes="floatingPanel"` consuming `FloatingPanelTheme` from Step 5. Swap their `Assets/Icons/*.png` `OpacityMask` references for the corresponding `PbIcon*` `StreamGeometry` from Step 7 (`Star`→`PbIconStar` etc.) — `Path Data="{StaticResource PbIconStar}"` in place of `Border.OpacityMask`/`ImageBrush`. Wire the open/close transition to respect `PbMotionFast` (already reduced-motion-aware from Step 3, no extra work needed here beyond using the token).
**Depends on:** Steps 3, 5, 7
**Verify:** Manual on-screen check of each of the 5 overlays — open/close, hover/focus glow on interactive elements, reduced-motion toggle actually shortens the open/close transition, all existing functionality (Save/Cancel, star rating, etc.) still works identically.

## Step 9: Real-window restyle (2 windows)
**Files:** `src/Paperbunkr.App/Views/CrashReportWindow.axaml`, `src/Paperbunkr.App/Views/PluginQuestionDialog.axaml` (both edit)
**What:** Both already use `DynamicResource` brushes throughout (confirmed by reading them — no hardcoded hex), so most of the visual update is automatic once Steps 1/4/5 land. Explicit changes: swap `Background="{DynamicResource PbBgBrush}"` → `PbSurface3Brush` (same visual tier as `FloatingPanel`, appropriate since these are conceptually floating panels that happen to be real OS windows) and `CornerRadius="6"` (CrashReportWindow's report box) → `{DynamicResource PbRadiusMd}`; apply the new `primary`/`secondary`/`ghost` button classes from Step 5 to their action buttons in place of ad-hoc per-button styling. No architecture change — both stay real `Window`s.
**Depends on:** Steps 1, 5
**Verify:** Manual on-screen check — trigger a crash report (or the plugin question dialog via a test plugin command) and confirm it renders with the new tokens and still functions (Copy/Save/Continue/Exit, and Option/Primary respectively).

## Step 10: Final pass
**Files:** none new — verification only
**What:** Full `dotnet build` + `dotnet test` run; re-read the design spec's Testing/Verification section and confirm every listed item was actually done (contrast ratios recorded, icon-mapping audited, five overlays + two windows manually checked, reduced-motion toggle checked).
**Depends on:** Steps 1–9
**Verify:** `dotnet build` clean, `dotnet test` green, manual checklist from the spec complete.

## Post-ship correction (during Phase 3 brainstorming)

The palette values below were the **original** ship values. Direct user testing during Phase 3
(Home screen) found the live app read bluish rather than matching the intended dark-amber design,
and asked for the base to go darker. Two real, retroactive fixes landed in `SkinTheme.cs`,
`Assets/Skins/default/theme.json`, and `App.axaml`:

1. `FluentTheme`'s own accent-color system (`ColorPaletteResources.Accent`) was never overridden,
   so every stock-templated control (`TextBox` focus, `CheckBox`/`ToggleSwitch`, `ScrollBar`,
   selection highlights) still showed Microsoft's default blue - fixed via `FluentTheme.Palettes`.
2. `Surface0` moved to literal `#000000`; `Bg`/`Surface1`/`Chrome`/`Surface2`/`Surface3` all
   shifted darker to match, per the user's explicit "really dark, if not black."

See [Home Screen design](2026-08-24-home-screen-design.md)'s Background section for the full
account. Current values live in the source files, not reproduced here - this plan's own numbers
below are a historical record of Phase 1's original ship state, not the present-day values.

## Implementation notes (real findings, not anticipated in the plan)

- **Real bug found and fixed:** `PbRadius`/`PbRadiusSm`/`PbRadiusLg` were declared as `x:Double` in `App.axaml` and assigned as raw `double` in `SkinService.ApplySkinResources`. Consuming them via `{DynamicResource PbRadiusX}` inside a `Style Setter` targeting `CornerRadius` (all four new primitives: `Button.primary/secondary/ghost`, `Border.pbChip`/`Button.pbChip`, `Border.posterTile`, `Border.floatingPanel`) crashed the app at startup — `System.InvalidCastException: Unable to cast object of type 'System.Double' to type 'Avalonia.CornerRadius'`, thrown from Avalonia's `PropertyStore`/style-application pipeline (not caught by `dotnet build`, which only validates XAML parses — this is a runtime-only failure). Fixed by declaring the three resources as genuinely `CornerRadius`-typed (`<CornerRadius x:Key="PbRadius">7</CornerRadius>`) and having `SkinService` assign `new CornerRadius(...)` instead of a raw double. Caught by actually launching the exe, not by `dotnet build` — confirms the project's own CLAUDE.md guidance that "0 Errors" isn't sufficient proof for XAML/resource-typed changes.
- **On-screen visual verification was not completed.** The app was confirmed to build clean, launch without crashing, and stay alive with a full memory footprint (612MB, consistent with a fully-loaded UI) after the fix — but interactive/visual confirmation via computer-use was blocked by an app-identity mismatch between the `dotnet run`/direct-exe dev build and the installed copy the computer-use tool's allowlist matches against. This is a tooling limitation, not a skipped step. If the user can visually confirm the showcase view and the five migrated overlays themselves, that's the one piece of the spec's Testing/Verification section not independently confirmed here.
- Font sourcing: Bebas Neue and Source Serif 4 were downloaded directly from the official `google/fonts` GitHub repository (OFL-licensed, redistribution permitted) rather than fabricated or approximated — both `.ttf` files and their `OFL.txt` license files are committed under `Assets/Fonts/`.
- Icon geometry: the 7 converted icons are genuine stroked-outline paths (Star computed via exact 5-point-star trigonometry; Copy/Circle-X/Save/Circle-Check/Folder-Open/Circle-Warning built from known Lucide icon shapes), not pixel-traced from the raster originals — see the Iconography section of the design doc for why that's actually more faithful to the agreed direction, not a shortcut.
