# Remote / Server Library Sharing — Implementation Plan

> **Status 2026-09-20: all phases implemented on `feat/remote-library-sharing`** (see the design doc §13 for deviations from this plan, notably the global query filter replacing per-site exclusions, and the follow-up list). Steps below are the original plan, kept as written.
*Implements: docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md*

Branch `feat/remote-library-sharing` (worktree `.claude/worktrees/remote-library-sharing`). Do **not**
run the built app from this worktree against the real profile (worktrees share the per-user dev DB,
`feedback_worktree_shares_user_db`) — every on-screen run uses `PAPERBUNKR_DATA_DIR`.

Naming note: `Services/RemoteCoverCache.cs` already exists (tracker/external-metadata provider
covers). Everything here uses the **"Peer"** prefix (`PeerCoverCache`, `PeerPageCache`) to avoid
confusion.

## Phase 0 — `PAPERBUNKR_DATA_DIR` (prerequisite) — DONE 2026-09-20

### Step 0.1: `AppDataPaths` helper + route every path through it — DONE
**Files:** `src/Paperbunkr.Data/AppDataPaths.cs` (new); edited `PaperbunkrDbContext.cs`,
`CoverThumbnailPaths`, `CustomCoverPaths`, `CustomBookCoverPaths`, `BookCoverThumbnailPaths`,
`ArcCoverPaths`, `CoverCacheState`, `BackupService`, `ThemePaths`, `PluginPaths` (2 dirs),
`DiagnosticsService`, `BootstrapSentinel`, `GraphicsBootstrap`, `UpdateService`, `RemoteCoverCache`,
`PdfPageReaderScreenViewModel` (annotations), `BookReaderScreen.axaml.cs` (WebView2 → `LocalRoot`).
**Deliberately left alone:** `MigrationViewModel.GetDefaultCePath` (reads *ComicRack's* folder, not
ours), `Paperbunkr.Engine/SystemPaths.cs` and `Common/Runtime/IniFile.cs` (dormant CE ports).
**Verify (done):** `dotnet build` App = 0 errors; `AppDataPathsTests` 5/5; App.Tests
path/backup/bootstrap/graphics/theme filter 135/135.

### Step 0.2: UI-test fixture isolation — DONE
**Files:** `Paperbunkr.App.UiTests/AppFixture.cs` — also sets `PAPERBUNKR_DATA_DIR` to a per-run temp
dir and deletes it on dispose (`PAPERBUNKR_DB_PATH` kept because tests read `DbPath`).
**Verify (done):** UiTests project builds. UI tests themselves not run (need a desktop session).

## Phase 1 — `Paperbunkr.Sharing` server core (headless)

### Step 1.1: Project scaffold + Kestrel size/R2R spike (**risk gate**)
**Files:** `src/Paperbunkr.Sharing/Paperbunkr.Sharing.csproj` (new, net10.0,
`<FrameworkReference Include="Microsoft.AspNetCore.App" />`), `src/Paperbunkr.Sharing.Tests/…csproj`
(new), `Paperbunkr.slnx`/`.sln` (edit), `Paperbunkr.App.csproj` (edit — ProjectReference).
**What:** empty projects wired into the solution; App references Sharing. Then measure: publish App
before/after, compare output size, and confirm the `PublishReadyToRun` + trim settings still work
(cross-check `project_paperbunkr_r2r_release_crash` — launch the published exe on this machine).
**Depends on:** none.
**Verify:** solution builds; recorded size delta + a launched published exe. **If the delta is
unacceptable, stop and take the spec's opt-in-component fallback to the user before Step 1.2.**

### Step 1.2: Protocol DTOs + version constant
**Files:** `Paperbunkr.Sharing/Protocol/{ProtocolVersion,HelloResponse,SessionRequest,SessionResponse,CatalogPage,CatalogIssueDto,CatalogSeriesDto,SharedListDto,PageInfoDto}.cs` (new).
**What:** DTOs per spec §5; `X-Paperbunkr-Protocol: 1`. **Catalog DTO field list is fixed here** by
reading `Issue`/`Series` columns (`src/Paperbunkr.Data/Entities/`) and listing exactly the display
fields; a reflection test forbids `FilePath`, `FileSize`, `FileModifiedTime`, `FileCreationTime`,
read-progress, rating, bookmark and note fields.
**Depends on:** 1.1.
**Verify:** `Paperbunkr.Sharing.Tests` — JSON round-trip; reflection test for local-only fields.

### Step 1.3: Password hashing, session tokens, rate limiter
**Files:** `Paperbunkr.Sharing/Server/{PasswordHasher,SessionStore,FailedAuthLimiter}.cs` (new).
**What:** PBKDF2 (`Rfc2898DeriveBytes`, per-install salt, high iteration count, constant-time
compare); 256-bit opaque tokens in memory with sliding expiry; per-remote-IP failed-attempt backoff.
**Depends on:** 1.1.
**Verify:** wrong password rejected; token expiry; sliding expiry; backoff after N failures and
recovery after the window; hash never equals/contains the plaintext.

### Step 1.4: TLS certificate manager
**Files:** `Paperbunkr.Sharing/Server/CertificateManager.cs` (new).
**What:** generate ECDSA P-256 self-signed cert (`CertificateRequest`), persist under
`AppDataPaths.Combine("sharing")` with the private key DPAPI-protected (current user), expose SHA-256
fingerprint; regenerate on demand.
**Depends on:** 1.1, Step 0.1.
**Verify:** generates once and reloads the same fingerprint; deleting the file regenerates a
different one; private key file is not readable as plain PKCS#8.

### Step 1.5: `IShareCatalogSource` + `ShareServer` endpoints
**Files:** `Paperbunkr.Sharing/Server/{IShareCatalogSource,ShareServer,ShareServerOptions}.cs` (new);
`Paperbunkr.Data/Sharing/DbShareCatalogSource.cs` (new — host read model over the EF context;
**excludes remote-mirror rows** so a host never re-shares what it mirrors).
**What:** Kestrel host with the §5 endpoints, `Authorization: Bearer` middleware (except `/v1/hello`),
`ETag`/`If-None-Match` on the catalog, page `Range`, `?w=` downscale (Skia/Avalonia-free — use
`System.Drawing`-free path: reuse `PageDecodeCore`'s decoder via an injected `IPageProvider` so
Sharing stays UI-free), generic error bodies, bind loopback/private only, refuse to start with no
password.
**Depends on:** 1.2–1.4.
**Verify:** in-process server + `HttpClient` tests: auth flow, 401s, 304, Range, sharing scope
(`All`/`Selected`), refuses to start without a password, exception text never reaches the client.

## Phase 2 — Client + mirror
### Step 2.1: `RemoteSource` entity + `RemoteSourceId`/`RemoteIssueId`/`RemoteSeriesId` + migration
**Files:** `Paperbunkr.Data/Entities/{RemoteSource,Issue,Series}.cs`, `PaperbunkrDbContext.cs`, new
migration + regenerated snapshot (mind the stale-Designer-snapshot bug and the migration
up-down-up antipattern in memory — no-op `Down()` for new nullable columns per recent fixes),
unique index on `(RemoteSourceId, RemoteIssueId)` and the series equivalent.
**Verify:** migration applies to a copy of a real dev DB (via `PAPERBUNKR_DATA_DIR`); index test.
### Step 2.2: `IssueQueries.Local()` predicate + §8 exclusions (**highest-risk step**)
**Files:** `Paperbunkr.Data/Queries/IssueQueries.cs` (new) + every §8 site (CoverThumbnailService
`BackfillAspectRatiosCore`/orphan sweep/verify, Library Health Empty Rows, DuplicateAlertHelper,
LibraryDeletionHelper, SeriesMergeHelper, SeriesReassignmentResolver, TrackerAutoSyncService,
Arc/Event/Continuity verification, content-type sweep, Stats/Insights totals).
**Verify:** one test per site proving a remote row is untouched; a guard test that fails when a new
`context.Issues` query site appears without `Local()` or an allowlist entry.
### Step 2.3: `ShareClient` + pinned-cert handler + mirror sync
**Files:** `Paperbunkr.Sharing/Client/{ShareClient,PinnedCertHandler,CatalogPuller}.cs`;
`Paperbunkr.Data/Sharing/RemoteMirrorSync.cs` (upsert by `(source, remoteId)`).
**Verify:** TOFU fingerprint capture, mismatch blocks, `ETag` no-op, upsert idempotence, paginated pull.
### Step 2.4: Library-screen source + Add-remote dialog + trust prompt
**Files:** new `AddRemoteLibraryView(.axaml/.axaml.cs)`+VM, Library toolbar/source filter + "remote"
badge (load `avalonia` skill + `avalonia-pro-max/review-checklist`; code-behind `.cs` in the same
step as each new `.axaml`; dispatcher-tick rule for row-remove buttons).
**Verify:** VM tests + on-screen two-instance check.

## Phase 3 — Reader + caches
### Step 3.1: `PeerPageCache` (bounded LRU: 2 GB quota, 30-day TTL) + `PeerCoverCache`
### Step 3.2: `RemotePageSource : IReaderPageSource` (bounded fetch queue N=3, cancellation disposes streams before decode) + branch at `ReaderScreenViewModel.cs:1135–1143`; guard `SavePageAsAsync` (`:529`) and the chapter-cover `CoverImageCache.Get` (`:2761`)
### Step 3.3: cover-path branch in `CoverThumbnailService.GetEffectiveCoverPath` → `PeerCoverCache`
**Verify:** cancel-mid-download test (no bitmap created, stream disposed); quota/LRU/TTL tests; end-to-end read of a remote issue in-process.

## Phase 4 — UI + Activity Center + discovery
Preferences → Libraries → Sharing; saved-servers list with re-trust (old/new fingerprints) and
**Relink** dialogs (spec §6/§7.1); Activity Center wiring (host: running indicator, clients, failed
-auth alert; client: catalog-sync job, offline / cert-changed alerts) — planned up front per
`feedback_use_activity_center_for_notifications`; TTL purge as a `ScheduledTaskCatalog` task;
mDNS discovery after a **library survey** (`feedback_survey_before_recommending_libraries`) with
CE's UDP broadcast on 7613 as the documented fallback. Relink reconciliation reuses `TitleNormalizer`.

## Phase 5 — Hardening
Rate-limit tuning, cert-changed UX polish, Public-network-profile warning, Offline lifecycle,
remove-source cleanup, wiki page (`wiki/`), `docs/paperbunkr-todo.md` + roadmap + `ce-feature-
inventory.md` §F status updates (by hand, with what was actually verified).

## Test strategy (project conventions)
xUnit; `Paperbunkr.Sharing.Tests` in-process server on an ephemeral loopback port (no live network);
Data tests use the existing in-memory/temp SQLite patterns; App VM tests follow
`PinnedThreadTestFramework`/`TestDispatcher.Drain()` (headless-flake resolution, 2026-09-19); on-screen
checks use two instances with distinct `PAPERBUNKR_DATA_DIR`s and are the user's call to run
(`feedback_no_unauthorized_ui_automation`).
