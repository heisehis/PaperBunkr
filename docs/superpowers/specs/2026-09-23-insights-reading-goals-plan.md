# Insights pitch slice 4 — Reading goals & challenges — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-23-insights-reading-goals-design.md*

First Insights-family slice with real persisted CRUD (a `ReadingGoal` table) plus a top-level creation
overlay, alongside the usual pure-function resolver. Verified against the real codebase: overlay dialogs in
this app are hosted at `MainWindow.axaml` level via the shared `controls:OverlayShell` (`IsOpen`/
`CloseCommand`/`DeferContent="True"`), matching `NewEventOrContinuityOverlay`'s exact wiring
(`MainWindow.axaml:1133-1139`, `MainViewModel.cs:266,1408-1413,1473`) — not a screen-local popup. Delete
confirmation uses the existing shared `IDialogService.ConfirmAsync` (`DialogService.cs:16-22`). Text-entry
autocomplete (publisher/genre free text) uses `Controls/SuggestBox`, this app's established replacement for
ComboBox/AutoCompleteBox everywhere else.

## Step 1: `ReadingGoal` entity + enums + migration

**Files:** `src/Paperbunkr.Data/Entities/ReadingGoal.cs` (new), `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit)

**What:** New entity, matching the design doc exactly:
```csharp
public class ReadingGoal
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public GoalMetric Metric { get; set; }
    public long Target { get; set; }
    public GoalPeriodKind PeriodKind { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public GoalScopeKind ScopeKind { get; set; }
    public string? ScopeValue { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public enum GoalMetric { Items, Pages }
public enum GoalPeriodKind { ThisYear, ThisMonth, Custom }
public enum GoalScopeKind { Library, Series, Publisher, Genre }
```
Add `public DbSet<ReadingGoal> ReadingGoals => Set<ReadingGoal>();` next to `LibrarySnapshots`
(`PaperbunkrDbContext.cs:151`). Scaffold with the real CLI, not hand-written:
```bash
dotnet ef migrations add AddReadingGoals --project src/Paperbunkr.Data --startup-project src/Paperbunkr.App
```
No index needed beyond the default PK — unlike `LibrarySnapshot`, there's no one-row-per-day invariant here.

**Depends on:** none

**Verify:** `dotnet build src/Paperbunkr.Data/Paperbunkr.Data.csproj`; migration-test convention (see Step 11).

## Step 2: `GoalResolver`

**Files:** `src/Paperbunkr.Data/Metadata/GoalResolver.cs` (new)

**What:**
```csharp
public static class GoalResolver
{
    public static IReadOnlyList<GoalProgress> Build(PaperbunkrDbContext context, DateTime nowUtc);
}

public sealed record GoalProgress(ReadingGoal Goal, long CurrentValue, bool IsComplete, GoalPaceState PaceState, long BehindAmount);

public enum GoalPaceState { OnTrack, Behind, NotApplicable }
```
- Reads `context.ReadingGoals.AsNoTracking().ToList()` and `context.ReadingEvents.AsNoTracking().ToList()`
  once, then computes each goal's `GoalProgress` in-memory (small tables, no reason to push per-goal
  filtering into SQL).
- Per goal: filter events to `Kind == Finished`, `TimestampUtc.ToLocalTime().Date` within
  `[PeriodStart, PeriodEnd]` (inclusive both ends — `PeriodEnd` is a calendar day, so convert to
  `DateOnly.FromDateTime(...)` for the comparison, same as `RecapResolver`'s own year-boundary handling).
  Then apply the scope filter: `ScopeKind.Series` → `e.SeriesId == int.Parse(Goal.ScopeValue!)`;
  `ScopeKind.Publisher` → `e.Publisher == Goal.ScopeValue`; `ScopeKind.Genre` → `e.PrimaryGenre ==
  Goal.ScopeValue`; `ScopeKind.Library` → no filter.
- `CurrentValue`: `Metric.Items` → `matched.Count`; `Metric.Pages` → `matched.Sum(e => (long)(e.PagesRead ?? 0))`.
- `IsComplete = CurrentValue >= Target`.
- Pace: `NotApplicable` when `IsComplete` or `Goal.PeriodKind == GoalPeriodKind.Custom`. Otherwise
  `elapsedFraction = Math.Clamp((nowUtc.ToLocalTime().Date - PeriodStart).Days / (double)(PeriodEnd -
  PeriodStart).Days, 0, 1)` (guard `PeriodEnd == PeriodStart` → `elapsedFraction = 1`, a same-day goal is
  either done or not); `expected = Target * elapsedFraction`; `Behind` with `BehindAmount =
  (long)Math.Ceiling(expected - CurrentValue)` when `CurrentValue < expected`, else `OnTrack`.

**Depends on:** Step 1

**Verify:** `dotnet test --filter FullyQualifiedName~GoalResolverTests` (Step 3).

## Step 3: `GoalResolverTests`

**Files:** `src/Paperbunkr.Data.Tests/GoalResolverTests.cs` (new)

**What:** Same fixture shape as `RecapResolverTests`/`StatsResolverTests` (reuse
`InsightsResolverTests.SeedSeries`/`SeedIssue`/`SeedEvent`; add goals via plain `ctx.ReadingGoals.Add(...)`).
Cases:
- One per `Metric` (`Items`/`Pages`).
- One per `ScopeKind`: `Library` (counts everything in range), `Series` (only matching `SeriesId`),
  `Publisher` (only matching `Publisher`), `Genre` (only matching `PrimaryGenre`) — plus a case proving a
  novel's `Finished` event (no `Publisher`/`PrimaryGenre`) never satisfies a `Publisher`- or `Genre`-scoped
  goal even when otherwise in range.
- Pace-behind case and pace-on-track case at a known `elapsedFraction` (e.g. a 100-day goal, 50 days
  elapsed, assert the exact `BehindAmount`).
- Completed case: `PaceState.NotApplicable` once `IsComplete` even if the naive elapsed-fraction math would
  otherwise say "behind."
- `Custom`-range case past its own `PeriodEnd` without completing: `IsComplete: false`,
  `PaceState.NotApplicable`.
- Mid-period edge case at `elapsedFraction` exactly 0 and exactly 1 (boundary days).

**Depends on:** Step 2

**Verify:** `dotnet test --filter FullyQualifiedName~GoalResolverTests`

## Step 4: `GoalRing` control

**Files:** `src/Paperbunkr.App/Views/Stats/GoalRing.cs` (new)

**What:** Same hand-rolled-control shape as `TrendSparkline`/`ActivityHeatmap` in this folder
(`TrendSparkline.cs` is the closest precedent — read it before writing this):
- `StyledProperty<double> Percent` (0-100, clamped when drawing), `StyledProperty<bool> IsComplete`, both
  `AffectsRender`.
- `MeasureOverride`: fixed `new Size(40, 40)`.
- `Render`: draw a full background circle stroke (`ResolveBrush("PbBorderBrush", ...)` or similar muted
  brush, same `ResolveBrush` helper shape as `TrendSparkline.cs:72-73`), then a foreground arc via
  `StreamGeometry`/`ArcTo` (start at 12 o'clock, sweep clockwise by `Percent / 100 * 360` degrees,
  `isLargeArc: Percent > 50`) stroked with `PbAccentBrush`. When `IsComplete`, skip the arc math and draw a
  full circle stroke in `PbSuccessBrush` instead (a 100%-sweep arc's start/end points coincide, which is a
  degenerate case worth avoiding rather than debugging).

**Depends on:** none

**Verify:** covered by Step 6's tests (construction/property-level, no pixel rendering - same accepted
precedent as `TrendSparkline`'s own tests in `InsightsChartPolishTests.cs`).

## Step 5: `GoalsViewModel`

**Files:** `src/Paperbunkr.App/ViewModels/GoalsViewModel.cs` (new)

**What:**
```csharp
public partial class GoalsViewModel : ViewModelBase
{
    public GoalsViewModel(IDialogService dialogs, IReadingEventRecorder? readingEventRecorder = null, Func<DateTime>? nowUtc = null);

    public bool IsActive { get; set; }
    public ObservableCollection<GoalCardViewModel> Cards { get; } = new();

    public void Refresh();
    [RelayCommand] private Task DeleteGoal(GoalCardViewModel card);
}

public sealed partial class GoalCardViewModel : ObservableObject
{
    public int GoalId { get; }
    public string Title { get; }
    public string StatusText { get; } // "36 of 50 · on track" / "... · N behind pace" / "Complete!" / "Goal ended · X of Y"
    public double Percent { get; }    // clamped 0-100 for GoalRing
    public bool IsComplete { get; }
}
```
- `Refresh()`: `GoalResolver.Build(context, nowUtc())`, map each `GoalProgress` to a `GoalCardViewModel`
  (status text branches: `IsComplete` → "Complete!"; `Goal.PeriodKind == Custom && DateOnly today >
  PeriodEnd && !IsComplete` → "Goal ended · {CurrentValue} of {Target}"; `PaceState.Behind` → "{CurrentValue}
  of {Target} · {BehindAmount} behind pace"; else → "{CurrentValue} of {Target} · on track"), replace
  `Cards`' contents.
- **Live milestone check**: before replacing `Cards`, snapshot the previous `(GoalId -> percent)` map; after
  computing the new percents, for each goal whose percent crossed 50 or 100 for the first time (previous <
  threshold <= new), raise `Activity?.RaiseAlert(new ActivityAlert { Severity = ActivityAlertSeverity.Info,
  Title = ..., DedupeKey = $"goal-50:{goalId}" / $"goal-complete:{goalId}" })` — needs an `IActivityService?
  Activity` constructor param too (same optional/nullable shape `InsightsScreenViewModel.Activity` already
  uses for the Recap export failure path - reuse that same instance, don't create a second one).
- `DeleteGoal`: `await _dialogs.ConfirmAsync("Delete this goal? This can't be undone.", isDestructive:
  true)`; if confirmed, `context.ReadingGoals.Remove(...)`, `SaveChanges()`, `Refresh()`.
- Same `ReadingEventRecorded` subscription shape as `StatsScreenViewModel`/`RecapViewModel` (clear nothing
  to cache here since `GoalResolver.Build` has no session cache of its own - goals are cheap to recompute
  every time, unlike `StatsSnapshot`/`RecapSnapshot`).

**Depends on:** Step 2

**Verify:** covered by Step 6.

## Step 6: `GoalsViewModelTests`

**Files:** `src/Paperbunkr.App.Tests/GoalsViewModelTests.cs` (new)

**What:** Same fixture shape as `RecapViewModelTests`/`StatsScreenViewModelTests`
(`PaperbunkrDbContext.DatabasePathOverride` + `PaperbunkrDb.CreateContext()`), plus a fake `IDialogService`
(`ConfirmAsync` returning a settable bool) and a fake `IActivityService` capturing raised alerts (check
`ActivityService`'s constructor/interface first - a hand-rolled fake implementing `IActivityService` is
likely simplest, matching this file's own `FakeRecorder : IReadingEventRecorder` precedent). Cases:
- Crossing 50% raises exactly one alert with `DedupeKey` `goal-50:{id}`; a second `Refresh()` while still
  above 50% (no further crossing) doesn't raise a second one.
- Reaching 100% raises `goal-complete:{id}`.
- `DeleteGoal` with a fake dialog service returning `false` (cancelled) leaves the goal in `Cards`; returning
  `true` removes it and it doesn't reappear on the next `Refresh()`.

**Depends on:** Step 5

**Verify:** `dotnet test --filter FullyQualifiedName~GoalsViewModelTests`

## Step 7: `GoalEditorViewModel` (create-goal overlay VM)

**Files:** `src/Paperbunkr.App/ViewModels/GoalEditorViewModel.cs` (new)

**What:** Same create-only shape as `NewEventOrContinuityViewModel` (`NewEventOrContinuityViewModel.cs` -
read it fully before writing this; no `LoadForEdit` here, per the design doc's "create/delete only for v1"
call):
```csharp
public partial class GoalEditorViewModel : ViewModelBase
{
    public GoalEditorViewModel(Action onSaved, Action onCancel);
    public void Reset(); // resets all fields to defaults, called each time the overlay opens

    [ObservableProperty] private GoalMetric _metric; // default Items
    [ObservableProperty] private string _targetText = string.Empty;
    [ObservableProperty] private GoalPeriodKind _periodKind; // default ThisYear
    [ObservableProperty] private DateTimeOffset? _customStart;
    [ObservableProperty] private DateTimeOffset? _customEnd;
    [ObservableProperty] private GoalScopeKind _scopeKind; // default Library
    [ObservableProperty] private string _scopeSeriesName = string.Empty; // SuggestBox text
    [ObservableProperty] private string _scopePublisher = string.Empty;
    [ObservableProperty] private string _scopeGenre = string.Empty;
    [ObservableProperty] private string _title = string.Empty; // auto-suggested, user-editable

    public bool IsCustomPeriod => PeriodKind == GoalPeriodKind.Custom;
    public bool IsSeriesScope => ScopeKind == GoalScopeKind.Series;
    public bool IsPublisherScope => ScopeKind == GoalScopeKind.Publisher;
    public bool IsGenreScope => ScopeKind == GoalScopeKind.Genre;
    public IReadOnlyList<string> AllSeriesNames { get; } // loaded once in ctor for the SuggestBox
    public IReadOnlyList<string> AllPublishers { get; }
    public IReadOnlyList<string> AllGenres { get; }

    public bool CanCreate => long.TryParse(TargetText, out long t) && t > 0
        && (!IsCustomPeriod || (CustomStart is not null && CustomEnd is not null && CustomEnd > CustomStart))
        && (!IsSeriesScope || !string.IsNullOrWhiteSpace(ScopeSeriesName))
        && (!IsPublisherScope || !string.IsNullOrWhiteSpace(ScopePublisher))
        && (!IsGenreScope || !string.IsNullOrWhiteSpace(ScopeGenre));

    [RelayCommand] private void Create(); // resolves PeriodStart/End, ScopeValue, inserts ReadingGoal, calls onSaved
}
```
- `AllSeriesNames`/`AllPublishers`/`AllGenres` loaded via a one-off `PaperbunkrDb.CreateContext()` query in
  the constructor (`context.Series.Select(s => s.Name).Distinct().ToList()`,
  `context.Issues.Select(i => i.Publisher).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList()`,
  `context.IssueTags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value).Distinct().ToList()`).
- Partial `On*Changed` handlers for `Metric`/`TargetText`/`PeriodKind` update a live-suggested `Title`
  ("Read {Target} {issues|pages} {this year|this month|by {CustomEnd:MMM d}}") only while the user hasn't
  manually edited `Title` themselves (track a private `_titleManuallyEdited` bool, set `true` the moment
  `Title`'s own setter fires from user input vs the auto-suggest path - simplest: have the auto-suggest path
  set a private backing field directly and only flip the flag in a `partial void OnTitleChanged` that
  ignores one internally-flagged programmatic update, mirroring how `RecapViewModel`'s `_isRefreshing` guard
  avoids a similar re-entrancy problem).
- `PeriodStart`/`PeriodEnd` resolution in `Create()`: `ThisYear` → `Jan 1..Dec 31` of `DateTime.Now.Year`;
  `ThisMonth` → first/last day of the current local month; `Custom` → `DateOnly` from `CustomStart`/`CustomEnd`.
  `ScopeValue`: `Series` → the *id* of the series matching `ScopeSeriesName` (look it up by name at save
  time; if no match, `CanCreate` should already have prevented this, but fail closed - don't save a
  garbage/unmatched scope) `.ToString()`; `Publisher`/`Genre` → the raw text; `Library` → `null`.

**Depends on:** Step 1 (needs the enums)

**Verify:** covered by a small `GoalEditorViewModelTests.cs` if time allows within this step, else fold into
manual verification (Step 12) - the create-form logic is simple enough that a full test file is optional;
use judgment here rather than padding the plan.

## Step 8: Wire the create-goal overlay (View + MainWindow + MainViewModel)

**Files:** `src/Paperbunkr.App/Views/GoalEditorOverlay.axaml` (new), `src/Paperbunkr.App/Views/GoalEditorOverlay.axaml.cs` (new),
`src/Paperbunkr.App/Views/MainWindow.axaml` (edit), `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)

**What:**
- `GoalEditorOverlay.axaml`: a `UserControl`, `x:DataType="vm:GoalEditorViewModel"`, matching
  `NewEventOrContinuityOverlay.axaml`'s general shape (read it for the header/footer/button-row
  convention). Fields: `RadioButton`s for `Metric`, a `TextBox` for `TargetText`, `RadioButton`s for
  `PeriodKind` with two `DatePicker`s (`SelectedDate="{Binding CustomStart}"`/`CustomEnd`,
  `IsVisible="{Binding IsCustomPeriod}"`, matching `BookPropertiesOverlay.axaml:61`'s exact `DatePicker`
  binding shape), `RadioButton`s for `ScopeKind` with a `controls:SuggestBox` per scope
  (`IsVisible="{Binding IsSeriesScope}"` etc., `ItemsSource="{Binding AllSeriesNames}"` /
  `AllPublishers`/`AllGenres`, `Text="{Binding ScopeSeriesName}"` etc.), a `TextBox` for `Title`, Create/
  Cancel buttons (`Create` disabled via `IsEnabled="{Binding CanCreate}"`).
- Code-behind: minimal `InitializeComponent()` stub, per this project's AVLN2000 build gotcha - ships in the
  same commit as the `.axaml`.
- `MainWindow.axaml`: new `controls:OverlayShell IsOpen="{Binding IsNewGoalDialogOpen}"
  CloseCommand="{Binding CloseNewGoalDialogCommand}" DeferContent="True"` block, same position/shape as the
  New Event dialog block (`MainWindow.axaml:1131-1139`), hosting `<views:GoalEditorOverlay
  DataContext="{Binding GoalEditor}" />`.
- `MainViewModel.cs`: `GoalEditor = new GoalEditorViewModel(OnGoalCreated, CloseNewGoalDialog);` (mirrors
  `MainViewModel.cs:266`), `public GoalEditorViewModel GoalEditor { get; }` (mirrors `:579`), `[ObservableProperty]
  private bool _isNewGoalDialogOpen;`, `private void OpenNewGoalDialog() { GoalEditor.Reset(); IsNewGoalDialogOpen
  = true; }`, `private void CloseNewGoalDialog() => IsNewGoalDialogOpen = false;`, `private void OnGoalCreated()
  { CloseNewGoalDialog(); Insights.Goals.Refresh(); }`. Add `IsNewGoalDialogOpen` to the "any overlay open"
  aggregator (`MainViewModel.cs:1294`) and the Escape-key dispatcher (`MainViewModel.cs:2720-2722`), same
  pattern as `IsNewEventDialogOpen` in both spots.

**Depends on:** Step 7

**Verify:** `dotnet build` (with the delete-`.dll`/`.pdb`-then-rebuild caution for the brand-new
`GoalEditorOverlay` view, per this project's Avalonia build gotcha - `RecapPosterView`'s own addition this
session is the freshest example of why).

## Step 9: Wire `Goals` into `InsightsScreenViewModel` + the Overview tab

**Files:** `src/Paperbunkr.App/ViewModels/InsightsScreenViewModel.cs` (edit), `src/Paperbunkr.App/Views/InsightsScreen.axaml` (edit)

**What:**
- `InsightsScreenViewModel`'s constructor gains an `Action openNewGoalDialog` parameter (a required
  callback, not optional-with-null-default - unlike `activity`/`nowUtc`, there's no sensible no-op fallback
  for "the user clicked Add Goal and nothing happens"), and `IDialogService dialogs` (also required, same
  reasoning). Construct `Goals = new GoalsViewModel(dialogs, readingEventRecorder, nowUtc) { Activity =
  activity };` (or thread `activity` through the constructor like `Recap`/`Stats` already take
  `readingEventRecorder`/`nowUtc` - implementer's call on whichever reads cleaner, but `Activity` must reach
  `GoalsViewModel` one way or another). Add `public GoalsViewModel Goals { get; }` and a
  `[RelayCommand] private void AddGoal() => _openNewGoalDialog();`. `Goals.Refresh()` is called from the
  main `Refresh()` method unconditionally (unlike `Stats`/`Recap`, which only refresh while their own tab is
  selected) - the goal cards live on the *Overview* tab, which is already refreshed whenever this screen is
  shown, so no separate `IsGoalsActive`-style tab-gating is needed. Update `Refresh()`'s existing `if
  (IsStatsTabSelected) Stats.Refresh();` block to leave `Goals.Refresh()` as an unconditional call right
  after `PopulateLists(_cache)`.
- `MainViewModel.cs`: update the `Insights = new InsightsScreenViewModel(...)` call
  (`MainViewModel.cs:245`) to pass `openNewGoalDialog: OpenNewGoalDialog, dialogs: Dialogs`.
- `InsightsScreen.axaml`: new `WrapPanel` inserted above the existing "READING" `StackPanel`
  (`InsightsScreen.axaml:152` area, inside the Overview `ScrollViewer`), `ItemsSource="{Binding
  Goals.Cards}"`, one `Border Classes="card attnCard"` per `DataTemplate x:DataType="vm:GoalCardViewModel"`
  containing a `stats:GoalRing Percent="{Binding Percent}" IsComplete="{Binding IsComplete}"`, `Title`,
  `StatusText`, and a small delete `Button` (`fi:SymbolIcon Symbol="Delete"`, `Command="{Binding
  $parent[ItemsControl].((vm:InsightsScreenViewModel)DataContext).Goals.DeleteGoalCommand}"`, `CommandParameter="{Binding}"`
  - same `$parent[ItemsControl]` cross-DataContext-command idiom already used throughout this file, e.g.
  `InsightsScreen.axaml:128`). A trailing `Border Classes="card attnCard"` (no binding to a `GoalCardViewModel`,
  just a static "+ Add goal" `Button Command="{Binding AddGoalCommand}"`) after the `ItemsControl` (a second
  sibling element in the same `WrapPanel`, not inside the `ItemsControl` itself, so it doesn't need its own
  `DataTemplate`).

**Depends on:** Steps 5, 8

**Verify:** `dotnet build`; existing `InsightsScreenViewModel`-adjacent tests still pass (this constructor
signature change needs every direct-construction test call site updated - grep for
`new InsightsScreenViewModel(` before touching the signature).

## Step 10: `goal-pace-check` scheduled task

**Files:** `src/Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs` (edit)

**What:** `public const string GoalPaceCheck = "goal-pace-check";` alongside the other 15 id constants
(after `LibrarySnapshot`). New descriptor:
```csharp
new ScheduledTaskDescriptor(
    GoalPaceCheck, "Check reading goal pace",
    "Lets you know if you're falling behind on an active reading goal.",
    ActivityJobKind.Other, Priority: 15, SchedulerResourceClass.Db,
    TimeSpan.FromHours(24), DefaultEnabled: true, ScheduleMode.DailyAt,
    static (handle, ct) => Task.Run(() =>
    {
        handle.Report("Checking goal pace…");
        using var context = PaperbunkrDb.CreateContext();
        var behind = GoalResolver.Build(context, DateTime.UtcNow).Where(g => g.PaceState == GoalPaceState.Behind).ToList();
        foreach (var g in behind)
        {
            // raise via the injected IActivityService - check how other DailyAt/Interval tasks in this
            // catalog reach IActivityService (the descriptor's own `handle` may already expose it, or
            // the task body may need a captured reference set up the same way another Other-kind task
            // in this file already raises ActivityAlert from inside its own RunAsync - grep this file
            // for `RaiseAlert` before assuming `handle` alone is enough).
        }
        return behind.Count == 0 ? "No goals behind pace" : $"{behind.Count} goal{Plural(behind.Count)} behind pace";
    }, ct)),
```
**Note for the implementer:** this file's existing tasks report their summary string back through `handle`
and don't obviously raise `ActivityAlert`s themselves from inside `RunAsync` bodies elsewhere in this
catalog - confirm during implementation whether `IActivityJobHandle` exposes enough to raise a *separate*
alert (distinct from the job's own completion toast) or whether the alert needs to be raised via a captured
`IActivityService` reference the same way `GoalsViewModel` reaches one, and adjust this step's code to match
whatever the real pattern turns out to be rather than the sketch above.

**Depends on:** Step 2

**Verify:** `dotnet build`; existing `SchedulerService`/`ScheduledTaskCatalog` test suites still pass.

## Step 11: Migration test

**Files:** `src/Paperbunkr.Data.Tests/AddReadingGoalsMigrationTests.cs` (new)

**What:** Same shape as `AddLibrarySnapshotsMigrationTests.cs` - apply the migration to a fresh SQLite file,
assert the `ReadingGoals` table exists with the expected columns.

**Depends on:** Step 1

**Verify:** `dotnet test --filter FullyQualifiedName~AddReadingGoalsMigrationTests`

## Step 12: On-screen verification (manual)

Not automatable. Open Insights → Overview, click "+ Add goal", create a "This year" Items goal with a small
target (e.g. 2) so it's completable quickly, confirm the ring/status text render and update after finishing
an issue (both the live progress and, at 50%/100%, an Activity Center alert), create a `Custom`-range goal
and a `Series`/`Publisher`/`Genre`-scoped goal to confirm each scope filters correctly, delete a goal and
confirm the confirm-dialog + removal, and run the `goal-pace-check` task manually from the Automation tab's
"Run now" (per `SchedulerService.RunNowAsync`'s existing generic support for any catalog entry) against a
deliberately-behind goal to confirm the alert appears.

## Notes for the implementer

- `InsightsScreenViewModel`'s constructor signature change (Step 9) is the one change in this plan most
  likely to break existing test call sites - grep and fix them as part of that step, not as an afterthought.
- Step 10's `ActivityAlert`-raising mechanism is deliberately left open pending a real look at how this
  catalog's existing tasks (if any) raise alerts rather than just job-completion summaries - don't guess the
  API shape, check it.
