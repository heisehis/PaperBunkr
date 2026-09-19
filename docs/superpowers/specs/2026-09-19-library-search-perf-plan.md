# Library search performance — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-19-library-search-perf-design.md*

Work happens on branch `claude/library-search-perf` in `.claude/worktrees/library-search-perf`, because another
session has uncommitted edits in `LibraryScreenViewModel.cs` in the main checkout. Paths below are under `src/`.
Avalonia subskills consulted for this plan: `avalonia-data-binding` / `avalonia-mvvm` (collection notifications),
`avalonia-input-interaction` (scroll host), `avalonia-testing` (headless dispatcher pump).

## Constraints found while surveying (they shape the steps)

- `MainViewModel.GoToRootScreen` calls `LibraryScreenViewModel.RequestScrollIntoView` right after
  `LoadFromDatabase()`, and it reads `Covers`/`IssueList.Rows` synchronously. So the **nav-in load and
  sidebar-selection swaps stay synchronous** (`RunNow`). Search text, filter, sort/group, mode changes and
  `InvalidateSeriesProjection` are the asynchronous triggers. This narrows spec §1's "every trigger goes through
  the same path": one compute function, two scheduling modes.
- ~154 existing `LibraryScreenViewModelTests` assert synchronously after `vm.SearchQuery = ...`. They keep
  working because the VM defaults to an **inline scheduler** (compute + apply on the calling thread, no
  debounce). `MainViewModel` installs the background scheduler after constructing the VM (property, not a ctor
  parameter, so it does not collide with the tracker-auto-sync ctor change pending in the main checkout).
- `IssueListScreenViewModel` is only used by Library in production; it also renders itself on every sort/group
  change (a double render today). Library sets `AutoRender = false` and drives it.
- `SeriesCardSample.Gradient` already returns an immutable brush; only sharing per palette entry changes.
- Tests that assert the old "whole series listed when any issue matches" issue-granularity behavior are updated
  deliberately (Q16), everything else must stay green untouched.

## Step 0: Baseline measurement harness
**Files:** `Paperbunkr.App.Tests/LibrarySearchPerfHarnessTests.cs` (new)
**What:** Seeds a temp DB with 3,000 issues / 500 series (and a 10,000 / 1,500 headroom scenario), builds the VM,
and stopwatches a typing sequence (`b`, `ba`, ... `batman`, then clear). Reports through `ITestOutputHelper`:
handler time, compute time, apply time (later split via a manual scheduler). Categorised so it is skipped by
default (`[Trait("Category","Perf")]`, opt-in by filter). Run it **before** any production change to record the
"before" numbers in this file's Results section.
**Depends on:** none
**Verify:** run with `--filter Category=Perf`; numbers pasted below.

## Step 1: `BulkObservableCollection<T>`
**Files:** `Paperbunkr.App/Models/BulkObservableCollection.cs` (new), `Paperbunkr.App.Tests/BulkObservableCollectionTests.cs` (new)
**What:** `ObservableCollection<T>` subclass with `ReplaceAll(IEnumerable<T>)`: replaces contents through the inner
list and raises exactly one `Reset` (+ `Count`/`Item[]` property changes). Empty replace and same-instance replace covered.
**Depends on:** none
**Verify:** unit tests (one Reset, contents, empty, null-safe).

## Step 2: `LibrarySearchIndex` (haystack index) + normalization
**Files:** `Paperbunkr.App/Services/LibrarySearch/LibrarySearchIndex.cs` (new), `Paperbunkr.App.Tests/LibrarySearchIndexTests.cs` (new)
**What:** Built from the `Series` snapshot. Per issue per `SearchMode`: `ToUpperInvariant()` join (U+001F) of
`SearchFieldBundleCatalog.IssueFieldSelectors[mode]`. Per series: name + titles (`All`/`Series`), + publisher/genre
(`All`). API: `NormalizeQuery`, `SeriesLevelMatches(seriesId, mode, q)`, `IssueMatches(issueId, mode, q)`,
`SeriesMatches` (= series-level OR any issue). Match is `Contains(q, StringComparison.Ordinal)`.
**Depends on:** none
**Verify:** parity tests against an oracle that re-implements today's `MatchesSearch` (per-field
`Contains(OrdinalIgnoreCase)`) over a corpus incl. accented Latin, `ß`, Turkish `İ`/`ı`, KELVIN SIGN, CJK, kana, empty/null
fields, every `SearchMode`; series-granularity sets must be identical.

## Step 3: `SuggestionCandidateList`
**Files:** `Paperbunkr.App/Services/LibrarySearch/SuggestionCandidateList.cs` (new), `Paperbunkr.App.Tests/SuggestionCandidateListTests.cs` (new)
**What:** Sorted-once candidates + parallel uppercased array; `Rank(trimmedQuery, max)` = binary-search prefix range,
then early-exit substring scan; output order identical to the current `OrderByDescending(startsWith).ThenBy(alpha).Take(max)`.
**Depends on:** none
**Verify:** oracle comparison over a corpus (ordering, cap, prefix-first, non-ASCII), empty pool, query longer than any value.

## Step 4: IssueList sort/group extraction + `AutoRender` + precomputed apply
**Files:** `Paperbunkr.App/ViewModels/IssueListScreenViewModel.cs` (edit), `Paperbunkr.App/Models/IssueListSortGroupSpec.cs` (new)
**What:** `IssueListSortGroupSpec` = immutable snapshot of resolved descriptors (row sort/group, card sort/group with the
existing fall-back rules, direction, `IsGrouped`) taken on the UI thread by `CaptureSortGroupSpec()`; static
`SortRows`/`GroupRows`/`SortCards`/`GroupCards` shared by `Render()` and the pipeline (behavior identical).
`AutoRender` (default true; `Render()`/`Reload()` no-op when false). `ApplyPrecomputed(...)` replaces
`Rows`/`Groups`/`FlatRows` via `ReplaceAll` (collections retyped to `BulkObservableCollection`).
**Depends on:** Step 1
**Verify:** existing `IssueListScreenViewModelTests` unchanged and green; new tests for spec capture, `AutoRender=false`, `ApplyPrecomputed`.

## Step 5: Projection cache + view pipeline
**Files:** `Paperbunkr.App/Services/LibrarySearch/LibraryProjection.cs`, `LibraryViewPipeline.cs`, `LibraryViewResult.cs` (new),
`Paperbunkr.App/Models/SeriesCardSample.cs` (edit: shared immutable palette brushes), `Paperbunkr.App.Tests/LibraryViewPipelineTests.cs` (new)
**What:** `LibraryProjection` = immutable version: per series `SeriesCardSample` + per issue `IssueListRow` (built with
`IsSelected=false`), the `LibrarySearchIndex`, `DataVersion`; `WithSeriesRebuilt(...)` copy-on-write. `LibraryViewPipeline.Compute(inputs,
projection, ct)` = today's `RebuildView` filter/sort/group logic over the projection: content type / collection / search /
tracked / unread / missing, **per-issue search matching (Q16)**, card + row sort/group, cancellation checks between stages
and every 512 items. Output `LibraryViewResult` of plain lists (no `ObservableCollection`).
**Depends on:** Steps 2, 4
**Verify:** pipeline tests (each filter, per-issue vs series-name search, grouped/ungrouped, cancellation returns null/throws OCE, projection reuse).

## Step 6: `ILibraryViewScheduler` (inline + background)
**Files:** `Paperbunkr.App/Services/LibrarySearch/LibraryViewScheduler.cs` (new), `Paperbunkr.App.Tests/LibraryViewSchedulerTests.cs` (new)
**What:** Interface `Schedule(debounce, compute, apply)`, `RunNow(compute, apply)`, `Debounce(key, delay, action)`. `Inline`: everything
synchronous. `Background`: per-trigger `CancellationTokenSource` + generation counter, `DispatcherTimer` for the debounce,
`Task.Run` for compute, `Dispatcher.UIThread.Post(..., Background)` for apply with the generation check; exceptions other than
cancellation are logged and dropped (no crash).
**Depends on:** none
**Verify:** headless tests: newer trigger cancels older and older result never applies; zero-debounce runs at once; `RunNow` supersedes a pending job; `Debounce` coalesces.

## Step 7: Rewire `LibraryScreenViewModel`
**Files:** `Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit)
**What:**
- Split `RebuildView` into `RebuildSidebar()` (content types, collections, collection tiles, series names, `AllSeriesCount`,
  collection-view flag; only on load / sidebar selection / browse-state apply) and `RebuildView(ViewTrigger)`.
  Triggers: `Full` (inline: load, sidebar selection, browse history), `SearchText` (150 ms, 0 when query is empty/prefix-only; scroll to top),
  `SearchMode` (0 ms, scroll to top), `Filter`, `SortGroup`, `Panorama`, `Invalidate` (0 ms, background).
- Snapshot inputs on the UI thread; swap on the UI thread: generation check → diff-only selection sync → `ReplaceAll`s → property
  notifications (`HasAnyResults`, empty state) → `ScrollToTopRequested` when asked. `PlayEntranceAnimation` only for `Full`/`SortGroup`.
- `IssueList.AutoRender = false`; relay drives `RebuildView(SortGroup)`.
- `LoadFromDatabase`: new `_dataVersion`, projection reset; `KickCoverReconcile()` moved here from `RebuildView`.
- Search: `DropActiveWorkspaceLabel()` extracted from `SaveLibrarySettings`; the DB write for search text/mode goes through
  `scheduler.Debounce("search-save", 500 ms, ...)` writing only those two settings fields off the UI thread.
- `RecomputeSuggestions` uses `SuggestionCandidateList`; `SearchSuggestions` uses `ReplaceAll`.
- `InvalidateSeriesProjection(seriesId)` + the three non-reloading `SetSeries*` commands call it.
- `RatiosLearned` reflow timer invalidates the whole projection instead of only calling `RebuildView`.
- Retype `Covers`, `Groups`, `FlatCovers`, `ContentTypes`, `Collections`, `CollectionTiles`, `ExistingSeriesNames`, `SearchSuggestions` to `BulkObservableCollection`.
- `ViewScheduler` internal property, default `Inline`.
**Depends on:** Steps 1–6
**Verify:** full `LibraryScreenViewModelTests` (only Q16 expectations edited), `IssueListScreenViewModelTests`, `SeriesCardSampleTests`.

## Step 8: View + wiring
**Files:** `Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit), `Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:** Handle `ScrollToTopRequested`: reset `Offset.Y` on the poster/panorama/tiles/collection `ScrollViewer`s and `ScrollIntoView(0)`
on the List/Details `ListBox`es (whichever are visible). `MainViewModel` sets `Library.ViewScheduler = LibraryViewScheduler.CreateBackground()`
right after constructing the VM (the property, not the ctor).
**Depends on:** Step 7
**Verify:** build the app project (XAML weave, per the CLAUDE.md build-gotcha check: `dotnet build` on the App project reports 0 errors and the test project launches). On-screen check by the user.

## Step 9: New behavior tests
**Files:** `Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit, adds tests), `Paperbunkr.App.Tests/LibrarySearchPipelineViewModelTests.cs` (new)
**What:** Per-issue search (`writer:` shows only matching issues; series-name search shows all issues of that series); selection resync
(select → filter out → clear → return, and select while filtered); `InvalidateSeriesProjection` race and the three `SetSeries*` commands
update the card label without reload; suggestions output parity; search-setting persistence debounced through a manual scheduler and
immediate through the inline one; scroll-to-top raised for search and not for sort/group.
**Depends on:** Step 7
**Verify:** run them.

## Step 10: Measure again, full regression
**Files:** this file (Results section)
**What:** Re-run the Step 0 harness (3k/500 and 10k/1.5k), record handler/compute/apply times and the `RecomputeSuggestions` p95
gate; run all of `Paperbunkr.App.Tests` and `Paperbunkr.Data.Tests` (memory: the whole `App.Tests` suite has a known headless flake when
run entire; failures are re-run in isolation before being called real).
**Depends on:** Steps 0–9

## Results

### Before (Step 0, Debug build, synchronous `vm.SearchQuery = ...`, whole handler incl. DB write and rebuild)

| Scenario | first key `b` | `ba`..`batman ` (typical) | narrow result `batman b` | clear box |
|---|---|---|---|---|
| 500 series / 3,000 issues | 1,926 ms | 665–940 ms | 45 ms | 1,431 ms |
| 1,500 series / 10,000 issues | 4,525 ms | 1,684–1,847 ms | 111 ms | 3,743 ms |

The cost tracks the number of rows being projected and re-added (the narrow result is cheap), which
confirms the diagnosis: it is the rebuild, not the matching.

### After (Step 10, same machine, Debug build)

Same synchronous harness (inline scheduler, so it still includes the compute, the swap and the settings write):

| Scenario | first key `b` | `ba`..`batman ` (typical) | narrow result `batman b` | clear box |
|---|---|---|---|---|
| 500 series / 3,000 issues | 19 ms | 6.0–7.5 ms | 5 ms | 11 ms |
| 1,500 series / 10,000 issues | 25 ms | 8.6–10.7 ms | 6 ms | 24 ms |

What production pays where (manual scheduler splits the halves; max over the typing sequence):

| Scenario | UI-thread keystroke handler | UI-thread swap | worker compute (off the UI thread) |
|---|---|---|---|
| 3,000 issues | 0.1 ms typical (max 3.7 ms) | max 2.3 ms | max 14.6 ms |
| 10,000 issues | max 0.16 ms | max 7.8 ms | max 19.4 ms |

`RecomputeSuggestions` ranking (`SuggestionCandidateList.Rank`) at a 20,000-value pool: p50 0.002 ms, p95 0.07 ms
(gate: p95 < 1 ms; the first version with a per-value `Contains` loop was p95 1.6 ms and failed it, so the
substring pass now scans one concatenated buffer with vectorized `IndexOf`).

Limits of these numbers: headless (no layout/render), so the cost of Avalonia re-realizing containers after the
single `Reset` is not in them (it is one pass now instead of one per item, but it is not measured here); Debug
build. The user's on-screen check on the real library is the real acceptance test.

### Regression status (Step 10)

- All new test classes pass (`BulkObservableCollection`, `LibrarySearchIndex` parity, `SuggestionCandidateList` parity,
  `LibraryViewPipeline`, `LibraryViewScheduler`, `IssueListSortGroupSpec`, `LibrarySearchPipelineViewModel`), and every
  existing Library / IssueList / SeriesCard / MainViewModel / Home test passes except the ones below.
- One existing test was **changed deliberately**: `FilterChange_ResetsPlayEntranceAnimation` became
  `FilterChange_DoesNotPlayEntranceAnimation` (design decision Q4), plus new search/sort tests pin the policy.
- Failures that are **not from this change**, verified by running the same subsets on the main checkout (no changes of
  mine): `SmartScreenViewModelTests` (3: two Dispatcher-pump tests, and `Results_MapCoverBrushToEachIssuesOwnSeries_NotASharedBrush`
  asserting `LinearGradientBrush` while the brush has been `ImmutableLinearGradientBrush` since before), `LibraryScreenViewModelTests.DeleteConfirm_Armed_RemovesCollection_AndFallsBackWhenActive`
  (order-dependent "different thread owns it" from `Dispatcher.UIThread.RunJobs()` in the headless bootstrap; passes alone),
  `EventsScreenViewModelTests` (3), `DetailTabsViewModelTests` (1), `MatrixRainOverlayRenderTests` (2),
  `BookReaderScreenViewModelTests` (5, missing `RunJobs()` pumps; fixed only in another session's uncommitted work).
- Not verified: the running app. No UI automation was used. The on-screen check of typing feel, scroll-to-top on a new
  search, and the entrance-animation policy is still the user's.

## Deviations from the spec found while implementing

- **Nav-in / sidebar-selection / browse-history swaps stay synchronous** (`ViewTrigger.Full`), see Constraints above.
- **The scheduler's UI post and timers are injectable.** Pumping `Dispatcher.UIThread.RunJobs()` from the xunit
  thread fails Avalonia's thread-ownership check under the headless bootstrap, so the scheduler tests inject the post
  and timers instead. Production uses `Dispatcher.UIThread.Post` (Background priority) and `DispatcherTimer`.
- **Series-card selection is now synced too.** Before, `SeriesCardSample.IsSelected` was reset to false by every rebuild
  while `SeriesSelection` kept the ids, so a search cleared the checkmarks of selected series cards; the swap now
  syncs both granularities from their selection sets.
- **`PlayEntranceAnimation` is never reset to false in Library** (found while wiring the policy): once true it stays
  true, so every container prepared afterwards, including scroll recycling, arms the stagger timers and classes. Search
  and filter swaps now set it false, but ordinary scrolling before any search still runs it. That is a scroll-smoothness
  finding for the second spec, not fixed here.
- **Suggestion ranking** needed the buffer-scan optimization above to meet its gate.
