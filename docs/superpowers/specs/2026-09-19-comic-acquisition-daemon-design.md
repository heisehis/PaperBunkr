# Comic Acquisition Daemon (Mylar-style want-list + acquisition) — Design

Date: 2026-09-19 · Status: draft for user review

## 1. Goal and scope

Give Paperbunkr a Mylar3-style acquisition loop: track series on a watchlist, compute missing and
upcoming issues, search indexers through Prowlarr, hand approved releases to qBittorrent, and import
finished files into the library. Story-arc gaps in reading lists feed the same want-list.

Behavior is modeled on Mylar3 by studying what it does; **no Mylar3 code is copied** (it is GPL-3).
The UI pattern for per-series "Missing Issues" is modeled on Omnibus (hankscafe/omnibus).

**Target stack (user's own):** Prowlarr (Torznab) + qBittorrent. Paperbunkr ships **no** indexers,
tracker lists or defaults; the user supplies their own Prowlarr.

**Out of scope (deferred):** SABnzbd, direct Newznab/Torznab clients, Deluge/NZBGet, owned-trade
coverage ("covered by" collected editions), per-issue snooze, pull-list scraping, Mylar's tablet
device sync, Mylar-style arc folders and reading-order filename prefixes, encryption of stored keys
(follows existing `CredentialStore`).

## 2. Deviations from the original brief (Gemini spec) and why

| Brief said | This design | Reason |
|---|---|---|
| LiteDB | EF Core + SQLite in the existing DB and migrations | The repo has no LiteDB; `Paperbunkr.Data` is EF Core 10 / SQLite |
| `Core` / `Daemon` / `UI` projects | One new `Paperbunkr.Daemon` project; contracts live in it | Repo already has `Common`, `Data`, `Engine`, `App`; a separate `Core` is YAGNI until a headless host exists |
| Torznab + Newznab clients | Prowlarr Torznab endpoint only, behind `IIndexerClient` | User runs Prowlarr; it handles indexer auth and limits |
| qBittorrent + SABnzbd | qBittorrent only, behind `IDownloadClient` | User's stack |
| Status machine `Wanted…Ignored` | Kept as `Wanted, Snatched, Downloading, Imported, Failed, Ignored` | Matches brief; "Upcoming" is a future-dated `Wanted` row, not a status |

## 3. Architecture

- **`Paperbunkr.Daemon`** references `Paperbunkr.Data` and `Paperbunkr.Common`. It **never references
  Avalonia**. It exposes `IIndexerClient`, `IDownloadClient`, `IComicVineClient`, `IEventPublisher`.
- **Hosting:** `App` builds a generic host and starts `AcquisitionService : BackgroundService`
  (`PeriodicTimer`) alongside the existing `SchedulerService`. Later extraction to a headless host is a
  project move, not a rewrite.
- **Events:** the daemon writes `DaemonEvent`s (e.g. `DownloadProgressEvent`) to a
  `Channel<DaemonEvent>` behind `IEventPublisher`. `App` drains the channel into **Activity Center**
  (jobs, alerts, toasts). No second progress UI.
- **Threading/DB:** each tick uses its own `DbContext` with short transactions; SQLite WAL keeps UI
  reads unblocked.
- **ComicVine client:** new `ComicVineClient` (volume search, volume issue lists, store dates). All
  ComicVine HTTP goes through one shared `DelegatingHandler`: min 1 s spacing, an hourly budget
  (~200 req/h, the documented ComicVine limit), ban/429 detection that pauses all ComicVine calls.
  The handler is also applied to the existing `ComicVineSource` (whose per-instance `ThrottleAsync`
  has no hourly cap or ban handling). Other sources are left alone.
  Mylar's floor is 2 s between requests; 1 s is kept per the original brief and the current code, with
  the hourly budget as the real guard.

## 4. Data model (EF Core, new migration)

- **`WatchedSeries`:** ComicVine volume id, optional link to local `Series`, `WatchFutureReleases`,
  monitored/paused.
- **`WantedIssue`:** ComicVine issue id, series link, number, store date, `Status`, torrent hash
  (once snatched), optional `IssueId` (once imported), last-searched timestamp.
- **`ReleaseCandidate`:** title, size, seeders, indexer, score, magnet or `.torrent` reference,
  `WantedIssueId`.
- **`AcquisitionSettings`:** Prowlarr URL/key; qBittorrent URL, credentials, category, save path.
  Keys go in `CredentialStore` like the ComicVine key (plain text in SQLite today; stated, not fixed
  here).
- The new migration must follow the repo's migration conventions (no up-down-up test antipattern; keep
  the Designer snapshot in sync).

## 5. Acquisition loop (per tick)

1. Refresh watched volumes from ComicVine within the shared budget.
2. Promote `Wanted` rows whose store date has arrived out of "Upcoming".
3. For each `Wanted` issue, query Prowlarr with number variants: unpadded (`5`), `05`, `005`, plus
   volume and year forms; strip punctuation/stopwords in the series name (behavior mirrors Mylar).
4. Filter and score results: seeders, size limits, release group, preferred format. CBZ gets a small
   bonus and CBR a small penalty only (Paperbunkr reads CBR and repacks to CBZ on import). Weights are
   configurable; Omnibus's numbers are not copied. Verify that title, issue number and year actually
   match before a result is accepted.
5. Store `ReleaseCandidate`s. **Manual approve is the default**; auto-grab is a later toggle.
6. Approving pushes the magnet/`.torrent` to qBittorrent → `Snatched`, store the hash.
7. Poll by hash for progress → `Downloading` events; on completion → import.

**Issue-owned check ("ignore what I have"):** match owned issues by ComicVine issue id first, then
series + number; manual "I have this / ignore" action sets `Ignored`. Never request an issue already
on disk.

## 6. Import (slice 3)

- Match the finished download to its `WantedIssue` **by torrent hash**; filename parsing only as a
  fallback for multi-issue packs.
- **Hardlink** into the library, falling back to **copy** across drives; **move** only if the user
  asks. This keeps seeding working (Mylar moves by default, which breaks private-tracker seeding).
- Repack `.zip`/`.rar` folders to `.cbz`; write `ComicInfo.xml` via the existing engine code
  (`Paperbunkr.Engine.ComicInfo`); rename by template.
- **Rename template:** CE-style `{token}` with `[optional group]`, default
  `{publisher}/{series} ({year})/{series} #{number}.cbz`.
  **CE parity note (verified in `_reference/ComicRackCE`):** CE's `ComicBook.FormatTitle` supports
  series, title, volume, number, year, month, day, format and filename, applies **no zero-padding**,
  and has **no publisher token**. `{publisher}` and a zero-pad option are therefore deliberate
  deviations needed for folder layouts; they are added on top of CE's syntax, not replacing it.
- After import, the real file replaces any placeholder `Issue` via the existing
  `ReadingListMatcher` relink so reading lists fill in place.

## 7. UI

- **Wanted screen (Layout A):** new nav-rail entry; pill tabs **Wanted, Upcoming, Candidates,
  Series**; "Search now" button. Mockup: `.superpowers/brainstorm/18605-1789845613/content/
  wanted-screen-layout-v2.html`.
- **Series Detail screen:** a "Missing Issues (n)" section styled like Omnibus's (greyed cover +
  Request button per issue), computed as the ComicVine issue list minus owned issues. "Request"
  marks the issue `Wanted` and queues a search. A separate **Watch series** toggle controls future
  releases. **Request all shown** asks for confirmation.
- **Upcoming:** not-yet-released issues show as "awaiting release" and become `Wanted`
  automatically on their store date. No snooze in v1.
- **Preferences:** a new tile for Prowlarr + qBittorrent setup with connection tests.
- All notifications go through Activity Center. UI work loads the `avalonia` skill and runs
  `avalonia-pro-max/review-checklist` before completion. Any "remove/close from inside a row" button
  must defer via `Dispatcher.UIThread.Post` (project runtime gotcha).

## 8. Story arcs ↔ reading lists

- An arc-linked reading list (`Source` + `ArcId`, built by `ArcReadingListBuilder`) with placeholder
  `Issue`s **is** the arc want-list. No Mylar-style `storyarcs` table, no arc folder, no filename
  order prefix.
- **"Request missing"** on arc-linked lists converts placeholders to `WantedIssue` rows using the
  ComicVine issue ids the arc source already returns, with a confirmation for bulk. Non-arc lists
  (hand-made, `.cbl`) get only a per-item Request on their placeholders.
- Downloads land in the normal series folder; the relink replaces the placeholder in list order.
- **"Follow arc"** (slice 4): off by default; an 8th scheduled task that re-runs Refresh and
  requests new gaps within the shared ComicVine budget.
- Fulfills the earlier deferred "arc gap detection" note from the story-event auto-population design.

## 9. Slices

1. **Slice 1:** watchlist, Missing/Upcoming lists, Wanted screen, series Missing Issues section, arc
   "Request missing", Prowlarr search → candidates review. **No downloads.**
2. **Slice 2:** qBittorrent grab + progress tracking.
3. **Slice 3:** import (hardlink/copy, CBZ repack, rename, `ComicInfo.xml`, placeholder relink).
4. **Slice 4:** auto-grab toggle and "Follow arc".

## 10. Errors, testing, safety

- Prowlarr/qBittorrent unreachable → loop pauses, Activity Center alert, exponential backoff.
  Failed torrent → `Failed` (retry only if configured). ComicVine ban/429 → all ComicVine calls pause.
- Tests: unit tests for query variants, scoring, status machine, owned-matching; fake Prowlarr and
  qBittorrent HTTP servers for client tests; in-memory SQLite for data tests; UI tests use
  `TestDispatcher.Drain()`.
- Safety: no bundled indexers/trackers; manual approve by default; secrets follow `CredentialStore`.

## 11. Open items to verify during planning

- Exact ComicVine fields for issue store dates and volume issue lists (new client; check the API, not
  memory).
- Mylar behaviors marked unverified in research (Skipped/Archived/Ignored statuses, pull-list
  refresh, arc match keys) were not relied on.
- Whether the `Series`/`Issue` schema needs a ComicVine issue id column for owned-matching.
