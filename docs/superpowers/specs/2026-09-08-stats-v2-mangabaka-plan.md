# Stats v2 — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md*

Survey notes (things confirmed against the real code, not assumed):

- `InsightsResolver.Build`/`InsightsSnapshot` currently hold everything (`src/Paperbunkr.Data/
  Metadata/InsightsResolver.cs`) — Reading attention + Gaps + Lifetime/streaks/Pace/Completion/
  Composition/Ratings all in one record. Splitting is a real edit to this file, not just an addition.
- `ReadingEvent` backfill (`src/Paperbunkr.Data/Migrations/ReadingEventBackfill.cs`) writes a
  backfilled item's `Opened` and `Finished` rows at the **identical** `TimestampUtc` (`OpenedTime`/
  `LastOpenedTime`). That's the exact, reliable signal for "exclude backfilled rows" in Highlights —
  no schema change needed, just filter `Opened.TimestampUtc != Finished.TimestampUtc` per pair.
- Skins are `theme.json` files (`src/Paperbunkr.App/Assets/Skins/{default,windows_11,cool_technical,
  vibrant_pop,vintage_paperback}/theme.json`) deserialized into `SkinTheme`/`SkinColors`
  (`src/Paperbunkr.App/Models/SkinTheme.cs`) and pushed into `Application.Current.Resources` by
  `SkinService.ApplySkinResources` (`src/Paperbunkr.App/Services/SkinService.cs`) — **not** separate
  `.axaml` dictionaries per skin as the design doc assumed. The class doc comment confirms new keys
  are meant to be added additively (a `theme.json` missing a key just gets the C# default) — this is
  the existing, safe pattern for the two new chart colors, not a special case.
- `Paperbunkr.Data.csproj` only references `Paperbunkr.Engine` — confirms `StatsResolver` (Data
  project) cannot call `MarkResolver` (App project). See the two corrections already made to the
  design doc for `AgeRating` bucketing.
- Real FluentIcons symbol confirmed via reflection on the installed `FluentIcons.Common.dll`
  (`2.1.337`): `DataPie` exists and is unused elsewhere in `MainWindow.axaml` — using it for Stats'
  rail icon, distinct from Insights' `DataHistogram`.
- `QuickOpenService.Screens` (`src/Paperbunkr.App/Services/QuickOpenService.cs:35`) does **not**
  include `"insights"` today — Insights isn't Quick-Open-able, so Stats doesn't need to be either;
  no change needed there.
- `RailOrder` (`MainViewModel.cs:56`) is a flat `Dictionary<string,int>` — inserting `"stats"` at
  index 2 means renumbering `library`(2→3) through `preferences`(7→8) in the same edit.
- Test project split confirmed: resolver-level tests live in `Paperbunkr.Data.Tests` (existing
  `InsightsResolverTests.cs`); ViewModel-level tests live in `Paperbunkr.App.Tests` (existing
  `InsightsScreenViewModelTests.cs`). `StatsResolverTests.cs`/`StatsScreenViewModelTests.cs` mirror
  those locations.
- No existing multi-value creator-splitting helper anywhere in the codebase — Top Authors/Artists'
  comma-split parsing is new, simple code in `StatsResolver`, not a reuse of something else.
- GUI verification for this feature happens by manual click-through, not computer-use (standing
  rule — [[feedback_no_computer_use]]) or optionally a FlaUI smoke test if time allows; called out
  per-step below rather than assumed.

## Step 1: Split the resolver — move, don't yet expand

**Files:** `src/Paperbunkr.Data/Metadata/InsightsResolver.cs` (edit), `src/Paperbunkr.Data/Metadata/
StatsResolver.cs` (new), `src/Paperbunkr.Data.Tests/InsightsResolverTests.cs` (edit — trim),
`src/Paperbunkr.Data.Tests/StatsResolverTests.cs` (new — receives the moved test cases)

**Correction found during implementation:** none of `Continue`/`AlmostDone`/`DiveIn`/`Gaps` ever
consumed the `range`/`inRange`-filtered events — only the tiles moving to Stats did. Once those
move out, Insights' range selector has zero effect on anything left on the screen. Removing it
entirely rather than shipping dead UI: `InsightsResolver.Build` drops its `range` parameter (now
just `(context, nowUtc)`), `InsightsScreenViewModel` drops `Range`/`RangeOptions`/`SetRange`, and
`InsightsScreen.axaml`'s fixed header drops the range-chip `ItemsControl` (Step 11). `InsightsRange`
the enum type moves to `StatsResolver.cs` unchanged (Stats is the only screen that still needs it).

**What:** Move `Lifetime`, `ReadingDayStreak`, `FinishStreak`, `FinishedInRange`, `Pace`,
`Completion`, `Composition`, `Ratings` (and their `Compute*` methods, `ComputeStreak`,
`ComputePace`, `ComputeCompletion`, `ComputeComposition`, `ComputeRatings`, `EstimateBookPages`,
`MonthsSpan`) out of `InsightsResolver`/`InsightsSnapshot` into a new `StatsResolver`/
`StatsSnapshot`, same static/pure shape, same `Build(context, range, now)` signature. `Completion`
is renamed to reflect it's being superseded in Step 2 (kept as-is for this step — a pure move, no
behavior change yet, so the diff is reviewable). `InsightsSnapshot` keeps only `Continue`,
`AlmostDone`, `DiveIn`, `Gaps`, `Range`, `GeneratedUtc`. Update `InsightsScreenViewModel` to drop
the now-gone snapshot members it referenced (`HasPaceData`, `HasStreakData`, `HasRatings`,
`CompositionMax`, `PaceOrRatingsChanged`, `OpenCompositionValue` command) — these move to
`StatsScreenViewModel` in Step 8, so this step temporarily leaves `InsightsScreen.axaml`'s charts
row without a data source. That's fine: Step 11 removes that XAML in the same PR-sized unit of
work, so there's no shipped intermediate state, just an intermediate commit.

**Depends on:** none

**Verify:** `dotnet test src/Paperbunkr.Data.Tests --filter FullyQualifiedName~InsightsResolverTests|FullyQualifiedName~StatsResolverTests`
— every existing assertion about the moved tiles now lives in `StatsResolverTests.cs` and still
passes unchanged (pure move).

## Step 2: Highlights — the backfill-exclusion logic

**Files:** `src/Paperbunkr.Data/Metadata/StatsResolver.cs` (edit), `src/Paperbunkr.Data.Tests/
StatsResolverTests.cs` (edit)

**What:** Add `ComputeHighlights(issues, events, nowUtc)` returning a `HighlightsData` record with
six fields: `HighestRated`, `MostReread`, `LongestJourney`, `FastestCompletion`, `PlanToReadCount`,
`ZeroProgressCount` (design §6.1). Core new logic: a `RealSpans` helper that pairs each item's
`Opened` events with the next `Finished` event by timestamp order, **excluding any pair where the
two timestamps are equal** (the backfill signature confirmed above) — used by both `LongestJourney`
and `FastestCompletion`. `MostReread` counts `Finished` rows per item (no exclusion — ships with a
footnote per the design's Q4 answer, not filtered). `ZeroProgressCount` mirrors the existing
"never opened" check already in `ComputeDiveIn` (`i.OpenCount > 0 || openedComics.Contains(i.Id)`)
but counts series where `ReadingStatus != Planned`, matching design §6.1's definition exactly.

**Depends on:** Step 1 (needs `StatsResolver` to exist)

**Verify:** New tests: a backfilled pair (equal timestamps) is excluded from Longest Journey/Fastest
Completion but counts toward Most Reread; a real pair (different timestamps) is included in all
three; ties render as multiple items (assert the "and N others" list, not just a count); Zero
Progress excludes `Planned` series and excludes anything with any `Opened` event regardless of
status.

## Step 3: Remaining Stats computations

**Files:** `src/Paperbunkr.Data/Metadata/StatsResolver.cs` (edit), `src/Paperbunkr.Data.Tests/
StatsResolverTests.cs` (edit)

**What:** Add the rest of `StatsSnapshot`'s new fields (design §6.2–§6.10):
- `ReadingActivity` — avg days to complete / avg issues per day, built on Step 2's `RealSpans`.
- `Heatmap` — `IReadOnlyDictionary<DateOnly, int>` of local-date → event count (`Opened` or
  `Finished`), full history, ignores range.
- `LibraryGrowth` — cumulative counts bucketed by `Issue.AddedTime`/`Book.AddedTime`, four stack-by
  dimensions (Total/ReadingStatus/ContentType/AgeRating — current-value stacking, the "accepted
  simplification" the design doc already calls out).
- `Breakdown` — `ByReadingStatus` (all 7 `ReadingStatus` values, series-level) and `ByMediaType`
  (all 5 `ContentType` values, series-level), replacing the old 3-bucket `Completion`.
- `ContentRating` — raw-grouped `Issue.AgeRating` (see the design-doc correction — same pattern as
  the existing `ComputeComposition`'s Format/Decade buckets, not a new mechanism).
- `PublicationYear` — bucketed `Issue.Year`.
- `TopAuthors` / `TopArtists` — new comma-split parsing over `Issue.Writer` and
  `Issue.Penciller`+`Inker`+`Colorist` (same-issue de-dupe across the three artist fields, per
  design §6.10), top 10 each.
- `Composition.ByPublisher` and `Ratings` carry forward unchanged from Step 1's move — `TopTags`
  reuses the existing `Composition.ByGenre`-style grouping but split by `IssueTagField.Tags` instead
  of `.Genre` (the existing `ByGenre` computation already does the `.Genre` half).

**Depends on:** Step 1

**Verify:** New tests per tile: growth-chart cumulative bucketing across all four stack-by
dimensions, `ReadingStatus`/`ContentType` donut counts including `Unknown`, Top Authors/Artists
parsing (including the same-issue multi-role de-dupe case), empty-library renders without throwing.

## Step 4: Chart colors — additive skin keys

**Files:** `src/Paperbunkr.App/Models/SkinTheme.cs` (edit), `src/Paperbunkr.App/Services/
SkinService.cs` (edit), `src/Paperbunkr.App/App.axaml` (edit), all 5 `theme.json` files under
`src/Paperbunkr.App/Assets/Skins/*/` (edit), `src/Paperbunkr.App.Tests/SkinServiceTests.cs` (edit)

**What:** Add `ChartBlue`/`ChartViolet` to `SkinColors` (with defaults, following the exact
"additive, falls back if a theme.json omits it" pattern the class's own doc comment already
describes for the elevation-scale keys). Add two `SetColorAndBrush(resources, "PbChartBlue", ...)`
/ `"PbChartViolet"` calls to `ApplySkinResources`. Add `PbChartBlueColor`/`PbChartVioletColor` (+
Brush) static entries to `App.axaml`'s resource block (same tier as `PbDangerColor` — a value
present before `SkinService.ApplyPersistedSettings` runs on startup). Pick one tasteful hex pair per
built-in skin (5 skins × 2 colors) that reads well against that skin's own palette rather than
reusing the default skin's blue/violet everywhere — read each `theme.json`'s existing `accent`/
`badge`/`surface` values first so the new hues sit in the same family.

**Depends on:** none (independent of the resolver work)

**Verify:** Extend `SkinServiceTests.cs` with the same assertion shape it already uses for existing
colors (`ApplySkin` sets `PbChartBlueColor`/`PbChartVioletColor` correctly for at least one skin);
`dotnet test --filter FullyQualifiedName~SkinServiceTests`.

## Step 5: Categorical palette in `InsightsChartTheme`

**Files:** `src/Paperbunkr.App/Services/InsightsChartTheme.cs` (edit), a new
`InsightsChartThemeTests.cs` if none exists yet (check first) or extend the existing one

**What:** Add `Blue`/`Violet` `Resolve(...)` accessors (same pattern as the existing `Accent`/
`Muted`/`Grid`), plus a `CategoricalPalette` — a fixed-order `ScottPlot.Color[]` (`Accent, Blue,
Badge, Success, Violet, Danger`) so any chart needing N categories assigns colors by enum/list index,
never randomly. `Badge`/`Danger` accessors need adding too (currently only `Text`/`Muted`/`Accent`/
`Grid` exist) — resolve `"PbBadgeColor"` and the static `"PbDangerColor"` the same way.

**Depends on:** Step 4 (the two new resource keys must exist to resolve)

**Verify:** Unit test asserting `CategoricalPalette` returns 6 distinct colors and falls back
correctly when `Application.Current` has no resources (headless/design-time), matching the existing
fallback test pattern for `Accent`/`Muted`.

## Step 6: Generalize the donut control

**Files:** `src/Paperbunkr.App/Views/Insights/CompletionDonut.cs` (deleted — its 3-segment arc-draw
logic moves, generalized, not duplicated), new `src/Paperbunkr.App/Views/Stats/CategoryDonut.cs`

**What:** `CompletionDonut` becomes dead code once Insights sheds its charts (Step 11) — Insights
has no donut anymore, so this isn't "keep both," it's "move and generalize." `CategoryDonut` takes
an `IReadOnlyList<(string Label, int Count)>` styled property instead of three fixed int
properties, reuses the exact same `StreamGeometry`/`ArcTo` rendering approach and the "every
non-zero segment gets a visible minimum sweep" logic (both already correct, no reason to redo them),
colors segments from Step 5's `CategoricalPalette` by list index. Used for both new Stats donuts
(Reading State — 7 slices, Media Type — 5 slices).

**Depends on:** Step 5

**Verify:** A focused headless-render test (matching this project's existing Avalonia
headless-test conventions for custom controls) asserting N segments render distinct colors and the
minimum-sweep behavior still holds for a lopsided distribution (one segment ≫ others).

## Step 7: Shared `StatCard`

**Files:** new `src/Paperbunkr.App/Views/Stats/StatCard.axaml` (+ `.axaml.cs`)

**What:** Title, optional big-number, optional footnote slot, `ContentPresenter` for chart/list
content — the reusable shape ~12 Stats sections all need (design §9). Follows `InsightsScreen.axaml`'s
existing `Border.card`/`panelTitle`/`bigNumber`/`sub` style classes for visual consistency rather
than inventing new ones, just packaged as a control instead of copy-pasted markup.

**Depends on:** none (styling-only; can be built in parallel with Steps 1-3)

**Verify:** Manual visual check once at least one real Stats tile uses it (Step 9) — a control with
no data-bound consumer yet isn't meaningfully testable on its own.

## Step 8: `StatsScreenViewModel`

**Files:** new `src/Paperbunkr.App/ViewModels/StatsScreenViewModel.cs`, new
`src/Paperbunkr.App.Tests/StatsScreenViewModelTests.cs`

**What:** Mirrors `InsightsScreenViewModel`'s exact shape (design §7's "same pattern"): per-range
`Dictionary<InsightsRange, StatsSnapshot>` cache, `IReadingEventRecorder` subscription that clears
the cache and refreshes if active, `IsActive` flag set by the shell, `RangeOptions`/`SetRange`,
flattened `ObservableCollection`s for the list-shaped tiles (Top Authors/Artists/Tags/Publishers,
Highlights ties), `OpenX` commands for each drill-down (Library-filtered search, reusing
`_goLibraryWithSearch` the same way `InsightsScreenViewModel.OpenCompositionValue` already does).

**Depends on:** Steps 1-3 (needs the finished `StatsResolver`/`StatsSnapshot`)

**Verify:** `StatsScreenViewModelTests.cs` mirrors `InsightsScreenViewModelTests.cs`'s existing test
shape: cache reuse across range switches, invalidation on a recorder event, empty-library/empty-log
renders without throwing.

## Step 9: `StatsScreen.axaml` — the tiles

**Files:** new `src/Paperbunkr.App/Views/StatsScreen.axaml` (+ `.axaml.cs`), new tile views under
`src/Paperbunkr.App/Views/Stats/` (one file per design §6 section — Highlights, ReadingActivity,
ActivityHeatmap control, LibraryGrowth, ReadingPace, LibraryBreakdown, ScoreDistribution,
ContentRating, PublicationYear, TopGenresTags, TopCreatorsPublishers)

**What:** Fixed header + range selector (matches `InsightsScreen.axaml`'s existing top-row pattern),
scrolling body built from `StatCard`-wrapped sections in design §6's order. `ActivityHeatmap` is a
new hand-rolled `Control` (same `OnRender`/`DrawingContext` approach as the old `CompletionDonut`,
now `CategoryDonut`) since a calendar grid isn't a ScottPlot/donut shape. `LibraryGrowth` and
`PublicationYear` are new `ScottPlot.Avalonia` plots (`AvaPlot`), reusing `InsightsChartTheme.Apply`
the same way `ReadingPace`'s chart already does. **Build-gotcha reminder for this step specifically**
(project standing note): add each new `.axaml`'s code-behind `.cs` in the same commit as the
`.axaml` itself — a fresh `x:Class` with no matching compiled partial class is the exact AVLN2000
trap this project has hit before.

**Depends on:** Steps 6, 7, 8

**Verify:** `dotnet build` (with the rm-the-.dll-first gotcha if a XAML compile error occurs
mid-step, per the project's own standing note); manual click-through in the running app — **GUI
verification here is a human task, not something to automate via computer-use** (permission
permanently revoked). Report GUI-unverified honestly if the user doesn't test it live before this
is considered done, matching this project's own established practice for shipped-but-unverified UI
work.

## Step 10: Nav-rail wiring

**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit), `src/Paperbunkr.App/Views/
MainWindow.axaml` (edit)

**What:** In `MainViewModel.cs`: construct `Stats = new StatsScreenViewModel(...)` alongside the
existing `Insights = new InsightsScreenViewModel(...)` (line 150); add `"stats"` to `RailOrder`
at index 2, renumbering `library`(→3) through `preferences`(→8); add the `Stats` property; add
`"stats" => Stats` to `ActiveScreenContent`; add `IsStats` bool; add it to the
`OnCurrentScreenChanged` notify block (+ `Stats.IsActive = ...`); add `GoStatsCommand`
(`[RelayCommand] private void GoStats() => TryLeaveCurrentEditor(() => { CurrentScreen = "stats";
Stats.Refresh(); ResetHistoryRoot("stats"); });`); add the `"stats"` case to `CycleScreen`'s switch;
add `["stats"] = "Stats"` to `RailScreenLabels`. In `MainWindow.axaml`: new rail `Button` block
immediately after Insights' (lines 202-209), `Symbol="DataPie"`, `ToolTip.Tip="Stats"`,
`AutomationProperties.AutomationId="StatsRailButton"` (matches the existing `InsightsRailButton`
naming convention exactly, for consistency with any existing FlaUI automation IDs).

**Depends on:** Step 8 (needs `StatsScreenViewModel` to construct)

**Verify:** `dotnet build`; manual click-through (rail order, active-state highlighting, Ctrl+Tab
cycling per `CycleScreen`).

## Step 11: Shrink `InsightsScreen.axaml`

**Files:** `src/Paperbunkr.App/Views/InsightsScreen.axaml` (edit), `src/Paperbunkr.App/Views/
InsightsScreen.axaml.cs` (edit — remove the `PaceOrRatingsChanged` subscription and ScottPlot
population code, now dead since Step 1 removed those snapshot fields)

**What:** Delete the AT A GLANCE 4-tile grid and the entire Charts `Grid` block (design §4's
"moves to Stats" column). Insights keeps only the fixed header/range selector, the READING
`WrapPanel`, and the Collection health card — exactly design §5's scope.

**Depends on:** Step 9 (Stats must already carry this content before Insights loses it, so nothing
is unreachable mid-implementation)

**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter FullyQualifiedName~InsightsScreenViewModelTests`;
manual check that Insights still renders correctly with its two remaining sections.

## Step 12: Roadmap note

**Files:** `docs/alpha-todo.md` (edit)

**What:** Record the shipped Stats v2 split per this project's standing rule ("update
`docs/alpha-todo.md` by hand — status, commit ref, what you verified, not just what the commit
message claims").

**Depends on:** Steps 1-11 complete and verified

**Verify:** n/a (docs-only)
