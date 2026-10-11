# Insights goal outcomes + chart colour — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md*

Surveyed 2026-10-04. Notes that refine the design against the real code:
- Time-series charts (growth, pace, publication year, burn-down) keep the single accent colour — a colour per bar would invent meaning. The
  palette work targets the *categorical* charts (three donuts, content-rating bars, score distribution). Pace/year/growth get nothing but a check.
- The Today tab has only the goal ring (no donuts); the donuts are all on Trends > Composition.
- Renew needs `MainViewModel.OpenNewGoalDialog` (it calls `GoalEditor.Reset()`), so the InsightsScreenViewModel `openNewGoalDialog` Action becomes
  `Action<GoalCardViewModel?>`-free: Renew is routed through a second Action `openRenewGoalDialog(int goalId)` added to the ctor as an optional parameter.

## Step 1: Goal outcome in the resolver
**Files:** Paperbunkr.Data/Metadata/GoalResolver.cs (edit), Paperbunkr.Data.Tests/GoalResolverTests.cs (edit)
**What:** `GoalOutcome` enum; `GoalProgress` gains `Outcome` and `CompletedOn`. Missed = not complete and `today > PeriodEnd` for every kind. Pace is
`NotApplicable` unless Active. `CompletedOn` = local date of the event on which the running total first reached Target.
**Depends on:** none. **Verify:** `dotnet test src/Paperbunkr.Data.Tests --filter GoalResolverTests`.

## Step 2: Shared chart palette
**Files:** Paperbunkr.App/Services/InsightsChartPalette.cs (new), Paperbunkr.App.Tests/InsightsChartPaletteTests.cs (new)
**What:** pure label→brush-key map (reading state, content rating, neutral for Unknown/Other/None, positional fallback), plus ScottPlot colour
resolution via `InsightsChartTheme`. Includes `Layout(slices)` that returns the ordered, capped (fold to "Other"), coloured list so donut and legend share it.
**Depends on:** none. **Verify:** palette tests.

## Step 3: GoalRing outcome visuals
**Files:** Paperbunkr.App/Views/Stats/GoalRing.cs (edit)
**What:** replace `IsComplete` with `Outcome` + `IsBehind` styled properties (keep `IsComplete` as a thin compatibility alias is NOT needed — only XAML uses it). Completed = success + check;
Missed = danger arc at reached % + cross; Active = accent, behind = badge.
**Depends on:** Step 1 enum. **Verify:** build + headless render.

## Step 4: GoalsViewModel
**Files:** Paperbunkr.App/ViewModels/GoalsViewModel.cs (edit), Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs (edit),
Paperbunkr.App.Tests/GoalsViewModelTests.cs (edit)
**What:** `GoalCardViewModel` gains `Outcome`, `IsBehind`, `CompletedOn`; `ActiveCards`/`PastCards`; hero from active with past fallback; shared StatusText;
Missed alert `goal-missed:{id}` (Warning); `HasPastGoals`; `RenewGoalCommand`. Pace-check task counts only Active+Behind (already true via `PaceState`; assert).
**Depends on:** Step 1. **Verify:** `--filter GoalsViewModelTests`.

## Step 5: Renew prefill
**Files:** GoalEditorViewModel.cs (edit: `LoadFrom(ReadingGoal)`), MainViewModel.cs (edit: wire renew Action), InsightsScreenViewModel.cs (edit: ctor param + command)
**What:** Renew opens the editor with metric/target/scope/period kind copied, period re-resolved (Custom keeps its length shifted to start today).
**Depends on:** Step 4. **Verify:** a GoalEditor test for `LoadFrom`; build.

## Step 6: Goal XAML
**Files:** Paperbunkr.App/Views/InsightsScreen.axaml (edit)
**What:** ring bindings → Outcome/IsBehind; collapsed "PAST GOALS" expander (expanded when no active goals) with Renew + Delete; follow the CLAUDE.md
focusability/detach rules (Renew closes nothing synchronously; delete reuses the existing path).
**Depends on:** Steps 3-5. **Verify:** build; headless PNG render.

## Step 7: Donut colours, legends, hover
**Files:** Views/Stats/CategoryDonut.cs (edit), Views/Stats/DonutLegend.cs or a DataTemplate in InsightsScreen.axaml (new/edit), InsightsScreen.axaml (edit: three donuts)
**What:** donut draws from `InsightsChartPalette.Layout`; legend rows = swatch + label + count + percent from the same layout; hit-test hover highlights slice+row and sets a tooltip;
content-rating bars take slice colour.
**Depends on:** Step 2. **Verify:** donut/legend agreement test; build; headless render.

## Step 8: Score distribution
**Files:** Views/InsightsScreen.axaml.cs (edit `RenderRatings`), Services/InsightsChartHover.cs (edit), tests in InsightsChartPolishTests.cs (edit)
**What:** digit tick labels (no `★`), ramp colours, value labels, zero-bucket stub, hover.
**Depends on:** Step 2. **Verify:** polish tests.

## Step 9: Verification
Fast App suite (`--filter "Speed!=Slow"`), `dotnet build` of the App, avalonia-pro-max review-checklist read, headless PNG of the Composition tab and a goals strip,
update docs/paperbunkr-todo.md and the memory file. One `dotnet` command at a time.
