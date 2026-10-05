# Preferences > Keyboard Shortcuts: master-detail redesign

Date: 2026-10-04. Builds on [2026-10-03-input-service-design.md](2026-10-03-input-service-design.md) §9 (the data-driven editor) and replaces the
presentation from [2026-09-07-keyboard-shortcuts-redesign-design.md](2026-09-07-keyboard-shortcuts-redesign-design.md).

## Problem

The page is one long scroll of 120 actions in 12 groups (Navigation alone has 28). Every action is a tall card with its chips stacked under the label, so a
group is a wall of near-identical blocks, nothing can be found without scrolling, and a colliding binding paints the whole row with a red outline and tint (plus a red
banner at the top), which reads as an error on rows that are only informational.

## Scope

Presentation only. Unchanged: the bindings model, `IInputService`, `keymap.json`, the capture box (`BindingCaptureBox`), conflict *detection*
(`IInputService.FindConflicts`), reset and import/export, and the Preferences search anchors (`shortcuts.<group>`).

## Design

A search/filter bar over a two-pane body: a grouped action list on the left, an editor for the selected action on the right.

```
[ Search actions or keys… ] [ All | Keyboard & mouse | Controller ] [ Customised only ] [ Import ] [ Export ] [ Reset all ]
 NAVIGATION · 28                    | Page back (vertical paging)
  ● Page back (spatial left)  Left +2 |  Navigation · comic reader, paged mode
    Page back (vertical)  ⚠ Up  +2   |  Bindings  [Up ⚠ ✕] [🎮 D-pad up ✕] [🎮 Left stick up ✕]
  ZOOM & FIT · 8                     |            [Add shortcut…] [Reset to default]
    …                                |  Defaults  Up · 🎮 D-pad up
                                     |  ⚠ "Up" is also bound to "Scroll up"… Select it
```

### List (left)

- Groups come from the action catalog, in catalog order, each with a header (`NAVIGATION · 28`; it scrolls with the list, since Avalonia has no native sticky header) carrying the group's anchor `Tag` so
  `PreferencesScreen.ScrollToAnchor` still finds it.
- One line per action: a dot when customised, the label, the first binding as a chip, and `+N` for the rest. A conflicted action shows a small neutral
  warning icon. Controller chips carry a gamepad icon and the existing pad tint.
- The list is a plain (non-virtualizing) `ItemsControl` of `Button` rows: 120 simple rows are cheap, anchors need their header realised, and it avoids the
  virtualizing-`ItemsControl` arrow-key focus loss (CLAUDE.md, Input handling). Up and Down step through the list's own rows from a tunnel handler on the list
  (as `WantedScreen` tunnels its arrows); the screen-wide `FocusReclaimer.TryMoveDirectionally` is not used for this, because it treats the Preferences sidebar's
  items as rows at the same height and hopped out of the list (found by the real-window test). Up from the first row goes to the search box.
- Focusing or activating a row selects it, so arrowing down the list updates the editor live.

### Filters

- **Search** matches the action label, the group title, and the formatted text of any of its bindings (`Ctrl+Wheel up`, `D-pad left`), case-insensitively.
  Empty groups are hidden. It is a normal `TextBox`, so `TextEntryMode` already makes it browse-only until Enter, F2, a click or Ctrl+F: on this page
  `PreferencesScreen`'s `FocusSearch` handler goes to this box (`ShortcutSearchBox`) before the screen-wide settings search.
- **Device** (`All`, `Keyboard & mouse`, `Controller`): an action matches when any of its bindings is of that device (`InputBinding.Device`).
  A segmented toggle using the existing `segToggle` button style.
- **Customised only**: actions whose bindings differ from their defaults (`IsCustomised`).
- The selected action is kept in the list while anything matches, even when a filter would hide it, so editing never makes the row you are editing vanish.
- Filters combine (AND). When nothing matches the list is empty, a "No actions match." line shows, and the editor keeps showing the selected action.

### Detail pane (right)

- Title, and a "where it works" line built from the action's `Scope` and `Context` (`Navigation · comic reader, paged mode`; `Always` context adds nothing).
- Chips with a remove button each (existing `RemoveChipCommand`), **Add shortcut…** which becomes the capture box in place (existing
  `BeginCapture`/`BindingCaptureBox`), **Reset to default** (shown when customised), and the defaults as read-only chips.
- Controller axis chips (sticks, triggers) carry a lock icon and the tooltip "Fixed to this stick or trigger", and no remove button (a fixed binding cannot be
  re-added, since the action has no Add button: existing `CanCapture`). "Reset to default" still restores them.
- **Conflicts:** an ordinary surface block with a warning icon, only when the action collides with another: it names the other action, says whether it can
  shadow this one (the other is `Always` in the same scope) or merely overlaps, and has a **Select it** link that selects that action. Never blocking.

### Conflict styling

The red row border and tint (`Border.ksConflict`), the red banner, and the red chip styling are removed. Conflict is shown by the neutral warning icon on the
list row and on the offending chip, and by the detail-pane block above. Colours are skin tokens, never hex.

### Controller and keyboard

- Reachable entirely from the keyboard, so the app-wide pad host needs no code: D-pad/stick moves focus, A activates.
- Right from a list row moves into the detail pane; Left from the pane returns to the selected row (`Left` inside the capture box is a capture input and is
  left alone: `BindingCaptureBox` is an `IInputSuppressor` with `InputSuppression.All`).
- When the controller is disabled, the capture prompt already says so; a row whose only chips are controller chips adds no extra text.

### Narrow windows

The two columns stack (list above, detail below) under a width threshold. The selected row stays in view.

## State and data flow

`ShortcutsEditorViewModel` keeps `Groups` (every row, unchanged, so existing tests and `Refresh` still work) and adds:

- `SearchText`, `DeviceFilter` (`ShortcutDeviceFilter`: All, KeyboardMouse, Controller), `CustomisedOnly`
- `FilteredGroups`: the groups the list shows, rebuilt when a filter changes and after `SyncAll`
- `SelectedRow` (and `SelectRow(ShortcutRowViewModel)`), defaulting to the first action, or the last one edited this session (kept on the VM, not persisted)
- `HasNoMatches`

`ShortcutRowViewModel` adds `SummaryChip`/`ExtraText`/`HasExtra`, `ScopeText`, `IsSelected`, `Conflicts` (other action, whether it shadows) and
`HasConflictDetails`. `ShortcutChip` gains `IsPad`, `IsLocked` (analogue axis) and an observable `IsConflicted`, so it becomes a small observable class with
the same `Binding` and `Label`. `ConflictError`/`HasConflictError`/`IsConflicted` stay (tests rely on them); the view just no longer paints them red.

`SyncAll` already runs a dispatcher tick after a click, never inside the routing of the remove button; `FilteredGroups` is rebuilt there, so removing a chip
cannot detach its button mid-event (CLAUDE.md, routed-event gotcha). Filter changes come from the filter controls, which are not inside the list.

A Preferences search-anchor hit (`shortcuts.navigation`) selects that group's first visible action and brings its header into view.

## Deviations from ComicRack CE (verified in `_reference/ComicRackCE`)

CE (`KeyboardShortcutEditor`, `PreferencesDialog`) is a grouped list of commands with an editor panel for the selected one; four shortcut slots per command;
a modal "press your key combination" capture; **no search, no filters, no conflict detection** (first matching command wins); Restore Default Layout; XML
import/export; edits applied on OK. Deliberate deviations kept here: search, device filter and Customised-only; conflict warnings; no cap on shortcuts per action;
immediate apply (as every Preferences control). The master-detail shape itself is closer to CE than the old card stack.

## Testing

- `ShortcutsEditorViewModelTests`: filter combinations (search by label, by key text, by pad text, device, customised-only, combined), selected row kept when
  filtered out, default and remembered selection, conflict details naming the other action and `shadows`, summary text (`+N`), axis chips locked, anchor
  selection. Existing tests stay green.
- A real-window test (`RealWindowKeyboardTests` pattern): arrows walk the list, Right enters the pane, Left returns to the same row, and Esc/Enter behave as before.
- `avalonia-pro-max/review-checklist` before calling the UI done; check the section in both skins and at a narrow width.

## Not in scope

Persisting the last selection across launches, a visual keyboard diagram, per-device column layout, and any change to how conflicts are detected.
