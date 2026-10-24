# Insights pitch completion — rhythm, drop-off, affinity, storage, milestones — design

Date: 2026-10-12. Status: **draft, awaiting user review.** Follows the grilling pass of 2026-10-12 (all eight recommended answers
accepted; the user chose **one spec for all three slices** instead of three). Closes the five items of the 2026-09-21 Insights pitch
that the 2026-09-22 decomposition left unbuilt: #3 reading rhythm, #5 drop-off analysis, #7 creator/publisher affinity,
#8 storage/format breakdown, #10 milestones timeline. Builds on `2026-09-23-insights-redesign-design.md` and
`2026-10-04-insights-goal-outcomes-and-chart-colour-design.md`; nothing in those is reversed.

**CE parity:** ComicRack CE has no reading-behaviour statistics, achievements or milestones (verified 2026-10-12: its statistics classes are
per-series count/gap/rating/last-opened for sorting and grouping only). All five are deliberate deviations; nothing to match.

**Roadmap corrections to make when this lands:** `Paperbunkr-Roadmap.md` says `ReadingEvent` has no session duration (`ActiveSeconds` shipped
2026-10-06) and lists #5 as not started (a Drop-off watch card already exists on Today).

**Avalonia notes** (check `avalonia-pro-max/review-checklist` before calling the UI done): every colour is a skin resource
(`Pb*Brush`/`Pb*Color`) or comes from `InsightsChartPalette`, never a hex literal. New cards use the plain inline `Border.card` markup the
Stats tab uses (the reusable `StatCard` renders blank). Custom-drawn controls (`OnRender`) resolve brushes with `TryFindResource`. Any new
`.axaml` ships with its code-behind in the same step (AVLN2000 gotcha). Any "remove/close from inside a row" button defers with
`Dispatcher.UIThread.Post`.

## 0. Placement (Q1 = A: no new tab)

| Feature | Where | Why |
|---|---|---|
| #3 Reading rhythm | Trends › **Activity** group, under the existing activity heatmap | same data family (events over time) |
| #5 Drop-off | Trends › **Composition** group; the Drop-off watch stays on Today | breakdown by publisher/genre/type |
| #7 Affinity | Trends › **Top Lists** group | ranked lists with click-through |
| #8 Storage | Trends › **Composition** group | breakdown by format/publisher |
| #10 Milestones | **History** tab, a strip above the day-by-day rows | a timeline of events, History's own idiom |

Trends cards follow the group pattern: add a result record to `StatsSnapshot` (`StatsResolver.cs`), compute it in `StatsResolver.Build`, add
`HasX` flags to the `OnPropertyChanged` list in `StatsScreenViewModel.Refresh`, add a `Border.card` in the matching `Is*GroupSelected`
section of `InsightsScreen.axaml`. The range selector (30d/90d/12m/All) applies to #3 and #5; #7, #8, #10 are lifetime and say so in their
card subtitle.

Rhythm and milestones are **read-side only** (no migration). Drop-off and affinity are read-side only. Storage is read-side only. **No new
table, no new column in this spec.**

## 1. Reading rhythm (#3) — `RhythmResolver` (Data/Metadata)

### Data and its honest limits
- `ReadingEvent.TimestampUtc` is the session start (it sits on the `Opened` row). Convert with `ToLocalTime()` exactly as `StatsResolver`
  does (`ComputeHeatmap`, `ComputeStreak`) so weekday/hour agree with the existing heatmap and streaks.
- `ActiveSeconds` exists only on `Opened` rows written after the 2026-10-06 migration `AddReadingEventActiveSeconds` that made forward page
  progress (`ReadingEventRecorder.UpdateSessionPages` returns early on a non-positive page delta and stores null for 0 seconds). `Finished`
  rows never carry it. Older rows are null with no backfill.
- Backfilled rows (`ReadingEventBackfill`) write `Opened` and `Finished` at the same timestamp (`OpenedTime`); `StatsResolver.ComputeRealSpans`
  already uses that equality as the backfill signature. Rhythm **excludes** them: their weekday/hour is fabricated.

### Computation
```csharp
public sealed record RhythmData(
    int[,] SessionsByWeekdayHour,            // [7 weekdays, 24 hours], Sunday-first to match ActivityHeatmap
    int SessionCount,                        // Opened rows in range, backfill excluded
    int MeasuredSessionCount,                // those with ActiveSeconds
    IReadOnlyList<SessionLengthBucket> LengthBuckets,   // <5, 5-15, 15-30, 30-60, 60+ minutes
    double? AverageSessionMinutes,           // mean of ActiveSeconds over measured sessions; null when none
    int? PeakWeekday, int? PeakHour);        // argmax cell, null when SessionCount == 0
public sealed record SessionLengthBucket(string Label, int Count);
```
- Counts are sessions (one `Opened` row each), not pages, so a long marathon doesn't swamp one cell.
- Length buckets and the average use **measured** sessions only. The card states "Based on N of M sessions with timing" so the early-history gap
  is visible instead of silently skewing the picture.
- Respects the range selector; `All` includes everything non-backfill.

### UI
- New `Views/Stats/RhythmHeatmap` custom `Control` (`OnRender`, 7 × 24 grid, weekday labels left, hour ticks every 3 h, opacity scales with the
  cell count, hover tooltip "Tue 20:00 · 14 sessions"). Same colour logic as `ActivityHeatmap` (accent at varying opacity).
- Under it, the length distribution as a small bar row (reuse the existing hand-drawn bar idiom, single accent: time buckets are an ordered
  quantity, so no colour-per-bar) and two stat lines: "Typical session 22 min" and "Peak: Tue evenings".
- Empty state: fewer than 5 sessions → "Not enough reading history yet" (no chart).

## 2. Drop-off analysis (#5) — `DropOffBreakdownResolver` (Data/Metadata)

The existing `DropOffResolver` (most common stall point + at-risk series, Today tab) stays untouched. This adds the missing half: **abandon vs
finish rate by publisher, genre and content type**, plus a stall-point histogram.

### Definitions (reuse, don't invent)
- **Stopped** series: `Series.ReadingStatus == Dropped`, **or** (≥1 issue read, ≥1 unread issue remaining, last touch older than
  `InsightsResolver.StalledDays` = 21, status not Completed). Last touch = max `ReadingEvent` timestamp for the series' issues, falling back to
  `OpenedTime`, exactly as `InsightsResolver.ComputeContinue` does. Backfilled-only history counts as a touch at its `OpenedTime`.
- **Finished** series: `SeriesProgress.UnreadCount == 0` with ≥1 read (the 95 % read rule via `HasBeenRead`), or `ReadingStatus == Completed`.
- A series that is neither (in progress and recent, or never opened) is **not counted**; the rate is stopped ÷ (stopped + finished).
- Minimum 5 decided series per bucket, otherwise the bucket is folded into "Other". Below that the ratio is noise.

### Buckets
- Publisher: effective publisher (`Issue.Publisher`, falling back to `Series.Publisher`), as `StatsResolver.ComputeComposition`.
- Genre: first genre tag (`IssueTagField.Genre`), as `ReadingEvent.PrimaryGenre` is frozen.
- Content type: `Series.ContentType` (Comic / Manga / Manhua / Manhwa / Unknown). Unknown is shown, in neutral grey from `InsightsChartPalette`.
- Stall-point histogram: for stopped series, `ReadCount` bucketed 1, 2, 3, 4–5, 6–10, 11+. This generalises `DropOffResolver`'s single cliff.

```csharp
public sealed record DropOffBreakdown(
    IReadOnlyList<DropOffRate> ByPublisher, IReadOnlyList<DropOffRate> ByGenre, IReadOnlyList<DropOffRate> ByContentType,
    IReadOnlyList<SessionLengthBucket> StallPoints, int StoppedTotal, int FinishedTotal);
public sealed record DropOffRate(string Label, int Stopped, int Finished);   // rate = Stopped / (Stopped + Finished)
```
Range selector: not applied (a series' fate is lifetime). The card subtitle says "All time".

### UI
Composition group card "Where you stop". A segmented chips row (Publisher · Genre · Type) switches which list shows. Each row: label, a
two-segment bar (stopped = `PbDangerBrush`-family via the palette, finished = `PbSuccessBrush`), "34% stopped · 12 of 35". Sorted by decided
count, top 8. Beneath, the stall-point histogram. Clicking a publisher/genre row opens the Library searched for it (reuse
`StatsScreenViewModel.OpenLibraryFilter`).

## 3. Creator and publisher affinity (#7) — `AffinityResolver` (Data/Metadata)

**Scope decision (Q4):** the pitch's "unowned works by your top creators → Wanted" is **dropped from this spec.** Wanted items carry no creator
data (`WantedAffinityScorer` header says so), the Wanted queue has no creator/publisher filter, `MainViewModel.GoWanted` takes no payload, and
nothing local knows a creator's unowned works. It needs creator-aware Wanted plus a Metron/ComicVine lookup, a project of its own. Recorded as a
follow-up in the roadmap.

### Score
- Creators come from the normalized `CreatorCredit` index (Creator, Issue, Role) maintained by `CreatorResolver`, not the free-text split
  `StatsResolver.ComputeTopCreators` uses, so one person isn't counted under spelling variants.
- Per creator, over series that have ≥1 credit for them: `affinity = avgRating × completionRate`, where `avgRating` is the mean of the user's
  `Issue.Rating` over **rated** issues credited to them (0–5 scale as stored) and `completionRate` = read issues ÷ owned issues credited to them.
- Gate: ≥3 distinct series and ≥3 rated issues, so one five-star one-shot doesn't top the list.
- Roles grouped like `ComputeTopCreators`: Authors (Writer) and Artists (Penciller, Inker, Colorist). Cover artists/letterers/editors are not ranked.
- Publisher affinity: same formula over effective publisher, gate ≥3 series and ≥3 rated issues.
- Books have no rating or credits (`Book.Author` single string, no `Book.Rating`), so books are excluded; the card says "comics and manga".

```csharp
public sealed record AffinityEntry(string Name, double Score, double AverageRating, double CompletionRate, int Series, int OwnedIssues);
public sealed record AffinityData(IReadOnlyList<AffinityEntry> Authors, IReadOnlyList<AffinityEntry> Artists, IReadOnlyList<AffinityEntry> Publishers);
```

### UI
Top Lists group card "You love these". Three-way chips (Authors · Artists · Publishers); each row shows the name, stars + completion
("★ 4.6 · 92% read"), series count, and a thin score bar. Clicking opens a Library search for the name. No "unowned works" line.
Lifetime; the subtitle says so.

## 4. Storage and format breakdown (#8) — `StorageResolver` (Data/Metadata)

### Data
- `Issue.FileSize` (`long?`, bytes, filled by `IssueFileStats.TryApply`); null when never scanned/refreshed or for remote/placeholder issues.
  Null sizes are excluded from byte totals and counted in "N files not measured".
- **File container** is derived, not stored: `IssueMetadataExtensions.FileFormat` (extension, e.g. cbz). The existing Composition "Format"
  chart is the *publication* format (TPB etc.); the new card is labelled "File type" so the two aren't confused. No sniffing pass (slow).
- **Comics only (Q5).** `Book` has no `FileSize`, so books appear as "N books · size not tracked". Adding a column + backfill is a separate migration
  and is explicitly out of scope. Remote-library issues are excluded from byte totals (they are not on this disk).
- Largest files: 10 by `FileSize`, with title, size, container; click opens the Detail screen for the series.
- Duplicate savings: `SmartListQueryBuilder.BuildDuplicateGroups` yields clusters; `DuplicateKeeperRanker.Recommend` picks the copy to keep;
  **reclaimable = Σ `FileSize` of the non-kept members**, skipping groups where the members are marked `DuplicateAcknowledged`. The figure is
  labelled "up to", since the ranker is a recommendation.

```csharp
public sealed record StorageData(
    long TotalBytes, int MeasuredFiles, int UnmeasuredFiles, int BookCount,
    IReadOnlyList<StorageSlice> ByFileType, IReadOnlyList<StorageSlice> ByPublisher,
    IReadOnlyList<LargeFile> Largest, long ReclaimableBytes, int DuplicateGroups);
public sealed record StorageSlice(string Label, long Bytes, int Files);
public sealed record LargeFile(int IssueId, int SeriesId, string Title, long Bytes, string FileType);
```

### UI
Composition group card "Storage": total as a big number ("212 GB across 2,369 comics"), a `CategoryDonut` for file type with `DonutLegend`
(bytes + %), a publisher bar list (top 8 by bytes), the largest-files list, and a "Duplicates could free up to 4.2 GB" line with a **Review
duplicates** button.

### Navigation callback
`InsightsScreenViewModel` receives no route to Preferences today (ctor at `InsightsScreenViewModel.cs:38`, built at `MainViewModel.cs:287`). Add one
`Action<string>? openPreferences` parameter, wired in `MainViewModel` to raise the existing `ActivityLink(ActivityLinkKind.Preferences,
"LibraryHealth/Review/Duplicates")` path (`MainViewModel.cs:3329-3331` → `OpenLibraryHealthFromPayload` → `PreferencesScreenViewModel.OpenLibraryHealth`).
Null-safe (tests pass null; the button hides). The Library Health duplicates section key is `duplicates` (`LibraryHealthSections.cs`).

### Cost
`BuildDuplicateGroups` loads full `Issue` entities. Compute storage **lazily when the Composition group is first selected** and cache it for the
session. Storage is not reading-driven, so `ReadingEventRecorded` does not invalidate it; it is recomputed when the Insights screen is
re-entered after a library scan/import (the existing library-changed signal the screen already refreshes on), or via the screen's Refresh.
Run on the thread pool like the other resolvers; never on the UI thread.

## 5. Milestones timeline (#10) — `MilestoneResolver` (Data/Metadata)

**Derived, not stored (Q6):** computed from `ReadingEvent` history each time the History tab loads. No table, no alerts. This keeps the timeline
honest if history is later corrected, and avoids the re-fire-on-every-launch problem (Activity Center alerts are session-scoped).

### Milestone set (Q7)
| Id | Rule | Date taken from |
|---|---|---|
| `finished-N` for N in 1, 10, 50, 100, 250, 500, 1000 | N-th distinct finished item (comic or book), ordered by first `Finished` timestamp per item | that event's local date |
| `streak-N` for N in 7, 30, 100 | first date a reading-day streak reaches N consecutive local days | the day it reached N |
| `first-complete-series` | earliest date a series reached `UnreadCount == 0` with ≥1 read | the finishing issue's `Finished` date |
| `first-manga-volume` | earliest `Finished` event whose issue's `Series.ContentType == Manga` | that date |
| `first-book` | earliest `Finished` event with `ItemType == Novel` | that date |
| `first-goal` | earliest `ReadingGoal` with outcome Completed | `GoalResolver`'s `CompletedOn` |

- **Backfilled rows are excluded from every rule** (Opened and Finished at the same instant is the signature), so the import can't mint
  a "100th finished" or a fake 30-day streak. If a user's only history is backfill, the timeline shows nothing earned and says why.
- Streak dates need a forward pass over the sorted distinct local days of non-backfill events, recording the day each threshold is first
  met. This is a new loop beside `StatsResolver.ComputeStreak` (which returns only current/longest); do not alter `ComputeStreak`.
- "First complete series" intentionally uses *all issues read*, not the user's `ReadingStatus.Completed` flag, which is manual and never set
  automatically. A series whose issues the user finishes and later adds issues to is unaffected: the milestone stays at its first date.
- Output is ordered newest first for the strip, with an **earned** list and a **next up** list (the next unearned threshold in each ladder,
  with progress "73 / 100").

```csharp
public sealed record MilestoneData(IReadOnlyList<Milestone> Earned, IReadOnlyList<MilestoneProgress> NextUp, bool OnlyBackfilledHistory);
public sealed record Milestone(string Id, string Title, string Detail, DateOnly Date, MilestoneKind Kind);   // Detail e.g. series name
public sealed record MilestoneProgress(string Id, string Title, int Current, int Target);
public enum MilestoneKind { Finished, Streak, Series, Manga, Book, Goal }
```

### UI
A strip at the top of `Views/Insights/InsightsHistoryView.axaml`: horizontally scrolling squircle badges (`PbRadiusChip`, never
`CornerRadius=999`), newest first, each showing an icon (FluentIcons, as the app already uses), the title ("100 issues finished"), and the date.
Below the earned badges, dimmed locked badges for "next up" with their progress. A **Hide** toggle on the strip's header (persisted via
the existing AppSettings pattern used by History's own toggles) honours "opt-in": shown by default once there is at least one earned milestone,
hidden entirely and unrendered if the user hides it. No milestone alerts in the Activity Center.
Clicking a badge with an associated series opens its Detail; others do nothing.

## 6. Cross-cutting

### Refresh and caching
- `StatsScreenViewModel` already caches per `InsightsRange` and invalidates on `ReadingEventRecorded`. Rhythm joins that cache; drop-off, affinity
  and storage are range-independent, so they are computed once per invalidation and reused across range switches.
- History's `HistoryTabViewModel` loads milestones with its existing load; milestone data is cheap (event scan) but is computed off the UI thread.
- All new resolvers take `Func<PaperbunkrDbContext>` like the existing ones and use `AsNoTrackingWithIdentityResolution` where they group by
  `Series` (plain `AsNoTracking` breaks `GroupBy(Series)`; see the 2026-09-05 note).

### Error handling
Each resolver call is wrapped like `DropOffResolver.Build` in `BuildToday`: a failure logs and leaves that card hidden, never blanking the tab.

### Settings
One new boolean: `HideMilestones` (History strip). It follows the existing AppSettings-column pattern; a bool with `HasDefaultValue(false)`
so older raw-SQL inserts keep working. **This is the spec's only migration** (`AddInsightsMilestonesToggle`; `Down` drops nothing, per the
codebase's orphan-column rule).

### Tests
Data.Tests (pure, in-memory/SQLite fixtures like `StatsResolverTests`):
- `RhythmResolverTests`: weekday/hour bucketing in local time (a UTC timestamp crossing midnight lands on the local day), backfill excluded,
  measured-vs-all counts, empty and under-threshold states, range filtering.
- `DropOffBreakdownResolverTests`: stopped vs finished classification (Dropped status, 21-day boundary, Completed excluded, never-opened
  not counted), bucket folding below 5, stall-point buckets.
- `AffinityResolverTests`: score = rating × completion, the ≥3 series / ≥3 rated gates, canonical creator merging via `CreatorCredit`, books excluded.
- `StorageResolverTests`: null-size handling, comics-only totals, remote excluded, reclaimable excludes the kept copy and acknowledged groups.
- `MilestoneResolverTests`: each rule's date, backfill-only history yields nothing, a streak threshold date, next-up progress, re-read doesn't
  double-count the Nth finished item.
App.Tests: `InsightsScreenViewModelTests` / `StatsScreenViewModelTests` additions for the new `HasX` flags and cache behaviour; `HistoryTabViewModelTests`
for the milestone strip and the Hide toggle; headless XAML-load tests for the new controls; a headless render of the Composition group and
History strip in dark and light (`FocusTestHarness.WithThemeAndTokens/AppStyles/AppResources/RunLayout` + `CaptureRenderedFrame`).
Navigation callback: a test that Review-duplicates raises the Preferences link with the right payload.

### Out of scope (YAGNI)
Wanted filtered by creator; "unowned works" lookups; book sizes and a `Book.FileSize` column; sniffing true container format; milestone
alerts or persistence; per-series rhythm; session-length history before 2026-10-06 (cannot be recovered); a new Insights tab; export of the new
cards as images (the Recap poster pipeline stays as is).

### Build order inside the single plan
1. Rhythm + milestones (both `ReadingEvent`-based; no cross-service wiring). 2. Drop-off + affinity (series analytics). 3. Storage (needs the
`openPreferences` callback). Each step is independently shippable and tested before the next; the settings migration lands with step 1.

### Not verified yet
Nothing here has been seen in the running app. The on-screen check list after building: heatmap hover and empty states on the real library,
the 21-day boundary on real data, affinity ranking sanity on the real ratings, the storage total against Explorer, the duplicates link landing on
Library Health › Duplicates, the milestone strip in light and dark, and that a backfill-only history shows no earned milestones.

## Implementation notes (2026-10-12)

Where the build departs from the design above, and why:

- **No migration.** The spec's only migration (`HideMilestones` as an `AppSettings` column) was dropped. Another session was mid-migration on `AppSettings`
  (`ShowTaskbarBadge`) and the model snapshot, and a view toggle does not justify a schema change. The switch lives in `insights.json` next to the database
  (`Services/InsightsPreferences`, same placement as `keymap.json`). Missing or malformed file reads as defaults.
- **Results are not on `StatsSnapshot`.** Each analysis is its own static resolver (`RhythmResolver`, `DropOffBreakdownResolver`, `AffinityResolver`,
  `StorageResolver`, `MilestoneResolver`); `StatsScreenViewModel` (partial file `StatsScreenViewModel.Insights.cs`) holds them as separate properties.
  Rhythm is cached per range with the snapshot; drop-off, affinity and storage load lazily off the UI thread when their group is first shown. Reading activity
  invalidates rhythm, drop-off and affinity; storage only reloads through `InvalidateStorage()`.
- **"First complete series" needs at least 2 issues** (`MilestoneResolver.MinIssuesForCompleteRun`), so a single one-shot is not a "complete run". It is derived
  from first-finish events (every real issue finished), not from the manual `ReadingStatus.Completed` flag.
- **Storage uses bars, not a donut** (donut slices carry an integer count, not bytes). File-type and publisher rows share one bar-row template.
- **`openDuplicateReview` lives on `StatsScreenViewModel`**, passed in through `InsightsScreenViewModel`'s new optional parameter, and is wired in `MainViewModel`
  to the existing `LibraryHealth/Review/Duplicates` payload. Largest-file rows open the series Detail through `GoDetailForSeries`.
- Drop-off's content-type bucket labels come from `ContentType.ToString()`; unknown or missing publisher/genre shows as "Unknown".
