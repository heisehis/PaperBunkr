---
name: paperbunkr-design-language
description: Paperbunkr's UI design language (type scale, tokens, the shared control families, dialogs, spacing, icons, motion, popups) and the lint rules that enforce it. Use before designing, specifying, planning, building or reviewing ANY Paperbunkr screen, dialog, popup or control - new or changed XAML/AXAML, a UI spec, or a UI review.
---

# Paperbunkr design language

Set by the 2026-10-19/20 app-wide UI polish (specs `docs/superpowers/specs/2026-10-19-ui-polish-*` and `2026-10-20-ui-polish-*`, audit
`2026-10-19-ui-polish-audit.md`). The user's complaint that started it: "almost everything looks out of line, even ever so slightly". The rule
behind every item below: **reuse the shared piece; never restyle it locally.** Inconsistency comes from local copies that drift.

Use this together with the `avalonia` skill (CLAUDE.md) - this file says *what* Paperbunkr looks like, that one says how Avalonia does it.

## Before you design

- The user's screen is **1366×768 at 100 %**. Treat that as the minimum: with the sidebar and the Library preview panel open, a toolbar is
  ~750 px wide. Every theme must look finished - dark, light (Daylight) and Matrix - so colours come from tokens only.
- In a design round, ask about layout and behaviour, not about which button style or font size to use: those are decided here.

## Type

Scale: **10 / 11 / 12 / 13 / 15 / 18 / 22** (+ 42 hero). Use the classes, not FontSize/FontWeight/Foreground attributes:

| Class | Size | Use |
|---|---|---|
| `pbTextHeading` | 22, display font | Screen titles ("Insights", "Unread manga") |
| `pbTextTitle` | 18 | Card/panel titles |
| `pbTextSection` | 15 | Content section headings, sentence case |
| `pbTextBody` | 13 | Body text |
| `pbTextMeta` | 12 | Secondary lines |
| `pbTextCaption` | 11 | Captions, counts |
| `pbTextOverline` | 10, caps | Sidebar group labels ("COLLECTIONS") |

Dialog titles are `dialogTitle` (display font) via DialogCard. Field labels are the shared `fieldLabel` - never define it locally.
Readable text is `PbTextBrush` or `PbTextMutedBrush`; **`PbTextFaintBrush` is for icons and dividers only** (3.1-3.8:1 contrast).

## Tokens

- Colours: `{DynamicResource Pb*Brush}` only - `PbSurface0..3`, `PbBorder`, `PbText`/`Muted`/`Faint`, `PbAccent`/`AccentSoft`/`AccentText`,
  `PbBadge*`, `PbSuccess`, `PbDanger`. No hex in views (`ThemeHardcodedColorLintTests` baseline may only go down). A colour that must stay
  dark on every theme because it sits on cover art is still a named token (`PbCoverShadeColor`, `PbHeroScrimColor`) in `AppTokens.axaml`.
- Radii: `{DynamicResource PbRadiusSm / PbRadius / PbRadiusLg / PbRadiusChip}` (DynamicResource, so skins can change them). Never a literal
  over 2 except a true circle. **No oval pills** - chips are squircles (`PbRadiusChip`), never CornerRadius 999.
- Sizes: `PbControlHeight` 28 (buttons, fields, segments, search), `PbControlHeightSm` 22. Icons `PbIconSizeXs..Xl`.

## Spacing

- Margins and paddings on the **4 px grid** (4, 8, 12, 16, 20, 24). 1 is fine for hairline nudges; odd values (3, 5, 7, 9, 11, 15) are not.
- Screen content gutter **24**. Cards pad **16**. Rows in a list: 8-12 vertical.

## The shared families (`Styles/Controls.axaml`) - use these, don't build your own

| Need | Use |
|---|---|
| Main action | `Button.primary` (one per area) |
| Other actions | `Button.secondary` (bordered), `Button.ghost` (text only), `Button.danger`; `.sm` for 22 px |
| **Icon-only button** | **`Button.iconButton`** (28 square) / `iconButton sm` (22), always with `AutomationProperties.Name`. Never a ghost/secondary button with only an icon, never a fixed Width on a text-family button - its padding crops the icon. |
| Segmented switch | `Border.segGroup` > `Button.segItem` (`.active`/`.on`), solid accent |
| Tabs | `Button.tab` (underline, `.active`) in a horizontal StackPanel with **`Spacing="20"`** - the family has no side padding. |
| Sidebar / nav rows | `Button.sideItemButton` / `prefNavItem` (3 px accent bar + tint); label `TextBlock.sideItemLabel` (trims, 8 px before a count) |
| Count | `Border.countPill`; filters `Button.filterChip`; tags `Border.pbChip`; status `Border.statusBadge` |
| Card | `Border.card` (16 padding) or `Button.clickCard`. A card you can't click must not glow on hover (`heroTile.inert`). |
| Dropdown / menu | `Border.dropdown` with `Button.menuRow` rows (content stretches, so a `*,Auto` row puts its count at the right edge) |

Retired classes stay retired (`RetiredControlClassesAreNotUsed`). A view may add **state** styles (`.selected`, `.dimmed`) but not
re-set a family's visuals (`SharedFamiliesAreNotRestyledLocally`) or its size (`FamilyButtonsCarryNoSizeOverrides`).
A chip is not an icon tile: for an icon in a coloured square use a plain Border (26 px, `PbRadius`, `PbAccentSoftBrush`).

## Dialogs

Every dialog is a **`controls:DialogCard`** (`DialogsUseDialogCard`): `Title`, optional `Subtitle`, `Size` Small 420 / Medium 560 /
Large 760 / XLarge 960, `Footer` (Cancel ghost + primary on the right), `FooterStart` (Back/secondary on the left). It owns the only ✕,
caps its height at the window minus 48 and scrolls its body, and shrinks to a narrower host instead of clipping. Never give its frame a
Width. Put wrapping text in a Grid column, **never in a horizontal StackPanel** (it gets unlimited width and never wraps).

## Popups and dropdowns

- A dropdown `Popup` gets `controls:PopupFit.ToWindow="True"` and a `ScrollViewer` inside its `Border.dropdown`, so a long menu scrolls
  instead of running off a 768 px window. Don't nest a second ScrollViewer.
- `SuggestBox` is the only dropdown/combobox. Its template keeps a 22 px gap for the chevron, so a screen can set PART_TextBox Padding freely.
- A `BrandMark` beside a field that already shows the value: `TextFallback="False"` (otherwise a value with no logo prints its name twice).

## Icons

FluentIcons `fi:SymbolIcon` only - no text glyphs as icons (✕ ＋ ▾ ★ …; `NoTextGlyphIcons`). Expand/collapse is ChevronRight/ChevronDown.
Every icon-only control has an accessible name (`IconButtonsHaveNames`).

## Motion

Motion v2 (`docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md`). Every animation has a **role**, and the role
picks the tokens:

| Role | Use | Opacity token | Transform token | Curve |
|---|---|---|---|---|
| Acknowledge | press, toggle, selection colour | `PbMotionAcknowledge` | `PbMotionAcknowledgeMove` | `PbMotionMoveEase` |
| Hover | hover colour, card/cover lift | `PbMotionHoverFade` | `PbMotionHoverMove` | colour `PbMotionEase`, lift `PbMotionSpring` |
| Reveal | dialogs, drawers, toasts, popups | `PbMotionRevealFade` | `PbMotionRevealMove` | in `PbMotionEase`, out `PbMotionRevealExit` + `PbMotionExitEase` |
| Replace | tab content, result sets | `PbMotionReplace` | - | `PbMotionEase` |
| Travel | routes, cover flight | `PbMotionTravelFade` | `PbMotionTravelMove` / `PbMotionFlight` | `PbMotionEase` / `PbMotionMoveEase` |
| Ambient | backdrops, Spotlight, loops | `PbMotionAmbient` | `PbMotionAmbient` | `PbMotionMoveEase` |

- Opacity on the *Fade* token, RenderTransform on the matching *Move* token: Reduced zeroes the move tokens, so a surface just fades. Never
  write a mode check into a style; the only motion-mode styles are the `Window.motionReduced` overrides at the end of `Controls.axaml`.
- Distances: 4 popups, 6 local, 8 toasts, 12 routes, 20 panels, 24 ambient, 1-3 nudges. Press `scale(0.97)` on the compact families only
  (rows, tabs, menu rows: colour only). Hover lift `translateY(-3px) scale(1.02)` on the artwork, never the hit-tested Button.
- `PbMotionSpring` (a damped spring: ~3 % once, settles) only for hover lift and panel arrival. No `BackEase`/`ElasticEase`/`BounceEase`.
- A dialog is an `OverlayShell` (it animates itself); a drawer or other show/hide surface uses `controls:Reveal.IsOpen` (visibility, exit,
  hit-test, tab order and focus handled); popups, menus and flyouts get their entrance automatically (`PopupMotion`) - close is instant.
- Animate Opacity/RenderTransform; a Width/Height/Margin/Padding transition needs a `motion: reflow` comment saying why. Never a
  BoxShadowsTransition - animate the Opacity of a fixed-shadow layer. `PbMotionFast/Standard/Large/Slow` are legacy aliases: don't add uses.
- Screens live in `controls:RouteHost` (one view kept per screen view model; readers are `ITransientScreen`): don't add a
  TransitioningContentControl for screens, and don't write scroll-restore code for Back - the kept view already has it. Route
  transitions follow `MainViewModel.RouteIntent` (Lateral settle 12 px, DrillIn from 0.985, Back reverses, Reader fades).
- On-page: list entrances are one-shot (owners pulse `PlayEntranceAnimation` false→true on real result changes only); tab strips get
  the sliding underline automatically (`TabIndicator`); a tab panel fades in with `controls:Replace.On="{Binding ItsShownFlag}"`; an
  expander chevron is one `fi:SymbolIcon Symbol="ChevronRight" Classes="chevronToggle" Classes.open="{Binding IsExpanded}"`, never two
  icons swapped by IsVisible. No count-ups, no chart sweeps.
- A panel that opens from a button sets `controls:Reveal.ReturnFocusTo="{Binding #ThatButton}"`. The reader's page turns (None/Slide/
  Crossfade/Vertical/Book/Curl) are the user's choice; the global Motion setting only overrides them.
- Code-driven motion asks `MotionPolicy.AllowsMotion(role)` / `AllowsContinuous()`; durations come from `MotionTokens.Get(role)`.
- Tune by eye with `PB_MOTION_SCALE=5` (every duration x5).
- Lint: `NoOvershootEasings`, `NoBoxShadowsTransition`, `LayoutTransitionsJustified`, `TransitionTokensOnly`, `TranslateDistancesOnScale`,
  `PressScaleScoped`, `LegacyMotionTokenBaselineOnlyGoesDown`.

## Input and focus

Hotkeys go through `IInputService` (CLAUDE.md "Input handling"). The focus ring is the app adorner - don't draw a second one. Don't make a
control inside a card focusable unless the keyboard needs it.

## Narrow windows

Design for the 1366×768 case: text trims (`CharacterEllipsis`) rather than overlapping, a `*` column holds the flexible text, and a toolbar
sheds width in steps (Workspace label → icon below 1000 px, search pill → icon below 900 px).

## Checking your work

1. `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~XamlConsistencyLintTests|FullyQualifiedName~ThemeHardcodedColorLintTests|FullyQualifiedName~SharedControlsStyleTests"`
   (19 lint rules: type scale, radii, faint text, fieldLabel, retired classes, no local restyle, no size overrides, icon names, no glyph
   icons, icon-only buttons, easing, DialogCard, and the 7 motion rules above).
2. Render it: `FoundationStyleTests.WithFoundation(theme, body)` loads the real styles and a theme's colours ("default", "daylight",
   "matrix"); write a PNG with `RenderTargetBitmap` when `PB_RENDER_OUT` is set (`PB_RENDER_SCALE` 1.25/1.5 for DPI). Check light and dark.
   The harness doesn't apply button role colours, so a primary button renders unaccented there.
3. After moving a view onto a family, check what the old local class gave it (padding, width, alignment): the lint can't see lost layout.
4. Run the `avalonia-pro-max/review-checklist` (CLAUDE.md), then ask the user to look on screen - every pass so far turned up something
   only the real app showed.
