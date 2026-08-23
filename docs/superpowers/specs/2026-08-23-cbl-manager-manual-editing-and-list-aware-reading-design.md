# CBL Manager Manual Editing + List-Aware Reading

**Date:** 2026-08-23
**Status:** Approved, pending implementation plan
**Source:** User request — a manual escape hatch for auto-built (arc-lookup) reading lists, plus the
two adjacent gaps that surfaced while scoping it: reading lists had no click-to-read at all, and the
Reader's existing chapter-boundary auto-advance only understood series order, not reading-list order.

## Context

[[project_paperbunkr_cbl_manager_and_delete]] shipped CBL Manager's external story-arc lookup
(`ArcReadingListBuilder`, six source adapters) on 2026-08-22. When a lookup can't match an arc issue
to anything in the library, `ReadingListMatcher.ResolveOrCreatePlaceholder`
(`src/Paperbunkr.Data/ReadingLists/ReadingListMatcher.cs:47`) creates a placeholder `Issue`
(`IsPlaceholder = true`, `FileIsMissing = true`) so the list still has a slot for it. The Reading
Lists screen shows this as a "Missing" badge (`ReadingListItemRowViewModel.IsMissing`,
`src/Paperbunkr.App/ViewModels/ReadingListItemRowViewModel.cs:53`) but there was no way to point
that slot at a real book — only remove-and-re-add-at-the-end via the existing free-text "Add Issue"
search (`ReadingScreenViewModel.Search`/`AddIssue`, `ReadingScreenViewModel.cs:471-516`), losing the
row's position, `Role`, and `Notes` in the process. Note "Missing" is not exclusive to placeholders —
a real owned `Issue` whose file later went missing on disk shows the same badge (`IsOwned` is just
`FileIsMissing == false`); both cases get the same recovery action here.

Scoping this surfaced two more gaps, both confirmed by grep rather than assumed:

- **No click-to-read from Reading Lists at all.** Every other issue-listing screen (Library,
  Detail, Home, `IssueListScreenViewModel.OpenIssue`, `IssueListScreenViewModel.cs:239`) wires a
  `goReaderForIssue` delegate from `MainViewModel`. `ReadingScreenViewModel`'s constructor
  (`ReadingScreenViewModel.cs:40`) never received one — you can reorder and edit a reading list's
  rows, but not open one.
- **Chapter-boundary auto-advance only knows series order.** `NavigateToAdjacentIssue`
  (`ReaderScreenViewModel.cs:1719`) and its preview twin `TryGetAdjacentIssuePreview`
  (`ReaderScreenViewModel.cs:1918`) — both already shipped, driving the chapter-transition card from
  [2026-08-23-reader-chapter-transition-design.md](2026-08-23-reader-chapter-transition-design.md) —
  resolve "next issue" purely via `series.Issues.OrderByNumber()`. A reading list built from a
  crossover arc spans multiple series, so today reaching the end of a comic opened from a reading
  list would fall back to that comic's own series order, not the list's order — silently wrong
  rather than obviously broken, which is worse.

All three are part of the same underlying gap — auto-built reading lists had no manual-editing or
manual-reading story past the initial build — so they're specified together rather than as three
separate cycles.

## Scope

### 1. Manual relink of a Missing row

New `ReadingListItemLinker.Relink(PaperbunkrDbContext context, int readingListItemId, int newIssueId)`
in a new `src/Paperbunkr.Data/ReadingLists/ReadingListItemLinker.cs`, sibling to
`ArcReadingListBuilder`/`ReadingListMatcher` (data-layer, not arc-specific — placeholders also come
from CBL/CSV import, `CblReadingListIO.cs:32`/`CsvReadingListIO.cs:61`). Behavior:

- Loads the `ReadingListItem` (with its current `Issue`), repoints `IssueId` to `newIssueId` in
  place. `SortOrder`, `GroupLabel`, `Role`, `Notes` are untouched.
- If the old `Issue` `IsPlaceholder` and no other `ReadingListItem` still references it, deletes it —
  the exact cleanup rule `ArcReadingListBuilder.RefreshAsync` already uses
  (`ArcReadingListBuilder.cs:103-112`). A real issue whose file just went missing is left alone; its
  recovery is the existing missing-file/Recycle-Bin flow, unrelated to this one.
- Bumps the parent `ReadingList.UpdatedAt` and saves.

**Small adjacent fix:** `ReadingScreenViewModel.Search` (`ReadingScreenViewModel.cs:471`) queries
`context.Issues` with no `IsPlaceholder` filter, even though `ReadingListMatcher.FindExisting`'s own
doc comment (`ReadingListMatcher.cs:20`) says the manual Add search "only ever offers issues already
in the library." Adding `&& !i.IsPlaceholder` to the `Where` closes that gap — otherwise relinking
could point a Missing row at *another* placeholder, which is meaningless.

**ViewModel (`ReadingScreenViewModel`):**

- New `LinkingRow` property (`ReadingListItemRowViewModel?`). Setting it puts the screen in linking
  mode.
- New `StartLinkCommand(ReadingListItemRowViewModel row)` sets `LinkingRow = row` and clears
  `SearchQuery`/`SearchResults`. New `CancelLinkCommand` clears `LinkingRow` without mutating
  anything.
- `AddIssue(IssueSearchResult? result)` (`ReadingScreenViewModel.cs:500`) branches: if `LinkingRow`
  is set, calls `ReadingListItemLinker.Relink(context, LinkingRow.Item.Id, result.IssueId)` instead
  of appending a new `ReadingListItem`; either way it clears search state and reloads the list
  afterward, same as today.
- Two small computed properties notified off `LinkingRow` changing: a label ("Add" vs. "Link") for
  the search-result button, and a banner string ("Linking *Series* #N — pick a result below, or
  Cancel") shown above the search box only while `LinkingRow is not null`.
- `ReadingListItemRowViewModel` (`ReadingListItemRowViewModel.cs`) gets a 5th action delegate
  (`onLink`), same shape as its existing `onMoveUp`/`onMoveDown`/`onRemove`/`onFieldChanged`, backing
  a new `LinkCommand`.

**View (`ReadingScreen.axaml`):** a "Link" button next to the existing "Missing" badge (the
`IsVisible="{Binding IsMissing}"` `StackPanel` at `ReadingScreen.axaml:383`), and the banner
described above inserted just before the search `Grid` at `ReadingScreen.axaml:323`.

### 2. Click-to-read from a reading list

- `ReadingScreenViewModel`'s constructor gains `Action<int> goReaderForIssue` (inserted after
  `filePicker`, before the existing `openProperties` param).
- New `OpenIssueCommand(ReadingListItemRowViewModel row)` — no-ops unless `row.IsOwned`; Missing rows
  have the Link button instead, not an open action.
- `MainViewModel.cs:31` passes its existing `GoReaderForIssue` method
  (`MainViewModel.cs:452`) through, the same delegate every other screen already uses.
- `ReadingScreen.axaml`: the cover thumbnail + title (columns 1–2 of the row `Grid`, lines
  371-376) become a click target — wrapped in a transparent/ghost-styled `Button` bound to
  `OpenIssueCommand`, *not* the whole row. The row already hosts a `ComboBox`, a `Notes` `TextBox`,
  and Move/Remove/Link buttons; making the entire row a `Button` would nest interactive controls
  inside a button.

### 3. Reading-list-order auto-advance in the Reader

- New `_activeReadingListId` field on `ReaderScreenViewModel`, alongside the existing
  `_loadedIssueId`/`_loadedSeriesId` (all three set together in `Load(...)`,
  `ReaderScreenViewModel.cs:667`).
- `LoadIssue(int issueId, int? readingListId = null)` (`ReaderScreenViewModel.cs:593`) — existing
  single-arg call sites (Library, Detail, Home's plain continue-reading, `EnsureIssueLoaded`'s
  fallback) are unaffected and stay in plain series mode. Only a caller that explicitly passes a list
  id enters list mode.
- Two entry points pass one: the new `OpenIssueCommand` above (always the currently-open reading
  list's id), and Home's "Try This Reading List" card (`HomeScreenViewModel.OpenReadingListSpotlight`,
  `HomeScreenViewModel.cs:221`) — `ReadingListSpotlightSample.ReadingListId`
  (`ReadingListSpotlightSample.cs:16`) already exists and is simply unused for this today.
- New `MainViewModel.GoReaderForIssueInReadingList(int issueId, int readingListId)` — same body as
  the existing `GoReaderForIssue` (`MainViewModel.cs:452`), calling the two-arg `LoadIssue` overload.
  Passed into `ReadingScreenViewModel` and `HomeScreenViewModel`'s constructors alongside their
  existing delegates. The widely-used single-arg `Action<int> goReaderForIssue` stays untouched
  everywhere else — no ripple into Library/Detail/MangaDetail/IssueList.
- **Anchoring persists across boundary crossings:** once `_activeReadingListId` is set,
  `NavigateToAdjacentIssue`'s own call to `Load(...)` passes the current `_activeReadingListId` back
  through (not `null`), so the Reader stays anchored to the list through further chapter jumps until
  the user opens a different issue from a non-list entry point (which passes `readingListId: null`
  and clears it).
- **Resolution fork, deduplicated:** `NavigateToAdjacentIssue` (`ReaderScreenViewModel.cs:1719`) and
  `TryGetAdjacentIssuePreview` (`ReaderScreenViewModel.cs:1918`) currently duplicate the same
  series-index lookup. Extract one shared resolver,
  `TryResolveAdjacentIssue(bool forward, out Issue fromIssue, out Issue toIssue)`, that:
  - When `_activeReadingListId` is set: loads that list's `ReadingListItem`s ordered by `SortOrder`,
    finds the current issue's index, and walks forward/backward — skipping over Missing rows (not
    readable, same reason they have no click-to-read) — to the next real (`IsOwned`) issue.
  - Otherwise: the existing `series.Issues.OrderByNumber()` lookup, unchanged.
  - Either way, reaching the boundary (no more real issues in that direction) returns `false` — no
    auto-advance, no chapter-transition card, matching the "same as today's clamp" behavior already
    documented for series boundaries. No fallback to series order after a list's last issue.
- `NavigateToAdjacentIssue` and `TryGetAdjacentIssuePreview` both call the shared resolver instead of
  their own inline lookup; everything downstream (the chapter-transition card, `AutoNavigateComics`
  gating, the explicit Previous/Next Chapter buttons, both paged and continuous mode) is unchanged —
  it already only depends on getting a `(fromIssue, toIssue)` pair back.

## Testing

- **`ReadingListItemLinkerTests.cs`** (new, `Paperbunkr.Data.Tests`, mirroring
  `ArcReadingListBuilderTests.cs`'s style): relink repoints `IssueId` while preserving
  `SortOrder`/`Role`/`Notes`/`GroupLabel`; deletes an orphaned placeholder; does not delete a
  placeholder still referenced by another list item; does not delete a real (non-placeholder) issue
  whose file is missing.
- **`ReadingScreenViewModelTests.cs`**: `Search` excludes placeholder issues; `StartLink` +
  `AddIssue` relinks the targeted row instead of appending; `CancelLink` leaves the list unmodified;
  `OpenIssue` invokes the reader callback with the row's issue id for an owned row and does nothing
  for a Missing row.
- **`ReaderScreenViewModelTests.cs`**: `LoadIssue(issueId, readingListId)` sets list-anchored mode;
  boundary crossing while anchored walks the list's `SortOrder` (including across a series
  change and skipping a Missing item) instead of series order; reaching the list's last real issue
  stops (no card, no navigate) rather than falling back to series order; opening a different issue
  via the plain single-arg `LoadIssue` clears the anchor.
- On-screen verification (per this project's standing practice): relink a Missing row from a real
  arc-built list end to end, open a reading list issue by click, and read through a multi-series
  arc confirming chapter-transition follows list order and stops correctly at the end.

## Explicitly out of scope

Relink is offered only on Missing rows, not as a general "fix a bad auto-match" action on
already-owned rows — no request for that, and it would need a second confirmation step to avoid
accidental swaps of a working row. A Preferences toggle for list-aware navigation, or a way to view
"reading as part of list X" status anywhere in the Reader's UI beyond the existing chapter-transition
card — no request for either yet, ship the fixed behavior first per this project's established
pattern (same reasoning [2026-08-23-reader-chapter-transition-design.md](2026-08-23-reader-chapter-transition-design.md)
already used).
