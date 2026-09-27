# Insights pitch slice 2 — nightly snapshot + Backlog burn-down — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-22-insights-backlog-burndown-design.md*

Verified against the real codebase: `ScheduledTaskDescriptor` has no `DailyAtMinutes` field of its own -
`ScheduledRunStore` already hardcodes `DailyAtMinutes = 3 * 60` (3:00 AM) whenever *any* task's state row is
first seeded, `DailyAt`-mode or not. So the new catalog entry needs no extra seeding code - it gets the
3:00 AM default for free the same way every other task already does. `SchedulerService.RunNowAsync(taskId)`
is generic over `ScheduledTaskCatalog.All`, so the Automation tab's "Run now" button works on the new task
automatically too - confirming the design doc's same-day-rerun/upsert requirement is real, not hypothetical.
`IssueMetadataExtensions.IsUnread(this Issue)` already exists (`ReadPercentage() == 0`) - reuse it directly,
don't reimplement the predicate.

## Step 1: `LibrarySnapshot` entity + migration

**Files:** `src/Paperbunkr.Data/Entities/LibrarySnapshot.cs` (new), `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit)

**What:** New entity:
```csharp
public class LibrarySnapshot
{
    public int Id { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int TotalOwnedComics { get; set; }
    public int BacklogComics { get; set; }
}
```
Add `public DbSet<LibrarySnapshot> LibrarySnapshots => Set<LibrarySnapshot>();` next to `ReadingEvents` in
`PaperbunkrDbContext`. Then scaffold the migration with the real EF Core CLI (don't hand-write it -
`AddReadingEventLog.cs`/`.Designer.cs` is the closest precedent for a plain new-table migration, but let the
tool generate it against the actual current model snapshot to avoid drift):
```bash
dotnet ef migrations add AddLibrarySnapshots --project src/Paperbunkr.Data --startup-project src/Paperbunkr.App
```
Add a unique index on `SnapshotDate` (one row per day is a real invariant, not just convention - catch it at
the schema level). This is the first entity in the codebase to use `DateOnly` as a stored column; EF Core
10's SQLite provider supports it natively (stored as TEXT), but sanity-check the generated migration's
column type looks right before moving on.

**Depends on:** none

**Verify:** the migration applies cleanly (`Paperbunkr.Data.Tests`' own migration-test convention - see any
recent `Add*MigrationTests.cs` for the pattern, e.g. `AddReadingEventLogMigrationTests.cs` if analogous, or
add a new one following that shape); `dotnet build src/Paperbunkr.Data/Paperbunkr.Data.csproj`.

## Step 2: `LibrarySnapshotService`

**Files:** `src/Paperbunkr.App/Services/LibrarySnapshotService.cs` (new)

**What:**
```csharp
public class LibrarySnapshotService
{
    public (int TotalOwned, int Backlog) Capture(PaperbunkrDbContext context)
    {
        var local = context.Issues.Where(i => i.RemoteSourceId == null).ToList(); // IsUnread() isn't translatable to SQL
        int total = local.Count;
        int backlog = local.Count(i => i.IsUnread());

        var today = DateOnly.FromDateTime(DateTime.Now); // local calendar day
        var row = context.LibrarySnapshots.FirstOrDefault(s => s.SnapshotDate == today);
        if (row is null)
        {
            row = new LibrarySnapshot { SnapshotDate = today };
            context.LibrarySnapshots.Add(row);
        }

        row.TotalOwnedComics = total;
        row.BacklogComics = backlog;
        context.SaveChanges();
        return (total, backlog);
    }
}
```
Synchronous, matching `BackupService.BackupNow()`'s own shape (the catalog wraps it in `Task.Run`, not an
async method itself). Upsert-by-date as required by the design doc's manual-rerun reasoning.

**Depends on:** Step 1

**Verify:** new `LibrarySnapshotServiceTests.cs` in `Paperbunkr.App.Tests` - same SQLite-file fixture shape
`StatsScreenViewModelTests` uses (`PaperbunkrDbContext.DatabasePathOverride` + `PaperbunkrDb.CreateContext()`).
Cases: first capture creates a row; a second same-day capture overwrites rather than duplicating (assert
`context.LibrarySnapshots.Count() == 1` after two calls); remote issues (`RemoteSourceId` set) are excluded
from both `TotalOwned` and `Backlog`; an issue with `LastPageRead > 0` isn't counted as backlog; an empty
library produces a `(0, 0)` row without throwing.

## Step 3: Wire into `ScheduledTaskCatalog`

**Files:** `src/Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs` (edit)

**What:** Add `public const string LibrarySnapshot = "library-snapshot";` alongside the other 13 id
constants. Add a new `ScheduledTaskDescriptor` to the `Build()` array:
```csharp
new ScheduledTaskDescriptor(
    LibrarySnapshot, "Record daily library snapshot",
    "Counts your unread comics for the Backlog burn-down chart on the Insights screen.",
    ActivityJobKind.Other, Priority: <next available>, SchedulerResourceClass.Db,
    TimeSpan.FromHours(24), DefaultEnabled: true, ScheduleMode.DailyAt,
    static (handle, ct) => Task.Run(() =>
    {
        handle.Report("Recording snapshot…");
        using var context = PaperbunkrDb.CreateContext();
        var (total, backlog) = new LibrarySnapshotService().Capture(context);
        return backlog == 0 ? "No backlog - all caught up" : $"{backlog} of {total} comics unread";
    }, ct)),
```
`TimeSpan.FromHours(24)` seeds `IntervalHours` even though `DailyAt` doesn't use it for due-checking
(`SchedulerDueLogic.EvaluateDailyAt` ignores `IntervalHours` entirely) - every other descriptor sets a
real `DefaultInterval`, so 24 keeps the field meaningful rather than leaving an arbitrary placeholder.
`SchedulerResourceClass.Db` matches `DbBackup`'s own resource class (a quick local DB operation, not
`Network`). Check `Priority` values already in use and pick the next free one in sequence.

**Depends on:** Step 2

**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`; existing `SchedulerService`/
`SchedulerDueLogic`/`ScheduledTaskCatalog` test suites still pass (this is the first entry to actually use
`ScheduleMode.DailyAt` for real, so this is also implicitly the first end-to-end proof that path works).

## Step 4: `StatsResolver` — `BurnDownData`

**Files:** `src/Paperbunkr.Data/Metadata/StatsResolver.cs` (edit)

**What:** Add the two records from the design doc (`BurnDownData`, `BurnDownPoint`) near `TrendData`/
`TileTrend`. Add `BurnDownData BurnDown` as a new trailing property on `StatsSnapshot`. In `Build`, read
`context.LibrarySnapshots.AsNoTracking().OrderBy(s => s.SnapshotDate).ToList()` alongside the other
top-of-method reads, and add `private static BurnDownData ComputeBurnDown(List<LibrarySnapshot> snapshots,
InsightsRange range, DateTime nowUtc)` implementing the design doc's precedence order exactly:
1. `IsCleared` - latest snapshot's `BacklogComics == 0` (independent of history depth).
2. `HasEnoughHistory` - `snapshots.Count >= 7` (only checked when not cleared).
3. Otherwise, linear regression over the last 30 real calendar days of `snapshots` (or all of them if
   fewer than 30), then the slope-sign branch from the design doc.
`Points` is `snapshots` filtered to `RangeStartUtc(range, nowUtc)` the same way `inRange` events already
are (converting the `DateTime` range boundary to a `DateOnly` comparison), or all snapshots for `AllTime`.

**Depends on:** Step 1 (needs the `LibrarySnapshots` DbSet)

**Verify:** extend `StatsResolverTests.cs` - cases for below-7-days (empty), cleared (backlog hits 0,
independent of the 7-day bar - seed only 2-3 days with the last one at 0), trending down (seed a
monotonically decreasing series, assert `ProjectedClearDate` lands roughly where a manual calculation
predicts), flat/growing (slope ≥ 0, both projection fields null), and the two-window distinction (seed
enough history that the displayed `Points` for a `Days30` range differ from what a `Months12` snapshot
would show, while the projection itself stays anchored to the last 30 days regardless of selected range).

## Step 5: `StatsScreenViewModel` — bindable state

**Files:** `src/Paperbunkr.App/ViewModels/StatsScreenViewModel.cs` (edit)

**What:** Add `public bool HasBurnDownData => Snapshot?.BurnDown is { IsCleared: true } or { HasEnoughHistory: true };`
alongside the other `Has*Data` computed properties (same shape as `HasGrowthData` etc.), and a
`public string BurnDownStatusText` computed property mapping `BurnDown`'s state to the four mutually
exclusive strings from the design doc ("Projected clear: {date:MMM d, yyyy}" / "Not currently trending
down" / a cleared message / the empty-state message - though the empty-state message itself is better
handled directly in XAML via `!HasBurnDownData`, so `BurnDownStatusText` only needs to cover the
`IsCleared`/projected-date/not-trending-down three cases). Add `nameof(HasBurnDownData)`,
`nameof(BurnDownStatusText)` to the `OnPropertyChanged` name array in `Refresh()`.

**Depends on:** Step 4

**Verify:** extend `StatsScreenViewModelTests.cs` - a case seeding enough `LibrarySnapshot` rows (via
`PaperbunkrDb.CreateContext()`, same as the existing `ReadingEvent`-seeding test in this file) to exercise
`HasBurnDownData`/`BurnDownStatusText` across a couple of the four states.

## Step 6: Wire into `InsightsScreen.axaml` + `.axaml.cs`

**Files:** `src/Paperbunkr.App/Views/InsightsScreen.axaml` (edit), `src/Paperbunkr.App/Views/InsightsScreen.axaml.cs` (edit)

**What:** In the axaml, add a new `Border Classes="card"` next to the existing "Library growth"/"Reading
pace" `Grid ColumnDefinitions="*,*"` pair (either as a third card in a similar row, or its own full-width
card below - match whichever reads better given the existing two-column layout there), containing:
```xml
<Panel Height="140">
    <sp:AvaPlot x:Name="BurnDownChart" IsVisible="{Binding Stats.HasBurnDownData}" />
    <TextBlock Text="Not enough history yet - check back in a few days." Classes="sub"
               VerticalAlignment="Center" HorizontalAlignment="Center" IsVisible="{Binding !Stats.HasBurnDownData}" />
</Panel>
<TextBlock Classes="sub" Text="{Binding Stats.BurnDownStatusText}" IsVisible="{Binding Stats.HasBurnDownData}" />
```
In the code-behind, add a `RenderBurnDown(StatsSnapshot snapshot)` method mirroring `RenderPace`'s shape
(clear the plot, apply the theme, early-return + `Refresh()` if `!snapshot.BurnDown.HasEnoughHistory &&
!snapshot.BurnDown.IsCleared`), then `plot.Add.Scatter(xs, ys)` for `BurnDown.Points` (`LineWidth = 2`,
`MarkerSize = 0`, matching `GrowthChart`'s solid-series style) and, when `ProjectedPoints is not null`, a
second `plot.Add.Scatter(...)` with `LinePattern = ScottPlot.LinePattern.Dashed`, `MarkerSize = 0`, no
`LegendText`. Reuse `IntegerLeftTicks(plot, ...)` for the Y-axis and a date-labeled X-axis tick generator
(mirror `GrowthChart`'s `NumericManual` tick-position/label construction, subsampled the same way). Call
`RenderBurnDown(snapshot)` from the existing `RenderCharts` dispatcher alongside `RenderPace`/
`RenderPublicationYear`/`RenderLibraryGrowth`. No hover wiring for this chart (matches `RatingsChart`'s
existing no-hover precedent - not every chart on this screen has one).

**Depends on:** Steps 4, 5

**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`; manual/on-screen only for the actual
rendered chart (ScottPlot draws imperatively, same as every other chart on this screen - there is no
automated visual check for it in this codebase). On-screen check needs real `LibrarySnapshot` history to
accumulate first (the whole point of this slice being infrastructure-plus-card rather than instant), so
this may not be checkable same-day - note that plainly rather than treating "built" as "verified visually."

## Notes for the implementer

- Steps 1-3 (data + write side + scheduling) have no UI dependency and could ship/verify independently of
  Steps 4-6 (read side + UI) if it's ever useful to land them separately - but there's no reason to split
  the PR given both halves are being built in the same pass here.
- `IsUnread()` is a C#-side extension method (not EF-translatable), so `Capture` materializes local issues
  with `.ToList()` before filtering by it - same pattern `StatsResolver.Build` already uses for its own
  `readIssues`/`issues` lists.
