# Reading Lists redesign — design

*Status: approved in brainstorming 2026-09-28 (three grilling rounds with the visual companion, then approach 2); not built. Mockups:
`.superpowers/brainstorm/936780-1790589252/content/round1-directions.html`, `round2-list-and-home.html`, `round3-modes.html`
(not shipped). Supersedes the layout of `2026-08-28-reading-lists-screen-redesign-design.md` and the sidebar/folder-overview views of
`2026-09-28-reading-lists-organize-and-track-design.md` (their data paths stay).*

## Goal

The user asked for a **full redesign that thinks broadly**. Today the section is a 236 px folder-tree sidebar plus one long page
(small cover, stacked banners, then grouped rows that only fade their controls in on hover). The rebuild makes it:

- a **place to browse** your lists: a gallery home;
- a **place to read** a list: a streaming-style hero and a journey path through the issues;
- a **place to curate**: an explicit Edit mode.

Every capability built so far keeps working.

## Facts this rests on (verified 2026-09-28)

- **Outside contract.** `MainViewModel` uses `Reading.EnsureListLoaded`, `RefreshSidebar`, `LoadReadingList`, `PlaceCreatedList`,
  `OpenContinuity` and `InvalidateForecast`. `MainWindow` binds the sidebar (`views:ReadingListsSidebar`) and a
  `DataTemplate DataType="vm:ReadingScreenViewModel"`. `NewReadingListViewModel` reads the static `ReadingScreenViewModel.ArcSourceOptions`.
- **The old screen.**
  - `ReadingScreenViewModel.cs` (~1,750 lines) with the `.Folders.cs` and `.Track.cs` partials.
  - Views: `ReadingScreen.axaml`, `ReadingListsSidebar.axaml`, `ReadingFolderOverview.axaml`, `ReadingListCompareOverlay.axaml`.
  - Support: `ReadingListItemRowViewModel`, `ReadingListGroupViewModel`, `ReadingSidebarContextMenuBuilder`,
    `ReadingListMemberContextMenuBuilder`.
- **Tests on the old view model:** `ReadingScreenViewModelTests` (42), `ReadingListRequestMissingTests` (9), `RoleDetectionUiTests` (9),
  `ReadingScreenPitchTests`, `ReadingPitchViewTests`.
- **Shell sidebar.** It is one shared `Border.contextualSidebar` whose width animates 0 ↔ 236 when
  `ShowContextualSidebar => IsLibrary || IsSmart || IsReading || IsEvents` (`MainViewModel.cs:945`).
- **Hero backdrop.** `BackdropBlurRenderer.Render(bitmap, PixelSize)` is what the Detail hero uses, faded into the skin's
  `PbHeroGradientStart/EndColor`.
- **Existing building blocks.**
  - Data: `ReadingListFolders`, `ReadingSidebarTree.ResolveDrop`, `ReadingListForecast`, `ReadingListOverlap`, `ReadingListMerger`,
    `ReadingListCanonicalDiff`, `ContinuityReadingListBuilder` / `ContinuityListOrders`, `ReadingListChecklist(Pdf)`, the CSV/CBL/text
    IO, `ArcReadingListBuilder`, `ArcRequestService`, `MemberRoleDetection`.
  - Covers: `ReadingListCoverMosaic`, `ArcCoverImageCache`.
  - Grouping: `ReadingListGrouping.Runs`.
  - Layout: `VirtualizingWrapPanel` (uniform-size grids only).
- **Item order writes.** `ReadingListManager` has `MoveItem(±1)` only; there is no move-to-index and no group-label write.
- **ComicRack CE** has no gallery, reading hero, chapters or edit mode for lists; this presentation is a Paperbunkr deviation. Folder
  semantics stay CE's (see the organize spec).

## Decisions (grilling log)

| # | Decision |
|---|---|
| S | **S3.** A gallery home is the section's landing page. Inside a list, a slim cover rail replaces the sidebar. |
| D | **A mix of D1 and D3, with D2 as a toggle.** A streaming-playlist hero plus a journey-path list; a cover wall as the alternate view. |
| Q3 | Scope is the whole section: the gallery home (replacing the folder overview), the list page and rows, the Manage actions, and the New Reading List dialog and properties overlay restyled to match. The Continuity page's create menu is unchanged. |
| Q4 | An explicit **Edit list** mode. Reading mode is clean; Edit mode brings drag reorder, checkboxes and the bulk bar, role pickers, notes, remove, and chapter editing. |
| Q5 | Nothing is removed. Every feature is re-homed, and the per-list banners merge into one **Checks** strip. |
| L | **L1.** A tall blurred-cover hero, then the spine down the left of compact track rows (# · cover · title · role · year · status), chapter diamonds, and the next issue expanded into an "Up next" card on the path. |
| H | **H1.** A "Continue reading" strip above one grid of folder and list tiles (folders first, manual order), with tag chips that filter. A folder opens in place with a breadcrumb. |
| Q6 | The hero backdrop is `BackdropBlurRenderer` over the list's arc cover (or its first owned cover), faded into the skin's hero gradient. |
| Q7 | Path \| Covers is one remembered `AppSettings` value, defaulting to Path. |
| Q8 | Chapters are the consecutive group-label runs; an unlabelled list has no chapter markers. Chapter headers show their count and how many are read. Chapters collapse, fully read chapters open collapsed, and the list opens scrolled to Up next. |
| Q9 | The path is one virtualized items list; chapter headers are items in it. |
| Q10 | The rail shows ⌂, then the lists in the open list's folder, each a cover with a progress ring and a name tooltip; the open list is ringed in the accent. |
| Q11 | The Continue row holds lists with at least one read and at least one owned-unread issue, ordered by latest `ReadingEvent` on any member, up to 8, hidden when empty. |
| Q12 | In reading mode a row click reads (owned) or shows inline Find & link · Request (missing). Hover shows ⋯ only. The right-click menu is kept. |
| K / Q13 | **K1.** The Checks strip is stacked under the hero, one line per check with its own actions and ✕, and shown only when there is something. |
| Q14 | Edit mode: the editing bar replaces the hero buttons (＋ Add issues, ＋ Chapter, Done), Esc exits, and it is not remembered. Whole-list drag, including across chapters; bulk Set role / Move to chapter / Mark read / Mark unread / Remove; role pickers with Accept and Dismiss on suggestions; note editing; remove; chapter rename. |
| Q15 | Add issues opens a right-side drawer: library search with multi-select, "Build from a story arc…" in the same drawer, and Find & link uses it too. |
| Q16 | The ⋯ menu is grouped: **Source** (Import .CBL/.CSV, Build from a story arc, Link story event, Refresh from source, Follow this arc, Rebuild from continuity, Check against ComicVine/Metron), **Maintenance** (Detect roles, Request missing issues), **Export ▸**, **Edit details…** |
| Q17 | Gallery tiles: a list tile shows its arc cover or a 2×2 mosaic plus a progress bar; a folder tile shows a mosaic of its lists, the list count and overall read %. Tiles drag to reorder or into a folder. Right-click menus as today. |
| Q18 | Empty states: no lists → illustration with New list / Import .CBL / Build from a story arc; empty list → an "Add issues to get started" card on the path; empty folder → "Drag lists here". |
| Q19 | The folder name in the hero caption links back to that folder in the gallery; ⌂ on the rail goes to the gallery root. |
| Q20 | The shell's 236 px sidebar is not shown on the Reading screen; the rail lives inside the screen. |
| Approach | **2, as the Continuity redesign did:** a new screen and view model built beside the old one, swapped in, then the old one deleted. |

## Design

### 1. Screen, view models and shell

New screen `Views/ReadingListsScreen.axaml`, backed by `ReadingListsScreenViewModel`. It is the only thing `MainViewModel` holds;
the `Reading` property keeps its name but changes type. It keeps the outside contract under the same names:

- `EnsureListLoaded()`
- `RefreshSidebar()` → refreshes the gallery and the rail
- `LoadReadingList(id, triggerEntrance)`
- `PlaceCreatedList(id)`
- `OpenContinuity`
- `InvalidateForecast()`
- `static ArcSourceOptions`

It owns three children and a mode:

- `Gallery` — `ReadingGalleryViewModel`: the home (§2).
- `List` — `ReadingListPageViewModel`: an open list (§3–§7), in partial files by concern:
  - `.Hero` — hero, meta, forecast, backdrop
  - `.Path` — items, chapters, collapse, Up next, Covers mode
  - `.Checks` — overlap, canonical diff, rebuilt note
  - `.Manage` — Source, Maintenance and Export commands
  - `.Edit` — Edit mode
  - `.Drawer` — Add issues, arc search, relink
- `Mode` — `Gallery | List`. `ReadingListsScreen` shows `ReadingGalleryView` or `ReadingListPageView` (with the rail) accordingly.

`MainViewModel.ShowContextualSidebar` drops `IsReading` (Q20). The `MainWindow` sidebar block for Reading is removed.
`IContextMenuProvider` moves to the screen view model and serves gallery tiles, rail covers and path rows.

Shared, unchanged: all Data-layer engines listed under Facts, `ReadingSidebarTree` (pure), `ReadingListGrouping`,
`ReadingListChecklistPdf`, `ContinuityStoryOrder`, `ReadingListCompareViewModel` and its overlay view (kept, re-hosted).

### 2. Gallery home (H1)

- **Header.** "Reading Lists" title; a breadcrumb when inside a folder (`Reading Lists › Crisis Events › Tie-ins`, each part
  clickable); ＋ Folder; ＋ New list (opens the existing New Reading List dialog, which places the list in the current folder via
  `PlaceCreatedList`); Import folder of .CBL (in a ⋯ next to them).
- **Tag chips.** "All" plus every tag used by a list, most used first, up to 12 with "+N". Picking one filters the grid to matching lists
  across all folders (flat, as the old tag filter did) and hides the Continue row.
- **Continue reading (Q11).** Shown at the root only. Each card: the list's cover (30×45), name, "next: <issue> · read/total". Click
  opens the list scrolled to Up next. Query: `ReadingEvent` rows whose `ItemType = Comic` and `ItemId` is a member issue, max
  `TimestampUtc` per list, over lists that qualify.
- **Grid (Q17).** A `WrapPanel` of fixed 148 px tiles in `ReadingListFolders.Children` order (folders first).
  - List tile: a 148×148 cover (arc cover, else `ReadingListCoverMosaic` 2×2, else a list glyph), name (2 lines max, tooltip),
    "read / total", a 3 px progress bar.
  - Folder tile: a mosaic of up to 4 of its lists' first covers, a folder glyph with the name, "N lists · P%" (read / total over every
    list below).
  - Click opens; right-click uses the same menus as the old sidebar (`ReadingSidebarContextMenuBuilder` entries, moved to the new VM).
  - Rename is inline on the tile name (Enter / Esc / blur, as the old sidebar did).
- **Tile drag.** Press-move drag with the in-process `DataFormat`, as the old sidebar did. On a tile, the left/right third means
  before/after (tiles of the same kind), and the middle of a folder tile means into. The move is resolved by
  `ReadingSidebarTree.ResolveDrop` over the current level's slots, and written by `ReadingListFolders`. Feedback is a 2 px accent bar
  beside the tile (reorder) or an accent wash (into).
- **Empty states (Q18)** as decided.

### 3. List page layout (L1)

A `Grid ColumnDefinitions="58,*"`:

- **Rail (Q10).** ⌂ (to the gallery root), then the lists in the open list's folder, each a 36×52 cover with a 16 px progress ring
  (drawn by a small custom `ProgressRing` control; no ring control exists yet) and a name tooltip. The open list has an accent outline.
  The rail scrolls vertically if long. Clicking switches lists without leaving list mode.
- **Hero.** A fixed-height band of about 190 px.
  - Backdrop: the blurred cover (Q6) at 1600×380, computed off the UI thread and cached per list id in memory for the session, under a
    vertical gradient from `PbHeroGradientStart` to `PbBg`. Skins control the fade.
  - Cover: 86×128 with an elevation shadow.
  - Caption line: `<folder link> · reading list · via <source> · #tags` (tags as clickable chips that open the gallery filtered by that tag).
  - Title: `pbTextHeading` at 30.
  - Meta line: `N issues · N read · N missing · <forecast> · from <continuity> · story order`, where the continuity part is a link.
  - Buttons: **▶ Continue — <issue>** (primary), **＋ Add issues**, **✎ Edit list**, **⋯** (§7), and the **Path | Covers** segmented toggle
    at the right.
- **Checks strip (K1).** See §6.
- **Body.** The path (§4) or covers (§5), filling the rest in one `ScrollViewer`, which is the only vertical scroll.

### 4. The path

`ReadingListPageViewModel.PathItems` is one flat `ObservableCollection<PathItem>`. It is rendered by an `ItemsControl` with a
`VirtualizingStackPanel` and one `DataTemplate` per type:

- `ChapterHeaderItem` — label, count, read count, `IsCollapsed`, `IsCurrent` (holds Up next). It is a diamond on the spine plus a caption
  "▾ Main event · 12 · 7 read"; clicking toggles collapse. It only exists for labelled runs (`ReadingListGrouping.Runs`).
- `PathRowItem` — wraps the existing row state (position, cover key, title/series lines, role chip, year, read, owned, missing, note,
  suggestion). It is a spine dot (filled = read, hollow = unread, dashed = missing) and a 36 px-high track row: # · 18×26 cover · title
  (+ faint series line) · role chip · year · state (✓ / "missing · Find & link · Request") · ⋯ on hover. A note shows as a faint italic
  second line.
- `UpNextItem` — the next-up issue, replacing its `PathRowItem`. A card on the path: accent-soft background, 48×72 cover, "Up next · 13
  of 31", title, the issue's summary (2 lines), its note, and ▶ Read.
- `EmptyListItem` — the Q18 card.

**Spine.** Each item template draws its own spine segment: a 3 px line in the left gutter, accent above Up next and `PbSurface3`
below. So the spine needs no overlay and virtualizes for free.

**Collapse (Q8).** State lives per list, in memory for the session, in a `Dictionary<int, HashSet<string>>` keyed by list id. The
initial rule is that fully read chapters are collapsed. Collapsing removes that run's row items from `PathItems` and leaves the header.

**Opening.** After `LoadReadingList`, the view scrolls Up next into view with `BringIntoView` on its container, deferred one
dispatcher tick after layout.

**Reading mode interactions (Q12).**
- Row click: owned → reader through the existing `goReaderForIssueInReadingList`; missing → nothing (the inline links act).
- ⋯ / right-click: Mark read or unread, Add note, Move up / Move down, Remove from list.
- The Up next card's Read and the hero's Continue do the same thing.

### 5. Covers mode (D2)

The same flat data rendered as a `VirtualizingWrapPanel` of uniform 96×166 tiles: a 96×144 cover plus a one-line caption (# and state:
"✓", "▶ next", "missing"). Read is at 0.45 opacity, next-up has an accent ring, and missing has a dashed outline. Chapter headers can't
break a uniform wrap, so **the first tile of each chapter carries the chapter name as a small caption above its cover** (every tile
reserves that 14 px line). Collapse doesn't apply in Covers mode. The choice is saved in `AppSettings.ReadingListViewMode` (string
`"Path"` / `"Covers"`, nullable; a new migration with a no-op `Down()`).

### 6. Checks strip (K1)

One bordered strip under the hero holding zero to three lines, each an icon, text, its actions and ✕:

- **Overlap** (`ReadingListOverlap`): "Shares 27 of 31 issues with X" · Compare · Merge… · Not a duplicate, plus "+N more ▾" when
  there are several.
- **Canonical diff** (`ReadingListCanonicalDiff`): "<source> lists N issues this list doesn't have · M out of order" · Insert missing
  (N) · Reorder · Show details · Use Metron/ComicVine instead. The details expand inside the strip. A matching result shows "Matches
  <source>'s order." and clears after 4 s.
- **Rebuilt note**: "Rebuilt · A added · R removed · M moved".

The strip is hidden when it has no lines. Merge keeps the two-button confirm. Compare keeps `ReadingListCompareOverlay`.

### 7. Manage menu (Q16)

The ⋯ in the hero, grouped with separators:

- **Source:** Import .CBL…, Import .CSV…, Build from a story arc… (opens the drawer's arc tab), Link story event…, Refresh from source
  (arc-linked), Follow this arc ✓ (arc-linked), Rebuild from continuity (continuity-linked), Check against ComicVine/Metron (eligible
  event-linked lists).
- **Maintenance:** Detect roles, Request missing issues (arc-linked).
- **Export ▸:** As .CBL…, As .CSV…, As plain text…, Printable checklist (PDF)…, Copy as text.
- **Edit details…** (the properties overlay).

"Link story event" opens a small flyout search anchored to the ⋯ button, the same search as today, instead of an inline panel.

### 8. Edit mode (Q14)

`IsEditing` on the page view model. Esc and Done exit; loading a list resets it.

- The hero's button row is replaced by an **editing bar**: "✎ Editing <name> · drag to reorder, select for bulk actions" · ＋ Add issues ·
  ＋ Chapter · Done.
- While anything is selected, a **bulk bar** shows under it: "N selected · Set role ▾ · Move to chapter ▾ · Mark read · Mark unread ·
  Remove · Clear".
- **Rows** in Edit mode: ⠿ handle · checkbox · # · cover · title · role picker (`SuggestBox`, strict) · suggestion chip with Accept and
  Dismiss · "✎ note" (inline TextBox) · ✕ remove.
- **Chapters are expanded** while editing. A chapter header shows its label as an inline-editable text; renaming rewrites that run's
  `GroupLabel`.
- **Up next** renders as a normal row.
- **Drag.** Press-move on ⠿ drags the row, or all selected rows if the dragged row is selected. The drop position between rows shows a
  2 px accent line. Dropping into another chapter's run adopts that chapter's label.
- **Keyboard equivalent:** Ctrl+↑ and Ctrl+↓ move the focused (or selected) rows one place.
- **＋ Chapter** inserts a chapter at the first selected row (or at the end), named "New chapter", and starts its rename.
- **Move to chapter ▾** lists the existing chapter labels plus "No chapter".

New Data writes in `ReadingListManager`, each announcing through the existing hook:

- `MoveItemsTo(ctx, listId, itemIds, targetIndex)` keeps the moved items' relative order, renumbers once, and announces `Reordered`.
- `SetGroupLabel(ctx, listId, itemIds, label)` has no announcement: membership doesn't change. It still marks the list managed so the
  save backstop stays quiet.

Remove uses the existing `RemoveItems`; bulk role uses the existing role path.

### 9. Add-issues drawer (Q15)

A right-side overlay panel 360 px wide (`PbSurface1`, left border, shadow) over the list page. It slides in on `RenderTransform`
translate X, over 200 ms with `PbMotionEase`. Esc or ✕ closes it. There are two tabs:

- **Library:** the search box, multi-select results with "＋ series", and **Add N selected**. While relinking (Find & link from a
  missing row) the header says "Link JSA #82 — pick an issue" and a single pick relinks and closes.
- **Story arc:** the arc source picker, the search box (or the browsable catalog), and results with **Use this arc** (creates a new list
  and opens it) or, on an arc-linked list, **Refresh from source**.

The drawer replaces today's inline Add-issues, relink and arc panels.

### 10. Dialogs restyled (Q3)

Same fields and behaviour; only the look changes:

- **New Reading List dialog:** its four build methods (Blank, Import, Arc, Event) become four option cards with a FluentIcon and a
  one-line description. The header matches the drawer's.
- **Properties overlay:** a 120 px header band using the list's hero backdrop and cover, then the existing fields in card sections.

### 11. Motion and accessibility

- **Motion:**
  - Mode switch Gallery ↔ List: 200 ms opacity plus a 12 px translate.
  - Drawer: 200 ms slide in, 140 ms out.
  - Chapter collapse: an instant item change, with the header chevron rotating over 150 ms.
  - Row entrance reuses `EntranceAnimation`.
  - Durations come from the `PbMotion*` tokens, so reduced motion follows them.
- **Accessibility:**
  - Every icon-only button (⌂, rail covers, ⋯, ✕, handles, chevrons) has an `AutomationProperties.Name`.
  - Tiles and rail covers are focusable buttons.
  - Edit mode is fully keyboard reachable (Ctrl+↑/↓, Space selects, Delete removes the selection after a confirm).
  - No `Pb` colour appears as a literal.

## Phases (each builds, tests green, and leaves the old screen working until the swap)

1. **View-model core and gallery.**
   - `ReadingListsScreenViewModel` with the outside contract.
   - `ReadingGalleryViewModel`: folders, breadcrumb, tag chips, the Continue query, tiles, drag, rename, empty states.
   - `ReadingGalleryView`.
   - Tests.
2. **List page, reading mode.**
   - `ReadingListPageViewModel` Hero, Path, Checks and Manage, ported from the old view model with the same Data calls.
   - Rail, hero backdrop, `PathItems` with the four item types, collapse, scroll to Up next, Covers mode and its migration.
   - `ReadingListPageView`.
   - Tests ported from `ReadingScreenViewModelTests` / `ReadingListRequestMissingTests` / `RoleDetectionUiTests` / `ReadingScreenPitchTests`
     onto the new view model.
3. **Edit mode and drawer.** `ReadingListManager.MoveItemsTo` and `SetGroupLabel` (Data tests), Edit mode, drag, keyboard moves,
   chapter editing, the bulk bar, the drawer (library, relink, arc). Tests.
4. **Swap and delete.**
   - `MainViewModel.Reading` becomes the new view model.
   - `ShowContextualSidebar` drops `IsReading`, and the `MainWindow` Reading sidebar block and old `DataTemplate` are removed.
   - Delete `ReadingScreenViewModel` (+ partials), `ReadingScreen`, `ReadingListsSidebar`, `ReadingFolderOverview`,
     `ReadingFolderOverviewViewModel`, `ReadingListGroupViewModel`, and the old tests that were ported.
   - Restyle the two dialogs.
   - `ReadingPitchViewTests` becomes `ReadingListsScreenViewTests` (headless XAML load of gallery, list, edit and drawer).
5. **Polish.** Headless renders with the real tokens, `avalonia-pro-max/review-checklist`, docs (`paperbunkr-todo.md`, Roadmap, wiki
   `Reading.md`), and implementation notes here.

## Testing

- **Pure:** PathItems construction (chapters, collapse rule, Up next placement, unlabelled lists, Covers-mode chapter captions), the
  Continue-row selection and order, gallery drop resolution at one level, and `MoveItemsTo` (multi-item, across chapters, relative
  order kept, one announcement), plus `SetGroupLabel`.
- **View model:**
  - every ported behaviour (import, export, arc create and refresh, request missing, story-event link, detect roles, follow arc,
    forecast, overlap / compare / merge, canonical check, rebuild, placement into folders);
  - mode switching, rail contents, tag filter from the hero chips, Edit mode entry and exit, bulk actions, drawer relink.
- **Headless view tests:** each view loads and shows real data, and a 300-issue list keeps realized containers in the tens
  (virtualization works).
- **Migration test** for `ReadingListViewMode`.
- **Headless PNG renders** at 1000×700 and 1400×900 in the default skin and one light skin, then the review checklist.

## Out of scope

- The Continuity page's "Create reading list" menu.
- Smart Lists and Collections, which keep the shared sidebar.
- Plugin API changes.
- Persisting collapse state across sessions.
- Per-list view-mode memory.
- Dominant-colour extraction.
- Reading lists for remote viewers.

## Defaults filled in while writing (flag in review)

- **Covers mode chapter names** ride on the first tile of each chapter, because a uniform virtualized wrap can't hold full-width headers.
- **Collapse state** is per session, not saved.
- **Continue row** only on the gallery root, not inside folders.
- **Tag chips** capped at 12 plus "+N".
- **Link story event** becomes a flyout from ⋯ instead of an inline panel.
- **Ctrl+↑ / Ctrl+↓** as Edit mode's keyboard reorder, and **Delete** removes the selection after a confirm.
- **Rail covers** use a new small `ProgressRing` control.

## Implementation notes (2026-09-28)

Built the same day from `2026-09-28-reading-lists-redesign-plan.md`, all five phases. The old screen is deleted; a backup of its files
is in that session's scratchpad (`old-reading-screen/`). It is **uncommitted** and **not yet seen in the running app**; the headless
view tests load every view with real data.

**Where things live:**
- View models (in `ViewModels/`, same namespace, no subfolder):
  - `ReadingListsScreenViewModel`: the façade, which also carries the right-click menus.
  - `ReadingGalleryViewModel`.
  - `ReadingListPageViewModel`, split into `.cs` (core), `.Path`, `.Checks`, `.Manage`, `.Drawer` and `.Edit` partials.
  - `ReadingPathItems.cs`: `PathItem` types, `ReadingPathBuilder`, `CoverTileItem`.
  - `ReadingListRecords.cs`: the shared small records.
- Views: `ReadingListsScreen`, `ReadingGalleryView`, `ReadingListPageView`, and `Styles/ReadingListsChrome.axaml`.
- New pieces: `Controls/ProgressRing`, `Services/ReadingContinueRow`, `Models/ReadingGalleryTile`.
- Data: `ReadingListManager.MoveItemsTo` / `SetGroupLabel`, `AppSettings.ReadingListViewMode`, and the `AddReadingListViewMode`
  migration (no-op `Down()`).

**Deviations from the design:**
- **Ported code.** The Manage, arc-search and library-search blocks were lifted verbatim from the old view model into the partials,
  not rewritten.
- **Continue label.** It now names the issue ("Continue — Infinite Crisis #4", as in the mockups). The ported tests were updated.
- **Deferred reloads.** Reloads raised from a row's own command (toggle read, link story event, move, remove) are posted to the
  dispatcher (CLAUDE.md runtime gotcha). The ported tests drain the dispatcher before asserting.
- **"＋ Chapter" is "＋ Chapter from selection".** It needs ticked issues; with none, a status message says so. A blank chapter
  name removes the chapter.
- **Link story event** is a search strip under the hero (opened from ⋯), not a flyout anchored to ⋯.
- **Compare overlay.** Inlined in `ReadingListPageView`. The old `ReadingListCompareOverlay` was typed to the old view model and is
  deleted with it.
- **Row drag.** It starts from the ⠿ handle only, so the row's own click still reads the issue. A lower-half drop draws its line as a
  bottom border on the hovered row.
- **Tag filter entry points.** Clicking a tag in a list's hero opens the gallery filtered by it; the gallery's tag chips filter too.
- **Real bug caught by the virtualization test.** A custom `ItemsControl` template needs `ItemsPanel="{TemplateBinding ItemsPanel}"`
  on its `ItemsPresenter`, or the `VirtualizingStackPanel` is silently replaced and all 300 rows realize.

**Tests:**
- App, in `Paperbunkr.App.Tests/ReadingLists/`:
  - `ReadingListsScreenViewModelTests` (ported, 42)
  - `ReadingListPageRequestMissingTests` (ported, 9)
  - `ReadingGalleryAndPathTests` (Continue row, gallery, drop, placement, path, covers, hero and rail, Edit mode, drawer)
  - `ReadingListsScreenViewTests` (headless loads plus the virtualization check)
- `RoleDetectionUiTests` is re-pointed at the new view model.
- Data: `ReadingListManagerEditTests`, `AddReadingListViewModeMigrationTests`.

**Polish pass (phase 5).** Headless renders with the real `App.axaml` colours at 1400×900 covered the gallery, a folder, the path,
Covers, Edit and the drawer. They led to four fixes:
- Fixed-width role and state columns, so years line up on missing rows.
- Disabled row and tile buttons (missing issues) are transparent instead of the theme's grey box.
- The Up next ring in Covers mode is drawn inside the tile, so it's no longer clipped.
- The hero's type label is shown only for non-User lists.

`avalonia-pro-max/review-checklist`: colours are skin tokens only; the gradient's `#33000000` mid-stop copies `DetailHero`. Icon-only
buttons carry `AutomationProperties.Name`, and every transition has an easing.

**Full App suite after the swap:** 4,661 of 4,663 passed. The 2 failures were fixed or cleared: `TrackerAutoSyncWiringTests` now
reads the service from `Reading.List`, and the `MatrixRainOverlayRenderTests` flake passes alone. `RoleDetectionUiTests`'s
construct check now supplies the tokens the new chrome needs, because it had only passed when another test added them first.
