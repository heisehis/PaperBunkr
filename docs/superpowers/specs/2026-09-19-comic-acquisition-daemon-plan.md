# Comic Acquisition Daemon — Slice 1 Implementation Plan
*Implements: docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md (slice 1 only; slices 2-4 get their own plans)*

Branch `feat/comic-acquisition-daemon` (worktree `.claude/worktrees/comic-acquisition-daemon`). The
pre-commit version bump only fires on `master`, so commits here don't bump. **Worktrees share the
per-user dev DB:** run tests only; do not launch the app or run `dotnet ef` from here.

## Survey findings that correct the spec
- **No generic host / `IServiceProvider`.** `MainViewModel` hand-builds services (`ActivityService`,
  `SchedulerService`) and `App.axaml.cs` calls `Scheduler.Start()`/`Stop()`. `AcquisitionService`
  will be a `BackgroundService` (package `Microsoft.Extensions.Hosting.Abstractions`) that `App`
  starts/stops manually the same way. No host is introduced.
- **Shared ComicVine code lives in `Paperbunkr.Data`, not `Daemon`.** `Daemon` references `Data`, and
  `ComicVineSource` (in `Data`) must use the same handler, so the handler/client/models go under
  `Paperbunkr.Data/ComicVine/`. (`Daemon` keeps Prowlarr, qBittorrent, loop, import.)
- **Only `ComicVineSource` makes ComicVine HTTP calls** (static `HttpClient`, per-instance throttle).
  Endpoints today: `/story_arcs/`, `/story_arc/4045-{id}/`, `/issues/`. No volume/search yet.
- **Tests:** `Data.Tests` uses temp-file SQLite (`Path.GetTempPath()`, `EnsureCreated`, `ClearAllPools`
  in `Dispose`), not in-memory. `TestDispatcher.Drain()` does not exist; App tests use plain `[Fact]`
  plus a per-class `PumpDispatcher()` (`Dispatcher.UIThread.RunJobs()`). The spec's mention is fixed.
- **Migrations are hand-written and additive.** The model snapshot has drifted (tracker columns the
  entities lack), so a scaffolded migration would be destructive. Latest:
  `20260919105009_AddSmoothScrolling`. New enums stored as strings by convention; new
  `CredentialKind` members are appended only.
- **Screens are string keys**, not an enum (`"insights"` etc.); adding one touches ~10 places in
  `MainViewModel.cs` plus `MainWindow.axaml`. **Preferences is a left-nav section list**
  (`PreferencesSection` enum), not a tile hub; ComicVine/Metron keys are edited in the Connections
  section. The acquisition setup becomes a new `Acquisition` section.
- **Series Detail issue list is `DetailTabsViewModel`** (not `DetailBandViewModel`); no Missing section
  exists and placeholders render as ordinary cards.
- **`ReadingListMatcher` matches series by name cascade only**, never by ID, and creates a `Series`
  itself when none matches. The new `WatchedSeries` link must reuse `FindSeriesByCascade`, not add a
  parallel lookup.

## Step 1: CredentialStore DPAPI encryption  — DONE (0d12d19)
**Files:** `src/Paperbunkr.Data/Credentials/CredentialStore.cs` (edit), `Paperbunkr.Data.csproj`
(add `System.Security.Cryptography.ProtectedData`), `src/Paperbunkr.Data.Tests/CredentialStoreTests.cs`
(edit/add)
**What:** `Set` writes `dpapi1:` + base64(`ProtectedData.Protect`, `CurrentUser`); `Get` decrypts
prefixed values, returns un-prefixed values as legacy plain text and re-saves them encrypted
(lazy migration, so ComicVine/Metron/tracker rows upgrade on first read). A value that fails to
decrypt (other user/machine) returns `null` so callers show "not connected" instead of throwing.
`HasCredentials`/`Delete` unchanged in behavior. Public signatures unchanged, so the ~15 existing
callers are untouched.
**Depends on:** none
**Verify:** `Data.Tests`: round-trip; stored column value is not the plaintext; legacy plaintext row
is read and rewritten encrypted; corrupt ciphertext returns null; existing `CredentialStoreTests`
and tracker adapter tests still pass.

## Step 2: Prioritized ComicVine rate-limit handler  — DONE (d0f27f2)
**Files:** `src/Paperbunkr.Data/ComicVine/ComicVineRateLimitHandler.cs` (new),
`ComicVineRequestPriority.cs` (new), `ComicVineHttp.cs` (new: shared handler singleton +
`HttpClient` factory), `ReadingLists/Sources/ComicVineSource.cs` (edit: use the shared client, drop
its private throttle), `src/Paperbunkr.Data.Tests/ComicVine/ComicVineRateLimitHandlerTests.cs` (new)
**What:** `DelegatingHandler` with a priority carried in `HttpRequestMessage.Options`
(default **High**, so every existing/UI caller is foreground; the daemon marks its calls **Low**).
Min 1 s spacing across all callers; sliding 1-hour window capped at 200; **Low** may only use the
first 150 of the window, so 50 stay reserved for High; High waiters are served before Low. A response
with HTTP 420/429 or ComicVine `status_code` 107 (rate limit) pauses **all** calls until a cool-off.
Injectable clock + delay for tests. Verify the ComicVine 107 status meaning against its docs while
implementing (do not rely on memory).
**Depends on:** none (independent of Step 1)
**Verify:** `Data.Tests` with a fake inner handler and fake clock: spacing enforced; Low blocked at
150 while High proceeds; High served before queued Low; 429/107 pauses both; existing
`ComicVineSource` tests still pass.

## Step 3: Daemon project scaffold and contracts  — DONE (feat(daemon) scaffold commit)
**Files:** `src/Paperbunkr.Daemon/Paperbunkr.Daemon.csproj` (new, net10.0, refs `Data`+`Common`,
`Microsoft.Extensions.Hosting.Abstractions`, **no Avalonia**), `Contracts/IIndexerClient.cs`,
`IComicVineClient.cs`, `IEventPublisher.cs`, `Events/DaemonEvent.cs`,
`Events/ChannelEventPublisher.cs` (all new), `src/Paperbunkr.Daemon.Tests/` (new xunit project),
`Paperbunkr.sln` (edit)
**What:** interfaces from the spec (`IDownloadClient` is deferred to slice 2), `DaemonEvent` records,
and a `Channel<DaemonEvent>` publisher.
**Depends on:** none
**Verify:** solution builds; a Daemon.Tests test asserts `Paperbunkr.Daemon` references no
`Avalonia*` assembly; publisher delivers events in order.

## Step 4: Entities and migration  — DONE (270f72c; EF-scaffolded then verified additive: 5 tables, 9 indexes, snapshot diff was insert-only, so no hand-writing was needed on this base)
**Files:** `src/Paperbunkr.Data/Entities/WatchedSeries.cs`, `WantedIssue.cs`, `WantedIssueStatus.cs`,
`ReleaseCandidate.cs`, `AcquisitionSettings.cs` (all new), `PaperbunkrDbContext.cs` (edit: DbSets +
config), `Migrations/<timestamp>_AddAcquisition.cs` + `.Designer.cs` (hand-written, additive) and
snapshot edit, `Data.Tests/AcquisitionEntityTests.cs` (new)
**What:** tables per spec section 4 (blocklist table waits for slice 2). Status stored as string.
Follows the repo's migration lessons: additive `Up`, no-op-safe `Down`, no up-down-up test, keep the
Designer/snapshot in sync without dropping the drifted tracker columns.
**Depends on:** none
**Verify:** `EnsureCreated` round-trip tests; a `Migrate()` test against a temp DB **only if** the
existing migration tests can run (memory notes some `Migrate()` tests already fail at base; do not
add to the failure set, and record baseline pass/fail counts before and after).

## Step 5: ComicVine client (volumes, issues, store dates)  — DONE
**Files:** `src/Paperbunkr.Data/ComicVine/ComicVineClient.cs`, `ComicVineModels.cs` (new),
`Data.Tests/ComicVine/ComicVineClientTests.cs` (new, fake handler with JSON fixtures)
**What:** search volumes, get volume, page a volume's issues (100/page). Verify field names
(`store_date`, `cover_date`, ids) against the real API docs, not memory. Goes through the shared
handler; daemon callers pass Low.
**Depends on:** Step 2
**Verify:** fixture-based parsing tests; pagination; error mapping.

## Step 6: Prowlarr client, query cascade, release filter/scoring  — DONE (native JSON search, not Torznab - see spec section 2)
**Files:** `src/Paperbunkr.Daemon/Indexers/ProwlarrSearchClient.cs`, `QueryBuilder.cs`,
`ReleaseEvaluator.cs`, `ReleaseSearcher.cs`, `PackDetector.cs` (new); tests in `Daemon.Tests/Indexers/`
**What:** Torznab XML parse; strict-then-sanitized query cascade with number variants
(`5`,`05`,`005`, volume, year); scoring (seeders, size limits, release group, small CBZ bonus/CBR
penalty, configurable weights); title/issue/year verification; range/pack flagging. No blocklist yet.
**Depends on:** Step 3
**Verify:** unit tests incl. "X-Men"/"+Anima" strict query first, fallback only on zero results,
pack titles flagged; fake HTTP server for the client.

## Step 7: Wanted domain service (missing, request, watch, owned-check, arc request)  — DONE
**Files:** `src/Paperbunkr.Data/Acquisition/WantedService.cs`, `OwnedIssueMatcher.cs`,
`ArcRequestService.cs` (new); tests in `Data.Tests/Acquisition/`
**What:** compute Missing (ComicVine volume issues minus owned by ComicVine id, then
`ReadingListMatcher` series+number); Request / Watch toggle / "I have this" ignore; arc "Request
missing" incl. creating the local `Series` (via `FindSeriesByCascade`) and a `WatchedSeries` with
`WatchFutureReleases=false`, and per-item Request on non-arc lists.
**Depends on:** Steps 4, 5
**Verify:** temp-DB tests: owned issues never requested; arc request on unknown series creates
`Series` + non-following `WatchedSeries`; duplicate requests are idempotent.

## Step 8: AcquisitionService loop (slice-1 form) and App wiring  — DONE
**Files:** `src/Paperbunkr.Daemon/Services/AcquisitionService.cs` (new),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit: construct, `AttachEvents`),
`src/Paperbunkr.App/App.axaml.cs` (edit: start/stop like `Scheduler`),
`src/Paperbunkr.App/Services/AcquisitionActivityBridge.cs` (new: `Channel` -> `ActivityService` jobs/
alerts), tests in `Daemon.Tests` and `App.Tests`
**What:** `PeriodicTimer` loop: refresh watched volumes (Low priority), promote due `Wanted`, search
Prowlarr, store `ReleaseCandidate`s, publish events. **Never contacts qBittorrent in slice 1.** Pauses
with an Activity Center alert and backoff when Prowlarr is unreachable. Own `DbContext` per tick.
**Depends on:** Steps 3, 6, 7
**Verify:** loop tests with fake indexer/ComicVine and a manual tick method (no real timers);
bridge test using `PumpDispatcher()`.

## Step 9: Preferences "Acquisition" section  — DONE (slice-1 controls only: Prowlarr, interval, size/format/groups; qBittorrent + destination-folder controls ship with slices 2/3 instead of as dead UI)
**Files:** `Models/PreferencesSection.cs` (edit), `ViewModels/PreferencesScreenViewModel.cs` (edit),
`Views/PreferencesScreen.axaml` (edit), `Views/Preferences/AcquisitionSection.axaml` **+ `.axaml.cs`
in the same step** (new; see CLAUDE.md AVLN2000 gotcha), tests in `App.Tests`
**What:** Prowlarr URL/key, qBittorrent URL/credentials/category (default `paperbunkr-comics`),
destination library folder, connection tests (Prowlarr only in slice 1; qBittorrent test stub returns
"available in the next update" — no dead UI), scoring weights. Secrets via `CredentialStore`.
**Depends on:** Steps 1, 4
**Verify:** view-model tests; build twice per the AVLN2000 recipe; avalonia-pro-max review checklist.

## Step 10: Wanted screen (Layout A)  — DONE (no cover thumbnails yet: remote cover URLs need the arc-cover cache plumbing, deferred; rows are text-only. Lists are not virtualized - fine for tens of rows, revisit if bulk requests make them long)
**Files:** `ViewModels/WantedScreenViewModel.cs`, `Views/WantedScreen.axaml` + `.axaml.cs` (new),
`MainViewModel.cs` and `Views/MainWindow.axaml` (edit: rail entry, DataTemplate, all string-key
touchpoints listed in the survey), tests in `App.Tests`
**What:** tabs Wanted / Upcoming / Candidates / Series, "Search now". Rows use existing card/pill
styling and Pb* tokens (no hardcoded hex). Any remove/close button inside a row defers via
`Dispatcher.UIThread.Post` (CLAUDE.md runtime gotcha).
**Depends on:** Steps 7, 8
**Verify:** view-model tests; build; review checklist; **on-screen check is the user's** (no UI
automation without permission).

## Step 11: Series Detail "Missing Issues" and arc "Request missing"  — DONE (Detail: own SeriesMissingIssuesViewModel, inline two-step confirm instead of a dialog; Reading list: bulk "Request missing issues" menu item on arc-linked lists as an Activity Center job, per-item Request on placeholder rows of any list)
**Files:** `ViewModels/DetailTabsViewModel.cs` and its view (edit), reading-list screen view model
and view (edit), tests in `App.Tests`
**What:** "Missing Issues (n)" section with per-issue Request, Watch toggle, confirmed "Request all
shown"; "Request missing" on arc-linked reading lists, per-item Request on other lists.
**Depends on:** Step 7
**Verify:** view-model tests; build; review checklist; on-screen check by the user.

## Step 12: Docs
**Files:** `docs/paperbunkr-todo.md` (update by hand: status, commits, what was verified),
`CHANGELOG.md`, spec (fix `TestDispatcher.Drain()` mention and the host/`ComicVineClient` location
notes from the survey)
**Depends on:** all
**Verify:** full `dotnet test` run compared with the recorded baseline failure set.

## Progress and baselines
- **Baselines (before any change, `master` 955f62f):** `Data.Tests` 1101 pass / 6 fail (all six are
  pre-existing `*MigrationTests` that call `Migrate()`); `App.Tests` `PreferencesScreenViewModelTests` +
  `DetailTabsViewModelTests` 249 pass / 8 fail (Library Health + shortcut-conflict tests; identical
  failures on an untouched `master` checkout, order-sensitive/flaky).
- **After Steps 1-2:** `Data.Tests` 1115 pass / the same 6 fail (+14 new tests, 0 new failures);
  `App.Tests` (same two classes) 249 pass / the same 8 fail.
- **Extra baseline (found later):** `ActivityCenterViewModelTests` has 3 pre-existing failures on untouched `master`
  (`Alerts_AreWrapped_AndDismissRoutesToService`, `OpeningPeekOrDrawer_SetsPanelIsOpen_OnService`,
  `FollowLinkCommand_InvokesResolver_AndCloses`), so the App-side baseline for the classes I run is **11 failing**
  (8 + 3), all identical on `master`. `ReadingScreenViewModelTests` adds 2 more pre-existing failures
  (`Delete_OfTheLastRemainingList_ClearsTheScreen`, `Delete_OfTheActiveList_FallsBackToAnotherList`), so **13** in total
  across the classes I run; each set was re-run on an untouched `master` checkout and is identical there.
- Step 2 deviation from the spec: spacing is **1.1 s**, not 1 s (ComicVine's velocity limit returns HTTP
  420 / `status_code` 107 and community guidance is >= ~1.1 s); the handler enforces the request timeout
  itself so queue time never counts against `HttpClient.Timeout`.

## Test strategy
xunit; temp-file SQLite in `Data.Tests`; fake `HttpMessageHandler`s and a fake clock for handler/
client tests (no live network); Daemon loop driven by a manual tick. Before Step 1, record the
baseline `dotnet test` result per project so pre-existing failures are not mistaken for regressions.
Anything only verifiable on screen is listed as such and left for the user.
