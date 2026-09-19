# Theme System Design

**Date:** 2026-09-16
**Status:** Approved by user (2026-09-17), including both open questions (Q13: `.crpck` export/
authoring UI deferred; Q14: `TrueBlackDark` auto-suspends in Reader screens). Ready for
`writing-plans`.
**Supersedes-in-part:** `docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md` (the mechanism this document renames/extends; that spec's build/UI history stays valid, see [[project_paperbunkr_preferences_skin_system]])

## Summary

Paperbunkr's existing `SkinService`/`theme.json` mechanism becomes the app's **Theme** system:
renamed in place (not a parallel system), extended with a `mode` (`Light`/`Dark`) that now
actually drives `Application.RequestedThemeVariant` per-theme (today it's hardcoded
`RequestedThemeVariant="Dark"` app-wide, so the one existing light skin, `windows_11`, never got a
correct native-chrome light variant). Adds two new light themes (Daylight, Overcast), a Matrix
dark theme with an animated code-rain background layer, and a global "true black" (OLED) toggle
for whichever dark theme is active — the Mihon/Komikku-style mechanism the user asked for
explicitly.

The old CE-style "install a custom `.crpck` visual package" idea is explicitly **not** touched or
built out further here — that's deferred to a future, separate "skins" initiative per the user's
own framing during design. The `.crpck` install path in code stays as-is, just renamed alongside
everything else; nothing about its behavior changes.

## Background / why this shape

- CE (the original ComicRack) has no installable skin/theme package at all — just a compiled
  `enum Themes { Default, Dark }` and hardcoded color tables (`ThemeManager.cs`,
  `DarkThemeColorTable.cs`), plus raw `uxtheme.dll` calls to force Windows' own chrome dark since
  ".NET apps always render Light chrome regardless of app preference" (CE's own comment). No
  Matrix/novelty theme, no OS-theme auto-follow. Paperbunkr's installable `.crpck` skin concept was
  already a deliberate deviation from CE before this work; nothing here is a CE-parity claim.
- Paperbunkr hits the *same* native-chrome problem CE describes, via a different mechanism:
  `App.axaml:8` hardcodes `RequestedThemeVariant="Dark"`, so FluentAvalonia's own theme
  dictionaries (native `ComboBox` popup chrome, `ScrollBar`, unthemed `CheckBox` parts) never
  follow whichever skin's `Pb*` colors are active. The existing `windows_11` skin ships light
  `Pb*` tokens but its native FluentAvalonia chrome has always rendered dark — unverified on
  screen until now.
- That hardcoded value isn't just an oversight: `FluentAvaloniaWorkarounds.cs:37-40` cites "single
  fixed visual identity, RequestedThemeVariant=Dark" as part of why it's safe to permanently detach
  `FluentAvaloniaTheme` from `IPlatformSettings.ColorValuesChanged` — the fix for a real UI-freeze
  bug (2026-09-10, every `ComboBox`/`AutoCompleteBox` dropdown froze the UI thread, see
  [[project_paperbunkr_dropdown_freeze_fluentavalonia]]). Traced before committing to a dynamic
  variant: the detach in `App.axaml.cs:35,42` is unconditional and never re-subscribes on a variant
  change, so switching `RequestedThemeVariant` at runtime does **not** reopen that freeze. Safe to
  proceed. (The workaround's own comment describing "fixed theme" goes stale once this ships —
  a wording fix only, not a behavior change, left for the implementation plan.)

## Data & schema

`theme.json` schema gains two fields:

```json
{
  "name": "Daylight",
  "mode": "Light",
  "category": null,
  "colors": { ... unchanged ... },
  "spacingUnit": 4,
  "radius": 7,
  "radiusSm": 5,
  "radiusLg": 14,
  "icons": {}
}
```

- **`mode`** (`"Light"` | `"Dark"`, required): drives `Application.RequestedThemeVariant`. Exactly
  two values — Matrix is `"Dark"` (near-black background, so FluentAvalonia's Dark native chrome
  is the correct match; FluentAvalonia has no third "Matrix" variant, and none is needed since
  every native-chrome color still comes from the Dark theme dictionary while all `Pb*` tokens carry
  Matrix's own green palette).
- **`category`** (string, optional, backend-only): a free-form future grouping tag, e.g.
  `"matrix"`. Not read by any UI in this phase — exists so Matrix (and future novelty themes) can
  be filtered out of the picker later without another schema migration. The Appearance screen's
  picker layout does not change in this phase: same flat card grid as today, just more cards.

Rename (mechanical, behavior-preserving where renamed, one column rename where persisted):

| Before | After |
|---|---|
| `SkinService` | `ThemeService` |
| `SkinTheme` (C# model) | `ThemeDefinition` |
| `SkinSummary` | `ThemeSummary` |
| `SkinPaths` | `ThemePaths` |
| `AppSettings.ActiveSkinKey` (DB column) | `AppSettings.ActiveThemeKey` (EF `RenameColumn`, data preserved) |
| `.crpck` file format/extension | unchanged — still `.crpck`, now called a "theme package" in UI copy only |

New on `AppSettings`: `TrueBlackDark` (`bool`, default `false`), `ThemeAutoMode`
(`Off`/`FollowSystem`/`Scheduled`, default `Off`), `LastLightThemeKey`/`LastDarkThemeKey`
(`string?`, auto-updated on every manual theme apply), `AccentOverrideHex` (`string?`, default
`null`), `TrueBlackAutoHour` (`int?`, default `null`) — see § Extended scope for what each drives.

New optional fields on `theme.json`: `defaultFontFamily` (`string?`), `scrollbarWidth` (`double?`),
`scrollbarThumbOpacity` (`double?`), `windowBackdrop` (`"None"|"Mica"|"Acrylic"`, default `None`) —
all additive, every existing theme that omits them keeps today's exact behavior.

## Variant switching + true black

`ThemeService.ApplyTheme(key)` (renamed from `ApplySkin`) reads the theme's `mode` and sets
`Application.Current.RequestedThemeVariant` to `ThemeVariant.Light` or `ThemeVariant.Dark`
accordingly, in addition to its existing `Pb*` resource writes and `FluentAvaloniaTheme.CustomAccentColor`
push. Runs both on explicit theme change and on `ApplyPersistedSettings` at startup.

True black: after `ApplySkinResources` normally sets `PbBg`/`PbChrome`/`PbSurface0-3`, one more
conditional step — if `TrueBlackDark` is on and the active theme's `mode == Dark`, overwrite those
five tokens to `#000000`. A single global boolean, not per-theme variant entries (matches
Mihon/Komikku's actual mechanism): it applies to whichever dark theme is currently active,
including Matrix (whose surfaces are already near-`#000000`, so the visible change there is
minimal/none — expected, not a bug).

**Toggling off** (gap caught in spec review, credit: external review round — the original draft
never said what "off" does): does **not** attempt to restore cached original values from memory.
Instead it calls `ThemeService.ApplyTheme(currentThemeKey)` again — the exact same method the
initial apply already goes through — which re-reads `theme.json` fresh and re-runs the normal
(non-true-black) resource write. Reuses an existing, already-tested code path instead of adding a
second one to keep in sync.

UI: a new toggle row in Preferences → Appearance, enabled/visible only when the active theme's
`mode == Dark` (no-op under a Light theme, so hidden/disabled there rather than shown doing
nothing).

**Reader auto-suspend (user-confirmed).** `TrueBlackDark`'s `#000000` override must not apply while
a Reader screen (comic/book/PDF canvas) is the active screen — true black crushes the native
contrast of full-bleed page art, which the toggle's own OLED-battery-saving rationale doesn't
justify overriding during actual reading. Mechanism: the Reader screens' navigation entry/exit
already goes through this app's existing screen-transition lifecycle — on entering a Reader screen,
run the true-black-off resource path (the same `ApplyTheme(currentThemeKey)` re-run § Toggling off
already uses) without touching the persisted `AppSettings.TrueBlackDark` value itself; on leaving,
if the setting is still on, re-run the true-black-on override. The toggle's *persisted* state is
untouched by entering/leaving the Reader — this is a live-resource suspend/resume, not a setting
change, so the user's actual preference survives a reading session unaltered.

**OLED pixel-shift (anti-burn-in).** Recommended in a second review round, agreed to, but only
actually written in here during the third — flagging that gap explicitly since it was fair to
catch: a chat recommendation isn't a spec change until it's in the file. Scope: **static app-shell
chrome only** — the nav rail and other panels that sit in the same screen position indefinitely —
**not** scrolling lists, comic/book reading pages, or anything already moving under user input;
shifting position on live reading content would be visibly broken, not merely superfluous. Active
only when `TrueBlackDark` is on. A `DispatcherTimer` (UI thread, 5-minute interval) nudges a shared
`TranslateTransform` on the shell chrome root by 1–2px in a slowly rotating direction (four-position
cycle), reverting to `(0,0)` immediately if `TrueBlackDark` is turned off or the active theme
changes away from Dark. No new settings surface — this is a behavior of the existing toggle, not a
separate one to expose.

## New theme catalog

Two new light themes — not 1:1-named with the existing dark set (a curated, smaller addition, not
a mirror of all four dark personalities) — plus Matrix. All colors below were verified against
WCAG 2.1 contrast minimums (4.5:1 for body/label text, 3:1 for large/faint text and non-text UI)
using the actual relative-luminance formula, not eyeballed. `PbAccentBrush` specifically is used as
literal `TextBlock.Foreground` in shipped XAML (e.g. `Breadcrumb.axaml:29`), not only as a fill/
border color, so `accent` itself was held to the 4.5:1 text bar, not the looser 3:1 non-text bar —
this caught both initial accent proposals failing (3.77–3.78:1) before they shipped.

### Daylight (`mode: Light`)

Warm paper-white, same amber-family accent as `default` so light mode still reads as Paperbunkr's
own identity rather than a generic light skin.

| Token | Hex | Contrast vs bg |
|---|---|---|
| bg | `#F7F3EC` | — |
| chrome | `#FDFBF8` | — |
| border | `#E2DBCB` | — |
| text | `#2A2620` | 13.60:1 |
| textMuted | `#6B6357` | 5.35:1 |
| textFaint | `#948C7C` | 3.01:1 |
| accent | `#985A27` | 4.96:1 |
| accentText | `#8A5322` | 5.68:1 |
| accentSoft | `#29985A27` | — |
| badge | `#D19A3D` | badgeText/badge 7.09:1 |
| badgeText | `#241505` | — |
| success | `#3E7A56` | 4.60:1 |
| chartBlue | `#5B8DBE` | decorative, unchanged from `default` |
| chartViolet | `#9B7EBD` | decorative, unchanged from `default` |
| surface0 | `#EDE7DA` | recessed (darkest of the set) |
| surface1 | `#F7F3EC` | = bg |
| surface2 | `#FDFBF8` | = chrome |
| surface3 | `#FFFFFF` | elevated (lightest of the set) |
| glow | `#668A5322` | |
| heroGradientStart | `#00F7F3EC` | |
| heroGradientEnd | `#FFF7F3EC` | |

`spacingUnit: 4`, `radius: 7`, `radiusSm: 5`, `radiusLg: 14` (matches `default`'s convention).

### Overcast (`mode: Light`)

Cool gray-blue, for users who want a cooler light option than Daylight's warm paper tone.

| Token | Hex | Contrast vs bg |
|---|---|---|
| bg | `#EEF0F3` | — |
| chrome | `#F7F8FA` | — |
| border | `#D7DBE0` | — |
| text | `#22262C` | 13.31:1 |
| textMuted | `#5A6169` | 5.49:1 |
| textFaint | `#70777F` | 3.97:1 |
| accent | `#3469A6` | 4.95:1 |
| accentText | `#2E5D96` | 5.89:1 |
| accentSoft | `#293469A6` | — |
| badge | `#3FAFC2` | badgeText/badge 6.40:1 |
| badgeText | `#0A2226` | — |
| success | `#2E7A5E` | 4.53:1 |
| chartBlue | `#4E9DE8` | decorative |
| chartViolet | `#8C6FBE` | decorative |
| surface0 | `#DEE2E7` | recessed |
| surface1 | `#EEF0F3` | = bg |
| surface2 | `#F7F8FA` | = chrome |
| surface3 | `#FFFFFF` | elevated |
| glow | `#662E5D96` | |
| heroGradientStart | `#00EEF0F3` | |
| heroGradientEnd | `#FFEEF0F3` | |

`spacingUnit: 4`, `radius: 7`, `radiusSm: 5`, `radiusLg: 14`.

### Matrix (`mode: Dark`, `category: "matrix"`)

Near-black background, phosphor-green palette. Ships with the code-rain overlay (below).

| Token | Hex | Contrast vs bg |
|---|---|---|
| bg | `#000000` | — |
| chrome | `#010401` | — |
| border | `#0D2B14` | — |
| text | `#33FF66` | 15.64:1 |
| textMuted | `#1FAF48` | 7.28:1 |
| textFaint | `#146B2C` | 3.17:1 |
| accent | `#33FF66` | 15.64:1 |
| accentText | `#33FF66` | 15.64:1 |
| accentSoft | `#2933FF66` | — |
| badge | `#1FAF48` | badgeText/badge 7.28:1 |
| badgeText | `#000000` | |
| success | `#33FF66` | 15.64:1 |
| chartBlue | `#33FF66` | matches palette — Matrix is intentionally monochrome |
| chartViolet | `#1FAF48` | matches palette |
| surface0 | `#000000` | |
| surface1 | `#000000` | |
| surface2 | `#010401` | |
| surface3 | `#0D2B14` | |
| glow | `#6633FF66` | |
| heroGradientStart | `#00000000` | |
| heroGradientEnd | `#FF000000` | |

`spacingUnit: 4`, `radius: 7`, `radiusSm: 5`, `radiusLg: 14`.

## Matrix rain effect

New `MatrixRainOverlay : Control`. Two things it deliberately does **not** do, both for reasons
already established elsewhere in this codebase, plus a third correction added during spec review:

1. **Not** a XAML `<Animation>`/`<KeyFrame>` targeting `RenderTransform` — known, confirmed crash:
   `System.InvalidOperationException: No animator registered for the property RenderTransform`,
   thrown at `EndInit`/`ApplyAnimations` (see [[reference_avalonia_keyframe_rendertransform_crash]]).
2. **Not** a `DispatcherTimer` calling `InvalidateVisual()` on a plain `Control.Render()` override
   (the pattern `Views/Stats/CategoryDonut.cs`/`ActivityHeatmap.cs` use) — fine for those
   redraw-on-data-change controls, but wrong for *continuous* full-frame-rate animation: per
   Avalonia's own docs, `Render()` always runs on the UI thread, and driving it continuously
   competes with layout/input on every other screen the overlay is mounted behind (caught in spec
   review, credit: external review round).
3. Instead: `CompositionCustomVisualHandler` (`Avalonia.Rendering.Composition`) — its `OnRender`
   callback runs on the **render thread**, is handed a real `SkiaSharp.SKCanvas` directly, and is
   driven by calling `sender.RequestNextFrameRendering()` at the end of each frame rather than a
   UI-thread timer. This is Avalonia's own documented mechanism for "smooth, continuous animations
   or real-time visualizations where UI-thread overhead is a concern" — verified against
   `docs.avaloniaui.net/docs/graphics-animation/custom-rendering` directly, not assumed.

- Classic falling-column glyph rain, drawn in the theme's green palette, via the handler's
  `SKCanvas`.
- Mounted once at the app-shell level, below all real content in z-order, `IsVisible` bound to
  "active theme is Matrix." **Lifecycle constraint**: the handler stops calling
  `RequestNextFrameRendering()` (i.e. the loop terminates, not just skips drawing) as soon as
  `IsVisible` goes false or the control detaches — it must not keep consuming render-thread time for
  an overlay nobody can see.
- **Disposal constraint** (gap caught in a second review round — the first draft covered stopping
  the loop but not releasing what the loop allocated): `SKPaint`/`SKTypeface` wrap unmanaged
  handles. The handler allocates them once (reused across frames, not recreated per-frame — also a
  perf concern) and explicitly disposes them in response to the same detach/`IsVisible`-false
  transition that stops the frame loop, not left for the GC finalizer to eventually reclaim.
  Switching away from Matrix and back must not leak a `SKPaint`/`SKTypeface` pair per switch.
- **Legibility constraint**: every content-bearing surface (cards, panels, any container that
  renders `Pb*`-colored text, including `textFaint`) keeps its normal opaque `Pb*` background,
  painted above the rain in z-order. The rain is only ever visible in genuine negative space
  (margins/gutters between panels) — it must never render directly behind live text. This is
  already how the existing screens are built (solid-background panels, not a transparent/glass
  layout), so no change to Matrix's already-WCAG-verified token values is needed to satisfy this —
  it's an explicit constraint on where the overlay is allowed to show through, not a color fix.
- **Overdraw constraint** (gap caught in a third review round): z-order hiding the rain behind an
  opaque panel stops the *user* from seeing it, but not Skia from computing and rasterizing it —
  the canvas still pays the fill cost for glyphs nobody will ever see under a solid panel, real
  wasted GPU work at whatever fraction of the screen those panels cover. Fix, scoped lighter than
  a full per-control registry (that's real overkill — a `Theme.IsOpaqueBackplate` attached property
  tracking every control's bounds globally is its own meaningful subsystem for a decorative
  effect): each screen's own top-level content panel (the one root `Border`/`Panel` per
  Home/Library/Preferences/etc. that already paints `PbBg`/`PbChrome`) reports its screen-space
  bounds to the handler via `SendHandlerMessage` on load and on layout change — a handful of rects,
  not one per control. `OnRender` issues `SKCanvas.ClipRect` (difference mode) against that small
  set before drawing, so Skia skips the columns that fall fully under an opaque screen region.
- **Excluded from the reader canvas specifically** (comic/book/PDF page-rendering surfaces) — those
  screens' visual trees don't include the overlay, so it never renders behind live reading content,
  while it still plays across Home/Library/Preferences/Detail/etc.
- **Frame-rate independence**: rain advances by elapsed wall-clock time since the last frame, not
  by a fixed per-frame step — otherwise a 144Hz display would visibly rain faster than a 60Hz one
  (a real correctness point from the third review round's list, adopted; not literally binding to
  `DateTime.Now.Millisecond` as phrased there, which would make speed jump at each second rollover —
  standard delta-time accumulation instead).
- `theme.json` gains optional `matrixGlyphs` (`string[]?`) — when present, the render loop draws
  from that character set instead of the hardcoded default katakana; absent falls back to the
  default. Cheap (one array field, one random-pick call), genuine personalization hook, no other
  system touched.
- Respects `PbMotionFast`/`ReducedMotion`: when reduced motion is on, the handler renders one
  static frame and stops requesting further frames, instead of animating (same "instant" convention
  every other `PbMotion*` consumer already follows).

## Appearance UI

No layout change from today's Preferences → Appearance screen — same flat theme-card grid
(`ThemeSummary`, renamed from `SkinSummary`), now populated with 10 entries instead of 5
(`default`, `cool_technical`, `vibrant_pop`, `vintage_paperback`, `windows_11`, Daylight, Overcast,
Matrix, Maximum Contrast, the color-blind-safe variant; plus any future ones). New rows below the
grid: the true-black toggle (visible/enabled only when the active theme's `mode == Dark`), the Auto
mode selector (`Off`/`FollowSystem`/`Scheduled`, with an hour picker when `Scheduled`), the accent
color picker, and the scheduled-true-black hour picker (visible only alongside `Scheduled` Auto
mode, since it reuses that same time-check).

## Testing

- `ThemeServiceTests` (renamed/extended from `SkinServiceTests`): `ApplyTheme` sets
  `Application.Current.RequestedThemeVariant` correctly per `mode` for a Light theme, a Dark theme,
  and Matrix specifically (still resolves to `Dark`). True-black override applies only when both
  conditions hold (`TrueBlackDark == true` AND active theme `mode == Dark`) — explicit test for the
  "true black ignored under a Light theme" case, matching the UI-hiding behavior.
  Test-injection seam (`internal ThemeService(Func<PaperbunkrDbContext>)`) carries over unchanged
  from `SkinService`.
- EF migration test: `ActiveSkinKey` → `ActiveThemeKey` rename preserves an existing row's value
  (same pattern as prior migration-safety tests in this project, e.g.
  [[project_paperbunkr_migration_rollback_orphan_column_bug]] — verify by inspecting the generated
  migration before running it, not after).
- `MatrixRainOverlay`: no pixel-level render assertions (established convention for the other
  custom-draw controls in this codebase); verify `IsVisible` toggles correctly off the active
  theme, that it's absent from the reader screens' visual tree, and that the render-thread frame
  loop actually stops (no further `RequestNextFrameRendering()` calls) once `IsVisible` goes false
  or reduced motion is on — not just that drawing is skipped while the loop keeps running. Separate
  test asserts `SKPaint`/`SKTypeface` are disposed (not just GC-eligible) on that same transition.
  Battery-throttle test: assert `GetSystemPowerStatus` is never called from the render-thread
  callback — only from the 30s UI-thread poll — and that the render loop reads the cached flag.
- True-black toggle-off test: verify it round-trips through `ApplyTheme(currentThemeKey)` and lands
  back on the theme's original (non-`#000000`) `PbBg`/`PbChrome`/`PbSurface0-3` values, not stuck
  values from the true-black overwrite.
- Reader auto-suspend test: with `TrueBlackDark` persisted `true`, entering a Reader screen leaves
  live resources at the theme's normal (non-`#000000`) values while `AppSettings.TrueBlackDark`
  itself stays `true` in the DB; leaving the Reader screen re-applies `#000000` live without the
  user having touched the toggle.
- Auto mode: `FollowSystem` reads `PlatformSettings.GetColorValues()` exactly once per
  check (startup/resume), never subscribes — a regression test asserting no `ColorValuesChanged`
  subscription exists after enabling `FollowSystem` protects the freeze-bug fix from being
  reintroduced by this feature. `Scheduled` test drives the clock via an injectable time source
  (same seam style as the rest of this codebase's testable services), not real `DateTime.Now`.
  `LastLightThemeKey`/`LastDarkThemeKey` update correctly on manual apply, and Auto switches between
  exactly that pair, not a hardcoded default.
- Accent override: applying a raw hex correctly derives `accentText`/`accentSoft`/`glow` at ≥4.5:1
  against the *active theme's* `bg` in both directions — one test under a Light theme (must darken)
  and one under a Dark theme (must lighten), specifically covering the bug caught in the second
  review round where a fixed "always darken" rule would fail the Dark case.
- Mica/Acrylic: `windowBackdrop` maps to the correct `TransparencyLevelHint`; a theme omitting the
  field leaves the window's current hint untouched (no regression for the 8 existing/new themes
  that don't set it).
- All new/renamed test classes touching `PaperbunkrDbContext.DatabasePathOverride` join
  `AvaloniaTestCollection`, per the standing rule in
  [[project_paperbunkr_preferences_skin_system]] — this class of bug only surfaces running the
  *full* suite, not a filtered one.
- Manual on-screen verification (no UI automation without asking first, per
  [[feedback_no_unauthorized_ui_automation]]): user clicks through all 8 themes, confirms native
  FluentAvalonia chrome (ComboBox popup, ScrollBar) actually follows Light/Dark correctly for the
  first time; confirms true-black toggle visibly changes a Dark theme's background; confirms Matrix
  rain plays on Home/Library and is absent from the Reader screen.

## Extended scope (folded in 2026-09-17, from external review)

Nine of the twelve features an external review round (Gemini) suggested were folded in — each is
either architecturally free given the mechanism already designed above, or a small, bounded
addition. Two more (`.crpck` export/authoring UI, a raw `Pb*`-override user file) were **not**
folded in — both directly reverse the "skins deferred to the future" decision made earlier in this
document's own design process, so they're a pending question, not an assumption; see the note in
"Explicitly out of scope" below.

**Auto mode (OS/time-synced Light↔Dark).** Not a third theme `mode` value — a selection layer
above the picker. `AppSettings.ThemeAutoMode` (`Off`/`FollowSystem`/`Scheduled`), plus
`AppSettings.LastLightThemeKey`/`LastDarkThemeKey` (updated automatically whenever the user
manually applies a Light or Dark theme) — Auto switches between whichever pair the user last picked
by hand, not a hardcoded default pair.

`FollowSystem` (revised — a one-shot startup-only read, as originally drafted, would miss every OS
theme change for the entire life of a long-running session, e.g. Windows' own sunset auto-dark-mode
firing while the app sits open; caught in a third review round). Subscribes to Avalonia's own
`Application.Current.PlatformSettings.ColorValuesChanged` directly — **not** a second, redundant
raw Win32 `SystemEvents.UserPreferenceChanged` hook (also proposed in that round; unnecessary,
since the event we need already exists in Avalonia and adding a parallel OS-level listener next to
it is duplicate machinery for the same signal). This is safe specifically because the freeze bug's
cause was *`FluentAvaloniaTheme`'s own handler* rewriting `HighContrast` resource-dictionary entries
synchronously while `Popup.Open()` was still constructing its `TopLevel` (see Background) — not the
event itself, and not "any handler on this event." A second, different subscriber is fine as long as
it doesn't repeat that specific shape.

And it easily could, naively: `FollowSystem`'s handler needs to end up calling `ApplyTheme`, which
writes ~20 `Application.Resources[key]` entries — if that ran synchronously inside the
`ColorValuesChanged` callback, and that callback happened to fire while a popup was mid-`Open()`
(plausible — a system dark-mode flip is not synchronized with what the user's cursor is doing),
it's the same "resource-dictionary mutation during `Popup.Open()`" shape that caused the original
freeze, just reached from a different trigger. Fix: reuse this codebase's own already-established
pattern for exactly this bug class (see `CLAUDE.md`'s "don't remove/detach a control from inside a
routed event it's still raising" — fixed there via `Dispatcher.UIThread.Post`) — the handler defers
the actual `ApplyTheme` call one dispatcher tick via `Dispatcher.UIThread.Post`, rather than running
it synchronously inline.

`Scheduled` compares local time against a configurable hour on a lightweight periodic check
(minutes-scale, not per-frame) — unaffected by the above, no event subscription involved. The
actual palette swap when `Scheduled` crosses its hour runs a 300ms crossfade (a full-window overlay
`Border` `DoubleTransition`-faded between old and new `Pb*` values) rather than an instant cut —
adopted from the third review round's list; reuses the exact transition pattern already documented
for this codebase's other view-level fades (`avalonia-pro-max/motion`), no new mechanism. Manual
theme picks from the Appearance grid stay instant, unchanged — the crossfade is specific to the
unattended `Scheduled` switch, where a jump-cut is more jarring since the user isn't looking at the
picker when it happens.

**Maximum Contrast theme** (`mode: Dark`, AAA — 7:1 minimum, not just 4.5:1): `bg #000000`,
`text #FFFFFF` (21.00:1), `textMuted #C4C4C4` (12.04:1), `textFaint #8A8A8A` (6.08:1),
`accent #FFD24D` (14.58:1), `success #5CE39A` (12.91:1) — computed the same way as every other
theme in this document, not eyeballed. A Light AAA counterpart is the same recipe against a white
bg if wanted later; not built now (nothing asked for it specifically).

**Color-blind-safe variant** (`mode: Dark`, pairs with `default`'s bg `#0A0B0D`): re-keys the
red/green-confusable tokens to the Okabe-Ito colorblind-safe palette — `accent #4FA3D9` (blue,
7.10:1), `success #3BC49A` (bluish-green, 8.95:1), `badge #E6A93D` (orange, 9.48:1). `chartBlue`/
`chartViolet` become the palette's sky-blue `#56B4E9` and reddish-purple `#CC79A7` so a 2-series
chart (`CategoryDonut`, reading stats) stays distinguishable under protanopia/deuteranopia, not
just under typical vision. One theme for now (dark); a light counterpart is the same recipe later
if wanted.

**Custom accent color picker.** New `AppSettings.AccentOverrideHex` (`string?`, null = no override,
global not per-theme — avoids a combinatorial per-theme-override matrix). When set,
`ApplyTheme`/`ApplySkinResources` derives `accentText`/`accentSoft`/`glow` from it — **not** via a
fixed "always darken" rule. `AccentOverrideHex` is global, so it has to stay legible against
whichever theme is active, including a Dark one — darkening only helps on a Light bg; against a
Dark bg it makes contrast worse (caught in a second review round: the first draft's "same
darken-for-contrast method" language was directionally wrong for half the app's themes, not merely
imprecise). Correct derivation: compare the picked hex's relative luminance against the *active
theme's* `bg` luminance, then adjust HSL lightness in whichever direction increases contrast
(darker when `bg` is light, lighter when `bg` is dark), iterating toward the 4.5:1 target the same
way § New theme catalog's contrast pass did for Daylight/Overcast — same target ratio and iterative
method, generalized to run bg-aware instead of assuming a fixed direction. Re-derives on every
theme switch while an override is active, not just once at pick-time. UI: a color picker control in
Preferences → Appearance, below the theme grid.

**Per-theme default font.** `theme.json` gains optional `"defaultFontFamily"`. Precedence in
`ApplyFontResource`: `AppSettings.SelectedFontFamily` (the existing explicit global override) wins
if set; else the active theme's `defaultFontFamily` if present; else the existing hardcoded
`DefaultFontFamily` (Source Serif 4) — additive fallback layer under the existing mechanism, no
behavior change for any theme that omits the field. Matrix sets
`"Cascadia Code, Consolas, monospace"`, completing its identity without a schema conflict.

**Scrollbar geometry tokens.** `theme.json` gains optional `scrollbarWidth` (double) and
`scrollbarThumbOpacity` (double, 0–1). Consumed via a `ControlTheme` override on FluentAvalonia's
`ScrollBar`, bound to new `PbScrollbarWidth`/`PbScrollbarThumbOpacity` `DynamicResource`s with
FluentAvalonia's own defaults as the fallback when a theme omits the fields (no visual change for
any existing theme). Matrix can go thin/minimal if wanted; left as a per-theme-catalog detail for
the implementation plan, not mandatory on every entry.

**Mica/Acrylic backdrop for `windows_11`.** `theme.json` gains optional
`"windowBackdrop": "None"|"Mica"|"Acrylic"` (Windows-only; ignored elsewhere). `ApplyTheme` sets
`MainWindow.TransparencyLevelHint` accordingly when present. Not a new pattern —
`TransparencyLevelHint` is already used in this codebase (`OverlayHostWindow.cs`,
`SplashWindow.axaml`), just extended to the main window and driven by theme choice instead of a
fixed value. Degrades silently on unsupported OS/compositor, per Avalonia's own documented
behavior for the hint.

**Battery-linked animation throttling — scoped to Matrix rain only**, not a general reduced-motion
redesign (that would be real scope creep; nothing else asked for it). **Not** a per-frame check —
caught in a second review round: calling `GetSystemPowerStatus` (Win32 interop, same raw-interop
pattern `ShellRegister.cs` already established) inside the render-thread loop would mean a P/Invoke
transition 60–144 times/second, real render-thread pressure regardless of how cheap the call itself
is. Instead: a `DispatcherTimer` on the **UI thread**, polling every 30s, writes the result to a
`volatile bool` field; the render-thread loop only ever reads that flag before calling
`RequestNextFrameRendering()`. Unplugged and below a threshold (20%) stops requesting frames, same
static-frame fallback as `ReducedMotion`. Windows-only check; other platforms always animate (no
battery API call attempted, flag stays `false`/unused).

**Resuming after a throttle — real bug caught in a third review round, now fixed.** If the render
loop simply stops calling `RequestNextFrameRendering()`, `OnRender` never fires again, ever — a
cached flag flipping back to unthrottled is never observed, because nothing is left running to
check it. Plugging the laptop back in would permanently freeze the rain until the theme is switched
away and back, or the app restarts. Fix uses the mechanism Avalonia's own docs already document for
UI-thread→render-thread signaling (not a direct cross-thread call to
`RequestNextFrameRendering()`, which is a render-thread-owned API): the UI-thread timer calls
`handler.SendHandlerMessage(BatteryThrottleChanged: false)` exactly once, only on the
throttled→unthrottled transition; the render-thread `OnMessage` callback receives it and is the one
that calls `sender.RequestNextFrameRendering()` — from the render thread, where that call belongs —
to restart the loop.

**Scheduled true black.** Extends Auto mode's time-check rather than adding a second scheduler:
new `AppSettings.TrueBlackAutoHour` (`int?`, null = no schedule). When set, the same periodic
time-check driving `Scheduled` Auto mode also flips `TrueBlackDark` on/off crossing that hour —
one scheduling mechanism, two settings it can drive.

## Explicitly out of scope

- The `.crpck` installable-package mechanism itself — untouched beyond the mechanical rename.
  Treated as a separate future "skins" initiative per the user's own framing.
- Fixing `windows_11`'s own `accent` contrast gap (`#0078D4` on `#F3F3F3` = 4.08:1, fails 4.5:1) —
  a pre-existing issue, out of scope for this change since the user scoped this work to the new
  light themes, not an audit of old ones. Left as-is.
- Any UI grouping/tabs by theme mode in the Appearance screen — picker stays a flat grid, per the
  user's explicit instruction to keep today's arrangement.
- Per-theme icon packs — the `icons: {}` field already exists in `theme.json` from the original
  2026-08-07 skin spec but was never wired to any UI. Not foldable as pure design/code work: it
  needs actual icon artwork per theme (up to 11 icon sets now), a content-creation job outside this
  document's scope, not a schema or service change.
- `.crpck` export/authoring UI and a raw `Pb*`-override user-chrome file — **confirmed deferred**
  by the user (both directly reverse this document's own earlier "skins deferred to the future"
  decision, § Summary; user re-confirmed leaving both out of this phase).
- Contrast auto-correction applied to *every* loaded theme, including third-party `.crpck` files —
  rejected, not just skipped: conflicts with the same deferred-skins boundary above, and silently
  rewriting a user's installed custom theme's colors without telling them is a worse experience than
  it fixes, not a neutral omission.
- A theme-specific rendering-backend override (force-software for Matrix specifically) — redundant
  with the already-shipped global `AppSettings.RenderingBackend` (Auto/Gpu/Software, see
  [[project_paperbunkr_hardware_rendering]]); a second, theme-scoped control surface next to that
  existing one would just be confusing, not additive.
- Pointer-reactive rain (cursor deflection / parallax camera matrix), theme-linked sound effects,
  and per-monitor HDR/SDR-aware contrast weighting — each real but unrelated to what "theme system"
  means in this document (input-driven micro-interaction, audio, and multi-monitor color management
  are each their own feature area), proposed in the third review round, not adopted.

## Revisions after first on-screen use (2026-09-19)

- **Matrix rain never rendered.** MainWindow.axaml had marked both full-area screen hosts
  `IsOpaqueBackplate`, so the overdraw-clipping rect set covered the entire overlay and every glyph
  was clipped away. Screens have no root background, so the rain now shows in every bare area (text
  rows, gutters, empty space) and is hidden behind opaque cards by z-order. The attached property
  stays available for genuinely opaque, small panels but is registered nowhere. Rain alpha is capped
  (peak 140/255) because it now sits behind bare-background text. `MatrixRainOverlay` also retries its
  composition-visual setup when it becomes visible (it is mounted hidden under other themes).
- **Rain toggle.** `AppSettings.MatrixRainEnabled` (default true, migration `AddMatrixRainEnabled`),
  Preferences → Appearance → "Matrix rain" row shown only while the Matrix theme is active,
  `ThemeService.SetMatrixRainEnabled` + `MatrixRainEnabledChanged` (separate from `ThemeApplied` so
  Home's cover-wall etc. do not re-render), gating `MainViewModel.IsMatrixRainVisible` live.
- **Hover/focus ring (`PbGlowRing`).** A `BoxShadows` value cannot embed a `DynamicResource`, so
  `ThemeService.ApplyGlowRing` rebuilds it from the theme's glow color (alpha bumped to 0x99) on
  every apply and accent override; consumers use `{DynamicResource PbGlowRing}`. A ring on a Border
  flush with its `Button.card` is clipped entirely by the Button's bounds (verified: 0 px vs 1504 px
  with a 5px gutter in a headless render), so every ringed Library Border (Panorama cover, List/
  Tiles/Details rows) carries a permanent margin inside its Button and the cell size / panel spacing
  compensate.
- **Splash "preloader".** Glow and loading-bar gradients read the persisted theme's Accent/AccentText
  (best-effort, same fallback pattern as reduced motion) instead of the hardcoded default palette.
