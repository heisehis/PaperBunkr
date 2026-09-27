# Insights pitch, slice 3 — Year-in-review recap (#1) — design

Date: 2026-09-23. Source: "Insights pitch" item #1 in `docs/Paperbunkr-Roadmap.md:2225` (10-item idea
capture, added 2026-09-21), third in the 5-item queue decomposed during slice 1's brainstorm
(`docs/superpowers/specs/2026-09-22-insights-period-over-period-deltas-design.md`'s Scope section). Status:
~~**approved, not yet implemented.**~~ **Built, uncommitted** — confirmed 2026-09-26 via source:
`RecapViewModel` present and untracked. On-screen check by the user still outstanding.

**CE parity:** ComicRack CE has no Insights/Stats screen, no reading-event log, and nothing resembling a
"Wrapped"-style recap — deliberate deviation, no CE behavior to match (confirmed: no hits for
"year-in-review"/"wrapped"/"recap" in `_reference/ComicRackCE` beyond an unrelated localized-string false
positive).

## Scope

Slice 3 of 5. Builds a calendar-year-scoped recap: a new "Recap" tab on the Insights screen, a year picker,
a 9-slide paged narrative (items finished → pages → streak → busiest day → top series → top writer → top
artist → highest-rated series → most-reread item), and a one-click PNG export of a purpose-built poster
layout distinct from the paged in-app slides. Slices 1 (#6, period-over-period deltas) and 2 (#4, nightly
snapshot + backlog burn-down) are already shipped and untouched by this work. Items #2 (reading
goals/challenges) and #9 (recommendations surface) remain queued.

Fully derivable from data that already exists (`ReadingEvent`, `Issue`, `Book`) — no new persisted table,
no new scheduled task. Computed live on tab-open/year-change, same as every other `StatsResolver` query.

## Data — `RecapResolver` + `RecapSnapshot`

New static class `RecapResolver` in `Paperbunkr.Data.Metadata` (same file or a sibling file to
`StatsResolver.cs` — implementer's call), same shape as `StatsResolver.Build`: a pure function of
`(context, year, nowUtc)`, no persistence, no caching.

```csharp
public static class RecapResolver
{
    public static IReadOnlyList<int> AvailableYears(PaperbunkrDbContext context, DateTime nowUtc);
    public static RecapSnapshot Build(PaperbunkrDbContext context, int year, DateTime nowUtc);
}
```

**Why a separate resolver, not an addition to `StatsResolver`:** `InsightsRange` (30d/90d/12mo/AllTime) and
"a specific calendar year" are incompatible range models — bolting a `year` parameter onto
`StatsResolver.Build` and its 15-field `StatsSnapshot` would conflate two different windowing schemes in one
already-784-line file. `RecapResolver` reuses `StatsResolver`'s public `HighlightGroup` record for tie
handling (`TopSeries`/`HighestRatedSeries`/`MostRereadItem` below) but is otherwise independent — none of
the 9 tiles need `StatsResolver`'s private `RealSpan`/journey-duration machinery.

**Year boundary:** the **local** calendar year (`Jan 1 00:00:00` through `Dec 31 23:59:59.999` local time),
converting every `ReadingEvent.TimestampUtc.ToLocalTime()` before bucketing — matches the existing
`ComputeStreak`/`ComputeHeatmap` convention in `StatsResolver`, not UTC.

**`AvailableYears`:** distinct `TimestampUtc.ToLocalTime().Year` values across all `ReadingEvent`s,
descending. Always includes the current year once it has ≥1 event, even mid-year (label it "so far" in the
UI, not in the resolver). No rolling "last 12 months" entry — the Stats tab already owns rolling windows;
Recap stays strictly calendar-year.

```csharp
public sealed record RecapSnapshot(
    int Year,
    bool IsCurrentYear,
    int ItemsFinished,
    long PagesRead,
    int LongestStreakDays,
    RecapBusiestDay? BusiestDay,
    HighlightGroup? TopSeries,
    HighlightGroup? TopWriter,
    HighlightGroup? TopArtist,
    HighlightGroup? HighestRatedSeries,
    HighlightGroup? MostRereadItem);

public sealed record RecapBusiestDay(DateOnly Date, long Pages);
```

Every `HighlightGroup?`/`RecapBusiestDay?` field being `null` is a real, expected state (that tile's
empty-state message — see Error handling below), not an error.

Per-tile computation, all reading `context.ReadingEvents`/`Issues`/`Books` filtered to the target year the
same way `StatsResolver.Build` already filters `inRange`:

1. **`ItemsFinished`** — count of `Finished` events in the year.
2. **`PagesRead`** — sum of `PagesRead` across `Finished` events in the year (same field/semantics
   `FinishedInRange.Pages` already uses).
3. **`LongestStreakDays`** — a **year-scoped** streak: the longest run of consecutive local calendar days
   with ≥1 event, **capped at the year's own boundaries** (a streak that started Dec 28 of the prior year
   and continued into January doesn't count days from the prior year). New logic, not a reuse of
   `StatsResolver.ComputeStreak` (which is lifetime-only and also tracks "current" streak, meaningless for a
   past year).
4. **`BusiestDay`** — the local calendar day within the year with the highest summed `PagesRead`; `null` if
   every day in the year summed to 0 (all events pre-teardown/no pages ever recorded).
5. **`TopSeries`** — series with the most issues/items finished in the year, `HighlightGroup`-tied.
6. **`TopWriter`** — `Issue.Writer` (same comma-split/dedupe/per-issue-distinct rule as
   `StatsResolver.ComputeTopCreators`) across issues finished in the year, **plus `Book.Author`** for novels
   finished in the year folded into the same tally (a novel's Author reads as a writer-equivalent role).
7. **`TopArtist`** — `Issue.Penciller`/`Inker`/`Colorist`, same rule as `StatsResolver.ComputeTopCreators`'s
   artist selector, comics only (novels have no artist credit) — a second, separate tile from `TopWriter`,
   not blended.
8. **`HighestRatedSeries`** — series with the highest average `Issue.Rating` among issues finished in the
   year (only issues with a rating count), `HighlightGroup`-tied.
9. **`MostRereadItem`** — items (comic or novel) with 2+ `Finished` events within the year, `HighlightGroup`-
   tied on the highest count, title resolved the same way `StatsResolver.ResolveTitle` does (comic → series
   name; a gone-item or a novel resolves to `null` and drops out of the tie set, same tolerance already
   accepted for `MostReread` in `StatsResolver`).

**Accepted limitation, not new:** `ReadingEvent` has no FK to `Issue`/`Book` by design ("a reading event
must survive deletion of the item it describes" — `ReadingEvent.cs`'s own class doc). `TopWriter`/
`TopArtist`/`TopSeries`/`HighestRatedSeries` therefore silently lose credit for anything finished-then-later-
deleted from the library, exactly like `StatsResolver.ComputeTopCreators`/`HighestRated` already do today —
not a new gap this feature introduces.

## UI — Recap tab, slide viewer

- **Tab header** (`InsightsScreen.axaml`): a third `Button Classes="tab"` ("Recap") alongside the existing
  `Overview`/`Stats` buttons (lines 116-117), driving a new `IsRecapTabSelected` on
  `InsightsScreenViewModel` (mutually exclusive with the existing `IsStatsTabSelected`/`!IsStatsTabSelected`
  pair — becomes a 3-way selector). A year `ComboBox`/`ItemsControl` bound to `Recap.AvailableYears` /
  `Recap.SelectedYear` sits where the Stats tab's range selector (`Stats.RangeOptions`, line 119) sits,
  visible only when `IsRecapTabSelected` — a sibling control, not a shared one, since a year and a rolling
  range mean different things. Current year's entry reads "2026 (so far)" when `IsCurrentYear`.
- **`RecapViewModel`** (new; composed as `InsightsScreenViewModel.Recap`, exactly how `Stats` is composed
  today): `AvailableYears`, `SelectedYear` ([ObservableProperty], re-runs `RecapResolver.Build` on change and
  resets `CurrentSlideIndex` to 0), `Snapshot` (`RecapSnapshot?`), `CurrentSlideIndex` ([ObservableProperty],
  0-8), `NextSlideCommand`/`PreviousSlideCommand` (clamped, not wrapping — no "slide 9 → slide 0" jump),
  `ExportCommand`.
- **Slide viewer:** one `Border Classes="card"` region using the app's normal `DynamicResource` skin brushes
  (`PbSurface1Brush`/`PbAccentBrush`/`PbTextBrush`) — **not** a fixed/branded palette (per review: both the
  in-app slides and the exported poster follow the user's active skin, matching every other Insights/Stats
  card). Shows the current slide's big number + label (or its empty-state message), left/right chevron
  buttons, a dot row (9 dots, current one highlighted), and Left/Right arrow-key navigation matching this
  app's existing keyboard-nav idiom elsewhere.
- **Slide order** (fixed narrative arc — broad totals → habits → favorites → superlatives): Items finished →
  Pages read → Longest streak → Busiest day → Top series → Top writer → Top artist → Highest-rated series →
  Most-reread item.

## Export — poster PNG

One "Export" icon button in the Recap tab's header (not per-slide — a single click exports the whole year,
not whichever slide happens to be showing).

- **New `RecapPosterView.axaml`/`.axaml.cs`** — a compact, single-portrait layout distinct from the paged
  in-app slides: `ItemsFinished` as the headline number, a 2×4 grid of the remaining 8 tiles below (each
  tile's own empty-state text if that field is `null`), a corner mark reusing the exact nav-rail logo asset/
  pattern (`Image Source="avares://Paperbunkr.App/Assets/paperbunkr-logo-rail.png"`, fixed brand colors, not
  skin-tinted, decorative — `MainWindow.axaml:195-198`'s own precedent) plus a "paperbunkr" text wordmark
  beside it — **not** the generic `BrandMark` control, which is for *external* providers (`Family`/`Value`
  semantics for publisher/service/language logos), not the app's own identity. `Year`/"so far" heading.
  The rest of the poster (numbers, labels, backgrounds) styled with the same skin `DynamicResource` brushes
  as the live app.
  Per this project's Avalonia build gotcha, the code-behind `.cs` ships in the same commit as the `.axaml`
  (minimal `InitializeComponent()` stub) to avoid the `AVLN2000`/stale-assembly trap.
- **Render:** verified against the real Avalonia docs (`avalonia-docs` MCP,
  `docs/graphics-animation/custom-rendering` + `docs/how-to/image-how-to`) rather than guessed —
  `RenderTargetBitmap.Render` requires its target control to be attached to a **visible window**; a fully
  detached/unparented control does not render correctly outside Avalonia's headless test platform (not
  applicable here — this is a live desktop app, not a test host). The originally-drafted "build it off-screen,
  unparented" approach does not work and is corrected here: `InsightsScreen.axaml` gets one small,
  always-present, zero-opacity, hit-test-invisible host (`<Panel x:Name="PosterExportHost" Opacity="0"
  IsHitTestVisible="False" />`) — `InsightsScreen` is only ever instantiated while the Insights nav-rail
  destination is active (`MainWindow.axaml`'s `DataTemplate DataType="vm:InsightsScreenViewModel"`), so it's
  guaranteed attached to the live window whenever the user could possibly click Export. On export, the
  code-behind adds a `RecapPosterView` as that host's child and sets its `DataContext` to the `RecapSnapshot`.
  With the control genuinely
  attached, `Measure`/`Arrange` at a fixed portrait pixel size, then `new RenderTargetBitmap(size).Render(view)`
  produces a correct bitmap (styles/`DynamicResource` skin brushes resolve normally since the control is a real
  part of the live tree). The control is removed from that host panel immediately after rendering — it isn't a
  persistent child. **No precedent for this exact recipe exists in this codebase**
  (`BackdropBlurRenderer.cs` only re-renders an already-rasterized `Bitmap`, not a laid-out control tree with
  live text/bindings), so implementation should still sanity-check the measure/arrange sequencing against the
  `avalonia`/`avalonia-pro-max` skill during the actual build.
- **Save:** `IFilePickerService.PickSaveFileAsync("Export Year in Review", $"paperbunkr-recap-{year}.png",
  "png", "PNG Image")` — existing method (`FilePickerService.cs:92`), reused as-is. `null` (user cancelled)
  is a silent no-op, matching every other cancel path in this codebase.

## Error handling

- **Per-tile empty state:** any `null` `HighlightGroup?`/`RecapBusiestDay?` on `RecapSnapshot` renders that
  slide's/poster-cell's own short message ("No rereads this year", "No rated series this year", "No writer
  info recorded") rather than being skipped — dot count and slide position stay fixed at 9 regardless of how
  much data a given year has. Matches the honest-empty-state precedent already established by `BurnDown`'s
  "not enough history yet" text and `Breakdown`'s shown-not-hidden `Unknown` bucket.
- **Export exceptions:** a render/save failure is caught and surfaced through the Activity Center
  (this project's standing rule: jobs/alerts/toasts go through Activity Center, not ad-hoc dialogs), not a
  raw exception dialog.

## Testing

- **`RecapResolverTests`** (new, `Paperbunkr.Data.Tests`, reusing `StatsResolverTests`'s
  `SeedSeries`/`SeedIssue`/`SeedEvent` helpers): one case per tile; a tie case for `TopSeries`/
  `HighestRatedSeries`/`MostRereadItem` exercising `HighlightGroup`'s "and N others" text; a year-boundary
  case (an event at `Dec 31 23:59:59` local vs `Jan 1 00:00:00` local of the following year lands in the
  correct year's snapshot); a streak-does-not-cross-year-boundary case; an empty-tile case (year has some
  activity but zero rereads/ratings, asserting the specific fields are `null` rather than the whole build
  throwing); an `AvailableYears` case confirming the current partial year appears with just one event and
  that years with zero events never appear.
- **`RecapViewModelTests`** (new, `Paperbunkr.App.Tests`, same conventions as `StatsScreenViewModelTests`):
  `NextSlideCommand`/`PreviousSlideCommand` clamp at 0 and 8 (no wraparound); changing `SelectedYear`
  re-resolves `Snapshot` and resets `CurrentSlideIndex` to 0.
- **No automated visual test for the rendered poster PNG** — same accepted precedent as the `BurnDown`/
  `GrowthChart` ScottPlot charts ("no automated visual check for it in this codebase"); the
  `RecapSnapshot → poster` data mapping is unit-testable without rendering actual pixels. The rendered PNG
  itself needs an on-screen check, same as those charts did when first shipped.
- A migration is **not** needed for this slice (no new table) — no migration test to add.

## Out of scope for this slice

Items #2 (reading goals/challenges) and #9 (recommendations surface) stay queued. A per-slide export
(exporting just the slide currently on screen, rather than the one full-year poster) was considered and
explicitly rejected during this brainstorm in favor of the single stitched-poster export. Sharing/uploading
the exported image anywhere is out of scope — this only ever writes a local PNG file via the OS save dialog.
