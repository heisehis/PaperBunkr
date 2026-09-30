# Reading Lists redesign: implementation plan

*Implements `docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md` (approach 2: a new screen beside the old one, a swap,
then deleting the old one).*

## Ground rules

- Work in the shared tree. When the plan started, another session ("Fix novel events leaking…") was **busy**. Before the migration
  (step 2.4) and the swap (phase 4), run `git status` and don't edit files that session has touched (`MainViewModel.cs`,
  `MainWindow.axaml` and the migration snapshot are the risky ones).
- **Port, don't re-derive.** Logic comes over from `ReadingScreenViewModel(.Folders/.Track).cs` with the same Data calls, the same
  `Dispatcher.UIThread.Post` deferrals, and the same `MarkReadingListManaged` / `ReadingListManager` rules.
- **Views:**
  - Every new `.axaml` is added together with its code-behind.
  - Load the avalonia subskills from disk before XAML work (input-interaction for drag, data-templates, layout-patterns).
  - After a XAML error that follows a successful compile, delete `obj/Debug/net10.0/Paperbunkr.App.dll/.pdb` before rebuilding
    (CLAUDE.md build gotcha).
- **Test runs:** check `Get-Process testhost` first and use `--blame-hang-timeout 90s --blame-hang-dump-type none`. Only my own
  leftover `testhost` processes may be stopped, never `Paperbunkr`.

## Phase 1: View-model core and gallery

**1.1 Screen VM shell.**
- **Files (new):** `ViewModels/ReadingLists/ReadingListsScreenViewModel.cs`.
- **What:**
  - Mode (`Gallery` | `List`) and the children `Gallery`, `List`.
  - The outside contract: `EnsureListLoaded`, `RefreshSidebar`, `LoadReadingList(id, triggerEntrance)`, `PlaceCreatedList`,
    `OpenContinuity`, `InvalidateForecast`, `static ArcSourceOptions`, `IContextMenuProvider`.
  - Constructor parameters mirror the old VM, plus `IDialogService`.
- **Verify:** it compiles; covered by 1.3's tests.

**1.2 Gallery VM.**
- **Files (new):** `ViewModels/ReadingLists/ReadingGalleryViewModel.cs`, `Models/ReadingGalleryTile.cs`,
  `Services/ReadingContinueRow.cs` (pure selection and order, plus a query helper).
- **What:**
  - Current folder, breadcrumb, tag chips (top 12 + N), filtered flat mode.
  - Continue row (Q11: ≥1 read, ≥1 owned-unread, latest `ReadingEvent`, max 8, root only).
  - Tiles: list tile (arc cover key or mosaic keys, read/total, progress); folder tile (mosaic, list count, read % over everything below).
  - Inline rename; create folder; import a folder; tile drop through `ReadingSidebarTree.ResolveDrop` over the current level;
    delete with confirm; empty states.
  - The menus from the old `ReadingSidebarContextMenuBuilder`, retargeted.
- **Verify:** `ReadingGalleryViewModelTests` (Continue selection and order, breadcrumb, tags, tiles, drop, rename, placement).

**1.3 Gallery view.**
- **Files (new):** `Views/ReadingLists/ReadingListsScreen.axaml(.cs)` (mode host), `Views/ReadingLists/ReadingGalleryView.axaml(.cs)`
  (tile drag code-behind, adapted from `ReadingListsSidebar.axaml.cs`).
- **Verify:** a headless load test in `ReadingListsScreenViewTests`.

## Phase 2: List page, reading mode

**2.1 Path model (pure).**
- **Files (new):** `ViewModels/ReadingLists/PathItems.cs`, with `PathItem`, `ChapterHeaderItem`, `PathRowItem` (wrapping
  `ReadingListItemRowViewModel`), `UpNextItem`, `EmptyListItem` and a `ReadingPathBuilder.Build(rows, collapsed, editing)`.
- **What:** chapters from `ReadingListGrouping.Runs`; the fully-read-collapsed default; Up next replaces its row; spine state
  (above / below Up next); the Covers-mode chapter caption on the first tile of each run.
- **Verify:** `ReadingPathBuilderTests`.

**2.2 Page VM.**
- **Files (new):** `ViewModels/ReadingLists/ReadingListPageViewModel.cs` and the partials `.Hero.cs`, `.Path.cs`, `.Checks.cs`,
  `.Manage.cs`.
- **What:** ported from the old VM:
  - Load, counts, Continue, forecast.
  - Arc cover and mosaic; hero backdrop via `BackdropBlurRenderer` (off the UI thread, session cache by list id).
  - Tags, rows, toggle read, remove, move, open, relink, request.
  - Story-event link, detect roles, follow arc, arc refresh, request missing.
  - Imports and exports (CBL / CSV / text / PDF / copy), overlap, compare, merge, canonical check, rebuild, continuity link.
  - Rail contents (the lists in the open list's folder, with progress).
- **Verify:** port the tests from `ReadingScreenViewModelTests`, `ReadingListRequestMissingTests`, `RoleDetectionUiTests` and
  `ReadingScreenPitchTests` onto the new VM (new files under `Paperbunkr.App.Tests/ReadingLists/`).

**2.3 Page view.**
- **Files (new):**
  - `Views/ReadingLists/ReadingListPageView.axaml(.cs)`: rail, hero, checks strip, path/covers body, scroll to Up next.
  - `Controls/ProgressRing.cs`: a custom-drawn ring.
- **Verify:** headless load tests, including a 300-issue list that realizes fewer than 60 containers.

**2.4 View-mode setting.**
- **Files:** `AppSettings.ReadingListViewMode` (string?), and the migration `AddReadingListViewMode` with a no-op `Down()`.
- **Verify:** `AddReadingListViewModeMigrationTests`.

## Phase 3: Edit mode and drawer

**3.1 Data writes.**
- **Files:** `ReadingListManager.MoveItemsTo`, `ReadingListManager.SetGroupLabel`.
- **Verify:** `ReadingListManagerEditTests`: multi-move keeping relative order, across chapters, one `Reordered` announcement;
  a label change marks the list managed and announces nothing.

**3.2 Edit session.**
- **Files (new):** the `ReadingListPageViewModel.Edit.cs` partial.
- **What:** `IsEditing`; selection and the bulk bar (Set role, Move to chapter, Mark read/unread, Remove with confirm);
  `＋ Chapter`; chapter rename; `MoveRows(rows, targetIndex)` (drag and Ctrl+↑/↓); Esc exits.
- **Verify:** VM tests.

**3.3 Drawer.**
- **Files (new):** the `ReadingListPageViewModel.Drawer.cs` partial and `Views/ReadingLists/AddIssuesDrawer.axaml(.cs)`.
- **What:** Library tab (search, multi-select, add all of a series, relink mode); Story arc tab (the arc search and catalog ported:
  Use / Refresh).
- **Verify:** VM tests and a headless load test.

**3.4 Edit view.**
- **Files:** `ReadingListPageView` (editing bar, bulk bar, edit row template, drag code-behind with the insertion line, keys).
- **Verify:** a headless load test in edit mode.

## Phase 4: Swap and delete

**4.1 Swap.**
- **Files:** `MainViewModel.cs`:
  - `Reading` becomes the new VM.
  - `ShowContextualSidebar` drops `IsReading`.
  - `OpenContinuity` is wired.
- **Files:** `MainWindow.axaml`: remove the Reading sidebar panel; the `DataTemplate` points at the new VM and view.
- **Files:** `NewReadingListViewModel.cs`: use the new `ArcSourceOptions`.

**4.2 Delete the old screen** (my own files only):
- `ReadingScreenViewModel.cs`, `.Folders.cs`, `.Track.cs`
- `Views/ReadingScreen.axaml(.cs)`, `ReadingListsSidebar.axaml(.cs)`, `ReadingFolderOverview.axaml(.cs)`
- `ReadingFolderOverviewViewModel.cs`, `ReadingListGroupViewModel` (wherever it lives), `ReadingSidebarContextMenuBuilder.cs`
  (logic moved in 1.2)
- `Models/ReadingSidebarNode.cs` (if unused)
- the ported old tests: `ReadingScreenViewModelTests`, `ReadingScreenPitchTests`, `ReadingPitchViewTests`, and the old parts of
  `ReadingListRequestMissingTests` and `RoleDetectionUiTests`

**4.3 Restyle the dialogs.**
- **Files:** `NewReadingListDialog` (the view for `NewReadingListViewModel`) and the reading-list properties overlay view.
- **What:** option cards and the header band; layout and fields unchanged.

**Verify:** a full `-t:Rebuild`; the whole App suite; the headless view tests.

## Phase 5: Polish

- Headless renders with the real tokens (the parsed-`App.axaml` trick) at 1000×700 and 1400×900, dark and a light skin.
- `avalonia-pro-max/review-checklist`.
- Docs: `paperbunkr-todo.md`, the Roadmap, and the wiki `Reading.md` (describe the new screen).
- Implementation notes in the design spec; the memory.
