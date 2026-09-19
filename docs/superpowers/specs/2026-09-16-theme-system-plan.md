# Theme System — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-16-theme-system-design.md*

Real surface area found by survey (not assumed):
- Rename touches `Paperbunkr.App/Services/SkinService.cs`, `SkinPaths.cs`, `Models/SkinTheme.cs`,
  `Models/SkinSummary.cs`, `Data/Entities/AppSettings.cs` (`ActiveSkinKey`), the EF snapshot, and —
  found during survey, not in the design doc's own file list —
  `Paperbunkr.Plugins.Abstractions/Theme/IThemePlugin.cs` + `Paperbunkr.App/Plugins/PaperbunkrThemePlugin.cs`
  (`CurrentSkinKey`, whose own doc comment says "no dark-mode flag - skins aren't a binary," now
  false) and `Paperbunkr.App.Tests/SkinServiceTests.cs` + `WindowsElevenSkinTests.cs`.
- `MatrixRainOverlay` mirrors `ReaderPageVisualHandler`/`PageCanvas`'s already-shipped
  `CompositionCustomVisualHandler` pattern exactly: `ElementComposition.GetElementVisual` →
  `Compositor.CreateCustomVisual` → `ElementComposition.SetElementChildVisual` in
  `OnAttachedToVisualTree`, `SendHandlerMessage`/`OnMessage` for UI→render-thread data, explicit
  `SKImage`/`SKPaint` disposal already precedented there.
- Win32 interop (battery status) follows `Paperbunkr.Common/Win32/ShellRegister.cs`'s nested
  `Native` class + `[DllImport]` pattern.
- Reader-screen enter/leave (for the true-black auto-suspend) hooks `MainViewModel.CurrentScreen`'s
  existing `partial void OnCurrentScreenChanged(string value)` — `IsReader`/`IsPdfReader`/
  `IsBookReader` are the three reader screen keys (`"reader"`, `"pdfReader"`, `"bookReader"`).

## Step 1: Rename SkinService → ThemeService (mechanical)
**Files:** `Services/SkinService.cs`→`ThemeService.cs`, `Services/SkinPaths.cs`→`ThemePaths.cs`,
`Models/SkinTheme.cs`→`ThemeDefinition.cs`, `Models/SkinSummary.cs`→`ThemeSummary.cs`,
`Data/Entities/AppSettings.cs`, `ViewModels/PreferencesScreenViewModel.cs`,
`Views/Preferences/AppearanceSection.axaml`, `App.axaml.cs`,
`Plugins.Abstractions/Theme/IThemePlugin.cs`, `App/Plugins/PaperbunkrThemePlugin.cs`,
`App.Tests/SkinServiceTests.cs`→`ThemeServiceTests.cs`, `App.Tests/WindowsElevenSkinTests.cs`.
**What:** Class/type/method renames only (`SkinService`→`ThemeService`, `ApplySkin`→`ApplyTheme`,
`SkinSummary`→`ThemeSummary`, `SkinTheme`→`ThemeDefinition`, `SkinPaths`→`ThemePaths`,
`GetAvailableSkins`→`GetAvailableThemes`, `TryInstallSkin`/`OpenSkinsFolder` stay as-is per the
design doc — `.crpck` mechanism untouched). `AppSettings.ActiveSkinKey`→`ActiveThemeKey` (EF
`RenameColumn`, not drop/add — new migration, Step 2 below). `IThemePlugin.CurrentSkinKey`→
`CurrentThemeKey`, `PaperbunkrThemePlugin` reads `AppSettings.ActiveThemeKey`.
**Depends on:** none.
**Verify:** `dotnet build` clean (catches every missed reference — this rename touches ~10 files);
existing `SkinServiceTests`/`PreferencesScreenViewModelTests`/`WindowsElevenSkinTests` pass renamed,
behavior unchanged.

## Step 2: `mode` field, `ActiveThemeKey` migration, `RequestedThemeVariant` wiring
**Files:** `ThemeDefinition.cs` (add `Mode` enum property + `Category` string), the 5 existing
`Assets/Skins/*/theme.json` (add `"mode": "Dark"` to `default`/`cool_technical`/`vibrant_pop`/
`vintage_paperback`, `"mode": "Light"` to `windows_11`), `ThemeService.ApplyTheme`/
`ApplyPersistedSettings`, new EF migration (`RenameColumn` for `ActiveSkinKey`→`ActiveThemeKey`).
**What:** `ApplyTheme` reads `theme.Mode` and sets `Application.Current!.RequestedThemeVariant` to
`ThemeVariant.Light`/`ThemeVariant.Dark` alongside the existing `Pb*` resource writes.
**Depends on:** Step 1.
**Verify:** New `ThemeServiceTests` case — applying a Light-mode theme sets `RequestedThemeVariant`
to `Light`, a Dark-mode theme to `Dark`. Migration test (mirroring this codebase's existing
migration-safety tests) confirms an existing row's skin selection survives the rename with its
value intact.

## Step 3: True black + toggle-off restore
**Files:** `Data/Entities/AppSettings.cs` (`TrueBlackDark` bool), migration, `ThemeService.cs`,
`Views/Preferences/AppearanceSection.axaml` + its code-behind/ViewModel section in
`PreferencesScreenViewModel.cs`.
**What:** `ApplySkinResources`, after normal token writes, overwrites `PbBg`/`PbChrome`/
`PbSurface0-3` to `#000000` when `TrueBlackDark && mode == Dark`. New
`ThemeService.SetTrueBlackDark(bool)`: persists the setting, then re-runs `ApplyTheme(currentKey)`
(toggle off restores via the same normal apply path, not cached values — design doc's explicit
decision). New toggle row in `AppearanceSection.axaml`, visible/enabled only when
`SelectedTheme.Mode == Dark` — a computed property on `PreferencesScreenViewModel` bound to the
row's `IsVisible`.
**Depends on:** Step 2.
**Verify:** `ThemeServiceTests`: true-black-on overwrites the 5 tokens; toggling off round-trips
through `ApplyTheme` back to the theme's real JSON values (not stuck `#000000`).

## Step 4: New catalog — Daylight, Overcast, Maximum Contrast, color-blind-safe
**Files:** `Assets/Skins/daylight/theme.json`, `Assets/Skins/overcast/theme.json`,
`Assets/Skins/maximum_contrast/theme.json`, `Assets/Skins/colorblind_safe/theme.json` (all new),
`ThemeService.BuiltInSkinKeys` (add the 4 new keys), `.csproj` (confirm new `Assets/Skins/*/`
folders are picked up by the existing `avares://` embedded-resource glob — check how
`windows_11`'s folder is included before assuming a new folder needs no manual `.csproj` edit).
**What:** Exact hex values from the design doc's § New theme catalog / § Extended scope tables —
copy verbatim, don't re-derive. `maximum_contrast`/`colorblind_safe` ship with `mode: "Dark"`,
`daylight`/`overcast` with `mode: "Light"`.
**Depends on:** Step 2 (needs `mode` field to exist).
**Verify:** `ThemeServiceTests` — all 4 new keys load via `GetAvailableThemes()`/`LoadTheme()`
without throwing (same shape as the existing `WindowsElevenSkinTests` proof for a checked-in
theme). No pixel/contrast re-verification needed in code — the design doc's contrast math is the
source of truth here, already computed and reviewed.

## Step 5: Matrix theme + `MatrixRainOverlay`
**Files:** `Assets/Skins/matrix/theme.json` (new), `Views/MatrixRainOverlay.cs` (new,
`CompositionCustomVisualHandler` subclass, mirrors `ReaderPageVisualHandler.cs`'s shape), a small
host control wired into the app shell (find the shell root — likely `MainWindow.axaml`'s outermost
panel, confirm exact insertion point by reading it fresh, not assumed) with `IsVisible` bound to
"active theme key is `matrix`", `Services/BatteryStatusInterop.cs` (new, `GetSystemPowerStatus`
P/Invoke, mirrors `ShellRegister.cs`'s `Native` nested-class pattern).
**What:** Handler allocates `SKPaint`/`SKTypeface` once (reused across frames), draws falling
glyph columns from `theme.json`'s `matrixGlyphs` (fallback to default katakana set), advances by
elapsed wall-clock delta (not per-frame fixed step — frame-rate independence). `OnMessage` receives
screen-region rects from the host control (top-level content panel bounds, updated on layout
change) and issues `SKCanvas.ClipRect` (difference) before drawing. UI-thread `DispatcherTimer`
(30s) polls `BatteryStatusInterop`, writes a `volatile bool`; on a throttled→unthrottled
transition, sends a `SendHandlerMessage` telling the render-thread callback to call
`RequestNextFrameRendering()` again. `IsVisible`-false / detach stops the loop AND disposes
`SKPaint`/`SKTypeface`. Host control's visual tree presence excludes the Reader screens entirely
(not just `IsVisible=false` — literally absent from those screens' trees, per design doc).
**Depends on:** Step 4 (catalog entry must exist to select Matrix at all).
**Verify:** New `MatrixRainOverlayTests` (or a `[Fact]` group if a dedicated custom-draw-control
test file isn't this codebase's convention — check `CategoryDonut`/`ActivityHeatmap` for whether
they have dedicated test files before deciding): frame loop stops and resources are disposed on
detach/`IsVisible=false`; render-thread `RequestNextFrameRendering()` resumes correctly after a
simulated battery-flag transition; `GetSystemPowerStatus` is asserted never called from the
render-thread path. Manual on-screen check: rain visibly animates on Home/Library, absent on
Reader screens, doesn't render behind opaque panels (overdraw clipping working).

## Step 6: Reader auto-suspend for true black
**Files:** `ViewModels/MainViewModel.cs` (`OnCurrentScreenChanged`), `ThemeService.cs`.
**What:** New `ThemeService.SuspendTrueBlackForReader()`/`ResumeTrueBlackAfterReader()` — live
resource re-apply only (the normal `ApplyTheme(currentKey)` path for suspend, true-black overwrite
re-applied for resume), `AppSettings.TrueBlackDark` itself never touched. `OnCurrentScreenChanged`
calls suspend when `value` transitions into `"reader"`/`"pdfReader"`/`"bookReader"`, resume when
transitioning out of one of those three back to anything else.
**Depends on:** Step 3.
**Verify:** New test: with `TrueBlackDark` persisted `true`, simulating `CurrentScreen` transitions
into then out of each of the 3 reader keys leaves the DB value `true` throughout while live
resources go non-`#000000` → `#000000` → back.

## Step 7: Auto mode (FollowSystem/Scheduled) + scheduled crossfade + scheduled true black
**Files:** `AppSettings.cs` (`ThemeAutoMode`, `LastLightThemeKey`, `LastDarkThemeKey`,
`TrueBlackAutoHour`), migration, `ThemeService.cs`, `App.axaml.cs` (subscribe
`PlatformSettings.ColorValuesChanged` — new, safe handler per design doc, separate from the
detached `FluentAvaloniaTheme` one), `AppearanceSection.axaml` (mode selector + hour pickers).
**What:** `ApplyTheme` updates `LastLightThemeKey`/`LastDarkThemeKey` on every manual apply.
`FollowSystem` handler reads `PlatformColorValues.ThemeVariant` and defers the actual
`ApplyTheme(...)` call via `Dispatcher.UIThread.Post` (not synchronous inline — the reentrancy
guard from the design doc). `Scheduled` mode: periodic (minutes-scale) time check flips between
`LastLightThemeKey`/`LastDarkThemeKey`, running through a 300ms crossfade (full-window overlay
`Border` `DoubleTransition`) rather than instant — find this codebase's existing crossfade/fade
pattern (`avalonia-pro-max/motion` referenced `CrossFade`/`DoubleTransition` precedent, confirm
exact usage site before writing) and mirror it, don't invent a new one. `TrueBlackAutoHour` reuses
the same periodic check to flip `TrueBlackDark` at that hour, via the same `SetTrueBlackDark` from
Step 3.
**Depends on:** Steps 2, 3.
**Verify:** New test asserting no `ColorValuesChanged` subscription exists from the (still-removed)
`FluentAvaloniaTheme` side — a regression guard on the 2026-09-10 freeze fix — separate from a test
that Auto's own new subscriber fires correctly and defers via the dispatcher. `Scheduled` test
drives an injectable time source, not real `DateTime.Now` (match this codebase's existing testable-
service seam conventions — check how a comparable time-driven service in this codebase is tested
before picking a shape).

## Step 8: Accent color picker (bg-aware derivation)
**Files:** `AppSettings.cs` (`AccentOverrideHex`), migration, `ThemeService.cs`,
`AppearanceSection.axaml` (color picker control).
**What:** New `ThemeService.DeriveAccentTokens(string accentHex, Color bg)` — compares the picked
hex's luminance against `bg`'s luminance, adjusts HSL lightness in whichever direction increases
contrast, iterating to 4.5:1 (same target/method as the design doc's own Daylight/Overcast
contrast pass, generalized to run bg-aware). `ApplyTheme` calls this after normal resource writes
when `AccentOverrideHex` is set, overwriting `PbAccent*`/`PbGlow`.
**Depends on:** Step 2.
**Verify:** Two new tests — override applied under a Light theme (must darken to reach 4.5:1) and
under a Dark theme (must lighten) — the exact case a fixed "always darken" rule would fail.

## Step 9: Per-theme font / scrollbar / Mica tokens
**Files:** `ThemeDefinition.cs` (`DefaultFontFamily`, `ScrollbarWidth`, `ScrollbarThumbOpacity`,
`WindowBackdrop` fields), `matrix/theme.json` (sets `defaultFontFamily`), `ThemeService.cs`
(`ApplyFontResource` precedence change), new `ControlTheme` for FluentAvalonia `ScrollBar` (find
where control themes currently live in `App.axaml` before adding a new one), `App.axaml.cs`
(`MainWindow.TransparencyLevelHint` driven by `WindowBackdrop`, mirroring `OverlayHostWindow.cs`/
`SplashWindow.axaml`'s existing use of the same hint — read both before writing this to match the
established pattern exactly, not reinvent it).
**What:** `ApplyFontResource` precedence: `AppSettings.SelectedFontFamily` (explicit override) wins
if set, else active theme's `DefaultFontFamily`, else existing hardcoded default. Scrollbar/backdrop
fields are additive — every theme that omits them keeps current behavior unchanged.
**Depends on:** Step 2 (Matrix, Step 5, needs to exist to set `defaultFontFamily`).
**Verify:** Font precedence test (3 cases: explicit override set, theme default only, neither).
Backdrop test: a theme without `WindowBackdrop` leaves the window's current hint untouched.

## Step 10: Appearance UI final assembly
**Files:** `Views/Preferences/AppearanceSection.axaml`, `ViewModels/PreferencesScreenViewModel.cs`.
**What:** Wire every new control added across Steps 3/7/8/9 into the existing flat theme-card grid
screen — no layout restructuring, per the design doc's explicit "keep today's arrangement"
constraint. `Skins`→`Themes` `ObservableCollection<ThemeSummary>` now yields 10 cards.
**Depends on:** Steps 3, 4, 5, 7, 8, 9 (assembles everything above into one screen).
**Verify:** `PreferencesScreenViewModelTests` extended for the new bound properties/commands.
Manual on-screen pass: all 10 theme cards selectable, true-black toggle visibility follows active
theme's mode, Auto mode controls, accent picker, and the reader-screen suspend/resume are all
exercised live — per this project's established "no UI automation without asking first" rule
([[feedback_no_unauthorized_ui_automation]]), this step is a manual click-through, ask the user to
drive it rather than scripting one.

## Step 11: Full test suite + plugin API check
**Files:** none new — verification step.
**What:** Run the full `Paperbunkr.App.Tests`/`Paperbunkr.Data.Tests` suites (not a filtered subset
— this codebase's own established gotcha: `DatabasePathOverride`/`SkinPaths`-style statics only
show cross-test races under the full run). Confirm `IThemePlugin.CurrentThemeKey` rename didn't
break the one real consumer (`PaperbunkrThemePlugin`) and that no plugin sample/doc still references
`CurrentSkinKey`.
**Depends on:** all prior steps.
**Verify:** `dotnet test` full solution, 0 failures. Grep for any remaining `SkinService`/
`ActiveSkinKey`/`CurrentSkinKey` reference left unrenamed.
