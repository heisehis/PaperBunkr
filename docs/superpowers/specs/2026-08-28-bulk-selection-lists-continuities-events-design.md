# Bulk Selection for Reading Lists / Continuities / Story Events — Design

**Date:** 2026-08-28
**Follows:** the Reading Lists and Events & Continuity redesigns (both 2026-08-28). Adds
multi-select so items aren't added/removed one at a time.

## Problem

Every "add issues" / "add series" search returns rows with a per-row add button — pulling 30
issues into a reading list means 30 clicks. Removing many is just as slow (one hover-`⋮` → Remove
per row). The user's words: "so frustrating".

## Scope

Three surfaces, on all three screens (Reading Lists, Continuity detail, Event detail):

1. **Add-from-search** — tick several search results, one **Add N** button.
2. **Existing items** — tick member rows / continuity posters, bulk **Remove** (+ bulk **Set role**
   on Reading Lists and Events).
3. **"Add all of a series"** — in issue search, a per-result action that adds *every* issue of that
   result's series (not just the shown/matched ones), in issue order.

## Shared infrastructure

Reuse `Paperbunkr.App.Services.TileSelectionController<TCard>` (`TCard : ISelectableCard`,
`ISelectableCard = { int Id; bool IsSelected }`) — the same controller Library's issue and series
grids use. It already handles plain-click toggle, ctrl-click, and shift-range against an ordered
list, with `SelectedIds` / `Count` / `IsSelected(id)` / `Clear()`.

Make these implement `ISelectableCard`:
- `Models/IssueSearchResult` — `int Id => IssueId;` + a mutable `bool IsSelected`.
- `Models/SeriesSearchResult` — `int Id => SeriesId;` + `bool IsSelected`.
- `ViewModels/ReadingListItemRowViewModel` — `int Id => Item.Id;` + `[ObservableProperty] bool _isSelected`.
- `ViewModels/EventMemberRowViewModel` — `int Id => Member.Id;` + `[ObservableProperty] bool _isSelected`.
- `Models/SeriesCardSample` — already implements it (Library reuse).

### The selection action bar

A consistent inline `Border` (not a new control) rendered per surface, `IsVisible` bound to that
surface's `controller.Count > 0`:

```
[ N selected ]   [ …surface-specific actions… ]                    [ Clear ]
```

`PbSurface2` background, `PbRadiusSm`, sits at the top of the search panel (add surfaces) or
directly under the list/grid header (existing-item surfaces).

### Checkbox affordance

- **Search-result rows:** a leading `CheckBox` bound to the row's `IsSelected`, always visible
  (these rows exist only to be picked). The existing per-row single-add button stays.
- **Existing member rows / posters:** a leading `CheckBox` in the `rowManage` hover group
  (fades in on hover, same mechanism as the `⋮` menu). Once `controller.Count > 0` the whole
  surface pins the checkbox column visible (a `bool AnySelected` on the VM drives an
  `IsVisible`/opacity override) so you can keep ticking without hovering each row.
- Click on the checkbox → `controller.Toggle(orderedList, row, isShiftHeld)`. The row's own
  primary click (read / open) is unaffected.

## Reading Lists (`ReadingScreenViewModel`, `ReadingScreen.axaml`)

- **`SearchSelection : TileSelectionController<IssueSearchResult>`** for the Add-issues panel.
  - Action bar: `Add {N}` (`AddSelectedIssuesCommand` — appends every selected result in list
    order, one context/`SaveChanges`, then `LoadReadingList`), `Clear`.
  - Disabled while `IsLinking` (relink is inherently single-target).
  - `OnSearchQueryChanged` and `ToggleAddIssues` (closing) call `SearchSelection.Clear()`.
- **Per-result:** `AddAllOfSeriesCommand(IssueSearchResult)` — a small "＋ all of {Series}" button.
  Adds `context.Issues.Where(i => i.SeriesId == r.Series).OrderBy(IssueOrdering.OrderByNumber)`,
  skipping issues already in the list.
- **`MemberSelection : TileSelectionController<ReadingListItemRowViewModel>`** for the item list.
  - Action bar under the list header: `Remove {N}` (`RemoveSelectedCommand`),
    `Set role ▾` (a role `ComboBox` + `Apply` → `SetRoleForSelectedCommand`),
    `Mark read` / `Mark unread` (`ToggleReadForSelectedCommand` variants), `Clear`.
  - Each bulk op: one context, loop `SelectedIds`, `SaveChanges`, `LoadReadingList`, then
    `MemberSelection.Clear()`.
  - `bool AnyMembersSelected => MemberSelection.Count > 0` drives the pinned-checkbox column.
  - Cleared on `LoadReadingList`.

## Continuity detail (`EventsScreenViewModel.Continuities.cs`, `EventsScreen.axaml`)

- **`ContinuitySeriesSelection : TileSelectionController<SeriesSearchResult>`** for Add-series.
  - Action bar: `Add {N}` (`AddSelectedSeriesCommand` → loop, `ContinuityResolver.AddSeriesToContinuity`
    for each, one context, `LoadContinuity`), `Clear`.
- **`ContinuityMemberSelection : TileSelectionController<SeriesCardSample>`** for the poster grid.
  - Poster gets a hover checkbox (top-left corner). Action bar above the grid:
    `Remove {N} from continuity` (`RemoveSelectedSeriesCommand`), `Clear`.
  - Cleared on `LoadContinuity`.

## Event detail (`EventsScreenViewModel`, `EventsScreen.axaml`)

- **`EventSearchSelection : TileSelectionController<IssueSearchResult>`** for Add-issues.
  - Action bar: a role `ComboBox` (defaults to the current `SelectedRoleOption`) + `Add {N}`
    (`AddSelectedMembersCommand` — adds each with that role via `EventMembershipResolver`), `Clear`.
  - `AddAllOfSeriesCommand(IssueSearchResult)` — same as Reading Lists, adds with the bar's role.
- **`EventMemberSelection : TileSelectionController<EventMemberRowViewModel>`** for the member list.
  - Action bar: `Remove {N}` (`RemoveSelectedMembersCommand`), `Set role ▾` + `Apply`
    (`SetRoleForSelectedMembersCommand`), `Clear`.
  - Cleared on `LoadEvent`.

## Non-goals

- Selection persistence across screen navigation (always clears).
- "Select all" / "invert" (`controller` supports it but no UI this pass — shift-range covers the
  common case; add later if asked).
- Drag-to-select / marquee.
- Bulk operations on the sidebar list of *lists* (deleting whole reading lists in bulk, etc.).
- Multi-select in the Timeline view (read-only browse).

## Testing

- `ReadingScreenViewModelTests`: `AddSelectedIssuesCommand` adds all ticked in order and clears
  selection; `RemoveSelectedCommand` removes N; `SetRoleForSelectedCommand` applies to N;
  `AddAllOfSeriesCommand` adds every issue of the series, skips duplicates; selection clears on
  list switch.
- `EventsScreenViewModelTests`: `AddSelectedMembersCommand` (with role), `RemoveSelectedMembersCommand`,
  `SetRoleForSelectedMembersCommand`, `AddSelectedSeriesCommand`, `RemoveSelectedSeriesCommand`.
- `TileSelectionController` is already covered by `TileSelectionControllerTests`.
- Manual: tick 5 search results → Add 5; hover a member row → checkbox → tick 3 → Remove 3;
  "＋ all of {Series}" pulls the whole run; role bulk-apply; every surface's selection clears when
  you switch the active item.
- Full suite per the `DatabasePathOverride` / `AvaloniaTestCollection` isolation lesson.

## Build note

Per CLAUDE.md — no new `.axaml` files here (edits to existing views only), so no `AVLN2000`
first-compile risk; still delete `obj/Debug/net10.0/Paperbunkr.App.dll`+`.pdb` and rebuild if a
XAML compile fails after `CoreCompile` succeeds.
