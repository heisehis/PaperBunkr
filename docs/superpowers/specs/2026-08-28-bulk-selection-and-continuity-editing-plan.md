# Bulk Selection + Continuity Editing — Implementation Plan
*Implements: 2026-08-28-bulk-selection-lists-continuities-events-design.md
and 2026-08-28-continuity-editing-design.md*

Five independently-shippable chunks, in order. Build + test green after each.

**Status: all five chunks shipped 2026-08-28** (uncommitted on `books/browse-chrome`). Final:
App.Tests 1127/1127, Data.Tests 539/539. Notable deviations from the plan below:
- C3: the editor VM/overlay kept their `NewEventOrContinuity*` names (renaming to
  `EventOrContinuityEditor*` would trip the new-`x:Class` XAML-weave gotcha for no real benefit).
- C4: `.Select(entity).Distinct()` doesn't dedupe in EF/SQLite — resolver methods that relied on it
  now select distinct ids then load. `DetailTabsViewModel` needed no changes (resolver-only).
- Extra: fixed a pre-existing bug where Detail's poster rails (Related / Same Continuity / Same
  Event / More Like This) and the continuity poster grid never loaded real covers.

---

## Chunk 1 — Bulk selection: Reading Lists

**C1.1 — `ISelectableCard` on the row types**
Files: `Models/IssueSearchResult.cs`, `ViewModels/ReadingListItemRowViewModel.cs`
- `IssueSearchResult`: `int Id => IssueId;` + `bool IsSelected { get; set; }`; implement `ISelectableCard`.
- `ReadingListItemRowViewModel`: `int Id => Item.Id;` + `[ObservableProperty] bool _isSelected`; implement `ISelectableCard`.

**C1.2 — VM: selection + bulk commands**
File: `ViewModels/ReadingScreenViewModel.cs`
- `TileSelectionController<IssueSearchResult> SearchSelection { get; } = new();`
- `TileSelectionController<ReadingListItemRowViewModel> MemberSelection { get; } = new();`
- `bool AnyMembersSelected => MemberSelection.Count > 0` (+ `bool AnySearchSelected`), raised after every toggle.
- `[RelayCommand] ToggleSearchSelection(IssueSearchResult)` / `ToggleMemberSelection(ReadingListItemRowViewModel)` — call `controller.Toggle(list, item, isShiftHeld:false)` then raise the `Any*` props + `SelectionSummary`. (Shift-range wired from XAML via a `KeyModifiers` check is a later nicety; plain toggle now.)
- `[RelayCommand] ClearSearchSelection` / `ClearMemberSelection`.
- `[RelayCommand] AddSelectedIssues` — one context, append each `SearchSelection.SelectedIds` issue (skip dupes), `SaveChanges`, `LoadReadingList`, `SearchSelection.Clear()`. `CanExecute` false while `IsLinking`.
- `[RelayCommand] AddAllOfSeries(IssueSearchResult)` — `context.Issues.Where(i => i.SeriesId == r's series).OrderByNumber()`, add non-dupes, reload.
  (Need the series id on `IssueSearchResult` — add `int SeriesId { get; init; }` and populate it in `Search()`.)
- `[RelayCommand] RemoveSelectedMembers` — loop `MemberSelection.SelectedIds` → `RemoveItem`-equivalent in one context, reload, clear.
- `[RelayCommand] SetRoleForSelectedMembers(EventMembershipRoleOption)` + `[ObservableProperty] EventMembershipRoleOption _bulkRole`.
- `[RelayCommand] MarkSelectedRead` / `MarkSelectedUnread` — `IssueReadStateResolver` per member, reload.
- `LoadReadingList` / `OnSearchQueryChanged` / `ToggleAddIssues(close)` → clear the relevant controller.

**C1.3 — XAML: `ReadingScreen.axaml`**
- Add-issues panel: leading `CheckBox IsChecked="{Binding IsSelected}"` on each result row bound
  through `ToggleSearchSelectionCommand`; a `＋ all of {Series}` `Button` per row; a selection
  bar `Border` (`IsVisible="{Binding AnySearchSelected}"`) — "{N} selected · Add {N} · Clear".
- Member list: a leading `CheckBox` in a new left column, in the `rowManage` hover group but
  forced visible when `AnyMembersSelected`; a selection bar above the group list —
  "{N} selected · Remove · [role combo] Apply · Mark read · Mark unread · Clear".

**C1.4 — tests** (`ReadingScreenViewModelTests`): `AddSelectedIssues` adds all + clears;
`AddAllOfSeries` adds the run, skips dupes; `RemoveSelectedMembers`; `SetRoleForSelectedMembers`;
selection clears on list switch.

**Verify:** build; `dotnet test --filter ReadingScreenViewModelTests`.

---

## Chunk 2 — Bulk selection: Continuity + Event

Mirror Chunk 1 on `EventsScreenViewModel` (+ `.Continuities.cs`) and `EventsScreen.axaml`:
- `EventSearchSelection` / `EventMemberSelection` / `ContinuitySeriesSelection` /
  `ContinuityMemberSelection` (`TileSelectionController<…>`).
- `ISelectableCard` on `SeriesSearchResult` (`Id => SeriesId`) and `EventMemberRowViewModel`
  (`Id => Member.Id`). `SeriesCardSample` already has it.
- Bulk commands: `AddSelectedMembers` (with `BulkRole`), `AddAllOfSeriesToEvent`,
  `RemoveSelectedMembers`, `SetRoleForSelectedMembers`, `AddSelectedSeries`,
  `RemoveSelectedSeries`.
- XAML: checkboxes on the event add-issues results + member rows; hover checkbox on the
  continuity add-series results + member posters (top-left corner); selection bars per surface.
- Clear on `LoadEvent` / `LoadContinuity` / search-panel close.
- Tests in `EventsScreenViewModelTests`.

**Verify:** build; `dotnet test --filter EventsScreenViewModelTests`.

---

## Chunk 3 — Continuity editing A + B (details editor + delete + merge)

**C3.1 — promote the editor VM**
Files: `ViewModels/NewEventOrContinuityViewModel.cs` → rename `EventOrContinuityEditorViewModel`;
`Views/NewEventOrContinuityOverlay.axaml(.cs)` → `EventOrContinuityEditorOverlay`.
- Add `[ObservableProperty] string _description`; a multi-line `Description` `TextBox` in the
  overlay (both kinds).
- `bool IsEdit`; `int _editId`; `Load(Kind kind, int id)` pre-fills from the entity.
- `_onCreated` stays; add `_onSaved`. `Create` splits: new vs update (`Name`/`Publisher`/
  `Description`/`UpdatedAt`), then fire the right callback.
- `MainViewModel`: keep `IsNewEventDialogOpen`; add `OpenEditEventDialogCommand` /
  `OpenEditContinuityDialogCommand` (call `Load` then open); `OnEventOrContinuitySaved` reloads
  the active detail. Update the `MainWindow.axaml` overlay `DataContext` name.

**C3.2 — delete + ⋯ Manage**
Files: `Models/ContinuitySummary.cs`, `ViewModels/EventsScreenViewModel.Continuities.cs`,
`Views/EventsScreen.axaml`, `Views/MainWindow.axaml`
- `ContinuitySummary` record gains `TwoStepConfirm DeleteConfirm` (positional or `init`);
  `RefreshContinuitiesSidebar` builds it → `DeleteContinuity(int)` (remove, `SaveChanges`, fall
  back to next / clear pane, refresh sidebar).
- Sidebar continuity rows get the hover-delete button (mirror the event row).
- `EventsScreen.axaml`: event `⋯ Manage` (currently absent) → add it with **Edit details**
  (`OpenEditEventDialogCommand`) + **Delete event** (relay to the summary's `DeleteConfirm` via a
  VM `DeleteActiveEventCommand`). Continuity `⋯ Manage` gains **Edit details** + **Delete
  continuity** (`DeleteActiveContinuityCommand`).

**C3.3 — merge** (`Data/Metadata/ContinuityResolver.cs`, `EventsScreenViewModel.Continuities.cs`,
`EventsScreen.axaml`)
- `ContinuityResolver.Merge(context, sourceId, targetId)` — move memberships, dedupe, delete
  source. (Pre-Chunk-4 it operates on the implicit join; Chunk 4 updates it for the entity.)
- Overlap card gains **Merge into this** with an inline two-step arm; `MergeContinuityCommand`.

**C3.4 — tests:** `EventOrContinuityEditorViewModelTests` (edit path), `EventsScreenViewModelTests`
(`DeleteActiveContinuityCommand`, event Edit/Delete), `ContinuityResolverTests` (`Merge`).

**Verify:** build; targeted tests; full App.Tests.

---

## Chunk 4 — `ContinuityMembership` join entity (schema change)

Files: `Data/Entities/ContinuityMembership.cs` (new), `Data/Entities/Continuity.cs`,
`Data/Entities/Series.cs`, `Data/PaperbunkrDbContext.cs`,
`Data/Migrations/*_ContinuityMembershipJoinEntity.*` (new),
`Data/Metadata/ContinuityResolver.cs` (rewrite all 8 methods),
`ViewModels/EventsScreenViewModel.Continuities.cs` (member load → ordered + note),
`Models/SeriesCardSample.cs` (a `MembershipNote` field for the continuity-member case).
- New entity per the design. `Continuity.Series`→`Memberships`; `Series.Continuities`→
  `ContinuityMemberships`. FK config + unique `(ContinuityId, SeriesId)` index.
- Migration: `CreateTable` + hand-written `Sql` to copy the implicit-join rows in
  (`SortOrder` = per-continuity row number by series name, `Note` = null) + `DropTable` the old
  join. **Inspect the generated file first.** Regenerate the model snapshot.
- `ContinuityResolver`: every method off the new nav; `GetSeriesInContinuity` orders by
  `SortOrder`; `AddSeriesToContinuity` appends `SortOrder = max+1`; add
  `SetMembershipNote` / `SetMembershipOrder(continuityId, orderedSeriesIds)`.
- `Merge` (from C3.3) updated to carry `Note`/`SortOrder`.
- New `Data.Tests` migration test: every old join row lands in `ContinuityMembership`.

**Verify:** `dotnet ef migrations add` review; build; full Data.Tests + App.Tests.

---

## Chunk 5 — per-series note + reorder UI

Files: `ViewModels/EventsScreenViewModel.Continuities.cs`, `Views/EventsScreen.axaml`,
`Models/SeriesCardSample.cs`
- `SetContinuitySeriesNoteCommand(SeriesCardSample)` + a per-poster hover pencil → inline
  `TextBox` bound to a row-VM/`SeriesCardSample` `Note`; note renders as a caption under the name.
- Reorder: `Move ◀ / ▶` on poster hover → `SetContinuitySeriesOrderCommand` persists the sequence
  and reloads. (Drag-drop is a follow-up; buttons match the Reading Lists redesign's call.)
- Tests: note round-trip, order round-trip, `GetSeriesInContinuity` returns the set order.

**Verify:** build; full App.Tests + Data.Tests; manual on-screen pass across all 5 chunks.
