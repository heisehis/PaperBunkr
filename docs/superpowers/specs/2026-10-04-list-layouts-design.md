# List Layouts

CE-parity named list layouts for the Library and Books screens: a column editor, named saved layouts, per-list
remembered layouts, thumbnail caption lines and tile text elements, all reached from the existing Workspace button.

Status: built 2026-10-04 (plan: `2026-10-04-list-layouts-plan.md`). Section 14 lists where the build differs from the text below.

## 1. Background and CE facts

Verified against `_reference/ComicRackCE` (2026-10-04). There is no standalone "list layout" type in CE; the saved
object is `ListConfiguration { Name, Config: DisplayListConfig }`.

- `DisplayListConfig` (`ComicRack.Engine/Database/DisplayListConfig.cs`) carries `View` (`ItemViewConfig`), `Thumbnail`,
  `Tile`, `StackConfig`, background image and quick-search/duplicate options. `ItemViewConfig`
  (`cYo.Common.Windows/Forms/ItemViewConfig.cs`) carries `Columns` (id, format id, visible, width; list order is column
  order), `ItemViewMode`, grouping/stacking ids, sort key and orders, `ThumbnailSize`, `TileSize`, `ItemRowHeight`.
  `ThumbnailConfig` carries `CaptionIds` (up to three column ids) and `HideCaptions`; tile text is `ComicTextElements` flags.
- **No fonts or colours** are part of a layout. **No built-in named layouts ship**; `Settings.ListConfigurations` starts empty.
- `ListLayoutDialog` ("List Options", Ctrl+L) has three tabs: Details (checkable ordered column list, Move Up/Down, Show All,
  Hide All), Thumbnails (First/Second/Third Line pickers, "Do not show any Text"), Tiles (checked list of 22 text elements, Default).
  Sort, group, stack and view mode are *not* edited in it.
- Save List Layout (`MainForm.cs:2836`) names the active list's config, replacing a same-named entry. Edit Layouts (`ListEditorDialog`,
  Ctrl+Alt+L) renames, deletes, reorders and offers "set on current list" / "set on all lists" (confirm, hideable). Each saved layout is a
  "Set '{name}' Layout" item; the first six get Ctrl+Shift+F6-F11.
- Every list (smart list, folder, id list) owns a `ComicListItem.Display` config, applied on selection and written back on change
  (`ComicBrowserControl.cs:1921-1955`, `UpdateViewConfig`). Applying a named layout *copies* it; there is no live link.

## 2. What Paperbunkr already has

- Library Details table with a header right-click column picker (visibility only); the visible set, in order, is
  `AppSettings.LibraryDetailsColumns`. Widths are fixed per column (`DetailsColumn.Width`), not persisted, not resizable.
  The field pool is `IssueListSortField` (about 63 fields), which already covers most CE columns.
- Cover styles Poster / Panorama / Tiles (`LibraryGridCoverFit`, `TilesItemTemplate` in `LibraryScreen.axaml`); view modes
  PosterGrid / List / DetailsTable; series vs issue granularity.
- Always-current persistence of sort/group/view mode/density/badges/filters on `AppSettings` (Saved List Layouts, 2026-08-17) and named
  per-screen `Workspace` rows with a `StateJson` blob (Saved Workspaces, 2026-09-03; `WorkspaceService`, `WorkspaceScreen`).
- A Workspace button + popup on both `LibraryToolbar.axaml` and `BooksScreen.axaml`.
- Books persists only sort and group (`BooksWorkspaceState`); it has no view modes, columns or tiles.

## 3. Scope

In: Details column editor (check, order, width, Show All/Hide All, drag-to-resize and drag-to-reorder in the header); thumbnail caption
lines; tile text elements; named layouts (save, edit, switch); per-list remembered layouts with "set on all lists"; one shared menu and
store for Library and Books.

Out (deferred): CE's series-statistics columns (200-213), and columns with no Paperbunkr data (cover, position, checked, state, icons, review,
linked, web, proposed values, gap information); per-column date format (CE `FormatId`); a Details table or cover styles on Books; stack config;
background image; the Ctrl+Shift+F6-F11 switch shortcuts; built-in preset layouts other than "Default".

## 4. Data model

One serialised record, two tables. Same posture as `Workspace`: tolerant read (unknown keys ignored, missing keys take the app default),
enums as strings, defaulted trailing fields so old blobs load.

```
ListLayoutState                        // Paperbunkr.App.Models, JSON in the two tables below
  Columns            : list of { Field: IssueListSortField, Visible: bool, Width: double }   // list order = column order
  CoverFit           : LibraryGridCoverFit                                                   // Poster | Panorama | Tiles
  CaptionFields      : IssueListSortField?[3]                                                // thumbnail lines 1-3, null = none
  HideCaptions       : bool                                                                  // CE "Do not show any text"
  TileElements       : set of TileTextElement                                                // see section 7
  SortField, SortDirection, GroupField                                                       // shared sort/group pool
  ViewMode           : LibraryViewMode
```

Books uses the same record with only `SortField`/`SortDirection`/`GroupField` populated (its own enums are mapped, as
`BooksWorkspaceState` already does); the other fields stay at defaults and are ignored there.

Tables (EF migration, `Paperbunkr.Data`):

- `ListLayout { Id, Screen: WorkspaceScreen, Name, SortOrder, StateJson }` - named layouts, per screen, user-orderable. Unique on (Screen, Name).
  `WorkspaceScreen.Reader` is never used here. No `IsBuiltIn`: the "Default" layout is not a row (section 6).
- `ListLayoutAssignment { Id, Screen: WorkspaceScreen, SelectionKey, StateJson }` - the per-list remembered layout, unique on (Screen, SelectionKey).
  `SelectionKey` is a stable string: `all`, `content:<ContentType>`, `collection:<id>`, `readinglist:<id>`, `smartlist:<id>`, and for Books
  `all` / `series:<id>` if Books has such selections. A deleted list's assignment is removed with it (service hook where the list is deleted);
  an orphan found on load is ignored, never fatal.

## 5. Behaviour

`ListLayoutService` (new, mirrors `WorkspaceService`'s shape): `Capture(screen, vmState) -> ListLayoutState`, `Apply(screen, state)`,
`ListNamed(screen)`, `SaveNamed(screen, name, state)` (replaces a same-named row), `Rename`, `Delete`, `Reorder`, `GetAssignment(screen, key)`,
`SetAssignment(screen, key, state)`, `SetOnAllLists(screen, state)`. The screen view models own capture/apply of their own fields; the
service only stores.

- **Selecting a list** loads its assignment; if none, the *global layout* (today's `AppSettings` values) is used and nothing is written.
- **Any layout-affecting change** (column visibility/order/width, cover style, captions, tile elements, sort, group, view mode) writes the
  current list's assignment, debounced (about 400 ms) and flushed on navigation away / app exit. The first change on a list creates its row.
- **Applying a named layout** copies its state onto the current list (assignment overwritten); it never links. Editing the list afterwards
  does not change the named layout. Deleting or renaming a named layout touches no assignment.
- **Save List Layout...** asks for a name (the shared name prompt) and captures the current list's state.
- **Set on all lists** copies the state onto every assignment key the screen can produce *and* the global layout, after a confirm dialog with
  a "don't ask again" option (the existing `ConfirmDialog` pattern; project feedback rule: use the overlay/Activity Center mechanisms).
- **Granularity:** `Columns` apply to issue granularity (Details table). Cover style, captions and tile elements apply at both series and
  issue granularity. Switching granularity does not create a separate assignment; it is one state per list.
- **Stale references:** a `Field` no longer in the catalog is dropped on load; a layout with zero visible columns loads as the Default column set.

## 6. Default layout

The global layout is the existing `AppSettings` state, unchanged, and is the fallback for any list without an assignment. The menu's
"Default" entry (not a table row) resets the *current list* to it by deleting that list's assignment. This keeps built-in-row machinery out of
the new table and matches the project's rule against inventing presets CE does not have.

## 7. Tile text elements and captions

CE's 22 `ComicTextElements` are mapped to what Paperbunkr tiles can show. The element set is an enum in the app (`TileTextElement`), defined from
the fields `TilesItemTemplate` and `SeriesCardSample` already render (series, title, issue count, publisher, year, format, progress, ...). The
implementation plan lists the exact members by reading those templates; CE elements with no data here are omitted, not stubbed. The default set
equals what tiles render today, so an unconfigured list looks identical to the current app.

Thumbnail caption lines replace `ShowTileTitles`: the old flag migrates to `HideCaptions = !ShowTileTitles`; line 1 defaults to the field the
caption shows today. Poster and Panorama use the three lines; Tiles use `TileElements`.

## 8. UI

**Workspace menu.** The existing Workspace popup (Library and Books) gains a "List layouts" section under the Workspaces list:
`List Options...`, `Save List Layout...`, `Edit Layouts...`, `Default`, then one row per named layout for the screen (click applies).
Rows follow the popup's existing `dropdownRow` styling. Applying/closing from a row defers the popup close one dispatcher tick
(CLAUDE.md "don't detach a control from inside its own routed event"); any list mutation from a row is posted the same way.

**List Options overlay** (an `OverlayShell`, tabs via the `tab`/`active` class convention so `TabStrip.Step` drives them):

- Library: `Details`, `Thumbnails`, `Tiles`. Opens on the tab matching the current cover style / view mode (CE behaviour).
  - Details: checkable ordered list of columns (Name, Description like CE), Move Up, Move Down, Show All, Hide All, per-row width.
    A note states columns apply to issue view.
  - Thumbnails: three searchable `SuggestBox` pickers (read-only, per the SuggestBox rule) and a "Do not show any text" check.
  - Tiles: checkable list of `TileTextElement` and a Default button.
  - Apply / OK / Cancel; Apply writes the current list's assignment immediately.
- Books: a single `Sort & group` tab; the tab strip is hidden when there is one tab.

**Edit Layouts overlay:** rename, delete (with the existing confirm), reorder (Move Up/Down), "Set on current list", "Set on all lists".

**Details table header:** drag a column's right edge to resize (minimum width 24, persisted as `Width`); drag a header to reorder. Both
write through the same debounced path. The existing right-click column picker stays and writes the same state. The preview-panel width
handling must not regress (it already syncs a column width in `LibraryScreen.axaml.cs`).

**Avalonia notes** (to be checked against `avalonia-pro-max/components`, `layout-patterns`, `accessibility` and run through
`review-checklist` before UI work is called done):
- Colours and radii only via the app's `Pb*` tokens/`DynamicResource`, never hex; chips use `PbRadiusChip`, never `CornerRadius` 999.
- Drag-to-reorder/resize use pointer capture on the header; remember a `Button` swallows a left press (Avalonia 12), so the header
  cells that must start a drag are tunnelled from an ancestor, not given an instance `PointerPressed` in XAML.
- No `BoxShadow` inset on bordered controls; focus uses the app-wide adorner ring, no hand-rolled inner border.
- Keyboard: every overlay control reachable without a pointer; a keyboard path exists for reorder (Move Up/Down buttons, already planned)
  and resize (width field), so drag is never the only way.

## 9. Input

Per CLAUDE.md all commands are `InputActionIds` + `InputActionInfo` in `InputActions.Core`, handled by the owning screen via
`ScreenInput.Attach`; no `KeyBindings`. New actions: `ListOptions` (default Ctrl+L), `SaveListLayout` (unbound), `EditListLayouts`
(Ctrl+Alt+L as in CE). Each handler declines (returns false) when its screen cannot do it right now (Books/Library only, granularity
irrelevant). Ctrl+L showed no existing binding in `Services/Input` on a grep; the plan must verify against the live keymap and
conflict detection before claiming it. Per-layout switch commands are not shortcuts in v1; they exist as menu rows only.

## 10. Persistence and migration

- New EF migration adds the two tables. The shared dev DB migrates when the app runs from a worktree (known project behaviour).
- No data migration is needed for layouts. `LibraryDetailsColumns` and the existing `AppSettings` fields remain the global layout and are
  still written, so downgrading and Workspaces keep working. `LibraryWorkspaceState.DetailsColumns` and Workspaces are unchanged.
- Down-migration must drop only the two new tables (see the rollback-chain bug memory: never `DropColumn` something this migration did not add).

## 11. Testing

- Unit: `ListLayoutState` round-trip and tolerant read (missing/unknown keys, stale field dropped, zero visible columns -> default);
  `ListLayoutService` CRUD, replace-by-name, reorder, unique (Screen, Name), assignment get/set, set-on-all, orphan assignment ignored.
- View-model: selecting a list loads its assignment else global; a change writes the assignment (debounce flushed in the test via the
  existing dispatcher drain); applying a named layout copies and later edits leave it unchanged; `ShowTileTitles` migration to
  `HideCaptions`; granularity switch keeps one state; Books maps only sort/group.
- Headless view tests for the List Options and Edit Layouts overlays (tab opens on current style; Move Up/Down; Show All/Hide All; Apply).
- Real-window keyboard tests (`RealWindowKeyboardTests` style) for `ListOptions`/`EditListLayouts` and for resize/reorder reachable by keyboard.
- `LibraryListLayoutPersistence`-style UI test: a column width and order survive a restart.
- Fast set only day to day (`--filter "Speed!=Slow"`), targeted classes while iterating.

## 12. On-screen verification (cannot be done unattended)

Drag-to-resize/reorder feel, overlay layout and focus ring in dark and light skins, the popup close-from-row path, and a restart
preserving a per-list layout. To be listed as outstanding in `docs/paperbunkr-todo.md` when built, not claimed verified.

## 13. Open items for the plan (not design questions)

- Exact `TileTextElement` members (read from the tile templates).
- `SelectionKey` for every Books selection (read from `BooksScreenViewModel`'s sidebar).
- Confirming the Ctrl+L / Ctrl+Alt+L bindings are free.

## 14. As built - differences from the design above

Decided while implementing; the scope did not change.

- **No debounce (section 5).** The Library already writes its settings on every governed change, immediately. The per-list row is written from
  that same hook (`ListLayoutsViewModel.TrackChange`), and only when the captured layout differs from the last one applied or saved for the
  list, so a filter or search change writes nothing. A header resize commits once, on release.
- **The default is a stored row (section 6).** `AppSettings` mirrors the list on screen, so it cannot also be the fallback once lists differ.
  The fallback is the row with key `*`, captured from the user's state the first time a screen view model runs after this ships. "Default
  layout for this list" deletes the list's own row and applies `*`.
- **Set on all lists** rewrites `*` and drops every list's own row, which has the same effect as copying onto each key and also covers lists
  created later.
- **Selection keys (section 4).** Library: `all`, `content:<ContentType>`, `collection:<id>` (reading lists and smart lists are separate
  screens with no layout of their own). Books has one list: `all`.
- **Caption lines are for the Poster style** (issue and series cards). Panorama cards have no caption row and keep none. `null` caption
  fields mean the built-in two lines, so an untouched list renders as before; the poster card grows by 17 px per extra line.
- **Tile elements (section 7).** `Title`, `Series` (issue tiles), `Summary` (series tiles' type and issue count) and 17 catalog-backed fields.
  `Title` is the first line; the rest share the second, joined with " · ".
- **A workspace apply** stores the workspace's look as the target list's own layout (it names both the list and its look).
- **Deleting a named layout asks first** (the shared confirm dialog), as section 8 says; deleting a collection drops its row.
- **Shortcuts are Global-scope actions** (`App.ListOptions` Ctrl+L, `App.EditListLayouts` Ctrl+Alt+L, `App.SaveListLayout` unbound) so both
  Library and Books claim them; no other built-in action uses either key.
- **The name prompt** is the workspace one with its heading and hint swapped ("NAME THIS LAYOUT").

## 15. Starter layouts (added 2026-10-05, at the user's request)

Five named Library layouts are seeded once (`ListLayoutTemplates`, `ListLayoutService.EnsureTemplatesSeeded`, called at startup beside the
workspace starters): **Cover wall**, **Compact tiles**, **ComicRack classic** (CE's default visible Details columns that Paperbunkr has data
for), **Reading progress**, **File details**. This is a deliberate deviation from CE, which ships none, and it replaces the "no built-in
presets" line in sections 3 and 6. They are ordinary layouts: the seeding is recorded (row key `#templates`), so one the user deletes or
renames never comes back, and a layout of the same name the user already has is left alone. Books gets none (a Books layout is only sort and
group).
