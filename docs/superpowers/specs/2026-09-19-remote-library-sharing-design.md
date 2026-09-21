# Remote / Server Library Sharing

**Date:** 2026-09-19
**Status:** Design confirmed by user; revised 2026-09-20 after external review (cert re-trust, Relink,
`PAPERBUNKR_DATA_DIR` prerequisite, cache bounds, mDNS pulled into P4); pending user re-review, then
`writing-plans`
**Source:** `docs/ce-feature-inventory.md` §F and `docs/Paperbunkr-Roadmap.md` "Remote/server library
sharing" — both mark it "decided: build, needs its own design spec". Reached via a `/grilling` pass
(27 questions over 4 rounds, all answered) per `CLAUDE.md`'s brainstorming-replacement rule. CE facts
below come from `_reference/ComicRackCE` (looked up by subagent, not assumed).

## 1. Goal and non-goals

Let one Paperbunkr instance (**host**) share part of its library, and another (**client**) browse it
and read it as if it were a source inside the client's own Library screen.

**In v1:** LAN/VPN use; read-only browse + stream-read of comics/manga; per-list sharing; password
auth over TLS with certificate pinning; manual host:port entry; Activity Center integration.

**Deliberately out of v1 (each a conscious deviation or deferral):**

| Item | CE behavior | v1 decision |
|---|---|---|
| Remote metadata edits | opt-in `ShareIsEditable`, pushed via `UpdateComic` | read-only, never written back |
| Export / download to local library | opt-in `ShareIsExportable` flag (no file-transfer call in the contract) | stretch item, not v1 |
| LAN auto-discovery | UDP broadcast, port 7613 | mDNS in P4 (manual host:port remains the baseline; see §5.1) |
| Host JPEG quality setting | per-share page/thumbnail recompression | originals + one downscaled variant instead |
| Multiple shares per instance | several shares on one port | single share |
| Books (EPUB/PDF) | n/a (CE is comics-only) | fast-follow — different reader stack |
| Headless / service host | n/a | later; server core is UI-free so it stays cheap |
| Public-internet exposure, NAT traversal | dead code in CE (`IsInternet` always false) | not supported |
| Interop with real ComicRack CE | WCF NetTcp | not supported (see §3) |

## 2. Context — what exists today

- `Paperbunkr.Engine` contains byte-for-byte ports of CE's `NetworkManager`, `ComicLibraryClient`,
  `ComicLibraryServer`, `RemoteComicBookProvider`, `IRemoteComicLibrary`, `IRemoteServerInfo`,
  `ServerRegistration` — all `Compile Remove`d in `Paperbunkr.Engine.csproj:25-35` because WCF has no
  drop-in .NET 10 equivalent. Zero references from `Paperbunkr.App`. Inert-but-compiled config types
  (`ComicLibraryServerConfig`, `ServerOptions`, `LibraryShareMode`, `ShareInformation`,
  `ShareableComicListItem`, `ComicIdListItem`) can be reused for vocabulary but not wire format.
- Reader seam: `ReaderScreenViewModel.cs:1135-1143` opens `ReaderImagePipeline.TryOpen(issue.FilePath)`
  only when `FilePath` is non-empty; `IReaderPageSource : IPageImageDecoder` is the interface a remote
  source implements. Decode already runs on background workers with cache + prefetch, so a network
  fetch inside `GetPage` is hidden the same way disk I/O is.
- Cover seam: `CoverThumbnailService.GetEffectiveCoverPath(issueId)` → `CoverImageCache` → grid.
- `Issue`/`Series` have only `int Id`; nothing is stable across machines (audit, 2026-09-19).
- Activity Center (`IActivityService`) is shipped for local jobs; remote/server jobs were explicitly
  deferred until this feature (`ce-feature-inventory.md:199`).

## 3. Approaches considered

**A. Embedded ASP.NET Core (Kestrel) HTTPS server + `HttpClient` client, in a new
`Paperbunkr.Sharing` project. — RECOMMENDED**
JSON for catalog/metadata, raw streamed bytes for pages/covers. Kestrel gives TLS with an in-memory
self-signed certificate (no Windows `netsh` cert binding), standard auth middleware, and rate
limiting. Debuggable with any HTTP tool; leaves the door open to OPDS-style clients later.
Cost: pulls the `Microsoft.AspNetCore.App` shared framework into the App's publish output (size
impact to measure in P1; `PublishReadyToRun`/trim implications must be checked against the known R2R
crash history in `project_paperbunkr_r2r_release_crash`).

**B. `HttpListener` hand-rolled server.** Smaller footprint, but HTTPS needs a machine-level cert
binding (`netsh http add sslcert`, admin rights) — unacceptable for a per-user desktop app — and
we'd hand-write routing, auth and rate limiting. Rejected.

**C. CoreWCF + `System.ServiceModel.NetTcp`, CE wire-compatible.** Only option that lets a real
ComicRack CE instance connect. Requires CE's embedded certificate (private key public), CE's
`CertificateValidationMode.None` client, WCF message security with a plaintext-equivalent password,
CE's compressed-XML whole-library dump, and a CE-schema mapper. Carries every CE security weakness
forward and is a large dependency for a niche interop nobody asked for. Rejected; revisit only as an
optional bridge if CE-interop becomes a stated goal.

## 4. Architecture

```
Paperbunkr.Sharing   (new, net10.0, no Avalonia; FrameworkReference Microsoft.AspNetCore.App)
  Protocol/   DTOs + protocol version constant
  Server/     ShareServer (Kestrel host), auth, rate limiter, catalog/page/cover endpoints,
              TLS cert manager
  Client/     ShareClient (HttpClient + pinned-cert handler), catalog puller, page/cover fetcher
Paperbunkr.Sharing.Tests   (new)
Paperbunkr.Data            + RemoteSource entity, RemoteSourceId on Issue/Series, migration
Paperbunkr.App             Preferences UI, Library-screen integration, RemotePageSource
                           (IReaderPageSource), remote cover cache, Activity Center wiring
```

`ShareServer` depends on an `IShareCatalogSource` (host-side read model over the DB) and never on
Avalonia, so a future headless host is a new entry point, not a rewrite.

## 5. Protocol

- **Transport:** HTTPS, Kestrel, user-chosen port (default 7614; avoids CE's 7612/7613 so a stray CE
  instance never confuses us). Bind interfaces: private/loopback only unless the user overrides;
  see §9.
- **Versioning:** every response carries `X-Paperbunkr-Protocol: 1`. A client that doesn't support
  the host's version refuses with a clear "update Paperbunkr on one side" message.
- **Endpoints** (all under `/v1`, all except `/v1/hello` require `Authorization: Bearer <token>`):

| Method + path | Purpose |
|---|---|
| `GET /v1/hello` | unauthenticated: protocol version, instance id (GUID), display name, `requiresPassword`. Nothing else leaks. |
| `POST /v1/session` | body `{password}` → `{token, expiresAt}`; rate-limited (§9) |
| `GET /v1/catalog?cursor=&pageSize=` | paginated issues+series DTOs for shared content; response `ETag` = catalog version stamp; supports `If-None-Match` → 304 |
| `GET /v1/lists` | shared lists (id, name, kind) — informational, for display |
| `GET /v1/issues/{id}/pages` | page count + per-page dimensions/type (drives reader layout without fetching pages) |
| `GET /v1/issues/{id}/pages/{n}` | original page bytes, `Content-Type` from the archive entry; `Range` supported |
| `GET /v1/issues/{id}/pages/{n}?w=` | server-side downscaled JPEG (width param, clamped) — thumbnails |
| `GET /v1/issues/{id}/cover?w=` | cover, downscaled by `w` |

- **Catalog DTO (Q18):** the fields the Library/Detail/search views display — series, number,
  title, creators, genre, tags (with category/weight), year, format, page count, summary, story arc,
  etc. **Never sent:** `FilePath`, file size/mtime, read progress, ratings, bookmarks, personal
  notes, anything from `Issue` that is local-only state. Exact field list is fixed in the
  implementation plan against the actual `Issue`/`Series` columns.
- **Sharing scope:** `LibraryShareMode` reuses CE's vocabulary — `All` or `Selected` (Reading Lists,
  Collections, Smart Lists, evaluated host-side). Series in the catalog are derived from the issues
  in scope. Nothing shared until the host picks (`None` is the default).

### 5.1 Discovery (P4)

Manual host:port stays the baseline and always works (VPNs and routed networks often block
multicast). On top of it, a running host advertises itself via **mDNS/DNS-SD** (`_paperbunkr._tcp`,
TXT record carrying instance id, display name, protocol version, `requiresPassword`), and "Add remote
library" lists discovered hosts. Discovery only *finds* a host: the certificate trust prompt and
password step are unchanged, and advertising is off whenever sharing is off. Open items for the
implementation plan: choose the implementation by surveying real libraries first (per
`feedback_survey_before_recommending_libraries`; CE itself used a UDP broadcast on 7613, which is the
fallback if no mDNS library is acceptable on net10), and confirm the Windows Firewall multicast
prompt/wording.

## 6. Security (Q10, Q14)

CE's model, for the record: one shared password per share, a fixed username, WCF message encryption
using a certificate whose private key ships in the binary, client `CertificateValidationMode.None`,
no rate limiting, exception details leaked in faults, unauthenticated `/Info`. We keep the *shape*
(one host password, per-share scope) and fix the substance:

- **Password:** host sets it in Preferences. Stored as PBKDF2 (`Rfc2898DeriveBytes`, per-install
  salt, high iteration count) — never plaintext. Server refuses to start with no password.
- **Session:** `POST /v1/session` verifies the password (constant-time compare) and returns a random
  256-bit opaque token, held in host memory only, sliding expiry. Server restart invalidates all
  tokens.
- **TLS:** on first enable the host generates a self-signed cert (`CertificateRequest`, ECDSA P-256)
  and stores it (private key protected with DPAPI, current-user scope). Kestrel serves it from memory.
- **Client pinning (TOFU):** first connect shows the certificate SHA-256 fingerprint and asks the user
  to trust it (same UX idea as SSH). The fingerprint is stored with the saved server. A later
  mismatch **blocks the connection** and raises a "host certificate changed" alert — never a silent
  re-trust — but is recoverable: the alert opens a re-trust dialog showing the **old and new
  fingerprints** with a plain warning ("only continue if the host's owner regenerated it, e.g. after
  a reinstall"). An explicit confirm overwrites `RemoteSource.CertFingerprint` in place, so the
  mirror rows and local reading data are untouched; the user never has to delete and re-add the
  library. Nothing is fetched from the host until the user confirms.
- **Rate limiting:** per-remote-IP failed-auth backoff on `/v1/session`; repeated failure raises an
  Activity Center alert on the host.
- **Errors:** generic messages to clients, details only in the host's own log.
- **Exposure:** default off; "Share this library" switch in Preferences → Libraries; explicit port;
  warns if the active network profile is Public; binds beyond loopback/private only with a password
  set.
- **Threat model stated plainly:** protects a household/VPN LAN against passive sniffing, casual
  access, and rogue-host impersonation after first trust. It is *not* hardened for the public
  internet (no account system, no per-user revocation, single shared password).

## 7. Client integration

### 7.1 Data model (Q8, Q19, Q25)
- New `RemoteSource` entity: `Id`, `InstanceId` (GUID from `/v1/hello`), `DisplayName`, `Host`,
  `Port`, `CertFingerprint`, `LastCatalogEtag`, `LastSyncedAt`, `IsOffline`.
- `Issue.RemoteSourceId` and `Series.RemoteSourceId` (nullable FK), plus `RemoteIssueId` /
  `RemoteSeriesId` (the host's int Ids), with a **unique index on `(RemoteSourceId, RemoteIssueId)`**
  and the series equivalent. Mirror rows always keep **`FilePath = null`** — the existing
  `FilePath != null` filters are the safety net for most local jobs.
- **Identity:** each instance mints a GUID once, stored in settings; the mirror is keyed by
  `(instance id, remote int Id)`. No per-row GUID migration on Issue/Series.
- **Instance id changes (host library rebuilt/reset):** the client **never** silently re-points rows
  at different books, and never offers deletion as the only option, because the mirror rows carry
  client-local reading progress, bookmarks and ratings that would be destroyed. Instead the source
  is flagged "Host changed" and the user chooses:
  - **Relink** (saved-servers UI): bind the new instance id to the *existing* `RemoteSource`, then a
    reconciliation pass re-keys `RemoteIssueId`/`RemoteSeriesId` by matching remote items to existing
    mirror rows on stable metadata (normalized series title + issue number + year/volume, reusing the
    `TitleNormalizer` cascade). Matched rows keep all local data; unmatched old rows are kept
    (Offline, unreadable) and reported in a summary so nothing vanishes silently; unmatched new items
    are added.
  - **Add as new source**, leaving the old one untouched, or **Remove** (see below).
- **Lifecycle:** items persist while offline, shown with an "Offline" badge; cached pages stay
  readable. "Remove remote library" deletes mirror rows + page/cover cache in one operation, and its
  confirm dialog states that local reading progress/bookmarks/ratings for that library are deleted
  with it. Refresh runs on connect and via a manual button; an unchanged `ETag` makes it near-free.
- `IsPlaceholder` is **not** reused (it means "reading-list stub").

### 7.2 Library screen
Remote items appear as a read-only source inside the existing Library screen (badge + source
filter), so sort/group/search/smart-list evaluation work unchanged. Edit affordances are disabled
on remote rows; the Detail screen renders normally.

### 7.3 Reader
`RemotePageSource : IReaderPageSource` fetches page bytes via `ShareClient`, backed by a bounded
on-disk cache, and plugs into the existing `ReaderImagePipeline` cache/prefetch behavior. Selection
is a branch on `issue.RemoteSourceId` before `ReaderScreenViewModel.cs:1143`. Other `FilePath`-only
spots handled: `SavePageAsAsync` (`:529`) and the chapter-transition `CoverImageCache.Get` (`:2761`).
Reading progress, bookmarks and ratings for remote issues are **client-local only**.

**Bounds (memory and disk).** Decoded-bitmap memory is already hard-bounded by
`ReaderImagePipeline`'s own budget and adaptive fringe; `RemotePageSource` must not add a second,
unbounded layer beneath it:
- **Bounded fetch queue:** at most N concurrent page fetches (default 3) and a prefetch depth capped
  to the pipeline's fringe; a fetch for a page outside the current virtualization window is dropped
  rather than queued.
- **Cancellation:** every fetch takes the pipeline's `CancellationToken`. When the user flips past a
  loading page, the request is cancelled and its response stream/buffer is disposed *before* any
  bytes reach the decoder — a cancelled fetch must never produce a decode. Covered by a test that
  cancels mid-download and asserts no bitmap is created and the stream is disposed.
- **On-disk page cache:** LRU with a byte quota (default 2 GB across all remote sources,
  configurable in Preferences) **and** a 30-day last-access TTL. Eviction runs on write (quota) and
  via a scheduled task (TTL) registered in `ScheduledTaskCatalog`, reporting through the Activity
  Center. Originals are cached as received (no re-encode); the quota accounts for the largest
  single page so one oversized page can't evict everything. The remote cover cache is separate and
  small, with its own TTL.

### 7.4 Covers
Remote covers live in their own cache directory (not `CoverThumbnailPaths.GetCachePath(issueId)`),
because `CollectOrphans`/`RunCacheMaintenance` (`CoverThumbnailService.cs:237-291`) would treat a
remote cover keyed by local id as an orphan and move it to the attic. One branch in the effective
cover-path lookup routes remote rows to that cache; the fetcher requests `?w=` sized covers.

## 8. Remote rows in cross-cutting jobs (Q26)

Audit result (2026-09-19), applied as policy.

**Excluded** (add an explicit `RemoteSourceId == null` filter or guard):
- `CoverThumbnailService.BackfillAspectRatiosCore` (`:308`, no `FilePath` filter today) and the cover
  orphan sweep/verify paths.
- Library Health "Empty Rows" (`PreferencesScreenViewModel.cs:3283,3294`) — a remote series with no
  mirrored issues must not be listed.
- `DuplicateAlertHelper` (`:25`), `LibraryDeletionHelper` (`:40,78`), `SeriesMergeHelper`,
  `SeriesReassignmentResolver`.
- All writes: `MetadataEditHistoryService`, bulk editors, write-back (already guarded by
  `FilePath`/`IsPlaceholder`).
- All network jobs: `TrackerAutoSyncService` (`:206,239,380`), `ArcExternalVerificationService`,
  `EventSuggestionResolver`, `ContinuityResolver`, `RunContentTypeSweepCore` (`:555`).
- Missing-file auto-remove (`PreferencesScreenViewModel.cs:2043,3710,3738`): safe only while remote
  rows never set `FileIsMissing` — enforced by a test.

**Included:** grid, search, smart lists, Home, Detail.
**Insights:** *reading events* count (you did read it); library-size and unread totals exclude
remote (`StatsResolver.cs:20`, `InsightsResolver.cs:40` get the filter).
**Reading lists:** an item links to a remote issue only by explicit user choice, never by the
automatic `ReadingListMatcher`.
**UI-level null-path guards:** `RevealInExplorerHelper`, `QuickOpenService`.

A single shared predicate (e.g. `IssueQueries.Local()` extension) is the implementation approach so
new jobs inherit the exclusion rather than each remembering it.

## 9. UI and Activity Center

- **Preferences → Libraries → "Sharing"** (host): enable switch, port, display name, password
  (set/change), share scope (All / Selected lists), certificate fingerprint display, connected
  clients list, network-profile warning. Follows the Phase-3 library-folder-management redesign
  language; the `avalonia` skill + `avalonia-pro-max/review-checklist` are mandatory at implementation
  time per `CLAUDE.md`.
- **Client:** "Add remote library" dialog (host, port, password) → fingerprint trust prompt →
  catalog sync. Saved-servers list with Connect/Refresh/Remove; per-source Offline badge.
- **Activity Center** (per the standing "notifications go through it" rule): host — server
  running/stopped indicator, connected clients in the peek popover, failed-auth alert; client — a
  "Syncing remote catalog" job, offline/cert-changed alerts. No ad-hoc toasts.
- Dispatcher-tick rule (`CLAUDE.md` runtime gotcha) applies to any remove/close button inside the
  saved-servers list or trust popup.

## 10. Testing and verification

- **`Paperbunkr.Sharing.Tests`:** protocol round-trips; auth (wrong password, token expiry, rate
  limit backoff); pinned-cert handler rejects a changed certificate; `If-None-Match` 304; page `Range`;
  sharing-scope evaluation (`All`/`Selected`) returns only in-scope issues; catalog DTO never contains
  local-only fields (reflection test over the DTO).
- **`Paperbunkr.Data.Tests`:** migration applies on a copy of a real dev DB; unique index; mirror
  upsert/removal; an exclusion test per §8 site (a remote row is not touched by each job) — this is
  the highest-value test set because a miss silently corrupts or deletes data.
- **App tests:** `RemotePageSource` against an in-process `ShareServer`; reader branch selection.
- **On-screen (two instances on one machine):** the second instance runs with
  **`PAPERBUNKR_DATA_DIR`** pointing at a scratch directory. This is a **prerequisite delivered in
  P0** (§11): a single base-path helper that every app-data path routes through. Today
  `PAPERBUNKR_DB_PATH` isolates only the DB, while ~12 other paths hardcode
  `%APPDATA%\Paperbunkr\` (`CoverThumbnailPaths`, `CustomCoverPaths`, `CustomBookCoverPaths`,
  `BookCoverThumbnailPaths`, `ArcCoverPaths`, `CoverCacheState`, `BackupService`, `ThemePaths`,
  `PluginPaths`, `DiagnosticsService`, `BootstrapSentinel`, `GraphicsBootstrap`, `UpdateService`,
  `PdfPageReaderScreenViewModel` annotations) and cover cache keys are `{issueId}.jpg`, so two
  instances sharing `%APPDATA%` would collide. Never point a second instance at the shared dev DB
  (`feedback_worktree_shares_user_db`). The `UiTests` `AppFixture` should adopt the same variable in
  place of `PAPERBUNKR_DB_PATH`.

## 11. Phasing

| Phase | Scope | Done when |
|---|---|---|
| **P0** (prerequisite) | `PAPERBUNKR_DATA_DIR`: one base-path helper; route every hardcoded `%APPDATA%\Paperbunkr\` path (list in §10) through it; `PAPERBUNKR_DB_PATH` keeps working as a narrower override; migrate `AppFixture`. Independent of sharing and shippable on its own. | Two app instances with different data dirs run side by side with zero shared files; existing single-instance behavior unchanged when the variable is unset |
| **P1** | `Paperbunkr.Sharing` protocol, `ShareServer`, auth, TLS cert manager, catalog/page/cover endpoints, `IShareCatalogSource`; headless, fully unit-tested. Measure Kestrel's publish-size and R2R impact. | Sharing tests green; a scripted client can auth, page the catalog, fetch a page and cover |
| **P2** | `ShareClient`, `RemoteSource` entity + migration, mirror sync, §8 exclusion predicate + tests, Library-screen source/badge, add-remote dialog + fingerprint trust | A second instance's library appears in the Library screen; no local job touches it |
| **P3** | `RemotePageSource`, on-disk page cache, remote cover cache, reader branch, `SavePageAs` + chapter-cover guards | Read a remote issue end to end, offline-cached pages still open |
| **P4** | Preferences host UI, saved-servers UI (incl. re-trust and Relink dialogs), Activity Center wiring, **mDNS discovery** (§5.1), page-cache TTL task | All notifications go through Activity Center; a host on the LAN appears in "Add remote library" without typing an address |
| **P5** | Hardening: rate-limit tuning, cert-changed UX, network-profile warning, Offline lifecycle, remove-source cleanup | Threat-model checklist in §6 verified |

## 12. Risks and open items

- **Kestrel in a desktop App** — publish size, trimming, and the R2R history
  (`project_paperbunkr_r2r_release_crash`) must be measured in P1 before committing to it for
  release builds. Fallback within Approach A: ship the server as an opt-in component if size is
  unacceptable.
- **Exclusion coverage** — §8's list comes from a grep-based audit; the plan should add a
  repository-wide check (new `Issues` query sites) so the list can't rot.
- **Catalog size** — 2000+ book libraries: paginated pull plus mirror upsert must not block the UI
  thread; report progress via Activity Center.
- **Relink reconciliation is fuzzy** — matching on normalized title + number + year can mismatch
  (reboots, reissues); the summary of unmatched rows is the safety valve, and reconciliation must
  never overwrite local data on an ambiguous match (leave both, flag for the user).
- **Windows Firewall** — first bind triggers the OS prompt; wording/guidance needs a small UX pass.
- **CE verification note (`CLAUDE.md` standing rule):** all CE behaviors cited here were read from
  `_reference/ComicRackCE`; every deviation is tabulated in §1 with its reason.

## 13. As built (2026-09-20) - where the implementation differs from the text above

Everything in §§1-12 was implemented except the items under "Not done". Decisions made during implementation that the text above does not
reflect, each with its reason:

1. **§8 exclusion strategy inverted.** The audit found ~260 `Issues`/`Series` query sites across ~70 files; patching a filter into each is exactly
   how a miss silently corrupts data. Instead `Issue` and `Series` carry a **global EF query filter** (`IncludeRemote || RemoteSourceId == null`), so
   every existing job excludes remote rows by default and new jobs inherit that. `PaperbunkrDb.CreateContext(includeRemote: true)` opts in - used only by
   the Library load, the two Detail screens' load/selection/mark-read contexts, the reader (all its writes are client-local state), the mirror sync and
   the cover fetcher. `RemoteRowIsolationTests.OptInSites_AreExplicit_AndMatchTheAllowlist` fails when a new opt-in site appears. Consequence: remote
   metadata is **read-only by construction** (edit and scrape paths run in default contexts that cannot see remote rows) rather than by hiding
   affordances.
2. **Host settings are a JSON file, not database columns** (`{data}/sharing/settings.json`, holding the PBKDF2 hash and the instance GUID) - no
   migration, and it follows `PAPERBUNKR_DATA_DIR`. Only the client-side `RemoteSource` table is in the database.
3. **Client-saved password**: DPAPI-protected (current user) in `RemoteSource.ProtectedPassword`; "don't remember" keeps it in memory only. The spec did
   not say how the client stores it.
4. **Reader integration**: not a new `IReaderPageSource` but a network-backed `ImageProvider` + `IComicAccessorSession` handed to the existing
   `ReaderImagePipeline`, so decode/cache/prefetch/strip-band behavior is unchanged. `LoadIssue` never waits on the network (page count comes from
   the mirror row).
5. **Host "sharing on" is a long-lived Activity Center job** (cancel it and the server stops); `RegisterUpkeep` is a single shared row and could not carry it.
6. **mDNS library**: `Makaretu.Dns.Multicast.New` 0.38.0 (MIT, net8/net9, released Nov 2024). Zeroconf browses only, Tmds.MDns is Linux-only. Discovery
   only pre-fills the Add dialog; the fingerprint prompt and password still follow.
7. **Relink** re-keys existing rows in two phases (release all keys, then assign unambiguous matches) so the unique index never trips; orphans keep null
   remote ids and are invisible to later syncs.
8. **New cache location**: remote covers live in `{data}/remote-library/covers/{localIssueId}.jpg`, resolved centrally in `CoverImageCache.ResolveFile` and
   `CoverThumbnailService.GetEffectiveCoverPath`; pages in `{data}/remote-library/pages` (`PeerPageCache.Shared`).
9. **Migration `Down()` is a deliberate no-op** (project convention: a real drop rebuilds `Issues`/`Series` and breaks earlier `Down()` steps).

**Bugs found and fixed while building** (each has a regression test): the `Task<IResult>` method-group/lambda binding to `RequestDelegate` that answered 200 with an
empty body; `Bitmap.Save(stream, quality)` encoding PNG under a JPEG header; an eviction race that could read from a closed archive; a page-cache byte total
double-counted on first write; `ImageProvider`'s file-existence check silently leaving remote providers with zero pages; a stale relink error surviving a
successful relink; a `CreateContext` optional parameter breaking ~10 method-group conversions; and - found only by the full-suite run, where it showed as two
order-dependent failures - **stale remote pages served from the reader pipeline's process-wide `SharedRawCache`**, which keys raw page bytes by container +
a file mtime/size stamp: a remote book has no file, so its stamp was a constant `0` and one open's bytes were served to every later open of the same
`remote://source/issue` (stale after the host replaced the book, or after a Relink re-keyed the id). Fixed by a per-open unique stamp
(`ReaderImagePipeline.OpenProvider`), by purging a source's cached pages on Relink, and by purging an issue's pages when a sync sees its page count change
(`MirrorSyncResult.ChangedContent`, `RemoteLibraryService.onContentInvalidated`).

**Follow-ups closed (2026-09-21)**
- **Stale pages/covers**: `CatalogIssueDto.ContentStamp` (opaque hash of size|mtime|page count) is stored as `Issue.RemoteContentStamp` (folded into the unreleased `AddRemoteSources`
  migration); a changed stamp purges that book's cached pages *and* cover (`MainViewModel` handler, `PeerCoverPaths.Delete` + `CoverImageCache.Invalidate`); Relink purges the whole source.
- **Insights/Stats**: reading activity (lifetime, ratings, highlights, Continue/Almost done/Dive in) includes remote books via `IgnoreQueryFilters`; library-size figures and Gaps stay local-only.
  `RemoteReadingStatsTests` pins both halves.
- **Read-only gating**: Library commands that open an editor / scrape / organize / delete refuse remote ids with a toast (mixed selections drop the remote part); remote rows get a reduced
  context menu (Open, Mark Read/Unread, Go to Series, Select); Mark Read/Unread still works (progress is client-local). Detail screens hide Edit / Change Cover for remote series.
  `Find()` honoring the filter is asserted strictly in `RemoteRowIsolationTests`. Not gated: Detail tabs (tracker linking, external metadata) - they act on rows a default context cannot load.
- **Library source filter + Remote pill**: "Library" section in the Filter popup (All / This computer / each remote library; session-only, not saved in layouts/workspaces) with a chip, and a
  "Remote" pill on the four poster tile templates (tooltip names the library).

**Not done / follow-ups**
- On-screen verification with two real instances (the second with its own `PAPERBUNKR_DATA_DIR`), incl. the avalonia review-checklist pass (contrast, light/dark, keyboard, narrow widths) on the new pill/filter.
- Books (EPUB/PDF) sharing; "Copy to my library"; a headless host; Windows Firewall prompt wording.
- Personal rating (QuickRate) on remote books is refused for now.
