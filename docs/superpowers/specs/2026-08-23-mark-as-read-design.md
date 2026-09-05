# Mark as Read / Mark as Unread

**Date:** 2026-08-23
**Status:** Approved, implementing
**Source:** User request mid-session, alongside the Reader chapter-transition work - "how do we
mark a read series and show that it's read?"

## Context

Paperbunkr only ever derives read/unread from actual reading progress
(`IssueMetadataExtensions.ReadPercentage`/`HasBeenRead`/`IsUnread`, driven by
`Issue.LastPageRead`/`PageCount`) - there is no manual override. The Library grid's unread badge
(`AppSettings.LibraryShowUnreadBadge`) already renders everywhere issues are shown; only the
*action* to flip that state without actually opening and paging through a book is missing.

Real CE precedent, verified against `_reference/ComicRackCE` before writing this (standing project
rule): `ComicBrowserControl.cs`'s `miMarkRead`/`miMarkUnread` context-menu commands call
`ComicBook.MarkAsRead()`/`MarkAsNotRead()`. `MarkAsRead` sets `CurrentPage = LastPageRead =
PageCount - 1` (0-indexed last page; a documented `// HACK` bumps a 1-page book to index 1 instead,
since index 0 over count 1 wouldn't read as "read"), bumps `OpenedCount` to at least 1, and sets
`OpenedTime = Now`. `MarkAsNotRead` resets `CurrentPage = LastPageRead = OpenedCount = 0` and
`OpenedTime = MinValue`.

Paperbunkr's `Issue.OpenCount`/`OpenedTime` are a deliberate CE deviation already (see
`Issue.OpenCount`'s own doc comment): real "was this actually opened" history, not a read-state
proxy the way CE's `OpenedCount` is. A manual mark/unmark action shouldn't fabricate or erase that
history - only `LastPageRead` (the sole input to `ReadPercentage`) is touched.

## Scope

**Resolver:** `IssueReadStateResolver.MarkAsRead(Issue)`/`MarkAsUnread(Issue)` in
`Paperbunkr.Data/Metadata` - pure field mutation on an already-tracked entity, caller does
`SaveChanges`. `MarkAsRead` sets `LastPageRead = PageCount - 1` (or `1` for a 1-page issue, porting
CE's own hack unchanged); no-ops when `PageCount` isn't known yet (unscanned/fileless - there's no
real "last page," and `ReadPercentage` already returns 0 unconditionally in that case regardless of
what `LastPageRead` holds). `MarkAsUnread` sets `LastPageRead = 0`.

**UI surfaces** - three places issues/chapters are already shown with a context menu, single-item
scoped everywhere except one:

- **Library grid** (`LibraryScreenViewModel`) - new `MarkIssueRead`/`MarkIssueUnread` commands
  (`int issueId`, same shape as `RevealIssueCommand`), added to all 7 per-view-mode issue-tile
  context menus in `LibraryScreen.axaml` (identical duplicated block across
  CompactGrid/ComfortableGrid/CoverOnlyGrid/PanoramaGrid/List/Tiles/IssueList templates - confirmed
  byte-identical before a single `replace_all` edit). Single-item only: Library has no
  multi-selection model at all today (confirmed by grep - no `IsSelected`/`SelectedCount`/
  `ContextMenu`-driven-by-selection anywhere in `LibraryScreenViewModel`), so a CE-style bulk
  "Mark Selected Read" has no selection to operate over. Building that selection model is out of
  scope here - a separate feature, not a natural side-effect of porting this one action.
- **DetailTabs Issues tab** (`DetailTabsViewModel`, the Western-comic per-issue tile grid) - new
  `MarkIssueRead`/`MarkIssueUnread` commands (`IssueCardSample`), using the *existing*
  `SelectedIssueIds` selection-union pattern `EditIssueProperties`/`RevealIssue` already use
  (right-clicked tile unioned with the current selection) - real bulk support, for free, since this
  screen already has the selection model Library doesn't.
- **Manga Detail Chapters tab** (`MangaDetailScreenViewModel`) - new
  `MarkChapterRead`/`MarkChapterUnread` commands (`ChapterRowSample?`), single-item only (this
  screen has no selection model on its chapter rows either).

No series-level "mark whole series as read" - CE's own action is per-book, invoked over a
selection; Paperbunkr has no natural "series" analog beyond marking every issue, which none of the
three surfaces above currently support selecting anyway.

## Testing

- `IssueReadStateResolverTests`: full-count issue reaches `HasBeenRead`, 1-page issue reaches
  `HasBeenRead` via the hack value, unknown-`PageCount` issue no-ops, `MarkAsUnread` reaches
  `IsUnread` from any prior state.
- `LibraryScreenViewModelTests`/`DetailTabsViewModelTests`/`MangaDetailScreenViewModelTests`:
  each new command updates the database and (for Library/DetailTabs) refreshes the visible
  collection; DetailTabs' bulk case (selection-union) marks every selected issue, not just the
  right-clicked one.

## Explicitly out of scope

Library multi-selection (needed for real bulk support there - separate future feature). Series-
level "mark all issues read." `OpenCount`/`OpenedTime` changes on mark/unmark (deliberately
untouched, see Context). Novels/Books (`Book` entity) - not requested, no precedent checked yet.
