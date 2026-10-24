# Scraper & Organizer B: Speed core

Date: 2026-10-21. Part 2 of 3 ([A reliability](2026-10-21-scraper-organizer-a-reliability-design.md), B, [C automation and UX](2026-10-21-scraper-organizer-c-automation-ux-design.md)).
Depends on A (Metron token) being merged.

## Goal
Bulk scraping runs many times faster without exceeding provider limits, can be cancelled and resumed, and Metron is the first provider
tried with ComicVine last.

## Facts established (2026-10-21 code survey)
- `ScrapeOrchestrator.ScrapeAsync` (`Paperbunkr.Data/ComicVine/Scraping/ScrapeOrchestrator.cs:180`) is a sequential `foreach` over books.
- After every book it waits `Math.Clamp(ScrapeDelayMs, 2000, 3_600_000)` (:217-225); the setting defaults to 1000 but is floored at 2 s.
- Every book runs its own volume search (`SearchVolumesAsync`, :336; digit/word retry :342). `_matchMemory` only boosts ranking. Only cache:
  `_issueSummaryCache` (:1133), per orchestrator, keyed volume+issue number; each new issue number re-pages `SearchIssuesAsync`.
- Metron fetches volume details per applied book when series Status is Unknown or Summary empty (:1001-1010). No HTTP cache exists.
- ComicVine: `ComicVineRateLimitHandler` 1100 ms spacing, 200/hour, Low priority 150/hour, escalating cool-off after 420/429.
  Metron (`MetronClient.cs:58-64`): 250 ms spacing, 18/min (Low 14), `MetronQuota` tracks the daily budget.
- Covers: `PassesCoverHashGateAsync` (:615-678) downloads up to three candidate covers sequentially through `CoverHttp`, uncached and
  outside the rate limiter. Metron skips downloads when the issue has a published `CoverHash`.
- Per book: its own DB context and `SaveChangesAsync` (:265, :1035), `job.Report` progress, a synchronous `ThumbnailBytes` file read.
- Write-back: `MetadataWriteBackQueue` 300 ms debounce, strictly serial, full-archive copy per comic (`UpdateZipEntries`).
- Entry points: `ScrapeCoordinator.ScrapeIssuesAsync` (library menu, Detail tabs), `ScrapeSeriesAsync`, scheduled `comicvine-scrape`
  (off by default). No "scrape all unmatched" action exists. `HeavyJobLane.Shared` allows one heavy job at a time.

## Design

### 1. Provider order
`ScrapeProviderOrder` setting, default `Metron, <others>, ComicVine`. The orchestrator asks providers in order and stops at the first
accepted match; ComicVine is reached only when Metron has no confident match or no token is configured. Spec C's field merge builds on
this order.

### 2. Series-first resolution (`SeriesBatchPlanner`)
- Books in a batch are grouped by `TitleNormalizer` series key + publisher/year hint. One volume search per group.
- The group's volume is chosen once (auto when confident; otherwise one series-match dialog for the group, not one per book).
- Each book then matches an issue from the group's cached issue list by number/cover; no per-issue paging.
- Books carrying a provider id skip the search regardless of auto-choose mode (extends `TryResolveByIdAsync`).

### 3. Provider cache (`ProviderCache`, Paperbunkr.Data/ComicVine/Caching)
- New table `ProviderCacheEntry` (Provider, Kind, Key, PayloadJson/Blob, FetchedAt, ExpiresAt, ETag?). EF migration; one add-only migration.
- Kinds and lifetimes: SeriesSearch 24 h, VolumeDetails 7 d, VolumeIssueList 7 d, IssueDetails 7 d, CoverHash never expires.
- Read-through at `MetronClient`/`ComicVineClient` call sites (not in the orchestrator), so every consumer benefits. A stale-but-expired entry is
  used when the provider is cooling off or over quota, flagged stale in the result.
- Cover hashes keyed by image URL; cover downloads go through the provider rate limiter and run at most 2 at a time.
- Preferences > Organize & Scrape: "Refresh provider data" (clears entries, with a count) and a size line. Pruning of expired rows at start.

### 4. Pacing and the 2 s floor
Remove the 2 s clamp. The limiters own pacing; `ScrapeDelayMs` stays as an optional extra delay (min 0). ComicVine limits unchanged.

### 5. Pipeline and Speed setting
Stages: Plan (group) -> Match (per group) -> Fetch (issue details, covers) -> Apply (DB) -> WriteBack. Bounded channels connect them.
`ScrapeSpeed` setting with three profiles:
| Profile | Match/fetch workers | Write-back workers | Notes |
|---|---|---|---|
| Careful | 1 | 1 | today's feel, no floor |
| Balanced (default) | 4 Metron, 1 ComicVine | 2 | |
| Fast | 8 Metron, 1 ComicVine | 3 | still gated by the limiters |
Workers never bypass the limiters; ComicVine is always 1 worker. Cancellation flows through every stage.

### 6. DB and UI churn
- Apply stage batches `SaveChangesAsync` per group (per series), one context per group; match-memory writes batched with it.
- Progress to Activity Center throttled to at most 4 updates/s plus a final update; thumbnails read async, only for the book being shown.

### 7. Write-back
Keep copy-then-swap atomicity. Add: skip a comic when the metadata that would be written equals what the archive already holds
(hash of the generated ComicInfo/MetronInfo bytes compared with the existing entry); 2 parallel writers (Balanced) on different files only
(per-path lock); folder-comics unchanged. Backoff retries unchanged.

### 8. Resumable job + "Scrape all unmatched"
- `ScrapeJob` persisted (table `ScrapeJob`: Id, Kind, Scope JSON, State, CreatedAt; `ScrapeJobItem`: JobId, IssueId, State
  Pending/Done/Skipped/Failed/NeedsReview, Reason). Items flip as each book finishes.
- On cancel or crash the job stays Paused; start-up shows it in the Activity Center with Resume / Discard. Resume re-plans only Pending items.
- New action "Scrape all unmatched" (Library toolbar scan actions area and Preferences > Organize & Scrape): scope = issues with no
  MetadataSource, not `ScrapePermanentlySkipped`, with a FilePath. The scheduled `comicvine-scrape` task is renamed "Scrape unmatched" and
  reuses the same job code (non-interactive: confident matches apply, the rest are marked NeedsReview for Spec C's queue).

## Testing
- `SeriesBatchPlannerTests` (grouping, annuals/volumes not merged), `ProviderCacheTests` (TTL, stale fallback, kinds),
  `ScrapeOrchestratorTests` extended (one search per series, no 2 s wait, id shortcut), pipeline tests with a fake provider that records
  request counts and enforces limits (assert N-issue series = 1 search; concurrency never exceeds the profile).
- `ScrapeJobStoreTests` (resume after simulated crash, discard). Write-back skip-unchanged and parallel-different-file tests.
- A benchmark in `Paperbunkr.Benchmarks`: 500 books across 40 series against a latency-injecting fake provider, before/after request counts.
- Success criteria: for 50 issues of one series, provider requests drop from ~150-200 to <= 6; Metron-first 500-book run no longer
  spends >= 1000 s in forced delays.

## Out of scope
Raising ComicVine limits; new providers; changing the match scoring itself (Spec C changes how scores are surfaced, not computed).

## As built (2026-10-21)

- The provider cache, the job store and the review list are files in the data folder (`provider-cache/`, `scrape-jobs/`,
  `scrape-review.json`), not tables: they are disposable working state, need no migration, and "Refresh" is a folder delete.
- Series-first is per run and in the orchestrator: once a library series' volume is chosen (by the user, an id, or the matcher), its other
  comics follow it without a dialog for as long as that volume has their issue number; a comic it lacks stops following and is asked about.
  This holds with "Confirm the issue too" on (only the series' first comic shows the issue dialog) and in the deferred review pass.
- Per-book parallel match/fetch workers were not built: Metron's 20/min and ComicVine's 200/h are the ceiling, so the speed comes from
  fewer requests (cache, series reuse, no forced pause). Speed controls the pause (Careful only), cover fetches and 1–3 file writers.
- The ComicVine fallback runs once at the end of a Metron run, unattended with auto-choose and the cover gate.
