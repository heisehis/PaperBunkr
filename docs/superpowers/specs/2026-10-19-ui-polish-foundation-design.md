# UI polish, sub-project 1: Foundation — Design

Date: 2026-10-19. Input: [2026-10-19-ui-polish-audit.md](2026-10-19-ui-polish-audit.md) (static sweep plus 20 screenshots).
Decisions came from three rounds of questions with the user on 2026-10-19. One of those rounds compared rendered options in the
browser companion.

## Summary

Paperbunkr looks "slightly out of line everywhere" because screens set their own font sizes, radii, colours and control looks by
hand, instead of using the design tokens from 2026-08-24. This sub-project lays the foundation every later pass builds on:

- one whole-pixel type scale;
- radii that follow the active theme;
- one rule for faint versus muted text;
- one look for every form input, popup and tooltip;
- a fixed dialog close button and one shared field label;
- the badge colour taken off decoration;
- a 24-px page margin;
- lint tests that keep all of the above from drifting back.

## Programme (decided: Q1)

| # | Sub-project | Status |
|---|---|---|
| 1 | **Foundation** (this doc) | designing |
| 2 | Shared controls: one tab/segmented control, one chip family, card/panel, button sizes, ghost-button border in low-contrast themes | later |
| 3 | Icons and a11y: Unicode glyphs → FluentIcons, 56 missing `AutomationProperties.Name`, a no-logo placeholder so list rows align | later |
| 4 | Screen passes: dialogs (adopt the full anatomy in §6), Library, Reader, then Detail, Home, Continuity, Preferences and the rest. Layout redesigns are allowed where a fix needs one (Q5). | later |
| 5 | Leftovers: `Width` animations, missing easings, Matrix-rain typeface, DPI sweep | later |

Every sub-project is checked in **all themes** (Q6), with Default, Daylight and Matrix as the minimum on-screen set.

## Goals

1. One type scale, with every XAML font size on it.
2. Radii that change with the theme everywhere, not only on the 133 places that already use a token.
3. Readable secondary text in every theme.
4. One look for text boxes, dropdowns, suggestion boxes, number boxes, popups and tooltips.
5. A dialog close button that sits inside the dialog, and one form-label style.
6. One accent colour per theme.
7. The same horizontal page margin on every standard screen.
8. Automated guards so new XAML can't reintroduce any of the above.

## Non-goals

- Spacing inside screens (margins and paddings other than the page margin). That moves screen by screen in sub-project 4 (Q2).
- Tabs, chips, cards, buttons (sub-project 2) and icons (sub-project 3).
- Moving each dialog onto the full anatomy (sub-project 4, dialog pass). This sub-project only ships the shared pieces and the
  close-button fix.
- Font sizes set from C# code. The lint test and the sweep cover `.axaml` only.
- Sizes of 24 and up (display and hero text). They're left alone (Q16).

## CE parity

None. This is presentation only. ComicRack CE has no equivalent design system, and nothing here changes behaviour, data or defaults
that CE defines.

## Decisions

| Q | Decision |
|---|---|
| Q2 | Font sizes and radii snapped across the whole app in one sweep. Spacing waits for sub-project 4. |
| Q3 | Page margin of 24 px on every standard screen. |
| Q4 | Badge colour (`PbBadge*`) only for counts and notification badges. Decoration uses the accent. |
| Q7 | Type scale A (compact): 10 · 11 · 12 · 13 · 15 · 18 · 22. |
| Q8 | Text fields style C: tinted background with only a bottom line, which thickens to the accent colour on focus. |
| Q9 | Dialog anatomy A: Bebas display title, uppercase overline labels, ✕ inside the card on the title row, divider above the footer, only the body scrolls. |
| Q10 | Placeholder text in the muted colour, italic. |
| Q11 | Readable text (labels, captions, meta, status) uses **muted**. **Faint** is only for disabled controls, dividers and decorative marks. |
| Q12 | Dropdowns, suggestion boxes and number boxes in forms match the underlined field. Toolbar search boxes keep their rounded filled look. |
| Q13 | Dialog widths S 420 / M 560 / L 760. Max height is the window height minus 48 px, with only the body scrolling. |
| Q14 | One error style: danger colour, an icon, a plain sentence, and raw detail behind "Details". |
| Q15 | Popups use the floating-panel look. Tooltips get a smaller version of it. |
| Q16 | Size mapping: 7–10.5 → 10, 11.5 → 12, 12.5 and 13.5 → 13, 14 → 15, 16 and 17 → 18, 20 → 22, 24 and up unchanged. |

## Approaches considered

1. **Override FluentAvalonia's own resource keys, plus app styles where no key exists (chosen).** FluentAvalonia 3.1.0 exposes
   resource keys for every state of its text and dropdown controls: `TextControlBackground[PointerOver|Focused|Disabled]`,
   `TextControlBorderBrush*`, `TextControlBorderThemeThickness[Focused]`, `TextControlPlaceholderForeground*`,
   `TextControlElevationBorder[Focused]Brush` (the gradient that draws Fluent's underline), and the matching `ComboBox*` keys.
   These key names were confirmed by reading `FluentAvalonia.dll` 3.1.0's strings. Defining those keys in `Application.Resources`
   wins over FluentAvalonia's theme dictionaries. The control templates stay theirs, so caret, selection, the clear and reveal buttons,
   and keyboard and a11y behaviour are untouched. Each override is a `SolidColorBrush` whose `Color` is a `{DynamicResource Pb*Color}`.
   That makes it follow live theme switches: `ThemeRuntimeApplier.SetColor` writes both the `Pb*Color` and a fresh `Pb*Brush`.
2. **Our own `ControlTheme`s with full templates for `TextBox`, `FlyoutPresenter` and `ToolTip`.** This gives full control, but means
   re-implementing everything FluentAvalonia's templates already do, and drifting from them on every upgrade. Rejected for TextBox.
   For `FlyoutPresenter` and `ToolTip`, plain property `Style`s (Background, BorderBrush, CornerRadius, Padding) are enough, because
   their templates bind those properties.
3. **Fix each screen as it comes up, with no shared foundation.** That's how the app got here. Rejected.

## Design

### 1. Type scale (`Styles/AppTokens.axaml`, `Styles/Typography.axaml`)

| Role | Size | Token | Today |
|---|---|---|---|
| Micro / overline | 10 | `PbFontSizeMicro` (9.5 → 10); `PbFontSizeOverline` (10.5 → 10) is kept as an alias | 7–10.5 |
| Caption | 11 | `PbFontSizeCaption` | 11 |
| Meta | 12 | `PbFontSizeMeta` (11.5 → 12) | 11.5, 12 |
| Body / control | 13 | `PbFontSizeBody`; `PbFontSizeControl` (12.5 → 13) is kept as an alias | 12.5, 13, 13.5 |
| Section | 15 | **new** `PbFontSizeSection` | 14, 15 |
| Title | 18 | **new** `PbFontSizeTitle` | 16, 17, 18 |
| Heading | 22 | `PbFontSizeHeading` (written by the theme) | 20, 22 |

- The existing keys keep their names so nothing that references them breaks. Only the values change.
- New role classes `TextBlock.pbTextSection` (15, SemiBold, text colour), `pbTextTitle` (18, SemiBold), `pbTextMeta` (12, muted) and
  `pbTextOverline` (10, SemiBold, uppercase via the view's own text, letter spacing 0.6, muted) join the existing `pbTextHero`,
  `pbTextHeading`, `pbTextBody` and `pbTextCaption`. `pbTextCaption` switches from faint to muted (§3).
- **Sweep:** every literal `FontSize` in `.axaml` is rewritten in place by the Q16 mapping. Sizes ≥ 24, the token references and the
  `PbIconSize*` uses are untouched. Literals stay literals in this pass. Turning them into role classes happens when each screen's
  local styles are consolidated (sub-project 4). The lint test (§8) pins every literal to the scale.

### 2. Radii that follow the theme

- The theme runtime rewrites `PbRadius`, `PbRadiusSm` and `PbRadiusLg` (`ThemeRuntimeApplier.cs:86`). So every literal radius, and every
  `{StaticResource PbRadius*}` (21 of them, on `PbRadiusSm`), ignores the theme's corner style.
- **Sweep:**
  - `{StaticResource PbRadius}`, `PbRadiusSm` and `PbRadiusLg` → `{DynamicResource …}`.
  - Uniform literal radii are mapped: 3–6 → `PbRadiusSm`; 7 and 8 → `PbRadius`; 9 → `PbRadiusChip`; 10 → `PbRadius`. (10 is closer to 7
    than to 14, and those 7 uses are panels that shouldn't jump to the large radius.)
  - Allowed to stay literal: `0`, `1`, `2` (hairlines, progress tracks and other elements a few pixels tall, where a theme radius
    would turn them into pills), and per-corner values such as `10,10,0,0` (handled per screen).
- `PbRadiusChip` isn't written by any theme today, so its 29 `StaticResource` uses stay as they are.

### 3. Faint versus muted

- Contrast measured on `surface2` across the ten v1 themes: faint is 3.1–3.8:1 everywhere except Maximum Contrast (5.6). Muted is
  5.1–11.1. Faint is too dim for anything people need to read.
- **Sweep:**
  - every `TextBlock` (and `SelectableTextBlock`) whose `Foreground` is `PbTextFaintBrush` → `PbTextMutedBrush`;
  - every style `Setter` for `Foreground = PbTextFaintBrush` whose selector targets a `TextBlock`, a text run, or a class applied only
    to `TextBlock`s → muted.
- Not swept: borders, separators, icons (`SymbolIcon`, `PathIcon`), progress tracks and disabled states. They keep faint.
- The hierarchy between primary and secondary text still comes from text vs. muted colour plus size and weight. A third, unreadable
  tone isn't needed for that.

### 4. Form inputs (new `Styles/FluentBridge.axaml`, merged into `Application.Resources` after `AppTokens.axaml`)

The resource overrides (all brushes are `SolidColorBrush Color="{DynamicResource …Color}"`):

| Key(s) | Value |
|---|---|
| `TextControlBackground` | `PbSurface2Color` |
| `TextControlBackgroundPointerOver` | `PbSurface3Color` |
| `TextControlBackgroundFocused` | `PbSurface2Color` |
| `TextControlBackgroundDisabled` | transparent |
| `TextControlBorderBrush`, `…PointerOver`, `TextControlElevationBorderBrush` | `PbTextMutedColor`, fully opaque. The underline is a boundary of an input, so it needs 3:1 (WCAG 1.4.11). Opaque muted measures 5.1–11.1:1 on `surface2` across the v1 themes. At 55 % it fell to 2.2:1 on Windows 11, Daylight and Overcast, so translucency was rejected. |
| `TextControlBorderBrushFocused`, `TextControlElevationBorderFocusedBrush` | `PbAccentColor` |
| `TextControlBorderBrushDisabled` | `PbTextFaintColor` |
| `TextControlBorderThemeThickness` | `0,0,0,1` |
| `TextControlBorderThemeThicknessFocused` | `0,0,0,2` |
| `TextControlForeground*` | `PbTextColor` |
| `TextControlPlaceholderForeground*` | `PbTextMutedColor` (Q10) |
| `TextControlSelectionHighlightColor` | `PbAccentColor` at 45 % |

- **Placeholder italic:** `TextBox /template/ TextBlock#PART_Placeholder { FontStyle: Italic }`. The `PART_Placeholder` part name is
  confirmed in `FluentAvalonia.dll`.
- **Field shape:** `Style Selector="TextBox"` sets `CornerRadius="{DynamicResource PbFieldRadius}"` (a new `CornerRadius` token,
  `4,4,0,0`). Rounded top corners on a flat bottom line. This one isn't theme-written, because a per-corner shape can't be derived from
  the theme's single `radiusSm`.
- **Dropdowns:** the `ComboBox*` keys follow the same rules (`ComboBoxBackground*`, `ComboBoxBorderBrush*`,
  `ComboBoxBorderThemeThickness` → `0,0,0,1`, `ComboBoxBackgroundBorderBrushFocused` → accent, `ComboBoxPlaceHolderForeground*` →
  muted). `FormControls.axaml`'s existing `ComboBox` style keeps the radius on `PbFieldRadius` and the background on surface2.
- **`SuggestBox`:** its `ControlTheme` already passes `Background` and `BorderBrush` through to its inner `TextBox`. It gets
  `BorderThickness 0,0,0,1` and `CornerRadius PbFieldRadius`, so it inherits the look. `NumericUpDown` and `AutoCompleteBox` contain a
  `TextBox`, so they inherit it automatically. `NumericUpDown`'s spinner keeps its existing styling.
- **Toolbar search boxes stay as they are:** `TextBox.searchBox` sets
  `/template/ Border#PART_BorderElement { BorderThickness: 1; CornerRadius: PbRadiusSm }` for every state (rest, `:pointerover`,
  `:focus`). These part selectors outrank FluentAvalonia's state styles, which assign those properties from the resources above.
  Today's other non-form search boxes are found in the sweep by `Watermark="Search…"` and given the `searchBox` class.
- **Error state:** `TextBox:error /template/ Border#PART_BorderElement { BorderBrush: PbDangerBrush }`. Avalonia sets `:error` on data
  validation failures.

### 5. Popups and tooltips (`Styles/Overlays.axaml`)

- `Style Selector="FlyoutPresenter"`: Background `PbPanelFloatingBackground`, BorderBrush `PbPanelFloatingBorder`, BorderThickness 1,
  CornerRadius `PbPanelFloatingRadius`, Padding 12, BoxShadow `{StaticResource PbElevationShadow}` (the same shadow `Border.floatingPanel`
  already uses). This covers all 25 `<Flyout>`s, including the grey continuity popup.
  - `MenuFlyoutPresenter` keeps `Menu.axaml`'s own style. It already uses tokens.
- `Style Selector="ToolTip"`: Background `PbSurface3Brush`, BorderBrush `PbBorderBrush`, BorderThickness 1, CornerRadius
  `PbRadiusSm`, Padding `8,6`, Foreground `PbTextBrush`, FontSize `PbFontSizeMeta`, MaxWidth 320, with wrapping text.
- Custom hover cards (Library's preview popover) aren't `ToolTip`s and are out of scope here (sub-project 4, Library).

### 6. Dialog pieces (`Styles/Overlays.axaml`, `Controls/OverlayShell.cs`)

- **Close button:** the `OverlayShell` template moves the ✕ from `Margin="0,-14,-14,0"` (straddling the corner) to `Margin="0,12,12,0"`
  (inside the card, top-right). Its hit area stays 28×28 and it gets `AutomationProperties.Name="Close"`. This applies to all 9
  `OverlayShell` dialogs at once.
- **Shared dialog styles**, used by every dialog that adopts the anatomy in sub-project 4, and by any dialog touched here:
  - `Border.dialogCard`: surface2, border, `PbRadiusLg`, `ClipToBounds`, width by class `.sm` 420, `.md` 560, `.lg` 760.
  - `TextBlock.dialogTitle`: `PbHeadingFontFamily`, `PbFontSizeHeading` (22).
  - `TextBlock.dialogSubtitle`: meta, muted.
  - `Border.dialogFooter`: top border `PbBorderBrush`, padding `20,12,20,16`, buttons right-aligned with spacing 8.
  - Max height (window − 48, only the body scrolls) needs each dialog's body inside a `ScrollViewer`. It ships with the dialog pass in
    sub-project 4, together with moving each dialog onto `dialogCard`.
- **One field label:** `TextBlock.fieldLabel` moves to `Styles/FormControls.axaml`. It's the overline role: 10 px, SemiBold, uppercase
  (a casing converter isn't added: label text is already written in caps in most views, and the few title-case ones, such as Edit
  Series, are fixed in the sub-project 4 dialog pass), letter spacing 0.6, muted, margin `0,0,0,4`. The 12 local copies (two sizes, three weights) are deleted. Views that rely
  on a local copy's extra setters keep only those extra setters under a more specific class.
- **Error style:**
  - `Border.inlineError`: `PbDangerSoftBrush` background, `PbRadiusSm`, padding `8,6`, containing a `Grid` with a `SymbolIcon ErrorCircle`
    (danger) and a `TextBlock.errorText` (danger colour, meta size, wrap);
  - an optional `Expander.errorDetails` holding a `SelectableTextBlock` in `PbMonoFontFamily`.

  Adopting it at actual call sites (MangaDex connect, the Trackers strip, others) is sub-project 4.

### 7. Badge colour and page margin

- `Views/DetailBand.axaml`: the left stripe's `BorderBrush` and the Virtual Tags chip (border, 14 % background, text) move from
  `PbBadge*` to `PbAccent*` / `PbAccentSoftBrush` / `PbAccentTextBrush`. Count and notification badges keep `PbBadge*`.
- Page margin: in `Views/*Screen.axaml`, the horizontal page padding of 28 (patterns `28,y,28,y` and `28,y`) becomes 24.
  - Library, Compare, the PDF reader and the comic reader have their own frame layouts. Their margins move in their sub-project 4 passes.

### 8. Guard rails (tests, `src/Paperbunkr.App.Tests`)

- **`XamlConsistencyLintTests`** (fast, no UI). It walks every `.axaml` under `src/Paperbunkr.App` and fails with `file:line` lists for:
  1. a `FontSize` literal not in {10, 11, 12, 13, 15, 18, 22} and below 24;
  2. a uniform `CornerRadius` literal other than 0, 1 or 2;
  3. `{StaticResource PbRadius}`, `{StaticResource PbRadiusSm}` or `{StaticResource PbRadiusLg}`;
  4. a `<TextBlock` or `<SelectableTextBlock` element with `Foreground="{…Resource PbTextFaintBrush}"`;
  5. a `Selector="TextBlock.fieldLabel"` style outside `Styles/`.
- **`FoundationStyleTests`** (headless, using the real `AppTokens.axaml`, `FluentBridge.axaml` and `FormControls.axaml`):
  - a `TextBox` resolves a bottom-only border thickness, a muted placeholder brush and the accent focus brush;
  - after a live theme switch (Default → Daylight → Matrix through `ThemeRuntimeApplier`), the resolved `TextControlBackground` colour
    follows `PbSurface2Color`;
  - a `FlyoutPresenter` and a `ToolTip` resolve floating-panel and surface-3 backgrounds;
  - a `ContentControl` with `CornerRadius="{DynamicResource PbRadius}"` changes when the theme's radius changes.
- **Headless render check:** a form sample (a filled field, a placeholder field, a focused field, an error field, and a dropdown) is
  rendered to PNG in Default, Daylight and Matrix. Images go to the scratch folder, not the repo, and are reviewed before sign-off.
  This is how the 3:1 underline and the italic placeholder get verified without driving the user's app.
- Existing tests that assert old literal sizes or radii are updated to the snapped values. Only the targeted classes are run, never the
  full suite (CLAUDE.md, and [[feedback_no_wide_app_test_runs]]).

## Risks

- **Tight layouts:** snapping 7–9.5 px text up to 10 can overflow tiny badges. Each badge whose size went up is listed in the change
  summary for the sub-project 4 passes, and the lint test makes them easy to find.
- **Faint → muted makes some secondary text more prominent.** That's intended. A spot that genuinely needs to recede gets size or
  weight, not a lower-contrast colour.
- **FluentAvalonia key names are internal API.** If an upgrade renames them, `FoundationStyleTests` fail instead of the app silently
  going back to stock Fluent.
- **Concurrent work in the tree:** 44 `.axaml` files already carry other sessions' uncommitted edits. The sweep edits in place and only
  touches the literals and lines named above.

## Verification

- `dotnet build` (watch for the AVLN weave trap in CLAUDE.md), then `XamlConsistencyLintTests`, `FoundationStyleTests`, and any test
  classes whose assertions the sweep changed.
- Headless PNGs reviewed in the three themes.
- On-screen check in the running app, in Default, Daylight and Matrix: a dialog (Edit Continuity), Edit Series, Preferences >
  Connections, a continuity popup, a tooltip, and the Detail band. The user does this, or Claude does it with explicit permission for UI
  automation ([[feedback_no_unauthorized_ui_automation]]).
