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
device sync, Mylar-style arc folders and reading-order filename prefixes, a dedicated "blackhole"
watch folder with its own metadata injection (see section 6), and external webhooks (Discord/ntfy/
Slack; Activity Center covers in-app notification, so this is backlog).

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
  ComicVine HTTP goes through one shared `DelegatingHandler`: min ~1.1 s spacing (ComicVine returns HTTP 420 / status_code 107 on velocity violations), an hourly budget
  (~200 req/h, the documented ComicVine limit), ban/429 detection that pauses all ComicVine calls.
  The handler is also applied to the existing `ComicVineSource` (whose per-instance `ThrottleAsync`
  has no hourly cap or ban handling). Other sources are left alone.
- **Priority (review fix):** requests carry a priority. **Foreground** (manual scraping, UI browsing)
  is High and jumps the queue; **background** (daemon polling) is Low and yields. The hourly budget
  also reserves a slice (~25%, i.e. ~50 of 200/h) that only High-priority calls can use, so a large
  background scan can never starve the UI. Ban/429 pauses both classes.
- Mylar's floor is 2 s between requests; 1 s is kept per the original brief and the current code, with
  the hourly budget as the real guard.

## 4. Data model (EF Core, new migration)

- **`WatchedSeries`:** ComicVine volume id, optional link to local `Series`, `WatchFutureReleases`,
  monitored/paused.
- **`WantedIssue`:** ComicVine issue id, series link, number, store date, `Status`, torrent hash
  (once snatched), optional `IssueId` (once imported), last-searched timestamp.
- **`ReleaseCandidate`:** title, size, seeders, indexer, score, magnet or `.torrent` reference,
  `WantedIssueId`.
- **`ReleaseBlocklist`** (review fix): release name and/or torrent hash, reason (`Corrupt`,
  `PasswordProtected`, `Unreadable`, `UserRejected`), timestamp. Checked in step 4 of the loop so a
  known-bad release is never fetched twice. Written when import fails or the user rejects a candidate.
- **`AcquisitionSettings`:** Prowlarr URL/key; qBittorrent URL, credentials, category (default
  `paperbunkr-comics`, see section 5), save path; and a **destination library folder** (which
  existing library folder imports and new series go into, since imports can create series folders
  from the rename template).
  Secrets go through `CredentialStore`.
- **`CredentialStore` upgrade (review fix):** qBittorrent WebUI credentials must not be plain text.
  `CredentialStore` (the single choke point for provider secrets) encrypts values at rest with
  Windows DPAPI (`ProtectedData`, `CurrentUser` scope). The app targets Windows only (Win32 PDFium,
  win-x64 LibHeif), so `Microsoft.AspNetCore.DataProtection` is unnecessary. Existing rows
  (ComicVine, tracker keys) are encrypted by a one-time migration on first read/write; values that
  don't decrypt are treated as legacy plain text and re-saved encrypted. Known consequence: a copied
  DB on another Windows account or machine loses its secrets and the user re-enters them. Shared
  per-user dev DBs across worktrees keep working (same user).
- The new migration must follow the repo's migration conventions (no up-down-up test antipattern; keep
  the Designer snapshot in sync).

## 5. Acquisition loop (per tick)

1. Refresh watched volumes from ComicVine within the shared budget.
2. Promote `Wanted` rows whose store date has arrived out of "Upcoming".
3. For each `Wanted` issue, query Prowlarr with a **cascading strategy (review fix)**: first the
   **strict, exact series name** (punctuation intact, so "X-Men", "Spider-Man", "+Anima" survive)
   crossed with the number variants — unpadded (`5`), `05`, `005`, plus volume and year forms. Only if
   the strict pass yields **zero accepted results** does it fall back to a **sanitized alias** query
   (punctuation/stopwords stripped, behavior mirroring Mylar) across the same variants. The first
   accepted hit stops the search. Prowlarr is local, so the extra calls are cheap.
4. Filter and score results: seeders, size limits, release group, preferred format. CBZ gets a small
   bonus and CBR a small penalty only (Paperbunkr reads CBR and repacks to CBZ on import). Weights are
   configurable; Omnibus's numbers are not copied. Verify that title, issue number and year actually
   match before a result is accepted. Skip anything on the `ReleaseBlocklist`. **Range/pack titles**
   (e.g. "v1-6", "#1-12", "Complete") are flagged as packs and not auto-accepted for a single-issue
   want; the user can still approve one manually (see import, multi-issue).
5. Store `ReleaseCandidate`s. **Manual approve is the default**; auto-grab is a later toggle.
6. Approving pushes the magnet/`.torrent` to qBittorrent → `Snatched`, store the hash.
7. Poll by hash for progress → `Downloading` events; on completion → import.

**Issue-owned check ("ignore what I have"):** match owned issues by ComicVine issue id first, then
series + number; manual "I have this / ignore" action sets `Ignored`. Never request an issue already
on disk.

**Manual drop-in (review suggestion, scoped down):** the app already has `LiveFolderWatchService` /
`LibraryFolderScanner`, so a `.cbz` the user drops into a library folder is already ingested. The
daemon hooks into that: when a scanned issue matches a `Wanted`/`Snatched` row by the owned-check
rules, the row is closed as `Imported` and its candidates are discarded. This covers "found it on
Discord" without qBittorrent. A separate blackhole folder that also repacks and injects metadata is
deferred until there's a real need.

**Cancelling the redundant torrent (review fix, refined):** closing a row by drop-in must not leave a
torrent that later finishes, maps to nothing and errors. The daemon asks `IDownloadClient` to handle
the torrent, restricted to the Paperbunkr category (below):
- **Still downloading:** remove the torrent and delete its partial files, but only if it maps to that
  one issue. A pack that still maps to other open `Wanted` rows is kept.
- **Already completed/seeding:** leave it seeding (removing it early hurts private-tracker ratio and
  the payload is not "dead"). The import engine treats "row already `Imported`" as a quiet no-op, not
  an Activity Center error.
This is the one place the daemon deletes files in qBittorrent, so it is limited to incomplete
torrents in the Paperbunkr category that it added itself (hash recorded in `WantedIssue`).

**qBittorrent category lock (review fix):** every torrent the daemon adds is put in the category from
`AcquisitionSettings` (default `paperbunkr-comics`). The polling loop, hash matching, cancel and
cleanup operate **only** on torrents in that category **and** whose hash is recorded by Paperbunkr;
all other torrents in the client are ignored and never touched.

## 6. Import (slice 3)

**The torrent's payload in qBittorrent's download folder is never modified** (seeding depends on it).
All changes happen on a copy.

- **Hash maps to the download, not to one issue (review fix).** The torrent hash identifies the
  `ReleaseCandidate`/download. On completion the import engine **inspects the payload** and maps each
  file to a `WantedIssue` individually: first by the queued issue (single-file case), otherwise by
  parsing filenames (reusing CE's `ComicNameInfo`) and embedded `ComicInfo.xml`. A multi-issue pack
  therefore imports every matching file to its own row; files that match no wanted row are left
  unimported and reported in Activity Center (no silent mapping of a whole folder to one issue).
- **Copy, modify, move (review fix; replaces "hardlink first").** Repacking and writing
  `ComicInfo.xml` change the file's bytes, so a hardlink would corrupt the seeded payload. Flow:
  copy the source file to a temp directory -> repack `.zip`/`.rar` folders to `.cbz` -> write
  `ComicInfo.xml` via the existing engine code (`Paperbunkr.Engine.ComicInfo`) -> rename by template
  -> move the result into the library. **Hardlink is used only when no modification is needed**
  (already a `.cbz` and the user has disabled metadata write-back for acquisitions); otherwise it is
  always copy-modify-move. Users can opt into moving the original (which ends seeding).
- **Failures feed the blocklist (review fix).** A corrupt, password-protected or unreadable archive
  sets the issue `Failed` and adds the release to `ReleaseBlocklist`; the loop then searches again
  and skips it.
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
- **Unknown parent series (review fix):** an arc can include an issue from a series the user doesn't
  track. Before converting a placeholder to a `WantedIssue`, "Request missing" resolves the issue's
  ComicVine volume. If no local `Series`/`WatchedSeries` exists for it, it fetches the volume
  metadata (through the priority handler), creates the local `Series` and a `WatchedSeries` row with
  **`WatchFutureReleases = false`** (tracked for naming/folder purposes, **not** followed), and only
  then queues the `WantedIssue`. Requesting one crossover issue must never silently subscribe the
  user to the whole series. The series folder itself is created at import time from the rename
  template inside the destination library folder (section 4), not up front.
- Downloads land in the series folder; the relink replaces the placeholder in list order.
- **To verify while planning:** how `ReadingListMatcher` assigns a `Series` to a placeholder `Issue`
  today (existing placeholder series vs. none), so the new `Series` is linked or reused rather than
  duplicated.
- **"Follow arc"** (slice 4): off by default; an 8th scheduled task that re-runs Refresh and
  requests new gaps within the shared ComicVine budget.
- Fulfills the earlier deferred "arc gap detection" note from the story-event auto-population design.

## 9. Slices

1. **Slice 1:** watchlist, Missing/Upcoming lists, Wanted screen, series Missing Issues section, arc
   "Request missing", Prowlarr search -> candidates review. **No downloads.** Includes the prerequisites
   that new secrets and ComicVine traffic need: the **`CredentialStore` DPAPI upgrade** (Prowlarr key
   lands here) and the **prioritized shared ComicVine handler**.
2. **Slice 2:** qBittorrent grab + progress tracking; `ReleaseBlocklist` (manual reject).
3. **Slice 3:** import (payload inspection and per-file mapping, copy-modify-move, `ComicInfo.xml`,
   placeholder relink, drop-in close-out, failure -> blocklist).
4. **Slice 4:** auto-grab toggle and "Follow arc".

### Backlog (from review, not in slices 1-4)

- Library right-click **"Repack & Inject Metadata"** to push a raw `.zip`/`.cbr` through the import
  pipeline manually (the scanner reads files but doesn't inject `ComicInfo.xml`).
- **Release upgrade path:** a cutoff-quality setting (e.g. prefer CBZ) that keeps an issue monitored
  after a lower-quality import and replaces it if a better release appears.
- Blackhole watch folder with repack/metadata injection; external webhooks.

## 10. Errors, testing, safety

- Prowlarr/qBittorrent unreachable → loop pauses, Activity Center alert, exponential backoff.
  Failed torrent → `Failed` (retry only if configured). ComicVine ban/429 → all ComicVine calls pause.
- Tests: unit tests for query variants, scoring, status machine, owned-matching; fake Prowlarr and
  qBittorrent HTTP servers for client tests; in-memory SQLite for data tests; UI tests use
  a per-class `PumpDispatcher()` (`Dispatcher.UIThread.RunJobs()`).
- Safety: no bundled indexers/trackers; manual approve by default; secrets follow `CredentialStore`.

## 11. Open items to verify during planning

- Exact ComicVine fields for issue store dates and volume issue lists (new client; check the API, not
  memory).
- Mylar behaviors marked unverified in research (Skipped/Archived/Ignored statuses, pull-list
  refresh, arc match keys) were not relied on.
- Whether the `Series`/`Issue` schema needs a ComicVine issue id column for owned-matching.
