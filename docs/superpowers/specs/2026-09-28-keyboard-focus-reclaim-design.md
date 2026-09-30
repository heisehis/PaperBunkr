# Keyboard focus reclaim, app-wide — Phase 1 (Library)

## Problem

Reading Lists' keyboard-operability follow-up (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md)
found and fixed two distinct ways a view silently loses keyboard focus with nothing to put it back:

- **Shape A — a permanently-attached sibling view/panel toggled only by `IsVisible`.** `ReadingListsScreen.axaml`
  keeps its gallery and list-page views both attached at all times (deliberately, so scroll position survives a
  round trip) and just flips `IsVisible` between them. A view built this way never regains focus when shown again,
  because nothing was ever focused inside it in the first place — arrow keys then go nowhere, or a stray focus
  change lands on the app's nav rail.
- **Shape B — a bound `ObservableCollection` resets in place while the view stays visible throughout.** A folder
  navigation, a list switch, a search/filter refresh: the container that held focus is detached along with the old
  content, and nothing reclaims it.

`FocusReclaimer` (`src/Paperbunkr.App/Views/FocusReclaimer.cs`) was built to close both gaps and is wired into the
two Reading Lists views (`ReadingGalleryView.axaml.cs`, `ReadingListPageView.axaml.cs`). A follow-up survey (see
"Survey findings" below) found both shapes recur throughout the rest of the app. This spec covers **Phase 1: Library**
— the single highest-severity screen — and sketches the roadmap for the phases after it. Each later phase gets its
own spec/plan cycle; this document does not design them.

## Survey findings (why Library first)

No top-level screen switch (Home/Library/Books/Continuity/Smart Lists/Reading Lists/Wanted/Preferences/Insights) has
either shape — `MainWindow.axaml` already uses a `TransitioningContentControl` with a `DataTemplate` per screen, so
the old view is discarded on every navigation (a deliberate earlier fix, per the comment at `MainWindow.axaml:899-901`).
Both shapes live entirely *inside* individual screens:

| Screen | Shape A | Shape B | Severity |
|---|---|---|---|
| **Library** | 3 nested layers: view mode (Poster/Panorama/List/Details/Tiles) × granularity (Issue/Series) × grouping | `Covers`/`Groups`/`FlatCovers`, `IssueList.Rows`/`FlatRows` reset on every search/filter/sort/content-type change | **High** — highest-traffic, highest-density screen |
| Continuity | Hero/overview toggle + Overview/Timeline/Map body switch | Timeline `Populate()`/histogram rebuild | Medium-High — brand-new screen, zero focus infra |
| Books | Grouped/ungrouped toggle | `Rebuild()` on every search/sort/group change | Medium-High |
| Preferences | 13 sections, all permanently attached | — | Medium |
| Wanted | 3 tabs | `ReleaseDays`/`CalendarDays`, series-add search results (`QueueItems`/`SeriesRows` already protected by a hand-rolled `SyncList`) | Medium (partially self-mitigated) |
| MainWindow contextual sidebar | Per-rail-section content + Continuity's own sub-tab | — | Low-Medium — cross-cutting shell code, not owned by one screen |

Sequencing (later phases, not this spec): Continuity and Books next, then Preferences and Wanted, then the
MainWindow sidebar last.

## Phase 1 architecture

### Extract the virtualization-aware "focus by index" logic

`ReadingListKeyboard.FocusedIndex`/`FocusIndex` (`src/Paperbunkr.App/Views/ReadingListKeyboard.cs`) already solve
"establish focus at index N, scrolling a virtualized panel into view and retrying until its container realizes" —
proven this session against both a `VirtualizingStackPanel` and a custom `VirtualizingWrapPanel`. This is a
different job from Library's existing `GridKeyboardNavigation`, which only computes *movement from an
already-focused item* and has no notion of establishing initial focus with nothing focused yet.

Extract the screen-agnostic part into a new file, `src/Paperbunkr.App/Views/VirtualizedFocus.cs`:

```csharp
internal static class VirtualizedFocus
{
    public static int FocusedIndex(ItemsControl list);
    public static void FocusIndex(ItemsControl list, int target, int direction);
}
```

`ReadingListKeyboard.FocusedIndex`/`FocusIndex` become one-line forwarders to this class — **no existing call site
or test changes**, so Reading Lists' own passing regression tests are the safety net for the extraction. Library's
code calls `VirtualizedFocus` directly.

### One `FocusReclaimer` for the whole screen

`LibraryScreen.axaml.cs` gets a single `FocusReclaimer` (not one per view mode/panel — only one region is ever
visible at a time, so a single reclaimer whose fallback dynamically resolves the active region is simpler than
gating several reclaimers by which one is "active," mirroring how Reading Lists' own `Body(vm)` already picks
between `PathList`/`CoverWall`):

```csharp
_focus = new FocusReclaimer(this, () => ActiveItemsControl() is not null,
    () => VirtualizedFocus.FocusIndex(ActiveItemsControl()!, _lastIndex ?? 0, 1));
```

`ActiveItemsControl()` walks the same nesting order the XAML's `IsVisible` bindings use (view mode → granularity →
grouping, enumerated from `LibraryScreen.axaml`'s real structure during implementation) and returns the one
currently-visible leaf `ItemsControl`, or `null` when Library shows an empty state.

### Four triggers (not three — Library needs one Reading Lists didn't)

1. **`OnAttachedToVisualTree`** — matches Reading Lists.
2. **`LibraryScreen`'s own `IsVisible` flipping true** — matches Reading Lists. Likely a no-op today, since
   MainWindow already discards and rebuilds `LibraryScreen` on every navigation to it (no case where Library itself
   stays attached-but-hidden) — wired anyway for consistency with the shared pattern, at no cost.
3. **New for Library — the VM's `PropertyChanged` on the properties driving an *inner* panel's `IsVisible`**:
   `IsPosterGrid`/`IsPanoramaGrid`/`IsListView`/`IsDetailsTableView`/`IsTilesView` (view mode),
   `IsIssueGranularity`/`IsSeriesGranularity` (granularity), `IssueList.IsGrouped` (grouping) — confirm the exact
   set against `LibraryScreen.axaml`'s real bindings during implementation, since XAML can drift from this list.
   Unlike Reading Lists' gallery/list pair, Library's mode switches never toggle `LibraryScreen`'s own `IsVisible`
   — only its children's — so there is no view-level `IsVisible` change to hook. Subscribe to `vm.PropertyChanged`,
   filter to this property set, call `_focus.Reclaim()`.
4. **`CollectionChanged` on all five relevant collections** (`Covers`, `Groups`, `FlatCovers`, `IssueList.Rows`,
   `IssueList.FlatRows`), subscribed unconditionally in `OnDataContextChanged` — matches Reading Lists' Shape-B
   wiring. All five are stable `{ get; }`-only properties for the VM's lifetime (confirmed), so one subscribe/
   unsubscribe pass per `DataContext` change is sufficient, the same shape `ReadingGalleryView` already uses for
   `Tiles`. `BulkObservableCollection<T>` is a plain `ObservableCollection<T>` under the hood (batches its own
   `Reset` notification) — no special-casing needed versus a plain collection.

No debouncing: `Reclaim()` is cheap (a dispatcher post plus an `O(depth)` focus check) and already deferred, so
firing on every debounced search keystroke's collection reset is fine.

### Search-box safety (a property worth stating explicitly, not just testing)

While typing in Library's search box, every debounced keystroke resets a collection, firing trigger 4 →
`Reclaim()`. Since the search box sits inside the reclaimer's region, `FocusReclaimer.IsInside` sees it as
already-focused and no-ops — typing is never interrupted. This falls out of the existing contract with no special
casing, but it's exactly the kind of thing that's easy to break by accident later, so it gets its own regression
test (below).

## Testing

One test per instance, per the reading-list precedent (`ReadingListsScreenViewTests.cs`), using the same
`WithThemeAndTokens`/`Press`/`Focused` headless harness:

1. Initial focus lands in the grid with no prior click.
2. Switching view mode (e.g. Poster → List) keeps focus inside Library (Shape A, outer layer).
3. Toggling granularity or grouping while a card is focused keeps focus inside Library (Shape A, nested layers —
   the part unique to Library versus Reading Lists' simpler two-mode case).
4. A search/filter that resets the active collection doesn't drop focus outside the screen (Shape B).
5. Typing in the search box survives the collection resets it triggers (the safety property above).

## Out of scope (this phase)

Continuity, Books, Preferences, Wanted, and the MainWindow contextual sidebar — each gets its own spec/plan cycle,
reusing `VirtualizedFocus` and `FocusReclaimer` as built here. Sequencing per the severity table above.
