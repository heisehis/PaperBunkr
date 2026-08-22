# Porting Tachiyomi/Mihon/Komikku Tracker Logic and Manga UI Patterns to Paperbunkr

*Research memo, not a design spec — feeds a future brainstorm → design-spec pass before
implementation starts (see `docs/alpha-roadmap.md`'s Beta backlog entry for this). User-supplied
research on Mihon (Tachiyomi's active fork) and Komikku (a Mihon fork), covering both tracker-
service integration and manga-specific detail-page UI, since neither exists in Paperbunkr today
and ComicRack CE has no equivalent to port from.*

## TL;DR

- **Tracker logic:** Mihon/Komikku model tracking as a single `Track` entity (one row per
  manga-per-service) plus a `Tracker`/`BaseTracker` interface hierarchy with one implementation
  class per service; all six services (MAL, AniList, Kitsu, MangaUpdates, Shikimori, Bangumi) are
  normalized behind common methods (search/bind/update/refresh/getScoreList), differing mainly in
  auth (OAuth2 for MAL/AniList/Kitsu/Shikimori/Bangumi; username→session token for MangaUpdates)
  and score scale. Mihon syncs one-way (app→tracker); Komikku adds opt-in two-way sync.
- **Manga UI:** The manga detail screen (`MangaScreen.kt` + `MangaInfoHeader.kt` + chapter-list
  items) uses a large blurred-cover header, expandable synopsis, genre "chips", ongoing/completed
  status text, a dominant chapter list with read/unread dimming + download/bookmark icons +
  scanlator attribution, and a tracking button — a denser, chapter-list-first design distinct from
  Western cover-grid comic managers.
- **For Paperbunkr:** Add a `Track` EF Core entity related to `Series`, an `ITracker` service
  abstraction with per-service implementations, store OAuth tokens locally via OS-backed
  encryption (DPAPI), and build a manga-specific Avalonia detail view (RTL/vertical badges, chip
  tags, read-state dimming) selected by `ContentType`/`ReadingMode`.

## Key findings

1. **Single normalized `Track` entity.** Both apps store one `Track` row per (manga, service)
   pair. The domain model (`domain/.../track/model/Track.kt`) carries the local `id`, `mangaId`, a
   `trackerId`/`syncId` (which service), the `remoteId`/`mediaId` (the entry on the service), an
   optional `libraryId` (the remote list-entry id, distinct from media id for AniList/Kitsu),
   `title`, `lastChapterRead` (a `Double`, to allow partial/decimal chapters), `totalChapters`,
   `status`, `score`, `startDate`, `finishDate`, `remoteUrl`, and a `private` flag.
2. **One interface, many implementations.** A `Tracker` interface + `BaseTracker` abstract class
   define shared behavior; each service is a concrete class (`MyAnimeList.kt`, `Anilist.kt`,
   `Kitsu.kt`, `MangaUpdates.kt`, `Shikimori.kt`, `Bangumi.kt`) registered in
   `TrackerManager`/`TrackManager`. Two marker interfaces refine behavior: `EnhancedTracker`
   (auto-binding self-hosted/source-linked services like Komga/Suwayomi/MangaDex) and
   `DeletableTracker` (services that support removing a remote entry).
3. **Per-service auth and score scales differ significantly.** MAL uses OAuth2 with PKCE; AniList
   uses OAuth2 + GraphQL; Shikimori and Bangumi use OAuth2 authorization-code with refresh tokens;
   Kitsu uses OAuth2 password grant (email as username); MangaUpdates uses a username/
   password→session-token login rather than OAuth. Score scales: MAL/Shikimori/Bangumi 0–10
   integer; AniList variable per user (the official `ScoreFormat` enum: `POINT_100`,
   `POINT_10_DECIMAL`, `POINT_10`, `POINT_5`, `POINT_3`); Kitsu a 20-point `ratingTwenty` internal
   scale; MangaUpdates 0.0–10.0 decimal.
4. **Matching is search-based with metadata shortcuts.** Default flow: open a series → Tracking →
   Add tracking → the app searches the service → the user picks the match. MAL supports `id:<id>`
   and `my:<name>` search prefixes. MangaDex sources can auto-associate trackers via tracker links
   embedded in metadata, and `EnhancedTracker`s auto-bind by matching the manga's source.
5. **Sync is progress-forward and configurable.** Mihon is one-way (Mihon→tracker): after reading
   the last page of a chapter (or, optionally, when a chapter is marked read), progress, status,
   and start/finish dates update; offline changes sync when back online. On conflict, the app
   keeps the higher last-read chapter (relevant for enhanced trackers). Komikku adds an opt-in
   two-way `SyncDataJob`.
6. **A history feature exists; a full statistics dashboard does not (in Mihon).** Mihon has a
   History tab (recently read chapters, resume) but it shows only the most-recent chapter per
   series; requests for full per-chapter history and reading stats have been declined/left open. A
   genuine gap Paperbunkr could differentiate on.
7. **Tokens are stored in plain SharedPreferences (unencrypted).** `TrackPreferences` wraps the OS
   preference store; per-service `*Interceptor` classes attach bearer tokens and refresh on
   expiry. No at-rest encryption in the base design — something Paperbunkr should improve on.
8. **Manga detail UI is chapter-list-first and visually distinct.** The detail screen leads with a
   large cover (often blurred/color-extracted as a backdrop), title/author/artist typography, an
   expandable description, genre chips, ongoing/completed status, source badge, and tracking
   button — then a long, dominant chapter list with read-state dimming, download/bookmark/
   scanlator/date indicators, and sort/filter controls. Reading direction (LTR/RTL/vertical/
   webtoon) is a per-manga reader setting rather than a prominent detail-page badge.

## Part 1 — Tracker logic

### 1.1 Data model (the `Track` entity)

Mihon/Komikku represent tracking with a single normalized entity: one `Track` per manga per
linked service. The domain data class lives at
`domain/src/main/java/tachiyomi/domain/track/model/Track.kt`, with a legacy database interface/
`TrackImpl` at `app/src/main/java/eu/kanade/tachiyomi/data/database/models/Track.kt` (the codebase
was migrated mid-life from snake_case DB fields like `manga_id`, `sync_id`, `media_id`,
`library_id`, `last_chapter_read`, `total_chapters`, `tracking_url`, `started_reading_date`,
`finished_reading_date` to camelCase domain names).

Fields (confirm exact names against source before porting):

- `id: Long` — local row id
- `mangaId: Long` — FK to the local library manga
- `trackerId: Long` (formerly `syncId`) — which service; a stable integer per tracker
- `remoteId: Long` (formerly `mediaId`) — the entry id on the remote service
- `libraryId: Long?` — remote user-list entry id (AniList/Kitsu need this distinct from media id)
- `title: String`
- `lastChapterRead: Double` — `Double` so partial/decimal chapters can sync
- `totalChapters: Long`
- `status: Long` — numeric status code interpreted per-tracker
- `score: Double` — normalized score
- `startDate: Long`, `finishDate: Long` — epoch millis, 0 = unset
- `remoteUrl: String` — link to the entry
- `private: Boolean` — private-tracking flag (AniList/Kitsu/Bangumi)

Status is normalized by a shared `TrackStatus` enum (`READING`, `PLAN_TO_READ`, `COMPLETED`,
`ON_HOLD`, `DROPPED`, `REREADING`), while each tracker keeps its own integer constants and maps
to/from the shared concept via `getStatusList()`/`getStatus()` and helpers
`getReadingStatus()`/`getRereadingStatus()`/`getCompletionStatus()`.

### 1.2 Tracker interface hierarchy

Directory: `app/src/main/java/eu/kanade/tachiyomi/data/track/`.

- `Tracker` interface — `id`, `name`, login state, icon/color, capability flags
  (`supportsReadingDates`, `supportsPrivateTracking`).
- `BaseTracker` abstract class — shared implementations (e.g., `setRemoteLastChapterRead`, score
  normalization).
- `EnhancedTracker` interface — auto-binding for self-hosted/source-linked services (Komga,
  Suwayomi, and, in Komikku, MangaDex/MDList).
- `DeletableTracker` interface — services supporting remote-entry deletion.

Common methods: `search()`, `add()/bind()`, `update(track, didReadChapter)`, `refresh()`,
`getScoreList()`, `displayScore()`, `login()/logout()`, plus status/score conversion helpers.
Interactors coordinate the logic: `AddTracks.kt` (binding + syncing all trackers to the highest
progress), `TrackChapter.kt` (push chapter-read progress; enhanced auto-bind), and
`SyncChapterProgressWithTrack` (reconcile local read chapters with remote).

### 1.3 The six trackers

- **MyAnimeList** — `myanimelist/MyAnimeList.kt` + `MyAnimeListApi.kt` +
  `MyAnimeListInterceptor.kt`. OAuth2 with PKCE against MAL API v2
  (`api.myanimelist.net/v2`); access + refresh tokens. Score 0–10 integer. Search supports `id:`
  and `my:` prefixes. Uses `my_list_status`/`num_chapters_read`.
- **AniList** — `anilist/Anilist.kt`. OAuth2 (token grant), GraphQL at `graphql.anilist.co`,
  bearer token. Score stored on 100 internally but displayed per the user's `scoreFormat` — the
  official `ScoreFormat` enum values are `POINT_100`, `POINT_10_DECIMAL`, `POINT_10`, `POINT_5`
  (stars), and `POINT_3` (smileys). Supports private tracking, reading dates
  (`startedAt`/`completedAt`), rereading (`repeat`). `libraryId` = AniList list-entry id. GraphQL
  enables efficient batch operations.
- **Kitsu** — `kitsu/Kitsu.kt`. OAuth2 password grant (email as username), JSON:API
  (`kitsu.app`). Internal 20-point `ratingTwenty` scale (displayed as 0–5 stars in half steps).
  Supports private tracking. Note: Kitsu the service is effectively deprecated/unmaintained — its
  Android app was removed from Google Play in April 2024, iOS availability is now unclear, and web
  development stalled in early 2025 (removal of Stripe integration from its codebase suggests the
  paid Patron tier has ended), per Achriom's "Kitsu vs MyAnimeList" review — though the integration
  remains coded.
- **MangaUpdates** — `mangaupdates/MangaUpdates.kt`. Not OAuth: username/password → session
  bearer token (`PUT /account/login`), API base `api.mangaupdates.com/v1`; re-login on expiry.
  Score 0.0–10.0 decimal. Uses reading lists mapped to the standard status enum.
- **Shikimori** — `shikimori/Shikimori.kt`. OAuth2 authorization-code against
  `shikimori.one`, access + refresh tokens. Score 0–10 integer. Uses `user_rates`. (Recent fix:
  outdated-domain bug; search now shows authors/descriptions.)
- **Bangumi** — `bangumi/Bangumi.kt`. OAuth2 authorization-code against `bgm.tv`, access +
  refresh tokens; migrated to Bangumi API v0 (token via header, not query string). Score 0–10
  integer. Status codes: 1=plan/wish, 2=completed/collect, 3=reading/do, 4=on-hold, 5=dropped. Now
  supports private tracking + start dates; retains volume data. Bangumi merges progress updates
  for the same book within a short window.

### 1.4 Matching local ↔ remote

Manual search binding is the default: open series → Tracking → Add tracking → search → select.
MAL `id:`/`my:` prefixes handle no-name-match cases. MangaDex sources auto-associate trackers
using tracker links in metadata (Komikku: "use tracker links to associate mangas automatically
with trackers"). `EnhancedTracker`s auto-bind by matching the manga's source, with an opt-in
"Auto-bind enhanced trackers" setting.

### 1.5 Sync direction & conflict resolution

Mihon: one-way Mihon→tracker. Triggers: reading the last page of a chapter, or (optional setting)
marking a chapter read. Status and start/finish dates auto-change on start/complete. Offline
changes sync when back online. On conflict, the app retains the higher last-read chapter (PR
"Retain remote last chapter read if it's higher than the local one for EnhancedTracker"). Komikku
adds an opt-in two-way sync (`SyncDataJob`); users continue to request smarter conflict
resolution.

### 1.6 Statistics / history

Mihon has a History tab (recent chapters, resume-reading) but no full reading-statistics
dashboard, and it stores only the most-recent chapter per series in the visible history. Feature
requests for full per-chapter history and stats remain open/declined. The `Track` entity already
carries `lastChapterRead`, `totalChapters`, `startDate`, `finishDate`, and `score`, which is
enough raw material for a statistics feature — a differentiation opportunity for Paperbunkr.

### 1.7 Token storage & security

`TrackPreferences.kt` stores per-service credentials/tokens (serialized OAuth token JSON,
username/password for password-grant services) via `PreferenceStore`, which wraps Android
SharedPreferences — **unencrypted at rest**. Per-service OkHttp `*Interceptor` classes attach the
bearer token and, on 401/expiry, use the refresh token to obtain a new token and persist it;
failed refresh logs the tracker out. Login state is observed reactively.

## Part 2 — Manga/manhwa detail UI vs Western comic apps

### 2.1 Detail-page layout

Files: `app/src/main/java/eu/kanade/presentation/manga/MangaScreen.kt` and
`app/src/main/java/eu/kanade/presentation/manga/components/MangaInfoHeader.kt` (Compose);
description rendering via `MarkdownRender.kt`. The header presents: large cover art (commonly with
a blurred/color-extracted backdrop), title with author/artist lines, an expandable synopsis
(truncated with expand/collapse), genre/tag chips, an ongoing/completed/hiatus status line, a
source badge, and action buttons (Add to library, Tracking, WebView). Komikku extends tag display
with `NamespaceTags.kt` (namespaced tags for e-hentai-style sources) and `LibraryBadges.kt`.

### 2.2 Chapter list UI

The chapter list dominates the screen (the header scrolls above one long list). Each item shows
chapter number + title, read/unread state (read chapters visually dimmed), a download-status
indicator, a bookmark icon, scanlator/group attribution, and the upload date; a "seen pages"
indicator can appear for partially read chapters. Sort (by source order / chapter number / upload
date) and filter (unread, downloaded, bookmarked) controls sit in the header. "Skip filtered
chapters" and missing-chapter indicators are supported.

### 2.3 Reading direction / format

Reading mode (LTR pager, RTL pager, vertical pager, or webtoon continuous scroll) is handled by
the reader's `Viewer` implementations (`L2RPagerViewer`, `R2LPagerViewer`, `VerticalPagerViewer`,
`WebtoonViewer`) and is a per-manga reader preference rather than a prominent detail-page badge —
RTL for manga, vertical/continuous for manhwa/webtoons, LTR for Western comics. This differs from
Paperbunkr's data model, where `ContentType` and `ReadingMode` are explicit separate axes; Mihon
effectively derives the default from source/user setting.

### 2.4 Visual identity vs Western apps

Manga apps lean on: cover-forward but chapter-list-dominant detail screens; Material 3 dynamic
color/dark themes; chip-based genre tags; dimming rather than checkmarks for read state; and dense
chapter rows with multiple small status icons. Western comic managers (ComicRack, Chunky)
emphasize cover-grid browsing of issues/files, page-thumbnail navigation, and file/metadata
(publisher, volume, issue number) over a scanlator/source + chapter-progress model.

### Reference screenshot notes (Komikku detail page, user-provided)

Concrete anchor for Stage 6 below, confirming and sharpening §2.1:

- **Blurred backdrop header**: cover art edge-blurred/tinted fills the top banner behind a sharp
  cover thumbnail overlaid on top — the "color-extracted backdrop" pattern from §2.1, seen in
  practice.
- **Icon-led metadata rows, not a label:value table**: person icon → status ("This is ON-GOING
  series"), brush icon → "Type: Manhwa", no-entry icon → source/scanlator ("Unknown •
  Mangafreak"). Scans faster than plain text rows; worth adopting directly in the Avalonia layout.
- **Action row as icon-over-label buttons, evenly distributed**: Add to library (heart), update
  frequency ("2 days", hourglass), tracker status ("1 tracker", checkmark = bound), WebView
  (globe). The "1 tracker" slot is the at-a-glance tracker-binding indicator — this is Stage 1/2's
  tracker data surfaced directly on the detail page.
- **Expandable synopsis with a chevron toggle** (matches §2.1), followed by tag chips in
  outlined-pill style: thin colored outline, transparent fill, rounded-full — a specific styling
  detail to replicate rather than a filled/solid chip.
- **Suggestions/recommendations row**: horizontal-scroll cover thumbnails below the tags. This is
  a Komikku-specific extra (source-driven "more like this"), not core Tachiyomi/Mihon — treat as
  optional/later scope, not part of the core tracker/detail-page port.
- **Floating "Resume" pill button**, bottom-right, persistent over the chapter list — avoids
  needing to scroll to the top to continue reading.

These are implementation-ready visual specifics for whenever the Avalonia manga detail view
(Stage 6) gets built; no change to the architectural recommendations below.

## Recommendations

- **Stage 1 — Data model (do first).** Add a `Track` EF Core entity related many-to-one to
  `Series` (a series can have multiple track links, one per service). Mirror the proven fields:
  `Id`, `SeriesId`, `TrackerId` (enum of services), `RemoteId`, `RemoteListEntryId` (nullable),
  `Title`, `LastChapterRead` (use `double`/`decimal`, not `int` — partial chapters),
  `TotalChapters`, `Status` (normalized enum `Reading/PlanToRead/Completed/OnHold/Dropped/
  Rereading`), `Score` (store normalized, e.g. 0–100 or 0–10 double), `ScoreFormat` (per-tracker/
  user), `StartDate`, `FinishDate`, `RemoteUrl`, `IsPrivate`. Keep `ContentType` and `ReadingMode`
  on `Series` as already done — cleaner than Mihon's derive-from-source approach.
- **Stage 2 — Service abstraction.** Define `ITracker` (Search, Bind, Update, Refresh,
  GetScoreList, DisplayScore, Login, Logout) with a `TrackerBase` for shared logic, plus optional
  `IEnhancedTracker`/`IDeletableTracker` marker interfaces. Implement one class per service.
  Because auth and score models diverge, encode per-service: auth type (OAuth2+PKCE for MAL;
  OAuth2 for AniList/Shikimori/Bangumi; OAuth2 password grant for Kitsu; session-token login for
  MangaUpdates), API base URL, and score scale/format. Model API responses as explicit DTOs
  (Mihon's PR #1103 lesson: DTOs beat ad-hoc parsing).
- **Stage 3 — Sync engine.** Start with one-way (Paperbunkr→tracker) on "mark chapter
  read"/"finished last page," auto-setting status + start/finish dates, with "keep the higher
  last-read chapter" conflict rule. Make the trigger configurable ("update after reading" vs "when
  marked read"). Defer two-way sync (Komikku-style) to a later phase; if built, add an explicit
  conflict-resolution prompt.
- **Stage 4 — Token security (improve on the source apps).** Do NOT copy the plaintext-
  SharedPreferences approach. Encrypt OAuth tokens at rest with Windows DPAPI (`ProtectedData`,
  per-user scope) or the OS credential vault; store only ciphertext in SQLite/config. Implement
  refresh-token rotation in an HTTP handler analogous to the `*Interceptor` classes.
- **Stage 5 — Statistics differentiation.** Since Mihon lacks a stats dashboard, build one from
  richer local history: chapters read over time, streaks, per-series completion %, score
  distributions. Persist a per-chapter read-history table (not just latest) to enable this.
- **Stage 6 — Manga detail view in Avalonia.** Build a manga-specific `DataTemplate`/view
  selected by `ContentType`: blurred-cover header, expandable synopsis, tag chips (`ItemsControl`
  + wrap panel, outlined pill style per the screenshot notes), ongoing/completed status, tracking
  panel with icon-led metadata rows, and a dominant chapter list with read-state dimming,
  download/bookmark icons, scanlator + date, and sort/filter. Add small RTL/vertical/webtoon
  badges driven by `ReadingMode` (an improvement over Mihon, which hides this on the detail page).
  Consider a floating "Resume" button over the chapter list. Keep the existing Western comic
  detail view for `Comic` content and switch via a template selector.

**Benchmarks that change the plan:** If only one service is ever integrated, collapse the
abstraction. If cross-device sync is a target, promote two-way sync to Stage 3. If tokens must
survive OS reinstall/backup, consider an encrypted export rather than DPAPI (which is
machine/user-bound).

## Caveats

- Exact field names, tracker integer ids, and per-tracker status codes should be confirmed against
  current source — the codebase migrated from snake_case DB fields to camelCase domain fields, and
  some names (`trackerId` vs `syncId`, `remoteId` vs `mediaId`) coexist across layers.
- **Service volatility:** MyAnimeList was acquired by Gaudiy Inc. (a Tokyo-based Web3/AI company
  operating the Gaudiy Fanlink platform); Gaudiy completed its buyout to become sole owner on May
  7, 2025 for roughly ¥531 million (~$3.5M USD), after Media Do sold its stake in late March 2025
  (per Anime News Network, 2025-04-01). As of mid-2026 no blockchain or NFT features have been
  deployed to MAL post-acquisition (Achriom), though MyAnimeList was permanently blocked in Russia
  by Roskomnadzor in October 2025. Kitsu's apps were removed from Google Play (April 2024) and its
  web development has stalled — prioritize AniList and MAL for integration and treat Kitsu as
  legacy.
- **APIs change:** Bangumi migrated to v0; Shikimori changed domains; MAL uses PKCE. Verify current
  auth/endpoint details against each service's live API docs before implementing.
- Mihon is Kotlin/Jetpack Compose; only the logic and UX patterns port to C#/Avalonia, not code.
- Some claims about exact internal class/method names rely on DeepWiki's AI-generated
  documentation and GitHub PR/issue snippets rather than direct file reads; treat file paths as
  strong leads to verify, not gospel.
