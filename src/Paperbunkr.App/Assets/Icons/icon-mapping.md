# Icon action mapping

Every icon in the app is a `FluentIcons.Avalonia` `<fi:SymbolIcon Symbol="…" />`
(migration: `docs/superpowers/specs/2026-08-28-fluenticons-migration-design.md`, replacing the
former hand-computed `PbIcon*` `StreamGeometry` set and the `Border.icon` + `OpacityMask` raster
pack). Defaults — `FontSize="15"`, `IconVariant="Regular"` — come from `Styles/Icons.axaml`; colour
is inherited from the parent `Button` / `TextBlock`.

**One `Symbol` per action.** Before adding an icon, find the action below and reuse its `Symbol`. If
the action is new, add a row here. If an existing action needs a different glyph, change it here and
everywhere at once — don't fork.

| Action | `Symbol` | Notes |
|---|---|---|
| Search / find / filter results empty-state | `Search` | |
| Add / new / add condition | `Add` | |
| Add a folder | `FolderAdd` | |
| Add to a Collection (context-menu submenu) | `CollectionsAdd` | |
| More actions on a sidebar row (Collections "⋯" menu) | `MoreVertical` | same as the overflow-menu row below |
| Remove / delete (destructive) | `Delete` | red `Foreground` at the call site |
| Remove one item from a list | `SubtractCircle` | non-destructive list edit |
| Minus / collapse / decrement | `Subtract` | |
| Close / dismiss | `Dismiss` | |
| Cancel an editor / dialog | `DismissCircle` | |
| Save | `Save` | |
| Apply (bulk) | `Save` | same as Save |
| Confirm / done / mark read | `Checkmark` | |
| Success / found / confirmed state | `CheckmarkCircle` | |
| Warning / error / missing | `Warning` | amber or red `Foreground` at the call site |
| Info / help | `Info` | |
| Copy | `Copy` | |
| Edit properties | `Edit` | |
| Open folder / browse for a path | `FolderOpen` | |
| Search a folder / scan | `FolderSearch` | |
| Reveal in Explorer | `FolderOpen` | same as open folder |
| Refresh / re-scan / re-roll | `ArrowClockwise` | |
| Undo | `ArrowUndo` | |
| Redo | `ArrowRedo` | |
| Rotate clockwise | `ArrowRotateClockwise` | |
| Rotate counter-clockwise | `ArrowRotateCounterclockwise` | |
| Rate (star toggle) | `Star` | `IconVariant="Filled"` on the selected state |
| Bookmark | `Bookmark` | |
| Home (nav) | `Home` | |
| Library / comics (nav) | `Book` | |
| Books / novels (nav & empty-state) | `Book` | |
| Smart Lists / filter (nav) | `Filter` | |
| Reading Lists (nav) | `Bookmark` | |
| Story Events / layers | `Layer` | also Books rail |
| Preferences (nav) | `Settings` | |
| Plugins (nav & empty-state) | `PuzzlePiece` | |
| Pin / unpin the nav rail | `Pin` | |
| Sort (view menu) | `ArrowSort` | |
| Sort ascending (explicit) | `TextSortAscending` | |
| Grid view | `Grid` | |
| Fit / auto-fit (reader) | `AutoFit` | |
| Fullscreen (reader) | `FullScreenMaximize` | |
| Play / start (reader, continue) | `Play` | |
| Next / skip forward | `Next` | |
| Previous / skip back | `Previous` | |
| More actions (overflow menu) | `MoreVertical` | |
| Chevron left / right (paged nav, back/forward) | `ChevronLeft` / `ChevronRight` | |
| Double chevron (jump to start/end) | `ChevronDoubleLeft` / `ChevronDoubleRight` | |
| Reading direction: left-to-right | `ArrowRight` | via `ReadingModeIconConverter` |
| Reading direction: right-to-left | `ArrowLeft` | via `ReadingModeIconConverter` |
| Reading direction: top-to-bottom | `ArrowDown` | via `ReadingModeIconConverter` |
| Import / upload (CBL, covers) | `CloudArrowUp` | |
| Import a file | `DocumentArrowUp` | |
| Export a file | `DocumentArrowDown` | |
| Open a book / read | `BookOpen` | |
| Archive / archived state | `Archive` | |
| Globe / language overlay | `Globe` | |
| Settings (per-item, e.g. plugin command) | `Settings` | |
