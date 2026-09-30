# Library bulk actions — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md*

Paths are under `src/Paperbunkr.App/` unless noted. Tests are under `src/Paperbunkr.App.Tests/`. Build/test with a private configuration
(`-c BulkActions`) so this session doesn't fight other sessions' `bin/Debug` (see the concurrent-sessions memory). Other sessions have
uncommitted edits in `LibraryScreenViewModel.cs`, `LibraryScreen.axaml(.cs)`, `IssuePropertiesScreenViewModel.cs`, `MainViewModel.cs`,
`AppSettings.cs` and the migrations folder, so every edit here is additive and must not revert them.

## Step 1: Selection pruning, invert, granularity clear
**Files:** `Services/TileSelectionController.cs` (edit), `ViewModels/LibraryScreenViewModel.cs` (`SyncSelection`, `OnGranularityChanged`),
`TileSelectionControllerTests.cs` / `LibrarySelectionPruneTests.cs` (new)
**What:** `PruneTo(IReadOnlySet<int> visibleIds)` drops ids not visible and returns whether anything changed; `InvertWithin(IList<TCard>)`.
`SyncSelection` builds the visible id sets from `result.Rows/RowGroups` and `result.Cards/CardGroups`, prunes both controllers, and
raises the selection-count properties if something was pruned. `OnGranularityChanged` clears the selection of the kind being left.
**Depends on:** none
**Verify:** new unit tests; existing `LibraryScreenViewModel*` selection tests.

## Step 2: Action catalog + builder migration
**Files:** `ViewModels/LibraryActions/LibraryAction.cs`, `LibraryActionContext.cs`, `LibraryActionCatalog.cs` (new);
`ViewModels/LibraryContextMenuBuilder.cs` (rewrite of the issue/series/remote/empty menus onto the catalog; preview-panel builders
unchanged); `LibraryActionCatalogTests.cs` (new); `LibraryContextMenuBuilderTests.cs` (update order)
**What:** the record/flags/group types from design §1. `LibraryActionContext` resolves ids once (`UnionForAction` for a click, the
current selection for the bar/keyboard); every label count reads from it (fixes the unselected-tile label bug). Menu-order and bar-order
lists per kind. Existing commands keep their parameters; the catalog passes the clicked id for menu builds and uses the existing
`…Selection` commands for the bar.
**Depends on:** none (new actions are added to the catalog in their own steps)
**Verify:** existing builder tests green; new catalog tests (per-target contents, "Mark 4 as" regression, unique gestures).

## Step 3: Rating, read-up-to-here, series-wide setters, reveal many, copy paths, invert, show in list
**Files:** `ViewModels/LibraryScreenViewModel.Rating.cs`, `.SelectionActions.cs`, `.ShowInList.cs` (new); `LibraryScreenViewModel.cs`
(`SetSeries*` bodies apply to the series union; new optional ctor params `setClipboardText`, `goReadingList`, `goCollection`);
`MainViewModel.cs` (pass them); catalog entries; `LibraryBulkActionTests.cs` (new)
**What:** `SetRatingForIssues(ids, int?)` with history snapshot/record, write-back enqueue and `RatingChanged` log per book; `SetRating`
menu/bar/keyboard commands for None/1–5. `MarkReadUpToHere(issueId)` via `IssueOrdering.OrderByRun()` on non-specials. `SetSeries*` over
`SeriesUnion` (distinct series of the resolved issues or selected series). `RevealSelection` → `RevealIssues`/`RevealSeries`.
`CopyFilePaths` (display order, fileless/remote skipped). `InvertSelection`. `ShowInList` children (reading lists + collections incl.
series-membership), navigation through the injected callbacks.
**Depends on:** Step 2
**Verify:** VM tests per action (rating + undo + write-back queued, run order + specials excluded, series union, copy path order).

## Step 4: Shared clipboard, Copy/Paste/Clear Data
**Files:** `Services/MetadataClipboardService.cs` (new); `ViewModels/IssuePropertiesScreenViewModel.cs` (clipboard via the service,
registry-keyed); `ViewModels/PasteDataScreenViewModel.cs`, `Views/PasteDataOverlay.axaml(.cs)` (new); `ViewModels/LibraryScreenViewModel
.DataTransfer.cs` (new); `MainViewModel.cs` + `Views/MainWindow.axaml` (overlay host, Escape chain, `IsEditorOverlayOpen`);
`src/Paperbunkr.Data/Entities/AppSettings.cs` + a new migration (`PasteDataFields`); tests `MetadataClipboardServiceTests.cs`,
`PasteDataScreenViewModelTests.cs`, `LibraryClearDataTests.cs`
**What:** clipboard = source caption + `Dictionary<string,string?>` keyed by `BulkFieldRegistry` label (the editor maps its staged fields
to and from those labels; editor-only fields ride along under their own keys). Paste field list = the registry minus the three
series-owned fields. Paste/Clear: snapshot → apply → one history record → write-back enqueue (bulk editor rule) → toast. Clear uses
`ConfirmDialog` via `IDialogService`.
**Depends on:** Step 2
**Verify:** unit tests (ticked-only, empty clears, list replaced, undo restores, clear keeps read state/series/cover); editor copy→Library
paste round-trip.

## Step 5: Refresh thumbnails + re-read from file
**Files:** `Services/IssueFileRescanService.cs` (new); `ViewModels/LibraryScreenViewModel.Refresh.cs` (new); catalog entries;
`IssueFileRescanServiceTests.cs`, `LibraryRefreshTests.cs` (new)
**What:** rescan = `EmbeddedComicInfoReader.TryRead` + `MapStoryFields(info, issue)` (overwrite) + page count/file size; returns
Updated/NoEmbeddedInfo/Failed. Both actions run as Activity Center jobs (thumbnail job skips custom covers); re-read confirms first,
records one history entry for the finished books, reports failures.
**Depends on:** Step 2
**Verify:** service tests with a temp CBZ containing / lacking ComicInfo.xml and a missing path; VM test for history + failure counts.

## Step 6: Merge series
**Files:** `ViewModels/MergeSeriesScreenViewModel.cs`, `Views/MergeSeriesOverlay.axaml(.cs)` (new); `LibraryScreenViewModel.SeriesMerge.cs`
(new); `MainViewModel.cs` + `MainWindow.axaml` (overlay host, Escape chain); `MergeSeriesScreenViewModelTests.cs` (new)
**What:** candidate rows, default = most issues, duplicate count by `(EffectiveNumber, EffectiveVolume)`, confirm → `SeriesMergeHelper
.MergeInto` per source in a fresh context, one `SaveChanges`, Library reload deferred, selection cleared.
**Depends on:** Step 2
**Verify:** VM tests (duplicates removed, issues moved, target kept, default target).

## Step 7: Selection bar
**Files:** `Controls/SelectionActionBar.cs` (new, code-built `TemplatedControl`-free `UserControl`) or `Views/SelectionActionBar.axaml(.cs)`;
`Views/LibraryToolbar.axaml` (replace the selection row, remove the Plugins/Add-to-List popups); `LibraryScreenViewModel.cs` (`BarActions`
list rebuilt on selection change; retire `IsLibraryPluginMenuOpen`/`IsAddToListOpen` toggles if unused elsewhere);
`SelectionActionBarViewTests.cs` (new)
**What:** icon buttons from the catalog's bar entries, tooltip = label + gesture, automation name = label, ▾ entries open
`ContextMenuHost.ShowMenu(anchor, children)`, separators between groups, Delete/Clear right-aligned. DynamicResource brushes only.
**Depends on:** Steps 2–6 (for the full action set; can land earlier with the existing set)
**Verify:** headless view test; `avalonia-pro-max/review-checklist`.

## Step 8: Keyboard
**Files:** `Views/LibraryScreen.axaml.cs` (`OnLibraryScreenKeyDown` → catalog lookup), `ViewModels/MainViewModel.cs` (Esc → clear Library
selection as the last Escape branch), `LibraryKeyboardTests.cs` (new or in `LibraryScreenViewTests.cs`)
**What:** gesture table from the catalog; TextBox focus skip retained; `/` and type-ahead unchanged.
**Depends on:** Steps 2–6
**Verify:** headless keyboard tests (Alt+Shift+3 rates, Ctrl+C in search box doesn't copy data, Esc clears).

## Step 9: Wrap-up
**Files:** `docs/paperbunkr-todo.md`, `docs/ce-feature-inventory.md`
**What:** record what shipped and what was verified (and that nothing was seen on screen yet); full App.Tests run under `-c BulkActions`;
review checklist on the bar and both overlays; delete the `bin/BulkActions`/`obj/BulkActions` folders at the end.
**Depends on:** all
