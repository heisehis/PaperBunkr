# Library search performance

Status: draft for review, 2026-09-19. First of two specs; the scroll-smoothness spec follows separately.

## Problem

Typing in the Library search box is laggy. The user's library is ~3000 comics in ~500 series, so the
lag is not a scale problem: matching 3000 issues costs microseconds. The cost is the rebuild that
runs on the UI thread for every keystroke.

Every `SearchQuery` change runs `OnSearchQueryChanged` → `RebuildView`
([LibraryScreenViewModel.cs](../../../src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs) lines 1518 and 1034):

1. `SaveLibrarySettings` opens a DbContext and calls `SaveChanges` synchronously (one SQLite write
   per keystroke).
2. `RebuildView` rebuilds sidebar summaries (`ContentTypes`, `Collections`, `ExistingSeriesNames`,
   `CollectionTiles`), none of which depend on the search text.
3. It projects **both** granularities on every call: `SeriesCardSample.FromSeries` per matching
   series and `IssueListRow.FromIssue` per matching issue (~90 fields each, a `File.Exists` per
   issue for `HasCustomCover`, and a fresh temporary `LinearGradientBrush` + stops + `.ToImmutable()`
   per row from `CoverBrushFor`; the result is already an immutable brush, so this is allocation cost only).
4. It sorts and groups both, then does `Clear()` plus one `Add()` per item on four
   `ObservableCollection`s (`Covers`, `FlatCovers`, `Rows`, `FlatRows`). `VirtualizingWrapPanel.OnItemsChanged`
   calls `DerealizeAll()` and `InvalidateMeasure()` for **every** notification, so N adds means N
   full re-realize passes.
5. It sets `PlayEntranceAnimation = true` (staggered fade-in on every keystroke) and calls
   `KickCoverReconcile`.
6. `RecomputeSuggestions` runs on the same path (cheap, in-memory).

CE for comparison (`_reference/ComicRackCE`, `ComicBrowserControl.cs`): 500 ms debounce timer, then a
full UI-thread rebuild. CE is not fast because of clever search; it is fast because its rebuild is
cheap. Paperbunkr's rebuild is much heavier and has no debounce.

## Goals

- Typing never blocks. The text box stays responsive at any library size.
- Results update ~150 ms after the last keystroke, in one visual swap, with no stagger animation.
- Matching semantics stay CE-faithful: case-insensitive **substring** (`IndexOf(OrdinalIgnoreCase)`) over
  the same per-mode field bundles. No token index.
- Headroom: correct and fast at 10k issues / 1.5k series, not only at the current size.

## Non-goals

- Scroll smoothness (separate spec).
- The synchronous `LoadFromDatabase` on navigation into Library (separate cost, separate work).
- Other screens' search boxes (Books, Reading, Events, Preferences).
- A persisted search index. The index lives in memory and is rebuilt on every data load.

## Decisions taken with the user

| Topic | Decision |
|---|---|
| Debounce | 150 ms, background compute, one UI swap |
| Entrance animation | Suppressed for search and filter rebuilds. Kept for nav-in, view-mode change, sort/group |
| Index | Haystack index, substring parity. No token/inverted index |
| Matching granularity | Issue granularity matches per issue (CE parity). This **fixes** current behavior, see below |
| Measurement | Measure first, stopwatch harness, before and after |
| Clearing the box | Empty/whitespace query skips the debounce, instant update |
| Scroll after a swap | Search-text change scrolls to top. Sort/group/filter keep the offset (clamped to the new extent) |
| Cancellation | In-flight compute is cancelled by a new trigger (token) and also generation-guarded |

## Design

### 1. Pipeline

```
keystroke
  ├─ immediately: prefix parse / SearchMode, chip + empty state, RecomputeSuggestions
  └─ debounce 150 ms
        └─ snapshot inputs (query, mode, filters, sort, group, content type, collection members)
              └─ background: match → filter → sort → group → LibraryViewResult (immutable)
                    └─ UI thread: single swap, guarded by a generation token
```

- Each trigger owns a `CancellationTokenSource`. A new trigger cancels the previous source (so an
  abandoned job stops burning a thread-pool thread; the job checks the token between match, sort, and
  group stages and every ~512 items inside the match loop) **and** bumps a generation counter. A job that
  finishes anyway with a stale generation is dropped at the swap. Cancellation is an optimization; the
  generation check is the correctness guard.
- Non-search triggers (sort/group/filter changes, sidebar selection of a content type or collection,
  nav-in, `InvalidateSeriesProjection`) go through the same compute path but with **zero** debounce, so one
  code path builds the view. Every one of them cancels the previous token and bumps the generation, so a
  stale in-flight job can never overwrite a newer trigger's result. Toggling a row's selection checkbox is
  **not** a trigger: `Selection.Toggle` still flips `IsSelected` in place on the displayed rows, as today.
- Clearing the box (empty or whitespace query) also skips the 150 ms debounce, so backspacing to empty
  updates instantly. The same holds for a `mode:` prefix with no text after it (already "mode-only, no text filter").
- `SaveLibrarySettings` for the search text is debounced separately (~500 ms) and its DB write runs off
  the UI thread. The persisted search text keeps surviving restarts, as today. Other settings writes
  are unchanged.

**Suggestions stay synchronous, but bounded.** `RecomputeSuggestions` runs on the UI thread on every
keystroke, before the debounce. Today it scans the whole distinct-value pool for the scoped mode with
`Contains(OrdinalIgnoreCase)`, then sorts every match. That is cheap at 3k issues and grows linearly with the
pool. New rule: it must stay sub-millisecond at 10k issues by construction.

- `_suggestionIndex` per mode becomes a candidate list sorted once (ordinal order of the normalized text)
  with a parallel array of `ToUpperInvariant()` strings, built off-thread with the search index.
- The query is normalized once. Pass 1 finds the prefix-match range by binary search and takes up to
  `MaxValueSuggestions`. Pass 2, only if the cap is not yet reached, scans the sorted list for substring
  matches and **exits as soon as the cap is filled**. Output order is identical to today's
  (prefix matches first, then the rest, each alphabetical by `OrdinalIgnoreCase`).
- Recent searches, field hints, and saved-search suggestions are already tiny and capped; unchanged.
- The `SearchSuggestions` list is replaced with `ReplaceAll` (one notification) instead of `Clear()` plus adds.
- A measurement gate in the harness fails the plan step if p95 of one `RecomputeSuggestions` call exceeds
  1 ms at the 10k / 1.5k scenario.

### 2. `LibrarySearchIndex` (haystack index)

Built once per `LoadFromDatabase`, from the same in-memory snapshot, on a background thread.

- Per issue, per `SearchMode`: one pre-normalized joined string built from
  `SearchFieldBundleCatalog.IssueFieldSelectors[mode]` (the single shared field definition in
  [SearchFieldBundleCatalog.cs](../../../src/Paperbunkr.Data/SearchFieldBundleCatalog.cs)). Fields are
  joined with a separator that cannot occur in user text (U+001F), so a query cannot match across a
  field boundary. This preserves today's per-field `Contains` semantics.
- Per series: one string for series-level fields. Mode `All` and `Series`: `Name` and every `Titles`
  value. Mode `All` adds `Publisher` and `Genre`. Other modes have no series-level fields.
- **Normalization.** Haystack strings are normalized once at build time with `ToUpperInvariant()`; the
  query is normalized once per trigger the same way; the scan is `haystack.Contains(query, StringComparison.Ordinal)`.
  That removes per-scan case folding. Upper, not lower, on purpose: .NET defines `OrdinalIgnoreCase` by
  invariant *upper*-casing, and lower-casing diverges from it on a few characters (for example U+212A
  KELVIN SIGN lower-cases to `k` but does not equal `k` under `OrdinalIgnoreCase`). The parity test corpus
  therefore includes non-ASCII titles (accented Latin, `ß`, Turkish `İ`/`ı`, KELVIN SIGN, CJK, kana).
- One scan over ~3000 short strings per query is well under a millisecond; at 10k issues still a few
  milliseconds. The haystack build (once per load, off-thread) reuses one `StringBuilder`; no further
  allocation tuning, it is not on any hot path.
- No incremental maintenance. Every data mutation already triggers a full `LoadFromDatabase`, which
  rebuilds the index. Nothing else mutates issues.
- `SearchQuery` prefix syntax (`writer:miller`) and the "prefix with nothing after it means mode-only,
  no text filter" rule are unchanged; the effective text comes from the existing `ParseFieldPrefix`.

### 3. Per-issue matching (fix, CE parity)

Today, in issue granularity, a series matches if any issue matches, then **all** issues of that series
are listed (`filtered.SelectMany(s => s.Issues)` in `RebuildView`). `writer:miller` shows every issue of
any series that has one Miller issue. CE matches per book.

New rule for issue granularity:

- An issue matches if the **series-level** text for its series matches (mode `All`/`Series`: series
  name, titles; `All` also publisher, genre) **or** its own per-mode bundle matches.
- So searching a series name still lists every issue of that series (each book of the series matches on
  the series field, as in CE), while `writer:miller` lists only the Miller issues.

Series granularity is unchanged: a series card matches if the series-level text matches or any of its
issues match.

Consequence for tests: `SearchFieldBundleCatalogParityTests` and the search tests in
`LibraryScreenViewModelTests` stay valid for series granularity. Issue-granularity expectations that
encoded the old "whole series" behavior are updated deliberately, and a new test pins the per-issue rule.

### 4. Projection cache

`IssueListRow` and `SeriesCardSample` are built once per data load (id-keyed dictionaries) and reused by
every search, sort, group, and filter. They are rebuilt only when:

- `LoadFromDatabase` reloads data;
- virtual tag definitions change (`SetVirtualTags`);
- `CoverAspectRatioStore.RatiosLearned` fires for Panorama (`PanoramaWidth` depends on it); the existing
  600 ms reflow timer invalidates the cache instead of just calling `RebuildView`.

**The cache is an immutable snapshot.** The background job reads one cache version and never sees it
change under it. `InvalidateSeriesProjection` and a data reload do not mutate the shared dictionaries; they
build a new version (copy-on-write for the affected series, full rebuild for a reload), publish it, and then
run the trigger described in §1 (cancel, bump generation, zero-debounce swap). A job still holding the old
version finishes into a stale generation and is dropped. No lock is needed on the read path.

**Selection state.** `IssueListRow.IsSelected` and `SeriesCardSample.IsSelected` must be correct for every row
in a result, and cached rows can be stale: `Selection.Clear`/`Toggle` only update the rows currently
displayed, so a row that was filtered out while selected (or while the selection was cleared) still carries an
old flag when a later search brings it back. So the swap syncs selection from the authoritative selection set.
This is a **UI-thread** step, because cached rows are `ObservableObject`s and writing `IsSelected` raises
`PropertyChanged` for bound checkboxes; the background job never writes to a shared row. It is diff-only
(`if (row.IsSelected != selected) row.IsSelected = selected`), so on the common path it writes nothing.

**In-place mutations (checked against the code).** Full reload is the normal mutation path: every
command that changes what a row or card shows (`SetSeriesContentType`, reading-list adds, bulk edits,
deletes, scans) already ends in `LoadFromDatabase()`, and navigating into Library reloads too, so edits made
on Detail, the Issue editor, or the Reader are picked up on return. Three commands do **not** reload:
`SetSeriesStatus`, `SetSeriesReadingStatus`, `SetSeriesReadingMode` (they write the DB and leave the
`_allSeries` snapshot stale until the next reload, which is already true today). The spec adds an
item-level hook, `InvalidateSeriesProjection(int seriesId)`: it patches the matching in-memory `Series`
(status/reading status/reading mode), drops that series' cached card and its issue rows, and triggers a
zero-debounce swap. Those three commands call it. The search index is untouched, since none of those fields
is in a bundle. Rule for new code: an in-place mutation of anything a row or card displays must call the hook
or reload. A test pins each of the three commands (label on the card changes without a reload).

**Brushes.** `SeriesCardSample.Gradient`/`CoverBrushFor` already return `ImmutableLinearGradientBrush`
(via `.ToImmutable()`), and `SeriesCardSampleTests.CoverBrush_IsImmutable_SoItSurvivesBeingBuiltOffTheUiThread`
pins it; a mutable `LinearGradientBrush` built off-thread crashed the compositor once before. That contract
stays: any brush created by the projection must be an `Immutable*` type. The change here is only that
`CoverBrushFor` looks up one shared immutable brush per palette entry (8 entries, built once) instead of
allocating a temporary mutable brush, its stops, and an immutable copy per row.

### 5. One swap, not N adds

New `BulkObservableCollection<T> : ObservableCollection<T>` with `ReplaceAll(IEnumerable<T>)`: replaces
the contents and raises a single `Reset`. `Covers`, `FlatCovers`, `Rows`, `FlatRows` use it. Each
`VirtualizingWrapPanel` and `VirtualizingStackPanel` consumer then re-realizes once per swap instead of
once per item.

**Swap order (UI thread, one dispatcher callback):**

1. Generation check; drop the result if stale.
2. Diff-only selection sync on the result's rows/cards (§4), **before** any collection is touched.
3. `ReplaceAll` on `Covers`/`FlatCovers`/`Rows`/`FlatRows`/`Groups` (one `Reset` each).
4. Update counts, empty-state properties, `HasAnyResults`.
5. Raise `ScrollToTopRequested` if the result asks for it.

Selection sync goes first so the re-realized containers bind already-correct `IsSelected` values and no row
flips after the `Reset`. Checked: `LibraryScreen.axaml`/`.cs` have no `SelectionChanged` handlers, and
multi-select is the app's own `Selection` service, not `ListBox` selection (list containers are retemplated to
inert, non-selectable items), so a `Reset` raises no selection event cascade here. The ordering rule is for
correct flags and no post-`Reset` flicker, not for suppressing events.

**Scroll policy.** A `Reset` does not itself move the scroll offset: `ScrollViewer` keeps its offset and
clamps it to the new extent, and nothing in `LibraryScreen.axaml.cs` scrolls on a rebuild today (the only
`Offset` writes are the A-Z indexer and `ScrollIntoView`). So this is a stated policy, not a side effect:

- **Search-text change:** scroll to top after the swap. A new result set should start at its first row.
  This is new behavior (today the offset is just clamped). The result carries `ScrollToTop = true` and the
  ViewModel raises `ScrollToTopRequested`; `LibraryScreen.axaml.cs` scrolls the active mode's scroll host
  (poster/tiles/panorama `ScrollViewer`, list/details `ListBox`).
- **Sort/group/filter/sidebar/selection swaps:** no scroll request; the offset is preserved and clamped.
- **Back/forward browse history** (`ApplyBrowseState`): scrolls to top, same as a search change.

**Group collections.** The background job produces plain immutable lists (`IReadOnlyList<T>`) for rows,
cards, and per-group items. `IssueListRowGroup.Items`/`SeriesCardGroup.Items` stay `ObservableCollection<T>`
for XAML and test compatibility, but they are constructed on the UI thread during the swap, from those lists
(one O(N) copy). No `ObservableCollection` is created or touched off-thread; not because an unbound instance
would throw, but so that nothing bound to the UI can ever be built by the job.

### 6. Side effects off the keystroke path

| Side effect | Now | New |
|---|---|---|
| `SaveLibrarySettings` | sync DB write per keystroke | debounced ~500 ms, off UI thread |
| Sidebar summaries, `ExistingSeriesNames` | rebuilt per keystroke | rebuilt only on data load or sidebar selection change |
| `PlayEntranceAnimation` | always true | false for search/filter swaps |
| `KickCoverReconcile` | every `RebuildView` | only from `LoadFromDatabase` |
| `RecomputeSuggestions` | per keystroke, scans whole pool, sorts all matches | still per keystroke and synchronous, but binary-search prefix range + early-exit scan (§1), `ReplaceAll` result; gated at p95 < 1 ms at 10k issues |

### 7. Threading rules

- The background job reads only immutable data: the index strings, the cached rows/cards, and the
  snapshotted inputs. No `ObservableCollection` and no UI object is touched off-thread.
- `_allSeries` is already `AsNoTracking` and is replaced wholesale on load, so readers never see a
  half-updated snapshot.
- The UI swap runs via `Dispatcher.UIThread.Post` at Background priority, after checking the generation.
- Per the routed-event gotcha in `CLAUDE.md`: the swap replaces item collections that back rows containing
  buttons. Swaps triggered from a button/popup click are posted one dispatcher tick later, never inline
  in the routed event.

## Testing

- **Index parity:** a corpus test compares `LibrarySearchIndex` against the existing `MatchesSearch`
  logic for every `SearchMode` × a set of queries (series-granularity sets must be identical), in the
  style of `SearchFieldBundleCatalogParityTests`.
- **Per-issue rule:** new tests for `writer:` / `artists:` / `catalog:` in issue granularity (only
  matching issues), and series-name search (all issues of the series).
- **Pipeline:** injectable debounce/scheduler so existing search tests run with zero delay and a
  synchronous compute, no dispatcher pump needed. Add tests for stale-generation drop and
  cancel-on-new-keystroke.
- **Bulk collection:** one `Reset`, correct contents, empty replace.
- **Cancellation and stale drops:** a newer keystroke cancels the older job's token and the older result never
  swaps in; clearing the box swaps without waiting for the debounce.
- **Scroll policy:** search change raises `ScrollToTopRequested`; sort/group/filter do not.
- **Invalidation hook:** the three non-reloading commands change the card label without a reload. Race test:
  an invalidation issued while a search job is in flight cancels it, and the stale result never swaps in over
  the patched state.
- **Selection:** select a row, filter it out, clear the selection, bring it back: it shows unselected. Select
  while filtered, clear the search: selected rows show selected. Swap writes no `IsSelected` when nothing differs.
- **Suggestions:** identical output to the current implementation over a corpus (ordering and cap), plus the
  p95 < 1 ms gate at 10k / 1.5k.
- **Fixtures:** `IssueListScreenViewModelTests.MakeIssue` gives every issue `Id = 0`; the projection cache is
  id-keyed, so fixtures get unique ids first.
- **Measurement:** a stopwatch harness (a test project console entry, not BenchmarkDotNet, whose
  `GlobalSetup` is broken under BDN in this repo) times a search swap end to end. Scenarios: 3k issues /
  500 series (current) and 10k / 1.5k (headroom), before and after. Numbers are recorded in the plan.
- **On-screen:** the user verifies typing and result feel on their real library. Automation is not used
  without permission.

## Risks

- **Stale rows after edits.** Mitigated by rebuilding the cache on every `LoadFromDatabase`, the only
  mutation path. A test asserts that a mutation followed by reload shows new data.
- **Behavior change from the per-issue fix.** Intentional and user-approved. Called out in the plan and in
  the release notes.
- **Thread affinity of Avalonia types.** Rows are `ObservableObject`s and the projection brush is already
  immutable (kept by the existing test). Any other projected type found to be thread-affine has its creation
  moved into the UI-thread swap; the projection cache is built off-thread only for the types that pass
  `FromSeries_BuiltOnBackgroundThread_BrushPropertiesReadableFromCaller`-style checks.
- **Scroll-to-top is new behavior.** Search results start at the top; previously the offset was only clamped.
- **Search-history push and workspace tracking.** `PushBrowseHistory` (debounced 800 ms today) and
  `SaveLibrarySettings`' workspace-label reset stay attached to the `SearchQuery` change hook, not the
  compute path. Verified by the existing browse-history tests.

## Out of scope, logged

- Sync `LoadFromDatabase` on nav-in (whole-library materialization, `GetMembers` per smart collection,
  suggestion index build).
- Scroll smoothness spec: decode queue, byte-budget display-size cache, prefetch, lazy card parts, mouse-wheel
  easing with a "Smooth scrolling" toggle.
