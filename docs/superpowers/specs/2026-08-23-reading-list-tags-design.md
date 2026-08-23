# Reading List Tags + Properties Overlay

**Date:** 2026-08-23
**Status:** Approved, pending implementation plan
**Source:** Follow-up flagged during the weighted/categorized tags design
(`docs/superpowers/specs/2026-08-23-weighted-categorized-tags-design.md`, "Explicitly out of
scope": "Reading Lists don't carry tags at all... explicitly deferred until after this feature
ships"). That feature has since shipped in full (entity, migration, Smart Lists/search rewire,
Detail-screen clickable+weighted chips extended to Teams/Locations/credits, Issue Properties
Editor rows, CBZ write-back). This spec covers the deferred half: tagging the Reading List itself,
not its member issues (which already have `IssueTag` from the prior spec).

## Context

`ReadingList` (`src/Paperbunkr.Data/Entities/ReadingList.cs`) has no tagging concept today, and
neither does CE's own reading-list model (`ComicIdListItem`, checked directly in
`_reference/ComicRackCE/ComicRack.Engine/Database/ComicIdListItem.cs`) - this is a Paperbunkr-
original addition, not CE parity. `ReadingScreenViewModel` currently edits `Name`/`Description`/
`Type`/`Source`/`ArcId`/`ArcName`/`CoverImageUrl` inline, persisting each field immediately on
change (e.g. `PersistTypeChange`) - no Save/Cancel buffer, unlike the Issue Properties Editor.

Per explicit user direction, "tags on reading lists" means the list itself carries its own
weighted/categorized tags (e.g. "Dark", "Recommended Order") - a descriptor of the list, distinct
from what its member issues are tagged with. This also comes with a scope decision to consolidate
Reading List editing: today's inline immediate-persist fields move into a new buffered-Save
properties overlay alongside the new Tags row, rather than adding a second, disconnected editing
surface just for tags.

## Scope

### Data model

New entity `ReadingListTag`, no `Field` discriminator (unlike `IssueTag` - a Reading List has one
tag concept, not a Genre-vs-Tags split):

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` (PK) | |
| `ReadingListId` | FK → `ReadingList`, cascade delete | |
| `Value` | `string` | The tag text. |
| `Category` | `string?` | Free text, extensible. Null renders "Uncategorized" - same rule as `IssueTag.Category`. |
| `Weight` | `enum { Unset, Incidental, Recurrent, Defining, Core }` | Same 5-tier scale as `IssueTagWeight`, ascending significance. Starts `Unset`, never inferred. |

One EF migration adds `ReadingListTags`. No data migration needed (brand-new concept, nothing to
backfill).

### `ReadingListPropertiesOverlay` (new)

Not a screen swap - a modal-style popup layered above whatever screen is currently active,
reusing the exact compositing pattern `MigrationOverlay` already established: a dimmed backdrop
`Border` (`Background="#B0000000"`) inside `MainWindow.axaml`, toggled by a bool flag on
`MainViewModel`, hosting the new overlay `UserControl` plus a close button. No new child-`Window`
mechanism - this app has none today, and the overlay pattern already covers "pops up above the
current screen" without the added cost of separate window lifecycle/positioning/chrome.

`ReadingListPropertiesScreenViewModel` uses the same buffered edit-buffer discipline as
`IssuePropertiesScreenViewModel`: `Load` copies `Name`/`Description`/`Type`/`Source`/`ArcId`/
`ArcName`/`CoverImageUrl` plus per-tag `Category`/`Weight` rows into buffer properties; `Save`
writes everything at once; `Cancel` discards. This **replaces** `ReadingScreenViewModel`'s current
inline immediate-persist editing for those same fields - one editing surface, not two. The sidebar/
item-browsing/reordering parts of `ReadingScreen` are untouched; only the "edit this list's own
properties" path moves into the overlay, reached via an Edit affordance next to the selected list
(mirroring how Issue Properties Editor is reached via right-click/Edit on an issue).

Tag rows in the overlay follow the same shape as the Issue Properties Editor's Genre/Tags Details
section: a plain CSV text box for adding/removing tag values, plus a per-value Category dropdown
and Weight picker for values that already exist.

### Detail-pane chip row (new)

The selected list's detail pane gains a chip row near its header (next to the existing
"Cross-series reading order · tracked list"-style subtitle), reusing `TagPillViewModel` - same
weight-based styling (bold/opaque Core → faint Incidental) as every other tag surface in the app,
no new visual language.

- **Left-click** a chip: filters the Reading Lists sidebar to only lists carrying that tag (new
  capability - the sidebar has no search/filter today). A visible "clear filter" affordance
  appears while a filter is active, since there's no search box to empty out instead.
- **Right-click** a chip: the same Weight-only quick-reweight popover Issue tags have. Simpler
  here than Issue tags' version - a Reading List is always one concrete list (no series-vs-single-
  issue ambiguity), so `CanReweight` is always true, no disabled state to handle.

Category is only editable from the properties overlay, same split Issue tags already have between
their reweight popover (Weight-only) and their editor (Category+Weight).

## Explicitly out of scope

- **Surfacing/aggregating the member issues' own tags on the reading list** - the other half of
  the original ambiguity ("what should tags on reading lists even mean"), explicitly not chosen.
  Could be a future addition (e.g. "this list spans: Horror, Sci-Fi") but is a separate, unrelated
  feature to this one.
- **Tag indicators on each sidebar row** - considered, deferred in favor of the single detail-pane
  chip row for this first version.
- **Smart-list-style filtering by reading-list tag** (Smart Lists currently operate on Issues/
  Series, not ReadingLists) - the new sidebar filter is a simple single-tag click filter, not a
  saved/compound query mechanism.

## Testing

- Entity/migration tests for `ReadingListTag`, mirroring `IssueTag`'s existing coverage shape.
- `ReadingListPropertiesScreenViewModel` Load/Save/Cancel tests covering every migrated field
  (Name/Description/Type/arc-link) plus Tags, confirming Cancel truly discards and Save writes
  everything atomically.
- Sidebar filter tests: clicking a tag narrows `Lists` to matching lists only; clearing restores
  the full set.
- Reweight-popover tests mirroring `TagPillViewModel`'s existing `SetWeightCommand` coverage.
- On-screen verification (per this project's standing UI-testing practice): open the overlay, add/
  categorize/weight a tag, Save, confirm the header chip row reflects it with correct styling,
  click it, confirm the sidebar filters correctly, right-click to reweight, confirm it persists
  without reopening the overlay.
