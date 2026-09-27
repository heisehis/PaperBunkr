# Insights pitch, slice 1 — period-over-period deltas + sparklines (#6) — design

Date: 2026-09-22. Source: "Insights pitch" item #6 in `docs/Paperbunkr-Roadmap.md` (10-item idea capture,
added 2026-09-21). Status: **approved, not yet implemented.**

## Scope

The Insights pitch is 5 items now in scope (#6, #4's snapshot table, #1, #2, #9 — the last added back into
scope during this brainstorm). Too much for one spec; decomposed into a delivery queue, each getting its own
design cycle:

1. **#6 — period-over-period deltas + sparklines (this spec)**
2. #4's snapshot table (nightly library-state snapshot; foundation for backlog burn-down and other trend charts)
3. #1 — year-in-review recap
4. #2 — reading goals/challenges
5. #9 — recommendations surface (UI for the existing backend-only Phase 6a engine, `RecommendationResolver`)

This spec covers **#6 only**. No schema change, no migration.

**CE parity:** ComicRack CE has no Insights/Stats screen at all (Paperbunkr's own addition, 2026-09-05). This
is a deliberate deviation; no CE behavior to match.

## What gets a delta + sparkline

The Stats tab's "Reading Activity" row has six tiles. Three are genuinely range-scoped scalars and are the
only ones in scope here:

- **Finished · range** (`FinishedInRange.Items`)
- **Pace** (`ReadingActivityData.AvgIssuesPerDay`)
- **Avg to finish** (`ReadingActivityData.AvgDaysToComplete`) — see bug fix below

**Read all time**, **Day streak**, and **Finish streak** are lifetime running values with no "prior period" to
compare against and are untouched.

The delta/sparkline attaches to each tile's primary `bigNumber` value only — the secondary `sub` line (e.g.
Finished · range's page count) doesn't get its own trend.

## Bug fix — `ComputeReadingActivity`'s `AvgDaysToComplete` ignores `range`

`StatsResolver.ComputeReadingActivity` currently averages `realSpans` (every completed open→finish journey,
ever) with no range filter — `inRange` is only used for `Pace`'s numerator. The result: "Avg to finish" never
changes when you switch the range selector, even after reading comics inside that window. This was found
during this brainstorm and confirmed against the user's own experience (comics read recently don't move the
number).

**Fix:** filter `realSpans` to those whose `FinishedUtc` falls at or after the range's start (same boundary
`inRange` events already use via `RangeStartUtc`) before averaging. For `InsightsRange.AllTime`,
`RangeStartUtc` returns `null`, so no filter is applied — All time's value is unchanged by this fix. This is a
one-parameter query change, no schema impact; it does change the *displayed* number for 30d/90d/12mo ranges
for anyone with reading history, which is the point.

## Data layer — extend `StatsResolver.Build`

Add a "prior window" pass alongside the existing current-range computation: same length as the selected
range, immediately preceding it (`[rangeStart - length, rangeStart)`). Re-run
`ComputeFinishedInRange`/`ComputePace`/`ComputeReadingActivity` (post-fix) against that slice of the
already-loaded `events`/`realSpans` lists — no new database query.

```csharp
public sealed record TrendData(
    TileTrend? FinishedItems,
    TileTrend? AvgDaysToComplete,
    TileTrend? AvgIssuesPerDay);

public sealed record TileTrend(
    double? PercentChange,   // null when ShowNew is true, or when the tile is hidden (see below)
    bool ShowNew,            // prior-window baseline was 0, current > 0 -> render "new" instead of a percent
    bool IsGoodDirection,    // true = render the success brush, false = danger. FinishedItems/AvgIssuesPerDay:
                              // true when PercentChange > 0 (or ShowNew). AvgDaysToComplete: true when
                              // PercentChange < 0 (fewer days is the improvement) - inverted, never both "up=good".
    IReadOnlyList<double> SparklinePoints);
```

`StatsSnapshot.Trend` is a new nullable property, `null` for `InsightsRange.AllTime` (no meaningful "prior
period" to compare a lifetime figure against). Each individual `TileTrend` is also `null` (not just its
`PercentChange`) when the prior window's start falls before the earliest `ReadingEvent.TimestampUtc` on
record — insufficient history to make a fair comparison — in which case the view shows the tile exactly as it
renders today, no badge, no sparkline.

**Zero-baseline:** when the prior window's value is 0 and the current value is greater than 0, `ShowNew` is
true and the badge reads "new" instead of a percentage (avoids a divide-by-zero / infinite percent).

**Sparkline points:** reuse `ComputePace`'s existing weekly/monthly bucket-boundary logic (the loop that picks
week-start or month-start boundaries depending on `range`), extracted into a small shared private helper so it
isn't duplicated. Points are: per-bucket finished-count for the Finished tile, per-bucket issues/day for Pace,
per-bucket average days-to-complete (over spans finished in that bucket) for Avg to finish. Sparklines cover
the *current* range's buckets only (not the prior window) — they show the shape of the current period, the
badge shows how it compares to the prior one.

## UI

Each of the three tile `Border.card`s in the "Reading Activity" `Grid` (`InsightsScreen.axaml`) gains:

- A small delta badge (▲/▼ + percent, or "new") next to the existing `bigNumber` text, colored via
  `TileTrend.IsGoodDirection` (success/danger brush) — or omitted entirely when `TrendData`/that tile's
  `TileTrend` is null.
- A thin sparkline strip below the existing `sub` text, using a new `TrendSparkline : Control` in
  `Views/Stats/`.

`TrendSparkline` follows the exact pattern `Views/Stats/ActivityHeatmap.cs` and `CategoryDonut.cs` already
use: a `StyledProperty<IReadOnlyList<double>> DataProperty` with `AffectsRender`/`AffectsMeasure`, a
`Render(DrawingContext)` override drawing a simple polyline (`StreamGeometry` or repeated `DrawLine` calls),
brush/color resolved via `this.TryFindResource(key, ...)` against the app's skin tokens with a hardcoded
fallback — **not** `InsightsChartTheme` (that's ScottPlot-`Color`-specific, used by the two full-size bar
charts) and **not** ScottPlot itself (too heavy for a tiny inline strip). Decorative only for this slice — no
hover tooltip; `InsightsChartHover`'s bar-hit-testing doesn't fit a polyline and isn't reused here.

## Testing

- `StatsResolverTests`: prior-window delta cases for all three tiles, including the zero-baseline "new" case,
  the insufficient-history "hidden" case, and the All-time "no Trend at all" case.
- A regression test proving `AvgDaysToComplete` now actually changes when `range` changes (reproducing the bug
  found in this brainstorm, then asserting the fix).
- The extracted bucket-boundary helper gets its own unit tests, same treatment `ComputePace`'s existing bucket
  logic gets today.
- `TrendSparkline`: property/state-level tests in the same style `InsightsChartPolishTests` already uses for
  `CategoryDonut` — construct the control directly, set `Data`, assert on its public state (e.g. computed
  min/max or point count) rather than pixel output, no headless render pipeline needed.

## Out of scope for this slice

Items #4 (nightly snapshot table), #1 (year-in-review recap), #2 (reading goals/challenges), and #9
(recommendations surface) stay idea-captured in the roadmap as queued future slices, in that order — each
gets its own brainstorm → design pass when picked up.
