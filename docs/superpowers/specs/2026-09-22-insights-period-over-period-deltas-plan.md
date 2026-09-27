# Insights pitch slice 1 — period-over-period deltas + sparklines — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-22-insights-period-over-period-deltas-design.md*

Three Stats-tab tiles (Finished · range, Pace, Avg to finish) gain a delta badge + sparkline, plus a real bug
fix to `AvgDaysToComplete` (currently ignores the range selector entirely). No schema change.

## Step 1: Fix `ComputeReadingActivity` and add the windowed/trend compute layer

**Files:** `src/Paperbunkr.Data/Metadata/StatsResolver.cs` (edit)

**What:**
- Add a private `RealSpan`-filtering step so `AvgDaysToComplete` only averages spans whose `FinishedUtc`
  falls at or after the range's start (`RangeStartUtc(range, nowUtc)`), matching how `inRange` events are
  already filtered. For `AllTime` (`RangeStartUtc` returns `null`), behavior is unchanged (no filter).
- Add two new records near the other `StatsSnapshot`-adjacent records:
  ```csharp
  public sealed record TrendData(TileTrend? FinishedItems, TileTrend? AvgIssuesPerDay, TileTrend? AvgDaysToComplete);
  public sealed record TileTrend(double? PercentChange, bool ShowNew, bool IsGoodDirection, IReadOnlyList<double> SparklinePoints);
  ```
- Add `StatsSnapshot.Trend` (`TrendData?`) as a new trailing parameter/property on the existing record.
- Add `private static TrendData? ComputeTrend(List<ReadingEvent> events, IReadOnlyList<RealSpan> realSpans, InsightsRange range, DateTime nowUtc, FinishedInRange currentFinished, ReadingActivityData currentActivity)`:
  - Returns `null` immediately for `InsightsRange.AllTime` (`RangeStartUtc` is null — no prior window is meaningful).
  - `rangeStart = RangeStartUtc(range, nowUtc)!.Value`; `windowLength = nowUtc - rangeStart`; `priorStart = rangeStart - windowLength`; `priorEnd = rangeStart`.
  - Insufficient-history guard: if there are no events, or `events.Min(e => e.TimestampUtc) > priorStart`, return `null` (not enough history to cover a full prior window — same visual as today, no badge on any of the three tiles).
  - `priorEvents = events.Where(e => e.TimestampUtc >= priorStart && e.TimestampUtc < priorEnd).ToList()`.
  - `priorFinished = ComputeFinishedInRange(priorEvents)` (reused as-is).
  - `priorRealSpans = realSpans.Where(s => s.FinishedUtc >= priorStart && s.FinishedUtc < priorEnd).ToList()`.
  - `priorAvgDays = priorRealSpans.Count > 0 ? priorRealSpans.Average(s => s.Days) : 0`.
  - `priorAvgPerDay = priorFinished.Items / Math.Max(1, windowLength.TotalDays)`.
  - Extract the existing `ComputePace` week/month bucket-boundary loop into a small shared private helper (e.g. `BucketBoundaries(range, nowUtc)` returning `IReadOnlyList<(DateTime Start, DateTime End, string Label)>` for the *current* range only) so `ComputePace` and the new sparkline-point builder below share it instead of duplicating the weekly/monthly branching.
  - Build each tile via a shared local:
    ```csharp
    TileTrend? Build(double current, double prior, bool lowerIsBetter, IReadOnlyList<double> sparkline)
    {
        if (prior <= 0 && current <= 0) return null; // nothing happened in either window
        if (prior <= 0) return new TileTrend(null, ShowNew: true, IsGoodDirection: !lowerIsBetter, sparkline);
        double pct = (current - prior) / prior * 100.0;
        bool isGood = lowerIsBetter ? pct <= 0 : pct >= 0; // flat/no-change reads as neutral-good, not red
        return new TileTrend(pct, ShowNew: false, IsGoodDirection: isGood, sparkline);
    }
    ```
  - Sparkline points per bucket (current range's buckets, from the extracted boundary helper): finished-count for `FinishedItems`, finished-count / bucket-length-in-days for `AvgIssuesPerDay`, average `RealSpan.Days` for spans finished in that bucket (0 if none) for `AvgDaysToComplete`.
  - `FinishedItems`: `Build(currentFinished.Items, priorFinished.Items, lowerIsBetter: false, financeSparkline)`.
  - `AvgIssuesPerDay`: `Build(currentActivity.AvgIssuesPerDay, priorAvgPerDay, lowerIsBetter: false, paceSparkline)`.
  - `AvgDaysToComplete`: `Build(currentActivity.AvgDaysToComplete, priorAvgDays, lowerIsBetter: true, avgDaysSparkline)`.
- Wire `Trend: ComputeTrend(events, realSpans, range, nowUtc, ComputeFinishedInRange(inRange), readingActivity)` into `Build`'s returned `StatsSnapshot` (compute `ComputeReadingActivity(...)` once into a local first, since it's now needed both for the snapshot and for `ComputeTrend`).

**Depends on:** none

**Verify:** `dotnet test --filter FullyQualifiedName~StatsResolverTests` (see Step 3 for new cases).

## Step 2: `TrendSparkline` control

**Files:** `src/Paperbunkr.App/Views/Stats/TrendSparkline.cs` (new)

**What:** Mirror `ActivityHeatmap`/`CategoryDonut`'s exact shape (same folder, `Control` subclass):
- `public static readonly StyledProperty<IReadOnlyList<double>> DataProperty` (default `Array.Empty<double>()`), `AffectsRender`/`AffectsMeasure`.
- `MeasureOverride`: return a small fixed-height strip (e.g. `new Size(availableSize.Width is double.IsInfinity ? 80 : availableSize.Width, 20)` — width stretches to fill the tile, height fixed).
- `Render(DrawingContext)`: if fewer than 2 points, draw nothing (a flat/single-point sparkline isn't informative). Otherwise normalize `Data` to the control's bounds (min/max of the series maps to bottom/top, with a small vertical inset so the line doesn't clip) and draw a polyline through the points via a `StreamGeometry` (open figure, `IsFilled: false`), stroked with a brush resolved via `this.TryFindResource("PbAccentBrush", ...)` with a hardcoded fallback color — same `ResolveBrush` helper shape as the other two controls.
- No hover/tooltip (per the design doc — decorative only for this slice).

**Depends on:** none (independent of Step 1)

**Verify:** covered by Step 4's tests.

## Step 3: `StatsResolver` tests

**Files:** `src/Paperbunkr.Data.Tests/StatsResolverTests.cs` (edit)

**What:** Add cases (using the existing `InsightsResolverTests.SeedSeries`/`SeedIssue`/`SeedEvent` helpers and the file's existing `Now` constant):
- **Regression:** seed a Finished event inside the last 30 days but outside the last 30-90 day boundary isn't quite right — instead: seed spans such that `AvgDaysToComplete` differs between `InsightsRange.Days30` and `InsightsRange.Months12` snapshots for the same seeded data, proving the fix (today, both would report the same lifetime-wide number; asserting they now differ closes the bug).
- **Trend, Finished/Pace tiles:** seed enough Finished events in both the current 90d window and the prior 90d window (i.e. reaching back before day -90) that `PercentChange` is computable; assert the sign and rough magnitude for both `Trend.FinishedItems` and `Trend.AvgIssuesPerDay`.
- **ShowNew:** seed events only in the current window, none in the prior one, with history reaching back far enough to not trip the insufficient-history guard (e.g. one very old event, then nothing until the current window) — assert `ShowNew == true` and `PercentChange == null`.
- **Both-zero:** a range/window pair with no events in either — assert the relevant `TileTrend` is `null` (no badge).
- **Insufficient history:** seed events only within the last 20 days, request `InsightsRange.Days30` (prior window would need history back to day -60) — assert `snap.Trend == null`.
- **All time:** any seed, `InsightsRange.AllTime` — assert `snap.Trend == null` unconditionally.
- **Direction:** a case where `AvgDaysToComplete` improves (goes down) — assert `IsGoodDirection == true` on that tile despite `PercentChange` being negative, and a Finished-items increase — assert `IsGoodDirection == true` with positive `PercentChange`.

**Depends on:** Step 1

**Verify:** `dotnet test --filter FullyQualifiedName~StatsResolverTests`

## Step 4: `TrendSparkline` tests

**Files:** `src/Paperbunkr.App.Tests/InsightsChartPolishTests.cs` (edit — add cases alongside the existing `CategoryDonut` ones, same file/class)

**What:** Construct `new TrendSparkline()` directly (no headless render pipeline), set `Data`, assert on public/measurable state rather than pixels — e.g. `MeasureOverride`'s returned height is fixed regardless of point count, and that setting fewer than 2 points doesn't throw on `Render` (call `Render` against a throwaway `DrawingContext` the way, if any, existing tests in this file do — otherwise limit to construction/property-level assertions matching the `Donut_WithNoMotionResource_...` style).

**Depends on:** Step 2

**Verify:** `dotnet test --filter FullyQualifiedName~InsightsChartPolishTests`

## Step 5: `StatsScreenViewModel` — trend display properties

**Files:** `src/Paperbunkr.App/ViewModels/StatsScreenViewModel.cs` (edit)

**What:**
- Add `public sealed record TrendDisplay(bool IsUp, string Text, bool IsGood, IReadOnlyList<double> Sparkline);` near the other small view-model records at the bottom of the file (`RangeOption`, etc.).
- Add three `[ObservableProperty] private TrendDisplay? _finishedTrend/_paceTrend/_avgToFinishTrend;`.
- Add a private static `ToDisplay(TileTrend? t)`:
  ```csharp
  private static TrendDisplay? ToDisplay(TileTrend? t)
  {
      if (t is null) return null;
      if (t.ShowNew) return new TrendDisplay(IsUp: true, "new", t.IsGoodDirection, t.SparklinePoints);
      double pct = t.PercentChange ?? 0;
      return new TrendDisplay(IsUp: pct >= 0, $"{Math.Abs(pct):0}%", t.IsGoodDirection, t.SparklinePoints);
  }
  ```
- In `Refresh()`, after `Snapshot = snap;`, set `FinishedTrend = ToDisplay(snap.Trend?.FinishedItems); PaceTrend = ToDisplay(snap.Trend?.AvgIssuesPerDay); AvgToFinishTrend = ToDisplay(snap.Trend?.AvgDaysToComplete);` (these are `[ObservableProperty]`-backed, so no manual `OnPropertyChanged` name needed).

**Depends on:** Step 1 (needs `TileTrend`/`StatsSnapshot.Trend` to exist)

**Verify:** existing `StatsScreenViewModelTests.cs` conventions — add a case seeding a resolver-level scenario (or faking `Snapshot` via the same pattern existing tests use) asserting `FinishedTrend`/`PaceTrend`/`AvgToFinishTrend` populate and clear correctly across a `Refresh()`.

## Step 6: Wire into `InsightsScreen.axaml`

**Files:** `src/Paperbunkr.App/Views/InsightsScreen.axaml` (edit)

**What:** In the "READING ACTIVITY" `Grid` (the row with `Grid.Column="0"`..`"5"` cards), extend the three target cards (`Finished · range` at `Grid.Column="3"`, `Avg to finish` at `Grid.Column="4"`, `Pace` at `Grid.Column="5"`) with, inside each card's `StackPanel`, after the existing `sub` `TextBlock`:
```xml
<StackPanel Orientation="Horizontal" Spacing="3" IsVisible="{Binding Stats.FinishedTrend, Converter={x:Static conv:ObjectConverters.IsNotNull}}">
    <fi:SymbolIcon Symbol="ArrowUp" FontSize="{StaticResource PbIconSizeXs}" IsVisible="{Binding Stats.FinishedTrend.IsUp}"
                    Foreground="{Binding Stats.FinishedTrend.IsGood, Converter=...}"/>
    <fi:SymbolIcon Symbol="ArrowDown" FontSize="{StaticResource PbIconSizeXs}" IsVisible="{Binding !Stats.FinishedTrend.IsUp}"
                    Foreground="{Binding Stats.FinishedTrend.IsGood, Converter=...}"/>
    <TextBlock Classes="sub" Text="{Binding Stats.FinishedTrend.Text}" />
</StackPanel>
<stats:TrendSparkline Height="20" Data="{Binding Stats.FinishedTrend.Sparkline}" IsVisible="{Binding Stats.FinishedTrend, Converter={x:Static conv:ObjectConverters.IsNotNull}}" />
```
repeated for `Stats.AvgToFinishTrend` and `Stats.PaceTrend` on their respective cards. For the `Foreground` bool→brush swap, follow whatever existing bool-to-brush converter (if any) `Styles/` already defines; if none exists, use two overlapping icons with hardcoded `PbSuccessBrush`/`PbDangerBrush` `Foreground` and `IsVisible` on each (`{Binding Stats.FinishedTrend.IsGood}` / `{Binding !Stats.FinishedTrend.IsGood}`), matching the two-icon-swap idiom already used for `ChevronUp`/`ChevronDown` pairs elsewhere in this codebase (e.g. `LibrarySection.axaml:290-291`) rather than introducing a new converter. Confirm `xmlns:conv="using:Avalonia.Data.Converters"` is already declared on the root `UserControl` (used elsewhere in the app for `ObjectConverters.IsNotNull`); add it if this file doesn't have it yet.

**Depends on:** Steps 1, 5

**Verify:** manual/on-screen only — launch the app (`run` skill or `dotnet run`) with a library that has reading history spanning more than one range window, open Insights → Stats, and check all four range selectors (30d/90d/12mo/All time): the three badges+sparklines appear correctly for 30d/90d/12mo, disappear for All time, and the arrow/color direction matches Q8's per-tile rule (fewer days = green down-arrow on Avg to finish).

## Notes for the implementer

- `Finished · range`'s `PercentChange` and `Pace`'s `PercentChange` will very often be numerically identical (both derive from the same finished-count-in-window numerator, one raw and one divided by a shared window length) — this is expected, not a bug to chase.
- The bug fix in Step 1 changes `AvgDaysToComplete`'s *displayed number* for anyone with reading history, on every range except All time. This is intentional (see design doc).
