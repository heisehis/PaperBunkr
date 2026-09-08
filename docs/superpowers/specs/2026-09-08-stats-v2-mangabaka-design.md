# Stats v2 — Insights/Stats split, MangaBaka-inspired analytics — design

**Date:** 2026-09-08
**Status:** design — awaiting user review before `writing-plans`
**Topic:** split the existing "Insights" screen into a lean actionable "Insights" and a new
analytics-only "Stats" nav-rail destination, expanding the curiosity content to a MangaBaka-style
section set, using only data the app already has or can cheaply derive.

---

## 1. Summary

Today's "Insights" screen (shipped 2026-09-06, `docs/superpowers/specs/2026-09-05-insights-
dashboard-design.md`) mixes two different jobs on one page: an actionable "what should I read
next" section (READING) and a curiosity "what are my habits" section (AT A GLANCE + four charts).
The user asked for a v2 redesign of "the stats menu" using MangaBaka's own stats page (screenshots
supplied) as visual and structural inspiration.

This design:

1. **Splits the screen in two.** "Insights" keeps only the click-through content (READING +
   Collection health). A new "Stats" nav-rail destination gets everything else, expanded to a
   MangaBaka-style section set — Highlights, Reading Activity, Activity Heatmap, Library Growth
   over time, Reading pace, Library Breakdown (real reading-status donut + media-type donut),
   Score Distribution, Content Rating, Publication Year, Top Genres/Tags, Top Publishers/Authors/
   Artists.
2. **Reuses data already on hand.** `ReadingEvent`, `ReadingStatus`, `ContentType`, `IssueTag`,
   `Issue.AddedTime`/`Year`/`AgeRating`, and the existing Writer/Penciller/Inker/Colorist/Publisher
   fields cover the entire new section list without new tracking. Three MangaBaka sections
   (Licensed %, Has-Anime-Adaptation %, You-vs-Community) do **not** have clean data and are out
   of scope (§2).
3. **Formalizes a categorical chart palette** (the app's existing accent/badge/success/danger plus
   two new supporting hues) in `InsightsChartTheme`, and introduces a shared `StatCard`/`ChartCard`
   control so ~12 new sections don't each hand-roll their own `Border.card`.

Everything stays local and deterministic — no LLM, no network, no external service.

## 2. Goals / non-goals

**Goals**

- Two nav-rail destinations: "Insights" (do something) and "Stats" (look at something).
- Stats adopts every MangaBaka section that's buildable from data the app already has.
- Reading-state breakdown switches from today's ad hoc Read/In-progress/Unread split to the real
  `ReadingStatus` enum (`Unknown/Planned/Reading/Completed/Paused/Dropped/ReReading`).
- A shared card component and a formalized categorical chart palette, so the larger tile count
  doesn't multiply one-off styling.

**Non-goals (v2)**

- **Licensed %** — no `IsLicensed` field anywhere in the model. Dropped entirely, not deferred.
- **Has-Anime-Adaptation %** — `MediaRelation`/`RelationType.Adaptation` exists but both sides of a
  relation are Paperbunkr library items; there's no "external anime" node, so the signal isn't
  clean. Dropped entirely.
- **"You vs Community" scatter** — needs a community score persisted in bulk. The `ExternalRating`
  entity exists (Metadata Model Phase 5a) but no adapter (AniList, MangaBaka) actually writes to it
  from a real fetch today — this is backend/adapter work, not a UI redesign. **Deferred** to a
  future "smart features" initiative, not built here.
- **"Chapters" as a Library-Growth measure unit** — Paperbunkr has no chapter-level granularity
  (one `Issue` = one file, whatever unit it represents). **Deferred** alongside You-vs-Community;
  Library Growth ships with Series/Issues as its only two measures.
- Configurable thresholds, a Preferences surface for any of this, incrementally-maintained
  aggregate tables, live chart restyle mid-session, exporting stats — all carried forward from the
  v1 doc's own non-goals, unchanged.

## 3. CE-parity note

Unchanged from the v1 doc: ComicRack CE has no reading-stats screen and no reading-history log.
One addition worth noting: the new **Highest Rated** highlight (§6.1) is *not* a new deviation —
CE's own `ComicBookSeriesStatistics`/`IComicBookStatsProvider` already compute a per-series average
rating. Everything else new in this doc (ReadingEvent-derived highlights, the heatmap, growth
chart) stays in the same "deliberate deviation" bucket the v1 doc already established.

## 4. Content split — what moves where

**Rule:** if a tile click-throughs to somewhere you'd act on it (a reader, a series, a filtered
Library view), it's Insights. If it's a number to look at with no such destination — or its only
"destination" is exploratory filtering, not a reading prompt — it's Stats.

| Today (Insights) | v2 destination |
|---|---|
| READING (Continue / Almost done / Dive in) | **Insights** — unchanged |
| Collection health | **Insights** — unchanged place, still click-through rows to Library |
| AT A GLANCE tiles (Read all time, Reading-day streak, Finish streak, Finished · range) | **Stats** — no click-through today, pure numbers |
| Reading pace chart | **Stats** |
| Completion donut | **Stats** — superseded by the real-`ReadingStatus` Library Breakdown donut (§6.5) |
| Library composition (by publisher) | **Stats** — folded into Top Publishers (§6.9) |
| Your ratings chart | **Stats** — renamed Score Distribution (§6.6), same data |

Exploratory drill-down (a bar → Library filtered to that value) is **kept** on the Stats tiles that
already had it (composition, ratings) and extended to the new Top-N tiles — that's browsing your
own collection, not a "read next" prompt, so it doesn't violate the split.

## 5. Insights (slimmed)

No behavior change from the shipped v1 screen except removing everything in the "Stats" column of
§4's table. `InsightsResolver`/`InsightsSnapshot` shrink to just the Reading attention lists +
`GapRow`s (Collection health) — `Lifetime`, `ReadingDayStreak`, `FinishStreak`, `FinishedInRange`,
`Pace`, `Completion`, `Composition`, `Ratings` move to the new `StatsSnapshot` (§7).

## 6. Stats (new screen)

### 6.1 Highlights (top row)

Six cards, `WrapPanel` like today's READING row. Ties render as "*Title* and *N* others" (matches
MangaBaka's own copy). Empty state per card: "Not enough data yet."

| Card | Data | Caveat |
|---|---|---|
| **Highest rated** | Series with the highest average `Issue.Rating` among its rated issues (min 1 rated issue) | None — reuses CE's own average-rating concept (§3) |
| **Most reread** | Item with the most `ReadingEvent` rows where `Kind == Finished`, grouped by `ItemId` | Backfill inserts at most one `Finished` per item for pre-2026-09-05 history — undercounts old rereads. Ships now with the same self-correcting footnote as the pace chart. |
| **Longest journey** | Item with the largest day-span between its earliest real `Opened` and latest real `Finished` `ReadingEvent` | **Excludes backfilled rows entirely** (not just flagged — actually filtered out). A backfilled item's `Opened`/`Finished` collapse to the same timestamp; including it would show a false "0-day journey" record. Empty until at least one real post-2026-09-05 pair exists. |
| **Fastest completion** | Same source as above, smallest non-zero real span | Same backfill exclusion. |
| **Plan to read** | Count of `Series` with `ReadingStatus == Planned` | — |
| **Zero progress** | Count of `Series` where `ReadingStatus != Planned` **and** no `Issue` in the series has ever had an `Opened` `ReadingEvent` | Matches MangaBaka's own framing ("added but never started, excl. plan to read") — a series can be `Reading`/`Unknown`/etc. in status yet still never actually opened if the status was set by hand or migrated from CE without a real open. |

### 6.2 Reading Activity + lifetime + streaks

One card, carrying forward the four AT A GLANCE tiles unchanged (Read all time, Reading-day streak,
Finish streak, Finished · range) plus two new MangaBaka-style averages:

- **Avg days to complete** — mean of the same real (non-backfilled) Opened→Finished spans used for
  Longest Journey/Fastest Completion, across all items with at least one such span.
- **Avg issues per day** — total `Finished` count over the selected range ÷ days in range (same
  basis as the existing pace chart, just expressed as a rate instead of a bar series).

Both are range-aware (respond to the 30d/90d/12mo/All-time selector, §6.10).

### 6.3 Activity Heatmap

Day-of-week rows × week/month columns (matches MangaBaka's layout), one cell per calendar day,
colored by count of `ReadingEvent` rows (`Opened` or `Finished`) that day. Tooltip on hover shows
date + count. Local time, same convention as the existing streak math. Ignores the range selector —
always shows the full history to date.

**Data caveat:** history only exists from 2026-09-05 forward (the log's introduction), so this
starts sparse. Same self-correcting-footnote treatment as the pace chart got in v1 — ship now, not
gated behind a "wait for more history" deferral, since the alternative (excluding it) just delays
the exact same thin-data problem to whenever it's eventually built.

### 6.4 Library Growth over time

Cumulative stacked-area chart. X-axis = calendar date, bucketed from `Issue.AddedTime` /
`Book.AddedTime` (confirmed set once at scan/import time, never overwritten — reliable for this).

- **Measure** (switchable): Series added · Issues added. (No "Volumes" — `Issue.Volume` is a free
  string label on an issue, not a distinct countable unit; dropped, see §2.)
- **Stack by** (switchable): Total · Reading state (`ReadingStatus`) · Media type (`ContentType`) ·
  Content rating (`Issue.AgeRating`, raw-grouped the same way `InsightsResolver.ComputeComposition`
  already buckets `Format`/`Decade` — trim + "Unknown" fallback, no cross-spelling canonicalization.
  **Correction from the original plan:** `MarkResolver`'s alias table lives in `Paperbunkr.App`,
  which `Paperbunkr.Data` cannot reference — reusing it isn't architecturally possible from
  `StatsResolver`. This is the same acceptable-simplification tier as the Format/Decade buckets
  already ship with.)

**Accepted simplification:** stacking reflects each item's **current** status/type/rating, not its
value at the historical add-date (Paperbunkr doesn't track status-change history). An item added
two years ago and only marked Completed last week counts as "Completed" all the way back on the
chart. This is the same class of trade-off as the `ReadingEvent` backfill collapse — noted, not
solved, in a footnote under the chart.

### 6.5 Library Breakdown

Two donuts, side by side:

- **Reading state** — real `ReadingStatus` enum, all 7 values (including `Unknown` — likely the
  largest slice for a freshly-migrated CE library, since CE never tracked this field; shown
  honestly rather than hidden or folded into another bucket).
- **Media type** — `ContentType` enum (Comic/Manga/Manhua/Manhwa/Unknown), series-level counts.

Replaces today's Completion donut (Read/In-progress/Unread), which was a coarser derived split of
the same underlying data.

### 6.6 Score Distribution

Unchanged data/logic from today's "Your ratings" chart (histogram of `Issue.Rating` 1–5, rounded,
excludes unrated) — relocated to Stats, restyled with the new palette (§8).

### 6.7 Content Rating

Donut over raw-grouped `Issue.AgeRating` values (same simple bucketing as §6.4's stack-by option —
no `MarkResolver` alias-table reuse, see the correction there).

### 6.8 Publication Year

Bar histogram of `Issue.Year` (confirmed `int?` field), bucketed per year, matching MangaBaka's
per-year bar chart.

### 6.9 Top Genres / Top Tags

Two separate top-10 horizontal-bar lists from `IssueTag`, split by its existing `Field` value
(`Genre` vs `Tags`) — this *is* the Theme/Tag-shaped split MangaBaka has, just correctly named for
what the data actually is (there's no separate Theme taxonomy in Paperbunkr, §2 of the original
research). Bar click → Library filtered to that genre/tag (existing composition-bar affordance).

### 6.10 Top Publishers / Top Authors / Top Artists

Three separate top-10 lists.

- **Publishers** — folds forward the existing `Composition.ByPublisher` logic, unchanged.
- **Authors** — parsed from `Issue.Writer` (free-text, comma-ish). Split per issue, count distinct
  names across the library.
- **Artists** — parsed from `Issue.Penciller` + `Issue.Inker` + `Issue.Colorist` combined. A name
  appearing in more than one of these three fields **on the same issue** counts once toward that
  issue's contribution (not once per role) — avoids one person's multi-hat credit inflating their
  own count.

Bar click → Library filtered to that creator/publisher (same affordance as Top Genres/Tags).

### 6.11 Range selector

Same fixed top row (not in the scroll region) and same 30d/90d/12mo/All-time options as today's
Insights screen. Range-aware: Reading Activity averages (§6.2), Finished-in-range,  Reading pace.
Range-**ignoring** (always absolute): Highlights, the Heatmap, Library Growth, both Breakdown
donuts, Score Distribution, Content Rating, Publication Year, Top Genres/Tags/Publishers/Authors/
Artists. Exactly the same "lifetime tiles ignore range" precedent the v1 doc already established.

## 7. Data layer

New `Paperbunkr.Data/Metadata/StatsResolver.cs`, mirroring `InsightsResolver`'s existing shape:
static, pure, testable, one entry point building an immutable `StatsSnapshot` for a given range.

```csharp
public static StatsSnapshot Build(PaperbunkrDbContext ctx, InsightsRange range, DateTime nowUtc);
```

`InsightsResolver`/`InsightsSnapshot` **shrink** — everything not in §5 (Lifetime, streaks,
FinishedInRange, Pace, Completion, Composition, Ratings) moves out of `InsightsSnapshot` and into
the new `StatsSnapshot`. Rejected alternative: one shared mega-resolver computing everything for
both screens. Insights is the screen opened far more often ("what do I read next"); it shouldn't
pay the cost of computing 10+ analytics sections it never renders.

`StatsScreenViewModel` caches its snapshot per range the same way `InsightsScreenViewModel` already
does, invalidated on the same `Opened`/`Finished` recorder event.

## 8. Charting & palette

`InsightsChartTheme` (existing) gets a **categorical palette** for any chart needing more than one
series/segment color: the app's existing `PbAccentBrush`, `PbBadgeBrush` (gold), `PbSuccessBrush`
(green), `PbDangerBrush` (red), plus two **new** supporting hues (`PbChartBlueBrush`,
`PbChartVioletBrush`) added to every registered skin's resource dictionary — not just the default
skin (Preferences/Skin System established multiple skins exist; this needs to be added everywhere,
flagged here so it isn't missed during implementation).

Category-to-color assignment is **fixed by enum order**, not randomly assigned per render — e.g.
`ReadingStatus.Reading` always gets the same palette slot across sessions and screen re-opens, so a
user builds a stable mental map of "orange = currently reading."

`ScottPlot.Avalonia` (already a dependency) is extended to draw: Library Growth (stacked area),
Publication Year (bar), Reading state / Media type / Content rating (donut or bar — reuses the
existing hand-rolled `CompletionDonut` pattern, generalized to accept N categories instead of a
fixed 3). Top-N lists stay `Border`-width bars, same as today's composition list — no new chart
type needed there.

## 9. Shared component

New `Views/Stats/StatCard.axaml` (+ `.axaml.cs`) — title, optional big-number, optional footnote
slot, `ContentPresenter` for chart/list content. Confirmed via research that no such shared
component exists today (`InsightsScreen.axaml` defines its own local `Border.card` style scoped to
that file); with ~12 new sections following the same card shape, hand-rolling each one again would
repeat the exact gap already flagged. Insights' own three-card READING row and single Collection
health card are small enough to keep their existing bespoke markup — not worth migrating for two
call sites.

## 10. Navigation

New nav-rail entry **"Stats"**, positioned immediately after **"Insights"**. Own FluentIcons symbol,
distinct from Insights' `DataHistogram` (exact enum member confirmed against the installed
`FluentIcons.Common` package during implementation — not guessed in this doc). Wiring follows the
identical pattern `MainViewModel` already uses for Insights (`CurrentScreen`, `IsStats` bool,
`GoStatsCommand`, rail-command list, `OnCurrentScreenChanged`, `ActiveScreenContent`), participating
in the same lateral transition system.

## 11. Testing

- **`StatsResolver`** — the bulk of new coverage. In-memory context, seed `Issue`/`Book`/
  `ReadingEvent`/`Series` rows, assert every tile: Highlights edge cases (ties, empty states, the
  backfill exclusion for Longest Journey/Fastest Completion), Library Growth cumulative bucketing
  across all four stack-by dimensions, Reading-state/Media-type donut counts across all 7/5 enum
  values including `Unknown`, Publication Year bucketing, Top-N parsing/splitting for Author/
  Artist/Genre/Tag/Publisher (including the same-issue multi-role de-dupe for Artists), range-in/
  range-out filtering.
- **`InsightsResolver`** — existing tests trimmed to match its shrunk scope; whatever tested
  Lifetime/streaks/Pace/Completion/Composition/Ratings moves to the new `StatsResolver` test file.
- **`StatsScreenViewModel`** — cache reuse across range switches, invalidation on a recorder event,
  empty-library and empty-log render without throwing.
- **No full-suite reliance** — targeted `--filter` per the known headless flake (carried forward).
- ScottPlot rendering itself stays untested (third-party draw path); `InsightsChartTheme`'s new
  categorical-palette resolution is unit-tested the same way its existing brush resolution is.

## 12. Decisions resolved during grilling

| # | Decision |
|---|---|
| Structure | Split into two nav-rail destinations: "Insights" (actionable) + "Stats" (analytics), option B of three presented. |
| Scope | Adopt every MangaBaka section buildable from existing data. |
| Reading-state donut | Switch from ad hoc Read/In-progress/Unread to the real `ReadingStatus` enum. |
| Thin/skewed history | Ship heatmap/reread/activity-averages now with a footnote (same precedent as the v1 pace chart); explicitly exclude backfilled rows from Longest Journey/Fastest Completion specifically, since those would otherwise show false 0-day records, not just "thin" data. |
| Creators | Ship both Top Authors (`Writer`) and Top Artists (`Penciller`+`Inker`+`Colorist` combined, same-issue de-duped). |
| Exclusions | Licensed % and Has-Anime-Adaptation % dropped entirely (no clean data source). "You vs Community" and "Chapters" as a measure unit deferred to a future smart-features initiative (need new backend/adapter work, not a UI redesign). |
| Content split | Collection health stays with Insights (has click-through rows); the four AT A GLANCE tiles move to Stats (no click-through today). |
| Palette | Real app skin tokens (`PbAccentBrush` etc.) plus two new supporting categorical hues, formalized in `InsightsChartTheme`, fixed enum-order assignment (not random per render). |
| Data layer | New `StatsResolver`/`StatsSnapshot`, mirroring `InsightsResolver`'s pattern; `InsightsResolver` shrinks to Reading attention + Collection health only. Rejected: one shared mega-resolver (unnecessary compute cost on the more-frequently-opened Insights screen). |
| Shared component | New `StatCard`/`ChartCard` control for Stats' ~12 sections; Insights keeps its existing bespoke cards. |
| Nav rail | "Stats" placed immediately after "Insights"; own icon (symbol name TBD at implementation). |
| Range selector | Same pattern as today: some tiles range-aware, absolute ones ignore it — unchanged precedent. |
| Spec shape | One spec (this doc), same as v1 — `writing-plans` phases the implementation (data layer + shared component first, then screen shell, then tile-by-tile), given the section count here is comparable to or larger than the original Insights dashboard build. |

## 13. New / changed files (orientation for the plan)

**New**
- `Paperbunkr.Data/Metadata/StatsResolver.cs` + `StatsSnapshot` record
- `Paperbunkr.App/ViewModels/StatsScreenViewModel.cs`
- `Paperbunkr.App/Views/StatsScreen.axaml` (+ `.axaml.cs`)
- `Paperbunkr.App/Views/Stats/StatCard.axaml` (+ `.axaml.cs`)
- `Paperbunkr.App/Views/Stats/*Tile.axaml` — one per §6 section
- `Paperbunkr.App/Views/Stats/ActivityHeatmap.cs` (hand-rolled control, same style as `CompletionDonut`)
- Tests mirroring the above

**Changed**
- `Paperbunkr.Data/Metadata/InsightsResolver.cs` + `InsightsSnapshot` — shrink to Reading attention + Collection health
- `Paperbunkr.App/Views/InsightsScreen.axaml` — remove AT A GLANCE tiles + charts row (moved out)
- `Paperbunkr.App/Services/InsightsChartTheme.cs` — add categorical palette + two new skin resource keys
- Every skin resource dictionary (Preferences/Appearance) — add `PbChartBlueBrush`, `PbChartVioletBrush`
- `Paperbunkr.App/Assets/Marks/SOURCES.md`-adjacent alias table (`age-rating-aliases.tsv`) — reused, not changed, for Content Rating canonicalization
- `Paperbunkr.App/ViewModels/MainViewModel.cs` — `stats` screen wiring
- `Paperbunkr.App/Views/MainWindow.axaml` — rail button, screen host
- `Paperbunkr.App/Views/Insights/CompletionDonut.cs` — generalize to N categories (reused for the two new Stats donuts) or a new sibling control, decided during planning

**Roadmap**
- `docs/alpha-todo.md` — record once shipped, per this project's standing rule to update the doc
  by hand rather than rely solely on the scheduled tracker sync.
