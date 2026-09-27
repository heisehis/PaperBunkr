# Insights pitch slice 5 (final) — Recommendations surface — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-23-insights-recommendations-surface-design.md*

Smallest slice of the queue: no new entity, no migration, no scheduled task. Reuses
`RecommendationResolver`/`HomeFeedResolver`/`PosterTile`/`SeriesCardSample` exactly as they already exist.

## Step 1: `InsightsRecommendationResolver`

**Files:** `src/Paperbunkr.Data/Metadata/InsightsRecommendationResolver.cs` (new)

**What:**
```csharp
public static class InsightsRecommendationResolver
{
    public static InsightsRecommendationSeed? GetSeedWithRecommendations(PaperbunkrDbContext context);
}

public sealed record InsightsRecommendationSeed(int SeedSeriesId, string SeedSeriesName, IReadOnlyList<RecommendationCandidate> Recommendations);
```
- Candidate seeds: `context.ReadingEvents.AsNoTracking().Where(e => e.Kind == ReadingEventKind.Finished &&
  e.SeriesId != null).ToList()`, group by `SeriesId`, order by `Max(TimestampUtc)` descending, take 5
  distinct series ids.
- Home's exclusion set: `HomeFeedResolver.GetRecentlyOpenedSeriesIds(context, count: 3)`, then for each,
  `RecommendationResolver.GetRecommendations(context, seedId)` (default `limit: 10`), union all
  `TargetSeriesId`s into one `HashSet<int>`.
- For each of the up-to-5 candidate seeds in order: `RecommendationResolver.GetRecommendations(context,
  seedId, limit: 20)`, `.Where(r => !exclusionSet.Contains(r.TargetSeriesId))`, `.Take(6).ToList()`. First
  seed with `Count > 0` wins — look up its `Series.Name` and return immediately.
- No seeds, or all 5 exhausted with nothing surviving exclusion: return `null`.

**Depends on:** none

**Verify:** `dotnet build src/Paperbunkr.Data/Paperbunkr.Data.csproj`; covered by Step 2.

## Step 2: `InsightsRecommendationResolverTests`

**Files:** `src/Paperbunkr.Data.Tests/InsightsRecommendationResolverTests.cs` (new)

**What:** Same fixture shape as `RecommendationResolverTests`/`RecapResolverTests` (reuse
`InsightsResolverTests.SeedSeries`/`SeedIssue`/`SeedEvent`, and `MediaRelationResolver.TryCreate` for
seeding a real relational anchor exactly like `RecommendationResolverTests.cs:67` does). Cases:
- A recently-finished series with a real `MediaRelation` to another series returns that target with its
  `Explanation` intact, seed name populated.
- A target that's also in Home's exclusion set (seed it via `HomeFeedResolver`'s own recently-opened
  criteria on a *different* series, related to the *same* target) is filtered out; if that was the only
  candidate, the resolver falls through to the next most-recently-finished seed instead of returning it.
- All candidate seeds' recommendations fully excluded → returns `null`.
- No `Finished` events at all → returns `null`.
- Two finished series, both with real recommendations — the more-recently-finished one is the one returned
  (proves ordering, not just "first with any results").

**Depends on:** Step 1

**Verify:** `dotnet test --filter FullyQualifiedName~InsightsRecommendationResolverTests`

## Step 3: Wire into `InsightsScreenViewModel`

**Files:** `src/Paperbunkr.App/ViewModels/InsightsScreenViewModel.cs` (edit)

**What:**
```csharp
public sealed record InsightsRecommendationCard(SeriesCardSample Card, string Explanation);
```
(new small record, same file as the other small Insights-family records at the bottom, e.g.
`AttentionRow`/`GapRow`)
- `[ObservableProperty] private string? _recommendationsSeedName;`
- `public ObservableCollection<InsightsRecommendationCard> Recommendations { get; } = new();`
- `public bool HasRecommendations => Recommendations.Count > 0;`
- In `Refresh()`, alongside the existing `Goals.Refresh()` call (unconditional, same reasoning as Goals -
  this section lives on Overview, which is always refreshed when this screen is refreshed): call
  `InsightsRecommendationResolver.GetSeedWithRecommendations(context)` (same `context` already open in this
  method for `InsightsResolver.Build`, or a fresh one if that's already disposed by this point - check the
  existing method body's `using` scope before deciding), and if non-null, resolve each
  `RecommendationCandidate.TargetSeriesId` to a `Series` (loaded with `.Include(s => s.Issues)`, matching
  `HomeScreenViewModel.BuildSnapshot`'s own query shape at `HomeScreenViewModel.cs:315-318` exactly, since
  `SeriesCardSample.FromSeries` needs `series.Issues` populated), map to `SeriesCardSample.FromSeries(target)`
  + the candidate's own `Explanation`, replace `Recommendations`' contents, set `RecommendationsSeedName`,
  and raise `OnPropertyChanged(nameof(HasRecommendations))`. When the resolver returns `null`, clear
  `Recommendations` and set `RecommendationsSeedName = null`.
- Reuse the existing `OpenSeriesCommand` (`InsightsScreenViewModel.cs`'s current `[RelayCommand] private void
  OpenSeries(int seriesId) => _goDetailForSeries(seriesId);`) for navigation - no new command needed, since
  `InsightsRecommendationCard.Card.SeriesId` already carries the int id `PosterTile`'s `CommandParameter` can
  bind to directly.

**Depends on:** Step 1

**Verify:** `dotnet build`; covered by Step 4.

## Step 4: ViewModel test coverage

**Files:** `src/Paperbunkr.App.Tests/InsightsScreenViewModelTests.cs` (edit - add cases to the existing file,
not a new one, matching how `RecapViewModel`/`Goals` composition is already exercised through this same
`InsightsScreenViewModel` test file's own `Refresh()`-driven assertions where applicable, or check if a
dedicated small test class reads better once the real diff is in front of you)

**What:** A seeded `Finished` event + a real `MediaRelation` produces a populated `Recommendations`
collection and `HasRecommendations == true` after `Refresh()`; an empty library leaves `Recommendations`
empty and `HasRecommendations == false` without throwing.

**Depends on:** Step 3

**Verify:** `dotnet test --filter FullyQualifiedName~InsightsScreenViewModelTests`

## Step 5: Wire into `InsightsScreen.axaml`

**Files:** `src/Paperbunkr.App/Views/InsightsScreen.axaml` (edit)

**What:** New section on the Overview tab, between the existing "READING" `StackPanel` and the "Collection
health" `Border Classes="card"` block. `IsVisible="{Binding HasRecommendations}"` on the whole section (no
empty-state placeholder, per the design doc). Header: a heading-style `TextBlock`/`controls:SplitText` (check
which this file already uses for section headers other than the plain `sectionLabel` style - `HomeScreen.axaml`
uses `controls:SplitText Classes="heading"` for its own "Because You Read" header) bound to
`RecommendationsSeedName` with `StringFormat='Because you finished {0}'`. Row: `ScrollViewer
HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled"` containing an `ItemsControl
ItemsSource="{Binding Recommendations}"` with a horizontal `StackPanel` items panel (mirrors
`HomeScreen.axaml:380-397`'s exact shape), each item a `DataTemplate x:DataType="vm:InsightsRecommendationCard"`
wrapping a `StackPanel` around: `views:PosterTile CoverSource="{Binding Card.CoverKey, Converter={x:Static
views:CoverImageConverter.Instance}}" TitleText="{Binding Card.Name}" BadgeText="{Binding Card.IssueCountLabel}"
Command="{Binding $parent[ItemsControl].((vm:InsightsScreenViewModel)DataContext).OpenSeriesCommand}"
CommandParameter="{Binding Card.SeriesId}"` (check `SeriesCardSample`'s exact property names -
`HomeScreen.axaml:389-391` is the source of truth for `CoverKey`/`Name`/`IssueCountLabel`, confirm they match
before writing this) plus a `TextBlock Classes="sub" Text="{Binding Explanation}" TextWrapping="Wrap"
MaxWidth="{same width as the PosterTile above it}"` underneath, always visible per the design's visual-companion
decision (not a tooltip).

**Depends on:** Step 3

**Verify:** `dotnet build`; manual/on-screen only for actual appearance (Step 6) - matches every prior
Insights-family UI step's own precedent.

## Step 6: On-screen verification (manual)

Not automatable. With a library that has both `Finished` reading history and at least one real relational
link (`MediaRelation`/shared `Continuity`/shared `StoryEvent`/shared `Collection`) between a recently-finished
series and another series in the library: open Insights → Overview, confirm the new section appears between
READING and Collection health with the correct "Because you finished {X}" header, the covers/titles/badges
render correctly via the reused `PosterTile`, each explanation caption is visible and makes sense, clicking a
tile navigates to that series' Detail screen, and the section is entirely absent when no such relational
history exists (e.g. a fresh/small library). Also confirm it doesn't show anything currently also shown in
Home's own "Because You Read" rows, if both happen to have content at the same time.

## Notes for the implementer

- This is the last slice in the 5-item Insights pitch queue - no follow-on slice after this one.
- Do not touch `HomeScreenViewModel.cs`/`HomeScreen.axaml` - this is purely additive on Insights.
- Confirm `SeriesCardSample`'s exact bindable property names (`CoverKey`/`Name`/`IssueCountLabel` etc.)
  against the real class before writing Step 5's XAML rather than trusting this plan's paraphrase.
