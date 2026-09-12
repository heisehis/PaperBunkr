# Entrance-animation v2 — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-12-entrance-animation-v2-design.md*

**Correction from the design doc during planning:** §5 item 4 said the Reading Lists entrance wires
onto "the outer `ItemsControl` over `Groups`." Re-checked against Library's own precedent
(`LibraryScreen.axaml` — every grouped view mode wires `EntranceAnimation.Enabled` on the *inner*
per-group items `ItemsControl`, never the outer group-container one, e.g. lines 856/876, 929/945).
Reading Lists follows the same rule: it wires onto the inner `<ItemsControl ItemsSource="{Binding
Rows}">` (ReadingScreen.axaml:442), inside the group `DataTemplate`, using the same
`$parent[UserControl].((vm:ReadingScreenViewModel)DataContext)` idiom already used two lines below it
(line 449) — not the outer `Groups` control. This still staggers rows, just correctly scoped per the
established pattern (stagger resets per group, matching how Library's own grouped modes behave).

## Step 1: `EntranceAnimation` stagger-tail cap
**Files:** `src/Paperbunkr.App/Controls/EntranceAnimation.cs` (edit), new
`src/Paperbunkr.App.Tests/EntranceAnimationTests.cs`
**What:** Extract the inline delay math in `Prepare` (`index * PerItemDelayMs`, gated by
`MotionTokens.IsReducedMotion()`) into `internal static int ComputeDelayMs(int index, bool
reducedMotion)`, clamping `index` to a new `private const int MaxStaggerIndex = 20`. `Prepare` calls
`ComputeDelayMs(index, MotionTokens.IsReducedMotion())` instead of computing inline.
**Depends on:** none
**Verify:** new `EntranceAnimationTests.ComputeDelayMs_ClampsAboveMaxStaggerIndex` (index 20 and 25
produce the same delay; index 5 differs; `reducedMotion: true` always yields 0).

## Step 2: Books screen
**Files:** `src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/BooksScreen.axaml` (edit),
`src/Paperbunkr.App.Tests/BooksScreenViewModelTests.cs` (edit)
**What:**
- VM: `[ObservableProperty] private bool _playEntranceAnimation;` (near other simple flags); set
  `PlayEntranceAnimation = true;` at the top of `Rebuild()` (line 513) — the single funnel every
  trigger (`LoadFromDatabase` nav-in, search/sort/sort-direction changes) already runs through.
- View: add `controls:EntranceAnimation.Enabled="{Binding PlayEntranceAnimation}"` to the ungrouped
  `ItemsControl` (line 273) and
  `controls:EntranceAnimation.Enabled="{Binding $parent[UserControl].((vm:BooksScreenViewModel)DataContext).PlayEntranceAnimation}"`
  to the per-group nested one (line 305) — same two-tier idiom as Library's own grouped templates.
**Depends on:** none (independent of Step 1; the cap applies uniformly once merged)
**Verify:** new `BooksScreenViewModelTests` case: `PlayEntranceAnimation` is `true` after
`LoadFromDatabase()` and after a `SearchQuery`/`SortField`/`SortDirection` change, same shape as
`LibraryScreenViewModelTests`' `*_ResetsPlayEntranceAnimation` cases.

## Step 3: Smart Lists screen
**Files:** `src/Paperbunkr.App/ViewModels/SmartScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/SmartScreen.axaml` (edit),
`src/Paperbunkr.App.Tests/SmartScreenViewModelTests.cs` (edit)
**What:**
- VM: `[ObservableProperty] private bool _playEntranceAnimation;`. Set `PlayEntranceAnimation = true;`
  at the top of **both** `RecomputeMatchCount()` (line 587) and `RecomputeMatchCount(SmartListQueryBuilder.LibrarySnapshot
  snapshot)` (line 638) — together these are the one real funnel every results-repopulation path
  reaches: `LoadSmartList`'s nav-in (both its `RecomputeMatchCount(snapshot)` and `RecomputeMatchCount()`
  branches, lines 424-431) *and* the rule-tree editor's live `onChanged: RecomputeMatchCount` callback
  (line 408, a genuine filter-changed trigger, analogous to Library's). Setting it in both overloads
  (rather than only `EnsureListLoaded()`, as the design doc's prose suggested) is more robust — it
  can't rot if `LoadSmartList`'s internals change — and still covers exactly the same real triggers.
- View: add `controls:EntranceAnimation.Enabled="{Binding PlayEntranceAnimation}"` to all three
  `VirtualizingWrapPanel`-backed `ItemsControl`s (`ResultsList` line 290, `SeriesResultsList` line
  317, `NovelResultsList` line 341) — one shared flag, no `$parent` indirection needed since none of
  these three sit inside a nested per-group `DataTemplate`.
**Depends on:** none
**Verify:** new `SmartScreenViewModelTests` cases: `PlayEntranceAnimation` is `true` after loading a
list (covers `EnsureListLoaded`/`LoadSmartList`) and after editing a condition (covers the
`onChanged` callback → `RecomputeMatchCount()`).

## Step 4: Reading Lists screen
**Files:** `src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit, 2 call sites),
`src/Paperbunkr.App/Views/ReadingScreen.axaml` (edit),
`src/Paperbunkr.App.Tests/ReadingScreenViewModelTests.cs` (edit)
**What:**
- `ReadingScreenViewModel.cs`: `[ObservableProperty] private bool _playEntranceAnimation;` near
  `_arcCoverImage` (line 572). Change `LoadReadingList(int readingListId)` (line 574) to
  `LoadReadingList(int readingListId, bool triggerEntrance = false)`; at its top (or wherever fits
  the existing flow cleanly), `if (triggerEntrance) { PlayEntranceAnimation = true; }`.
- Pass `triggerEntrance: true` from exactly these 6 genuine switch/nav-in call sites (all others keep
  calling `LoadReadingList(id)` unchanged, defaulting `false`):
  - `ReadingScreenViewModel.EnsureListLoaded()` — both internal calls, lines 654 and 662.
  - `ReadingScreenViewModel.CreateNew` — line 1155.
  - `ReadingScreenViewModel.SelectList` — line 1163.
  - `ReadingScreenViewModel.DeleteReadingList`'s post-delete fallback — line 779.
  - `MainViewModel.GoReadingWithList` — line 787.
  - `MainViewModel.OnNewReadingListCreated` — line 1103.
- `ReadingScreen.axaml`: add
  `controls:EntranceAnimation.Enabled="{Binding $parent[UserControl].((vm:ReadingScreenViewModel)DataContext).PlayEntranceAnimation}"`
  to the **inner** `<ItemsControl ItemsSource="{Binding Rows}">` at line 442 (see the correction note
  above — not the outer `Groups` control).
**Depends on:** none
**Verify:** new `ReadingScreenViewModelTests` cases — `PlayEntranceAnimation == true` after
`SelectList`, `CreateNew`, and `EnsureListLoaded`; stays `false` after a representative mutation
sample (`BulkMarkRead`/`MarkSelectedRead`, `Reorder` via `MoveItemUp`, `AddIssue`).

## Step 5: Build + verify
**What:** Full `Paperbunkr.App` build (XAML weave — these are edits to existing compiled views, not
new ones, so the AVLN2000 new-view gotcha doesn't apply); run all four new/extended test classes
(`EntranceAnimationTests`, `BooksScreenViewModelTests`, `SmartScreenViewModelTests`,
`ReadingScreenViewModelTests`) plus a regression pass of `LibraryScreenViewModelTests`' own
`PlayEntranceAnimation`-related cases (unaffected, but Step 1's shared-control change touches code
they exercise indirectly). On-screen verification of the stagger visual, the large-N cap, and Reduced
Motion across all three screens stays out of scope this session (no computer-use) — flag as
outstanding per the design doc §8.
