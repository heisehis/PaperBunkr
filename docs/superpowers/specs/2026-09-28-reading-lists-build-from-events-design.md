# Reading lists: build from events (pitch spec B)

Spec B of the Reading List / CBL Manager pitch (Roadmap, "Reading List / CBL Manager pitch", 2026-09-28). It covers
#10 (story-order lists from a Continuity), #12 (check an event-linked list against ComicVine/Metron), and a grouping bug on
the list screen that #10 would make worse. Spec A (`2026-09-28-reading-lists-organize-and-track-design.md`) is built first.
Decisions come from the same 2026-09-28 grilling session.

## Facts this rests on (verified 2026-09-28)

- **CE** has no auto-building of reading lists from arcs, chronology or event order. `StoryArc` is only a matcher, a sort and a
  grouping. CBLManager builds lists from external arcs and never from local data.
- **#10 is half built.** `ContinuityReadingListBuilder.CreateFromContinuity`
  (`Paperbunkr.Data/ReadingLists/ContinuityReadingListBuilder.cs`) makes a one-shot `PublicationOrder` list. It interleaves
  every member-series issue by cover date and sets `GroupLabel = series name`. It's reached from the Continuity hero's
  "Create reading list (publication order)" (`ContinuityHero.axaml:80` → `ContinuityPageViewModel.CreateReadingList`). The
  list keeps no link to the continuity.
- **The continuity map's order already exists.**
  - `ContinuityMapLoader.LoadData(ctx, continuityId, gcd)` → `ContinuityMapBuilder.Build(data, ContinuityMapOptions)` gives
    `ContinuityMapSource.Rows` in reading order, split into `Blocks`.
  - Each block is an `EventMapBlock(Index, Kind, Label, EventId, FirstRow, LastRow)`. `Kind` is Event (one event's issues,
    in its own `Position` order) or the between-events / year blocks (issues in no event, by date; GCD on-sale dates when
    installed).
  - Events are ordered by `EventChronology.Order` (smart-connector relations, then date).
  - All of this lives in `Paperbunkr.App/Services/EventMap/`.
  - An issue in two events appears in both events' blocks.
- **Event-to-list links.**
  - `ReadingList.StoryEventId` links a list to an event one way.
  - `NewReadingListViewModel.CreateFromEvent` copies members once, and nothing re-syncs afterwards.
  - `StoryEvent` carries `ComicVineArcId` / `MetronArcId`, filled by `StoryEventIdCompletion`.
  - `IReadingListSource.GetArcIssuesInOrderAsync(arcId)` returns the provider's order for the ComicVine and Metron sources.
  - `ReadingListMatcher.ResolveOrCreatePlaceholder` resolves or creates placeholder issues.
- **Arc Refresh** (`ArcReadingListBuilder.RefreshAsync`):
  - It reorders to the source and adds new items.
  - It removes items the source no longer has, deleting orphaned placeholders.
  - It never touches `Role`, `Notes` or `GroupLabel`.
  - It announces one compound change.
- **Grouping bug.** `ReadingScreenViewModel.LoadReadingList` (`:643`) runs `items.GroupBy(i => i.GroupLabel ?? "")`. That
  merges runs that aren't next to each other but share a label into one group, and then renumbers `Position` in group order.
  A continuity list is interleaved by date with series-name labels, so it shows as series blocks and its numbers stop
  matching the real `SortOrder`.

## Decisions

| # | Decision |
|---|---|
| Q4 | Fix the grouping: group consecutive runs only. A label that comes back later starts a new group with the same header. |
| Q17 | "Story order" follows the Continuity map read left to right: events in smart-connector order, each event's issues in its own order, and between-event issues by date. Group labels are the block labels (event names, or the map's between/year labels). It reuses `ContinuityMapBuilder`, so the list and the map always agree. |
| Q18 | Keep the date-only builder as the second choice. Building a list offers **Story order** (default) or **Publication order**. |
| Q19 | Continuity lists stay linked. New `ReadingList.ContinuityId` and a **Rebuild from continuity** action, following arc Refresh's reconciliation rules. |
| Q28 | Rebuild is manual only. There's no auto-follow. |
| Q20 | **Check against ComicVine/Metron** on event-linked lists whose event has an arc id. It shows a diff: missing issues and an out-of-order count. Actions: **Insert missing** (at the provider's positions; placeholders for issues you don't own) and an optional **Reorder to match**. It never removes anything. |
| Q29 | The check and the rebuild run as Activity Center jobs. Results show inline in the list header area. |

## Design

### 1. Grouping fix

In `LoadReadingList`, replace the `GroupBy` with a walk over `items` (already in `SortOrder`). It starts a new
`ReadingListGroupViewModel` whenever `GroupLabel ?? ""` differs from the previous item's. `Position` numbering stays "1..n over
all rows", which now equals the `SortOrder` order. Extract it as a pure `ReadingListGrouping.Runs(items)` (App) so it can be
unit-tested. The checklist PDF (spec A §6) uses the same helper for its group rows.

A visible consequence: a publication-order continuity list now shows a header for every series change. That's accurate for an
interleaved list, but noisy. The publication-order builder therefore stops writing `GroupLabel` (it was series names, which
the row already shows). Existing lists keep their labels; the user can clear them.

### 2. Story-order builder (#10)

**Layering.** The order is computed in App, where `ContinuityMapBuilder` lives. The list is written in Data.

- New `ContinuityStoryOrder.Compute(ContinuityMapData data)` (`Paperbunkr.App/Services/EventMap/ContinuityStoryOrder.cs`):
  - Builds with `ContinuityMapOptions.Default` (no hidden events, optional issues included, not events-only).
  - Returns `IReadOnlyList<(int IssueId, string GroupLabel)>`, walking `Blocks` in order and each block's rows
    `FirstRow..LastRow`, labelled with `block.Label`.
  - An issue already emitted is skipped: an issue in two events stays at its first appearance, since a reading list reads each
    issue once.
- `ContinuityReadingListBuilder` gains:
  - `CreateFromOrder(ctx, continuityId, order, ReadingListType type, int? folderId)`: `Type = Chronological` for story order;
    name "`<continuity> (story order)`"; sets `ContinuityId`; items in order with labels; announces through
    `ReadingListManager.RecordCreatedWithItems`.
  - `RebuildFromOrder(ctx, listId, order)`: the reconciliation below.
  - `CreateFromContinuity` stays as the publication-order path, now also setting `ContinuityId`, dropping labels (§1) and
    taking `folderId`. Its rebuild computes the same date order in Data and calls `RebuildFromOrder`.

**Data.**
- `ReadingList.ContinuityId` (int?, FK `SetNull`), plus `ContinuityOrderKind` (an enum `StoryOrder` | `PublicationOrder`,
  nullable) so Rebuild knows which order to recompute.
- Migration `AddReadingListContinuityLink`. `Down()` drops nothing (convention).

**Rebuild reconciliation** (follows arc Refresh, Q19):
- Items in the new order are placed at their new `SortOrder`.
- New issues are added.
- Items whose issue is no longer in the order are removed, and orphaned placeholders are deleted (the same helper arc Refresh
  uses: extract it from `ArcReadingListBuilder` into a shared internal `ReadingListReconciler`).
- `Notes` and `Role` on kept items are never touched.
- `GroupLabel` **is** rewritten, because it's generated from the map's block labels, not written by the user.
- Duplicate issue rows are cleaned up first, as in arc Refresh.
- Everything is announced as one compound `ReadingListManager.Record(..., added, removed)`.
- The result is `ContinuityRebuildResult(Added, Removed, Moved)`.

**UI.**
- The Continuity hero menu item becomes "Create reading list…". It opens a small flyout with two radio choices, **Story order
  — as on the map** (default) and **Publication order — by release date**, and a Create button.
- The story-order path loads `ContinuityMapLoader.LoadData(ctx, id, GcdDataStore.TryOpen())`, then runs Compute, then
  `CreateFromOrder`.
- It notifies as today and navigates to the list. The list goes into spec A's `SelectedFolderId` if the Reading screen has
  one, otherwise the top level.
- On a continuity-linked list, the Reading screen's Manage menu gains **Rebuild from continuity**, visible when
  `ContinuityId != null`. It runs under `IActivityService.StartJob(ActivityJobKind.Other, "Rebuilding <name>")`.
- The result shows as a dismissible inline note under the header: "Rebuilt · 12 added · 3 removed · 40 moved".
- The list header's meta line shows "from *<continuity>* · story order", with the continuity name linking to its page.

### 3. Check against ComicVine/Metron (#12)

**Eligibility.** The list's `StoryEventId` points to an event with `ComicVineArcId` or `MetronArcId`, **and** that source is
usable (`ReadingListSourceRegistry.Get` returns non-null, meaning credentials are set). When both qualify, **ComicVine is
used**; the diff panel's header names the source used and offers "Use Metron instead". Otherwise the Manage item is hidden.

**Engine.** New `ReadingListCanonicalDiff` (`Paperbunkr.Data/ReadingLists/`):
- `ComputeAsync(ctx, listId, IReadingListSource, arcId, ct)` → `CanonicalDiff(Source, Missing, OutOfOrderCount, Unmatched)`:
  - It resolves each provider `ArcIssue` to a local issue with `ReadingListMatcher.FindExisting` (match only), so no
    placeholder is created while diffing.
  - `Missing` lists the provider entries not in the list, each with the provider position and its resolved issue if you own
    it.
  - `OutOfOrderCount` counts the list items the provider has that aren't in provider order: list length minus the longest
    increasing subsequence of their provider positions.
- `InsertMissing(ctx, listId, diff)`:
  - For each missing entry, in provider order, resolve or create a placeholder, then insert it right after the list item
    holding the nearest earlier provider position (at the top when there is none).
  - Renumber once, and announce one compound change.
- `ReorderToMatch(ctx, listId, diff)`:
  - Stably sorts the items the provider has into provider order, **within the slots those items already occupy**.
  - Items the provider doesn't have stay in their slots.
  - Nothing is added or removed.
- Both writes go through `ReadingListManager`.

**UI.**
- The Manage item is **Check against ComicVine** (or Metron). It runs as an Activity Center job, "Checking <name> against
  ComicVine".
- The result opens an inline panel under the header, the same slot as spec A's overlap banner, stacked below it if both
  apply.
- If there are differences, the panel reads "ComicVine lists **5 issues** this list doesn't have · **8** are out of order",
  with the actions **Insert missing (5)**, **Reorder to match** and **Show details**.
- Show details expands to the missing entries as "`#pos` Series #Number (Year) · owned / not in library".
- If there are none, the panel reads "Matches ComicVine's order" and auto-dismisses after a few seconds.
- After Insert or Reorder, the list reloads and the panel re-computes from the cached provider result, with no second fetch.

## Error handling

- A provider failure (`ReadingListSourceException`, network, rate limit) fails the job with the source's message; the list is
  untouched.
- A continuity deleted since the list was made: `ContinuityId` becomes null (`SetNull`) and Rebuild is hidden. A list whose
  continuity is gone never errors.
- A rebuild of a continuity with no events and no loose issues is refused with "This continuity has no issues to build from";
  it never empties the list.
- A missing GCD extract only changes date precision: `TryOpen()` returns null and the map falls back to cover dates.

## Testing

- **App:**
  - `ReadingListGrouping.Runs`: repeated labels become separate runs, null and empty labels, numbering.
  - `ContinuityStoryOrder.Compute` on a hand-built `ContinuityMapData`: block order, an issue in two events kept first,
    labels.
  - The hero flyout choice passes the right order kind.
- **Data:**
  - `CreateFromOrder` / `RebuildFromOrder`: add, remove, move, notes and roles kept, labels rewritten, orphaned placeholders
    deleted, one announcement.
  - The shared `ReadingListReconciler` still passes the existing `ArcReadingListBuilder` tests unchanged.
  - The publication-order rebuild.
  - `ReadingListCanonicalDiff` against a fake `IReadingListSource`: missing positions, the LIS out-of-order count, insert
    positions, placeholder creation only on insert, reorder that leaves foreign items in their slots, ComicVine preferred over
    Metron.
- Migration test for `AddReadingListContinuityLink`.
- A headless render of the diff panel and the rebuilt-note, then `avalonia-pro-max/review-checklist`.

## Out of scope

- Auto-rebuild or "follow" for continuity lists (Q28).
- Re-syncing a Story Event's own member order from providers. This changes only the list.
- Removing items the provider doesn't list (Q20 is insert and reorder only).
- GCD as an order source (spec A, Q2).

## Defaults filled in while writing (flag in review)

- When both ComicVine and Metron ids are present, **ComicVine** is used, with a "Use Metron instead" switch.
- Rebuild **removes** issues that left the continuity, including ones you added to the list by hand, exactly as arc Refresh
  does.
- The publication-order builder **stops writing series-name group labels**.
- An issue in two events is kept at its **first** appearance in story order.

## Implementation notes (2026-09-28)

Built together with spec A. It is **uncommitted**, and nobody has seen it in the running app yet.

Where the build differs from the design:

- **The two order choices are a submenu,** "Create reading list ▸ Story order — as on the map / Publication order — by release
  date", instead of a flyout with radio buttons. It's the same choice with one fewer click. A continuity-page failure (an empty
  continuity) is reported through `_nav.Notify`.
- **`ReadingListReconciler`** was extracted from `ArcReadingListBuilder.RefreshAsync`. Arc Refresh now resolves every provider entry
  first, then reconciles, then runs role detection over the reconciled items. The existing `ArcReadingListBuilderTests` pass
  unchanged.
- **Story order** is `Services/EventMap/ContinuityStoryOrder` (App); `ContinuityListOrders.Compute` picks either order for Create
  and Rebuild. With no events, the map is in publication mode, so a story-order list gets the map's year blocks as its labels.
- **One migration** carries `ContinuityId` / `ContinuityOrderKind`: `AddReadingListFolders` (see spec A's notes).
- **The canonical diff** resolves provider entries with `ReadingListMatcher.FindExisting` (read-only). Placeholders are created only
  by Insert missing. After Insert or Reorder, the panel recomputes from the provider order already fetched.

**Tests:** Data `ReadingListBuildFromEventsTests`. App `ReadingScreenPitchTests` (Rebuild, canonical check, story order) and the
updated `ContinuityPageViewModelTests` (both orders).
