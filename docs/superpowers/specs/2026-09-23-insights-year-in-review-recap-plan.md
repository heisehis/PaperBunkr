# Insights pitch slice 3 — Year-in-review recap — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-23-insights-year-in-review-recap-design.md*

A third "Recap" tab on the Insights screen: a year picker, a 9-slide paged narrative built from
`ReadingEvent`/`Issue`/`Book`, and a one-click PNG export of a purpose-built poster. No new table, no new
scheduled task — everything is computed live, same as `StatsResolver`.

## Step 1: `RecapResolver` + `RecapSnapshot`

**Files:** `src/Paperbunkr.Data/Metadata/RecapResolver.cs` (new)

**What:** New static class, same shape as `StatsResolver.Build` (`Metadata/StatsResolver.cs:16-66`) but
keyed on a calendar year instead of `InsightsRange`:

```csharp
public static class RecapResolver
{
    public static IReadOnlyList<int> AvailableYears(PaperbunkrDbContext context, DateTime nowUtc);
    public static RecapSnapshot Build(PaperbunkrDbContext context, int year, DateTime nowUtc);
}

public sealed record RecapSnapshot(
    int Year, bool IsCurrentYear,
    int ItemsFinished, long PagesRead, int LongestStreakDays,
    RecapBusiestDay? BusiestDay,
    HighlightGroup? TopSeries, HighlightGroup? TopWriter, HighlightGroup? TopArtist,
    HighlightGroup? HighestRatedSeries, HighlightGroup? MostRereadItem);

public sealed record RecapBusiestDay(DateOnly Date, long Pages);
```

- Reuses `StatsResolver`'s public `HighlightGroup` record (`StatsResolver.cs:731`) directly — same
  `Paperbunkr.Data.Metadata` namespace, no new type needed for tie handling/`DisplayTitle`.
- All queries filter `context.ReadingEvents`/`Issues`/`Books` to events whose `TimestampUtc.ToLocalTime()`
  falls within `[year-01-01 00:00:00, year-12-31 23:59:59.999]` local time — same `ToLocalTime()` convention
  `StatsResolver.ComputeStreak`/`ComputeHeatmap` already use, not UTC.
- `AvailableYears`: `context.ReadingEvents.AsNoTracking().ToList()` (small table, no reason to query
  distinct-year in SQL), `.Select(e => e.TimestampUtc.ToLocalTime().Year).Distinct().OrderByDescending(y => y)`.
- `LongestStreakDays`: new private helper `ComputeYearStreak(List<ReadingEvent> yearEvents)` — collect
  distinct local calendar `Date`s from the year's events into a `HashSet<DateTime>`, then the same
  longest-consecutive-run scan `StatsResolver.ComputeStreak`'s second loop already does (`StatsResolver.cs:143-160`),
  but only over days already filtered to the target year, so a run can never cross into the prior/next year.
- `BusiestDay`: group the year's `Finished`+`Opened` events (any event carrying `PagesRead`) by local calendar
  day, sum `PagesRead ?? 0` per day, take the max; `null` if every day sums to 0.
- `TopSeries`: group issues finished in the year by `Series`, count, `HighlightGroup`-tie on the max count
  (mirrors `StatsResolver.ComputeHighlights`'s `HighestRated`/`MostReread` tie-building shape, `StatsResolver.cs:364-378`).
- `TopWriter`: `Issue.Writer` comma-split/trim/per-issue-distinct exactly like `StatsResolver.ComputeTopCreators`
  (`StatsResolver.cs:654-675`) restricted to issues finished in the year, **plus** `Book.Author` for novels
  finished in the year folded into the same `Dictionary<string,int>` tally before ranking.
- `TopArtist`: same as `ComputeTopCreators`'s artist selector (`Penciller`/`Inker`/`Colorist`), issues finished
  in the year only, no book contribution.
- `HighestRatedSeries`: series average `Issue.Rating` (`> 0` only) among issues finished in the year,
  `HighlightGroup`-tied on the max average (mirrors `ComputeHighlights.HighestRated`, `StatsResolver.cs:364-378`).
- `MostRereadItem`: items (by `(ItemType, ItemId)`) with 2+ `Finished` events in the year, title resolved via
  a local copy of `StatsResolver.ResolveTitle`'s logic (comic → `Series.Name`; not exposed publicly today, so
  duplicate the one-liner rather than making it public for a single external caller).

**Depends on:** none

**Verify:** `dotnet build src/Paperbunkr.Data/Paperbunkr.Data.csproj`; covered by Step 2's tests.

## Step 2: `RecapResolverTests`

**Files:** `src/Paperbunkr.Data.Tests/RecapResolverTests.cs` (new)

**What:** Same fixture shape as `StatsResolverTests`/`InsightsResolverTests` (temp SQLite file,
`InsightsResolverTests.SeedSeries`/`SeedIssue`/`SeedEvent` reused — they're `internal static`, same assembly).
Cases:
- One per tile: `ItemsFinished`, `PagesRead`, `LongestStreakDays`, `BusiestDay`, `TopSeries`, `TopWriter`
  (including a `Book.Author` contribution — construct a `Book` directly via `ctx.Books.Add`, no existing
  `SeedBook` helper, check for one first and add a minimal local one if truly absent), `TopArtist`,
  `HighestRatedSeries`, `MostRereadItem`.
- Tie case: two series tied for `TopSeries` (or `HighestRatedSeries`) — assert `HighlightGroup.DisplayTitle`
  reads "X and 1 other".
- Year-boundary case: one event at local `Dec 31 23:59:59` and one at local `Jan 1 00:00:00` of the next
  year — assert each lands in the correct year's `Build` result and not the other's.
- Streak-does-not-cross-year-boundary: events on Dec 30/31 of year N and Jan 1/2 of year N+1 (a 4-day
  lifetime streak) — assert `Build(ctx, N, now).LongestStreakDays == 2` and
  `Build(ctx, N+1, now).LongestStreakDays == 2`, never 4.
- Empty-tile case: a year with `Finished` events but zero rereads and zero rated issues — assert
  `MostRereadItem`/`HighestRatedSeries` are `null` while `ItemsFinished` is nonzero (proves the whole build
  doesn't throw or null out unrelated fields).
- `AvailableYears`: one event in the current year → contains it; a year with zero events never appears;
  descending order with 3+ distinct years seeded.

**Depends on:** Step 1

**Verify:** `dotnet test --filter FullyQualifiedName~RecapResolverTests`

## Step 3: `RecapViewModel`

**Files:** `src/Paperbunkr.App/ViewModels/RecapViewModel.cs` (new)

**What:** Same shape as `StatsScreenViewModel` (per-key cache, `IsActive`, `Refresh()`) but keyed on year:

```csharp
public partial class RecapViewModel : ViewModelBase
{
    public RecapViewModel(IReadingEventRecorder? readingEventRecorder = null, Func<DateTime>? nowUtc = null);

    public bool IsActive { get; set; }

    [ObservableProperty] private int _selectedYear;
    [ObservableProperty] private RecapSnapshot? _snapshot;
    [ObservableProperty] private int _currentSlideIndex;

    public IReadOnlyList<int> AvailableYears { get; private set; } = Array.Empty<int>();
    public IReadOnlyList<RecapTile> Tiles { get; private set; } = Array.Empty<RecapTile>();

    public string CurrentSlideLabel => ...; // Tiles[CurrentSlideIndex].Label, "" if Tiles empty
    public string CurrentSlideValue => ...; // Tiles[CurrentSlideIndex].Value

    [RelayCommand] private void NextSlide();     // clamp at Tiles.Count - 1
    [RelayCommand] private void PreviousSlide();  // clamp at 0

    public void Refresh(); // rebuilds AvailableYears, re-resolves Snapshot for SelectedYear, rebuilds Tiles, resets CurrentSlideIndex to 0
}

public sealed record RecapTile(string Label, string Value);
```

- `_cache = new Dictionary<int, RecapSnapshot>()` per-year cache, same invalidate-on-`ReadingEventRecorded`
  pattern as `StatsScreenViewModel`'s own ctor (`StatsScreenViewModel.cs:34-44`).
- `partial void OnSelectedYearChanged(int value)` → `Refresh()` (mirrors `StatsScreenViewModel.OnRangeChanged`,
  `StatsScreenViewModel.cs:175-183`), and resets `CurrentSlideIndex` to 0.
- `partial void OnCurrentSlideIndexChanged(int value)` → raise `OnPropertyChanged(nameof(CurrentSlideLabel))`/
  `nameof(CurrentSlideValue))`.
- `BuildTiles(RecapSnapshot snap)` (private static): the fixed 9-tile order from the design doc (Items
  finished → Pages read → Longest streak → Busiest day → Top series → Top writer → Top artist →
  Highest-rated series → Most-reread item), formatting each value string (`"128"`, `"14,220"` via `"N0"`,
  `"22 days"`, `"{date:MMM d} · {pages:N0} pages"`, `HighlightGroup.DisplayTitle`, or the tile's own
  empty-state message — e.g. `"No rereads this year"` — when the source field is `null`). This same list is
  reused by `RecapPosterView` (Step 6) as its `DataContext`, so the formatting lives in exactly one place.
- `Refresh()` sets `Snapshot`, `AvailableYears` (via `RecapResolver.AvailableYears`, once per `Refresh` — cheap,
  no need to separately cache), defaults `SelectedYear` to `AvailableYears[0]` the *first* time it's called if
  `SelectedYear` is still its default `0` (i.e. the current/most-recent year with activity, not calendar year
  0), and calls `OnPropertyChanged(nameof(AvailableYears))`/`nameof(Tiles))`.
- No `IFilePickerService` dependency here — exporting needs a live `Control` reference for
  `RenderTargetBitmap`, which a view-model can't hold; Step 7 wires Export directly in `InsightsScreen`'s
  code-behind (same reasoning as `StatsScreenViewModel.ChartsChanged` existing as an event because ScottPlot
  rendering can't be data-bound either).

**Depends on:** Step 1

**Verify:** covered by Step 4's tests.

## Step 4: `RecapViewModelTests`

**Files:** `src/Paperbunkr.App.Tests/RecapViewModelTests.cs` (new)

**What:** Same conventions as `StatsScreenViewModelTests` (check that file first for the exact
`PaperbunkrDbContext.DatabasePathOverride`/`PaperbunkrDb.CreateContext()` fixture pattern before writing this).
Cases:
- `Refresh()` with seeded reading-event history populates `AvailableYears`/`Snapshot`/`Tiles` (9 entries).
- `NextSlideCommand`/`PreviousSlideCommand` clamp at `0` and `Tiles.Count - 1` (no wraparound).
- Changing `SelectedYear` re-resolves `Snapshot`/`Tiles` for the new year and resets `CurrentSlideIndex` to 0.
- A year with no data for a specific tile (e.g. no rereads) produces that tile's empty-state string, not an
  exception or a blank string.

**Depends on:** Step 3

**Verify:** `dotnet test --filter FullyQualifiedName~RecapViewModelTests`

## Step 5: Wire `Recap` into `InsightsScreenViewModel`

**Files:** `src/Paperbunkr.App/ViewModels/InsightsScreenViewModel.cs` (edit)

**What:** Add `Recap` alongside the existing `Stats` composition (`InsightsScreenViewModel.cs:62`), and extend
the Overview/Stats 2-way tab selection (`IsStatsTabSelected`, `InsightsScreenViewModel.cs:64-80`) to 3-way:

```csharp
public RecapViewModel Recap { get; }

[ObservableProperty] private bool _isRecapTabSelected;

partial void OnIsStatsTabSelectedChanged(bool value)
{
    if (value) IsRecapTabSelected = false;
    Stats.IsActive = value;
    if (value) Stats.Refresh();
    OnPropertyChanged(nameof(IsOverviewTabSelected));
}

partial void OnIsRecapTabSelectedChanged(bool value)
{
    if (value) IsStatsTabSelected = false;
    Recap.IsActive = value;
    if (value) Recap.Refresh();
    OnPropertyChanged(nameof(IsOverviewTabSelected));
}

public bool IsOverviewTabSelected => !IsStatsTabSelected && !IsRecapTabSelected;

[RelayCommand] private void SelectOverviewTab() { IsStatsTabSelected = false; IsRecapTabSelected = false; }
[RelayCommand] private void SelectStatsTab() => IsStatsTabSelected = true;
[RelayCommand] private void SelectRecapTab() => IsRecapTabSelected = true;
```

`SelectOverviewTab`'s body changes from its current single-line form (`InsightsScreenViewModel.cs:77`) to
explicitly clear both flags. Construct `Recap = new RecapViewModel(readingEventRecorder, nowUtc);` in the
constructor next to the existing `Stats = new StatsScreenViewModel(...)` line (`InsightsScreenViewModel.cs:40`).

**Depends on:** Step 3

**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`; existing `InsightsScreenViewModel`-adjacent
tests (if any reference `IsStatsTabSelected`/`SelectOverviewTabCommand` directly) still pass — grep for them
before editing to catch any test asserting the old single-bool shape.

## Step 6: `RecapPosterView` (export layout)

**Files:** `src/Paperbunkr.App/Views/RecapPosterView.axaml` (new), `src/Paperbunkr.App/Views/RecapPosterView.axaml.cs` (new)

**What:** A `UserControl` with a fixed portrait size (`Width="1080" Height="1920"`, matching a standard
shareable "story" aspect ratio), `x:DataType="vm:RecapPosterDisplayModel"` where
`RecapPosterDisplayModel(int Year, bool IsCurrentYear, IReadOnlyList<RecapTile> Tiles)` is a small new record
(same file or alongside `RecapTile` in `RecapViewModel.cs` — implementer's call) wrapping the same `Tiles`
list `RecapViewModel` already builds, so no formatting logic is duplicated between the in-app slides and the
poster.

Layout: `Year`/"so far" heading; `Tiles[0]` (Items finished) as a large headline number + label near the top;
`Tiles.Skip(1)` (the remaining 8) in a `UniformGrid Columns="2" Rows="4"` of small `Border` cells below, each
showing its `Label`/`Value`; a corner mark reusing the nav-rail logo pattern exactly
(`Image Source="avares://Paperbunkr.App/Assets/paperbunkr-logo-rail.png"`, per `MainWindow.axaml:195-198`)
plus a "paperbunkr" `TextBlock` beside it. All colors via the same `DynamicResource` skin brushes used
elsewhere on this screen (`PbSurface1Brush`/`PbAccentBrush`/`PbTextBrush`) — no fixed palette.

Per this project's Avalonia build gotcha (`CLAUDE.md` "Build gotcha: adding a new Avalonia View"), the
code-behind `.cs` (minimal `InitializeComponent()` stub) ships in the same commit as the `.axaml`, and the
subsequent build step (Step 7, which actually references `RecapPosterView` from `InsightsScreen.axaml.cs`)
should be preceded by a forced `dotnet build -t:Rebuild` (or the delete-`.dll`/`.pdb` workaround) rather than
trusting a plain incremental build's "0 Errors" if this step's own first build shows *any* XAML compile error.

**Depends on:** Step 3 (needs `RecapTile`)

**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` (using the rebuild caution above); no
automated visual test (matches the `BurnDown`/`GrowthChart` ScottPlot precedent) — visual correctness is
checked on-screen in Step 9.

## Step 7: Wire the Recap tab into `InsightsScreen.axaml`

**Files:** `src/Paperbunkr.App/Views/InsightsScreen.axaml` (edit)

**What:**
- Tab strip (`InsightsScreen.axaml:115-118`): add a third tab button, and fix the two existing ones' `Classes.active`
  bindings (currently `!IsStatsTabSelected` for Overview, which will start including the Recap-tab-selected
  state once it exists):
  ```xml
  <Button Classes="tab" Classes.active="{Binding IsOverviewTabSelected}" Content="Overview" Command="{Binding SelectOverviewTabCommand}" />
  <Button Classes="tab" Classes.active="{Binding IsStatsTabSelected}" Content="Stats" Command="{Binding SelectStatsTabCommand}" />
  <Button Classes="tab" Classes.active="{Binding IsRecapTabSelected}" Content="Recap" Command="{Binding SelectRecapTabCommand}" />
  ```
- Sibling year-picker next to the existing range-selector `ItemsControl` (`InsightsScreen.axaml:119-131`), same
  `Grid.Column="1"` cell, `IsVisible="{Binding IsRecapTabSelected}"`, `ItemsSource="{Binding Recap.AvailableYears}"`,
  each item a `Button` (or `ComboBox`) setting `Recap.SelectedYear`; the current year's entry displays
  `"{year} (so far)"` via a converter or an inline `IValueConverter` if `Recap.Snapshot?.IsCurrentYear` — simplest
  is a small `IMultiValueConverter`/plain `IValueConverter` on the year int checking against `DateTime.Now.Year`
  directly in the converter rather than plumbing `IsCurrentYear` through the button template.
- Existing Overview/Stats `ScrollViewer`s' `IsVisible` bindings (`InsightsScreen.axaml:135`, `:245`) change from
  `!IsStatsTabSelected`/`IsStatsTabSelected` to `IsOverviewTabSelected`/`IsStatsTabSelected` (the Stats one is
  unchanged in binding path, just now correctly mutually exclusive with Recap too).
- New third `ScrollViewer Grid.Row="2" IsVisible="{Binding IsRecapTabSelected}"` containing:
  - The slide viewer: a `Border Classes="card"` with `Recap.CurrentSlideLabel`/`CurrentSlideValue` `TextBlock`s
    (classes `cardHeading`/`bigNumber`, matching this screen's existing tile typography), left/right chevron
    `Button`s bound to `Recap.PreviousSlideCommand`/`NextSlideCommand`, and a dot row (`ItemsControl` over
    `Recap.Tiles` rendering a small circle per tile, highlighted when its index equals `Recap.CurrentSlideIndex`
    — simplest as an `ItemsControl` binding each dot's fill via a converter comparing its own index, or a tiny
    per-item view-model if that proves awkward in pure XAML).
  - An "Export" `Button` (icon + text) with `Click="OnExportPosterClick"` (code-behind, not a VM command — see
    Step 3's rationale) rather than a bound `Command`.
  - `<Panel x:Name="PosterExportHost" Opacity="0" IsHitTestVisible="False" />` — the always-present, empty,
    invisible host `RecapPosterView` gets added to only for the duration of an export (design doc's Export
    section).

**Depends on:** Steps 5, 6

**Verify:** `dotnet build`; manual/on-screen only for actual appearance (Step 9).

## Step 8: Export click handler in `InsightsScreen.axaml.cs`

**Files:** `src/Paperbunkr.App/Views/InsightsScreen.axaml.cs` (edit)

**What:** Add an `async void OnExportPosterClick(object? sender, RoutedEventArgs e)` handler (matches this
codebase's existing precedent of code-behind owning anything ScottPlot/rendering can't bind to). Shape:

```csharp
private async void OnExportPosterClick(object? sender, RoutedEventArgs e)
{
    if (_subscribed?.Recap.Snapshot is not { } snap) return;

    string? path = await new FilePickerService().PickSaveFileAsync(
        "Export Year in Review", $"paperbunkr-recap-{snap.Year}.png", "png", "PNG Image");
    if (path is null) return;

    var poster = new RecapPosterView { DataContext = new RecapPosterDisplayModel(snap.Year, snap.IsCurrentYear, _subscribed.Recap.Tiles) };
    PosterExportHost.Children.Add(poster);
    try
    {
        var size = new Avalonia.Size(1080, 1920);
        poster.Measure(size);
        poster.Arrange(new Avalonia.Rect(size));
        var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(1080, 1920));
        bitmap.Render(poster);
        bitmap.Save(path);
    }
    catch (Exception ex)
    {
        // surfaced via Activity Center per this project's standing notification rule - implementer:
        // find the existing "report a background failure" call other Insights-adjacent code uses
        // (grep ActivityCenter/Activity usages in a sibling ViewModel) rather than a raw dialog.
    }
    finally
    {
        PosterExportHost.Children.Remove(poster);
    }
}
```

`new FilePickerService()` inline matches `ReaderScreenViewModel.SavePageAsAsync`'s own existing precedent
(`ReaderScreenViewModel.cs:547`) for a one-off export path with no test-fake need at the VM layer (the actual
render+save logic here isn't unit-testable anyway — see Step 6's note — so there's no DI benefit to threading
`IFilePickerService` through the constructor just for this one code-behind call). Before writing the
catch-block's error path for real, grep this codebase for how `Activity`/Activity Center reports a failed
background action from a screen's code-behind (`InsightsScreenViewModel`'s constructor already takes no
`Activity` reference today, so this may need one threaded in, or may reuse a simpler existing toast/error
surface if Activity Center turns out to be VM-only) — resolve this against the real API during implementation
rather than the sketch above.

**Depends on:** Step 7

**Verify:** `dotnet build`; manual on-screen check (Step 9) — no automated test for the render/save path
itself (consistent with Step 6).

## Step 9: On-screen verification (manual)

Not automatable. With a library that has `ReadingEvent` history spanning at least one full calendar year
(or the current partial year): open Insights → Recap, confirm the year picker lists the right years including
"{year} (so far)" for the current one, page through all 9 slides with the chevrons and keyboard arrows,
confirm empty-state tiles render their message rather than blank/crashing for a thin-data year, click Export,
confirm the save dialog appears and the resulting PNG looks correct (headline number, 2×4 grid, corner mark,
skin colors matching whatever skin is currently active). This mirrors the pattern already used for the
`BurnDown` chart in slice 2 — note explicitly if the available library's history is too thin to exercise every
tile's populated (non-empty-state) path, same caveat slice 1/2 hit.

## Notes for the implementer

- No migration, no `ScheduledTaskCatalog` change, no `PaperbunkrDbContext` change — this slice reads existing
  tables only.
- `RecapResolver`/`StatsResolver` intentionally stay separate files/classes per the design doc's rationale —
  don't fold this into `StatsResolver.Build` even though it's tempting for symmetry.
- The Export flow's `Activity`-reporting detail in Step 8 is deliberately left slightly open — resolve it
  against the real `Activity`/Activity Center API during implementation rather than guessing its shape here.
