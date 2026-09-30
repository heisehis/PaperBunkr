# Keyboard focus reclaim, Phase 1 (Library) — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-28-keyboard-focus-reclaim-design.md*

## Survey notes (beyond what the design doc already covers)

- Library's grouped Poster/Panorama/Tiles modes are a level deeper than the design doc's "3 nested layers"
  framing suggested: the grouped branch is an *outer* `ItemsControl` of group headers, each realized as a
  `StackPanel` containing its *own inner, independently-virtualized* `ItemsControl` of cards
  (`LibraryScreen.axaml:1302-1323` etc.). `ReadingListKeyboard.FocusIndex`'s existing retry loop advances to the
  next *outer* index the moment a container is found but has no focusable descendant yet — wrong for this case,
  where the right move is retrying the *same* outer index a few more ticks while the inner panel realizes its own
  first item. List/Details modes don't have this problem — they use the pre-flattened `FlatRows`/`FlatCovers`
  projections (group headers interleaved as ordinary rows in one flat virtualized list), so only Poster/Panorama/
  Tiles's grouped sub-mode needs it.
- `LibraryScreen.axaml.cs` already has `FocusFirstGridItem()` (wired to the toolbar's Esc-from-search-box action),
  whose one-line resolution — `this.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault(ic =>
  ic.IsEffectivelyVisible && ic.ItemCount > 0)` — already finds "whichever of the grid-family ItemsControls is
  actually visible right now," working across all 5 view modes without needing new `x:Name`s on the ~10 unnamed
  inner `ItemsControl`s. Reusing this (factored out so both call sites share it) means **no XAML edits are
  needed** for Phase 1.
- `LibraryScreen.axaml.cs`'s `OnDataContextChanged` is a plain event handler (`DataContextChanged +=`, not an
  override) with an explicit precedent: *"`Library` is a single stable instance for this control's whole
  lifetime... No unsubscribe guard needed."* (MainWindow discards and rebuilds the View on every navigation, so
  `LibraryScreen` itself is only ever constructed with one `DataContext`.) New subscriptions go inside its existing
  `if (DataContext is LibraryScreenViewModel vm)` block, no unsubscribe/resubscribe pairing needed — unlike
  `ReadingGalleryView`, which does need that pairing since its `DataContext` genuinely can be reset.
- Driving properties confirmed by reading the VM source (not the XAML's binding names, which don't all raise their
  own `PropertyChanged`): `LibraryScreenViewModel`'s `ViewMode`/`GridCoverFit`/`Granularity` are `[ObservableProperty]`
  and raise their own notifications automatically. Grouping is different — `LibraryScreenViewModel.IsGrouped` is a
  pure alias (`=> IssueList.IsGrouped`) that **never raises its own `PropertyChanged`** when the underlying value
  changes (confirmed: the constructor's existing `IssueList.PropertyChanged` relay forwards `IsGrouped` changes to
  `ShowAlphabetIndex`/`RaiseChipAndEmptyState`, never to its own `IsGrouped`). Trigger 3 has to subscribe to
  `vm.PropertyChanged` for the first three and **separately** to `vm.IssueList.PropertyChanged` (filtered to
  `GroupField`/`IsGrouped`) for grouping — one subscription target does not cover both.

## Step 1: Extract `VirtualizedFocus`, with a nested-virtualization retry fix
**Files:** `src/Paperbunkr.App/Views/VirtualizedFocus.cs` (new), `src/Paperbunkr.App/Views/ReadingListKeyboard.cs` (edit)
**What:** Move `FocusedIndex`/`FocusIndex`/`FirstFocusable` out of `ReadingListKeyboard` into the new class,
unchanged except: when `FocusIndex`'s retry loop finds a target index's container but `FirstFocusable` returns
null (the container's own content hasn't realized yet — Library's grouped-mode inner panels), retry the *same*
index a few more `DispatcherPriority.Loaded` ticks (capped, e.g. 5) before advancing to the next index, instead of
advancing immediately. `ReadingListKeyboard.FocusedIndex`/`FocusIndex` become one-line forwarders to
`VirtualizedFocus` — no other file in `ReadingListKeyboard`'s own call sites or tests changes.
**Depends on:** none
**Verify:** the existing Reading Lists suite (`ReadingListsScreenViewTests`, `ReadingGalleryAndPathTests`,
`ReadingListPageRequestMissingTests`) unchanged and still green — the new same-index retry only activates when a
container is found with zero focusable descendants, which never happens in Reading Lists' flat-list case, so it's
a no-op path there.

## Step 2: Wire `FocusReclaimer` into `LibraryScreen.axaml.cs`
**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit)
**What:**
- Factor `FocusFirstGridItem`'s resolution one-liner out into a shared `private ItemsControl? ActiveGridItemsControl()`
  method; have `FocusFirstGridItem` call it (no behavior change to that existing method).
- Add `private readonly FocusReclaimer _focus;`, constructed in the constructor:
  `new FocusReclaimer(this, () => ActiveGridItemsControl() is not null, () =>
  VirtualizedFocus.FocusIndex(ActiveGridItemsControl()!, _lastGridIndex ?? 0, 1))`.
- Add `_lastGridIndex` (`int?`), updated via a `GotFocusEvent` handler on `this` using
  `VirtualizedFocus.FocusedIndex(ActiveGridItemsControl())` — mirrors `ReadingGalleryView`'s `_lastTileIndex`.
- Call `_focus.Reclaim()` from four places:
  1. The existing `OnAttachedToVisualTree` override — add one line.
  2. A new `PropertyChanged` subscription in the constructor, filtered to `IsVisibleProperty` — matches Reading
     Lists' pattern (likely a no-op today per the survey, wired for consistency at no real cost).
  3. Inside the existing `OnDataContextChanged`'s `vm.PropertyChanged +=` handler (line ~192): extend its `if` to
     also match `nameof(vm.ViewMode)`, `nameof(vm.GridCoverFit)`, `nameof(vm.Granularity)`.
  4. A new `vm.IssueList.PropertyChanged +=` subscription (same `OnDataContextChanged` block) filtered to
     `nameof(IssueListScreenViewModel.GroupField)` / `nameof(IssueListScreenViewModel.IsGrouped)`.
  5. New unconditional `CollectionChanged +=` subscriptions (same block, no unsubscribe needed per the survey
     note above) on `vm.Covers`, `vm.Groups`, `vm.FlatCovers`, `vm.IssueList.Rows`, `vm.IssueList.FlatRows`.
**Depends on:** Step 1
**Verify:** Step 3's tests.

## Implementation note (found while writing Step 3's tests)

`ActiveGridItemsControl`'s "first visible `ItemsControl` with items" resolution had a real, pre-existing bug for
Details mode: its column-header row is *also* an `ItemsControl` (over `DetailsColumns`), sitting before the actual
data grid in document order, so the naive match picked the header instead - a latent bug in `FocusFirstGridItem`
too (this method is its exact prior body), not introduced by this phase, but blocking enough of Phase 1's own
correctness (and its test) to fix here rather than deferring it. Fixed by checking the first realized container
actually holds a `Button.card` (every real content grid's items are `Button.card`; the header's are
`Button.detailsHeader`).

## Step 3: Headless regression tests
**Files:** `src/Paperbunkr.App.Tests/LibraryScreenViewTests.cs` (new — `LibraryScreenViewModelTests.cs` only
exercises the VM, there's no existing view-rendering test for Library to extend)
**What:** Follow `ReadingListsScreenViewTests.cs`'s `WithThemeAndTokens`/`Press`/`Focused` harness (adapting the
injected token set to whatever `LibraryScreen.axaml` actually needs — survey its `DynamicResource`/`StaticResource`
lookups while writing this, the same way that file's own token dictionary was built up). Seed enough issues/series
to form multiple groups for the grouping tests. Five tests, one per instance fixed:
1. Initial focus lands in the grid with no prior click.
2. Switching view mode (Poster → List) keeps focus inside Library.
3. Toggling grouping while a card is focused keeps focus inside Library, with grouping *on* specifically (exercises
   Step 1's nested-virtualization retry).
4. A search/filter that resets the active collection doesn't drop focus outside the screen.
5. Typing in the toolbar's search box survives the collection resets it triggers.
**Depends on:** Step 2
**Verify:** `dotnet test --filter "FullyQualifiedName~LibraryScreenViewTests"`, run 3× for flake-checking (this
session's own established practice for this kind of test), then the full `Paperbunkr.App.Tests` suite once before
calling Phase 1 done.
