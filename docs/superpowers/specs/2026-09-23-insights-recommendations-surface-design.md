# Insights pitch slice 5 (final) — Recommendations surface (#9) — design

Date: 2026-09-23. Source: "Insights pitch" item #9, exact wording from `docs/Paperbunkr-Roadmap.md`
("Recommendations surface — UI for the existing backend-only Phase 6a recommendation engine: a 'Because you
finished X' row on the Overview tab, each suggestion showing why it was picked"), last of the 5-item queue
decomposed during slice 1's brainstorm (`docs/superpowers/specs/2026-09-22-insights-period-over-period-
deltas-design.md`'s Scope section). Status: ~~**approved, not yet implemented.**~~ **Built,
uncommitted** — confirmed 2026-09-26 via source: `InsightsRecommendationResolver` present and
untracked, wired into `InsightsScreenViewModel`. On-screen check by the user still outstanding.

## Scope

**Premise correction, found before writing this doc, not after:** the pitch text ("backend-only, no UI")
is stale. The recommendation engine (`RecommendationResolver.GetRecommendations`, Metadata Model Phase 6a,
shipped 2026-08-18) already has a live UI — the Home screen's "Because You Read" module
(`HomeScreenViewModel.cs`, shipped 2026-08-23), which seeds from the 3 most **recently-opened** series and
renders up to 3 `PosterTile` rows, without ever showing the `Explanation` text the resolver already
computes. Confirmed by direct code inspection, not assumed.

The real gap that survives that correction, and what this slice actually builds: a **distinct** section on
the Insights Overview tab, seeded by the most recently **finished** series (not opened — a different
signal, matching the pitch's literal "because you finished X"), showing the `Explanation` text per
suggestion (which Home never does), and explicitly excluding anything already appearing in Home's current
"Because You Read" picks (confirmed during this brainstorm: this needs no real cross-screen coupling —
Home's exclusion set is computed by calling the same already-public, stateless
`HomeFeedResolver.GetRecentlyOpenedSeriesIds`/`RecommendationResolver.GetRecommendations` functions Home
itself calls, not by reaching into `HomeScreenViewModel`).

No changes to `RecommendationResolver` itself, `HomeScreenViewModel`, or `HomeScreen.axaml` — this is a
purely additive, independent surface.

## Read side — `InsightsRecommendationResolver`

New static class, `Paperbunkr.Data.Metadata`, alongside `RecapResolver`/`GoalResolver`:

```csharp
public static class InsightsRecommendationResolver
{
    public static InsightsRecommendationSeed? GetSeedWithRecommendations(PaperbunkrDbContext context);
}

public sealed record InsightsRecommendationSeed(
    int SeedSeriesId, string SeedSeriesName, IReadOnlyList<RecommendationCandidate> Recommendations);
```

Algorithm:
1. **Candidate seeds**: distinct `SeriesId` from `ReadingEvent`s where `Kind == Finished` and `SeriesId is
   not null`, grouped and ordered by each series' latest `Finished` `TimestampUtc` descending, top 5 (a
   fixed, small try-list — not user-configurable). This mirrors `HomeFeedResolver.GetRecentlyOpenedSeriesIds`'s
   shape but off `ReadingEvent` (the authoritative finish log every other Insights-family resolver already
   uses) rather than `Issue.OpenedTime`.
2. **Home's current exclusion set**: `HomeFeedResolver.GetRecentlyOpenedSeriesIds(context, count: 3)`, then
   `RecommendationResolver.GetRecommendations(context, seedId)` (default `limit: 10`) for each, unioned into
   one `HashSet<int>` of `TargetSeriesId`s — exactly the computation `HomeScreenViewModel.BuildSnapshot`
   already performs for its own "Because You Read" rows, recomputed here rather than read from anywhere
   shared (both screens independently compute the same live query, consistent with every other
   Insights-family resolver's "no persistence, no cross-screen state" shape).
3. For each of the up-to-5 candidate seeds in order: call `RecommendationResolver.GetRecommendations(context,
   seedId, limit: 20)` (a generous limit so there's enough left after exclusion), filter out any candidate
   whose `TargetSeriesId` is in the exclusion set, take the first 6. If ≥1 remains, return
   `InsightsRecommendationSeed` for this seed immediately (don't keep searching once one succeeds).
4. If every candidate seed yields zero after exclusion (or there are no `Finished` events at all), return
   `null`.

## UI — Overview tab section

- **Placement**: a new full-width section on `InsightsScreen.axaml`'s Overview tab, between the existing
  "READING" attention-card section and "Collection health".
- **Header**: `"Because you finished {SeedSeriesName}"` (`SplitText`/heading style, matching Home's own
  `StringFormat='Because you read {0}'` idiom for the analogous copy).
- **Row**: a horizontally-scrolling `ItemsControl` (same `ScrollViewer HorizontalScrollBarVisibility="Auto"`
  + horizontal `StackPanel` shape as Home's own "Because You Read" row), one item per recommendation: the
  existing `PosterTile` control (`CoverSource`/`TitleText`/`BadgeText`/`Command`/`CommandParameter`, reused
  exactly as `HomeScreen.axaml` already uses it — no fork), plus a small `TextBlock` underneath each tile
  bound to that recommendation's `Explanation` string (confirmed via this brainstorm's visual companion pass
  — always-visible captions, not a hover tooltip, since showing the "why" is this slice's whole point).
- **Mapping**: the ViewModel (not the resolver — `SeriesCardSample` is an `App`-layer model, so this mapping
  stays in `Paperbunkr.App` exactly like `HomeScreenViewModel.BuildSnapshot` already does it) resolves each
  `RecommendationCandidate.TargetSeriesId` to a `Series` row and calls the existing
  `SeriesCardSample.FromSeries(target)` factory — no duplicated cover/title/badge logic.
- **Navigation**: clicking a tile opens that series' Detail screen, same `_goDetailForSeries` callback
  `InsightsScreenViewModel` already holds for other Overview-tab rows.
- **Empty state**: when `GetSeedWithRecommendations` returns `null`, the whole section is absent —
  `IsVisible` false, no placeholder text. Recommendations aren't something the user can act on the way an
  empty goal list is ("add one"), so there's nothing constructive an empty-state message would prompt.
- **Refresh cadence**: same as `Goals` — refreshed unconditionally whenever `InsightsScreenViewModel.Refresh()`
  runs (which itself only happens while the Insights screen is the visible screen), no independent
  `ReadingEventRecorded` subscription of its own (same reasoning as `GoalsViewModel`'s own design doc note:
  the parent's cascade already covers it, a second subscription would just recompute twice per event).

## Error handling

- A series that later gets deleted from the library between resolution and render isn't a real race in
  practice (this is all synchronous, single-context-lifetime computation per refresh), but if a target
  `Series` row is ever missing when mapping candidates to `SeriesCardSample`, it's silently skipped — same
  tolerance `HomeScreenViewModel.BuildSnapshot`'s own `targetSeriesById.TryGetValue` guard already applies.

## Testing

- **`InsightsRecommendationResolverTests`** (new, `Paperbunkr.Data.Tests`): a seed with real relational
  recommendations returns them with `Explanation` intact; a seed whose only recommendations are all in
  Home's exclusion set falls through to the next-most-recently-finished seed; all 5 candidate seeds
  exhausted with nothing surviving returns `null`; no `Finished` events at all returns `null`; a case
  confirming the returned seed is genuinely the most-recently-finished series when multiple exist and the
  top one already yields results (proving it doesn't skip ahead unnecessarily).
- **On-screen verification only** for the actual row rendering, explanation captions, and click-to-Detail
  navigation — matches every other Insights-family UI slice's own precedent (no automated visual test for
  imperative/visual layout in this codebase).

## Out of scope

This closes the 5-item Insights pitch queue. No further slices are queued after this one. `HomeScreenViewModel`/
`HomeScreen.axaml` are explicitly untouched — if Home's own module ever wants to show `Explanation` text too,
that's a separate, later decision, not bundled into this slice.
