# UI polish, sub-project 3: Icons and accessible names — Design

Date: 2026-10-19. This builds on the [foundation](2026-10-19-ui-polish-foundation-design.md) and [shared
controls](2026-10-19-ui-polish-shared-controls-design.md) specs. The audit sections it covers are §6, §7, §11a (glyphs) and §11e
(rows without a logo). All six decisions below were accepted as recommended.

## Problem

- **Text characters used as icons:** about 95 uses of 20 characters (→ ＋ ✕ ▾ ▲ ▼ ✎ ⋯ ▶ ⚙ 🔖 ✓ ↻ ⠿ ● ★ …).
  - These draw in the *text* font, including the user's font override and Matrix's monospace, so their size, weight and baseline drift
    away from the FluentIcons beside them.
- **Icon-only buttons with no `AutomationProperties.Name`:** 72 of them, so a screen reader announces "button".
  - 33 have a tooltip whose text can serve as the name.
- **Rows that shift when a logo is missing:**
  - Continuity sidebar rows hide the logo slot when a publisher has no image, so that row's label starts further left (`MainWindow.axaml`
    ~679).
  - Tracker logos in Connections are drawn at different sizes.

## Decisions

| Q | Decision |
|---|---|
| Q1 | A character becomes an icon only where it *acts as an icon*: alone, as button content, or as the leading symbol of a label. Characters inside running text stay, for example "Old → New" and the Quick Open hint line "↑↓ navigate ↵ open". |
| Q2 | The ▾ on dropdown chips becomes a 12 px `ChevronDown` icon after the label, matching the Workspace button. |
| Q3 | Unnamed icon buttons get a name: the tooltip text where one exists, otherwise a short action name ("Move up", "Rename", "Close"). A lint rule requires a name on every icon-only button. |
| Q4 | Rows that can show a logo keep a fixed, empty logo slot when there is none. There's no placeholder icon. Tracker logos share one box size. |
| Q5 | The ▲ ▼ ✎ move/rename buttons become `Button.iconButton.sm` with `ChevronUp`, `ChevronDown` and `Edit` icons. |
| Q6 | A lint rule bans the replaced characters as button content or as a standalone label's text. Arrows inside running text stay allowed. |

## Mapping

| Character | Where it acts as an icon | Becomes |
|---|---|---|
| ✕ | close and remove buttons | `iconButton` (`.sm` inside chips and rows) with `Dismiss` |
| ＋ | "＋ Add series", "＋ New continuity", … | a button whose content is a `StackPanel`: `SymbolIcon Add` (14) followed by the label |
| ⋯ | "⋯ Manage", the more-actions buttons | `MoreHorizontal`, plus the label where there is one |
| ▲ ▼ ✎ | reorder and rename in list rows | `iconButton sm` with `ChevronUp`, `ChevronDown` or `Edit` |
| ▾ | dropdown chip labels | label, then `ChevronDown` (12) |
| ▶ | "▶ Read" | `Play`, then the label |
| ⚙ | "⚙ Configure" | `Settings`, then the label |
| 🔖 / ✓ | the book reader's bookmark toggle text | `Bookmark` / `Checkmark` icon, then the text |
| ✓ | the poster rail's done tick | `Checkmark` |
| ↻ | "↻ Refresh provider details" | `ArrowClockwise`, then the text |
| ⠿ | the Reading List drag handle | `ReOrderDotsVertical` (the instruction sentence keeps ⠿, since it describes the handle in prose) |
| ● | the dot in the Library list's count badge | a 6×6 `Ellipse` filled with the badge text colour |
| ★ | the rating in the Library preview panel | `Star` (`IconVariant="Filled"`, 12), then the number |
| ↔ | the mapping arrows on the migration and Library folder screens | `ArrowSwap` |
| → | arrows that stand alone as a mapping marker | `ArrowRight`; inside sentences it stays |

Icons sit in a horizontal `StackPanel` with `Spacing="6"`, sized 14 (`PbIconSizeXs`) or 12 next to 12-px text, and are centred vertically.
They inherit the button's foreground, so the shared-control states (accent, on-accent, muted) keep working.

## Names

- The tooltip is copied into `AutomationProperties.Name` when the button has one.
- The remaining buttons get names written from their command and context. A list of every name added goes into the plan's change
  report.
- **Lint rule:** an icon-only `Button` — whose content is a `SymbolIcon`, `PathIcon`, `ThemedIcon`, `Image`, `Path` or `BrandMark`, or a
  single replaced character — must have `AutomationProperties.Name`.

## Rows without a logo

- **Continuity sidebar:** the `BrandMark` stays in the layout but is hidden with `Opacity="0"` instead of `IsVisible="False"`. Its grid
  column gets `Width="34"`, which equals `MaxMarkWidth`, so the slot always takes the same space and every label lines up.
- **Connections tiles:** every tile's `BrandMark` gets the same `MarkSize` and `MaxMarkWidth`, so logos draw in one box.

## Guard rails

`XamlConsistencyLintTests` gains two rules:

1. **`IconButtonsHaveNames`**: the icon-only button rule above.
2. **`NoTextGlyphIcons`**: none of ✕ ＋ ⋯ ▲ ▼ ✎ ▾ ▶ ⚙ 🔖 ✓ ↻ ● ★ ↔ may be a `Button`'s whole `Content` or its leading character, nor a
   standalone `TextBlock`'s whole `Text`. The ⠿ in the Reading List instruction sentence and the hint line in Quick Open are allowed.

## Out of scope

- The reader overlay chrome's icons beyond names. Those are covered by the reader pass in sub-project 4.
- Arrows in prose.
