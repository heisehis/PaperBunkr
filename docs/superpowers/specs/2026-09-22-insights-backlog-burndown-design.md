# Insights pitch, slice 2 — nightly library snapshot + Backlog burn-down (#4) — design

Date: 2026-09-22. Source: "Insights pitch" item #4 in `docs/Paperbunkr-Roadmap.md` (10-item idea capture,
added 2026-09-21), decomposed into a 5-item queue during slice 1's brainstorm
(docs/superpowers/specs/2026-09-22-insights-period-over-period-deltas-design.md's Scope section). Status:
**approved, not yet implemented.**

## Scope

Slice 2 of 5. Builds both the nightly snapshot infrastructure *and* the Backlog burn-down card that
consumes it (expanded from the original "infrastructure only" recommendation during this brainstorm - the
card ships now, showing its own empty state until there's enough history). No change to slice 1 (#6,
already shipped). Items #1 (year-in-review), #2 (goals/challenges), #9 (recommendations surface) remain
queued, each getting its own design pass when picked up.

**CE parity:** ComicRack CE has no Insights/Stats screen and no historical snapshotting of any kind -
deliberate deviation, no CE behavior to match.

## Data — `LibrarySnapshot` entity + migration

```csharp
public class LibrarySnapshot
{
    public int Id { get; set; }
    public DateOnly SnapshotDate { get; set; }   // local calendar day, one row per day
    public int TotalOwnedComics { get; set; }
    public int BacklogComics { get; set; }
}
```

One row per **local calendar day**. Scope, matching existing precedent rather than inventing a new
definition:
- **Comics only.** Books (EPUB/PDF) track completion via `Book.Finished` (a bool, no percentage) - folding
  them into "backlog" needs its own not-started/in-progress distinction Books don't have today. Out of
  scope for this slice; a natural follow-up once this lands and is checked against comics.
- **Local library only** - `Issue.RemoteSourceId == null`. Matches `StatsResolver`'s own existing,
  commented precedent: "Library-SIZE figures ... still use only the local issues - a remote series is not
  something you own."
- **Backlog = `ReadPercentage == 0`** (never opened at all - `Issue.LastPageRead`/`PageCount`, the same
  pair `IssueReadStateResolver`/`ReadPercentage` already use elsewhere). Not `Series.ReadingStatus`: a
  series can be "Reading" while still holding 30 unread issues, which the series-level cataloging status
  doesn't capture but per-issue read state does.
- **Never pruned** - same append-forever philosophy `ReadingEvent`'s own doc comment already states for
  the same reason (long-range history for Insights). At ~1 row/day this is trivial storage.

**Upsert, not blind insert.** The Automation tab has a per-task "Run now" button, so a task can run twice
in one calendar day even though `ScheduleMode.DailyAt`'s own due-logic normally prevents that. Writing
must find-or-create today's `SnapshotDate` row and overwrite its counts, never append a duplicate.

Migration: adds the `LibrarySnapshots` table. No existing table changes.

## Write side — `LibrarySnapshotService`

New file `src/Paperbunkr.App/Services/LibrarySnapshotService.cs` - lives in `App/Services`, matching where
the closest analogous "record-writer" (`ReadingEventRecorder`) already lives, not `Paperbunkr.Data`, even
though it only touches the database.

```csharp
public class LibrarySnapshotService
{
    public void Capture(PaperbunkrDbContext context) // upserts today's row from current Issues state
}
```

Synchronous, like `BackupService.BackupNow()` - the catalog entry's `RunAsync` wraps it in `Task.Run(...)`
the same way `db-backup`'s own descriptor already does, rather than the method itself being async.

Wired as `ScheduledTaskCatalog`'s 14th entry (`library-snapshot`):
- **`ScheduleMode.DailyAt`** - the scheduler already has this mode (`SchedulerDueLogic.EvaluateDailyAt`,
  "once per calendar day, at/after a configured local time") but no existing task uses it; every one of
  the current 13 entries uses `ScheduleMode.Interval`. This is that mode's first real consumer. Default
  time: a fixed off-peak local time (e.g. 03:00), same as any other `DailyAt` seed would need - editable
  afterward from the Automation tab like every other task's schedule.
- **`ActivityJobKind.Other`** - matches `db-backup`'s own use of `Other` for a similarly small, safe,
  no-visible-side-effect job; none of the more specific kinds (`LibraryScan`, `SyncMetadata`, etc.) fit a
  pure count-and-write.
- **`DefaultEnabled: true`** - same reasoning as `db-backup` (cheap, safe, nothing surprising happens),
  unlike the scan/organize tasks that default off because they can add files or move things around.

A day the app never opens on is simply a gap in the snapshot history - no catch-up/backfill, identical to
how every other `ScheduledTaskCatalog` entry already behaves (`SchedulerDueLogic`'s own doc comment: "a
long gap produces exactly one run").

## Read side — `BurnDownData` on `StatsSnapshot`

New records in `StatsResolver`:

```csharp
public sealed record BurnDownData(
    IReadOnlyList<BurnDownPoint> Points,           // real snapshot history within the selected range
    IReadOnlyList<BurnDownPoint>? ProjectedPoints, // 2-point dashed continuation, or null (see below)
    DateOnly? ProjectedClearDate,
    bool HasEnoughHistory,
    bool IsCleared);

public sealed record BurnDownPoint(DateOnly Date, int BacklogCount);
```

Computed in `StatsResolver.Build` by reading a new `context.LibrarySnapshots` set (parallel to how
`context.ReadingEvents` is already read). Two independent windows are in play here, deliberately not the
same thing:
- **`Points`** (what's drawn) is filtered to the *selected chart range* (30d/90d/12mo/All time), the same
  way every other chart on this screen windows by range.
- **The slope/projection** always looks back over the **last 30 real calendar days of snapshots** (or all
  available history if there's less than 30 days), *regardless of the selected chart range* - "current
  pace" means current pace even when you've zoomed the chart out to 12 months to see the longer trend.

Evaluated in this precedence order:
1. **`IsCleared`** - true whenever the *latest* snapshot's `BacklogComics` is 0, independent of
   `HasEnoughHistory`. This is a statement about right now, not a trend claim, so it isn't gated behind a
   history minimum - a library that started at zero backlog deserves the positive state from day one, not
   an empty-state placeholder. Rendered like the Overview tab's existing "Nothing in progress" checkmark
   card (`ReadingAllClear`/`CheckmarkCircle` idiom in `InsightsScreen.axaml`).
2. **`HasEnoughHistory`** - false when there are fewer than **7 days** of `LibrarySnapshot` rows ever
   recorded (not just within the current range) *and* `IsCleared` is false. Below that, the whole card
   shows the empty state, not a 2-3-point squiggle - a deliberate simplification of the "hide only the
   projection" framing floated earlier in the brainstorm: the whole card gates on one threshold, matching
   `ActivityHeatmap`'s existing all-or-nothing empty-state precedent instead of a partial-chart state
   nothing else on this screen does.
3. Otherwise (not cleared, enough history): a simple linear-regression slope over the 30-day lookback
   above, day-index as x, `BacklogComics` as y.
   - **Slope ≥ 0** (flat or growing): `ProjectedPoints` and `ProjectedClearDate` are both null. The card
     shows "Not currently trending down" instead of a date - never an infinite or negative projection.
   - **Slope < 0**: `ProjectedClearDate` is the *latest snapshot's* `BacklogComics` extrapolated forward
     from the *latest snapshot's* date (`latestBacklog / -slope` days); `ProjectedPoints` is exactly two
     points - the latest real point and `(ProjectedClearDate, 0)` - the dashed line's two endpoints.

## UI

New card next to "Library growth" on the Stats tab (`InsightsScreen.axaml`/`.axaml.cs`), same tier as the
other full-size trend charts there (not a `TrendSparkline` - this is a real chart with its own axes and a
projected continuation):

- `<sp:AvaPlot x:Name="BurnDownChart" IsVisible="{Binding Stats.HasBurnDownData}" />`, drawn imperatively in
  code-behind on `ChartsChanged`, mirroring `GrowthChart`'s own build method exactly: `plot.Add.Scatter(xs,
  ys)` for `Points` (solid, `LineWidth = 2`, `MarkerSize = 0`, matching `GrowthChart`'s own series style), a
  second `plot.Add.Scatter(...)` for `ProjectedPoints` when non-null with `LinePattern =
  ScottPlot.LinePattern.Dashed` (verified real API on `ScottPlot.Plottables.Scatter` in the installed
  5.1.59) and no marker/legend entry.
- A text line under the chart bound to a small computed view-model property: "Projected clear: `{date}`" /
  "Not currently trending down" / the cleared checkmark state / the empty-state message - four mutually
  exclusive states mapped from `BurnDownData`.
- Empty state (`!Stats.HasBurnDownData`, i.e. `!(IsCleared || HasEnoughHistory)` per the precedence order
  above): "Not enough history yet - check back in a few days." overlaid the same way `ActivityHeatmap`'s
  `!Stats.HasHeatmapData` `TextBlock` already is.
- Reuses the Stats tab's existing range selector (30d/90d/12mo/All time) exactly like every other chart on
  this screen - the chart windows into however much snapshot history falls in the selected range.

## Testing

- `LibrarySnapshotServiceTests` (new): upsert overwrites same-day row instead of duplicating; counts
  exclude remote issues; counts exclude backlog for issues with `ReadPercentage > 0`; empty library
  produces a zeroed row without throwing.
- `SchedulerDueLogicTests`/`ScheduledTaskCatalogTests` (existing files, if present, else inline near the
  scheduler's own test suite): the new `library-snapshot` entry's `DailyAt` due/next-run behavior - this is
  the first real catalog entry to exercise `DailyAt`, so also a regression check that the pre-existing but
  previously-unused code path actually works end to end.
- `StatsResolverTests`: `BurnDownData` cases for empty/below-7-day, cleared (backlog hits 0), trending down
  (slope < 0, projected date lands where expected), flat/growing (slope ≥ 0, no projection), and a
  same-day-upserted-twice case producing exactly one row.
- A new EF migration test, matching this project's existing per-migration test convention.

## Out of scope for this slice

Items #1 (year-in-review recap), #2 (reading goals/challenges), #9 (recommendations surface) stay queued.
Book backlog tracking (see Data section above) is a plausible follow-up once comics-only is shipped and
checked, but isn't scoped here.
