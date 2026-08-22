# Library Browse History (Back/Forward)

**Date:** 2026-08-19
**Status:** Approved, pending implementation
**Related:** Next item in the "Library browsing extras" backlog sequence (`docs/alpha-roadmap.md`),
after Saved List Layouts, Manga/ContentType classification, and pluggable sort/group all shipped.

## Context

CE has a real precedent for this, not a guessed-at feature: `IBrowseHistory` (`_reference/
ComicRackCE/ComicRack/Views/IBrowseHistory.cs`) with `CanBrowsePrevious`/`CanBrowseNext`/
`BrowsePrevious`/`BrowseNext`, wired to toolbar buttons `btBrowsePrev`/`btBrowseNext`
(`ComicBrowserControl.cs`). `ComicListLibraryBrowser` implements it via a
`CursorList<IComicBookListProvider>` field (`ComicListBrowser.cs`) - every time the browser's
`BookList` (which list is currently being viewed) changes, the new value is pushed via
`history.AddAtCursor(bookList)`.

`CursorList<T>` (`cYo.Common.Collections.CursorList<T>`) is a `LinkedList<T>` with a movable cursor
and real browser back/forward semantics: `AddAtCursor` no-ops if the new value equals the current
cursor value (so navigating back and having that re-apply the same state doesn't corrupt the
stack), otherwise truncates everything after the cursor (the abandoned "forward" branch) before
appending, capped at `MaxSize` (CE default 50, oldest entries drop off the front). **Already ported
into this codebase verbatim** at `src/Paperbunkr.Common/Collections/CursorList.cs` - confirmed zero
call sites today, same "ported early, never wired into the App layer" pattern as several other
Common/Engine pieces this project has picked up along the way (embedded ComicInfo.xml reading,
`FileExplorer`, etc.). This spec is the first caller.

**Scope deviates from CE in one deliberate way**, decided with the user: CE only tracks *which list*
is selected (a Smart List, a folder, a category - Paperbunkr's equivalent is the sidebar's Content
Type/Collection filter). Paperbunkr also tracks the search query as a real back/forward step, which
CE's model doesn't support at all. Sort, group, display mode, and content granularity stay out of
scope on both sides - transient toolbar state, not "which list."

## Scope

### `LibraryBrowseState`

New record (`Paperbunkr.App.Models`):

```csharp
public sealed record LibraryBrowseState(ContentType? ActiveContentType, int? ActiveCategoryId, string SearchQuery);
```

Record equality gives `CursorList<T>.AddAtCursor`'s `object.Equals(cursorNode.Value, node.Value)`
dedup check the right behavior for free - two states with identical fields are equal, matching a
`record`'s auto-generated structural equality.

### `LibraryScreenViewModel` changes

- `private readonly CursorList<LibraryBrowseState> _browseHistory = new();` - reuses the ported
  class as-is, no changes needed to it.
- `private bool _isNavigatingHistory;` - guards `BrowsePrevious`/`BrowseNext`'s own state-apply
  from re-triggering a push (which would otherwise be harmless per `AddAtCursor`'s dedup, but the
  guard avoids scheduling the search-debounce timer pointlessly too).
- Seeded once, right after `LoadLibrarySettings()` in the constructor, with the just-loaded
  `(activeContentType, activeCategoryId, searchQuery)` - matches CE's own behavior of the very
  first `BookList` assignment already being history entry #1, so `CanBrowsePrevious` is
  meaningfully `false` until a second, different state gets pushed, not `true` from a phantom
  pre-history state.
- `SelectAllSeries`/`SelectContentType`/`SelectCollection` (the three existing commands that set
  `_activeContentType`/`_activeCategoryId`) each push a snapshot immediately after their existing
  `SaveLibrarySettings(); LoadFromDatabase();` calls - these are discrete clicks, no debounce needed,
  matching CE.
- `SearchQuery`'s existing `OnSearchQueryChanged` partial (already does
  `SaveLibrarySettings(); LoadFromDatabase();` on every keystroke, unchanged) additionally
  (re)starts an ~800ms `DispatcherTimer` that, on elapse, pushes a snapshot of the *current* state -
  guarded by `!_isNavigatingHistory` so applying a Back/Forward state doesn't re-arm it. The
  keystroke-level search-as-you-type behavior itself doesn't change at all; only the history-push
  is debounced.
- `public bool CanBrowsePrevious => _browseHistory.CanMoveCursorPrevious;` /
  `CanBrowseNext => _browseHistory.CanMoveCursorNext;` - computed, no backing field, re-evaluated
  (via `OnPropertyChanged`) after every push and every navigate.
- `BrowsePreviousCommand`/`BrowseNextCommand`: move the `CursorList`'s cursor
  (`MoveCursorPrevious`/`MoveCursorNext`), and if the resulting node is non-null, apply its
  `Value` - direct writes to `_activeContentType`/`_activeCategoryId` plus `SearchQuery = ...`
  (through the property setter, so the existing reload/save path runs unchanged), all under the
  `_isNavigatingHistory` guard, then `OnPropertyChanged` for `CanBrowsePrevious`/`CanBrowseNext`.
- **Deliberate simplification vs. CE**: CE's `BrowsePrevious`/`BrowseNext` loop past entries whose
  underlying list no longer exists (a deleted Smart List, e.g.), searching for the next valid one.
  Paperbunkr just applies whatever state it lands on - a stale `ActiveCategoryId` (its Collection
  deleted since it was pushed) already renders "zero results" gracefully today (same fallback
  `LoadLibrarySettings` uses for a stale `LibraryActiveCategoryId` at startup), not a crash. Not
  worth porting CE's skip-forward loop for what's a genuine edge case (deleting a Collection while
  it's sitting in your back-history).
- `internal void FlushSearchHistoryDebounce()` - stops the timer (if pending) and immediately runs
  its push logic. Exists purely for testability (an 800ms real-time wait in a unit test is slow and
  flaky); production code never calls it, only the timer's own `Tick` does the equivalent inline.
- Not persisted to `AppSettings` - session-only, matching CE's own in-memory-only `CursorList` and
  this project's existing boundary (sort/group/display/filter persist across restarts per the Saved
  List Layouts spec; search history/browse history don't). Already persists across navigating away
  from and back to Library *within* a session for free, since `LibraryScreenViewModel` is a
  long-lived singleton (`MainViewModel` constructs it once), not recreated per visit.

### UI

Two new toolbar buttons in `LibraryScreen.axaml`, next to the existing Filter/Sort/Group/Display
pills - plain `‹`/`›` text-glyph content (matching the exact precedent `BookReaderScreen.axaml`
already uses for its own prev/next page buttons - `Classes="chromeIcon"` there; Library's toolbar
uses its own existing pill button styling instead), `IsEnabled` bound to
`CanBrowsePrevious`/`CanBrowseNext`, `Command` bound to the two new commands, real
`AutomationId`s (`LibraryBrowsePreviousButton`/`LibraryBrowseNextButton`) for UI-test coverage.

## Explicitly out of scope

- Sort/group/display-mode/granularity as browse-history steps - transient toolbar state on both
  CE's model and this codebase's, not "which list."
- Persisting browse history across app restarts - CE never does this either.
- CE's skip-stale-entries loop in `BrowsePrevious`/`BrowseNext` - see the simplification note above.
- Extending this to Smart Lists/Reading Lists/Story Events - the backlog item this closes out is
  specifically "Library browsing extras"; those screens don't have their own `IBrowseHistory`
  equivalent in CE either (each `ComicListBrowser`-derived control gets its own independent
  history, never shared), so this would be new, separate scope if ever wanted later.

## Testing

- `LibraryScreenViewModelTests` (isolated temp SQLite, existing pattern): sidebar-click pushes a
  history entry immediately and makes `CanBrowsePrevious` true; two sidebar clicks then
  `BrowsePreviousCommand` returns to the first state; navigating back then clicking a *third*,
  different sidebar filter truncates the abandoned forward entry (`CanBrowseNext` becomes false
  again); a search-query change does *not* immediately push (`CanBrowsePrevious` still reflects the
  pre-search state) until `FlushSearchHistoryDebounce()` is called, at which point it does;
  `CanBrowsePrevious` is
  false immediately after construction (nothing to go back to yet); a state identical to the current
  one (e.g. re-selecting the already-active Content Type) doesn't push a redundant entry.
- One new `Paperbunkr.App.UiTests` case confirming the two buttons exist, start disabled, and a
  sidebar click enables Back / clicking Back returns to the prior sidebar selection on screen.
