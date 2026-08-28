# Books Bulk Edit, Series Editor & Pruning (Piece B3) — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md*

**Status: done 2026-08-27.** All 8 steps implemented. App.Tests 1047 green, Data.Tests 457 green,
full solution builds 0/0, app launches clean. On-screen click-through pending (no computer-use).

## Step 1: Empty-series pruning helper + call sites (component d)
**Files:** `src/Paperbunkr.App/Services/BookSeriesMaintenance.cs` (new),
`src/Paperbunkr.App/ViewModels/BookPropertiesScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/BookDetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs` (edit)
**What:** `BookSeriesMaintenance.PruneIfEmpty(PaperbunkrDbContext, int?)`. Wire into
`BookPropertiesScreenViewModel.Save` (capture pre-edit `BookSeriesId`, prune if it changed),
`BookDetailScreenViewModel.DeleteBook`, `BooksScreenViewModel.DeleteBook` (extract
`DeleteBooks(IEnumerable<int>)` while here — Step 3 reuses it).
**Verify:** `BookSeriesMaintenanceTests` (new); extend `BookPropertiesScreenViewModelTests` +
`BookDetailScreenViewModelTests` for the detach/delete prune paths.

## Step 2: `MetadataEditHistoryService.RecordBookEdits`
**Files:** `src/Paperbunkr.App/Services/MetadataEditHistoryService.cs` (edit)
**What:** Add `RecordBookEdits(desc, Dictionary<int,…> before, after)` (multi-id `Target=Book`
entry). Make `RecordBookEdit` (singular) delegate to it.
**Verify:** `MetadataEditHistoryServiceTests` (extend) — two-book undo/redo.

## Step 3: `BooksScreenViewModel` + `BookCardSample` selection
**Files:** `src/Paperbunkr.App/Models/BookCardSample.cs` (edit),
`src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs` (edit)
**What:** `BookCardSample` → `sealed partial class : ObservableObject, ISelectableCard` +
`[ObservableProperty] bool _isSelected` + `int Id => BookId`. VM: `TileSelectionController<BookCardSample> Selection`,
`OrderedCards`, `ToggleBookSelection(card, shift)` + `ToggleBookSelectionCheckboxCommand`,
`CardClickCommand` (replaces `SelectBookCommand`), `HasSelection`/`SelectionCount`/`SelectionCountLabel`,
`ClearSelectionCommand`, `EditSelectionCommand`, `DeleteSelectionCommand`, `EditSeriesCommand(int?)`.
`Rebuild()` re-applies `IsSelected`. New ctor args `goBulkEdit`, `goEditSeries` (+ `_shiftHeld` field).
**Depends on:** Step 1 (`DeleteBooks`), Step 2 not required yet.
**Verify:** `BooksScreenViewModelTests` (extend) — toggle/shift/clear, survives re-sort, cleared by
reload, `EditSelection`/`DeleteSelection`/`EditSeries` callbacks. Update the existing
`SelectBook_…`/`OpenSeries_…` tests for the renamed command.

## Step 4: `BooksScreen.axaml` — checkboxes, action bar, header menu
**Files:** `src/Paperbunkr.App/Views/BooksScreen.axaml` (edit),
`src/Paperbunkr.App/Views/BooksScreen.axaml.cs` (edit)
**What:** `CheckBox.tileSelect` on `BookCardTemplate` (copy the style setters from LibraryScreen if
not shared). Card `Button.Command` → `CardClickCommand`. Chrome row gated `!HasSelection`; new
selection bar gated `HasSelection` (`SelectionCountLabel` · Edit… · Delete · Clear). Grouped-Series
header `Button` gets a `ContextMenu` "Edit series…". Code-behind: `PointerPressed` on the content
`ItemsControl` sets the VM `_shiftHeld` (via a small public setter or method).
**Depends on:** Step 3
**Verify:** build + launch; grid renders, checkboxes appear, click still navigates.

## Step 5: `BulkBookPropertiesScreenViewModel` + overlay
**Files:** `src/Paperbunkr.App/ViewModels/BulkBookPropertiesScreenViewModel.cs` (new),
`src/Paperbunkr.App/Views/BulkBookPropertiesOverlay.axaml` (new),
`src/Paperbunkr.App/Views/BulkBookPropertiesOverlay.axaml.cs` (new — same commit)
**What:** Per design §Components 4. 6 staged fields (Author/Summary/PublishedDate/SeriesName/
SeriesAuthor/SeriesSortName) with `Apply*` bools, auto-tick on edit. `Load` computes
agreed-or-blank + watermark. `Save` writes staged to all, resolves series once, `RecordBookEdits`,
prunes distinct old series. `HasUnsavedChanges` = any staged.
**Depends on:** Steps 1, 2
**Verify:** `BulkBookPropertiesScreenViewModelTests` (new) — Step 8 list.

## Step 6: `BookSeriesPropertiesScreenViewModel` + overlay
**Files:** `src/Paperbunkr.App/ViewModels/BookSeriesPropertiesScreenViewModel.cs` (new),
`src/Paperbunkr.App/Views/BookSeriesPropertiesOverlay.axaml` (new),
`src/Paperbunkr.App/Views/BookSeriesPropertiesOverlay.axaml.cs` (new — same commit)
**What:** Per design §Components 5. Name (rename in place) / SortName / Author. Blank + collision
guards with `notify`. Buffered Save/Cancel, `HasUnsavedChanges`.
**Depends on:** none
**Verify:** `BookSeriesPropertiesScreenViewModelTests` (new).

## Step 7: `BookDetailScreenViewModel` series-mode buttons + `MainViewModel`/`MainWindow` wiring
**Files:** `src/Paperbunkr.App/ViewModels/BookDetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/BookDetailScreen.axaml` (edit),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit),
`src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:** `BookDetailScreenViewModel` ctor gains `goBulkEdit` + `goEditSeries`; `EditAllSeriesBooks`
+ `EditSeries` commands; Series-mode header buttons in the axaml. `MainViewModel`: two overlay VMs +
flags + `IsBulkBookProperties`/`IsBookSeriesProperties`, `GoBulkBookPropertiesForBooks`,
`GoBookSeriesPropertiesForSeries`, `Close…` reload commands, `Escape` branches,
`TryLeaveCurrentEditor` OR-clauses + `LeaveAndNavigate` clears. Update `Books` + `BookDetail` ctor
calls. `MainWindow.axaml`: two more backdrop overlay blocks.
**Depends on:** Steps 3, 5, 6
**Verify:** `MainViewModelTests` (extend if nav-covered); build + launch.

## Step 8: Test sweep + docs
**Files:** design/plan status lines, `project_paperbunkr_books_browse_chrome` memory
**What:** `dotnet build` (delete `obj/Debug/net10.0/Paperbunkr.App.dll`+`.pdb` if a XAML failure
masks). `dotnet test` App.Tests + Data.Tests. Launch, run the manual checklist from the design.
Update docs + memory.
**Depends on:** all

## Notes / discovered surface
- `TileSelectionController<TCard>` + `ISelectableCard` (`Id`, `IsSelected`) are the reusable bits;
  `IssueListRow` is the `ObservableObject` precedent for a selectable card model.
- Library feeds shift-state from code-behind → `ToggleIssueSelection(row, isShiftHeld)`.
- `MainWindow.axaml` overlay blocks ~lines 644-790; corner close = `Button.rail` +
  `/Assets/Icons/Close_Circle.png` `OpacityMask`.
- `BookPropertiesScreenViewModel.ResolveSeries` is the create/reuse/detach precedent for the bulk
  editor's series handling.
- `MetadataEditHistoryEntry.Target` defaults to `Issue`; multi-id `Before`/`After` dicts already
  supported by `Apply`.
- Concurrent worktree sessions under `.claude/worktrees/`; live DB was recently broken by one and
  repaired — keep edits to `src/Paperbunkr.App*` + docs, no `Paperbunkr.Data` changes (B3 needs none).
