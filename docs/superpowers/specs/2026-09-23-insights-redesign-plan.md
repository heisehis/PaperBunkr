# Insights section redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-23-insights-redesign-design.md*

**Corrections found during planning survey** (see note at the end of each affected step — flagged
here up front since they adjust the approved design's assumptions):

1. **Goal-tile and Continue-reading-tile sparklines are dropped.** `GoalCardViewModel`/`GoalProgress`
   carry no pace-history series, and per-series reading pace doesn't exist anywhere either — building
   either would mean new resolver logic, which the design doc explicitly puts out of scope. The mono
   current/target readout (`36/50`) stays; the sparkline flourish on those two tiles does not.
2. **Recap's cover-art scope is narrower than the design doc says.** The design doc names "Highest
   rated, Most reread, Longest journey" as the covered Recap slides — but "Longest journey" is a
   **Trends-tab-only** field (`StatsSnapshot.Highlights.LongestJourney`); Recap has no such tile.
   Recap's actual nine tiles are Items Finished, Pages Read, Longest Streak, Busiest Day, Top Series,
   Top Writer, Top Artist, Highest Rated, Most Reread — of which only **Top Series, Highest Rated, and
   Most Reread** name a coverable item. Top Writer/Top Artist name a creator (no cover concept).
3. **`HighlightGroup` (shared by `StatsResolver` and `RecapResolver`) needs two new optional fields**
   to carry through the identity a cover lookup needs — a small, additive touch to a `Paperbunkr.Data`
   resolver record. This is a narrow, necessary exception to the design doc's "no resolver changes":
   without it there is no way to know *which* item's cover to show when a `HighlightGroup` represents a
   tie. Both fields default to `null` and `StatsResolver`'s own four `HighlightGroup` constructions
   (feeding the Trends tab, which doesn't get covers) are untouched.

---

## Step 1: Tab rename (Overview → Today, Stats → Trends)

**Files:** `src/Paperbunkr.App/ViewModels/InsightsScreenViewModel.cs`, `src/Paperbunkr.App/Views/InsightsScreen.axaml`

**What:**
- Rename in `InsightsScreenViewModel.cs`: `_isStatsTabSelected`/`IsStatsTabSelected` → `_isTrendsTabSelected`/
  `IsTrendsTabSelected`; `OnIsStatsTabSelectedChanged` → `OnIsTrendsTabSelectedChanged`; `SelectStatsTab`
  (→ command `SelectTrendsTabCommand`) → `SelectTrendsTab`; `SelectOverviewTab`
  (→ command `SelectTodayTabCommand`) → `SelectTodayTab`; the derived getter `IsOverviewTabSelected` →
  `IsTodayTabSelected` (body unchanged: `!IsTrendsTabSelected && !IsRecapTabSelected`). Update the two
  `OnPropertyChanged(nameof(IsOverviewTabSelected))` call sites to the new name. Update the class's own
  doc comment (currently says "Overview... Stats").
- Update `InsightsScreen.axaml`: tab strip button `Content="Overview"`/`"Stats"` → `"Today"`/`"Trends"`,
  all `Command="{Binding SelectOverviewTabCommand}"`/`SelectStatsTabCommand` bindings, all
  `Classes.active="{Binding IsOverviewTabSelected}"`/`IsStatsTabSelected` bindings, and the three
  `IsVisible="{Binding IsOverviewTabSelected}"`/`IsStatsTabSelected`/`IsRecapTabSelected` bindings gating
  the Overview/Stats/Recap `ScrollViewer`s and the Stats-only range-selector `ItemsControl`.

**Depends on:** none — do this first since every later step edits these two files.

**Verify:** confirmed via repo-wide grep that no other file (tests included) references the old names —
rename is self-contained. `dotnet build` clean; scoped `Insights` test filter still green.

---

## Step 2: `CoverThumb` shared control

**Files:** `src/Paperbunkr.App/Views/CoverThumb.axaml` (new), `src/Paperbunkr.App/Views/CoverThumb.axaml.cs` (new)

**What:** A small `UserControl` used at every place a specific series/issue needs a thumbnail (Today
hero tiles, Today list rows, Collection health gaps, Recap). One `StyledProperty<string?> CoverKey`.
Two sizes via CSS classes rather than an enum property — `Classes="hero"` (46×64) and `Classes="row"`
(28×40) — each a `Style` selector inside `CoverThumb.axaml` setting `Width`/`Height`.

Internally: `Border CornerRadius="{DynamicResource PbRadius}" ClipToBounds="True"` around an `Image` using
the **`AsyncCoverImage.SourceId`** attached property (`views:AsyncCoverImage.SourceId="{Binding CoverKey,
RelativeSource={RelativeSource AncestorType=views:CoverThumb}}"`) — not the older synchronous
`CoverImageConverter` the design doc named, since `AsyncCoverImage` is the convention already used for
every cover thumbnail elsewhere (`LibraryScreen.axaml`, `ReadingScreen.axaml`, `SmartScreen.axaml`) and
supports `DecodeWidth` for correctly-sized small thumbnails — relevant here since several `CoverThumb`s
render per screen at once. `Stretch="UniformToFill"`.

Code-behind is minimal (`InitializeComponent()` only) but **must be added in the same step as the
`.axaml`**, per this project's own documented AVLN2000 build gotcha (a new `x:Class` with no matching
compiled partial class fails `CompileAvaloniaXamlTask`, and a bare retry can silently ship an un-woven
assembly).

**Depends on:** none.

**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`. If it fails inside XAML compilation
after `CoreCompile` already produced output, delete `obj/Debug/net8.0/Paperbunkr.App.{dll,pdb}` before
rebuilding (per the gotcha) rather than trusting a bare retry's "0 Errors."

---

## Step 3: Widen `AttentionRow`/`GapRow` with `CoverKey`

**Files:** `src/Paperbunkr.App/ViewModels/InsightsScreenViewModel.cs`, `src/Paperbunkr.App.Tests/InsightsScreenViewModelTests.cs`

**What:**
- `AttentionRow(string Title, string Subtitle, int? ResumeIssueId, int SeriesId, string? CoverKey)`,
  `GapRow(string Title, string Missing, int SeriesId, string? CoverKey)`.
- In `PopulateLists`, before building the rows, batch-resolve cover keys for every distinct `SeriesId`
  across `snap.Continue`/`AlmostDone`/`DiveIn`/`Gaps` in one query — `context.Series.Include(s =>
  s.Issues).Where(s => ids.Contains(s.Id))` → `SeriesCardSample.FromSeries(s).CoverKey` per series —
  mirroring exactly the pattern `RefreshRecommendations` already uses for its own series lookup a few
  lines below. Build a `Dictionary<int, string?>` once, then pass the looked-up value into each
  `AttentionRow`/`GapRow` constructor call.

**Depends on:** none (independent of Steps 1-2, though Step 4/5 consume this).

**Verify:** add a case to `InsightsScreenViewModelTests.cs` asserting a `ContinueRows`/`GapRows` entry for
a series with a real cover-bearing issue gets a non-null `CoverKey`, and one for a series with no issues
gets `null` (matching `SeriesCardSample.FromSeries`'s existing null-cover behavior). `dotnet test` scoped
to `Insights`.

---

## Step 4: Today tab hero row

**Files:** `src/Paperbunkr.App/Views/InsightsScreen.axaml`, `src/Paperbunkr.App/ViewModels/GoalsViewModel.cs`,
`src/Paperbunkr.App/ViewModels/InsightsScreenViewModel.cs`, `src/Paperbunkr.App/Styles/Primitives.axaml`

**What:**
- Widen `GoalCardViewModel` record (currently `(int GoalId, string Title, string StatusText, double
  Percent, bool IsComplete)`) to add `long CurrentValue, long Target` — sourced in `GoalsViewModel.Refresh()`
  from the `GoalProgress p` already in scope there (used today only to build `StatusText`, then discarded).
- On `InsightsScreenViewModel`, expose which goal is the hero pick — simplest correct rule: the first
  entry in `Goals.Cards` ordered by nearest period end (if `GoalCardViewModel` doesn't already carry
  `PeriodEnd`, add it too, sourced from `p.Goal.PeriodEnd`, same widening as above). Add a `GoalCardViewModel?
  HeroGoal => Goals.Cards.OrderBy(c => c.PeriodEnd).FirstOrDefault();` computed property.
- New hero-row markup in `InsightsScreen.axaml`, replacing the current "GOALS" `StackPanel` + top of
  "READING": three `Border.heroTile` cards in a `Grid`/`UniformGrid` (three columns):
  - **Goal tile:** existing `stats:GoalRing` + new `TextBlock` bound to `HeroGoal`'s
    `{CurrentValue}/{Target}` (mono/monospace font weight, matches the design's data-console accent) +
    `HeroGoal.StatusText` as a caption. No sparkline (see correction #1 above). `IsVisible="{Binding
    Goals.HasGoals}"`; falls back to the existing "no goals yet" prompt when empty.
  - **Continue-reading tile:** `views:CoverThumb Classes="hero"` bound to `ContinueRows[0].CoverKey` +
    `ContinueRows[0].Title`/`.Subtitle`. No sparkline (see correction #1). `IsVisible="{Binding
    !ContinueEmpty}"`.
  - **Recommendation tile:** `views:CoverThumb Classes="hero"` bound to `Recommendations[0].Card.CoverKey`
    + `Recommendations[0].Explanation`. `IsVisible="{Binding HasRecommendations}"`. Remaining
    `Recommendations` (index ≥ 1) move to a compact list below the hero row — `CoverThumb Classes="row"` +
    `Card.Name` per row — replacing the current horizontal-scroll `ItemsControl` of `PosterTile`s.
  - When all three are empty/absent, the hero row itself collapses (no empty hero placeholder) — matches
    existing per-section empty-state precedent elsewhere on this tab.
- New `Border.heroTile` style in `Primitives.axaml` (or a new sibling style block near the existing
  `posterTile`/`shelfCard` rules) giving these three cards the same glow-on-hover/focus treatment,
  **hardcoded per pseudo-class rather than transitioned** — mirrors `posterTile`'s own documented fix for
  the BoxShadow-mid-transition tearing bug. Scoped to a new class rather than reusing `posterTile`/
  `shelfCard` verbatim, since those are chromed/sized for grid cards, not a 3-across hero row.

**Depends on:** Step 2 (`CoverThumb`), Step 3 (`CoverKey` on `AttentionRow`).

**Verify:** on-screen check — hero row renders with real library data; each tile's individual empty state
(no goals / nothing to continue / nothing to recommend) degrades correctly; hero row itself disappears
when all three are empty.

---

## Step 5: Today tab list rows gain `CoverThumb`

**Files:** `src/Paperbunkr.App/Views/InsightsScreen.axaml`

**What:** Add `views:CoverThumb Classes="row"` before the title in each of: Almost Done rows, Dive In
rows, the remaining-recommendations compact list (added in Step 4), and Collection-health gap rows — all
already carry `CoverKey` after Step 3 (or `Card.CoverKey` for recommendations, unchanged). Pure XAML,
no new bindings.

**Depends on:** Step 2, Step 3, Step 4 (for the remaining-recommendations list markup it extends).

**Verify:** on-screen check — every row shows a thumbnail or a graceful blank for a `null` `CoverKey`
(`AsyncCoverImage` already handles a missing source elsewhere in the app; confirm the same holds here).

---

## Step 6: Trends tab pill sub-nav + card regrouping

**Files:** `src/Paperbunkr.App/ViewModels/StatsScreenViewModel.cs`, `src/Paperbunkr.App/Views/InsightsScreen.axaml`,
`src/Paperbunkr.App.Tests/StatsScreenViewModelTests.cs`

**What:**
- New `TrendsGroup` enum (`Activity | Composition | TopLists`) on `StatsScreenViewModel`, plus
  `SelectedTrendsGroup` property, three `IsActivityGroupSelected`/`IsCompositionGroupSelected`/
  `IsTopListsGroupSelected` boolean properties (avoids a converter in XAML, matching this codebase's
  existing preference for boolean bindings over converters — e.g. `IsOverviewTabSelected`), a
  `TrendsGroupOptions` list + `SetTrendsGroupCommand`, and a `partial void OnSelectedTrendsGroupChanged`
  that updates each option's `IsActive` — mirroring the exact 4-part shape already used for
  `RangeOption`/`SetRangeCommand`/`GrowthMeasureOption` in this same file. Default: `Activity`.
- `InsightsScreen.axaml`: new pill row above the card stack reusing the existing `rangeChip` style
  (already the app's non-pill squircle chip convention — no new visual language needed here).
- Wrap the existing card blocks (no internal markup changes) in three groups per the design doc's split:
  - **Activity:** Highlights, Reading Activity numbers, Activity heatmap, Library growth, Reading pace,
    Backlog burn-down
  - **Composition:** Reading state donut, Media type donut, Score distribution, Content rating,
    Publication year
  - **Top Lists:** Top genres, Top tags, Top publishers, Top authors, Top artists
  Each group's outer container binds `IsVisible` to its corresponding `IsXGroupSelected` boolean. No
  changes to any card's own bindings/resolver calls.
- Confirm the ScottPlot chart code-behind (`GrowthChart`/`PaceChart`/`BurnDownChart`/`RatingsChart`/
  `PublicationYearChart`, all drawn imperatively via `Stats.ChartsChanged`) still redraws correctly when
  its containing group is hidden then reshown — `AvaPlot` controls inside a collapsed `IsVisible="False"`
  panel may need their redraw re-triggered on becoming visible again if `ChartsChanged` only fires on data
  refresh, not on visibility change. If so, add a lightweight re-draw call in
  `OnSelectedTrendsGroupChanged` for the charts belonging to the newly-selected group.

**Depends on:** none (independent of Steps 2-5).

**Verify:** add cases to `StatsScreenViewModelTests.cs` for `SetTrendsGroupCommand`/`IsXGroupSelected`
transitions (mirroring the existing `SetRangeCommand` test shape). On-screen check: switching groups
shows/hides the right cards, and every chart (Growth/Pace/BurnDown/Ratings/PublicationYear) still renders
after switching away and back to its group.

---

## Step 7: Recap cover art (Top Series, Highest Rated, Most Reread only — see correction #2)

**Files:** `src/Paperbunkr.Data/Metadata/StatsResolver.cs` (record only), `src/Paperbunkr.Data/Metadata/RecapResolver.cs`,
`src/Paperbunkr.App/ViewModels/RecapViewModel.cs`, `src/Paperbunkr.App/Views/InsightsScreen.axaml`,
`src/Paperbunkr.App/Views/RecapPosterView.axaml`, `src/Paperbunkr.Data.Tests/RecapResolverTests.cs`,
`src/Paperbunkr.App.Tests/RecapViewModelTests.cs`

**What:**
- `HighlightGroup` (in `StatsResolver.cs`, shared by both resolvers) gains two optional trailing fields:
  `int? SeriesId = null, int? IssueId = null`. `StatsResolver.cs`'s own four `HighlightGroup`
  constructions (feeding the Trends tab's Highlights card) are left unchanged — both stay `null`, unused
  by Trends UI, which doesn't get covers.
- In `RecapResolver.cs`: `ComputeTopSeries`/`ComputeHighestRatedSeries` populate `SeriesId` with the
  first tied series' `Id` (`bySeries` entries already carry the `Series` entity — take `.Id` from the
  first one matching `max`, same element `DisplayTitle` already privileges via `Titles[0]`).
  `ComputeMostRereadItem` populates `IssueId` with the first tied item's id, **only** when `ItemType ==
  ReadingItemType.Comic` — matches this function's existing Comic-only name-resolution behavior, no new
  asymmetry introduced (Book rereads still show no cover, same as they show no name today).
- `RecapTile` widened: `RecapTile(string Label, string Value, string? CoverKey = null)`.
- `RecapViewModel.Refresh()`: after `RecapResolver.Build` returns `snap`, resolve the up-to-3 relevant
  `SeriesId`/`IssueId`s to a `CoverKey` using the same `context` already open — `SeriesCardSample.FromSeries`
  for the two series-identity fields, `CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize)`
  (via a direct `context.Issues.Find`/query) for the issue-identity field — then pass the result into the
  corresponding `RecapTile` in `BuildTiles`. (This lookup lives in `RecapViewModel`, not `RecapResolver`,
  because `CoverFingerprint`/`SeriesCardSample` are `Paperbunkr.App` types the `Paperbunkr.Data` project
  can't reference.)
- Add `RecapViewModel.CurrentSlideCoverKey` (mirrors `CurrentSlideValue`'s indexed-lookup shape) for the
  in-app slide viewer. In `InsightsScreen.axaml`'s Recap tab, show `views:CoverThumb Classes="hero"
  CoverKey="{Binding Recap.CurrentSlideCoverKey}"` next to the slide value when non-null.
- `RecapPosterView.axaml`'s single shared `DataTemplate x:DataType="vm:RecapTile"` (used for
  `GridTiles`) gets the same `CoverThumb`, shown only when `CoverKey` is non-null — since all 8 grid
  tiles already share one template, this one edit covers all three affected poster cells automatically.

**Depends on:** Step 2 (`CoverThumb`).

**Verify:** `RecapResolverTests.cs` gets cases asserting `SeriesId`/`IssueId` populate for Top
Series/Highest Rated/Most Reread, including a tie case asserting it's the *first* tied item (matching
`DisplayTitle`'s own convention). `RecapViewModelTests.cs` gets a case for `CoverKey`/
`CurrentSlideCoverKey` resolution. On-screen check: slide navigation shows covers on the 3 relevant
slides only, and PNG export includes them in the same 3 poster cells.

---

## Step 8: Full on-screen verification pass (all five previously-unchecked Insights slices)

**Files:** none (manual + test-run step)

**What:** Launch the app and walk through, per the design doc's Verification section:
- Goal creation flow end-to-end (Track/Target/Period/Scope, Title auto-suggest, Create) and confirm the
  resulting ring/mono-readout render correctly in the new hero tile, including the "nearest deadline"
  hero-pick rule when more than one goal exists.
- Recap slide navigation (prev/next, dots) and PNG export, confirming the new per-slide covers appear
  correctly and don't break the export layout.
- Recommendation hero tile and remaining-candidates list both navigate to Detail on click.
- Backlog burn-down and period-over-period sparkline/chart rendering are unaffected by the Trends
  regrouping — including their existing empty states (`HasBurnDownData`/`HasGrowthData`-style) still
  showing correctly inside their new group.
- Collection-health gap-row navigation still works with its new thumbnail.
- Run the scoped test filter `Insights|Stats|Recap|Goal|Recommendation|Trends` across both
  `Paperbunkr.App.Tests` and `Paperbunkr.Data.Tests`; `dotnet build` clean on both `Paperbunkr.Data` and
  `Paperbunkr.App`.

**Depends on:** Steps 1-7.

---

## Step 9: `avalonia-pro-max/review-checklist` pass

**Files:** none (review step, fixes applied inline wherever it finds something)

**What:** Read `~/.claude/skills/avalonia/avalonia-pro-max/review-checklist/SKILL.md` directly off disk
(per this project's documented convention — `Skill()` calls to subskill names fail silently or loudly)
and run its checklist against every new/changed view from Steps 1-7. Particular attention to its
reduced-motion check: the new hero-tile hover/glow and any transitions must respect the app's existing
reduced-motion setting, and the glow implementation must follow `posterTile`'s own hardcoded-per-
pseudo-class precedent (never a `BoxShadowsTransition` on these selectors) per the documented tearing bug.

**Depends on:** Steps 1-7.
