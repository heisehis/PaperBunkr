# Cover-Image Memory Virtualization — Design Spec

*Date: 2026-08-22. Scope: root-cause and fix the Library/Home screens' cover-art memory usage
(observed: ~1.4GB resident after normal browsing) - real virtualization of the grid card screens,
not another capacity-constant tweak.*

**Investigation, not a guess:** the user asked why the app was using ~1.4GB. `CoverImageCache`'s
history was checked via `git log`/`git show` before touching anything, since its own doc comments
already told a "we fixed a crash by bumping the cache cap" story. That story turned out to be
wrong: commit `04a1eb0` (00:34) fixed `LruCache` eviction to never dispose evicted `Bitmap`s
(confirmed still true today by `LruCacheTests.Add_ExceedingCapacity_DoesNotDisposeEvictedValue`,
which passes). A later commit the same day, `806762a` (04:36), re-diagnosed a real crash against a
large library but wrote its fix based on the belief eviction *still* disposes - already false by
then - and "fixed" it by bumping the cap from 1000 to 5000 (both `CoverImageCache` and
`BookCoverImageCache`), which doesn't address any bug that actually still existed. The real driver
was found by tracing where decoded `Bitmap`s actually get held: `SeriesCardSample`/`IssueListRow`
(and their Books-screen equivalents) bind a **permanently**-held `Bitmap` into an `init`-only
property, populated eagerly for the *entire* library in one synchronous pass, and the Library grid
used a plain `WrapPanel` inside `ItemsControl` - which has no virtualization support in Avalonia for
a wrapping layout. So every series/issue ever loaded held a live decoded cover for the rest of the
session, regardless of the LRU cap size or scroll position - proportional to library size, not a
"leak" but a real, unbounded-with-library-growth memory ceiling.

## 1. Scope

Covers:
- A real virtualizing panel (`VirtualizingWrapPanel`), scoped to **uniform-size** grids only (see
  §2 for why) - wired into the Library screen's Compact/Comfortable/CoverOnly grid density modes,
  both granularities (`SeriesCardSample` and `IssueListRow`), both grouped and ungrouped rendering.
- Lazy, on-realize cover decoding: `SeriesCardSample.CoverIssueId`/`IssueListRow.Id` resolve to a
  `Bitmap` only when a card's container is actually realized, via a new `CoverImageConverter`
  bound directly in XAML - not eagerly at card-construction time. This is the other necessary half:
  virtualizing the *panel* alone doesn't save anything if the *view-model* still eagerly decodes
  and permanently holds every cover regardless of container realization.

Explicitly out of scope, decided with the user during design:
- **Panorama grid mode** - its per-card variable width came from each cover's real decoded aspect
  ratio, which conflicts with lazy decoding (an unrealized card's real ratio isn't known yet).
  Confirmed with the user: Panorama now uses `SeriesCardSample.DefaultCoverAspectRatio` uniformly
  instead of a real per-cover ratio, trading its shape-adaptive width variety for the memory fix
  applying everywhere covers are constructed (not just the virtualized modes) - and it keeps using
  the plain, non-virtualizing `WrapPanel` it always has (variable widths aren't uniform-grid
  arithmetic).
- **Tiles view mode** - its card template has no single fixed-height VM property to virtualize
  against (a horizontal thumb+text row layout, not a clean image-tile grid like the other three
  modes) - left as plain `WrapPanel`, unchanged.
- **List / Details view modes** - already row-based (`StackPanel`, not `WrapPanel`), not a
  wrap-grid problem in the first place.
- **Books screen, Smart Lists screen** - same eager-decode pattern exists there
  (`BookCardSample`/`SmartScreenViewModel`'s per-issue rows), not yet retrofitted. Natural follow-up
  once this pass is verified, using the exact same `VirtualizingWrapPanel`/`CoverImageConverter`
  pair - no new design needed, just wiring.

## 2. Why a custom `VirtualizingPanel`, and why scoped to uniform sizes

Checked Avalonia's real source (`AvaloniaUI/Avalonia` on GitHub, tag `11.3.0`) before writing
anything: `VirtualizingStackPanel.cs` (~1000 lines), `VirtualizingPanel.cs` (the abstract base
every virtualizing panel implements), `ItemContainerGenerator.cs` (the realize/recycle protocol),
and `WrapPanel.cs` (the non-virtualizing layout this replaces). Two things confirmed from that
reading, not assumed:

- Avalonia 12.1.1 ships no virtualizing *wrap*-grid panel - only `VirtualizingStackPanel`
  (single-axis). A wrap layout needs its own `VirtualizingPanel` subclass.
- `ItemsPresenter` (`src/Avalonia.Controls/Presenters/ItemsPresenter.cs`) auto-detects
  `Panel is VirtualizingPanel` and calls `Attach(ItemsControl)` - a custom subclass dropped into
  `<ItemsControl.ItemsPanel>` wires up exactly like the built-in one, no other plumbing needed.

A general flow/wrap virtualizer is a genuinely hard problem (which row an item falls in depends on
the cumulative widths of every item before it in that row) - which is why `VirtualizingWrapPanel`
here is deliberately restricted to **uniform** `ItemWidth`/`ItemHeight` (mirroring `WrapPanel`'s own
`ItemWidth`/`ItemHeight` properties for API familiarity). With a uniform grid, which row/column any
index falls in is pure arithmetic (`row = index / itemsPerRow`) - no estimation heuristics, no
scroll-position jitter as unknown sizes get discovered (the entire reason
`VirtualizingStackPanel` carries an `IScrollAnchorProvider` integration and an "estimated element
size" field that refines over time - both omitted here as genuinely unneeded, not overlooked).

`VirtualizingWrapPanel` implements the real `VirtualizingPanel` abstract contract
(`ScrollIntoView`/`ContainerFromIndex`/`IndexFromContainer`/`GetRealizedContainers`/`GetControl`)
using the same `ItemContainerGenerator` realize/recycle protocol `VirtualizingStackPanel` uses
(`NeedsContainer`→`CreateContainer`/pool-reuse→`PrepareItemContainer`→`AddInternalChild`→
`ItemContainerPrepared`, and the inverse on recycle), driven by the `EffectiveViewportChanged`
event every `Layoutable` exposes - the same mechanism `VirtualizingStackPanel` itself uses to learn
its viewport, regardless of whether the scrolling ancestor is the panel's own template-internal
`ScrollViewer` or (as in this codebase) one outer `ScrollViewer` wrapping the whole screen.

The pure layout/realization arithmetic (`ComputeLayout`, `ComputeRealizedRange`,
`IndexToRowColumn`) is extracted into `VirtualizingWrapGridMath` - same "pure math, thin
Avalonia-touching caller" split as `ZoomPanMath`/`GridKeyboardNavigation` already established in
this codebase, and directly unit-testable without a headless UI harness.

`OnItemsChanged` deliberately does a full derealize-and-remeasure on *any* collection-changed
notification rather than porting `VirtualizingStackPanel`'s incremental index-shifting logic
(`RealizedStackElements.ItemsInserted`/`ItemsRemoved`) - every current caller in this codebase
repopulates its card collections via full `Clear()` + re-`Add()` on reload, never fine-grained
single-item mutations, so a full re-realize is both correct and exactly as expensive as real usage
needs.

## 3. Lazy cover decoding

```csharp
// SeriesCardSample.cs - was: public Bitmap? CoverImage { get; init; }, decoded eagerly in FromSeries()
public int? CoverIssueId { get; init; }

// IssueListRow.cs - was: public Bitmap? CoverImage { get; init; }
// (no new property needed - Id already is the issue's own id)
```

```csharp
// CoverImageConverter.cs (Paperbunkr.App.Views) - new
public sealed class CoverImageConverter : IValueConverter
{
    public static readonly CoverImageConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int issueId ? CoverImageCache.Get(issueId) : null;
    // ...
}
```

```xml
<!-- was: <Image Source="{Binding CoverImage}" .../> -->
<Image Source="{Binding CoverIssueId, Converter={x:Static views:CoverImageConverter.Instance}}" .../>
```

The converter is invoked by Avalonia's binding engine only when the bound `Image` control is
actually part of the realized visual tree - i.e., only for containers `VirtualizingWrapPanel` has
realized. A card scrolled far off-viewport gets derealized (its container recycled/hidden), the
binding stops being evaluated, and nothing outside `CoverImageCache`'s own bounded LRU holds the
decoded `Bitmap` alive. Detail-screen call sites (`DetailScreenViewModel`, single-item, low volume)
keep calling `CoverImageCache.Get()` directly and eagerly - no virtualization concern there, one
cover at a time.

## 4. Testing

`Paperbunkr.App.Tests`:
- `VirtualizingWrapGridMathTests` - layout math (items-per-row rounding, degenerate/infinite-width
  inputs), realized-range computation (buffer rows, clamping at both ends of the item list, empty
  layout), row/column arithmetic.
- Existing `CoverImageCacheTests`/`LruCacheTests` continue to cover the cache/eviction layer
  unchanged by this pass (confirmed via a full test-suite run, no regressions).

No new UI-level virtualization test - this codebase has no headless-Avalonia harness exercising
realized-container counts today, and building one is out of scope for this pass. Live verification
(memory before/after against a real library, visual correctness across Compact/Comfortable/
CoverOnly, grouped and ungrouped) is manual, same practice already used for reader-canvas and
migration-UX work in this codebase.
