# UI polish, sub-project 2: Shared controls — Design

Date: 2026-10-19. Builds on [2026-10-19-ui-polish-foundation-design.md](2026-10-19-ui-polish-foundation-design.md) (type scale, radii, faint
vs muted, underlined fields). Inventory and screenshots: [2026-10-19-ui-polish-audit.md](2026-10-19-ui-polish-audit.md) §5 and §11.
Decisions come from one text round and one browser round with the user (2026-10-19).

## Summary

Controls that do the same job have been rebuilt separately, screen by screen, and the copies have drifted apart:

- **Segmented switches:** about 14 classes, with four different "selected" looks.
- **Underline tabs:** 5 variants.
- **Chips:** about 30 classes, with 3 radii and about 10 paddings.
- **Cards:** `card` is defined 3 times and `groupBox` 8 times, on three surface levels.
- **Buttons:** `headerAction` is redefined in 5 places, plus hundreds of per-button padding and font-size overrides.

This sub-project defines **eight shared control families** in one new style file, moves every screen onto them, deletes the local copies,
and adds lint rules so the copies can't come back.

## Decisions

| Q | Decision |
|---|---|
| Q1 | Eight families: button, segmented switch, tab, sidebar item, chip (four kinds), card, clickable card, menu row. The floating panel already exists and stays. |
| Q2 | Call sites move to the shared class. Local definitions are deleted, not kept as aliases. A lint test blocks local restyling of shared families. |
| Q3 | Every screen is migrated, not only Library, Reader and dialogs. Layout fixes stay in sub-project 4. |
| Q4 | Outlines of ghost and secondary buttons, chips and segmented-switch frames use the faint text colour (3.1–3.8:1). Dividers keep `PbBorder`. |
| Q5 | Sidebar items are in scope. |
| Q6 | Segmented switch: the selected option is **solid accent** (accent fill, on-accent text). |
| Q7 | Buttons are **compact: 28 px normal, 22 px small**. Icon-only buttons are 28×28 (22×22 small), clickable at that size (see §1). |
| Q8 | Chips use the **rounder squircle**, radius `PbRadiusChip` (9). Badges use a 7-px radius. No ovals. |
| Q9 | Sidebar item: the selected item gets an **accent bar plus a soft accent tint**. |
| Q10 | Card: **raised** — `PbSurface2` (one step above the page) with a `PbBorder` border. |
| — | Tabs (Claude's call, shown to the user for context): an underline tab, with the active tab in semi-bold text colour plus a 2-px accent line, inactive tabs muted, and an optional count badge. |

## Where it lives

- New `Styles/Controls.axaml`, included in `App.axaml` right after `Primitives.axaml`. It holds every family below.
- The base `Button.primary` / `.secondary` / `.ghost` styles move there from `Primitives.axaml`, so one file owns buttons.
- All colours, radii and sizes come from tokens. Two new size tokens go in `AppTokens.axaml`:
  - `PbControlHeight` (28) and `PbControlHeightSm` (22);
  - `PbControlBorderBrush`, which equals `PbTextFaintBrush` today but is a separate name so a theme can override it later without touching
    the text colour.

## The families

### 1. Button — `Button.primary | .secondary | .ghost | .danger`, size `.sm`, plus `Button.iconButton`

| | Normal | Small (`.sm`) |
|---|---|---|
| Height | 28 (`PbControlHeight`) | 22 (`PbControlHeightSm`) |
| Horizontal padding | 12 | 8 |
| Font | Body 13, SemiBold | Meta 12, SemiBold |
| Radius | `PbButton*Radius` (the existing theme component roles) | same |

- **primary:** the theme's primary role: accent fill, on-accent text.
- **secondary:** surface2 fill, a `PbControlBorderBrush` outline, text colour.
- **ghost:** transparent, a `PbControlBorderBrush` outline, muted text. The outline used to be `PbBorder`, which is 1.35:1 in Matrix and why
  "Cancel" looked like plain text there.
- **danger:** transparent, with a danger outline and danger text. It replaces `discardConfirm` and `destructive`.
- **Toggle state:**
  - `.active` and `.open` on secondary and ghost give an accent-soft fill, an accent outline and accent text. This covers the reader's
    choose-one options, dropdown triggers that are open, and pinned tools.
  - Primary has no toggle state.
- **`Button.iconButton`:**
  - square, 28×28 (22×22 with `.sm`), transparent, with a muted glyph that turns to text colour on hover;
  - the clickable area is the visible square. That's below the 32-px guideline in `avalonia-pro-max/accessibility`, a deliberate trade-off
    that comes with the compact sizes the user picked (Q7). A 32-px hit area around 28-px chrome would need a custom Button template,
    because a Button's hit area is whatever its template draws;
  - `AutomationProperties.Name` is required. Sub-project 3 fills the 56 that are missing.
- **`headerAction` is retired.** `headerAction primary` becomes `primary`, and so on, and its 5 local redefinitions are deleted.
- **Per-button overrides are removed.** `Padding`, `FontSize`, `Height`, `MinHeight`, `CornerRadius`, `Background`, `BorderBrush` and
  `BorderThickness` on a family button are deleted. A button that had `FontSize` ≤ 11 or a `Height` ≤ 24 gets `.sm` instead.
  `Width` and alignment stay.
- **Reader overlay chrome stays a separate family for now.** `chromeIcon`, `clusterIcon` and `clusterPill` float over page art with their
  own translucent surfaces. They only get their sizes aligned to the 28 / 22 scale here; the reader pass in sub-project 4 decides the rest.

### 2. Segmented switch — `Border.segGroup` holding `Button.segItem` (`.sm` for small)

- **Group frame:** surface2, a `PbControlBorderBrush` outline, `PbRadius`, padding 2, spacing 2.
- **Item:**
  - transparent, muted text, `PbRadiusSm`, height 24 (18 with `.sm`; the group frame adds 4 to match the 28 / 22 controls beside it),
    horizontal padding 12 (8 with `.sm`);
  - hover: text colour;
  - **`.active` / `.on`:** accent fill, on-accent text, SemiBold (Q6).
- **Mapped to `segItem`:** `seg`, `segTab`, `ecTab`, `lensTab`, `coverPickerTab`, `ksToggle` and `modeToggle`.
- **`segToggle` keeps its name too.** Those buttons become `segItem segToggle`, because `TabStrip` (`Services/Input/TabStrip.cs:50`) uses
  `segToggle` to let the controller bumpers and Ctrl+PageUp/PageDown step through them. The new items don't gain that behaviour: it stays
  wherever it already was.
- Each group's existing container becomes `Border.segGroup` (`ksSeg` and the ad-hoc borders).

### 3. Tab — `Button.tab`

- Transparent, with no border except the selected line; padding `0,6,0,9`; Body 13; muted text.
- **`.active`:** text colour, SemiBold, and a 2-px accent bottom line (`BorderThickness 0,0,0,2`, `BorderBrush` accent).
- A tab strip sits on a 1-px `PbBorder` line (`Border.tabStrip`), with 20 px between tabs.
- **Optional count:** `Border.countPill` inside the tab content.
- **Mapped to `tab`:** `ipTab`, `detailsSubTab` and `activityTab`. `TabStrip` already recognises `tab`. `SettingsTabStrip`'s `TabStripItem`
  gets the same look through a `TabStripItem` style in `Controls.axaml`.
- Fixes the Series detail problem where "Issues" was underlined and "Details" was filled at the same time (shot 8): there is one active look,
  and the stray background is removed.

### 4. Sidebar item — `Button.sideItemButton`

- The style moves from `MainWindow.axaml` (plus Preferences' local copy) into `Controls.axaml`.
- Transparent, padding `PbSidebarItemPadding` (the density token), muted text, `PbRadiusSm`.
- **`.active` (Q9):** an accent-soft background, a 3-px accent bar on the left drawn with an inset `BoxShadow` (CLAUDE.md: an inset BoxShadow
  draws nothing on a control that has a BorderThickness, so the item keeps BorderThickness 0), text colour and SemiBold. The corners on
  the bar side are square (`0,5,5,0`).
- `prefNavItem` (Preferences) gets the same look and keeps its class for `TabStrip`.
- A row without a logo or icon keeps a fixed icon column, so labels line up (shot 20). The column is 16 px wide and stays empty.

### 5. Chips — four kinds

| Class | Job | Look |
|---|---|---|
| `Button.filterChip` (also `ToggleButton.filterChip`) | toggles a filter | height 24, padding `10,0`, `PbRadiusChip`, transparent, `PbControlBorderBrush` outline, muted Meta 12; `.active`/`:checked` gives accent-soft fill, accent outline, accent text, SemiBold |
| `Border.pbChip` (tag) | a static label, optionally removable | height 24, padding `10,0`, `PbRadiusChip`, surface3 fill, no outline, text colour Meta 12; a remove button is a `Button.iconButton.sm` with a Dismiss glyph |
| `Border.statusBadge` (+ `.accent` `.success` `.warning` `.danger`) | a state ("Ongoing", "Current", "Scheduled") | height 20, padding `7,0`, radius 7, the matching soft fill and colour, Caption 11 SemiBold; one indicator only, so either a dot or an icon, never both (shot 17) |
| `Border.countPill` | a number | height 18, min width 18, padding `6,0`, radius 7, Micro 10 Bold. The neutral variant is surface3 with muted text; `.badge` is `PbBadge` fill with `PbBadgeText` (Q4 of the foundation: the badge colour is only for counts) |

- **To `filterChip`:** `Button.pbChip`, `filterChip`, `chapterFilterPill`, `rangeChip`, `chip` (goal editor), `rlChip` and `chipBtn`.
- **To `pbChip`:** `previewChip`, `valueChip`, `heroChip`, `bandChip`, `tagPill`, `vtChip`, `Border.chip` and `ksChip`.
- **To `statusBadge`:** `statusBadge`, `scheduledTaskBadge`, `currentBadge`, `contrastChip`, `changelogTag` and `historyTag`, each with the
  colour variant its old state classes implied.
- **Stays separate:** `heroBadge` on the always-dark Detail hero art (its translucent white is intentional, and in the colour-lint baseline).

### 6. Card — `Border.card` (+ `.tight`)

- surface2, a `PbBorder` outline (Q10 A), `PbRadius`, padding 16 (`.tight`: `12,10`).
- **To `card`:** `groupBox` (8 local definitions), `sectionCard`, `rowCard`, `ksPanel`, `groupCard`, `reviewCard`, `miniCard` and
  `issueCard`. `statCard` is unused and deleted. The 3 existing `card` definitions collapse into the one in `Controls.axaml`.
- A view that needs a card *header* uses `TextBlock.pbTextSection` inside it. There's no card-header class.

### 7. Clickable card — `Button.clickCard`

- The card's look plus `Padding 12,10`. On hover the outline goes to `PbControlBorderBrush` and the surface to surface3; `.active` gives an
  accent outline. The focus ring is the app adorner (unchanged).
- **Mapped:** `tplCard` (the New Smart List templates), `setupCard`, `connTile` and `continueCard`.
- The transparent wrapper buttons around covers (`Button.card`, `resultCard`, `rlCard`, `railCard`, `rlTile`) aren't cards visually. They're
  hit targets around art, and they stay as they are.

### 8. Menu row — `Button.menuRow` in `Border.dropdown`

- **`Border.dropdown`** gets the floating-panel look: surface, border, `PbPanelFloatingRadius`, padding 4.
- **`menuRow`** matches `MenuFlyoutPresenter MenuItem` (`Menu.axaml`), so custom dropdowns and context menus look the same:
  - height 28, padding `10,0`, `PbRadiusSm`, text colour Body 13, left-aligned;
  - hover is surface3;
  - **`.active`** is accent-soft with accent text.
- **Mapped:** `modeOption` (66 uses) and `drawerRow`, where it's a menu-style row (checked per use during migration; list rows inside
  drawers stay).

## Migration mechanics

- **A scratch script rewrites classes in `.axaml`** using the mappings above. Bound state classes (`Classes.active="{Binding …}"`,
  `Classes.open`) carry over unchanged, because every family uses the same state names (`active`, `on`, `open`).
- **C# references are updated by hand**, since there are few that touch renamed classes: `ipTab`, `detailsSubTab`, `groupBox` and
  `modeOption` all have 0 C# references. `pbChip` and `tagPill` are checked individually, and `card`, `tab`, `sideItemButton`, `segToggle`
  and `prefNavItem` keep their names. Tests that assert class names are updated alongside.
- **Local `<Style>` blocks for the old classes are deleted.** Where a local block styled *content inside* a control (for example
  `Button.seg fi|SymbolIcon`), it's kept and re-targeted to the new class only if the shared style doesn't already cover it.
- **Per-use overrides are stripped** on family buttons and chips (§1). Any override the script can't classify is listed for review, not
  guessed.

## Guard rails (extend `XamlConsistencyLintTests`)

1. No `Selector="Button.<family class>"` (primary, secondary, ghost, danger, iconButton, segItem, tab, sideItemButton, filterChip,
   clickCard, menuRow) and no `Selector="Border.<family class>"` (segGroup, pbChip, statusBadge, countPill, card, dropdown) outside
   `Styles/`. Nested content selectors such as `Button.tab TextBlock.x` are allowed.
2. No retired class name anywhere in `.axaml`: `headerAction`, `modeOption`, `seg` (as a whole class), `segTab`, `ecTab`, `lensTab`, `ipTab`,
   `detailsSubTab`, `activityTab`, `coverPickerTab`, `groupBox`, `sectionCard`, `toolbarPill`, `pillOption`, `sheetOption`, `chapterFilterPill`,
   `rangeChip`, `previewChip`, `valueChip`, `heroChip`, `bandChip`, `tagPill`, `vtChip`, `ksChip`, `discardConfirm`, and the others named
   above.
3. No `Padding`, `FontSize`, `Height`, `MinHeight` or `CornerRadius` attribute on a `<Button>` whose classes include primary, secondary,
   ghost, danger, segItem, filterChip or menuRow.

## Testing

- **`SharedControlsStyleTests` (headless, with the real `AppTokens`, `FluentBridge` and `Controls.axaml`):**
  - a primary and a ghost button measure 28 high (22 with `.sm`);
  - the ghost outline resolves to the faint colour;
  - an active `segItem` resolves to the accent fill;
  - an active `tab` has a 2-px accent bottom border;
  - an active `sideItemButton` draws the inset bar;
  - an active `filterChip` uses accent-soft;
  - `card` is surface2.
- **Headless render sheet:** every family in Default, Daylight and Matrix, to the scratch folder, reviewed before sign-off.
- **`TabStrip` tests:** the existing `TabStrip` / tab-strip keyboard tests run unchanged and must still pass (they cover `tab`, `segToggle`,
  `ipTab` and `prefNavItem`; `ipTab` now becomes `tab`).
- **Targeted classes only**, never the full suite (CLAUDE.md). That means the lint tests, the two style test classes, the `TabStrip` tests,
  and the view tests whose XAML changed most (Library toolbar, Selection bar, List layouts, Insights history, Preferences keyboard tests).

## Risks

- **Layout shifts:** controls get shorter (most were 30–34 px, now 28) and some chips grow to a fixed 24. Rows that relied on the old
  height may shift. That's expected and is checked in the renders and on screen. Real overflow goes to sub-project 4.
- **Solid-accent segments next to a primary button** compete a little. The user picked this knowingly (Q6), so it's noted, not redesigned.
- **Volume:** a few hundred call sites. The script reports a per-file count, and other sessions' uncommitted edits in the same files are
  preserved because the edits are in place.

## Not in scope

- Reader overlay chrome beyond sizing (sub-project 4, reader pass).
- Icons and missing automation names (sub-project 3). The new `iconButton` makes them easy to find.
- Spacing and layout inside screens (sub-project 4).
