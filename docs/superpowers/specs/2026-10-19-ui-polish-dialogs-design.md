# UI polish, sub-project 4a: Dialogs — Design

Date: 2026-10-19. This builds on the foundation, shared-controls and icons specs. The anatomy decisions were made in the foundation spec
(Q9 A, Q13, Q14). Every recommendation in this round (Q1–Q6) was accepted.

## Problem

There are 36 dialog-like views, and they share almost nothing:

- 15 widths, from 280 to 980.
- Six title styles.
- 13 have no shared card.
- Height caps are either fixed numbers (640–760) or missing, so tall dialogs run off the window (audit shot 2).
- The ✕ is added by `OverlayShell`, not by the dialog. Before the foundation pass it sat outside the card.

## Decisions

| Q | Decision |
|---|---|
| Q1 | A shared `DialogCard` control owns the anatomy. Each dialog moves onto it. A lint rule stops a dialog using `floatingPanel` as its root. |
| Q2 | **In:** properties and editor overlays; New list / continuity / event / goal; confirm, merge, paste data, workspace name, quick rate, list options, edit layouts; the scraper dialogs; legal viewer; update available; migration; template gallery; continuity compare; inline dialogs (Edit Continuity, MangaDex connect). **Out:** Quick Open, the welcome tour, Welcome / What's New, the cover and status pickers (those stay floating panels). |
| Q3 | Small 420: confirm, quick rate, workspace name, update available, merge. Medium 560: properties, new X, goal, paste, list options, edit layouts. Large 760: manga series editor, reading-list properties, template gallery, legal viewer. XLarge 960: continuity compare and the scraper review tables. |
| Q4 | The footer is right-aligned: Cancel (`ghost`), then the primary action. A destructive primary uses `danger`. Back and other secondary actions go at the left of the footer. |
| Q5 | The title states the action in sentence case. The item's name goes in the subtitle (this fixes `Edit ""Batman" Dark Nights"`). |
| Q6 | Fix the screenshot defects: the template gallery grid, the Designation hint, MangaDex labels and the empty box, the goal title placeholder, the Change Cover nested card and clipped tab, and raw errors (shown with `inlineError`). |

## `controls:DialogCard`

A `TemplatedControl` in `Controls/DialogCard.cs` (+ a ControlTheme in `Styles/Overlays.axaml`). It derives from `ContentControl`.

| Property | Type | Notes |
|---|---|---|
| `Title` | string | `dialogTitle` style (Bebas, heading size) |
| `Subtitle` | string? | `dialogSubtitle`, hidden when empty |
| `Size` | `DialogSize` (Small 420 / Medium 560 / Large 760 / XLarge 960) | sets `Width` |
| `Content` | object | the body, inside a `ScrollViewer` (vertical auto, horizontal disabled) |
| `Footer` | object? | right-aligned footer content (Cancel + primary) |
| `FooterStart` | object? | left-aligned footer content (Back, Delete…) |
| `CloseCommand` | ICommand? | ✕ in the header; when it isn't set, the card adopts the enclosing `OverlayShell`'s `CloseCommand` |
| `IsBodyScrollable` | bool (true) | off for dialogs that host their own list scroller (the template gallery, compare) |

**Template:**

- The root is `Border.dialogCard`.
- Inside it is a 3-row grid:
  - **Header:** padding `20,16,12,12`. Title and subtitle on the left, the ✕ (`iconButton`, `Dismiss`, name "Close") on the right.
  - **Body:** padding `20,4,20,12`, scrolling.
  - **Footer:** a `Border.dialogFooter`, with `FooterStart` docked left and `Footer` right, both in horizontal stacks with spacing 8. It's
    hidden when both are empty.

**Behaviour:**

- **Max height:** on attach the card finds its `TopLevel`, subscribes to its `ClientSize`, and sets `MaxHeight = ClientSize.Height − 48`.
  It unsubscribes on detach.
- **Shell close button:** on attach the card finds an ancestor `OverlayShell`, sets the shell's `ShowCloseButton = false`, and adopts its
  `CloseCommand` when its own is null. The shell's restore-on-detach puts the old value back.
- **Keyboard:** Escape is already handled by the shell and screens through the input service. Nothing new.

## Per-dialog migration

- Each in-scope view replaces its root `Border.floatingPanel` (or bare `Border`) plus its hand-built header and footer with a
  `DialogCard`.
- Field content is kept as it is. Labels use `fieldLabel`, written in caps.
- Hand-set `MaxHeight`s, title `FontSize`s and footer `StackPanel`s are removed.

## Defect fixes

- **Template gallery:** a `UniformGrid Columns="2"`, or two star columns, with equal 12-px gaps. Cards stretch to the row height.
- **New Continuity:** the Designation explanation becomes `TextBlock.hint` (muted, caption size), not value text.
- **MangaDex connect** (Preferences > Connections pane):
  - each field gets a `fieldLabel` (Client ID, Client secret, Username, Password);
  - the empty outlined box is the provider's ToggleSwitch with no content: give it its label, or remove it if it's dead;
  - errors use `Border.inlineError` with the raw text in `Expander.errorDetails`.
- **Goal editor:** the generated title shows the number only once a target exists. Otherwise the placeholder reads "Read N issues this
  year", without "?".
- **Change Cover popup:** one card, not two. The tab strip becomes a `segGroup` with three `segItem`s that fit the width.
- **Tracker sync errors on Detail:** use `inlineError` with "Details".

## Guard rails

`XamlConsistencyLintTests.DialogsUseDialogCard`: a `*Overlay.axaml` / `*DialogView.axaml` file outside the Q2 exclusions must contain
`controls:DialogCard` and must not have `Border Classes="floatingPanel"` as its first element.

## Testing

- **`DialogCardTests` (headless):**
  - width per size;
  - `MaxHeight` follows the window height minus 48 and updates on resize;
  - inside an `OverlayShell`, the shell's ✕ is hidden and the card's ✕ runs the shell's `CloseCommand`;
  - the footer is hidden when empty.
- **Existing tests for the migrated dialogs:** goal editor, smart template gallery, reading-list properties, merge series and others, run
  by class.
- **A render sheet** of three dialogs (Small confirm, Medium New reading list, Large template gallery) in Default, Daylight and Matrix.
