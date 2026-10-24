# Splash redesign + light/dark theme support

Date: 2026-10-22 · Status: approved (user, 2026-10-22) · Supersedes the visual part of
`2026-09-09-startup-onboarding-whats-new-design.md` Decision 2 (the startup sequence itself is unchanged).

## Problem

`SplashWindow` is a fixed near-black window (`#0A0B0D`) with hardcoded text and track colours. Only the emblem glow and the
progress gradient follow the theme (its accent). On a light theme (Daylight, Overcast, Newsprint, Windows 11, the light
variants) the user gets a dark splash and then a light app. It also reads the active theme key straight from the database
before the integrity check and migrations run, so after an update it often falls back to the built-in palette, and with
Auto mode (Follow system / Scheduled) the stored key can be the wrong one for this launch.

## Decisions (grilling rounds 1-2)

| # | Decision |
|---|---|
| Q1 | Layout **B, wordmark lockup**: emblem left with its glow, "PAPERBUNKR" in the display font beside it, tagline under it; status bottom-left, version bottom-right, progress as a hairline along the bottom edge. |
| Q2 | The splash uses the **whole theme palette** (surface, text, muted, faint, border, accent, accent text, accent soft), not just the accent. |
| Q3 | Under Auto mode the splash shows **the theme the app will open with** (Follow system → the OS light/dark preference; Scheduled → the time-of-day pick). |
| Q4 | Colours come from a **saved snapshot file** (`splash-theme.json`), never the database. No file (fresh install) or unreadable file → the built-in Default palette. |
| Q5 | Motion unchanged: entrance, breathing pulse, ambient dots (recoloured), Reduce motion / "Splash ambient motion" still turn them off. |
| Q6 | Rounded corners (`RadiusLg` of the theme), 1 px border, soft shadow, so a light splash has an edge on a light desktop. |
| Q7 | Size unchanged: a 480×340 card. |
| Q8/9 | Name + tagline; tagline "Your comics. Your machine" (changed by the user during implementation from "Comics, manga and books"). |
| Q10 | The accent glow stays on light themes (it reads as a soft tint). |
| Q11 | Matrix gets green dots, not the rain. |
| Q12 | Status messages unchanged. |
| Q13 | The ~150 ms fade-out hand-off unchanged. |

## Approach

Chosen: **snapshot file** written by `ThemeService`, read by the splash. Rejected: reading the DB more carefully (fails in
exactly the post-update case), and applying the full theme to `Application.Resources` before the splash (needs the DB
before migrations; risks the old dead-splash crash).

## Units

### `SplashPalette` (record, `Services/Splash/`)
`IsLight`, `Surface`, `Text`, `TextMuted`, `TextFaint`, `Border`, `Accent`, `AccentText`, `AccentSoft` (Avalonia `Color`),
`CornerRadius` (double). `SplashPalette.Default` = the built-in "default" theme's values (dark). `FromTheme(ThemeDefinition)`
maps `Colors.Bg/Text/TextMuted/TextFaint/Border/Accent/AccentText/AccentSoft` + `RadiusLg` + `Mode`.

### `SplashThemeSnapshot` (record) + `SplashThemeStore`
JSON next to the database (`Path.GetDirectoryName(GetDefaultDatabasePath())/splash-theme.json`, same pattern as
`insights.json` / `keymap.json`). Shape:

```json
{ "version": 1, "autoMode": "Off|FollowSystem|Scheduled", "darkHour": 20, "lightHour": 7,
  "current": { "isLight": false, "surface": "#0A0B0D", "text": "...", "textMuted": "...", "textFaint": "...",
               "border": "...", "accent": "...", "accentText": "...", "accentSoft": "#29C9803F", "cornerRadius": 14 },
  "light": { ... } | null, "dark": { ... } | null }
```

`Load()` returns null for a missing, unreadable or malformed file (never throws). `Save(snapshot)` writes to a temp file and
moves it over the old one; failures are logged and swallowed. Constructed with no path → in memory (tests).

### `SplashThemeChooser.Choose(snapshot, PlatformThemeVariant? os, int hour)` (pure)
- null snapshot → `SplashPalette.Default`.
- `Off` → `current`.
- `FollowSystem` → `light`/`dark` by `os` (unknown OS → `current`); missing target → `current`.
- `Scheduled` → dark period uses the same rule as `ThemeService.CheckScheduledSwitch` (shared static helper
  `ThemeService.IsDarkPeriod(hour, darkHour, lightHour)`), missing target → `current`.

### `ThemeService` writes the snapshot
`SaveSplashSnapshot()`: reads `AppSettings`, resolves the current theme (`Current` if set, else `ResolveTheme(ActiveThemeKey)`),
and the Auto targets `AutoSwitchTarget(settings, true/false)` through `ResolveTheme` (with overrides and True Black, so the
splash matches the real look). A target that fails to resolve is written as null. Triggers:
- `ResolvedThemeApplied` → deferred one dispatcher tick (so `ApplyTheme` has saved `ActiveThemeKey`/`LastLight/DarkThemeKey`),
  coalesced so a burst writes once. This covers startup apply, manual picks, Auto switches, override edits and True Black.
- `SetThemeAutoMode`, `SetThemeScheduleHours` → directly.
The production constructor uses the file store; the test constructor gets none unless a test passes one, so tests never touch
the developer's real file.

### `SplashWindow`
- Window: `Background="Transparent"`, `TransparencyLevelHint="Transparent"`, `SystemDecorations="None"`, 512×372 (the
  480×340 card plus a 16 px margin for the shadow). The same pattern as `OverlayHostWindow`.
- Card: `Border` with `{DynamicResource PbSurface1Brush}`, `PbBorderBrush`, 1 px, `CornerRadius` from the palette,
  `ClipToBounds`, a fixed shadow (not animated).
- Content per Q1/Q8: emblem + glow (left), `TextBlock.pbTextHero` "PAPERBUNKR" (`PbTextBrush`), `pbTextMeta` tagline,
  `pbTextMeta` status, `pbTextCaption` version (`PbTextFaintBrush` is allowed only for icons/dividers, so the version uses
  muted), progress bar 3 px along the bottom edge.
- The constructor picks the palette (`SplashThemeChooser` with the OS variant from `Application.Current.PlatformSettings`
  and the local hour) and writes the `Pb*Color`/`Pb*Brush` keys above into the **window's own** `Resources`, so the XAML
  uses tokens only and no hex. The glow and progress gradients are recoloured in code as today; ambient dots take the accent.
- Old `TryGetThemeAccentColors` (DB read) removed.

## Errors and fallbacks
Every read is best-effort: no snapshot, corrupt JSON, a bad colour string or a missing platform-settings object all fall
through to `current`, then to `SplashPalette.Default`. A failed snapshot write is logged and never interrupts a theme apply.

## Testing
- `SplashThemeChooserTests`: Off; FollowSystem light/dark/unknown; Scheduled inside/outside dark period incl. wrap past
  midnight and `darkHour <= lightHour`; missing light/dark target → current; null snapshot → Default.
- `SplashThemeStoreTests`: round-trip; missing file; malformed JSON; bad colour value → null.
- `ThemeServiceSplashSnapshotTests`: applying Daylight writes a light `current`; Auto mode + hours are written; light/dark
  targets resolved.
- `SplashWindowTests` (existing) still pass; new: the window's resources carry the chosen palette (light snapshot →
  light surface).
- Lint: `XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`.
- Render PNGs of the card in Default, Daylight, Matrix, Maximum Contrast; then the user checks on screen.

## Out of scope
Matrix rain on the splash, a startup checklist, window size/position changes, startup-order changes.
