# Reading signals — design

Date: 2026-11-10. Source: "Smart features pitch 2" in `docs/Paperbunkr-Roadmap.md`, items #4 (catch up before release day), #5 (rating
suggestions from how you read) and #19 (weekly digest). Grilled over two rounds with the user; every decision below was confirmed. The three
surfaces were confirmed from a mockup in the visual companion.

## 1. Scope

One spec, three independent parts. Each gets its own plan steps and its own on-screen check.

| Part | Pitch item | Surface |
|---|---|---|
| R1 | #4 Before release day | New Home section; the reader's in-memory queue |
| R2 | #5 Rating suggestions | Series Detail chip; the reader's end-of-issue review prompt |
| R3 | #19 Weekly digest | Scheduled task, Activity Center entry, "Your week" dialog |

**Dependency:** R1's "Catch up" uses the in-memory reader queue added by slice B of
`docs/superpowers/specs/2026-11-10-smart-features-2-design.md` (plan correction 1: a third `ReadingOrderResolver` anchor). Build slice B first.
R1 also reuses slice B's default pace (comics 20 s/page, manga 12 s/page) while the user has fewer than 5 qualifying sessions.

**Out of scope:** books in R1 and R2 (books have their own rating and estimator); a weekday mode in the scheduler (R3 avoids it).

**ComicRack CE** (checked in `_reference/ComicRackCE`):
- CE has no release reminders, rating suggestions or digest. R1, R2's suggestion chip and R3 are deviations. CE's News dialog is an RSS
  reader, not a library digest.
- **Parity fix in R2:** CE's quick-review prompt appears only for a book that is read and unrated (`MainForm.cs:889`,
  `HasBeenRead && Rating == 0`, `Settings.AutoShowQuickReview`). Paperbunkr's `MaybePromptReviewOnFinish`
  (`ReaderScreenViewModel.cs:3707-3727`) does not check the rating. R2 adds the check.

## 2. Foundations

**Code placement.** Each calculator is a pure function in `Paperbunkr.Data/Metadata` (next to `UpNextResolver`, `ReadingPaceResolver`),
with unit tests; view models and views in `Paperbunkr.App`.

**Schema** (one migration each; hand-edit any generated `Down()` that drops an `AppSettings` column so it leaves it, per the rollback-chain
bug; migration tests mirror `AddSmartFeaturesSettingsMigrationTests`):

| Part | Change |
|---|---|
| R2 | None. "Not now" reuses `HealthFindingDismissal` (`Kind` = `RatingSuggestion`, `Key` = series id). |
| R3 | New table `WeeklyDigest` (`Id`, `WeekStartUtc`, `GeneratedAtUtc`, `LinesJson`). `AppSettings.WeeklyDigestDay` (int, `DayOfWeek`, default Monday). The run time is the task's own `DailyAt` time in its existing scheduler state (default 09:00), not a new setting. |

R1 adds no columns: its Home section key goes through the existing `HomeSectionOrder`/`HomeHiddenSections` CSV.

**UI rules.** The `paperbunkr-design-language` skill throughout: tokens only, shared families (`Button.primary/secondary/ghost`,
`Border.card`), `DialogCard` for the dialog, Fluent icons in 26 px tinted squares (`PbRadius`, `PbAccentSoftBrush`) for the digest lines.
Each part runs the lint set and `avalonia-pro-max/review-checklist`.

## 3. R1 — Before release day (#4)

### 3.1 The calculator

`ReleaseCatchUpResolver.Build(context, nowUtc, pace)` returns `IReadOnlyList<ReleaseCatchUp>`; each item is `{ SeriesId, SeriesName,
ReleaseIssueNumber, StoreDate, CatchUpIssueIds (in number order), Minutes, IsRoughEstimate }`.
1. Take `PullListRelease` rows not hidden, with `StoreDate` from today up to 7 days ahead.
2. Map each to a local series through the same id mapping Wanted uses (`PullListService.ListSeriesIdFor`, `PullListService.cs:174-187`,
   then `WatchedSeries.SeriesId`). Releases with no local series are skipped. Matching is by provider id, never by name.
3. Catch-up issues: the series' owned, non-placeholder, non-missing issues with `NumberSortKey()` below the release number's sort key and
   not `HasBeenRead()`.
4. Keep the series only if it has at least one catch-up issue. When a series has two releases in the window, keep the nearer one (the count
   covers everything before it).
5. Time: `ReadingPace.TimeLeftInIssue` for an in-progress issue, `TimeFor` for unread ones; slice B's defaults when pace is null.
6. Order by `StoreDate`, then series name.

### 3.2 Home section

- New `HomeSectionKey` "BeforeReleaseDay", display name "Before release day", default position right after Continue Reading; hidden when
  empty; movable and hideable in Preferences › Appearance › Home like the others (`Models/HomeLayout.cs`, `ViewModels/Home/HomeSections.cs`,
  `HomeScreenViewModel` section registry and `BuildSnapshot`).
- Header "Before release day" (`pbTextSection`) + "· from your pull list" (`pbTextMeta`).
- One row per item, matching the Up Next row style: cover, "<Series> #<n> is out <weekday>" (or "today"/"tomorrow"), "N to catch up
  (#a–#b) · about M min" ("rough estimate" appended when rough), and a "Catch up" button (`Button.primary` on the first row,
  `Button.secondary` on the rest).
- "Catch up" opens the first catch-up issue in the reader with `CatchUpIssueIds` as the in-memory queue.
- No Activity Center alert. R3's digest mentions it.

### 3.3 Tests (R1)

Calculator: window edges (today, +7, +8); unmapped release skipped; read, missing and placeholder issues excluded; caught-up series dropped;
two releases → nearer kept; rough estimate when pace is null; ordering. Home: section hidden when empty; default position; Catch up passes
the queue. Headless render in default, daylight and matrix.

## 4. R2 — Rating suggestions (#5)

### 4.1 When a suggestion exists

On opening a series' Detail screen (comics and manga), when all hold:
- At least 3 of the series' issues are read (`HasBeenRead()`), and none of the series' issues has a rating.
- `Series.ReadingStatus` is not Dropped or Paused.
- No `HealthFindingDismissal` with `Kind` = RatingSuggestion for the series.

### 4.2 The formula

`RatingSuggestionResolver.Suggest(context, seriesId, userBaseline)` over the series' `ReadingEvent` rows (backfill rows excluded with
`ReadingEventFilters.WithoutBackfill`) and its issues:

| Signal | Change | Reason text |
|---|---|---|
| Start | 3★ | — |
| Read every owned issue | +1 | "You read all N issues" |
| Binge: median gap between consecutive Finished events is under half the user's own median gap | +½ | "You read N issues in D days" |
| Re-read: any issue with more than one Finished event | +½ | "You went back to #n" |
| Pace quicker than the user's median (`ReadingPaceResolver` per series vs overall) | +½ | "You read it faster than usual" |
| Two or more gaps over 21 days between consecutive reads | −½ | "You paused it a few times" |

- Slow pace never lowers the score (dense books read slowly).
- Clamp to 2–5, round half up to whole stars.
- **Calibration:** if the user has rated 10 or more series, shift every suggestion by (user's mean series rating − mean of the formula's
  unclamped output over those rated series), then clamp and round again.
- The user's medians (gap between consecutive finishes, seconds per page) come from the whole log; when either is unavailable its signal is
  skipped, not guessed.
- Reasons: the two signals with the largest absolute change, joined with " and ".

### 4.3 The chip

Under the series header on Detail, `Border.card` row: "Rate this series? Suggested ★★★★☆" (the stars are clickable, 1–5 whole stars, starting
at the suggestion), the reason line (`pbTextMeta`), `Button.secondary` "Not now", `Button.primary` "Rate N★" (N follows the stars).
- **Rate:** sets `Issue.Rating` = N on every read issue of the series that has no rating, in one save; `SeriesRatingResolver` recomputes
  `Series.Rating` (existing behaviour) and a `SeriesActivityEventKind.RatingChanged` is logged, as Quick Rate does. The chip disappears.
- **Not now:** writes the dismissal; the chip never returns for that series.
- Comics and manga only; books are excluded.

### 4.4 Review prompt parity fix

`MaybePromptReviewOnFinish` returns early when the issue already has a rating (CE's rule). The end card's "Rate" button
(`EndCardRate`) and the palette's "Rate this issue…" still open Quick Rate for any issue.

### 4.5 Tests (R2)

Formula table (each signal alone, combinations, clamp, rounding, calibration on and off, missing medians skip signals). Eligibility (under 3
read, any rated issue, Dropped/Paused, dismissed). Rate writes only read, unrated issues and the series rating follows. Not now persists.
Review prompt skips rated issues and still shows for unrated ones. Headless render of the chip in three themes.

## 5. R3 — Weekly digest (#19)

### 5.1 The builder

`WeeklyDigestBuilder.Build(context, periodEndUtc, pace)` returns `WeeklyDigest?` covering the 7 days before `periodEndUtc`. Each line is
`{ Kind, Text, Detail, LinkKind, LinkPayload }`. Lines, in this order, each omitted when it has nothing to report:

| Kind | Text / detail | Source | Link |
|---|---|---|---|
| Reading | "7 issues finished · 412 pages" / change vs the previous 7 days | `ReadingEvent` Finished count, sum of `PagesRead` | Insights |
| Releases | "4 releases this week from your pull list" / "You're behind on 2: Saga, Monstress" | `PullListRelease` next 7 days + R1's calculator | Home |
| Added | "23 issues added" / top two publishers | `Issue.AddedTime` | Library saved filter (added in the last 7 days) |
| Finished series | "You finished Paper Girls" / issue count, start month | Finished events completing a run (the rule `MilestoneResolver` uses for "complete run") | Series Detail |
| Stalled | "2 series have stalled" / names, "3+ weeks" | `UpNextResolver`'s Stalled reason (21 days) | Insights |
| Goal | "Goal: 50 issues this year" / "41 so far · on track" or "N behind" | the existing goal pace check | Insights goals |
| Metadata | "Metadata: 18 issues scraped, 6 proposals accepted" / "From scheduled scrapes" | scrape-task `ActivityRun.ItemsProcessed`; `MetadataProposal.ResolvedAt` accepted | Activity Center |

Returns null when every line is omitted.

### 5.2 Storage and schedule

- `WeeklyDigest` rows; keep the newest 12, delete older ones when a new one is written.
- New `ScheduledTaskCatalog` descriptor "Weekly digest", `ScheduleMode.DailyAt`, default 09:00. The body does nothing unless today
  (local) is `WeeklyDigestDay` and no digest exists for this period; otherwise it builds, stores, and finishes with `handle.Succeed(summary,
  link)`. The Automation tab shows the day and time for this task (a `SuggestBox` for the weekday, the existing time field for the time).
- Quiet week: nothing stored, the run succeeds with "Nothing to report this week", and no toast.

### 5.3 In the app

- **Activity Center:** a "Your week" entry per stored digest (newest first), opening the dialog. Entries survive restarts because they come
  from the `WeeklyDigest` table, not from session alerts.
- **Toast:** one "Your week in Paperbunkr" toast with an Open action when a digest is stored, following `ScheduledTaskNotificationLevel`.
- **Dialog:** `DialogCard`, Size Medium, title "Your week", subtitle the date range. One row per line: tinted icon square (Fluent icon per
  kind), text (`pbTextBody`), detail (`pbTextMeta`), and a `Button.ghost sm` link at the right. Footer: Close (`primary`).
- **Links:** resolved by `MainViewModel.ResolveActivityLink`. New `ActivityLinkKind` values: `Insights` (payload: tab, e.g. goals), `Home`,
  `ActivityCenter`. Library and Series Detail reuse `LibrarySavedFilter` and `SeriesDetail`.

### 5.4 Tests (R3)

Builder: each line from seeded data; omission of empty lines; null on a quiet week; period boundaries; the previous-week comparison. Task:
runs only on the chosen weekday; no duplicate for the same period; retention keeps 12. Link resolution for the three new kinds. Dialog
render in three themes. Migration test for the table and settings.

## 6. Implementation notes

(Filled in while building: deviations from this spec, premises found wrong, and what was verified on screen.)
