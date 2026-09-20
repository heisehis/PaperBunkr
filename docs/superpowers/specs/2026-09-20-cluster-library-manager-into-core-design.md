# Cluster Library Manager into core — Design Spec

*Date: 2026-09-20. Status: implemented on `feat/clm-into-core` (see the plan for per-phase status and the deliberate deviations); revised after external review (section 13). Unmerged and untested against real services.
Scope: move the Cluster Library Manager (CLM) — ComicVine scraping + library organizing, today the native-tier
plugin `github.com/heisehis/ClusterLibraryManager` v1.7.0 — into the Paperbunkr core app, unify it with the
comic-acquisition daemon (`2026-09-19-comic-acquisition-daemon-design.md`, merged as `8295bed`), and make
"scrape on import" the first joined-up feature. Sources read: the plugin's history in this repo (`de8394d^`) and a fresh
clone of the standalone repo (single commit, "Initial import v1.7.0", ~4,900 non-test lines, 45 files).*

## 1. Decisions (grilling round 1)

| # | Decision |
|---|----------|
| Q1 | CLM is retired as a plugin. The standalone repo is archived; no `.pbplugin` ships. The v4 native tier stays (third parties) but CLM no longer consumes it. |
| Q2 | State moves to **core EF tables** (one additive migration). **No import of the plugin's LiteDB data**: the owner is effectively the only user of the plugin and will recreate the few profiles by hand (decided 2026-09-20). No LiteDB dependency in core. |
| Q3 | One ComicVine client in `Paperbunkr.Data.ComicVine`; one shared rate-limit budget. Interactive scrapes = foreground priority, scheduled = low. |
| Q4 | One template engine. The plugin's Organizer grammar is the single grammar; acquisition's stored templates are translated (see 5). |
| Q5 | Library right-click: "Scrape with ComicVine…", "Organize…". New Preferences section **Organize & Scrape**. Two real scheduled tasks, off by default. Plugin dialogs ported to core views. |
| Q6 | Scrape-on-import applies metadata **by the known ComicVine issue id** — no search, no review. On by default when Acquisition is on; toggle. Failure leaves the issue imported and raises a needs-review item in the Activity Center, never a modal. |
| Q7 | Writes go through core's audited metadata writer and the existing ComicInfo write-back, not direct DbContext writes. |
| Q8 | Phasing in section 10. |
| Q9 | Standalone repo cloned and compared (section 2). |
| New | **All credentials live in Preferences → Connections** (section 7). This includes the Prowlarr and qBittorrent credentials that slice 1-3 of acquisition put in the Acquisition section. |

## 2. What is being absorbed (verified against the standalone repo)

The standalone repo is byte-for-byte the code that was in this monorepo plus later work (v1.7.0 added: a series detail panel
`DetailPanel/SeriesScraperPanel*`, `ComicVineIssueReviewDialog*` (the issue-match step), `ProfileSelectDialog*`,
`ScrapeBatchHeader*`, `RemoteCoverImage`/`RemoteCoverImageLoader`). It has one commit, so there is no history to preserve or reconcile.

Class map (plugin → core):

| Plugin | Core destination |
|--------|------------------|
| `ComicVine/ComicVineService`, `ComicVineDtos`, `ComicVineException` | folded into `Paperbunkr.Data.ComicVine` (`ComicVineClient` grows search-with-detail methods; DTOs merged, not duplicated) |
| `ComicVine/MatchScoreCalculator`, `ComicVineImprints`, `ComicVineMatchMemory` | `Paperbunkr.Data/ComicVine/Scraping/` (pure logic; tests come with it) |
| `ComicVine/ComicVineScrapeOrchestrator` | `Paperbunkr.Data/ComicVine/Scraping/ScrapeOrchestrator` with an `isInteractive` flag (unattended = skip-and-log, as in the plugin spec §4) |
| `Templating/*` (parser, evaluator, resolvers, sanitizer) | `Paperbunkr.Common` or `Paperbunkr.Data/Naming/` — one engine used by the organizer **and** the acquisition importer |
| `Organizing/*` (organizer service, models, profile store, undo log) | `Paperbunkr.Data/Organizing/` (service) + EF entities; `OrganizerProfileStore`/`UndoLog` become EF-backed |
| `Persistence/PluginDatabase`, `Settings/PluginSettings` | dropped; state in core tables, API key already in `CredentialStore` |
| `Dialogs/*`, `Settings/*View*`, `DetailPanel/*` | `Paperbunkr.App/Views` + `ViewModels` (Pb* tokens instead of the plugin's hard-coded colors, real `OverlayShell`/`ConfirmDialog`) |
| `OrganizerScraperPlugin` | deleted; its command wiring becomes ordinary commands + scheduled tasks |

No new package dependencies.

## 3. ComicVine unification

- The plugin owns its own `HttpClient` and its own key (`PluginSettings.ApiKey`). Core already has a shared prioritized rate-limit
  handler (1.1 s spacing, 200/h, Low capped at 150, 420/429/107 pause). The scraper uses **that**, so scraping while acquisition
  searches cannot double the request rate against one key.
- The scraper reads the key from `CredentialStore` ("ComicVine"/ApiKey) — the same key Connections already manages. No second field.
- Interactive actions (right-click scrape, Detail panel) request `High`; the scheduled scrape and scrape-on-import use `Low`.
- **Capacity is already reserved, not just ordered:** the handler caps `Low` at 150 of the 200/h window (`LowPriorityLimit`), so 50 requests (25%) are
  available only to `High`. That stays; no change to the numbers. A test pins the guarantee (150 Low requests in the window leave `High` unblocked).
- **Negative cache:** a ComicVine 404 for an issue id is remembered (see 6.2) so background work never re-asks for an issue that does not exist upstream.
- DTO overlap is reconciled by extending core's `ComicVineVolume`/`ComicVineIssue` records (the plugin's are richer: person credits,
  characters, story arcs, image sets). One record set, one JSON mapper.

## 4. Storage

### 4.1 Tables (one additive migration, hand-verified per project convention)
- `ComicVineMatchMemory(SearchKey PK, VolumeId, UpdatedAt)`
- `OrganizerProfile` (name, folder/file templates, mode, sanitization overrides, rule reference, remove-empty-folders, `IsDefault`)
- `OrganizeBatch` + `OrganizeMove` (old path, new path, batch id, at, `IsReverted`) for Undo. An undo **marks rows reverted instead of deleting them**, so the movement history stays auditable.
- `WantedIssue` gains the durable scrape state (6.2): `ScrapeStatus` (`NotApplicable`, `Pending`, `Scraped`, `Failed`), `ScrapeAttempts`, `ScrapeLastAttemptAt`, `ScrapeError`.
- `AcquisitionSettings` gains `RenameTemplateOriginal` and `RenameTemplateGrammar` (`Import` | `Organizer`) for the template upgrade (section 5).
- Scrape *settings* (which fields to apply, overwrite policy, collision policy for unattended runs) as columns on `AppSettings`, like
  `AcquisitionSettings`' approach of a single settings row — decided at plan time whether a dedicated `ScrapeSettings` row is cleaner.

### 4.2 Why this reverses plugin spec §10
The plugin spec chose a private LiteDB file to avoid contaminating core migrations with one optional plugin's concerns and to sidestep the
native-SQLite packaging bug. Neither applies to a core feature.

### 4.3 No import of existing plugin data
The plugin was never distributed to the wider user base, and its only real user (the owner) will recreate profiles by hand. Match memory is a +7 score
nudge that rebuilds itself as scrapes are confirmed; the undo log holds stale paths. So nothing is read from `%AppData%\Paperbunkr\plugins\cluster-library-manager\`,
and core takes **no LiteDB dependency**. (Deliberately not a migration step: dropping it removes a code path that could otherwise fail at startup.)

## 5. One template engine (the grammar difference)

Fact found while surveying (the round-1 recommendation assumed one engine was a superset of the other; it is not):

- **Organizer grammar** (plugin): `{prefix<name(args)>postfix}` with `?` conditional and `!` inversion markers; the CE Library Organizer's syntax.
- **Import grammar** (acquisition `NameTemplate`, a port of CE core's `ExtendedStringFormater`): `{token}`, `{token:000}`, `[optional group]`, `\` escape.

Decision: **the Organizer grammar is the one engine.** It can express everything the import grammar does (a conditional group with prefix/postfix
is `[ … ]`; `{number:000}` is a padded-number argument). Concretely:
1. A `NameTemplateTranslator` converts an import-grammar template to the Organizer grammar **once**, not on every read. Delivery: an additive migration adds
   `RenameTemplateOriginal` and `RenameTemplateGrammar`; a **one-time, idempotent data upgrade** (gated on `RenameTemplateGrammar = Import`, run at startup in the
   same place other data upgrades run) translates the stored template, keeps the untouched original in `RenameTemplateOriginal`, and flips the grammar flag. It is
   deliberately *not* C# inside the EF migration itself (unlike `ReadingEventBackfill`, which is static SQL): migrations must not depend on evolving application code.
   Translation that fails or is ambiguous **never throws and never silently changes output**: the default template is used, the original text is preserved and shown in
   Preferences with a "couldn't be converted" note, and the importer keeps working. The original is dropped only when the user explicitly saves a template in the UI.
2. The acquisition default becomes the Organizer-grammar equivalent of `{publisher}/{series} ({volumeyear})/{series} #{number:000}` and a
   **regression test pins that the produced path for a fixed set of inputs is identical before and after** (including the "issue 1.5 is never rounded" rule and folder-segment sanitization).
3. Token names unify to the union (`publisher`, `volumeyear` are already accepted by both after the merge; the Organizer resolvers are the source of truth).
4. `NameTemplate` (Daemon) is deleted once the importer calls the shared engine. Standing rule: token semantics are verified against the CE plugin source
   the plugin already cites, not against memory.

## 6. Scrape-on-import (the first joined-up feature)

Flow, after `ImportProcessor` has copied, renamed and (if enabled) written ComicInfo for an acquired issue:
1. The import already knows the `CatalogIssue.ComicVineIssueId` and `WatchedSeries.ComicVineVolumeId`.
2. A `ScrapeByIdService` fetches that one ComicVine issue (High-priority cheap call; volume details come from the catalog cache) and applies the
   configured scrape fields through the audited writer, then triggers the existing ComicInfo write-back. **No series search, no scoring, no review dialog** —
   the match is known.
3. Failure modes (network down, key missing, 404, rate-limited): the issue **stays imported and unchanged**, its `ScrapeStatus` becomes `Failed` (see 6.2) and an
   Activity Center alert is raised as a *notification of durable state*, not as the record of it. The blocklist and the download `Status` are untouched: this must never mark a good download failed.
4. Toggle: Preferences → Organize & Scrape → "Add ComicVine details to downloaded issues" (default on when Acquisition is on). It is a step *inside* the
   import job's Activity Center run, not a separate job, so progress and errors appear where the user already looks.
5. Deferred/unattended rule from the plugin spec carries over: nothing here may open a modal.

### 6.2 Durable scrape state (never rely on a transient alert)
- `WantedIssue.ScrapeStatus`: `NotApplicable` (feature off / not an acquired issue), `Pending` (set in the same transaction that marks the issue `Imported`, so an app
  crash between import and scrape leaves a `Pending` row, not a silent gap), `Scraped`, `Failed`.
- **Retry loop:** the existing acquisition download loop (already a periodic `BackgroundService` pass) also sweeps `Pending`/`Failed` rows with backoff (attempt-count based,
  capped), at `Low` ComicVine priority. No second thread or new watchdog service.
- **Not retried automatically:** a 404 (issue does not exist upstream) and a missing API key are terminal until the user acts; they are recorded as `Failed` with a reason and
  count as the negative cache. Rate-limit and network failures are retried.
- **Needs-review list:** `Failed` rows appear in one list (in the Wanted screen's Downloads/Imported area) with **bulk** Retry and Dismiss, so a failed batch is one action, not one dialog each.
  The Activity Center alert is derived from these rows (it can be dismissed; the rows remain).
- The existing `ChannelEventPublisher` gets an `IssueScraped`/`IssueScrapeFailed` daemon event so the Activity Center bridge and future consumers react without
  coupling to the writer. (No mediator library is introduced; the codebase does not have one.)

## 7. Credentials: everything in Preferences → Connections

Requirement (user, 2026-09-20): all API keys and similar secrets live in the Connections section "as they always have".

- ComicVine already lives there (`ComicVineRow`, token shape). CLM reads it; the plugin's own key field is not carried over.
- **Move out of Preferences → Acquisition into Connections:** Prowlarr (URL + API key) and qBittorrent (URL + username + password), each with its
  existing Test button, saved through `CredentialStore` (DPAPI) exactly as now. They become a third group in Connections ("Download automation") beside the
  existing source and tracker groups, using the same `ConnectionProviderRow` row + dialog pattern and the "connected" checkmark.
- Preferences → Acquisition keeps behavior only: enable, check interval, size/format/group filters, destination folder, template, auto-grab. It shows a
  one-line status ("Prowlarr connected · qBittorrent not set up") with a link to Connections instead of edit fields for secrets.
- `AcquisitionSettings.ProwlarrUrl` / `QBittorrentUrl` (non-secret) can stay as columns; Connections reads and writes them through the same view-model so
  there is one source of truth. Existing saved values are untouched — this is a UI move, not a data migration.
- The Acquisition Preferences view-model tests and the connection-test seams (`AcquisitionSettingsViewModel`) are re-homed rather than rewritten.

## 8. UI surface

- **Library:** right-click → "Scrape with ComicVine…" (selected issues/series; opens the ported series-match then issue-match review, with the batch progress
  header) and "Organize…" (profile select dialog → plan preview → collision dialog when needed → Undo last organize).
- **Series Detail:** the plugin's `SeriesScraperPanel` becomes a native Detail panel (no plugin UI contract needed).
- **Preferences → Organize & Scrape (new):** profiles CRUD (`ProfileManager`), scrape fields, overwrite/collision policies, the scrape-on-import toggle. No secrets here.
- **Preferences → Automation:** two new real tasks in `ScheduledTaskCatalog` — "Scrape with ComicVine" and "Organize library" — off by default, `isInteractive:false`
  (skip-and-log matching, configured collision policy). This replaces the plugin's private timer.
- **Threading rule (applies to every ported dialog and to scrape-on-import):** ComicVine DTO mapping, credit/string formatting and path resolution happen off the UI thread
  and yield ready-to-bind view-model data; the dialog opens with it or shows a `BusyIndicator` while it loads. Only what genuinely is CPU/IO work is moved to a worker
  (`Task.Run` is not sprinkled over trivial property sets). Verified with the same headless-view tests plus a test that opening a dialog does no ComicVine work on the UI thread.
- All dialogs use core `OverlayShell`/`ConfirmDialog`/`BusyIndicator`, Pb* tokens, and go through the Activity Center for jobs/alerts (standing rule). Avalonia
  subskill review checklist applies at implementation time.

## 9. Retirement of the plugin

- Archive `github.com/heisehis/ClusterLibraryManager` with a README pointer to core (a repo action for the user — not done from here).
- On startup, if the `cluster-library-manager` package is installed, show a one-time Plugins-screen notice ("Now built in — this plugin is no longer needed")
  and refuse to load it, so both cannot scrape the same library. No auto-uninstall.
- `de8394d` already removed it from this repo; nothing to delete here. The v4 native tier, its docs and `sample-plugins` stay.

## 10. Phasing (each phase shippable; tests green before the next)

1. **Unify (no visible change):** ComicVine client merge; template engine + translator + one-time template upgrade (with `RenameTemplateOriginal`); Daemon importer moved onto the shared engine; regression test on the default path; rate-limit reserve test.
2. **Scrape-on-import + credentials move:** `ScrapeStatus` columns and retry sweep, `ScrapeByIdService`, the import step, needs-review list with bulk actions, Activity Center alert; Prowlarr/qBittorrent rows into Connections.
3. **Scraper in core:** migration, match memory, scoring, orchestrator, series/issue review dialogs, batch header, right-click action, Detail panel.
4. **Organizer:** profiles, planner/mover, collision dialog, Undo, Preferences → Organize & Scrape.
5. **Automation + retirement:** the two scheduled tasks, the plugin-installed notice, docs/changelog/wiki.

## 11. Testing

Port the plugin's test suite with the code (scoring, service, template engine, sanitizer, organizer, orchestrator, view-models) — it is the strongest
evidence that behavior did not drift. Add: template translator round-trips; default-path regression; scrape-by-id (fake ComicVine); unattended paths never
open a dialog; migration additivity; Connections rows (save/test/connected state) for Prowlarr and qBittorrent. Headless view-construct
tests for every new view (XAML weave). Not verifiable here: real ComicVine/Prowlarr/qBittorrent behavior and on-screen look — listed for the user, as before.

## 12. Risks and open items

- **Behavior drift in the port** — mitigated by porting tests first, phase by phase.
- **Template translation edge cases** (rare user templates using `\` escapes or nested optionals) — translator rejects what it cannot translate and keeps the
  old text visible with a validation message rather than silently changing output.
- **Scope**: this is the size of the acquisition feature again. Phase 1-2 alone deliver the hand-off the user asked for first.
- **Version bump / worktree**: work happens on a feature branch in a worktree; the DB is shared per-user, so migrations are not run from the worktree.
- Out of scope now: richer Undo history, CE's advanced-settings text box, publishing the plugin contract packages, remote sharing.

## 13. External review (Gemini, 2026-09-20) - dispositions

| # | Finding | Disposition |
|---|---------|-------------|
| Flaw 1 | Data destruction / JSON export-import bridge | **Declined by the owner** (decided; see 4.3). |
| Flaw 2 | Scrape-on-import failures live only in a transient alert | **Accepted, extended.** Durable `ScrapeStatus` on `WantedIssue`, set `Pending` atomically with the import; retry via the existing loop (6.2). Not a separate watchdog. |
| Flaw 3 | High priority only orders the queue; needs reserved quota | **Already true in the code.** `LowPriorityLimit = 150` of 200 reserves 25% for High. Kept at 25% (the review's "exactly 20%" would shrink it); pinned by a test. |
| Flaw 4 | On-the-fly translation is fragile | **Accepted in part.** Translate once and keep the original, but via a one-time idempotent data upgrade, not C# inside an EF migration (section 5). |
| Flaw 5 | UI-thread blocking in dialog hydration | **Accepted as a rule** (section 8), scoped to real CPU/IO work. (The "C# 10" note in the review's prompt is moot: the solution is net10 / current C#; file-scoped namespaces and records are already the norm.) |
| Add 1 | Dead-letter table | **Subsumed** by `ScrapeStatus = Failed` + attempts (6.2); a separate table adds nothing. |
| Add 2 | Dry-run organizer + JSON plan | **Partly.** The organizer already previews its plan before moving anything; a JSON dump / CLI argument is not built (YAGNI). |
| Add 3 | Soft-delete undo | **Accepted** (`IsReverted`, 4.1). |
| Add 4 | `IMetadataProvider` abstraction | **Declined for now.** A single implementation behind an interface; Metron/AniList already have their own adapters elsewhere. Extract it when a second scraper provider is real. |
| Add 5 | Headless CLI for scheduled tasks | **Declined.** The daemon is UI-free but hosted in the app; a headless host is a separate project. |
| Add 6 | Negative cache for 404s | **Accepted** (3, 6.2). |
| Add 7 | Event bus / mediator | **Accepted minimally** via the existing daemon event channel; no mediator library. |
| Add 8 | Bulk needs-review grid | **Accepted** (6.2). |
| Add 9 | Automated SQLite `VACUUM` | **Declined.** VACUUM takes an exclusive lock and rewrites the whole live database; not worth doing unattended for logs this small. |
| Add 10 | Auto-ping Prowlarr/qBittorrent when Connections opens | **Declined.** It would fire network calls (and qBittorrent login attempts) on merely opening a settings page. The explicit Test button and the connected checkmark stay. |
