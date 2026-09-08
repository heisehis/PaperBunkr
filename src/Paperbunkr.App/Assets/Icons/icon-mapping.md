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
| Disconnect (clear a saved credential) | `PlugDisconnected` | red `Foreground` at the call site, e.g. Connections dialogs |
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
| Plugins (nav & empty-state) | `PuzzlePiece` | also Preferences' sidebar Plugins item |
| Preferences sidebar: General | `Settings` | same glyph as the Preferences nav item itself |
| Preferences sidebar: Appearance | `PaintBrush` | |
| Preferences sidebar: Library | `Folder` | |
| Preferences sidebar: Automation | `Clock` | |
| Preferences sidebar: Reader | `Book` | |
| Preferences sidebar: Keyboard Shortcuts | `Keyboard` | |
| Preferences sidebar: Connections | `Link` | |
| Preferences sidebar: Advanced | `Wrench` | |
| Preferences sidebar: About | `Info` | |
| Preferences → General: reopen last screen on startup | `Play` | |
| Preferences → General: resume where left off | `Book` | |
| Preferences → General: auto-advance to next issue | `ArrowNext` | |
| Preferences → General: prompt to rate on finish | `Star` | |
| Preferences → General: drag-and-drop import | `ArrowUpload` | |
| Preferences → General: minimize to tray | `WindowArrowUp` | |
| Preferences → Appearance: install/open skin folder | `FolderOpen` | same as other folder-open actions |
| Preferences → Appearance: font family | `TextFont` | |
| Preferences → Appearance: reduce motion | `Pulse` | |
| Preferences → Appearance: nav rail hover-expand | `Navigation` | |
| Preferences → Appearance: developer/design showcase | `Code` | |
| Preferences → Reader: RTL page-turn direction | `ArrowLeft` | |
| Preferences → Reader: high quality page display | `Image` | |
| Preferences → Reader: default fit mode | `ArrowFit` | |
| Preferences → Reader: auto-rotate landscape | `ArrowRotateClockwise` | |
| Preferences → Reader: double-page spread | `DualScreen` | |
| Preferences → Reader: page transition style | `SlideTransition` | |
| Preferences → Reader: page transition speed | `TopSpeed` | |
| Preferences → Reader: reset zoom on page change | `ZoomFit` | |
| Preferences → Reader: mouse wheel scroll speed | `ArrowSort` | |
| Preferences → Reader: brightness | `BrightnessHigh` | |
| Preferences → Reader: contrast/saturation (no dedicated glyph) | `Options` | contrast; `ColorBackground` for saturation, `Gauge` for gamma |
| Preferences → Reader: canvas/margin background color | `ColorBackground` | also saturation slider above |
| Preferences → Reader: gamma | `Gauge` | |
| Preferences → Reader: page margin toggle | `BorderAll` | |
| Preferences → Reader: page margin width | `ArrowExpand` | |
| Preferences → Advanced: graphics backend | `DeveloperBoard` | |
| Preferences → Advanced: prefer native OpenGL | `Cube` | |
| Preferences → Advanced: write metadata to files | `DocumentData` | |
| Preferences → Advanced: write metadata automatically | `ArrowSync` | |
| Preferences → Advanced: write paperbunkr.json sidecar | `DocumentDatabase` | |
| Preferences → Advanced: write all metadata now | `Save` | |
| Preferences → About: version number | `Tag` | |
| Preferences → About: check for updates | `ArrowSync` | same as write-metadata-automatically |
| Preferences → About: check for updates on startup | `ArrowClockwise` | |
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
