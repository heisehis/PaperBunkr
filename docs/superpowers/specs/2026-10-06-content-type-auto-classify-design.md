# Content-type auto-classify pipeline - design

Date: 2026-10-06. Roadmap item: "Content-type classification & manga metadata scraping" (Paperbunkr-Roadmap.md ~line 848) - the
tracker-driven pipeline that the publisher heuristic (`254b36e`) was explicitly a pre-filter for.

Decided in a grilling pass with the user on 2026-10-05/06 (every question answered "as recommended" except the queue layout: the user
chose **dense table (A) + cards (C)**, resolved as table rows that expand into a card). CE parity: CE has only the `Manga`
Yes/No/YesAndRightToLeft flag - no Manhwa/Manhua, no auto-classification - so this is Paperbunkr-only.

## Problem

`Series.ContentType` (Comic/Manga/Manhua/Manhwa/Unknown) is set by the scanner chain at series creation (embedded `Manga` flag, then
publisher, then `LanguageISO`), by a weekly publisher-only sweep over Unknown series, or by hand. Three gaps:

1. Nothing uses the trackers/providers to classify a series, and nothing can tell Manhwa/Manhua from Manga (`PublicationFormat` is
   stored but unused; AniList `countryOfOrigin` and MangaDex `originalLanguage` are not fetched).
2. Nothing records *where* a type came from, so the sweep cannot tell a deliberate "Unknown" from a never-classified one and can overwrite it.
3. Series the heuristic cannot resolve have only the manual dropdown; Library Health's Content-type queue just lists every Unknown series.

## Settled design

### Data model (one migration, `AddContentTypeProvenance`)

`Series` gains:

| Field | Meaning |
|---|---|
| `ContentTypeSource` (`Unset/Embedded/Language/Publisher/Provider/Manual/Existing`) | where the current type came from |
| `ContentTypeConfidence` (`double?`) | title-match confidence for a `Provider` source |
| `ContentTypeLocked` (`bool`) | a human decided; no pipeline ever changes or re-asks |
| `PreviousContentType`, `PreviousReadingMode` (nullable) | what Undo restores |
| `ContentTypeSuggestion` (`ContentType?`), `ContentTypeEvidence` (json text) | a queued, not-yet-accepted candidate and its per-source chips |
| `ContentTypeCheck` (`None/Matched/NoMatch/Skipped`), `ContentTypeCheckedUtc` | "tried, no match" memory (retry after 90 days); `Skipped` = user said not a comic (never retried) |

`AppSettings` gains `AskBeforeClassifying` (default false). Backfill: every existing non-Unknown series becomes
`Locked = true, Source = Existing`; Unknown series stay unlocked. `Down()` is a no-op (the repo's SQLite orphaned-column rule).

Every manual write (bulk edit, Library context menu + bulk setter, both Detail headers' pickers, the queue's actions) goes through
`SeriesContentTypeEditor.SetManual`, which sets `Source = Manual, Locked = true` and clears any suggestion. The scanner stamps
`Embedded/Language/Publisher` at series creation (unlocked). CE migration / remote mirror / new-issue creation are left as they are
(migration rows are `Unset`; a non-Unknown `Unset` row is treated as locked by the migration backfill only).

### Decision core (`Paperbunkr.Data/Metadata/ContentTypeClassifier*`)

Per series: skip gate, then for each provider in order **MangaBaka, AniList, MangaDex**: `SearchAsync(series name)`, score every
result against the series' known titles with `TitleMatchScorer` (0.95 Auto / 0.75 NeedsReview), `GetAsync` the best, map to a type.

- **Skip gate (not searched, counted + shown in the queue):** `GcdSeriesId` set, the publisher heuristic says Comic, an issue's `MetadataSource` is
  ComicVine/Metron, or the series is an unlocked `Embedded` Comic (i.e. `Manga = No`).
- **Provider type mapping (`ProviderContentTypeMapper`):** MangaBaka `type` manga/manhwa/manhua direct; `novel` = ignored; `oel` = Comic, queue-only; other = unmapped, queue-only.
  AniList `format` + `countryOfOrigin`: JP -> Manga, KR -> Manhwa, CN/TW/HK -> Manhua; `ONE_SHOT` defaults to Manga with no country; `NOVEL` ignored.
  MangaDex `originalLanguage`: ja -> Manga, ko -> Manhwa, zh/zh-hk -> Manhua, en -> Comic queue-only.
- **Reading mode follows type** (Manga RightToLeft, Manhwa/Manhua Webtoon, Comic LeftToRight) - applied only while the series' `ReadingMode`
  is still the default `LeftToRight`; never touches per-issue overrides.
- **Auto-apply** (needs `AskBeforeClassifying` off): at least one Auto-tier, unambiguously typed vote, all votes agree, and corroboration - a second provider
  agreeing, or the publisher heuristic / the series' current unlocked type agreeing. Result: `Source = Provider`, confidence, previous values saved.
- **Queue:** anything else with a >= 0.75 typed match - `ContentTypeSuggestion` + evidence stored, nothing applied. Conflicting types are marked as a conflict (never bulk-accepted).
- **No match:** `ContentTypeCheck = NoMatch`, retried after 90 days.
- **Priority:** Manual (locked, never touched) > high-confidence provider match > embedded flag > language > publisher.
- **No side effects:** no `ExternalMediaId`/tracker link is created and nothing is enqueued for file write-back. (A later manual change through the Detail pickers
  still enqueues write-back as it does today; the existing write-back maps `ContentType` to the ComicInfo `Manga` field.)

### Entry points

- **Scheduled task `ContentTypeTrackerClassify`** ("Classify series from trackers"): `SchedulerResourceClass.Network`, off by default, daily (see the implementation notes), a per-run budget of 150 series,
  resumable (it takes series by oldest `ContentTypeCheckedUtc` first, never-checked first). The existing weekly publisher sweep stays, now skipping locked rows and stamping `Source = Publisher`.
- **"Classify library"** button in Preferences > Automation (runs the same core now); **"Classify selected"** in the Library series context/bulk menus.
- **Preferences > Behavior:** "Ask me before changing any type" toggle.

### Confirm queue (Library Health > Review > Content type)

Dense table; Enter or a click expands a row into a card (cover, one chip per source, raw provider type as tooltip). Columns: series, now, suggested, evidence, confidence.
Row actions - all four write the lock: **Accept** (apply suggestion), **Change** (type picker), **Keep** (lock the current type), **Not a comic: skip** (`Skipped`).
Header: "Accept all high confidence" (two-step confirm; conflicts excluded), skipped/no-match counts, "Re-check publisher-classified series" (unlocks and re-queues
`Existing`/`Publisher` rows whose publisher still matches the heuristic). A collapsed "Recently auto-classified" section (30 days, newest first) has Undo per row;
the Detail header keeps an Undo for an auto-classified series with no time limit (`PreviousContentType` is on the row). Activity Center gets one summary alert when the queue is non-empty.

## Implementation notes (where the build differs from the text above)

- **Schedule is daily, not 30-day.** A 150-search budget once a month would take over a year to cover a few thousand series. The task is still off by default, and once
  nothing is due a run does no network work.
- **Queue query is three flat queries**, not one projection: EF on SQLite cannot translate correlated `First()`/`Distinct()` inside a `Select` (SQL APPLY), and the review
  refresh swallows that error, so the queue would have shown a stale list.
- **No separate Activity Center alert.** The run's own Activity Center entry carries the summary ("N waiting for your review") and a link to Library Health, which is
  what the "summary alert when the queue is non-empty" was for.
- **Queue "Change" is four buttons in the expanded card** (Comic / Manga / Manhwa / Manhua), not a popup picker: it reuses the app's per-value-command shape.
- Existing write-back still maps `ContentType` to the ComicInfo `Manga` field when a person changes the type on Detail; only the pipeline itself enqueues nothing.

## Staging

1. Core + data model + manual-write lock + sweep fix (tested headlessly).
2. Scheduled task, Classify library/selected, Preferences toggle.
3. The confirm queue UI.

## Not included

Detail-fetch extensions for MangaUpdates/Kitsu/MAL/Shikimori (three more calls per series to confirm what the three providers already say), automatic tracker linking,
write-back of the `Manga` field by the pipeline.
