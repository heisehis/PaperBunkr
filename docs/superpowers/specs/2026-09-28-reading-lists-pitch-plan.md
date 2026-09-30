# Reading List pitch (specs A + B): implementation plan

*Implements:*
- `docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md` (A)
- `docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-design.md` (B)

The work stays in the shared working tree. B builds on the uncommitted Continuity map code (`ContinuityMapBuilder` and related),
so a worktree off master can't compile it. On 2026-09-28 all peer sessions were idle.

## Conventions every step follows

- **Writing list items.** Any direct write to `ReadingListItems` (merge, reorder-to-match, reconciler) calls
  `context.MarkReadingListManaged(list)` and announces once through `ReadingListManager.Record(...)`. Otherwise the save
  backstop logs a bypass.
- **Removing rows from inside a command.** Row-removing collection changes raised from a row's own command are posted with
  `Dispatcher.UIThread.Post` (CLAUDE.md runtime gotcha).
- **Migrations.**
  - Create with `dotnet ef migrations add <Name> --project Paperbunkr.Data`.
  - `Down()` drops only new tables; new columns stay (the project's orphan-column rule).
  - Never apply a migration to the user's DB from here.
- **Test databases.** Data tests use a temp SQLite file built with `new PaperbunkrDbContext(options)` (the
  `ReadingListManagerTests` shape). App VM tests redirect `PaperbunkrDbContext.DatabasePathOverride` (the
  `ReadingScreenViewModelTests` shape).
- **Running tests.** Before a run, check `Get-Process testhost`. Run with `--blame-hang-timeout 90s --blame-hang-dump-type none`.
- **XAML.** New `.axaml` files are added together with their code-behind. Load the matching avalonia subskill from disk before
  XAML work.

## Step 1: Schema
**Files:**
- `Paperbunkr.Data/Entities/ReadingListFolder.cs` (new)
- `ReadingListOverlapDismissal.cs` (new)
- `ContinuityOrderKind.cs` (new)
- `ReadingList.cs` (edit): `FolderId`, `Folder`, `ContinuityId`, `Continuity`, `ContinuityOrderKind?`
- `PaperbunkrDbContext.cs` (edit): DbSets and configuration
- Migrations `AddReadingListFolders` (folders, the dismissal table, `FolderId`, and a `SortOrder` renumber in SQL) and
  `AddReadingListContinuityLink`
- Tests `AddReadingListFoldersMigrationTests`, `AddReadingListContinuityLinkMigrationTests`

**Configuration:**
- The folder's parent FK is Restrict.
- `ReadingList.FolderId` is SetNull.
- `ContinuityId` is SetNull.
- The dismissal table has a unique index on (A, B), and both of its FKs cascade.

**Verify:**
- Migration tests: the renumbered `SortOrder` for existing lists, the columns round-trip, and cascade on list delete.
- `dotnet build`.

## Step 2: Folder write path
**Files:** `Paperbunkr.Data/ReadingLists/ReadingListFolders.cs` (new); `Paperbunkr.Data.Tests/ReadingListFoldersTests.cs` (new)

**What:** the methods from spec A §1:
- Create, Rename, SetDescription, SetCollapsed
- Move (with the cycle check), MoveList
- Delete (re-parents the folder's contents)
- SortAlphabetically (ignores articles)
- `PlaceNewList(ctx, list, folderId)` (sets `FolderId`, appends `SortOrder`)
- `Children(ctx, folderId)` (the ordering helper)

**Depends on:** Step 1.

**Verify:** Data unit tests for every method, including the refused cycle and preserved order after a delete.

## Step 3: Pure engines (Data)
**Files:**
- `Paperbunkr.Data/Metadata/ReadingListForecast.cs`
- `Paperbunkr.Data/ReadingLists/ReadingListOverlap.cs`
- `ReadingListMerger.cs`
- `ReadingListChecklist.cs` (the `ChecklistModel` builder)
- `CsvReadingListIO.cs` (edit, add `Write`)
- `ReadingListTextExporter.cs` (edit, add notes)
- Tests for each

**Details:**
- The forecast counts `ReadingEvent` Finished Comic rows in the last 90 days. It needs at least 5, returns null for 0 unread,
  and gives "in N days/weeks" wording under 31 days.
- Overlap uses an inverted index, a ≥ 60% threshold, a minimum of 5 in the smaller list, and dismissals.
- Merge inserts after the last shared issue, fills blanks, merges tags, and announces once.

**Depends on:** Step 1 (dismissals, merge).

**Verify:** unit tests from the spec's Testing section, plus a CSV round trip (export, then import, same issues).

## Step 4: Grouping fix and checklist PDF (App)
**Files:**
- `Paperbunkr.App/Services/ReadingListGrouping.cs` (new, `Runs`)
- `ReadingScreenViewModel.LoadReadingList` (use `Runs`)
- `Paperbunkr.App/Services/ReadingListChecklistPdf.cs` (new; SkiaSharp `SKDocument`)
- Tests `ReadingListGroupingTests`, `ReadingListChecklistPdfTests`

**Depends on:** Step 3 (`ChecklistModel`).

**Verify:**
- Runs tests.
- PDF bytes start with `%PDF`.
- 100 rows across 3+ pages.
- A group row is never last on a page.
- The paper size is chosen correctly.

## Step 5: Reading screen VM (A)
**Files:**
- `Paperbunkr.App/Models/ReadingSidebarNode.cs` (new: base, `ReadingFolderNode`)
- `ReadingListSummary.cs` (derives from the node; gains `Depth`, `FolderId`)
- `ReadingScreenViewModel.cs` (edit), plus new partial files so it stays readable:
  - `ReadingScreenViewModel.Folders.cs`: sidebar flatten, folder commands, drop resolution, `SelectedFolderId`, folder overview
  - `ReadingScreenViewModel.Track.cs`: forecast, overlap banner, compare/merge, checklist and CSV export
- `Paperbunkr.App/ViewModels/ReadingFolderOverviewViewModel.cs` (new)
- `ReadingListCompareViewModel.cs` (new)
- Tests `ReadingScreenFolderTests`, `ReadingScreenTrackTests`

**Details:**
- `Lists` stays the flat, filtered list collection (existing tests and `HasNoReadingLists`).
- `SidebarNodes` is the new flattened tree the view binds to.
- A pure `ReadingSidebarDrop.Resolve(zone, source, target)` turns a drop into a move.
- New-list placement runs through `ReadingListFolders.PlaceNewList` at every create or import site in this VM, and through the
  `folderId` parameter on `ArcReadingListBuilder.CreateFromArcAsync`.

**Depends on:** Steps 2 and 3.

**Verify:** VM tests for:
- flattening (depth, collapsed, flat under a tag filter)
- drop resolution
- placement
- overview totals
- the forecast label
- the banner partner ordering
- merge then delete

## Step 6: Reading screen views (A)
**Files:**
- `Views/MainWindow.axaml` (sidebar: node templates, folder row, indent, drag handlers in the MainWindow code-behind or an
  attached behaviour, right-click menus)
- `Views/ReadingScreen.axaml` (overview host switch, overlap banner, Manage ▸ Export items, a "Folder" header action)
- `Views/ReadingFolderOverview.axaml` + `.cs` (new)
- `Views/ReadingListCompareOverlay.axaml` + `.cs` (new, `OverlayShell`)
- A merge confirm through the existing `ConfirmDialog`
- Folder import/export commands (`IFilePickerService.PickFolderAsync`)

**Depends on:** Step 5.

**Verify:**
- The build (then launch the exe to confirm the XAML weave, per the CLAUDE.md build gotcha).
- A headless PNG render of the sidebar with nested folders, the overview and the banner.
- `avalonia-pro-max/review-checklist`.

## Step 7: Continuity builders (B, Data)
**Files:**
- `Paperbunkr.Data/ReadingLists/ReadingListReconciler.cs` (new; extracted from `ArcReadingListBuilder.RefreshAsync`'s
  reorder/add/remove/orphan pass)
- `ArcReadingListBuilder.cs` (uses the reconciler)
- `ContinuityReadingListBuilder.cs` (`CreateFromOrder`, `RebuildFromOrder`, `PublicationOrder(ctx, id)`, no labels, sets the
  link, `folderId`)
- Tests

**Depends on:** Step 1.

**Verify:**
- The existing `ArcReadingListBuilderTests` pass unchanged.
- New tests: rebuild add/remove/move, notes and roles kept, labels rewritten, orphaned placeholders deleted, one announcement.

## Step 8: Story order and continuity UI (B, App)
**Files:**
- `Services/EventMap/ContinuityStoryOrder.cs` (new)
- `ContinuityHero.axaml` / `ContinuityPageViewModel.cs` (the two-choice flyout)
- `ReadingScreenViewModel` (Rebuild command, meta line "from X · story order", the rebuilt note)
- Tests

**Depends on:** Step 7.

**Verify:** `ContinuityStoryOrder` tests on hand-built data (block order, first appearance kept, labels), and VM tests for
both hero choices.

## Step 9: Canonical diff (B)
**Files:**
- `Paperbunkr.Data/ReadingLists/ReadingListCanonicalDiff.cs` (new)
- `ReadingScreenViewModel.Track.cs` (the check command, diff panel state, "Use Metron instead")
- `ReadingScreen.axaml` (the diff panel)
- Tests with a fake `IReadingListSource`

**Depends on:** Step 1 only; independent of Steps 7 and 8.

**Verify:**
- Missing entries and positions.
- The LIS out-of-order count.
- Insert positions, with placeholders created only on insert.
- Reorder leaving foreign items in their slots.
- ComicVine preferred over Metron.

## Step 10: Wrap-up
- Run the full Data and App suites.
- `dotnet build -t:Rebuild` of the App, then launch the exe once to confirm startup. The user's DB will need the two new
  migrations.
- Update `docs/paperbunkr-todo.md` and the Roadmap entry to "built, not viewed on screen".
- Add "Implementation notes" to both specs.
- Update the memory.
