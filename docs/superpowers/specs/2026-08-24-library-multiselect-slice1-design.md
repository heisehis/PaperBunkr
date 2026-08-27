# Library Multi-Selection, Slice 1: Per-Issue Selection + Bulk Edit/Delete

**Date:** 2026-08-24
**Status:** Approved, pending implementation

## Context

Library has no multi-selection model today. `LibraryScreenViewModel.MarkIssueRead`'s own doc comment
says as much: *"single-item only, Library has no multi-selection model to bulk-apply over"*
([ViewModels/LibraryScreenViewModel.cs:897-899](../../../src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs)).
No `SelectedItems`, no `IsSelected` flag, no ctrl/shift-click handling exists anywhere in Library's
view models or views.

The full scope the user actually wants is larger than one pass: multi-select across all of Library's
view modes (per-issue tiles/list/details, series-cards, and grouped views) feeding four actions (bulk
edit, bulk delete, mark read/unread, add to reading list). Decomposed into slices, matching this
project's established pattern for large features (Metadata Model Phases 1-7, Library Search Modes
Slices 1-4, Reader Polish slices):

- **Slice 1 (this spec):** per-issue selection (covers every issue-granularity layout at once - see
  below) + bulk edit + bulk delete, the two actions that already exist as screens/commands and just
  need wiring.
- **Slice 2 (deferred):** mark read/unread + add-to-reading-list bulk actions, still issue-level.
- **Slice 3 (deferred):** series-card selection + a net-new series-level bulk editor (`SeriesCardSample`
  is a separate model from `IssueListRow`, and there's no existing series-level bulk-edit screen to
  wire into, unlike issue-level bulk edit).

## Key finding: one model covers seven layouts

`IssueListRow` ([Models/IssueListRow.cs](../../../src/Paperbunkr.App/Models/IssueListRow.cs)) already
backs every issue-granularity `DataTemplate` in `LibraryScreen.axaml` - Compact/Comfortable/CoverOnly/
Panorama grids, List, Details, and Tiles all bind to the same `IssueListRow`, just laid out
differently. Adding selection to this one model covers all seven layouts in this single pass; there is
no separate "Comic List" data type that would need its own wiring later.

## CE verification

Per this project's standing rule, checked against `_reference/ComicRackCE` before designing:
`ItemView.cs` (lines 3695, 3750, 3857-3859, 4116, 4125, 4289) and `ComicListLibraryBrowser.cs`
(lines 525, 1109) check `Control.ModifierKeys.HasFlag(Keys.Control)`/`Keys.Shift` directly - CE's
multi-select is native WinForms `ListView` selection supporting both ctrl+click (additive toggle) and
shift+click (range). No dedicated checkbox-mode or select-all command was found in CE's source.
Paperbunkr's own existing precedent (`DetailTabsViewModel`, built before this spec) only implements
shift+click + plain-click-toggle, no ctrl+click - this slice closes that gap for both screens at once
(see "Shared selection controller" below), reaching fuller CE parity than what shipped in the Detail
screen's original bulk-editing spec.

## What changes

### 1. `IssueListRow`: plain class → partial `ObservableObject`

Exact precedent: `IssueCardSample` ([Models/IssueCardSample.cs](../../../src/Paperbunkr.App/Models/IssueCardSample.cs))
was converted from a plain `init`-only POCO to a partial `ObservableObject` specifically to add a
live-notifying `IsSelected` bool (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md §1).
`IssueListRow` gets the identical treatment: `sealed class` → `sealed partial class : ObservableObject`,
every existing property stays exactly as it is (all still `init`-only, row instances are still rebuilt
fresh on every reload per the class's own existing doc comment), and a new
`[ObservableProperty] private bool _isSelected;` is added. This is the only change to the model itself.

### 2. Shared `TileSelectionController`

New type (`Services/TileSelectionController.cs` or `ViewModels/TileSelectionController.cs` - exact
placement is an implementation-time call) extracting and generalizing
`DetailTabsViewModel.ToggleIssueSelection` ([ViewModels/DetailTabsViewModel.cs:896-929](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs)),
which already implements `HashSet<int> SelectedIssueIds` + `_lastToggledIndex`-based shift-range
selection. Generalized over a small `ISelectableCard` interface (`int Id { get; }`,
`bool IsSelected { get; set; }`) that both `IssueCardSample` and `IssueListRow` implement, so the
controller works against either model without depending on either concretely.

Gestures the controller exposes:
- **Plain click** (no modifiers): selects just this item, clearing everything else. (Library's tiles
  don't actually call this path - see "Entry gesture" below - but Detail's issue grid, which has no
  competing plain-click-to-navigate behavior, keeps using it exactly as today.)
- **Ctrl+click / checkbox click**: additive toggle of just this one item, leaving the rest of the
  selection untouched. **New** - neither `DetailTabsViewModel` nor Library had this before; both gain
  it from the shared controller.
- **Shift+click**: extends a contiguous range from the last-toggled index to this one, in whichever
  order is currently displayed (respects active sort; when grouped, uses the flattened group order).
  Unchanged behavior, just generalized.

`DetailTabsViewModel` is migrated onto this shared controller in the same pass (replacing its own
`SelectedIssueIds`/`_lastToggledIndex`/`ToggleIssueSelection` with calls into the controller) rather
than left as a near-duplicate - the "two places want the same shape" signal is exactly what this
extraction responds to. Its existing tests get re-pointed at the shared controller's behavior rather
than duplicated.

### 3. Entry gesture in Library: checkbox + ctrl/shift-click, plain click untouched

Library's plain click is load-bearing (it's the primary way into Reader/Detail), unlike Detail's own
issue grid (a browsing dead-end where sacrificing plain-click-to-navigate for plain-click-to-toggle
was fine). This slice does **not** touch plain-click behavior in Library at all:

- A small checkbox appears in each tile's corner, hover-revealed normally, but **all** tiles' checkboxes
  stay visible (not just the hovered one) once any tile in the current view is selected - so the whole
  active selection is visible/adjustable without hunting for hover targets.
- Checkbox click = ctrl-click-equivalent additive toggle of just that tile.
- Ctrl+click / shift+click on the tile body itself work as power-user shortcuts to the same actions,
  without needing to find the checkbox.
- Plain click with no modifiers keeps navigating to Reader/Detail exactly as it does today, in every
  layout, selection active or not.

This applies to all seven `IssueListRow`-backed `DataTemplate`s (Compact/Comfortable/CoverOnly/
Panorama/List/Details/Tiles) - each needs the checkbox markup and the ctrl/shift click wiring added,
but it's one selection system underneath, not seven.

### 4. Right-click union (unchanged from Detail's existing precedent)

`DetailTabsViewModel.EditIssueProperties`/`RevealIssue`'s existing union logic
(`SelectedIssueIds.Count > 0 ? SelectedIssueIds.Append(issue.Id) : [issue.Id]`, deduplicated) is
reused as-is for Library's context menu: right-clicking a tile acts on "whatever's currently selected,
plus the right-clicked tile," computed fresh for that one action - it does **not** mutate the
persisted selection (an unselected tile that gets included in a right-click action doesn't become
visibly selected afterward). Right-clicking a lone unselected tile with nothing else selected still
just acts on that one tile alone, exactly like today.

### 5. Bulk edit wired into Library for the first time

Library currently has **zero** issue metadata-edit entry point (`_goToNewIssueProperties` is only the
placeholder "add a physical book" creation flow -
[ViewModels/LibraryScreenViewModel.cs:30,51,1138](../../../src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs)).
This slice adds both single- and multi-issue editing to Library's context menu for the first time:
`LibraryScreenViewModel`'s constructor gains a `goBulkIssueProperties` callback (mirroring
`DetailScreenViewModel`'s existing constructor shape), wired through `MainViewModel` to the
already-existing `BulkIssuePropertiesScreenViewModel`/`IssuePropertiesScreenViewModel`, dispatching by
selection-union count exactly like `DetailTabsViewModel.EditIssueProperties` already does (count == 1
→ single editor, count > 1 → bulk editor).

### 6. Bulk delete

`DeleteIssueCommand` ([ViewModels/LibraryScreenViewModel.cs:961](../../../src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs))
currently takes a single `int issueId` and calls `LibraryDeletionHelper` for that one issue (recycle
bin + reading-list/event cross-reference cleanup, per docs/superpowers/specs/
2026-08-22-delete-functionality-design.md). Extended to accept the right-click union instead of one
id, looping the same per-issue deletion logic - no change to `LibraryDeletionHelper` itself, no change
to the recycle-bin/confirmation UX (the existing submenu-confirm pattern), just fed more than one id
when the union has more than one.

### 7. Selection action bar

When selection is non-empty, Library's existing toolbar area shows a contextual bar instead of (or
above) the normal sort/filter controls: "N selected", Bulk Edit, Delete, Clear. Reverts to the normal
toolbar when selection becomes empty - whether via Clear, an action completing, or navigating away
from Library (selection does not persist across navigation).

## Explicitly not changing

- Library's plain-click-to-navigate behavior, in any layout - untouched.
- `LibraryDeletionHelper`'s deletion logic itself (recycle bin, cross-reference cleanup) - reused
  as-is, just invoked per-id from a loop instead of once.
- Series-card selection (`SeriesCardSample`) - stays fully out of scope for this slice, deferred to
  Slice 3.
- Mark read/unread and add-to-reading-list bulk actions - deferred to Slice 2; their existing
  single-item commands (`MarkIssueReadCommand`/`MarkIssueUnreadCommand`) are untouched by this slice.

## Testing

- `TileSelectionController` gets direct unit tests: plain-toggle, ctrl-additive-toggle, shift-range
  (including range direction both ways, and re-anchoring `_lastToggledIndex` after each gesture),
  independent of any View or ViewModel.
- `LibraryScreenViewModelTests`: selection state after each gesture type; right-click union
  computation (selected-plus-clicked, deduplicated, unselected-lone-tile case); bulk delete acting on
  multiple ids via one call; the bulk-vs-single dispatch threshold for the new edit entry point;
  selection clearing on navigation-away.
- `DetailTabsViewModelTests`: existing selection-behavior tests re-pointed at the shared controller
  (same assertions, same coverage) rather than duplicated - plus new coverage for the ctrl-click path
  Detail didn't have before.
