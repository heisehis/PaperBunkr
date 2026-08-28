# Continuity Editing — Design

**Date:** 2026-08-28
**Follows:** the Events & Continuity redesign (2026-08-28). Fills the editing gaps in the
Continuity section (and one in Events).

## Problem

A continuity can be created (name + publisher) and have series added/removed, but **cannot be
renamed, cannot have its publisher/description edited, and cannot be deleted** — the sidebar
continuity rows don't even have a delete affordance. Story events have the same rename/edit gap.
Beyond that: no way to give a member series a note, no way to order the series, no way to fold two
overlapping continuities together.

## Scope — four parts, ordered by dependency

### Part A — Details editor + delete (app-layer)

Reachable from **`⋯ Manage → Edit details`** on the continuity detail. Reuses the
`NewEventOrContinuityViewModel` + `NewEventOrContinuityOverlay` from the redesign, promoted to a
create-**or-edit** editor:

- Rename it `EventOrContinuityEditorViewModel` / `EventOrContinuityEditorOverlay`.
- Add `Load(Kind kind, int id)` — pre-fills `Name` / `Publisher` (continuity) / a new
  `Description` field (both kinds), and remembers the id + a `bool IsEdit`.
- The editor gets a `Description` `TextBox` (multi-line), shown for both kinds
  (`StoryEvent.Description` and `Continuity.Description` both exist).
- On save: create path unchanged; **edit path** updates the entity's `Name` / `Publisher` /
  `Description` + `UpdatedAt`, then invokes an `onSaved(kind, id)` callback that reloads the
  detail.
- `MainViewModel`: the existing `IsNewEventDialogOpen` flag + overlay serve edit too;
  `OpenEditEventDialog` / `OpenEditContinuityDialog` commands call `Load` then open.
- **`⋯ Manage`** for an **event** gains **Edit details** (fixes the "events can't be renamed" gap)
  and **Delete event** (relays to the sidebar's existing `DeleteConfirm`).
- **`⋯ Manage`** for a **continuity** gains **Edit details** and **Delete continuity**.
- **Continuity delete:** `ContinuitySummary` gains a `DeleteConfirm` (`TwoStepConfirm`, like
  `StoryEventSummary`); `RefreshContinuitiesSidebar` populates it; a new `DeleteContinuity(int)`
  private method (`context.Continuities.Remove`, `SaveChanges`, fall back to the next continuity
  or clear the pane). The redesigned sidebar's continuity rows get the same hover-delete button
  the event rows have.

### Part B — Merge continuities (app-layer)

On the continuity detail, when `OverlappingContinuities` is non-empty, each overlap card gains a
**`Merge into this`** action (alongside "Show overlap"). Confirm via a `TwoStepConfirm`-style
inline arm. On confirm: `ContinuityResolver.Merge(context, sourceId, targetId)` —

- move every membership of `source` to `target` (skip any the target already has),
- carry over each membership's `Note` / `SortOrder` (Part C) when the target doesn't already have
  that series,
- delete `source`,
- reload the detail on `target`.

`Merge` is a new resolver method with its own `ContinuityResolverTests` cases (disjoint sets;
overlapping sets; self-merge is a no-op).

### Part C — Explicit `ContinuityMembership` join entity (data-model change)

Per-series note **and** deliberate ordering both need columns on the continuity↔series link, which
today is EF Core's implicit skip-navigation join (no join entity). Introduce an explicit one:

```
class ContinuityMembership
{
    int Id;
    int ContinuityId;   Continuity? Continuity;
    int SeriesId;       Series? Series;
    string? Note;       // "flagship title", "spin-off", free text
    int SortOrder;      // deliberate order; default = append
}
```

- **Entity/context:** `Continuity.Series` (`List<Series>`) → `Continuity.Memberships`
  (`List<ContinuityMembership>`); `Series.Continuities` → `Series.ContinuityMemberships`. Configure
  the FK: `ContinuityId` cascade-delete (deleting a continuity drops its memberships), `SeriesId`
  cascade-delete on the membership only (deleting a series drops its memberships, never the
  continuity). Unique index on `(ContinuityId, SeriesId)`.
- **Migration** `ContinuityMembershipJoinEntity`: create the table, **copy the existing implicit
  join rows** (`ContinuitySeries` or whatever EF named it — check the current model snapshot) into
  it with `SortOrder` = row-number per continuity ordered by series name, `Note` = null, then drop
  the old join table. Hand-write the data-copy `Sql(...)` (the scaffolder won't).
- **`ContinuityResolver` rewrite:** all 8 methods move from `s.Continuities` / `c.Series` skip-nav
  queries to `c.Memberships` / `s.ContinuityMemberships`. `GetSeriesInContinuity` orders by
  `SortOrder` then name. New: `SetMembershipNote(context, continuityId, seriesId, note)`,
  `ReorderMembership(context, continuityId, seriesId, newIndex)` (or a bulk
  `SetMembershipOrder(context, continuityId, orderedSeriesIds)`).
- **`AddSeriesToContinuity`** appends with `SortOrder = max + 1`.

### Part D — Per-series note + reorder UI (app-layer, needs Part C)

On the continuity detail's poster grid:

- **Note:** the member poster's `⋮` (or a small pencil on hover) → an inline `TextBox` for the
  membership `Note`; the note renders as a caption under the poster name when set.
  `EventsScreenViewModel.Continuities.cs` gets `SetContinuitySeriesNoteCommand`;
  `SeriesCardSample` (as used for continuity members) carries a `MembershipNote` string.
- **Reorder:** drag a poster to a new position → `SetContinuitySeriesOrderCommand` persists the
  new `SortOrder` sequence and reloads. (If drag-drop proves heavy, fall back to `Move ◀ / ▶` on
  the poster hover — decided during implementation, same call the Reading Lists redesign made for
  member reordering.)

## Non-goals

- Continuity hierarchy / parent-child continuities.
- Membership date ranges ("in continuity from year X to Y").
- Migrating `MediaRelation.SameContinuity` pairwise assertions into `Continuity` groupings.
- Editing a continuity's series set from anywhere but this screen.

## Testing

- `EventOrContinuityEditorViewModelTests`: edit path updates name/publisher/description +
  `UpdatedAt`; `onSaved` fires; create path unchanged.
- `EventsScreenViewModelTests`: `DeleteContinuityCommand` removes it and falls back;
  `SetContinuitySeriesNoteCommand`; `SetContinuitySeriesOrderCommand` persists order;
  `GetSeriesInContinuity` returns `SortOrder` order.
- `ContinuityResolverTests`: the full method-set re-verified against the join entity; `Merge`
  cases; note/reorder round-trips.
- New `MetadataModelContinuityMembershipMigrationTests` (Data.Tests): the migration copies every
  existing implicit-join row into `ContinuityMembership` with a stable per-continuity `SortOrder`.
- Full App.Tests + Data.Tests unfiltered.

## Build order

A → B can ship together (pure app-layer, no migration). C is the schema change and must land
before D. Recommended: **A+B first**, then **C**, then **D**. Each is independently shippable.

## Build note

Part C adds an EF migration — inspect the generated file before running it (this project has hit
scaffolder `RenameColumn` / `AddColumn(defaultValue:)` mistakes before), and run it against a
throwaway connection, not the dev DB, first. Tests redirect via
`PaperbunkrDbContext.DatabasePathOverride`.
