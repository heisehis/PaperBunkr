# Insights section redesign — design

Date: 2026-09-23. Status: ~~**approved, not yet implemented.**~~ **Built, uncommitted** — confirmed
2026-09-26 via source: `InsightsScreenViewModel`/`InsightsScreen.axaml` modified accordingly (plan's
own "corrections found during planning" notes the dropped sparklines). On-screen check by the user
still outstanding.

**CE parity:** the Insights section (Overview/Stats/Recap and everything on it — goals, recap,
recommendations, backlog burn-down, period deltas) is entirely Paperbunkr-original; ComicRack CE has no
reading-insights/dashboard concept at all. No CE behavior to match here — this is a from-scratch UI/IA pass
over Paperbunkr's own accumulated feature set, per the same deliberate-deviation precedent already recorded
in every one of this week's Insights-slice design docs.

## Why now

Five feature slices (period-over-period deltas, nightly-snapshot backlog burn-down, year-in-review recap,
reading goals, recommendations surface) landed on this screen over 2026-09-21 through -23, each with its
own design doc, each shipped **uncommitted** and **never checked on screen**. They were added incrementally
to a screen that predates all of them (`InsightsScreen.axaml`, plain `card`/`sectionLabel`/`bigNumber`
styling, no relation to the cosmetics-pitch visual language now used across Library/Detail/Home/About/
Automation). The result: an "Overview" tab that's really become a mixed bag of actionable-today items and
a static health widget, and a "Stats" tab that's a 15-card linear scroll with no internal structure. This
redesign gives the accumulated feature set an actual information architecture and a visual identity, and
uses the opportunity (every card's markup gets touched anyway) to do the first on-screen verification pass
of all five slices.

## Scope

UI/IA only. No backend, resolver, or data-model changes — every section below consumes exactly the data
`StatsResolver`/`GoalResolver`/`RecapResolver`/`InsightsRecommendationResolver` already compute. No change
to the Recap PNG export mechanism. No other screen touched.

## 1. Tab rename (structure unchanged, naming corrected)

Three tabs stay three tabs — renamed to say what they now actually do:

| Current | New |
|---|---|
| Overview | **Today** |
| Stats | **Trends** |
| Recap | Recap (unchanged) |

Mechanical rename across `InsightsScreenViewModel` (`IsOverviewTabSelected` → `IsTodayTabSelected`,
`SelectOverviewTabCommand` → `SelectTodayTabCommand`, `IsStatsTabSelected` → `IsTrendsTabSelected`,
`SelectStatsTabCommand` → `SelectTrendsTabCommand`) and the corresponding bindings in
`InsightsScreen.axaml`, plus the existing test names in `InsightsScreenViewModelTests.cs`,
`InsightsChartPolishTests.cs`, and `StatsScreenViewModelTests.cs` that reference them. Purely internal
symbol names — nothing persisted, no migration implication.

## 2. Today tab

**Hero row** — three tiles replacing the current stacked "GOALS" section + top of "READING":

- **Goal** tile: existing `GoalRing` (glow-tier squircle card, accent ring showing percent-to-target) plus a
  monospace current/target readout (`36/50`) and a small `TrendSparkline` beneath it showing recent pace.
  When there's more than one active goal, this tile shows the nearest-to-deadline goal; the rest remain
  reachable via "Add goal" → the existing goal list (unchanged CRUD flow from slice 4).
- **Continue-reading** tile: same glow-tier squircle treatment, now showing the series' cover art
  (`CoverThumb`, see below) next to title/subtitle, with a thin sparkline reflecting recent reading pace for
  that series.
- **Recommendation** tile: same glow-tier squircle treatment, series cover art (`CoverThumb`) plus the
  `InsightsRecommendationResolver` explanation text beneath — this replaces the current horizontally-
  scrolling recommendation strip with a single hero pick; if there's more than one candidate, the rest move
  below the hero row as a compact list (see below), still following the "absent entirely when there's
  nothing to recommend" rule from slice 5.

Glow is **only** used in this hero row — it's Paperbunkr's existing signal for "this needs your attention /
is actionable," consistent with how it's used in Library/Detail. It is not used anywhere in Trends.

**Below the hero**, unchanged logic, restyled rows:

- "Reading" section (Almost Done, Dive In) — same `AttentionRow` data, but each row now shows a
  `CoverThumb` next to the title instead of text-only.
- Remaining recommendation candidates beyond the hero pick (if any) — same treatment, `CoverThumb` + title.
- "Collection health" gap rows — same `GapRow` data, `CoverThumb` next to each series with missing issues.

**New global rule for this screen:** any row or tile that names one specific series/issue/book shows that
item's cover via `CoverThumb` — never text-only. Applies to every list above and to Recap (section 4).

### `CoverThumb` — new shared control

A small `UserControl` (`Views/CoverThumb.axaml` + code-behind, per this project's own AVLN2000 build
gotcha — code-behind added in the same step as the `.axaml`) wrapping the existing `CoverImageConverter`
with a fixed rounded clip, used at two sizes:

```
CoverThumb.CoverKey    (StyledProperty<string?>)   -- series/issue cover key, same value PosterTile.CoverSource takes today
CoverThumb.Size        (StyledProperty<CoverThumbSize>)  -- Hero (46x64) | Row (28x40)
```

Internally: `Border CornerRadius="{DynamicResource PbRadius}" ClipToBounds="True"` containing an `Image`
bound through `CoverImageConverter`, `Stretch="UniformToFill"`. This avoids duplicating
Border+Image+Converter markup at the five-plus call sites above (hero tiles, Reading rows, gap rows, Recap
slides) and keeps corner radius consistent with the rest of the app's card language (not a pill —
per this project's squircle convention, `PbRadius`, same token `Border.card` already uses). `PosterTile`
itself is unchanged and untouched — it's sized/chromed for grid/hero contexts elsewhere (Home, Library) and
isn't a fit for a 28×40 inline row icon.

## 3. Trends tab

Same fifteen cards as today's Stats tab, no data changes, reorganized under a pill sub-nav (`rangeChip`
styling already used for the range selector — same non-pill squircle convention, not `CornerRadius 999`):

- **Activity** — Highlights, Reading Activity numbers, Activity heatmap, Library growth, Reading pace,
  Backlog burn-down
- **Composition** — Reading state donut, Media type donut, Score distribution, Content rating, Publication
  year
- **Top Lists** — Top genres, Top tags, Top publishers, Top authors, Top artists

Implementation: a `SelectedTrendsGroup` enum property (`Activity | Composition | TopLists`) on
`StatsScreenViewModel`, three `IsVisible` bindings gating the existing card blocks in `InsightsScreen.axaml`
— pure XAML visibility partition, no changes to any card's internal markup or the resolver data feeding it.
Default group: Activity (matches current top-of-scroll content, least disruptive to muscle memory).

Visual register for Trends cards: monospace numerals and sparklines where they already exist (Finished/Avg-
to-finish/Pace already have `TrendSparkline` — unchanged), but **no glow** on any Trends card. Glow stays
reserved for Today's hero row as an attention signal, not applied here as ambient decoration.

## 4. Recap tab

Structurally and visually unchanged — keeps its own distinct poster/narrative identity (slide dots, large
celebratory numbers, the existing `RecapPosterView` PNG export). The only change: slides that name a
specific item — Highest rated, Most reread, Longest journey — now show that item's cover
(`CoverThumb`, `Hero` size) next to the stat instead of title text alone. `RecapViewModel`'s existing slide
data already carries series/issue identity (used today only for click-through, if any); this reuses the
same key to drive `CoverThumb.CoverKey` rather than adding new resolver output.

## Verification (in addition to normal build/test gates)

Because this pass touches the markup of every one of the five previously-unverified Insights slices,
implementation includes launching the app and confirming each still works functionally as it's restyled,
not just that it looks right:

- Goal creation flow (Track/Target/Period/Scope, Title auto-suggest, Create) and the resulting ring/progress
  still render and update correctly in the new hero tile.
- Recap slide navigation (prev/next, dots) and PNG export still work with the new per-slide cover art.
- Recommendation card click-through still navigates to Detail.
- Backlog burn-down and period-over-period sparkline/chart rendering are unaffected by the Trends
  regrouping (i.e. `HasBurnDownData`/`HasGrowthData`-style empty states still show correctly inside their
  new group instead of always-visible).
- Collection-health gap-row navigation still works with `CoverThumb` added.

Standard gates: `dotnet build` clean on `Paperbunkr.App`/`Paperbunkr.Data`, scoped
`Insights|Stats|Recap|Goal|Recommendation|Trends` test filter across both suites, and the
`avalonia-pro-max/review-checklist` subskill run before calling the UI work done (per this project's
mandatory UI-foundation rule) — in particular its reduced-motion check, since the hero tiles' sparklines and
any hover/glow transitions must respect the app's existing reduced-motion setting rather than introducing
new unconditional animation.

## Out of scope

- No new entities, migrations, or resolver logic.
- No change to how goals/recap/recommendations are computed — only where and how they're displayed.
- No change to any other screen (Home, Library, Detail) even though `CoverThumb` could theoretically be
  reused there later — that's a future call, not part of this pass.
