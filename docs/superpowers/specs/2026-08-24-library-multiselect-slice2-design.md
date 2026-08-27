# Library Multi-Selection, Slice 2: Mark Read/Unread + Add to Reading List

**Date:** 2026-08-24
**Status:** Approved, pending implementation

## Context

Follow-on to Slice 1 (docs/superpowers/specs/2026-08-24-library-multiselect-slice1-design.md), which
shipped per-issue selection + bulk edit/delete. This slice adds the two remaining issue-level
actions from the original scope: bulk mark read/unread, and bulk add-to-reading-list.

**Research findings before designing** (per this project's standing CE-verification rule):

- Mark read/unread: `LibraryScreenViewModel.MarkIssueRead`/`MarkIssueUnread` are untouched by Slice
  1, still single-`int issueId`. `IssueReadStateResolver.MarkAsRead`/`MarkAsUnread` (static,
  `Paperbunkr.Data.Metadata`) each take one `Issue`, no batch overload — bulk just loops. Trivial
  extension, same shape as Slice 1's `DeleteIssue` → `DeleteIssues` refactor.
- Add to reading list: **no existing single-item version anywhere in the app** to wire into. The
  only "add issue to list" code is `ReadingScreenViewModel.CreateNew`/`AddIssue`
  (`src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs:539,586-594`), both scoped to an
  already-open list (no "pick a list" step) and both inlined directly in that ViewModel, not a
  reusable service. **ComicRackCE has no menu-driven equivalent either** — checked
  `_reference/ComicRackCE/ComicRack/Views/ComicListLibraryBrowser.cs`, its only mechanism is
  drag-and-drop onto a reading-list tree node (`IEditableComicBookListProvider`, lines 1001-1016).
  This slice is the first place a reading-list *picker* exists in this app.

## What changes

### 1. Bulk mark read/unread

`MarkIssueRead(int issueId)`/`MarkIssueUnread(int issueId)` refactored the same way Slice 1
refactored `DeleteIssue`: each becomes a thin wrapper over a shared `MarkIssuesRead`/
`MarkIssuesUnread(IReadOnlyList<int> issueIds)` that resolves `Selection.UnionForAction(issueId)`,
loops `IssueReadStateResolver.MarkAsRead/Unread(issue)` per id in one context, one `SaveChanges()`,
then `LoadFromDatabase()`. Two new action-bar commands (`MarkSelectionReadCommand`/
`MarkSelectionUnreadCommand`) call the same shared method with `Selection.SelectedIds.ToList()`,
mirroring `DeleteSelectionCommand`'s relationship to `DeleteIssueCommand` exactly. The existing
context-menu "Mark as Read"/"Mark as Unread" items become selection-union-aware for free (same
`CommandParameter="{Binding Id}"`, no XAML change needed — only the ViewModel-side method changes,
same as `DeleteIssueCommand` in Slice 1).

Toast after every bulk action (2+ issues): "Marked N issues as read" / "...as unread". Single-issue
actions (the pre-existing per-tile case) stay silent, matching this app's existing convention of not
toasting single, immediately-visible actions.

### 2. Add to reading list

**Action bar button** "Add to List" (visible whenever `HasSelection`) opens a `MenuFlyout` listing
every existing `ReadingList` by `Name` (ordered by `SortOrder`, matching `ReadingScreenViewModel`'s
own list ordering), plus a leading "New Reading List…" item. No new overlay/modal — this is the
cheapest UI that covers "pick an existing list" and doesn't block on building a picker dialog.

- Clicking an existing list: adds the whole current selection to it (see write logic below), closes
  the flyout, shows a toast.
- Clicking "New Reading List…": creates a list the same way `ReadingScreenViewModel.CreateNew` does
  (`Name = "New Reading List"`, `SortOrder = context.ReadingLists.Count()`, `Type =
  ReadingListType.User`, `CreatedAt`/`UpdatedAt = DateTime.UtcNow`), then immediately adds the
  selection to it. The user can rename it later from the Reading screen — no inline rename UI here,
  keeping this action a single click.

**Write logic** (`AddIssuesToReadingList(int readingListId, IReadOnlyList<int> issueIds)`):
for each issue id, skip if a `ReadingListItem` with that `(ReadingListId, IssueId)` pair already
exists (the approved duplicate guard — `ReadingListItem` has no DB-level uniqueness constraint, so
this is an application-level check); otherwise insert
`new ReadingListItem { ReadingListId, IssueId, SortOrder = nextOrder++ }`, matching
`ReadingScreenViewModel.AddIssue`'s exact field shape (`GroupLabel`/`Role`/`Notes` left null/default,
same as that precedent). One `SaveChanges()` for the whole batch.

**Toast**: "Added N to \"{list name}\"" if everything was added; "Added N to \"{list name}\" (M
already in list)" when the duplicate guard skipped some; "All N already in \"{list name}\"" if
everything was skipped (no-op write, still worth telling the user why nothing visibly happened).

Right-click per-tile: **not extended to add-to-list in this pass** — the flyout only appears in the
action bar, not the tile context menu. A per-tile "Add to Reading List ▸ {list names}" submenu is a
reasonable follow-on but adds a submenu-per-tile cost (rebuilt per right-click) for a feature the
selection action bar already covers; deferred rather than building both entry points at once.

### 3. Toast plumbing into LibraryScreenViewModel

`LibraryScreenViewModel`'s constructor gains a `showToast: Action<string, string>?` parameter
(optional, no-op default — same pattern as `onQuickRate`/`goIssueProperties`), wired from
`MainViewModel`'s existing `ShowToast` the same way `PreferencesScreenViewModel`/
`IssuePropertiesScreenViewModel` already receive it.

## Explicitly not changing

- Slice 1's selection mechanism, bulk edit, bulk delete — untouched.
- Series-card selection, Comic-List-specific concerns — still Slice 3/deferred, unaffected by this
  slice (mark-read/add-to-list here are issue-level only, same scope boundary as Slice 1).
- No reading-list rename/multi-list-at-once UI — creating via "New Reading List…" reuses the
  existing Reading screen for any further list management.

## Testing

- `LibraryScreenViewModelTests`: bulk mark read/unread (selection union, action-bar path, toast
  content), add-to-reading-list (new-list creation + add, existing-list add, duplicate-skip
  counting, toast wording for the three cases above).
