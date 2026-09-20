# Cluster Library Manager into core — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md*

Branch `feat/clm-into-core` (worktree `.claude/worktrees/clm-into-core`). Plugin source of truth: `github.com/heisehis/ClusterLibraryManager` v1.7.0
(cloned to the session scratchpad; the same code is in this repo's history at `de8394d^:plugins/ClusterLibraryManager`). Do not run the app or
`dotnet ef database update` from the worktree (shared per-user DB). Baselines (untouched `master` 8295bed): `Data.Tests` 6 pre-existing migration failures,
`App.Tests` classes run per class (full suite cannot complete), `Daemon.Tests` 188 pass.

## Phase 1 — Unify (no visible change)

### Step 1: Shared template engine in `Paperbunkr.Data`
**Files:** `src/Paperbunkr.Data/Naming/{TemplateNode,TemplateParser,TemplateEvaluator,FieldResolvers,Sanitizer}.cs` (new, ported from the plugin, namespace
`Paperbunkr.Data.Naming`); `src/Paperbunkr.Data.Tests/Naming/{TemplateEngineTests,SanitizerTests}.cs` (ported).
**What:** Move the plugin's Organizer-grammar engine as-is. Two additions: `TemplateContext.Extra` (host-supplied name→value, used by callers that have
values an `Issue` doesn't carry) and two resolvers, `volumeyear` and `filename`, that read `Extra`.
**Verify:** ported engine tests pass unchanged (only namespaces edited); new tests for `Extra`/`volumeyear`/`filename`.

### Step 2: `NameTemplateTranslator` (import grammar → Organizer grammar)
**Files:** `src/Paperbunkr.Data/Naming/NameTemplateTranslator.cs` (new), tests.
**What:** Translate `{token}`, `{token:000}` (→ pad arg where the organizer resolver pads; otherwise fail), `[ … {token} … ]` single-token optional groups
(→ prefix/postfix), `\` escapes. Token map: month→`month#2` semantics, `{month}`→`<month#N>`, volume/publisher/series/title/number/year/day/format 1:1,
`volumeyear`/`filename` → new resolvers. Anything ambiguous (multi-token optional group, literal `<`/`{`, a format with no equivalent) → returns
`TranslationResult.Failed(reason)`; never throws, never guesses.
**Verify:** table-driven translator tests including the default template, plus a **round-trip equivalence test**: for a grid of inputs, `NameTemplate.Format(old)`
equals `TemplateEvaluator.Evaluate(translated)`.

### Step 3: Schema — `RenameTemplateOriginal`, `RenameTemplateGrammar`
**Files:** `AcquisitionSettings.cs`, `PaperbunkrDbContext.cs`, new migration `AddTemplateGrammar` (+ Designer, snapshot). Additive, hand-verified.
**Verify:** migration test file mirroring the existing acquisition migration tests; snapshot diff is insert-only.

### Step 4: One-time template upgrade
**Files:** `src/Paperbunkr.Data/Acquisition/TemplateUpgrade.cs` (new), call site next to the existing startup data steps, tests.
**What:** Idempotent, gated on `RenameTemplateGrammar = Import`. On success: store the original, write the translated template, set `Organizer`. On failure: keep
original in `RenameTemplateOriginal`, set the default (Organizer) template, set `Organizer`, and expose a "could not be converted" flag for the settings UI.
**Verify:** upgrade tests (default, custom, untranslatable, already-upgraded no-op, fresh DB default).

### Step 5: Importer and settings UI on the shared engine
**Files:** `ImportProcessor.cs` (edit), `NameTemplate.cs` (delete), `AcquisitionSettingsViewModel.cs` + `AcquisitionSection.axaml` (edit), Daemon/App tests (edit).
**What:** The importer builds a transient `Issue` (series, publisher, title, volume `v{year}`, number, year/month/day from the store date) and a `TemplateContext` with
`Extra` (`volumeyear`, `filename`), evaluates the Organizer template, splits on `/` and `\`, sanitizes each segment (CE `Sanitizer`) and applies the existing
Windows safety layer (reserved names, length). The settings view-model validates and previews with the same engine; shows the "couldn't be converted" note and the original.
**Verify:** **default-path regression test** (fixed inputs, path identical to the pre-change output for normal names; the deliberate `:`→` - ` difference is asserted
explicitly), Daemon import tests, App view-model tests, view-construct test.

### Step 6: Rate-limit reserve test
**Files:** `src/Paperbunkr.Data.Tests/ComicVine/RateLimitHandlerTests.cs` (edit).
**What:** Pin the existing guarantee: with 150 Low requests used in the window, a High request is served without waiting for the window to roll.
**Verify:** the test, plus existing handler tests.

Deferred from the design's Phase 1 (done in the phase that first needs it, recorded in the spec): the **ComicVine DTO/client merge** (Phase 2 adds
`GetIssueAsync`; Phase 3 folds the plugin's search/scoring) — extending records with no consumer would be dead code.

## Phase 2 — Scrape-on-import + credentials
1. `WantedIssue.ScrapeStatus/Attempts/LastAttemptAt/Error` + migration; `Pending` set atomically with `Imported`.
2. `ComicVineClient.GetIssueAsync` (full issue fields) + DTOs; 404 negative cache.
3. `ScrapeByIdService` (audited writer + ComicInfo write-back); retry sweep in the acquisition download loop; daemon events; Activity Center bridge alert derived from rows.
4. Needs-review list with bulk Retry/Dismiss on the Wanted screen.
5. Connections: "Download automation" group (Prowlarr, qBittorrent rows + Test), Acquisition section reduced to behavior + status line.
**Verify:** service/loop tests with fakes; bridge tests; Connections view-model tests; view-construct tests; virtualization test unaffected.

## Phase 3 — Scraper in core
Migration (`ComicVineMatchMemory`, scrape settings); fold plugin `ComicVineService`/DTOs/scoring/imprints/orchestrator into `Paperbunkr.Data`; port series-match, issue-match,
batch-header dialogs and the series detail panel to core views; Library right-click "Scrape with ComicVine…". Port the plugin tests with the code.

## Phase 4 — Organizer
Migration (`OrganizerProfile`, `OrganizeBatch`, `OrganizeMove` with `IsReverted`); port `LibraryOrganizerService`, planner/mover, collision + profile-select dialogs, Undo;
Preferences → Organize & Scrape; Library right-click "Organize…". Port the plugin tests.

## Phase 5 — Automation and retirement
Two `ScheduledTaskCatalog` tasks (off by default, unattended paths); Plugins-screen notice + refusal to load `cluster-library-manager`; changelog, wiki, todo, spec status.

## Test strategy
xunit, temp-file SQLite, fake `HttpMessageHandler`s (no live network), per-class App runs, view-construct tests for every new view. Not verifiable here and listed for the
user: real ComicVine/Prowlarr/qBittorrent behavior and on-screen look. The plugin's own test suite is ported with each piece as the behavioral baseline.
