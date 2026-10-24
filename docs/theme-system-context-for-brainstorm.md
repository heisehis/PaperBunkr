# Paperbunkr theme system: context for brainstorming

Written 2026-10-12 from the code and specs as they stand. It is meant to be pasted into another chat as a standalone briefing. Where something is a claim from a spec rather than something checked in code or on screen, it says so.

## 1. What Paperbunkr is

A desktop comic/manga/book library and reader, a from-scratch rewrite of ComicRack CE. Avalonia 12 on .NET 10, with FluentAvalonia supplying native control chrome, CommunityToolkit.Mvvm for MVVM, and SQLite through EF Core. It is Windows-first. The app shell is dark by default, and the identity is "dark, near-black, amber accent, serif body text".

ComicRack CE has no user-facing theme picker. It has a compiled `Default`/`Dark` enum and hardcoded color tables, with no installable skins and no Light/Dark auto-follow. So everything below is a deliberate addition, not CE parity.

## 2. Architecture in one page

**Single mechanism.** A theme is a `theme.json` file. Built-in themes are embedded resources under `Assets/Skins/<key>/theme.json`. Users can also install a `.crpck`, which is a ZIP holding the same `theme.json`, into `%AppData%\Paperbunkr\skins\`. The built-in default goes through the same code path as any installed theme.

**`ThemeService`** (`Services/ThemeService.cs`, about 1,170 lines) is the one service that owns all of the following:
- Loading a theme, applying it, and persisting the choice.
- Auto mode (OS-follow or time-scheduled).
- True black, the accent override, font precedence and reduced motion.
- Glow tiers, density presets and Matrix rain enablement.

The class and DB column used to be called `SkinService`/`ActiveSkinKey`. They were renamed to `ThemeService`/`ActiveThemeKey` in the 2026-09-16 theme-system work. The installer methods are still named `TryInstallSkin` and `OpenSkinsFolder`, and the asset folder is still `Assets/Skins`.

**How a theme reaches the UI.** `ApplyThemeResources` overwrites `Application.Current.Resources["PbXxxColor"]` and `["PbXxxBrush"]` wholesale with new `Color` and `SolidColorBrush` objects. Every consuming XAML site uses `{DynamicResource Pb*}`, which is why a switch recolors live with no restart. A static fallback copy of the tokens lives in `Styles/AppTokens.axaml`. It only matters before the first apply, which runs on every startup via `ApplyPersistedSettings`.

**Light/Dark variant.** `theme.json` has `mode: "Light" | "Dark"`, and applying a theme sets `Application.RequestedThemeVariant` from it. This is what makes FluentAvalonia's native chrome (ComboBox popup, ScrollBar, CheckBox parts) follow the theme. Before the theme-system work the variant was hardcoded to Dark, so the one light skin, `windows_11`, had light tokens on dark native chrome.

**Things a `theme.json` string cannot do, handled in code instead:**
- `RequestedThemeVariant` is not a token, so it is set from `mode`.
- `BoxShadows` cannot embed a `DynamicResource`, so `PbGlowRing`, `PbGlowPulseRing` and `PbGlowRingInset` are rebuilt in C# from the theme's glow color on every apply.
- FluentAvalonia's own accent system needs `FluentAvaloniaTheme.CustomAccentColor` pushed on each apply.

## 3. Token vocabulary (what a theme.json controls)

```
name, mode (Light|Dark), category? (free tag, unused by UI), defaultFontFamily?,
windowBackdrop? (None|Mica|Acrylic), scrollbarWidth?, scrollbarThumbOpacity?,
matrixGlyphs? (string[]), spacingUnit (4), radius (7), radiusSm (5), radiusLg (14),
icons {} (reserved, nothing consumes it),
colors:
  bg, chrome, border, text, textMuted, textFaint,
  accent, accentText, accentSoft, badge, badgeText, success,
  chartBlue, chartViolet,
  surface0..surface3   (elevation scale: 0 recessed .. 3 popovers/modals; bg==surface1, chrome==surface2 by convention),
  glow, heroGradientStart, heroGradientEnd,
  eraPlatinum? eraGolden? eraSilver? eraBronze? eraModern?   (optional, defaulted per mode)
```

Every field after the original set is optional and additive. An old or third-party `theme.json` that omits them still loads, falling back to the default values.

**Tokens not in `theme.json`, defined once in `App.axaml`/`AppTokens.axaml`, so they do not vary per theme:**
- `PbDanger` and its soft variant. The same red is used in every theme.
- `PbBadgeSoft` and `PbSuccessSoft`.
- Font-size scale (`PbFontSize*`) and icon-size scale (`PbIconSize*`).
- Motion durations (`PbMotionFast/Standard/Large/Slow`) and easing.
- `PbRadiusChip`, a squircle radius. The user has said no oval pills.
- The Home masthead sky gradients (`PbSky*`), which are theme-independent.
- The Home seasonal tints (`PbSeason*`).

**Derived at apply time:**
- `PbHeroScrimColor` is `#59000000` on dark themes and `#1F000000` on light ones.
- `PbThemeIsLight` is a bool resource.
- Five era colors (`PbEraPlatinum/Golden/Silver/Bronze/Modern` plus `...Soft` at about 18% alpha) drive the Continuity timeline. There is a dark default set and a deeper light default set. Modern follows the accent unless the theme sets it.

## 4. Built-in themes (10)

| key | mode | bg | text | accent | notes |
|---|---|---|---|---|---|
| default | Dark | #0A0B0D | #ECE7DB | #C9803F amber | the Paperbunkr identity |
| cool_technical | Dark | #0B0E14 | #E4E7EC | #5EA7F0 | |
| vibrant_pop | Dark | #0D0B12 | #F5F0E8 | #E85D4E | |
| vintage_paperback | Dark | #12100D | #EDE0C8 | #8C3B2E | |
| windows_11 | Light | #F3F3F3 | #1A1A1A | #0078D4 | radius 8, `windowBackdrop: Mica`. Real Fluent tokens. Its accent on bg is 4.08:1, a known failure of the 4.5:1 text bar that was left as is. |
| daylight | Light | #F7F3EC | #2A2620 | #985A27 | warm paper, same amber family as default |
| overcast | Light | #EEF0F3 | #22262C | #3469A6 | cool gray-blue |
| matrix | Dark | #000000 | #33FF66 | #33FF66 | category `matrix`, monospace default font, code-rain overlay |
| maximum_contrast | Dark | #000000 | #FFFFFF | #FFD24D | WCAG AAA, 7:1 or better |
| colorblind_safe | Dark | #0A0B0D | #ECE7DB | #4FA3D9 | category `accessibility`, Okabe-Ito palette (blue/bluish-green/orange; chart colors #56B4E9 and #CC79A7) |

The Daylight, Overcast, Maximum Contrast and colorblind-safe values were contrast-computed with the WCAG relative-luminance formula, not eyeballed. Targets: 4.5:1 for text, 3:1 for large text and non-text UI. `PbAccentBrush` is used as literal text foreground in places, so `accent` itself was held to 4.5:1. That check caught both first-draft light accents at about 3.8:1.

## 5. Settings and behaviors on top of the picker

All live in `AppSettings`, a singleton DB row. The UI is Preferences → Appearance.

- **Active theme** (`ActiveThemeKey`). Selecting a card applies it instantly. Each manual apply also records `LastLightThemeKey` or `LastDarkThemeKey`, according to the theme's mode.
- **Auto mode** (`ThemeAutoMode`: Off, FollowSystem or Scheduled). It switches between the last-picked light theme and last-picked dark theme.
  - FollowSystem subscribes to `PlatformSettings.ColorValuesChanged` and defers the apply one dispatcher tick with `Dispatcher.UIThread.Post`.
  - Scheduled polls every 5 minutes against a dark hour and a light hour (defaults 20 and 7), wrapping midnight. It fires `ScheduledThemeCrossfadeRequested`, and the UI layer does a 300 ms opacity dip.
- **True black** (`TrueBlackDark`). On a Dark-mode theme it overwrites `bg`, `chrome` and `surface0..3` to `#000000`. It is a single global boolean, not per-theme variants, modeled on Mihon/Komikku.
  - Turning it off re-runs `ApplyTheme` instead of restoring cached values.
  - It auto-suspends while a Reader screen is active (live resources only, the persisted setting is untouched). Page art loses contrast on pure black.
  - The spec also describes an OLED pixel-shift timer for static chrome. I did not verify whether it was built.
  - There is also `TrueBlackAutoHour`, which turns true black on at a set hour using the same scheduler tick.
- **Accent color override** (`AccentOverrideHex`). It is global, not per theme. It derives `accentText` by nudging lightness in whichever direction raises contrast against the active theme's bg, up to 4.5:1. The first draft always darkened, which is wrong on dark backgrounds. It re-derives on every theme switch.
- **Font.** Precedence is the user's explicit `SelectedFontFamily`, then the theme's `defaultFontFamily` (only Matrix sets one), then the bundled default Source Serif 4. A separate display font, Bebas Neue, is non-overridable and used for hero text. The Appearance section has a live preview line.
- **Reduced motion.** It zeroes the four `PbMotion*` durations, so everything bound to them becomes instant.
- **Matrix rain** (`MatrixRainEnabled`). It is a `CompositionCustomVisualHandler` that runs on the render thread with Skia, on a single overlay mounted at shell level beneath screens. It never mounts in the reader screens.
  - It pauses under reduced motion and when the battery is below 20% and unplugged.
  - The user's first on-screen use found it never rendered, because every full-area host was marked as an opaque backplate. That was fixed, the attached property stays available but is unused, and rain alpha is now capped at 140/255.
- **Cover/UI cosmetics**, a separate set of Appearance toggles that read theme tokens:
  - Glow tier (Off/Subtle/Normal/Vivid), which rebuilds `PbGlowRing`.
  - Density preset (Compact/Comfortable/Spacious), which writes `PbListRowPadding`, `PbDetailsRowPadding` and `PbSidebarItemPadding`.
  - Binding spine, read-progress ring, cover fade-in and tooltips, tile checkboxes, reading-list mosaic and splash ambient motion.
  - Detail hero backdrop and per-series accent color.
  - Home sections and a seasonal flourish.
- **Home light-theme sky.** When the active theme is light, the Home masthead swaps the cover wall for a time-of-day sky gradient. The Detail hero stays light-on-dark in every theme by design.

## 6. Appearance UI today

Preferences → Appearance, top to bottom: THEMES, THEME OPTIONS, FONT, COVER COSMETICS, DETAIL SCREENS, HOME, INTERFACE.

- **THEMES** is a wrapping grid of mini-mockup preview cards. Each card paints a fake screen in that theme's own colors: a title bar from `chrome` with an accent dot, a `surface3` sidebar sliver, and `textMuted` content lines on a `bg` card. The active card gets a ring in the app's current accent (not the card's own accent) plus a checkmark badge. An Install… button and an Open Themes Folder button sit in the group header. The grid is flat, with no grouping by Light/Dark or category.
- **THEME OPTIONS** has True black, Matrix rain (shown only under Matrix), Auto switch, the dark and light hours (shown only in Scheduled), Auto-enable true black at, and Accent color override.
- A per-setting search index jumps to a row by anchor.

## 7. Related theming-adjacent systems that do NOT share the theme tokens

- **Book reader themes** (`BookTheme` enum: Light, Dark, Sepia, MatchAppSkin, OledBlack, HighContrast). Content and chrome colors come from a hardcoded table in `BookThemeBrushes.cs`. Only `MatchAppSkin` follows the app theme, and its dark values are hardcoded to the default theme's, not read from the live tokens.
- **Comic reader.** `ReaderScreen.axaml` has its own separate `Reader*Brush` family, which was explicitly excluded from the skin conversion. It also has background textures (`ReaderBackgroundTextures`) and an ambient mode.
- **Splash window.** It reads the persisted theme's accent and accent-text at startup, best-effort, before the main app resources exist.
- **Insights charts.** `InsightsChartPalette` maps reading states and content ratings to theme tokens (`Success`, `ChartBlue`, `TextMuted` and so on) so they follow the theme. The charts are custom-drawn controls that read brushes at render time.
- **Event Map / Continuity map.** A custom-drawn edge layer reads theme colors at render time and drops its caches on a theme change.
- **Publisher logos and brand marks.** They are rasters and are never tinted. A light plate sits behind them on light themes.

## 8. Known gaps and debt (checked in code unless noted)

1. **Hardcoded colors still exist.** `AppTokens.axaml` holds about 50 fallback values by design, but real literals remain in `PluginScreen.axaml` (about 15), `Primitives.axaml` (about 15), `DetailHero.axaml` (about 10, intentional), `SplashWindow.axaml` (about 8), `MainWindow.axaml`, `LibraryScreen.axaml` and a few more. I have not audited which of these matter under light themes.
2. **Scrollbar tokens are schema-only.** `scrollbarWidth` and `scrollbarThumbOpacity` are parsed and written to `PbScrollbarWidth`/`PbScrollbarThumbOpacity`, but no `ControlTheme` consumes them. The reason given was the risk of rewriting FluentAvalonia's lookless ScrollBar without being able to look at it.
3. **`icons: {}` is unconsumed.** `GetIcon` exists with a cache, but nothing in the UI calls it. Per-theme icon packs need art, not code.
4. **`category` is unused by UI.** It exists so Matrix and accessibility themes can be filtered later without a schema change.
5. **`windows_11` accent fails 4.5:1.** The user chose to leave it as it was.
6. **PbDanger and the chart/era defaults are not theme-aware.** The same red is used on every theme, including Maximum Contrast and the light themes. I have not checked its contrast on the light backgrounds.
7. **The `.crpck` authoring story is deliberately thin.** You can install one by hand. There is no export, no editor, and no in-app authoring UI. No override-file mechanism exists, such as a user-supplied `Pb*` override. The user explicitly deferred both, twice. Contrast auto-correction of third-party themes was rejected on purpose.
8. **Theme switch cost.** Applying a theme rewrites about 60 resources. A perf pass in the same period suspected an unneeded full theme reload on every reader enter/exit (`SetTrueBlackReaderSuspend` reloads and rewrites everything), and it was unconfirmed.
9. **Verification status.** The theme system was seen on screen by the user at least once (Matrix rain, glow ring and splash fixes came from that session). Several later items are recorded as not verified on screen, including light-skin era colors, light-theme rendering of several newer screens (Continuity, Event Map, Library redesign, Reading Lists), and the Reader auto-suspend path.

## 9. Hard constraints and gotchas for any redesign

- **Switching is by resource overwrite, not by `ThemeDictionaries`.** New tokens must be written by `ThemeService` and consumed with `DynamicResource`. A `StaticResource` consumer will not update.
- **Do not mutate resources synchronously inside a popup-open or `ColorValuesChanged` callback.** A past bug froze the UI on every ComboBox open because FluentAvalonia's handler rewrote high-contrast resources mid-`Popup.Open()`. `FluentAvaloniaTheme` is permanently detached from that event. Any new auto-follow logic must defer one dispatcher tick. A regression test asserts no extra subscription appears.
- **`BoxShadows`, gradient strings and anything parsed from a single string cannot embed `DynamicResource`.** Those need code that rebuilds them on apply, as the glow ring does.
- **Avalonia keyframe animations targeting `RenderTransform` crash.** For continuous animation, use the composition custom visual handler, not a `DispatcherTimer` calling `InvalidateVisual`.
- **No hex literals for UI colors.** The standing rule is to use the tokens so skins react. The Avalonia review checklist caught real defects of this kind.
- **Multi-type XAML style selectors that set `FontFamily` fail to compile**, so each control type needs its own style block. This matters for any per-theme typography work.
- **A running app instance locks the output DLLs.** The dev DB is shared across worktrees, and the user dislikes force-killing the app.
- **UI-thread ownership.** `RequestedThemeVariant` can only be set on the dispatcher thread, which affects test design.
- **The user dislikes** oval pill shapes (use the squircle chip radius), infinite scroll, and unsolicited commit talk. Process rule for this project: design work goes through a grilling pass that asks the whole decision frontier in rounds, each question with a recommended answer.

## 10. Directions I would put on the table for the brainstorm

These are my suggestions, not decisions or commitments.

1. **Semantic token layer.** Today a token is a raw color name. Splitting it into roles (surface, on-surface, interactive, danger, warning and info, plus state colors) would let danger/warning/info vary per theme and would give plugins and screens a smaller, stable contract.
2. **Theme authoring and sharing.** Export `.crpck`, a live editor with contrast checking, and import or share. This was deferred, not rejected.
3. **Light/dark pairing.** A theme "family" (Daylight plus a matching dark) instead of Auto mode's independent last-light and last-dark keys, and a real grouping in the picker (the `category` field is already there).
4. **Finish the unfinished schema.** Scrollbar tokens, per-theme icon sets, `windowBackdrop` beyond Mica, and maybe per-theme typography scale and corner-style tokens.
5. **Reader and chrome unification.** Fold `BookTheme`, the comic reader's own brushes, and the app theme into one system, or define a clean boundary.
6. **Hardcoded-color burn-down** with an automated check, for example a test that fails on new hex literals in XAML outside an allow-list.
7. **Accessibility.** A higher-contrast light theme (Maximum Contrast has no light twin), a color-blind-safe light theme, and theme-aware danger and warning colors.
8. **Cost and smoothness.** A diff-only apply that skips unchanged tokens, plus a cross-fade for manual switches and not just Scheduled ones.
9. **Cover- or content-derived color.** There is already a per-series accent from the dominant cover color and a Home spotlight accent sampler. A "dynamic theme" mode could generalize that.
10. **Preview before apply.** Hover or temporary preview was explicitly out of scope earlier.

## 11. Where to read more in the repo

- `docs/superpowers/specs/2026-09-16-theme-system-design.md` is the main design (also `-plan.md`). It contains the review history and the full reasoning behind each decision.
- `docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md` is the original mechanism.
- `docs/superpowers/specs/2026-09-07-appearance-redesign-design.md` is the picker and Appearance layout.
- `docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md` and `-2-design.md` cover glow tiers, density, spine and ring.
- `docs/superpowers/specs/2026-09-28-home-cosmetics-design.md` covers the light-theme sky, scrim token and seasons.
- `docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md` covers the era color tokens.
- Code: `src/Paperbunkr.App/Services/ThemeService.cs`, `Models/ThemeDefinition.cs`, `Styles/AppTokens.axaml`, `Assets/Skins/*/theme.json`, `Views/Preferences/AppearanceSection.axaml`, `Views/MatrixRainOverlay.cs`, and `ThemeServiceTests.cs`.
