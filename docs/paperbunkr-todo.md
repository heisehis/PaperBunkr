# Paperbunkr To-Do

*Renamed from `alpha-todo.md` 2026-09-18 — Alpha is done, project is past that stage now (see the
P0-P7 section below for the historical record). Content and format unchanged, just the name.*

*Scope: git/release prep + known gaps only, per `Paperbunkr-Roadmap.md` (2026-08-07). Beta backlog is
tracked separately in that document and not repeated here.*

Priority order below is suggested — highest-risk / release-blocking first, then polish ordered by
user-facing impact.

## Live tracker

This file is the authoritative, human-written record — commit refs, rationale, sub-item detail.
A companion dashboard renders a lighter view of the same P0–P7 status for quick scanning:
**https://claude.ai/artifact/2Zf5nmJiCzARE4MKgKAVMF** (old `/code/artifact/...` link now 404s —
same artifact, new short-link form; re-check this stays live before trusting either).

A scheduled cloud agent (`paperbunkr-alpha-tracker-sync`, routine
`trig_018nELx6EohKVCqFrdP9bX3T`, every 6h, read-only against the repo) checks `git log` against
the tracker's own embedded `HEAD` marker and republishes it to the same URL only when it can
concretely verify a status change — it never edits this file or commits anything. This file still
needs a human (or a Claude Code session working in it) to update by hand when priorities shift;
the tracker just keeps a lightweight view from silently going stale between those updates the way
this file itself already did once (see the note below).

## What's left (as of 2026-08-12, HEAD `85fb681`)

> **2026-10-28 — Plugin API 4.5: the rest of the 2026-09-20 plugin pitch (items 1–14) implemented (working tree, uncommitted):**
> spec/plan `docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-{design,plan}.md`. Developer mode (script plugins reload on save,
> live log pane), New plugin… scaffolds (C#/Python/native), `src/Paperbunkr.Plugins.Testing` (fakes + `PluginScriptRunner`); hooks
> `SeriesStatusChanged`, `ScheduledTaskRan`; plugin smart fields (`<Fields>`, typed in Smart Lists), write batches with a preview; plugin
> metadata/cover/story-arc sources; `menus`/`toolbar` placement, reading-list menu, native issue panel and Insights card; `<Network>` +
> install confirm + `Environment.Http`. Cut: scoped file system, plugin-to-plugin registry. `PluginApi.Current` = 4.5.
> **Verified (headless):** Plugins.Tests full run 235/235 after slice A; the touched Plugins/Data/App classes since (manifests, sandbox `#r`,
> runner, typed field queries, backstop, smart-list query builder, host hooks, scheduler, fields/batches/providers/http, menu builders,
> Plugin screen) all pass; XAML lint passes for every new/changed view (the one lint failure is in two Scraper dialogs this work didn't touch).
> **Not verified:** anything on screen; the cover picker's plugin path, native issue/Insights views from a real module, the toolbar button.

> **2026-10-25 — CBL Smart Match review tools implemented (working tree; approved spec/plan `b2a2e04`):**
> first delivery within the user's six-item backlog sequence. Dedicated retained review screen for Find & link, current-list and selected-list review;
> read-only CBL/CSV/arc/folder previews (including new-list and dropped-list entry points); default-on preservation of unresolved entries;
> explicit, strict cross-list choice application; atomic stale/duplicate guards; database-local 30-day link history with partial undo conflict previews,
> placeholder recreation and preservation of later notes/roles/order. Import and cross-list changes share a transaction, while undo never deletes
> the newly imported list. Drafts survive navigation; close, tray exit and update restart check unfinished choices.
> **Verified:** 184 broader Data checks passed; the final 17 review tests passed after transaction/legacy-source changes, including migration round-trip,
> mixed import/link rollback and shared-placeholder undo across visits. Final App selection: 45/45 passed (review workflows, updated single-link/cancel
> assertions, real MainWindow search/candidate-arrow behavior, shared UI lint and six Default/Daylight/Matrix renders at 750/1100 px).
> Final arc selection: 10/10 passed, including fetch-once preparation and preserving the source arc name when the user customizes the list title.
> Earlier expanded App coverage exercised 121 tests; its obsolete drawer assertions and six checkbox-contrast failures were corrected and covered by
> the final focused pass. Render inspection caught unthemed list backgrounds/selection and invisible Matrix checkbox text; tokens now govern them.
> **2026-10-25 — CBL Smart Match healing and safe refresh implemented (working tree; approved spec):** identity-affecting saved operations now recheck unresolved entries once after commit, with summaries through existing feedback. Arc refresh blocks incomplete source responses, reconciles inside one transaction, uses strict source-entry identity, updates a still-present row to the current automatic match while preserving row annotations, and retains hand-added plus removed-but-user-linked rows after source order. Continuity reconciliation is unchanged. **Verified:** main `Paperbunkr.App` Debug build succeeded (0 errors; existing ImageSharp license warning); `git diff --check` passed. Focused tests were not run for this delivery. No new view/XAML was added.
> **Item 1 complete.** Native on-screen/Narrator/high-contrast/DPI review for the first review-tools delivery remains pending; no full fast/slow suite pass is claimed. Specs/plans:
> `docs/superpowers/specs/2026-10-25-cbl-smart-match-review-{design,plan}.md` and
> `docs/superpowers/specs/2026-10-25-cbl-smart-match-healing-refresh-{design,plan}.md`.

> **2026-10-25 — Item 2, tracker auto-push retry queue implemented in the working tree; design spec committed as `cff2fd2`.** Added typed lookup/push outcomes for all eight tracker adapters, a durable unique retry row per link with target snapshot and five backoff attempts (1/2/4/8/16 minutes), coalescing that never regresses chapter progress, start/network-recovery/periodic retry processing, pause/resume, manual-success/unlink/retarget cleanup, and Activity Center Retry now/exhausted Dismiss actions. Retry attempts re-read the remote entry; manual two-way conflict behavior remains intact. **Verified:** main `Paperbunkr.App` Debug build succeeded (0 errors; existing ImageSharp license notices and nullable warning); `git diff --check` had no whitespace errors. No test suite or native screen-reader/responsive visual review was run. Specs/plans: `docs/superpowers/specs/2026-10-25-tracker-auto-push-retry-queue-{design,plan}.md`.

> **Item 5 — Guided View detection performance implemented in the working tree (2026-10-26; no commit).** Cold panel detection now runs through a per-reader single-flight queue, serializes inference, drops stale queued pages, and records p50/p99 analysis duration in the existing reader performance overlay. Smart double-click preserves its click while detection runs and applies it only if the page/view still matches; “Show detected panels” and “Report bad panel detection” use the async path, with reports keeping an owned page image across navigation. **Verified:** main `Paperbunkr.App` Debug build succeeded (0 errors; existing ImageSharp license warning); `git diff --check` passed. Test suites and native on-screen/accessibility review were not run. Specs/plans: `docs/superpowers/specs/2026-10-26-guided-view-performance-{design,plan}.md`.

> **2026-10-26..28 — Books arc and Books section redesign built in the working tree (no commit); status lines in the design docs corrected 2026-10-28.** Source confirmed present for: Slice 1 data foundation (`AddBookMetadataFoundation`), 2a browse shelves (`AddBooksBrowseViews`, `BooksBrowseQuery`, `BookShelfService`), 2b Detail, 3a jump-back, 3b typography aids, 3c annotations, 4a Calibre import (`Paperbunkr.Data/Calibre/*`), 4b dictionary (`Services/Dictionary/*`), 4c OPDS (`Services/Opds/*`, `OpdsCatalog`), 4d read aloud (`Services/Speech/BookReadAloudPlan`), the paged/scroll reader modes (`BookPageMap`), and the three-slice section redesign (Reading Room + Shelf). Also built the same days: You-vs-Community + chapter alignment (spec "As built"), ComicRack import gaps, and continuity unplaced events + CBRO overlap notes (`AddEventOrderOverlapNotes`). **Verified:** the paged/scroll modes, popup, drawers, and the redesign's reader/Detail fixes were user-checked on screen; the rest has unit/headless tests only. New migrations (Books slices, tracker retry queue, event-order notes) are not yet applied to the user's dev database. Still not built: comic reader Slice H, Metadata Model Phase 7 (deferred), and the two Plugin API items cut by decision (7 scoped file system, 12 plugin-to-plugin registry). The rest of the 2026-09-20 plugin pitch, including the live log pane, was built as Plugin API 4.5 on 2026-10-28 (see the entry above).

> **Release preparation (2026-10-24, v0.8.0-beta; source `0be8125`, focus-ring fix `a003b76`):**
> the accumulated source, artwork, installer and documentation changes are committed on
> `codex/release-v0.8.0` (PR #106). This supersedes earlier "uncommitted" labels as a statement about Git
> status only; their individual visual-review and verification caveats still apply. App version
> is `0.8.0.0`; the release retains the existing `-beta` convention. Installer verification passed
> all 16 PowerShell/build checks and the shared native Pascal checks, including compilation with
> and without the thumbnail handler. Website builds passed locally and in CI. The app and compiled
> XAML rebuilt successfully; all 139 focused release-version, mark resolver/render, plugin API and
> focus-ring tests passed. Four stale Australian-rating assertions were updated to the CE raster
> artwork contract. A real Home focus-ring clipping defect was reproduced in isolation and fixed
> by expanding the tile's top gutter while compensating shelf margins to preserve cover alignment.
> The broad `Speed!=Slow` run was stopped after the machine became unresponsive; the user confirmed
> terminating its testhost. No complete fast/slow-suite or manual accessibility sign-off is claimed.
> Packaging/publication follows the existing tag-triggered workflow, with the external Six Labors
> licence supplied through a repository secret rather than committed source.

> **Manual session note (2026-10-24, installer polish; implementation uncommitted,
> design/plan `2c256dc`):** implemented restrained welcome/finish copy, accurate Library quick-start,
> shortcut-only component descriptions, labelled Repair/Uninstall and Update/Change options,
> a shorter upgrade flow with previous choices, numeric downgrade blocking in interactive/silent
> setup, unreadable-version fallback/blocking, and default data preservation with separately
> confirmed, bounded profile deletion. Version inputs now derive from the app project and reject
> mismatches; thumbnails are offered only when the native DLL is bundled. Existing modified brand
> assets were preserved. Native verification passed 39 helper checks; the PowerShell/build harness
> passed 16 checks, including production-script compilation with/without thumbnails. Real Release
> app publishing and installer compilation succeeded using the user's external Six Labors licence.
> Corrected a missing Windows `OS` environment variable in automation; Native AOT then published,
> with the handler verified as x64 native rather than managed. Repackaging with that DLL succeeded:
> `installer/Output/PaperbunkrSetup-0.7.89-beta-2c256dc.exe`. Visual/DPI, keyboard/Narrator,
> and real install/repair/update/uninstall/registry checks
> in a disposable Windows environment remain outstanding. See
> `docs/superpowers/specs/2026-10-24-installer-polish-{design,plan}.md`.

> **Manual session note (2026-10-24, bulk story-event suggestions; implementation uncommitted,
> design `1167a91`):** added checkboxes, selected count, Select all/Clear and Accept/Dismiss selected
> to New story events in Suggestions & checks. Batches keep the page open, show progress and one
> summary, refresh the event sidebar, retain failed rows selected with their error, and continue
> processing other rows. Individual acceptance still opens the event. Shared verification and
> transactional event creation preserve existing membership/role behavior. The App and compiled
> XAML built; all 45 focused behavior, layout and shared UI lint checks passed, including three
> theme layouts at 640/1000 px content widths. Headless render inspection supplements these checks;
> real-window keyboard and screen-reader review remain outstanding. See
> `docs/superpowers/specs/2026-10-24-story-event-suggestions-bulk-actions-design.md`.

> **Manual session note (2026-10-18, Collector’s index cosmetics package; uncommitted):** implemented
> the approved 14-item package (pitch #8 excluded) in
> `docs/superpowers/specs/2026-10-18-cosmetics-package-design.md`: Library mats/hover/selection and
> grouping treatments, explicit edition ribbons, detail issue covers and current-reader marker,
> ordered reading-list progress, global comic page frames, bookmark accessibility, chapter openings,
> existing source/tracker marks, truthful cover states, and live theme preview tabs. App build and
> Avalonia XAML weave succeeded. Migration test passed; focused App selection was 146/147, with one
> existing tag-chip assertion mismatch (`#User` included unexpectedly by the test's expected set).
> Manual visual review across sizes/skins and assistive technology remains outstanding.

> **Manual session note (2026-10-17, app-wide motion system; uncommitted):** implemented the
> approved system-wide motion policy and staged migration in
> `docs/superpowers/specs/2026-10-17-app-wide-motion-system-{design,plan}.md`. The app builds
> successfully; focused motion policy, preferences, theme layout, home effects, Matrix rain, and
> migration tests passed (376/378 in the focused app selection; the two failures are existing
> detail-screen focus-ring checks reporting `MarkdownView` clipping a `SelectableTextBlock`). The
> 40-minute `Speed!=Slow` run was interrupted at the user's request; no full-suite pass is claimed.
> On-screen visual review remains outstanding.

> **Manual session note (2026-09-26, status-line audit across the 2026-09-22 to -25 spec batch):**
> the user confirmed directly that **Event Map is the only item from this batch not worked on** —
> every other design doc in it had a stale "not yet implemented"/"draft" status line despite real,
> uncommitted source already existing. Fixed each doc's own status line in place (see
> `docs/superpowers/specs/2026-09-2{3,4,5}-*-design.md`) rather than duplicating the detail here.
> Confirmed built, uncommitted, on-screen check outstanding on all: Insights reading goals/
> year-in-review recap/recommendations surface/redesign, Metron API utilization (5 phases), ComicVine
> scraper fidelity, comic reader Performance (steps 1-7), publisher icons + reader textures, reading-
> list role detection, library organizer audit fixes. **Event Map** (`2026-09-25-event-map-design.md`)
> remains a draft awaiting review — no plan doc, zero source, confirmed by grep. *(Superseded 2026-09-27: built, see
> "2026-09-27 - Event Map" below.)*

> **Manual session note (2026-09-24, ComicVine scraper fidelity audit — uncommitted, working tree
> shared with other concurrent sessions):** the user supplied the real, original ComicRack CE
> "ComicVine Scraper" plugin source (`ComicVineScraper-1.0.102.crplugin`, extracted for direct
> reading) and asked for a fidelity audit against Paperbunkr's own C# port, since prior design docs'
> claims hadn't been checked against the real plugin. The audit found a real correctness bug (the
> Imprint/Publisher write logic) plus a genuinely missing safety mechanism (CE's perceptual-cover-
> hash confirmation gate before an unattended auto-match ever applies). Design + plan:
> `docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-{design,plan}.md` (4 phases, 17
> steps). **Phases 1-3 (Steps 1-13) and Phase 4 Steps 14-16 all built and unit-tested this session:**
> - **Phase 1 (correctness):** Imprint/Publisher bug fixed; match-scoring apostrophe/underscore/
>   giant-sized/king-sized/one-shot canonicalization; fractional issue-number `BookScore`; out-of-
>   range-year handling; ComicVine **and** Metron clients both gained a real retry-once on transient
>   failure (2500ms, test-seam `RetryDelay`); person-credit role mapping now fans out every
>   comma-separated role (e.g. "artist" → Penciller **and** Inker) instead of just the first match.
> - **Phase 2 (safety):** the real cover-hash auto-match gate, via `CoenM.ImageSharp.ImageHash`'s
>   `AverageHash` (matching CE's own real algorithm, verified) — pinned `SixLabors.ImageSharp` to
>   2.1.13 to dodge several CVEs the transitive floor version carried. A gate failure falls through
>   to interactive review (or skip-and-log, unattended) exactly like `AutoChooseTopMatch` being off
>   for that one book. ComicVine identity (series+issue) now persists durably via the existing
>   `ComicMetadataExternalId` table (extended with `Issue`/`Series` kinds), not new columns.
> - **Phase 3 (settings):** `ConvertImprints`/`ForceSeriesArt`/`ShowCovers`/`ScrapeDelayMs`/
>   `PublisherAliases` added and wired into real behavior (imprint off-behavior, the match dialog's
>   cover pane falling back to the specific issue's own cover via a new `FindIssueCoverUrlAsync`,
>   cover-thumbnail visibility, a real inter-book delay distinct from the per-request rate limiter),
>   plus Preferences UI for all five.
> - **Phase 4 Step 14:** `ScrapeAsync` now returns a `ScrapeBatchResult` (Applied/SkippedByUser/
>   NoMatchFound/Failed breakdown) instead of a bare int; `ScrapeCoordinator`'s job-result summary
>   reflects the real breakdown. **Step 15:** the issue-review dialog's candidate list sorts in CE's
>   real natural order (`utils.py:natural_key`, verified, including unicode-fraction issue numbers
>   like "5½"), not raw API order. **Step 16:** Ctrl-held Skip in both review dialogs now writes a
>   durable `Issue.ScrapePermanentlySkipped` marker (migration `AddScrapePermanentlySkipped`) that
>   excludes the book from every future scrape attempt, interactive or unattended, with no search
>   even attempted — CE's real `book.skip_forever()`, verified.
> - **Step 17 (cover-image cycling + alt-cover memory) completed 2026-10-26:** live authenticated
>   issue-list and issue-detail responses confirmed `associated_images` entries with `original_url`.
>   Both review dialogs now cycle unique covers; a selected URL is remembered by provider + issue ID
>   for the current interactive scrape run only. The main Debug App build succeeded. See the
>   implementation note in `docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md`
>   §4.2.
> **Verified this session:** `Paperbunkr.Data.Tests`' full `ComicVine` subset and
> `Paperbunkr.App.Tests`' full `Scraper` subset both green after every step, plus two full-suite runs
> (Data.Tests 1601/1602, App.Tests 3463/3464 — the lone failures in each were pre-existing/unrelated
> to this work, from other sessions' uncommitted changes already in this shared tree:
> `RecapResolver.cs`'s remote-opt-in allowlist and `WantedScreenViewModel.Releases.cs`'s own sort
> test). **Not verified on screen** (no UI automation without permission, and the shared dev DB/
> working-tree risk noted elsewhere in this file). **Commit caveat:** the `AddScrapePermanentlySkipped`
> migration was generated against a tree that already had several other sessions' own uncommitted
> migrations present (`AddLibrarySnapshots`, `AddReadingGoals`, `AddComicMetadataEntities`,
> `AddMetronScrapeFields`, `AddPhase5MetronItems`) — the migration itself only touches the one new
> column (confirmed by reading its generated `Up()`/`Down()`), but review the migration ordering at
> commit time once those other sessions land, rather than assuming this one is safe to commit in
> isolation right now.
> **Follow-up same session:** the user pointed out there was no way to scrape a single comic from the
> Detail screen (only the whole-series panel existed there; the Library screen already had a working
> per-issue "Scrape…" right-click). Added a "Scrape…" entry to the Detail screen's issue-tile context
> menu (`DetailIssueContextMenuBuilder`/`DetailTabsViewModel.ScrapeIssue`), mirroring
> `LibraryScreenViewModel.ScrapeWithComicVine`'s exact selection-union shape and reusing the same
> `ScrapeCoordinator.ScrapeIssuesAsync`; the shell wiring (`MainViewModel.cs`) reloads the series
> afterward so the tile reflects whatever the scrape wrote. 692 Detail/Library/Scraper App.Tests pass.
> Not verified on screen (computer-use permission stays revoked per standing note elsewhere in this
> project).
> **Follow-up same session (2026-09-24, review-dialog tables + manual-search fallback + batch
> summary):** the user's own screenshot of the series-match dialog prompted three more asks, all
> verified against the real CE plugin source before building: design doc
> `docs/superpowers/specs/2026-09-24-scraper-review-tables-and-batch-summary-design.md`.
> - **Sortable tables:** both review dialogs (series-choice, issue-choice) redesigned from the old
>   card-list ("Best match"/"Other results") into real `Avalonia.Controls.DataGrid` tables with
>   click-to-sort column headers - CE's own `seriesform.py`/`issueform.py` shape, verified directly.
>   Series table: Series/Year/Issues/Publisher/Score (Score stays visible - Paperbunkr's own existing
>   deviation from CE, which hides it). Issue table: Issue#/Title, natural-sorted by default via a new
>   `NaturalSortKey` on `ComicVineIssueCandidateViewModel` (DataGrid only supports a single
>   `SortMemberPath` redirect, not a custom comparer, so the natural-sort tuple from Step 15 had to be
>   collapsed into one ordinal-sortable string). **Caught by its own test, not on screen:** the first
>   delimiter choice (`|`) sorted *after* lowercase letters, inverting the ordering for an empty vs.
>   non-empty prefix (e.g. "3" vs "Annual 1") - fixed to U+0001. New dependency:
>   `Avalonia.Controls.DataGrid` 12.1.1 (needed for the `DataGrid` control type itself); DataGrid
>   column bindings are always reflection bindings even in this compiled-bindings app (confirmed
>   Avalonia limitation, isolated to these two views). **Real theming bug caught before shipping, not
>   on screen:** `Avalonia.Controls.DataGrid`'s own bundled `Themes/Fluent.xaml` targets Avalonia's
>   *core* `<FluentTheme/>`, and FluentAvaloniaTheme's own docs are explicit that running it alongside
>   core FluentTheme is unsupported (this app uses FluentAvaloniaTheme, not core FluentTheme) - a live
>   community report (`github.com/amwx/FluentAvalonia` issue #30) confirms mixing them for DataGrid
>   specifically causes unresolved-resource/binding-exception failures. Verified two ways rather than
>   trusting a search result: directly inspecting the installed `FluentAvalonia.dll` (v3.1.0, the exact
>   version this app references) for its embedded `avares://` resource manifest, which confirmed
>   FluentAvaloniaUI ships its own bundled DataGrid styling
>   (`Styling/ControlThemes/BasicControls/DataGrid/DataGridStyles.axaml`); and the Avalonia XAML
>   compiler itself (`AVLN2000`), which rejected an initial `<StyleInclude>` attempt by reporting the
>   resource's real type as `Avalonia.Controls.ResourceDictionary` - confirming it needs
>   `ResourceInclude`/`MergedDictionaries` under `Application.Resources`, not `Styles`. Fixed in
>   `App.axaml` (wrapped the existing resource block in an explicit `<ResourceDictionary>` so
>   `MergedDictionaries` could sit alongside the pre-existing bare `<Color>`/`<Brush>` entries -
>   `Application.Resources` doesn't allow a second top-level content assignment). A full forced rebuild
>   (`-t:Rebuild`, not incremental - this project's own documented stale-DLL gotcha) confirms 0 errors
>   with the fix in place.
> - **Manual-search fallback:** `ScrapeOrchestrator.ScrapeAsync` now falls through to the interactive
>   review dialog when an auto-choose run's automatic search comes back with zero results (and a
>   reviewer is available) - CE's own real `scrapeengine.py` behavior ("no series could be
>   found...force the user to choose the search terms"), verified; Paperbunkr previously just skipped
>   the book silently in this case, which was the user's actual complaint.
> - **Batch summary:** `ScrapeBatchResult` restructured from bare counts to a list of
>   `ScrapeBookOutcome` (IssueId/BookLabel/Kind/Reason) with the four counts now computed properties -
>   every one of the ~10 counter-increment sites in the orchestrator's batch loop got a real reason
>   string (exception message for Failed; "Skipped"/"Skipped permanently" for SkippedByUser, the
>   latter re-checking `Issue.ScrapePermanentlySkipped` right after a skip resolves since the
>   orchestrator has no other signal which kind just happened). New `ScrapeBatchSummaryDialogView`/
>   `ViewModel` - a blocking modal at the end of every *interactive* batch (never unattended, matching
>   the existing headless-automation gate) with 4 count chips plus expandable-by-bucket lists, CE's
>   real `FinishForm` with a richer breakdown than CE's own plain scraped/skipped pair. Shown via
>   `ScrapeCoordinator.ScrapeIssuesAsync`, after the batch-progress header closes, not layered above it.
> **Verified:** 1619/1620 Data.Tests pass (the one failure is the same pre-existing, unrelated
> `RecapResolver.cs` issue from another concurrent session noted above); full App.Tests suite
> (3494 tests, re-run after the DataGrid/theme fix, with a `-t:Rebuild` forced full rebuild beforehand
> per this project's own documented stale-DLL gotcha) - 3493/3494 pass, the one failure the same
> pre-existing, unrelated `WantedQueueTests` sort issue from another concurrent session. **Not verified
> on screen** - same standing computer-use constraint; specifically flag whether the two review
> dialogs' new tables render with proper Fluent styling (not blank/unstyled) the first time either is
> opened for real, since that's the one piece automated tests can't confirm.
> **Follow-up same session (2026-09-24, two real bugs found live by the user, both fixed+tested):**
> - **Whole-series scrape barely applied anything, except whichever issue already had its Number set
>   directly.** The user found this themselves mid-session ("the scraper cannot detect the issue number
>   at all... paperbunkr scraper can only detect the issue number when i edit the metadata in-app and
>   save the exact number"). Two compounding root causes, both in how `ScrapeOrchestrator.ScrapeAsync`
>   resolved a book's own number for the real `FindByNumber` lookup (`CurrentBookNumber`, the legacy
>   `ApplyAsync` path, the issue-dialog's `LoadIssueSummariesAsync` all used to call
>   `issue.EffectiveNumber()` directly):
>   1. A filename-parsed number is **never written to `Issue.Number` directly** - it always becomes a
>      `MetadataProposal` row (`LibraryFolderScanner.AddFilenameProposal`), resolved dynamically via
>      `IssueMetadataExtensions.EffectiveNumber()`. That resolver reads the already-loaded
>      `issue.MetadataProposals` **in-memory collection** - and none of the scraper's real callers ever
>      `.Include(i => i.MetadataProposals)` (`ScrapeCoordinator.ScrapeIssuesAsync` loads books via a bare
>      `.Include(i => i.Series)`), so that collection is always empty at scrape time and
>      `EffectiveNumber()` silently returns null even when the DB has a perfectly good proposal.
>   2. `MetadataResolutionPolicy.Prompt` (which would leave the proposal `Pending`) has **no Preferences
>      toggle to ever reach** - every real library runs under the only reachable policy, `Automatic`,
>      under which the proposal is created already `Accepted`. So root cause 1 alone was the whole
>      story for virtually every real user: this was never a Pending-vs-Accepted policy gap, it was an
>      unloaded-navigation-collection bug that made an already-Accepted number invisible to the scraper.
>   Editing+saving the issue "worked" only because Issue Properties writes `Issue.Number` directly,
>   bypassing the whole proposal mechanism (and its unloaded-collection gap) entirely.
>   Fixed by rewriting the shared resolver (renamed `ResolveEffectiveNumberAndYearAsync`) to read
>   `MetadataProposal` straight from the database (Accepted preferred, Pending as a secondary fallback
>   for a book still sitting in Needs Review) instead of ever trusting `issue.EffectiveNumber()`, then
>   threading that one resolved value through every `FindByNumber` call site instead of each re-deriving
>   it. Two regression tests, one per root cause -
>   `PendingNumberProposal_StillResolvesTheRightIssue_ForPerIssueDetails` and
>   `AcceptedNumberProposal_NotIncludedOnTheLoadedIssue_StillResolvesTheRightIssue` (the realistic one,
>   matching what every actual user's library looks like) - both confirmed to fail without their
>   respective fix (manually reverted each in turn, reran red, restored, reran green).
>   **No rescan needed** - this only changes what the ComicVine *scrape* step reads, not
>   scanning/import, and the Number proposal this now finds was already sitting in the DB from the
>   original import. Any issue already scraped once under the old bug (recorded "Applied" but with only
>   Publisher/Series/Volume-year actually written) needs a plain **re-scrape**, not a rescan, to pick up
>   the fix and fill in the missing per-issue fields (Title/Summary/credits/dates/genres/...). An issue
>   whose filename genuinely had no parseable number (no proposal exists at all) still needs its number
>   typed in by hand, exactly as before - neither this fix nor a rescan invents a number from nothing.
> - **Detail screen's whole-series scrape panel never refreshed after finishing.** `MainViewModel`
>   wired `Detail.Tabs.ScraperPanelFactory` straight to `Scraper.CreateSeriesPanel` with no completion
>   callback at all - unlike the single-issue scrape action above it, which explicitly reloads. So even
>   once the number-resolution bug above is fixed, the Issues tab's tiles would still show stale data
>   until the user navigated away from Detail and back. Gave `CreateSeriesPanel` an optional
>   `onScraped` callback, wired to `Detail.ReloadCurrentSeries()`, matching the sibling action's
>   pattern. New test `TheSeriesPanel_InvokesOnScraped_OnceTheBatchFinishes`.
> - **Also found+fixed while investigating (a crash report the user pasted mid-session):**
>   `IssueDetailsApplier`'s genre-tag loop checked incoming genres against a `HashSet` snapshot taken
>   *before* the loop instead of updating it as tags were added, so a ComicVine response that repeats a
>   genre name (confirmed live: "Super-Hero" twice) wrote two duplicate `IssueTag` rows - no DB
>   constraint stops it. Didn't crash at scrape time; crashed later, the moment the user opened that
>   issue's Properties editor and hit Save (`ApplyTagRows`'s `ToDictionary` throwing on the duplicate
>   key). Fixed the root cause plus hardened both `ApplyTagRows` call sites (Issue Properties and
>   Reading List Properties - identical fragile pattern, no DB uniqueness constraint on either tag
>   table) so a pre-existing duplicate can never crash Save again.
> **Verified:** targeted suites green - Data.Tests ComicVine folder 174/174 (incl. the three new
> regression tests above), App.Tests `IssuePropertiesScreenViewModelTests`/`ScraperTests`/
> `ReadingListPropertiesScreenViewModelTests` all pass. Both `Paperbunkr.App`/`Paperbunkr.Data` rebuild
> with 0 errors. **Not verified on screen** - same standing computer-use constraint; the user's own
> live reports are what surfaced both bugs, so worth a real re-scrape to confirm before considering
> this fully closed.
> **Follow-up same session (2026-09-25, two-phase auto-choose - the user's own observation: "the CE
> comicvine scraper would keep on going till it's scraped all the matched comics [then] let you sort
> out the unmatched ones"):** verified directly against `scrapeengine.py`'s real `__scrape`/
> `__scrape_book` - with auto-choose on, CE never interrupts a batch to ask about a book its own
> automatcher (`automatcher.py`'s `find_series_ref`, also verified) couldn't confidently resolve. It
> silently appends that book to the end of its own worklist (`BookStatus("DELAYED")`) and keeps
> auto-matching everything else; only once the whole original batch has had its automatic shot does it
> loop back and interactively resolve whatever got set aside. Paperbunkr's earlier "manual-search
> fallback" (this same file, above) had modeled the wrong CE codepath - the always-immediate
> `BookStatus("UNSCRAPED")` retry, not the auto-scrape-specific deferred one - so it blocked on a
> dialog for the very first unmatched book instead of finishing the batch first.
> - Restructured `ScrapeOrchestrator.ScrapeAsync` into two passes when interactive + auto-choose +
>   a reviewer are all present: an `AutoMatching` pass over the whole batch (deferring instead of
>   falling through to a dialog on either failure mode - `find_series_ref` returns "no match" for
>   both an empty search and a failed cover-hash check, so both defer identically), then a `Reviewing`
>   pass over just what got deferred, doing a *fresh* search per CE (its automatcher and its later
>   interactive step are genuinely separate queries, not one cached and replayed for the other) with
>   no inter-book delay (`delayed_b` skips it in CE too - a dialog's own human-paced interaction
>   already spaces requests out). A single-phase run (non-interactive, or no reviewer) keeps its
>   exact old immediate-skip behavior - nothing to come back to would ever resolve a deferred book.
> - New `ScrapePhase` enum (`Single`/`AutoMatching`/`Reviewing`) threaded through `onProgress` so the
>   batch header can label the two passes separately - **grilling round: "Two labeled phases"
>   (Recommended)** - "Auto-matching 4 of 10" then, only if anything was deferred, "Reviewing 2 of 3"
>   with its own count, rather than CE's own single counter that would visibly shrink when the second
>   pass starts (looks like a glitch, not a phase change).
> - **Also fixed while investigating:** the `BookLabel` helper building the review dialogs' own title
>   text (and a second, separate copy in `ScrapeCoordinator` for the Activity Center job title/batch
>   header) still called the old, never-fixed `issue.EffectiveNumber()` directly - the user's own
>   screenshot showed "Red Hulk #" with no number even after the Pending-proposal fix landed, because
>   that fix only ever touched the *matching* logic, not these two separate display-label call sites.
>   Both now use the same resolved number as everything else; `ScrapeCoordinator`'s own copy was
>   deleted in favor of a new public `ScrapeOrchestrator.ResolveBookLabelAsync` one-off entry point.
> **Verified:** Data.Tests ComicVine folder 177/177 (3 new tests proving the defer-to-end ordering,
> per-phase progress counts, and that a genuinely single-phase run still never defers), App.Tests
> `ScraperTests`/`ScraperDialogViewModelTests`/`ScraperScrapeBatchHeaderViewModelTests` 70/70. Both
> projects rebuild with 0 errors. **A real lesson from a false start**, worth recording: a new
> `ScraperTests` test hung the test host indefinitely (not flaky - unconditional) because
> `ScrapeCoordinator`'s own cover-hash gate is *always* active (its constructor's `getCoverPath ??
> CoverThumbnailService.GetEffectiveCoverPath` fallback means `_getCoverPath is null` is never true
> for a real coordinator, test or production) - a fake issue with no real decoded cover fails that gate
> and defers into a genuine `ComicVineMatchReviewDialog` via the real `NativePluginModalHostViewModel`,
> which nothing in the test ever resolves. Fixed by testing with a zero-issue series (completes
> instantly via `ScrapeSeriesAsync`'s own "Nothing to scrape" early-out, never touching the modal host)
> rather than faking a passing cover-hash match. **Not verified on screen** - same standing constraint.
> **Follow-up same session (2026-09-25, the user's "extensive usage" observations, all verified against
> the CE plugin source and the user's own fork at `Downloads\comic-vine-scraper`):**
> - **Punctuation-sensitive search.** The scrape used ComicVine's `filter=name:` (an exact-substring match
>   capped at 25 results sorted by issue count) where CE uses the fuzzy `/search/?resources=volume` (100
>   results). Now `IComicVineClient.SearchVolumesFuzzyAsync` (default-implemented, so Metron falls back to
>   its own search), plus a port of CE's `__cleanup_search_terms` (`SearchTermCleaner`: lowercase, "&" to
>   "and", noise words, keep only `\w ' : . -`) and its one retry with digits/number words swapped.
> - **"Barely auto-matches".** CE has no score threshold: it takes the top-scored series and confirms by
>   comparing the book's cover with that *issue's own* cover (found by number; series art only as a
>   fallback). Ours compared against the series art (usually #1's cover), so any other issue failed the
>   gate. Rebuilt to match `find_series_ref`: the TPB-vs-#1 guard now runs only for #1/unnumbered books and
>   compares top vs 2nd/3rd series art (0.77); the issue lookup is cached per (volume, number) so gate and
>   apply share it. Alternate-cover review, formerly deferred pending ComicVine's `associated_images`
>   field confirmation, was implemented in Step 17 on 2026-10-26.
> - **Fork fields ported (each its own toggle, on by default; a saved settings row gets them turned on
>   once via `FieldSetVersion`):** Count (volume issue count - approved, not in CE or the fork),
>   Main character/team, Series group, Tags from concepts (`concept_credits`), and Story arc order =
>   StoryArcNumber + AlternateNumber + AlternateCount via `StoryArcOrderResolver` (ComicVine `story_arc`
>   issue list, sorted store date, cover date, natural number; cached per arc; optional
>   `arc_overrides.json` in the app data folder, same format as the fork). Correction to an earlier claim:
>   ComicVine *does* carry this data; CE's own plugin just never used it. Metron gets none of the arc
>   fields. `StoryArcNumber` is stored but the engine's `ComicInfo` has no such element, so it is not written
>   into `ComicInfo.xml` (AlternateNumber/AlternateCount/SeriesGroup/MainCharacterOrTeam/Count are).
>   `ScrapeByIdService` doesn't compute arc order (its own apply path) - the other new fields do apply there.
> **Verified:** Data.Tests ComicVine + external-id suites 209/209, App.Tests scraper/organize/write-back 86/86,
> Daemon scrape 9/9; not run against real ComicVine or seen on screen.

> **Manual session note (2026-09-20, Remote/server library sharing built on branch `feat/remote-library-sharing`; **MERGED to master 2026-09-21 (dcaef82, v0.6.9.0) and confirmed on-screen by the user; branch/worktree removed**):** design + plan in
> `docs/superpowers/specs/2026-09-19-remote-library-sharing-{design,plan}.md`; all phases P0-P5 implemented. **P0** `PAPERBUNKR_DATA_DIR` (`AppDataPaths`, ~17 paths routed through it).
> **P1** new `Paperbunkr.Sharing` (Kestrel HTTPS server, PBKDF2 password + session tokens + failed-auth lockout, DPAPI-protected self-signed ECDSA cert, paginated JSON
> catalog with ETag, pages/covers with Range and `?w=`) and `DbShareCatalogSource` + `ArchivePageSource` (Data/App). **P2** `RemoteSource` + `RemoteSourceId`/`RemoteIssueId`/
> `RemoteSeriesId` (migration `AddRemoteSources`, deliberate no-op `Down()`), pinned-cert `ShareClient`, `RemoteMirrorSync`, `RemoteRelinkReconciler`, and - the key safety
> decision, a change from the spec's per-site plan - a **global EF query filter** on Issue/Series so every existing job excludes remote rows *by default*; only the Library load,
> Detail load/selection, and the reader opt in (`includeRemote`), guarded by an allowlist test. **P3** `RemoteImageProvider`/`RemoteAccessorSession` feed the existing
> `ReaderImagePipeline` (no new reader), bounded `PeerPageCache` (2 GB LRU + 30-day TTL, scheduled sweep task), `RemotePageFetcher` (max 3 in flight, shared in-flight fetches),
> covers in their own dir. **P4** Preferences -> Sharing (host controls, saved libraries, inline trust/re-trust/relink/remove, mDNS "Find on this network" via
> `Makaretu.Dns.Multicast.New`), all reporting through the Activity Center. **P5** Windows Public-network warning.
> **Verified:** unit/integration tests against real in-process hosts (Sharing, Data, App suites - see the final run below), incl. a changed certificate never being accepted silently,
> Relink preserving reading progress, and reading real pages through the real reader pipeline. **NOT verified:** nothing has been run on-screen with two real instances
> (use `PAPERBUNKR_DATA_DIR` for the second); Windows Firewall prompt wording; mDNS across real machines. **Known gaps / follow-ups:** Books (EPUB/PDF) not shared; QuickRate on remote books refused; Detail tabs (tracker/external metadata) not gated. **2026-09-21 follow-ups done + tested:** Library source filter + "Remote" tile pill, Insights/Stats count remote reads (size figures stay local), read-only gating (Library command guards, reduced remote menus, Detail hero), content stamp so replaced books drop stale cached pages/covers - see design doc section 13.

> **Manual session note (2026-09-20, Cluster Library Manager into core — branch `feat/clm-into-core`, NOT merged):** all five phases implemented (shared template engine + one-time
> template upgrade, durable scrape-on-import with retry and a Needs-attention list, Prowlarr/qBittorrent moved to Connections, the ComicVine scraper and review dialogs in core, the organizer with
> profiles and undo, two off-by-default scheduled tasks, and the plugin refused at load with a "now built in" message). Design + plan: `docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-{design,plan}.md`.
> **Not verified** against a real ComicVine key, Prowlarr, qBittorrent, or on screen. Merge note: the main tree has an uncommitted spinner change touching the same Preferences views.

> **Manual session note (2026-09-21, Wanted screen redesign - on `master` working tree, uncommitted, unreleased):**
> Spec + plan: `docs/superpowers/specs/2026-09-21-wanted-screen-redesign-{design,plan}.md` (approved after a grilling pass and three visual-companion rounds).
> Built: five tabs -> **Queue / Series / Releases**. Queue is one full-width virtualized list of series groups (needs-attention groups open on their own, user choices persist for the session)
> with stage filter chips (Wanted / Upcoming / Has candidates / Downloading / Failed / Needs details) and live counts, sort (Attention / A-Z / Recently added), inline candidate expanders, a
> downloads strip showing speed and ETA (already published by `DownloadProgressEvent`, previously dropped by the UI), confirmed group bulk actions ("I have all" / "Remove all"; the
> mockup's "Request all" was dropped because everything queued is already requested), and in-place reconciling so live updates never jump the scroll. Series: "Track a series" moved to a flyout,
> new **Untrack** (cascades the series' wants), reloads no longer rebuild rows. Releases: cover-tile shelf, one week per page, custom month calendar with release/followed dots,
> and an **on-demand week fetch** (`PullListService.FetchRangeAsync` + range-scoped `Store`, `PullListSourceFactory`) for weeks outside the cached window (they last until the next scheduled
> refresh replaces the cache). Transient messages are now toasts; the inline status line is gone; one dismissible "automatic searching is off" banner. Right-click / Menu key / "..." share
> `WantedScreenViewModel : IContextMenuProvider`. No migrations. User wiki (`Getting-Comics-Automatically.md`) updated.
> **Verified:** full `App.Tests` (3258 pass), `Daemon.Tests` (197), the touched Data pull-list/acquisition tests (71); headless tests construct the real screen (proves the XAML weave), realize under 100
> of 3000 queue rows, and lay out a 150-tile week.
> **Verified on screen by the user (same day):** the Queue, the chips (now squircles - `PbRadiusChip`, applied app-wide to every former `CornerRadius="999"` chip/badge), the group-header hover fix,
> the Releases shelf and calendar popup, and the right-click menu (it offers only what applies: an already-wanted, followed tile offers just Hide). Covers: a series without one now borrows its first
> issue's, then (Metron) the weekly list's cache, in both the Queue and the Series tab. A tile's "Request" label was clipped at 124px and its button padding was tightened (not yet re-checked on screen).
> **Not verified:** compact width, contrast, and the on-demand week fetch against real Metron/ComicVine. `avalonia-pro-max/review-checklist` applied by reading. Known small gaps: calendar dots rely on
> colour plus an accessible name; the release tile grid isn't virtualized (Avalonia has no virtualizing wrap panel).

> **Manual session note (2026-09-20/21, Metron as a second comic database + weekly pull list - all merged to `master`, unreleased):**
> Specs: `docs/superpowers/specs/2026-09-20-metron-as-comicvine-alternative-design.md` and `...-weekly-pull-list-design.md` (each has a status section and follow-ups).
> Built, tested and merged: `ComicProvider` (ComicVine/Metron) per tracked series, want and catalog row (ids renamed `ExternalVolumeId`/`ExternalIssueId`; migration `AddComicProvider`);
> `MetronClient` behind `IComicProvider` (search, catalog, issue details, releases by store date) with its own 1-minute rate limit and a daily-quota tracker read from Metron's response headers
> (`MetronQuota`; background work leaves a 10% reserve); a source selector on the series Missing Issues panel, Wanted "Track a series" and the scraper (default under Organize & Scrape, per-run
> switch in the match dialog, provider-specific match memory); the **weekly pull list** (Wanted -> Releases tab: Metron store-date releases cached, promoted to Upcoming wants for followed series,
> hide/restore, publisher filter, Activity Center notice; ComicVine as the fallback source; migrations `AddWeeklyPullList`, `AddPullListReleaseHidden`, `AddReleaseListProvider`);
> arc requests follow a series' own source; `Issue.MetadataSource` (`AddIssueMetadataSource`) so a re-scrape starts on the right source; covers for Metron series; Wanted's Downloads list scrolls.
> **Verified:** Data (1417), Daemon (197) and the touched App test classes pass, including a migration test that carries Metron's cache over and caught an EF default-value bug. The user then tested everything live (real Metron, Prowlarr and qBittorrent, on screen), including the final gap-closing pass (arcs by source, the daily-quota tracker, `Issue.MetadataSource`, Metron covers, the ComicVine weekly list).
> **Known limits:** a Metron-tracked series is not matched by a ComicVine weekly list; ComicVine's list only looks up 40 publishers per refresh; Cloudflare-protected indexers need the user's own FlareSolverr. **Not done:** books - the Books section has no metadata source or acquisition path yet (not started).

> **Manual session note (2026-09-19/20, comic acquisition — Mylar-style want-list, slices 1-4 built on branch `feat/comic-acquisition-daemon`, NOT merged or released):**
> Design: `docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md`; plan and per-step status:
> `...-plan.md`. Built and unit-tested: DPAPI-encrypted `CredentialStore`; a shared, prioritized ComicVine
> rate-limit handler; the UI-free `Paperbunkr.Daemon` project (search loop, Prowlarr native JSON search,
> strict-then-sanitized query cascade, scoring/pack detection, category-locked qBittorrent client, grab/reject/retry/cancel,
> blocklist, download tracker, copy-then-import with CE-syntax rename template + ComicInfo.xml, drop-in close-out, opt-in
> auto-grab); watch-list/want-list entities + two additive migrations; the Acquisition Preferences section; the **Wanted**
> screen (Wanted with Downloads, Upcoming, Candidates, Series; virtualized, with cover thumbnails); "Missing Issues (n)" with covers on the
> series Detail screen; "Request missing" + "Follow this arc" on reading lists; a daily "Follow story arcs" scheduled task (off by default).
> **Verified:** `Daemon.Tests` (188) pass; `Data.Tests` 1168 pass / the same 6 pre-existing migration failures as untouched `master`;
> each new/touched `App.Tests` class passes when run on its own, and the headless tests construct each new view (proves the XAML
> weave) and prove 3000 rows realize under 100. **Not verified:** nothing has been run against a real Prowlarr, qBittorrent or
> ComicVine key, and nothing has been looked at on screen (review checklist applied by reading). **The full `App.Tests` suite
> cannot complete on `master` or this branch** (dispatcher thread-affinity crash), and running two headless classes in one
> filtered run can trip the same problem, so App evidence is per-class.
> **Still to do:** owned-trade coverage; a library right-click "Repack & Inject Metadata" action (backlog); virtualizing the series
> Missing Issues cards and the Series tab; an on-screen pass. Merging note: this file and `Paperbunkr-Roadmap.md` also
> have uncommitted edits from another session in the main working tree — expect a trivial merge.

> **Cosmetics pitch COMPLETED (2026-10-12, uncommitted):** spec `docs/superpowers/specs/2026-10-12-cosmetics-pitch-completion-design.md`. **#24** taskbar/tray badge
> (user-verified on screen): counts pending Activity Center alerts (the user rejected "wanted releases this week"), `ShowTaskbarBadge` + migration
> `AddShowTaskbarBadge`, `TaskbarBadgeService` (ITaskbarList3 overlay, re-sent on every refresh because Windows drops it), redrawn on skin change.
> **#23** page-dot hover preview gained a page number and a numbered placeholder (the thumbnail tooltip already existed). **#22** `ImageBackgroundMode.Ambient`:
> a gradient from the current page's thumbnail edges (`PageEdgeColors`, `ReaderScreenViewModel.Ambient.cs`), cross-faded by a `BrushTransition`.
> **#19** `LibraryViewMode.Bookshelf`: spines on shelf rows, built on the existing `VirtualizingVariableWrapPanel` (new `UseSpineWidths`) with one
> custom-rendered `SpineTile` per item, `ShelfBoards` behind, colour from `SpineColorCache` (cover thumbnail via `CoverPalette`, two decodes at a time).
> **#11** closed with no work (Slide/Crossfade already exist; paper-curl ruled out 2026-08-13). Deviations from the spec: the view-mode enum is stored as
> a *string* (no migration either way); #19 reuses the existing wrap panel instead of a bespoke control, so selection, context menu and keyboard come
> free; no continuous-mode throttle for Ambient (it samples an in-memory thumbnail, so it costs nothing); spreads use the current page only. **User-confirmed on screen 2026-10-12:** all of #19, #22, #23, #24 and the tray dot (#19 needed one fix: a Button centres its content, so `SpineTile` measures itself a full row height). **Known gap:**
> the alphabet rail and pinned group header in bookshelf mode
> are not wired (they only know the other grids). The fast App suite was not re-run after the Continuity-sources session merged.
>
> **Cosmetics pitch items 8–26 BUILT (2026-09-21, uncommitted; user reviewed it running and signed off the same day):** spec `…-cosmetics-pitch-2-design.md` + plan
> `…-cosmetics-pitch-2-plan.md`. Slice A (series status chip, unread glyph, placeholder covers, A–Z rail for grouped views), slice B (hero backdrop
> switch, per-series accent, arc-source brand mark), slice C (empty-state illustrations, menu icon audit, chart hover/contrast/donut sweep, Activity Center
> rings + grouping, skin-preview tile/chip, density presets). Most items already partly existed; the spec's "Implementation notes" lists what was actually
> added per item. Also: the tile multi-select checkbox is now opt-in (Ctrl/Shift+click selects). New `AppSettings` columns: `ShowSelectionCheckbox`,
> `HeroBackdrop`, `SeriesAccentColor`, `DensityPreset`. Verified by unit + headless render tests, then reviewed on screen by the user (spine, ring, checkbox,
> A-Z rail all iterated on from real screenshots). Chapter closed; the remaining pitch items are the reader/platform ones listed below. Not built from the pitch:
> #11/#22/#23 (reader), #19 (bookshelf), #24 (taskbar badge), #12/#27 (already shipped).
>
> **Cosmetics pitch items 1–7 BUILT (2026-09-21, uncommitted; user reviewed it running and signed off the same day - CLOSED):** spec
> `docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md` + plan `…-cosmetics-pitch-plan.md`. Binding spine + read-progress
> ring (one `TileCosmeticsOverlay` on the 4 Poster/Panorama cover templates), glow tiers (`ThemeService.BuildGlowRing`),
> Library Health severity chips, reading-list 2×2 cover mosaic, Events-timeline connector art, splash ambient dots; five new
> `AppSettings` columns in migration `AddCosmeticsPitchSettings`. Verified: unit + headless render tests, migration test,
> full App suite green on the slice-B tree (3275 passed). **Not verified:** how any of it looks on screen. The spec's
> "Implementation notes" lists where reality differed from the pitch (e.g. Library Health had no text severity chip to
> reskin; ring is bottom-centre and hover-only). Items 8–27 of the pitch (added 2026-09-21) are untouched.
>
> **Smart features pitch - designed and built 2026-10-06 (uncommitted, not seen in the running app).** Spec
> `docs/superpowers/specs/2026-10-06-smart-features-design.md` (its "Implementation notes" list every place the build differs),
> plan `…-smart-features-plan.md`. Pitch grew to 20 items; #9 and #12 dropped, #14/#15 (OCR, embeddings) out of scope. Built in five
> slices: **S1** smart-list template gallery (dialog, 11 templates), series progress fields (unread / read / days since last read),
> Library Health "Collection gaps" and "Metadata consistency" sections, shared `HealthFindingDismissal` table. **S2**
> `ReadingEvent.ActiveSeconds` from all three readers, reading pace and time-left estimates (Home, Detail, Library inspector), Insights
> "Drop-off watch", Home "Up next" ranked list, Wanted "Most likely to read" sort. **S3** relink suggestions for missing files
> ("Relink to this", "Relink all exact matches"), "Recommended" badge on duplicates. **S4** auto-apply confidence threshold with
> policy + slider controls, "Accept all at or above X%", synopsis genre suggestions (scheduled task, off by default). **S5**
> shared-character continuity suggestions and the "Also in" nudge, reading-list order vs story-event chronology check,
> `SeriesStatusChanged` event + refresh of a linked series marked completed, continuity / story-event completion with an Activity
> Center notification and the `ContinuityCompleted` plugin hook (**Plugin API 4.4**). Three migrations:
> `AddHealthFindingDismissals`, `AddReadingEventActiveSeconds`, `AddSmartFeaturesSettings`. Verified: unit tests for every resolver,
> headless render tests for the new rows and the gallery, migration tests, the fast App suite and the Data and Plugins suites.
> **Not verified:** anything on screen, in either theme; the provider refresh against a real provider; the plugin hook with a real plugin.
>
> **Manual session note (2026-09-18, 16 smart-feature + 7 cosmetic-feature pitch items recorded):**
> Not scoped, not brainstormed, not started — pure idea capture so they aren't lost. Full detail and
> rationale lives in `Paperbunkr-Roadmap.md`'s "Smart features pitch" and "Cosmetics pitch" sections
> (search those headings); one-line index here since this is the doc a human opens first.
> **Cosmetic pitch (7, from 2026-09-14):** series binding spine texture; read-progress ring on
> poster hover; skin-aware accent glow tiers (subtle/normal/vivid); Continuity/Event timeline
> connector art (deliberate CE deviation); Library Health traffic-light chip reskin; splash/startup
> ambient motion; Reading-list CBL 4-cover mosaic thumbnail. **Items 8–27 added 2026-09-21** (20 more,
> idea capture only; detail in the Roadmap's "Cosmetics pitch"): detail-hero backdrop blur; per-series
> dominant-color accent; series-status color language; reader page-turn feel; reader chrome auto-dim;
> empty-state illustrations; BrandMark consistency; live skin previews; Insights chart polish;
> density/typography presets; generated placeholder covers; bookshelf spine view; read-state glyph
> set; Activity Center visual polish; reader ambient letterbox; page-thumbnail scrubber; taskbar/tray
> badge; context-menu polish; slim scrollbars + A–Z letter rail; auto light/dark + OLED skin.
> **Comic reader pitch (30 items, 2026-09-21, comic reader only, idea capture; detail in the
> Roadmap's "Comic reader pitch"):** guided panel view; double-click smart zoom; event/reading-list
> context strip; pinned reference page; image adjustments/night mode; auto-crop; moiré-aware
> downscaling; optional upscaling; auto-skip tagged pages; auto-tag ads by page hash; report bad
> page; auto mark-read at story end; on-device OCR/translate; series-level reader defaults;
> end-of-issue card; jump-back chip; reader profiles; slider markers; command palette; session
> stats HUD; eye-care reminders; media keys/gamepad; pen/ink layer; copy/share; tap-zone editor;
> page notes/clips; two-edition compare; in-reader info panel; pre-open next issue; adaptive prefetch.
> **Insights pitch (10 items, 2026-09-21, idea capture; detail in the Roadmap's "Insights pitch"):**
> year-in-review recap; reading goals/challenges; reading rhythm (needs a new session-duration
> field); backlog burn-down (needs a nightly library snapshot); drop-off analysis; period-over-period
> deltas + sparklines; creator/publisher affinity linked to Wanted; storage/format breakdown;
> recommendations surface (UI for the backend-only Phase 6a engine); milestones timeline.
> **Reading List / CBL Manager pitch (8 items, 2026-09-28, idea capture; numbers kept from a 15-idea
> pitch, non-sequential — items 3, 8, 9, 11, 13–15 not selected; detail in the Roadmap's "Reading
> List / CBL Manager pitch"):** 1) GCD as a 7th arc source; 2) list folders; 4) per-item free-text
> notes (visible to remote-library viewers); 5) printable/exportable checklist; 6) per-list
> completion forecast (reuses Insights pace math); 7) cross-list duplicate/merge detector; 10)
> Continuity-order auto-build (uses the Continuity map's smart connector); 12) Story-Event diff
> against canonical CV/Metron order. CE-parity lookup owed for 1, 10, 12 before any spec.
> **→ Built 2026-09-28, uncommitted, not yet seen in the running app.** Grilled into two specs:
> `docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md` (A: #2, #5 + CSV export, #6, #7) and
> `…-build-from-events-design.md` (B: #10, #12, plus a grouping fix). The plan is `…-reading-lists-pitch-plan.md`, and each spec
> ends with "Implementation notes". CE-parity check done: CE has list folders (built, minus combine modes) and has none of #5,
> #6 or #7. #1 **dropped**: the GCD dump has only 582 arcs, and the extract has no arc tables. #4 **already existed**
> (`ReadingListItem.Notes`); remote viewers see no reading lists at all, so that part moves to the remote-sharing follow-ups.
> One new migration, `AddReadingListFolders`, which the user's DB applies on next launch. Verified with Data and App unit tests,
> headless XAML-load tests and headless renders.
> **→ Then fully redesigned 2026-09-28** (`docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md`, approach 2: new
> screen swapped in, old deleted; uncommitted, not yet seen in the running app): gallery home with Continue row and folder tiles, a
> cover rail, a streaming-style hero, a journey path with chapters and an Up next card, a Path | Covers toggle, the Checks strip,
> an explicit Edit mode (drag reorder, bulk bar, chapters) and an Add-issues drawer. One more migration: `AddReadingListViewMode`.
> **Home spotlight → accordion carousel, BUILT 2026-09-29** (`docs/superpowers/specs/2026-09-29-home-spotlight-accordion-design.md`,
> uncommitted): eight picks, one open at 400px (cover + details), the rest darkened slivers, animated by a custom `AccordionPanel`;
> replaces the two-layer crossfade/zoom/tilt/dots below. **Seen running by the user 2026-09-29** (accordion, hover tooltip, NEW
> ribbons, collages, Preferences › Appearance › Home tab) - no issues raised.
> **Home screen pitch → designed and BUILT 2026-09-28** (uncommitted, not yet seen in the running app): specs
> `docs/superpowers/specs/2026-09-28-home-cosmetics-design.md` (A) and `…-home-improvements-design.md` (B), plan
> `…-home-pitch-plan.md`. Home is now an ordered list of sections (reorder/hide in Preferences › Appearance › Home), with a
> time-of-day greeting, a light-theme sky, an opt-in seasonal flourish, a real two-layer spotlight crossfade + zoom, a tilted
> hero cover, parallax (restored on Detail too), scroll-reveal, one Continue Reading row for comics and books, a Needs Attention
> banner, one-per-series spotlight with new-arrival slots, and "Not interested" with Undo. One migration: `AddHomeCustomization`.
> Verified: Data.Tests 2026/2026, App.Tests for every touched area 866/866 (the full App run was stopped at 31 min, not
> repeated), and headless renders of the real screen in light/dark/night/hover. First on-screen look by the user 2026-09-29 led to
> three fixes (tilt strength, ribbon text contrast, night-sky wordmark). **Still to look at on screen:** light vs dark masthead, the crossfade/zoom on
> rotation, the tilt + hover, parallax on Home and a Detail screen, the NEW ribbon, the collage, the Because-You-Read lead card
> and ✕, the Needs Attention banner, and the Preferences Home tab (drag, ↑/↓, hide, Unhide).
> Original idea capture: **Home screen pitch (10 cosmetic + 5 improvement items, 2026-09-28, idea capture; detail in the
> Roadmap's "Home screen pitch"):** Cosmetics — spotlight ambient backdrop; time-of-day greeting;
> section header micro-icons; Continue Reading progress-bar restyle; "new" ribbon on Recently Added;
> Collections cover collage; spotlight rotation transition polish; Because-You-Read connector chip;
> Home hero parallax on scroll; seasonal accent flourish. Improvements — section reordering/
> visibility; unified "pick up where you left off" digest; top Needs Attention item on Home;
> Spotlight relevance tuning; Because-You-Read feedback control. Whole Home dashboard is itself a
> deliberate CE deviation, so no CE-parity lookup owed.
> **Insights pitch, decomposed 2026-09-22 into a 5-item queue** (#9 recommendations surface brought back
> into scope during the brainstorm, slotted last): 1) #6 period-over-period deltas + sparklines, 2) #4's
> nightly snapshot table (foundation), 3) #1 year-in-review recap, 4) #2 reading goals/challenges, 5) #9
> recommendations surface. Each gets its own brainstorm/design/plan cycle.
> **Slice 1 (#6) — built 2026-09-22** (docs/superpowers/specs/2026-09-22-insights-period-over-period-
> deltas-{design,plan}.md), uncommitted: delta badge + sparkline on the Stats tab's Finished · range/Pace/
> Avg to finish tiles, hidden on All time or when history doesn't reach back far enough for a full prior
> window; also fixed a real bug found in the same session where `AvgDaysToComplete` ignored the range
> selector entirely (averaged lifetime `RealSpan`s regardless of range). Verified: `StatsResolverTests`
> (25/25, includes 7 new trend/regression cases), `InsightsChartPolishTests`/`StatsScreenViewModelTests`
> (both scoped Insights/Stats filters, 32/32 and 40/40 across App.Tests/Data.Tests respectively) — not a
> full-suite run. **On-screen-checked by the user 2026-09-22:** confirmed the hidden-when-insufficient-
> history behavior is correct; the user's own library is too young for a full 90-day prior window yet, so
> the actual delta/sparkline rendering with real numbers won't be visible on their machine until roughly
> early 2027 — the "hidden" path is what's actually been eyeballed, not the populated one.
> **Slice 2 (#4's nightly snapshot table + Backlog burn-down card) — built 2026-09-22**
> (docs/superpowers/specs/2026-09-22-insights-backlog-burndown-{design,plan}.md), uncommitted: new
> `LibrarySnapshot` table (one row/day, comics-only local backlog counts, migration `AddLibrarySnapshots`
> scaffolded via the real `dotnet ef` CLI), `LibrarySnapshotService` upsert-by-date write side, wired as the
> `library-snapshot` scheduled task — the **first real use of the pre-existing but previously-unused
> `ScheduleMode.DailyAt`** (every other of the app's 14 scheduled tasks uses `Interval`). Read side:
> `StatsResolver.BurnDownData`/`ComputeBurnDown` (cleared → 7-day history gate → 30-row rolling linear
> regression, precedence order documented in the design doc), new Backlog burn-down card on the Stats tab
> with a solid ScottPlot line for real history plus a dashed projected-zero-crossing line (verified real API:
> `ScottPlot.Plottables.Scatter.LinePattern`/`ScottPlot.LinePattern.Dashed` in the installed 5.1.59).
> Verified: `dotnet build` clean (0 errors/warnings) on both `Paperbunkr.Data` and `Paperbunkr.App`; scoped
> test runs — `Paperbunkr.App.Tests` 68/68 (Insights/Stats/Scheduler/LibrarySnapshot filter),
> `Paperbunkr.Data.Tests` 47/47 (Insights/Stats/LibrarySnapshot filter) — not a full-suite run. **Not
> checked on screen**: like slice 1, the card needs real `LibrarySnapshot` history to accumulate (minimum 7
> daily rows) before there's anything to look at, so this is unverifiable visually for about a week from
> whenever it first starts running.
> **Slice 3 (#1 year-in-review recap) — built 2026-09-23**
> (docs/superpowers/specs/2026-09-23-insights-year-in-review-recap-{design,plan}.md), uncommitted: new
> "Recap" tab on the Insights screen (3-way tab selection alongside Overview/Stats), a calendar-year picker
> (years with any `ReadingEvent` history, newest first, current year labeled "so far"), a 9-slide paged
> narrative — items finished, pages read, longest streak (year-scoped, capped at the calendar-year boundary,
> not `StatsResolver`'s lifetime streak), busiest day (by pages), top series, top writer (comics `Writer` +
> novel `Author` combined), top artist, highest-rated series, most-reread item — computed by a new
> `RecapResolver` deliberately separate from `StatsResolver` (incompatible range models: calendar year vs.
> `InsightsRange`'s rolling windows). No new table, no new scheduled task — purely a read-side query over
> existing `ReadingEvent`/`Issue`/`Book` data. One-click PNG export of a purpose-built compact poster
> (`RecapPosterView`, distinct portrait layout from the in-app slides, both skin-reactive per this session's
> design review — not a fixed "wrapped" palette). **Real gotcha caught before implementation, not after**:
> the design originally assumed `RenderTargetBitmap.Render` works on a fully detached/unparented control;
> checking the actual Avalonia docs (`avalonia-docs` MCP) showed this needs a control genuinely attached to
> a visible window outside the headless test platform, so the poster is instead added to an always-present,
> zero-opacity `PosterExportHost` panel already inside `InsightsScreen`'s live visual tree for the duration
> of the render, then removed. Verified: `RecapResolverTests` (13/13, including a year-boundary case and a
> streak-does-not-cross-year-boundary case using `TimeZoneInfo.ConvertTimeToUtc` for precision regardless of
> the test machine's local zone), `RecapViewModelTests` (5/5), scoped `Insights|Stats|Recap` filter across
> both suites (38/38 App.Tests, 58/58 Data.Tests) — not a full-suite run. `dotnet build` clean on both
> `Paperbunkr.Data` and `Paperbunkr.App`, including a forced fresh compile of the brand-new `RecapPosterView`
> per this doc's own AVLN2000 build gotcha. **Not checked on screen**: the whole tab, year picker, slide
> paging, and PNG export are unverified visually — outstanding for the user to check.
> **Slice 4 (#2 reading goals & challenges) — built 2026-09-23**
> (docs/superpowers/specs/2026-09-23-insights-reading-goals-{design,plan}.md), uncommitted: first
> Insights-family slice with real persisted user state — new `ReadingGoal` table (migration
> `AddReadingGoals`), a `GoalResolver` computing progress + pace against it, goal cards (progress ring +
> status text) on the Overview tab, a "+ Add goal" create dialog (hosted the same way as every other "New X"
> dialog in this app — `controls:OverlayShell` at `MainWindow.axaml` level, not a screen-local popup), and
> live 50%/100% milestone alerts fired from `GoalsViewModel` the moment a `ReadingEvent` crosses a threshold.
> Goals can be scoped to the whole library, one series, one publisher, or one genre, and to "This year"/"This
> month"/a custom date range — no auto-renewal; a preset goal whose period ends just stays as history.
> **Two real corrections found before they shipped as bugs, not guessed at:** (1) the design's original plan
> to raise a persistent, per-goal-deduped `ActivityAlert` for behind-pace nudges from inside the new
> `goal-pace-check` scheduled task turned out to be impossible — a scheduled task's `RunAsync` body has no
> `IActivityService` of its own (no DI container in this app, confirmed via `ScheduledTaskCatalog.cs`'s own
> comment on the `StoryEventAutodetect` entry) — so behind-pace nudges instead follow every other task in
> this catalog's own shape: a plain completion-summary toast, not a dismissible alert card. (2) an early XAML
> draft nested one `ItemsControl`'s own `WrapPanel` inside another `WrapPanel` to combine the goal cards with
> a static "+ Add goal" tile in one flowing row — caught before it shipped: a nested `WrapPanel` gets measured
> at infinite width by its parent and never actually wraps internally, so the goal cards would have rendered
> as one unbounded-width row. Fixed by moving "+ Add goal" into the section header instead of the card flow.
> Verified: `GoalResolverTests` (11/11), `GoalsViewModelTests` (4/4), `AddReadingGoalsMigrationTests` (1/1),
> scoped `Insights|Stats|Recap|Goal` filter across both suites (42/42 App.Tests, 70/70 Data.Tests) — not a
> full-suite run. `dotnet build` clean on `Paperbunkr.Data` and `Paperbunkr.App`, including a forced fresh
> compile for the two brand-new views (`GoalEditorOverlay`, plus `GoalRing`/`GoalCardViewModel`'s wiring into
> the already-fresh-compiled `InsightsScreen`) per this doc's own AVLN2000 build gotcha.
> **On-screen-checked by the user 2026-09-23**: the goal creation flow works end to end (Track/Target/Period/
> Scope chips, Title auto-suggest, Create) and the resulting card renders correctly on the Overview tab (ring,
> "0 of 30 · on track" status text). **Real bug found + fixed during this on-screen pass, not before**: the
> Custom-range period's two date fields were originally `DatePicker` (three separate day/month/year spinner
> fields), which visibly collided/wrapped when squeezed into a half-width `Grid` column - screenshotted by the
> user. Swapped to `CalendarDatePicker` (compact single text field + calendar dropdown) fixed the visual
> collision, but its `SelectedDate` turned out to be `DateTime?` at runtime, not `DateTimeOffset?` as the
> Avalonia docs' own prose claimed (`CalendarDatePicker` wraps an internal `Calendar` control, which has
> always used `DateTime?` in both WPF and Avalonia) - a real `System.InvalidCastException` surfaced this
> immediately on first use, caught and fixed by retyping `GoalEditorViewModel.CustomStart`/`CustomEnd` to
> `DateTime?` (trusted the runtime exception over the docs' prose, verified rather than re-guessed). **Still
> not checked on screen**: watching the ring/status update live as a goal progresses, the 50%/100% milestone
> alerts, goal deletion, the `Series`/`Publisher`/`Genre` scope options, and the behind-pace scheduled task's
> actual toast.
> **Slice 5 (#9 recommendations surface) — built 2026-09-23 — closes the Insights pitch queue**
> (docs/superpowers/specs/2026-09-23-insights-recommendations-surface-{design,plan}.md), uncommitted.
> **Premise correction found before writing the design, not after**: the pitch's "backend-only, no UI"
> framing was stale — the Home screen's "Because You Read" module already ships a live UI for
> `RecommendationResolver`, seeded by recently-*opened* series (`HomeFeedResolver.GetRecentlyOpenedSeriesIds`),
> shipped 2026-08-23. The real surviving gap, matching the roadmap's actual literal wording (not the todo
> doc's own paraphrase): a distinct card on the Insights Overview tab seeded by recently-*finished* series,
> showing the `Explanation` text Home's `PosterTile` rows never display. New `InsightsRecommendationResolver`
> (no changes to `RecommendationResolver`/`HomeScreenViewModel`/`HomeScreen.axaml` at all) finds the most
> recently-finished series (falling through up to 5 candidates if needed), excludes anything already in
> Home's current picks by directly recomputing that same exclusion set from the identical public functions
> Home itself calls (no real cross-screen coupling), and reuses `SeriesCardSample.FromSeries`/`PosterTile`
> as-is for the actual tiles — no new entity, no migration, no scheduled task, the smallest of the 5 slices.
> Verified: `InsightsRecommendationResolverTests` (5/5, including a fall-through-after-exclusion case and an
> ordering case proving it doesn't just always pick the oldest finish), 1 new `InsightsScreenViewModelTests`
> case plus assertions added to its existing empty-library case, scoped `Insights|Stats|Recap|Goal|
> Recommendation` filter across both suites (43/43 App.Tests, 84/84 Data.Tests) — not a full-suite run.
> `dotnet build` clean on both projects. **Not checked on screen**: the section's actual placement/appearance
> between READING and Collection health, the explanation captions, click-to-Detail navigation, and whether it
> visibly avoids duplicating Home's current picks are all unverified visually — outstanding for the user to
> check. **This closes all 5 items of the 2026-09-22 decomposed Insights pitch queue** (slices 1-5, all built
> 2026-09-22/23, all uncommitted, all with on-screen verification still outstanding).
> **Insights pitch, remaining 5 items — BUILT 2026-10-12, uncommitted, user-verified on screen 2026-10-12** (design + plan:
> `docs/superpowers/specs/2026-10-12-insights-pitch-completion-{design,plan}.md`; one spec for all three slices at the user's request). This closes the
> whole 10-item Insights pitch. #3 reading rhythm (Trends › Activity: weekday × hour session heatmap, session-length buckets, typical session; counts sessions,
> skips backfilled rows, length uses only sessions with `ActiveSeconds`), #5 "Where you stop" (Trends › Composition: stopped-vs-finished rate by publisher/genre/type
> plus a stall-point histogram, same 21-day stalled rule and 95% read rule as the Today Drop-off watch, which is unchanged), #7 "You love these" (Trends › Top
> Lists: avg rating × completion via the normalized `CreatorCredit` index, ≥3 series and ≥3 rated issues; the pitch's "unowned works → Wanted" half is **dropped** —
> Wanted items carry no creator data and have no creator filter), #8 Storage (Composition: comics only, by file type and publisher, ten largest files, "duplicates
> could free up to X" with a Review-duplicates button into Library Health › Duplicates; books have no size column, so they show as a count), #10 Milestones strip
> (History tab: derived from the reading log, never stored, backfill excluded, earned + dimmed next-up badges, Hide remembered). **No migration**: the Hide switch
> is `insights.json` next to the database (another session was mid-migration on `AppSettings`). Verified: 27 new Data resolver tests, 10 view-model/prefs tests,
> 2 headless renders of the real screen with seeded data (PNGs in `.superpowers/pitch-render/`), plus the scoped Insights/Stats/History/Goal/Recap/ScreenInput/
> MainViewModel suites. **Still to look at on screen:** the rhythm grid on the real library (and that backfilled history leaves it sparse), the 21-day boundary,
> affinity ranking against real ratings, storage total vs Explorer, Review duplicates landing on Library Health › Duplicates, the milestone strip in light/dark,
> and that an import-only history shows the explanatory note instead of fake milestones.
> **Insights section redesign — built 2026-09-23** (design + plan:
> `docs/superpowers/specs/2026-09-23-insights-redesign-{design,plan}.md`), uncommitted. IA rework, not just a
> restyle: renamed the three tabs Overview/Stats/Recap -> **Today**/**Trends**/Recap to match what they'd
> organically become after the five slices above landed. **Today**: a new glow-tier hero row (goal
> progress + mono readout, continue-reading, top recommendation, all with real cover art via a new shared
> `CoverThumb` control wrapping `AsyncCoverImage` — not the older synchronous `CoverImageConverter`) replacing
> the old stacked Goals section + top of Reading; every remaining row that names a series (Almost Done, Dive
> In, secondary recommendations, Collection health gaps) also gained a cover thumbnail. **Trends**: the same
> ~15 existing cards regrouped under a pill sub-nav (Activity/Composition/Top Lists), pure display-side
> partition, no data change; glow deliberately excluded here (reserved for Today's actionable tiles).
> **Recap**: the three tiles that actually name a coverable item (Top Series, Highest Rated, Most Reread —
> not "Longest journey", which turned out to be Trends-only and got wrongly conflated with Recap during
> brainstorming) now show cover art too, both in-app and on the exported poster. Two design-doc assumptions
> corrected during implementation, not silently dropped: (1) goal/continue-reading hero-tile sparklines cut
> entirely — no pace-history data source exists without new resolver logic, which was out of scope; (2)
> `HighlightGroup` (shared by `StatsResolver`/`RecapResolver`) gained two optional `SeriesId`/`IssueId`
> fields so `RecapViewModel` can resolve a cover for a tied item — a narrow, additive exception to the
> design's "no resolver changes," left null everywhere the Trends tab constructs the type. Also fixed one
> real accessibility defect caught by the `avalonia-pro-max/review-checklist` self-review: hero tiles were
> marked `Focusable="True"` on the wrapping `Border` even where a real inner `Button` was the actual
> interactive element, creating an inert extra tab-stop — removed. **Verified:** `dotnet build` clean on
> `Paperbunkr.Data`/`Paperbunkr.App` (including a forced fresh compile for `CoverThumb`, the first new View,
> per this project's AVLN2000 gotcha); scoped `Insights|Stats|Recap|Goal|Recommendation|Trends` filter, 46/46
> `Paperbunkr.App.Tests` and 85/85 `Paperbunkr.Data.Tests` — not full-suite runs. **Not checked on screen**:
> the whole redesign is unverified visually — the hero row's layout/empty-state fallback, the Trends pill
> sub-nav (including whether ScottPlot charts redraw correctly after a group is hidden then reshown), and
> every cover-thumbnail placement, all outstanding for the user.
> **Correction (2026-09-23, same day, on-screen check by the user):** the Trends tab's "Score
> distribution" chart was found completely broken - `InsightsScreen.axaml.cs`'s `RenderCharts` never
> called any ratings-chart renderer at all, so the `RatingsChart` `AvaPlot` sat permanently on
> ScottPlot's default empty-axis range (-10 to 10) regardless of `StatsScreenViewModel.HasRatings`.
> Pre-existing bug, not introduced by this redesign - just never on-screen-checked closely enough
> before now. Fixed with a new `RenderRatings` method (same bar-chart shape as the existing
> `RenderPublicationYear`), wired into `RenderCharts`. Scoped test filter re-run clean (46/46
> `Paperbunkr.App.Tests`) after the fix; still needs the user's on-screen confirmation that the bars
> now actually render.
> **Two more corrections (2026-09-24, on-screen check continued):** (1) every Trends chart's mouse
> wheel hijacked the page scroll to zoom the individual chart instead - genuinely annoying while
> scrolling the tab. `AvaPlot.HandleMouseWheelEvent = false` alone does **not** fix this (that flag
> only controls whether the pointer-wheel event bubbles to the ScrollViewer, not whether ScottPlot's
> own zoom interaction fires) - confirmed by the user after a first attempted fix still zoomed. The
> real fix, taken from ScottPlot's own "Plot in a Scroll Viewer" Avalonia demo:
> `chart.UserInputProcessor.RemoveAll<ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom>()`
> on all five charts, alongside keeping `HandleMouseWheelEvent = false` so the event still bubbles.
> (2) The Recap tab's original one-slide-at-a-time card (prev/next, dots) left almost the entire tab
> empty on a real window - user pushback led to a second grilling round (visual companion, three
> layout options) and **replacing the whole slideshow with an always-visible 9-tile grid** (Items
> Finished spans 2 rows in a wider first column; the rest fill a 3-column grid), matching the density
> the exported poster already had. Removed now-dead `RecapViewModel` slide-paging surface entirely
> (`CurrentSlideIndex`, `Dots`/`SlideDot`, `NextSlideCommand`/`PreviousSlideCommand`,
> `CurrentSlideLabel`/`Value`/`CoverKey`) in favor of nine named `*Tile` accessors
> (`ItemsFinishedTile`, `TopSeriesTile`, etc.) bound directly from the grid - `RecapPosterView`'s
> export path is untouched, it already read `Tiles` directly rather than the slide index. Verified:
> `RecapViewModelTests` updated (removed the two now-meaningless slide-navigation tests, adjusted the
> rest to the named accessors), scoped filter 45/45 `Paperbunkr.App.Tests`; `dotnet build` clean.
> **Not yet checked on screen**: the new Recap grid layout, the mouse-wheel fix's actual behavior in
> the running app, and the poster export still matching (it should be untouched, but hasn't been
> re-exported and looked at since this change).
> **Library growth chart split (2026-09-24), the real fix behind the legend mess:** root-caused via
> ScottPlot's own source (`Plot.ShowLegend(Edge)` calls `Axes.AddPanel(...)` and returns a brand-new
> panel every call; neither `Plot.Clear()` nor `HideLegend()` ever removes a previous one) - every
> stack-by/range switch had been permanently stacking another legend panel at the chart's bottom edge
> all session long, which is what the "two different legend boxes" screenshot actually showed. Fixed
> that leak first (tracked + explicitly removed the panel each render), but the user then asked to
> split the chart up anyway rather than just fix the leak - **removed the Stack-by toggle (Reading
> state/Media type/Content rating) entirely**: `StatsScreenViewModel`'s `GrowthStackBy` enum,
> `GrowthStackByOption`, `SetGrowthStackByCommand` all deleted; `RenderLibraryGrowth` now plots one
> plain "Total" cumulative line with no legend at all (sidesteps the whole leak class structurally,
> not just patches it). Composition breakdowns aren't lost - **Content rating gained the same
> `CategoryDonut` treatment Reading state/Media type already had** (identical
> `IReadOnlyList<CompositionSlice>` shape, no new binding), so all three composition dimensions now have a
> consistent donut+list card instead of two donuts plus one bar-only list. Panel height reverted
> 160->240->160 (240 was compensating for the leak, not a real sizing need once the leak's gone).
> Verified: `dotnet build` clean (`Paperbunkr.Data` and `Paperbunkr.App`), scoped filter 52/52
> `Paperbunkr.App.Tests`; confirmed zero remaining `GrowthStackBy` references repo-wide. **Not yet
> checked on screen**: the single-line growth chart's actual appearance, and whether the new Content
> rating donut renders correctly with up to 9 categories (more than Reading state's 5 or Media type's
> handful - `CategoryDonut`'s slice-count ceiling, if it has one, hasn't been checked).
> **Two more requests (2026-09-24):** (1) Recap is now gated to a 3-day year-end window
> (Dec 29-31, local time) rather than visible year-round - it's a once-a-year "Wrapped" moment, not a
> daily-use tab. New `InsightsScreenViewModel.IsRecapAvailable` (backed by a pure, unit-tested
> `IsWithinYearEndWindow(DateTime)` static) hides the Recap tab button outside the window and guards
> `SelectRecapTabCommand` directly as a second line of defense. No persistence/timer for a mid-session
> midnight rollover - out of scope, extremely low-value edge case. (2) The Trends tab's "Library
> growth" chart panel was found genuinely broken on screen, not just tight: at its old fixed
> `Height="160"`, the chart's own legend (up to 5 reading-state/media-type/content-rating categories,
> wrapping to 2 rows in that column width) ate almost the entire panel, squeezing the actual plotted
> lines into a barely-visible sliver at the top. Bumped to `Height="240"` - the only other chart on
> this tab with a legend of comparable size. Verified: 7 new `InsightsScreenViewModelTests` cases
> (year-end window theory cases + command-guard cases), scoped filter 52/52 `Paperbunkr.App.Tests`;
> `dotnet build` clean. **Not yet checked on screen**: whether 240 is actually enough room for the
> Library growth legend + chart together, and that the Recap tab button correctly disappears/
> reappears (can't be verified live without changing the system clock).
> **Comic reader pitch, slice A "Flow & defaults" — built 2026-09-21 (uncommitted; on-screen check by the
> user outstanding):** series-level fit-mode/auto-rotate defaults (`Series.PageFitModeOverride`/
> `AutoRotateOverride`, migration `AddSeriesReaderDefaults`, "Apply to series" in the reader's fit flyout and
> drawer, clear on the Details Info sub-tab of both comic and manga detail screens), end-of-issue card (5 s
> auto-advance countdown when `AutoNavigateComics` is on), context strip (reading list, else Story Event),
> jump-back chip (`Reader.JumpBack`, Alt+Left, >5-page jumps from thumbnails/bookmarks), all on a shared
> `ReadingOrderResolver`. Verified by tests only, nothing viewed on screen: full `Paperbunkr.App.Tests` 3432 passed/0 failed and full
> `Paperbunkr.Data.Tests` 1493 passed/0 failed (both with `--blame`). An earlier plain full run of each aborted with
> "Test host process crashed" (App after 3068 passing, Data with no detail) and did not reproduce; a clean-HEAD Data run
> also passed, so treat it as an intermittent host crash of unknown cause. Not built: #16 (needs the Cosmetics scrubber), #19 (slice F). Slices B–H not started.
> **Comic reader pitch, slice B "Page intelligence" — built 2026-09-25 (uncommitted; on-screen check by the user
> outstanding):** spec `docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md` (deviations in its
> "Implementation notes"), plan `docs/superpowers/specs/2026-09-25-comic-reader-page-intelligence-plan.md`. CE read-percentage fix (`IssueMetadataExtensions`:
> `(LastPageRead+1)*100/PageCount`, so short issues reach 100%; reader's Finished threshold aligned); #6 page skipping
> (`SkipDeletedPages` on / `SkipAdvertisementPages` off, migration `AddPageSkipSettings`, `PageSkipStepper`, "Skipped N pages"
> hint); #13 story-end finish (last non-ad/non-deleted page writes the last page as `LastPageRead`); #12 bad-page report
> (`PageReport`, migration `AddPageReports`, `X` / page menu → reason picker → undo chip, Preferences → Library Health
> "Reported pages": open / tag Deleted / dismiss); #9 ad-page detection (`AdPageHash`/`PageHash`/`AdPageProposal`, migration
> `AddAdPageDetection`, dHash `PageHasher`, `AdPageDetectionService`, reader-seeded ad library, "Detect advertisement pages"
> scheduled task off by default, Needs Review "Advertisement Pages" section with Accept/Reject all). All three migrations have
> a no-op `Down()`. Verified by tests, **nothing viewed on screen**. Full `Paperbunkr.App.Tests` (1 h 1 m, machine shared with other sessions): 3572 passed, 13 failed; 12 of the 13 (PDF book source, remote library/sharing, two Preferences cover-cache confirms) passed when rerun alone, so they are load flakes; `WantedQueueTests.GroupsFollowTheSort...` fails alone and sits in another session's uncommitted Wanted work. Full `Paperbunkr.Data.Tests`: non-Migration 1549 passed/1 failed (`RemoteRowIsolationTests.OptInSites_AreExplicit...` flags `RecapResolver.cs`, another session's new `includeRemote` site that needs an allowlist entry), Migration 114/114. Still needing a look: the
> skipped-pages hint, the reason picker and undo chip, the Library Health list, the Preferences toggles and the Needs Review
> section (thumbnail loading included) all need a look. Slices C–H not started.
> **Comic reader pitch, slice G "Performance" — built 2026-09-25 (uncommitted; on-screen check by the user outstanding):** spec
> `docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md`, plan `...-plan.md`, deviations in the spec's "Implementation notes". #24 was mostly
> already shipped (adaptive fringe), so it became the continuous-scroll page-boundary fix the user reported: overlay metrics (boundary handler, decode latency,
> blank frames, layout shift, position-save time; once-per-page cache hit/miss), thumbnail selection replaces at most 2 items instead of up to 200, the debounced
> position save writes off the UI thread (single-flight, sequence-guarded), `ScrollAnchor` scroll anchoring for page sizes arriving above the viewport,
> `ScrollWindowHint` (visible range + direction) with distance-ordered decode and a 100 ms fringe throttle during sustained scroll. #23: `NextIssueStager` opens the
> next issue in the background in the last 3 pages (paged and continuous, forward only, local files), `Load` adopts it, discard rules (adopted / other issue /
> below the last 5 pages / 2 min idle / file stamp changed / setting off / GoBack), `AppSettings.PreOpenNextIssue` (migration `AddPreOpenNextIssue`, no-op `Down()`,
> Preferences → Reader). Verified by unit/service/view-model tests only; **not felt or viewed on screen** (steady-pace continuous scroll with the overlay open; a real
> solid `.cbr` read to its last pages, then Continue).
> **Needs Review → Library Health — built 2026-09-25 (uncommitted; on-screen check by the user outstanding):** spec
> `docs/superpowers/specs/2026-09-25-needs-review-into-library-health-design.md` (committed e242af6; no plan file, the user said "implement it"). All five
> review queues (Content Type, Duplicate Files, Series Conflicts, Metadata Proposals, Ad Pages) moved out of the CE migration overlay into the Library Health card in
> Preferences → Library, in the spec's order, each with an "all clear" empty state. The overlay lost its Needs Review tab (Results button is now "Review in Library
> Health"). `MainViewModel.NeedsReview` owns the one `NeedsReviewViewModel`; Preferences hosts it. Metadata Proposals split into Pending (shown) and Applied
> (collapsed "Applied · N", not counted as pending); `PendingCount` drives a "Needs review · N" chip on the card header and a dot on the Library nav item.
> `ActivityLinkKind.MigrationReview` kept but routes to Library Health; the duplicate-files alert now links `Preferences`/"LibraryHealth". Beyond the spec: the
> Applied list binds to `ExpandedAppliedProposalItems` (empty until expanded), because a collapsed ItemsControl still realizes every row and a big library has
> thousands of auto-applied proposals. Bulk actions (added after the user pointed out none had been): Duplicate Files "Dismiss All" (alongside the existing "Keep
> Largest in All Groups"); Metadata Proposals and Ad Pages "Accept All" / "Reject All" behind the inline two-step confirm (pending only - Applied proposals stay
> per-row, since rejecting one reverts a written field; Series-field accepts and series-scoped rejects go one by one through `ResolveProposal` on fresh contexts);
> Content Type has no bulk action because each series needs its own answer. **Applied proposals (2026-09-26, after the user hit 1,814 of them):**
> per-row Accept on an applied proposal used to be a durable no-op (already `Accepted`, so it came straight back), so `MetadataProposal.ReviewedAt` (migration
> `AddMetadataProposalReviewedAt`, real `Down()`) marks "looked at and kept"; an explicit accept sets it, auto-applied ones leave it null. The Applied list is now
> `AppliedProposalGroups` - one bucket per field + source (+ provider) from a single `GROUP BY`, rows built only on expand (latest 100), each with Accept All
> (one `UPDATE`) and Reject All (two-step confirm; series-scoped rows restore the earlier field value one by one), plus "Accept All" / "Reject All Applied" on the
> section header. Tests: `NeedsReviewViewModelTests` (+9), `AddMetadataProposalReviewedAtMigrationTests`, all 131 Data migration tests pass.
> **Library Health sub-tabs + faster refresh (2026-09-26, uncommitted, not viewed on screen):** spec
> `docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md` (deviations in its "Implementation notes"). The card is now Overview / Review / Files
> sub-tabs with ten collapsible sections (`LibraryHealthSections`), every list a `VirtualList` (code-only control: `VirtualizingStackPanel` in a scroll region up to 420 px,
> since `ItemsRepeater` is unsupported in Avalonia 12), the last tab remembered (`AppSettings.LibraryHealthTab`, migration `AddLibraryHealthTab`, no-op `Down()`), ten
> new search entries and deep-link payloads (`LibraryHealth/Files`, `LibraryHealth/Review/Duplicates`; the duplicate, missing-files and path-repair alerts use them).
> Pending proposals are bucketed like Applied ones (`ProposalGroupViewModel`, latest 500 rows). Speed: Content Type is a direct series query (was a smart-list scan of every
> issue), the duplicate scan loads a slim projection instead of tracked entities with `Include(Series)` (algorithm untouched), and `RefreshAsync` does the database work
> off the UI thread with per-queue `Refresh(Queue)` after actions. Measured on a seeded 3,650-issue library: full refresh 500-880 ms -> 53-70 ms, UI-thread time for
> the async refresh 0.1-3.6 ms, a 1,000-row list builds about 7-25 rows instead of 1,000. Tests: `VirtualListTests`, `AddLibraryHealthTabMigrationTests`, +~20 in
> `NeedsReviewViewModelTests` / `PreferencesScreenViewModelTests` / `MainViewModelTests`. **Nothing viewed on screen** - tab switching, section open/close, inner-list scrolling and
> scroll chaining over a long list, every deep link, a search hit into a hidden tab, both themes. Full `Paperbunkr.App.Tests` not run (affected classes only). Verified: `Paperbunkr.App` builds 0 errors; the affected classes (`NeedsReviewViewModelTests`, `DuplicateFilesReviewTests`,
> `MainViewModelTests`, `PreferencesScreenViewModelTests`, `DetectAdPagesTaskTests`) 302 passed, plus 1 `VerifyCoversCommand…` load flake that passes alone; the full
> suite was not run. **Nothing viewed on screen** - the section order, chip, nav dot, Applied toggle, the migration overlay without tabs and the Results-button
> deep-link all need a look. Not done: the Pending proposals list is neither capped nor virtualized (it was not before either, but it now sits on a page that is always
> built); search-index entries (`PreferenceIndex`) for the new subsections.
> **Comic reader pitch, slice F (F1 Reach, F2 Profiles, F3 Comfort) — built 2026-09-25 (uncommitted; on-screen checks by the user outstanding):** specs
> `docs/superpowers/specs/2026-09-25-comic-reader-{reach,profiles,comfort}-design.md`, plan `...-slice-f-plan.md`, deviations in each spec's "Implementation notes". **F1:** right/middle clicks no longer turn
> pages (pointer-button policy), PageDown/Space/media-next and PageUp/Shift+Space/media-previous reading-order turns (clickers work), mouse side buttons, Mihon-style tap-zone layouts for paged and continuous
> mode including the mouse (`TapZoneResolver`, `TapZoneOverlay`, Preferences preview), a Ctrl+K command palette with go-to-page (Ctrl+G), an XInput gamepad; migration `AddReaderInputSettings`. **F2:** named
> reader profiles on the `Workspace` table (`WorkspaceScreen.Reader`), layered `AppSettings` -> profile -> series -> issue, a per-series pointer and a default, a session-only live switch (hotkey P, drawer,
> palette), capture from the live reader, three built-ins, Preferences management; migration `AddReaderProfiles`. **F3:** reading stats chip (H), eye-rest nudge toasts with snooze, a schedule-based warm tint
> (W), Ctrl+C copy of the page or stitched spread and Save Spread As; migration `AddReaderComfortSettings`. Verified by unit/service/view-model tests only; **not viewed or felt on screen** (media keys delivery, the
> Xbox controller, mouse clicks with a non-default layout, the palette, the profile flyout, the chip, the tint, pasting a copied page/spread). *(User-verified on screen 2026-09-25/26 after fixes: the
> Ctrl+Shift+P overlay's double toggle, the stats chip moved to the bottom-right.)*
> **Comic reader pitch, slice D "Panels & zoom" — built 2026-09-26 (uncommitted; on-screen checks by the user outstanding):** spec `docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md`, plan
> `...-plan.md`, deviations in the spec's "Implementation notes". **Smooth zoom:** one 25%-400% range for paged and continuous mode (100% = fit), a logarithmic slider with 100% in the middle, proportional wheel
> and key steps, the five preset buttons gone (only "Fit (100%)" remains); the PDF reader keeps 100%-400% via a new canvas `MinZoomLevel`. **#1 guided panel view** (`G`): a local gutter-based panel detector
> (`PanelDetector`, no ML), next/previous step panel by panel with an eased zoom and pan, landing on the next page's first (or previous page's last) panel, "Panel 3/7" label, paged single-page only, settings
> `GuidedViewOnOpen` + profile field. **#14 smart double-click zoom:** double-click/tap zooms to the panel under the pointer, second one returns, falls back to 200%; in guided view it toggles panel/whole page;
> `SmartDoubleClickZoom` (on). Fixes a pre-existing flaw: the first click of a double-click turned the page before the zoom, now undone. **Tuning aid:** palette "Show detected panels". Migration
> `AddGuidedViewSettings`. Verified by unit, view-model and headless canvas tests only (synthetic pages); **the detector on real comics/manga, the eased steps and the log slider are not viewed or felt.**
> **Guided view detection upgrade — built 2026-09-29 (uncommitted; on-screen check by the user outstanding):** spec `docs/superpowers/specs/2026-09-28-guided-view-detection-upgrade-design.md` (with measured
> Results), plan `...-plan.md`. The slice-D gutter detector worked on about 14% of a 624-page sample of the user's library (`tools/Paperbunkr.PanelHarness`, not in the sln). Now: `PanelDetectionService` runs
> the bundled **ONNX model** first (`PanelModel/panel-detector.onnx`, YOLO26-nano by Leandro Narosky, Apache-2.0, trained on Manga109-s; credited in THIRD-PARTY-NOTICES.md; `Microsoft.ML.OnnxRuntime`, CPU),
> then the gutter heuristic (now analysed at 1000 px, finer min gutter) when the model finds nothing or fails to load, then the whole page. `OnnxPanelDetector` adds borderless bands the model misses;
> `PanelOrdering` owns reading order; tall webtoon strips take `PanelDetector.DetectStrip` (cuts at full-width empty bands). Palette: "Report bad panel detection" saves the page + detection JSON to
> `panel-reports` next to the database. **Not done:** performance (about 300 ms per cold page on CPU, run on the UI thread by `EnsurePanels`; deferred by the user), the heuristic's adaptive-background/skew
> upgrades (not needed once the model is primary), on-screen use. Numbers and caveats are in the spec's Results.
> **Comic reader pitch, slice C "Image quality" — built 2026-09-26 (uncommitted; on-screen checks by the user outstanding):** spec `docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md`, plan `...-plan.md`,
> deviations in the spec's "Implementation notes". **#2** turned out mostly built (sliders, warm tint): the new work is CE's **auto levels** (per-page black/white points, true levels stretch, baked into the display bitmap
> in the reader pipeline, `PageId.Variant` in the cache identity) and **sharpen 0-3** (CE's kernel, a live paint-level convolution), on the usual layers (Preferences default, per-issue override, reader profile, palette, drawer
> ADJUST). **#3 auto-crop margins** (Paperbunkr-original): `PageCropDetector` on a small luminance copy, crop applied in the pipeline before the downscale (detail tier reuses it; strips and thumbnails never cropped), off by
> default, per-page override table `PageCropOverrides` (Follow / Never / Always from the page menu and palette), "Show crop" toast. **#25** was measured first: the pipeline downscale was fine; the colour-filter draw path sampled
> nearest-neighbour (about twice the shimmer), now mipmapped/cubic. Migration `AddImageQualitySettings`. Verified by unit, pipeline and view-model tests on synthetic pages plus one real page; **how it looks on real scans is not viewed.**
> **Comic reader pitch, slice E "Info & compare" — built 2026-09-26 (uncommitted; on-screen checks by the user outstanding):** two specs, `docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md` (E1) and
> `...-compare-design.md` (E2), deviations in each one's "Implementation notes". **E1:** **#28** a read-only info panel (`I`; summary hidden behind "Show summary", Preferences default off), **#29** a pinned reference page (`Shift+P`;
> a scaled copy, drag to a corner, three sizes, survives moving to the next issue), **#7** page notes (one per page, marker on the page dot) and region clips (`Ctrl+Shift+C`, PNGs under `annotations\clips`, caption/copy/save/delete)
> with a Markdown export; migration `AddPageNotesAndClips`; a fix that made `CaptureOverlay` hit-testable. **E2:** **#11** a Compare screen (side by side or flicker, dHash page pairing with an offset, linked zoom/pan, facts strip with
> badges, Keep A / Keep B through a confirmation, Not a duplicate), from the Library menu (two selected) and each duplicate group; the duplicate delete was extracted into `DuplicateGroupResolver`. Verified by unit, view-model and headless
> tests on synthetic pages; **how it looks and feels, and pairing quality on real editions, are not viewed.**
> **Smart-feature pitch (16 total — 7 from 2026-09-14, 9 added 2026-09-18):** Smart Lists v2 preset
> gallery; Continuity auto-suggest via shared-character MediaRelation data; Reading-order conflict
> detector (CBL order vs. StoryEvent chronology); auto-match missing files against series-identity
> scan; scheduled cover-refresh on tracker-status change; Insights-driven auto smart-lists; Plugin
> API on-continuity-complete hook; **cheap/local —** library gap detection (missing-issue-number
> analysis), reading-integrity health scan (low-res/duplicate/blank pages, extends
> `LibraryHealthService`), drop-off detection, smart "Up Next" blended queue, reading-stats
> dashboard refinements (check against shipped Insights/Stats v2 first), best-scan dedup heuristic
> (extends Duplicate Finder); **medium/local-ML —** full-text dialogue OCR search, semantic
> search over descriptions/covers (local embeddings), auto-tagging with confidence (feeds the
> existing `MetadataProposal` review queue).
> Every one of these needs its own brainstorm → design spec per this project's `CLAUDE.md`
> workflow before any code gets written — none are scheduled.

> **Manual session note (2026-09-18, series/event name matching, empty-row cleanup, Reader
> Save-Page-As + Cover Picker, external-metadata extraction spec):** Beta-backlog work, P0–P7
> unchanged. None of this is on `paperbunkr-todo.md`/`Paperbunkr-Roadmap.md` until this note — it shipped
> across the prior few days without the hand-update this file's own standing rule requires.
> - **Series/event name matching + empty rows:** design+plan
>   `docs/superpowers/specs/2026-09-17-series-name-matching-and-empty-row-cleanup-{design,plan}.md`.
>   New `TitleNormalizer` (CE `StripDown`/`NamesMatch` cascade) wired into `LibraryFolderScanner`,
>   `ReadingListMatcher`, `StoryArcGroupingResolver`, `StoryEventResolver`, `ContinuityResolver` —
>   punctuation/volume-variant series names now resolve to the same `Series` instead of spawning a
>   duplicate. New `Issue.IsContentEmpty`/`EmptyRowAcknowledged` columns + `LibraryHealthService`
>   corrupt-file/0-page detection. New "Find Similar Series" (extracted `SeriesMergeHelper`) and
>   "Empty Rows" sections added to Library Health. Own bug found and fixed same day: the cascade fix's
>   own collision case crashed `ArcReadingListBuilder` on a duplicate key
>   (`src/Paperbunkr.Data/ReadingLists/ArcReadingListBuilder.cs`). Verified: `Paperbunkr.Data.Tests`
>   name-matching/cascade subset 54/54 green. `Paperbunkr.App.Tests` subset not run this session —
>   the app was open locally and held `bin/` locked; on-screen verification also still outstanding.
> - **Reader "Save Page As" + Cover Picker:** design+plan
>   `docs/superpowers/specs/2026-09-17-reader-save-page-and-cover-picker-{design,plan}.md`. New
>   `PageExportService` (PNG/JPEG export via the reader's right-click menu — comic/manga reader only;
>   Books/PDF has no context-menu-provider infra yet, deliberately out of v1 scope, a fast-follow).
>   New 3-tab `CoverPickerViewModel`/`CoverPickerView` (Series / Reading List / Browse File
>   candidates) now backs all 3 existing "change cover" entry points (Detail, Manga Detail,
>   DetailTabs), replacing the old direct-file-picker-only path. Not GUI-verified this session.
> - **External Metadata Full Extraction — IMPLEMENTED (status corrected 2026-09-19; this note
>   originally said "design + plan only, not started", which went stale):**
>   `docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-{design,plan}.md`. Expands
>   the AniList/MangaBaka/MangaDex providers past title/description/status: cover images, creator/
>   staff, publication year/format, demographic, cross-references, and categorized tags feeding real
>   `IssueTag` import. MangaBaka moved from beta `v2` to stable `v1`. Landed in `fbe7295` (merged
>   via PR #89). Verified 2026-09-19 by reading the code (grep, not built/run): A1–A4 (schema,
>   AniList/MangaDex/MangaBaka v1), B (cover pipeline: `TrySetCustomCoverFromBytes`,
>   `IMultiCoverProvider`, `ProviderCoverCandidateCache`, picker provider tab), C (`Series.Creator`
>   + resolver wiring), D (`MergeFromCategorized`, `ExternalTagImportResolver`), E1–E3
>   (`ExternalMediaRelation`, `IRelationsProvider` on AniList + MangaBaka, placeholder→
>   `MediaRelation` auto-upgrade), E4 view-model side (lazy fetch + `ExternalRelationPlaceholders`),
>   F1 (`UpsertCrossReferences`). User confirmed on screen that the Linking tab auto-links
>   cross-referenced providers and shows per-provider cover actions. **Open:** (1) no "Not in
>   library" badge string found in the Related-tab view — check on screen; (2) no dedicated tests
>   found for the AniList/MangaDex normalizer fields or `ExternalTagImportResolver` (plan asked
>   for them); (3) stale `v2` doc comment at `MangaBakaMetadataProvider.cs:23`.
> **Not committed as of this note** — all three items above sat staged/uncommitted in the working
> tree when written. *(Update 2026-09-19: since landed in `fbe7295`, merged via PR #89.)*

> **Manual session note (2026-09-17, IsFinalIssue migration-rollback bug actually fixed):**
> Closes the task spawned 2026-09-12 (`Fix migration rollback: IsFinalIssue NOT NULL bug`, noted
> further down this file). P0–P7 unchanged; this is `Paperbunkr.Data.Tests` infrastructure, not a
> feature. Unrelated to the theme-system work in progress on this branch — no theme/skin files
> touched.
> `Paperbunkr.Data.Tests` had 7 failing migration tests (`AddFb2MobiBookFormat`, `AddWorkspaces`,
> `AddCoverAspectRatio`, `AddBookReaderErgonomicsAndAnnotations`, `AddLastContentTypeSweepUtc`,
> `AddBooksBrowseState`, `LibraryDetailsColumns`), all `SqliteException 19: NOT NULL constraint
> failed: ef_temp_Issues.IsFinalIssue` — the same root cause as the 2026-09-12 note below, but this
> time across **7 distinct stale Designer.cs snapshots**, not just one. **Correction:** the fix that
> session's note describes was never actually committed (`git log --all` on
> `UnifyLibrarySortGroupFields.Designer.cs` showed only its original scaffold commit) — it must have
> been made in an uncommitted worktree and lost. Patched `IsFinalIssue` from `bool` to `bool?` (with
> an inline comment) in all 7 target snapshots, found by reading each failing test's own
> `PriorMigration` constant rather than assuming.
> That fix alone took 7 failures down to 6 different ones — `SqliteException 1: no such column:
> "LibraryGroupField"`, the exact failure mode this file's own 2026-09-06 note below already
> diagnosed and (thought it had) closed. Root cause this time: `AddCosmeticThumbnailToggles`
> (2026-09-13, see that session's note above) and `AddConfirmBeforeClose` (2026-09-14) both used a
> real per-column `DropColumn` on `Down()`, which — like every prior instance of this bug — silently
> drops the orphaned `LibraryGroupField`/`LibrarySortField`/`LibrarySortDirection` columns via
> SQLite's full-table-rebuild, breaking any earlier `Down()` step in the same rollback whose target
> snapshot predates `UnifyLibrarySortGroupFields`. The 2026-09-13 note's claim that real `DropColumn`
> was "the current convention" was the mistake — `AddNavRailHoverExpandEnabled`'s no-op is. Fixed by
> making both migrations' `Down()` no-ops (matching that established convention) and updating
> `AddCosmeticThumbnailTogglesMigrationTests` (the only test exercising it) to assert the 5 columns
> persist as orphans instead of asserting they're dropped. `AddConfirmBeforeClose` has no dedicated
> migration test.
> **Verified:** full `Paperbunkr.Data.Tests` suite 951/951 green (was 944/951, 7 failing, before this
> session). One unrelated flake (`ReworkBookPositionAnchorMigrationTests`, `FOREIGN KEY constraint
> failed`) surfaced once under full-suite parallel execution but passed cleanly in isolation —
> pre-existing order-dependent flake, not caused by this fix. **Standing implication recorded in
> memory:** any future migration doing a real (non-no-op) `DropColumn`/`AlterColumn` on `AppSettings`
> or `Issues` reintroduces one of these two bug classes for whatever migration test happens to roll
> back across it once HEAD moves further ahead — the no-op-`Down()` convention on those two tables
> is load-bearing, not stylistic. Uncommitted at session end (10 files: 7 Designer.cs patches, 2
> migration `Down()` fixes, 1 test update) — worktree `pensive-einstein-c920fb`.

> **Manual session note (2026-09-13, cosmetic Preferences micro-toggles shipped):** Beta-backlog
> work, P0–P7 unchanged. Design + plan: `docs/superpowers/specs/2026-09-13-preferences-cosmetic-
> toggles-{design,plan}.md`. Closes the last item in "Preferences: Behavior / CE-parity toggle
> remainder" (`docs/Paperbunkr-Roadmap.md`) — previously flagged "low value, revisit only on
> request," now built after CE-source research revealed 3 of the 6 named toggles (`DogEarThumbnails`,
> `ShowToolTips`, `NumericRatingThumbnails`) are real rendering features, not plain checkboxes.
> Shipped: `FadeInThumbnails` (opacity fade on genuine cover decode via `AsyncCoverImage`, never on a
> cache-hit repaint), `DogEarThumbnails` (real hover/selected second-page peek — found and used
> `PageDecodeCore.DecodeSinglePage` instead of the design doc's original `ReaderImagePipeline`
> suggestion, which would've meant a full background-threaded reader session per tile hover),
> `ShowToolTips` (a custom `Popup` mirroring the Activity Center peek-popover's entrance pattern),
> `NumericRatingThumbnails` (hover-reveal badge sharing the tile's corner with the selection checkbox,
> hidden whenever any issue is selected), `ExportedListsContainFilenames` (`CblReadingListIO` now
> populates the already-ported-but-unused `ComicReadingListItem.FileName` from `Issue.FilePath`).
> `CoverThumbnailsSameSize` shipped no code — confirmed already fully expressed by the existing
> PosterGrid/Panorama view-mode split. Verified: migration round-trip (real per-column `DropColumn`
> on `Down()`, matching the post-2026-09-06 convention, not the older no-op-`Down()` pattern found
> still present in one sibling migration), ~35 new/extended targeted test cases across
> `Paperbunkr.Data.Tests`/`Paperbunkr.App.Tests` all green, no regressions (the pre-existing
> unrelated `TwoStepConfirm` delete-bug test failure reproduced again, not caused by this work).
> **Not done:** on-screen verification of all 4 visual behaviors — standing no-computer-use caveat,
> weighted more heavily than usual since this batch is unusually visual/interactive.

> **Manual session note (2026-09-13, open-a-comic-on-launch shipped):** Beta-backlog work, P0–P7
> unchanged. Design + plan: `docs/superpowers/specs/2026-09-13-open-file-on-launch-{design,plan}.md`.
> Closes the `AddToLibraryOnOpen` prerequisite gap noted below in "Preferences: Behavior / CE-parity
> toggle remainder" (`docs/Paperbunkr-Roadmap.md`) — `App.axaml.cs` now recognizes a bare supported
> file-path CLI argument (the shape Windows' file-association launch produces) via new
> `NavigationCliArgs.TryParseFilePathArg`, and dispatches to new `MainViewModel.OpenFilePath`: already-
> in-library path opens the existing Issue directly (mirrors CE's `Storage.FindItemByFile`); a new
> path is always imported via the existing `LibraryFolderScanner.ImportNewFilesAsync` then opened.
> Per the design doc's explicit scope decision, the `AddToLibraryOnOpen` toggle itself is dropped
> (not built) rather than added as a Preferences checkbox — a real transient/non-persisted reading
> mode (true CE parity for the toggle's OFF state) was decided out of scope as disproportionate to
> what the roadmap flagged as a small gap-filler. Verified: `Paperbunkr.App` + `Paperbunkr.App.Tests`
> build clean; new `NavigationCliArgsFilePathTests` (5 cases) and 3 new `MainViewModelTests` pass,
> plus the full 66-case `MainViewModelTests` suite re-run clean (no regressions). **Not done:**
> on-screen verification of an actual file-association double-click launch, both cold-start and
> while already running (standing no-computer-use caveat this session).

> **Manual session note (2026-09-16, open-on-launch deadlock fixed):** User-reported bug: launching
> via file association only opened the app, never the file. Root cause confirmed by repro, not
> guessed — launched a Debug build directly with a bare file-path arg (same shape Windows' own
> association command line produces) and watched `startup.log`: the process reached
> `MainWindow.Show() returned` and then hung forever, never logging `initial screen loaded`.
> `MainViewModel.OpenFilePath` (`src/Paperbunkr.App/ViewModels/MainViewModel.cs`) calls
> `LibraryFolderScanner.ImportNewFilesAsync(...).GetAwaiter().GetResult()` synchronously on the UI
> thread; `ImportNewFilesAsync` (`src/Paperbunkr.App/Services/LibraryFolderScanner.cs`) awaits
> `Task.Run(...)` without `ConfigureAwait(false)`, so its continuation tries to resume on the
> captured Avalonia UI `SynchronizationContext` — the same thread already blocked on
> `.GetResult()`. Classic sync-over-async deadlock, 100% reproducible, not timing-dependent. Fixed
> by adding `.ConfigureAwait(false)` to that one `await`; every other caller of
> `ImportNewFilesAsync` (`DragImportService`, `LiveFolderWatchService`) already uses a real `await`
> and is unaffected. Verified: re-ran the exact repro against the rebuilt exe — reaches
> `initial screen loaded` in ~5s instead of hanging; `Paperbunkr.App.Tests` `LibraryFolderScanner*`
> suite (51 cases) still green. **Not done:** on-screen verification of the actual reader screen
> opening (confirmed via log timing only, not a visual check) and the real Explorer double-click /
> registry path (registry-registration code itself wasn't touched and wasn't re-audited this
> session) — standing no-computer-use caveat.

> **Manual session note (2026-09-16, Books added to file association):** Beta-backlog work, P0–P7
> unchanged. User asked to add EPUB "and the other formats" to Preferences > Advanced's file-
> association list, which only ever covered comic-engine formats (`Providers.Readers`) - Books
> (epub/fb2/mobi/azw/azw3) live in a fully separate schema/scanner
> (`src/Paperbunkr.App/Services/BookFolderScanService.cs`) with no prior association surface at
> all. Grilled first (real design decisions, not a pure UI tweak): user chose to include the
> `.fb2.zip` case (accepting that it means claiming bare `.zip` too - Windows can't key an
> association off a compound extension, so this necessarily contends with the comic engine's own
> pre-existing "ZIP Archive" row for the same extension if both are ever toggled on - documented in
> `FileAssociationService.BookFormats`' doc comment and the installer checkbox description rather
> than silently hidden), to leave `.pdf` comic-only (no second row fighting the existing PDF
> association), to wire real open-on-launch support for the new formats in this same pass (not just
> a cosmetic toggle - required so enabling them doesn't reproduce the deadlock above for epub/fb2/
> mobi), and to add matching installer per-format checkboxes.
> Added: `FileAssociationService.BookFormats`/`BookAssociationExtensions` (EPUB / FB2 (+.zip) /
> Kindle-MOBI groups) alongside the existing comic list, `GetAvailableFormats()` now returns both,
> `SetAssociated` resolves either; `SetBookAssociationsFor` mirrors `SetComicAssociationsFor` for
> the installer/CLI path (`Program.cs` now calls both Set*AssociationsFor with the same requested
> extension set - each ignores what it doesn't own). `BookFolderScanService` gained a public
> `ImportNewFilesAsync` (refactored `ScanAll`'s inline classification+import into shared
> `ClassifyFormat`/`ImportFiles` helpers, `BookFolderScanResult` now also carries `AddedBookIds`) -
> written with `.ConfigureAwait(false)` from the start, learning directly from the deadlock note
> above rather than repeating it. `MainViewModel.OpenBookFilePath` mirrors `OpenFilePath` against
> the Book schema; `NavigationCliArgs.TryParseBookFilePathArg` mirrors `TryParseFilePathArg` against
> the Books extension set (deliberately excludes `.zip`/`.pdf` - a bare `.zip` argument stays routed
> through the existing comic pipeline; there's no way to tell at that layer whether it's really an
> `.fb2.zip` file, a known, accepted limitation of associating a compound extension at all).
> `App.axaml.cs`'s open-on-launch dispatch now checks the Book path between the comic path and
> `--open`. `installer/Installer.iss` gained matching `associateepub`/`associatefb2`/`associatemobi`
> tasks + `[Run]`/`[UninstallRun]` wiring (not yet build/run - Inno Setup script, no dotnet build to
> verify it against).
> Verified: `Paperbunkr.App` builds clean; new/extended tests (`FileAssociationServiceTests` Book-
> scoping cases, `NavigationCliArgsBookFilePathTests`, `MainViewModelTests` `OpenBookFilePath_*`) all
> pass, full targeted re-run (`FileAssociationServiceTests`+`NavigationCliArgs*`+`MainViewModelTests`
> +`BookFolderScannerTests`+`LibraryFolderScannerTests`, 174 cases) green, no regressions. Repro'd
> the exact deadlock-avoidance end-to-end, not just at the unit level (the XUnit test harness does
> not reproduce the UI-thread deadlock class - the pre-fix comic version of this same call shape
> passed its own unit tests despite the real bug, which is why yesterday's bug shipped at all):
> built a real minimal EPUB by hand (PowerShell + `System.IO.Compression`, same shape as
> `EpubFixture`), launched the rebuilt Debug exe with it as a bare CLI arg, confirmed `startup.log`
> reaches `initial screen loaded` in ~4s with no hang. **Not done:** on-screen verification of the
> Preferences list actually rendering the 3 new rows and their toggles actually writing/reading the
> registry correctly, the real Explorer double-click path, and building/running the updated
> installer script — standing no-computer-use caveat, and Inno Setup wasn't invoked this session.

> **Manual session note (2026-09-16, Book reader drawer-vs-WebView airspace bug fixed):** User
> reported every Book reader drawer/sheet (TOC/Bookmarks/Highlights/Search/Font+Theme) renders with
> only its header sliver visible on v0.6.0-beta, real content covered by the page — confirmed by
> user as reproducing every time, for every drawer, regardless of which one, and that it did NOT
> happen right after the reader was first rebuilt (2026-09-02) — a real regression, not an inherent
> limitation. Root cause, confirmed via Avalonia's own docs (not guessed): the drawers' `Popup
> ShouldUseOverlayLayer="False"` (meant to force a real separate top-level OS window able to beat
> the already-documented WebView "airspace" problem) is silently overridden app-wide by
> `Program.cs`'s `Win32PlatformOptions.OverlayPopups = true` (added 2026-09-10 for an unrelated
> ComboBox-dropdown freeze fix) — Avalonia's own docs: "OverlayPopups: Embeds popups to the window
> when set to true," a platform-wide override with no per-popup opt-out. So every book-reader
> overlay has actually been in-process/overlay-layer-rendered since 2026-09-10 regardless of its own
> `ShouldUseOverlayLayer` value, which loses to `NativeWebView`'s native child HWND (Chromium host)
> the same as any plain in-tree overlay would. Exactly matches the timeline the user described. Only
> the reader's top/bottom chrome bars were ever safe from this, since they use a reserved `Margin`
> instead (no overlap, nothing to lose). Fix applies that same "don't overlap" principle to the
> WebView itself: `ReaderWebView.IsVisible` now binds to `!IsAnyDrawerOpen`
> (`src/Paperbunkr.App/Views/BookReaderScreen.axaml`), hiding the native control entirely while any
> drawer/sheet is open instead of trying to out-z-order it. Also fixed a second, separate bug found
> while wiring this: `IsAnyDrawerOpen` is a computed property, not its own `[ObservableProperty]`,
> and none of the 5 `[ObservableProperty]` flags it reads (`IsTocOpen`/`IsFontSheetOpen`/
> `IsBookmarksOpen`/`IsHighlightsOpen`/`IsSearchOpen`) had
> `[NotifyPropertyChangedFor(nameof(IsAnyDrawerOpen))]` — so nothing bound to `IsAnyDrawerOpen`
> (this new binding included) would ever have reacted to a drawer opening/closing without also
> adding that attribute to all 5 (`src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs`).
> Corrected 4 now-stale/misleading doc comments claiming `ShouldUseOverlayLayer="False"` alone
> solves the airspace problem in this app (`ReaderListDrawer.axaml`, `ReaderSettingsSheet.axaml`,
> and two spots in `BookReaderScreen.axaml`), and flagged the highlight color/note popup
> (`IsHighlightPopupOpen`) as a very likely same-class latent bug, deliberately NOT fixed the same
> way (hiding the whole WebView would hide the very selection it's anchored next to — needs its own
> design call, not a copy-paste of the drawer fix). Diagnosis note: an attempted live repro via the
> project's own FlaUI/UIA3 UI-automation harness (`Paperbunkr.App.UiTests`) was abandoned mid-
> session at the user's explicit objection — automation wasn't reproducing the real interaction
> reliably and was burning time without permission to do so (memory saved:
> `feedback_no_unauthorized_ui_automation.md`); the fix itself was reached from the user's
> screenshot + description + static code reading alone, and the scratch test file was deleted.
> **Verified:** `Paperbunkr.App` builds clean; the 5 pre-existing `BookReaderScreenViewModelTests`
> failures in this area were confirmed pre-existing on unmodified master (stashed this change,
> re-ran, same 5 failures) — not caused by this fix; **user confirmed on-screen** the TOC drawer now
> renders correctly (clean styled list, no longer blocked) and that the reading text reappears
> immediately once the drawer closes (no regression) — the only follow-up (page fully hidden rather
> than dimmed while a drawer is open) was confirmed as acceptable, not a bug. **Not done:** the
> highlight popup fix (deliberately out of scope this pass, see above).

> **Manual session note (2026-09-16, Book reader drawers rebuilt on real Windows, replacing the
> WebView-hide workaround above):** User rejected the WebView-hide fix immediately above once they
> saw it - it stopped the drawer from being blocked, but lost the original UX (text dimly visible
> *behind* the drawer, not fully blanked), which the reader genuinely had right after being first
> built. Root cause (from the prior note) stands: `Win32PlatformOptions.OverlayPopups = true`
> collapses every `Popup` into the in-window overlay layer, so no `Popup`-hosted drawer can ever
> render above `NativeWebView`'s native child HWND while both exist - there is no `ShouldUseOverlayLayer`-only
> fix available. The only way to restore the original look is for the drawer to stop being a
> `Popup` at all: `Window` creation is NOT subject to `OverlayPopups` (that option only affects
> Popups per Avalonia's own docs), and Windows guarantees an *owned* window renders above its owner
> - including the owner's native child HWNDs.
> Rebuilt `ReaderListDrawer` (TOC/Bookmarks/Highlights/Search) and `ReaderSettingsSheet` (Font
> sheet) to host their scrim+panel content in a real, separately-owned `OverlayHostWindow`
> (`src/Paperbunkr.App/Views/OverlayHostWindow.cs`, new - borderless, transparent, `WindowDecorations.None`,
> `ShowInTaskbar=False`) managed by a new shared `OverlayWindowController`
> (`src/Paperbunkr.App/Views/OverlayWindowController.cs`) instead of a `Popup`. The controller:
> creates/shows the window (`Show(owner)`) when `IsOpen` flips true, closes it when false; tracks
> the host screen's own root Grid (`OverlayReference`) via `PointToScreen`/`Bounds` and re-syncs the
> window's `Position`/`Width`/`Height` on the reference's own `LayoutUpdated` and the owner window's
> `PositionChanged`, so it stays aligned across resize/maximize/move while open. Both `.axaml` files
> lost their `<Popup>` wrapper (the scrim+panel Grid is now the file's real content, given `x:Name="OverlayRoot"`);
> both `.axaml.cs` files detach that Grid from their own `Content` in the constructor (so it never
> renders inline) and forward `IsOpenProperty` changes to the controller. `ReaderSettingsSheet.axaml`'s
> local `Border.miniCard`/`TextBlock.cardTitle`/`TextBlock.fieldLabel` styles moved from
> `UserControl.Styles` to `Grid.Styles` on `OverlayRoot` itself, since Avalonia resolves local
> styles by walking up from an element's *current* logical parent - once reparented into the new
> Window, the original UserControl stops being an ancestor and would no longer supply them.
> `BookReaderScreen.axaml`'s `ReaderWebView.IsVisible` binding from the superseded fix was reverted
> (WebView stays visible throughout - the whole point of this rebuild). Added
> `OnDetachedFromVisualTree` overrides on both controls that force-close any open overlay window -
> belt-and-braces against orphaned floating windows if the reader screen itself is torn down
> (navigated away from) while a drawer is left open, since these windows are no longer anchored
> inside `BookReaderScreen`'s own visual tree the way a `Popup` was.
> **Verified:** full solution builds clean (0 errors). Re-ran the same targeted test suites as the
> superseded note plus `PdfPageReaderScreenViewModelTests` - the same 5 pre-existing
> `BookReaderScreenViewModelTests` failures plus one more pre-existing failure
> (`PdfPageReaderScreenViewModelTests.DeleteCapture_RemovesTheFileAndTheRow`, confirmed via the same
> stash/re-run comparison to fail identically on unmodified master - a real file leftover under
> `%AppData%\Paperbunkr`, unrelated to this change) - no new failures. **Not verified:** this is a
> materially bigger change than the superseded fix (real window creation/positioning/lifecycle, not
> a single binding), and it has NOT been checked on-screen at all. Specifically unverified: the
> drawer actually renders above the WebView with text visible behind it (the whole point); position/
> size tracking holds when the main window is moved, resized, or maximized/restored while a drawer
> is open; closing via scrim-click still works when hosted in a real window; no orphaned floating
> windows after closing the reader or navigating away with a drawer left open; the Font sheet's
> `Grid.Styles` move didn't break its mini-card visuals. User needs to check all of this for real
> before this is considered done.

> **Manual session note (2026-09-16, Reading Lists "Find & link" fixed):** User reported "find and
> link" in reading lists don't work, with a screenshot: clicking "Find & link" on a missing item
> shows the `LinkingBannerText` ("Linking X #Y — pick a result below, or Cancel") but no search box
> or results appear under it - nothing to click, the feature does nothing observable. Root cause:
> `ReadingScreenViewModel.StartLink` (`src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs`)
> sets `LinkingRow` (which drives the banner via `IsLinking`) but never sets `IsAddIssuesOpen` -
> the actual search TextBox/results/Add-or-Link-button panel in `ReadingScreen.axaml` is gated on
> that separate flag, normally only flipped by the unrelated "+ Add issues" button. One-line fix:
> `StartLink` now also sets `IsAddIssuesOpen = true`. **Verified:** `Paperbunkr.App` builds clean;
> added a missed assertion (`Assert.True(vm.IsAddIssuesOpen)`) to the existing
> `StartLinkThenAddIssue_RelinksTheTargetedRow_InsteadOfAppending` test right after
> `row.LinkCommand.Execute(null)` - that test already drove the relink logic end-to-end via direct
> command calls but never checked whether the panel a real user would need to click into was
> actually open, which is exactly why this shipped unnoticed; full `ReadingScreenViewModelTests`
> suite re-run (44 cases) shows only the same 2 pre-existing `Delete_*` failures, confirmed
> pre-existing via stash/re-run against unmodified master, unrelated to this fix. **Not done:**
> on-screen click-through (diagnosed from the user's own screenshot + code reading, not re-verified
> visually after the fix).

> **Manual session note (2026-09-16, comic reader chrome auto-hide toggle added):** User reported
> the comic reader's floating chrome (Navigate/View/Page-turn/Actions clusters,
> `ReaderScreenViewModel.ShowChrome`) doesn't fade while reading - distracting - and asked for a
> toggleable auto-hide option in reader settings. Confirmed by reading the code: the idle-fade was
> already there (`docs/superpowers/specs/2026-08-25-reader-chrome-design.md`) but hardcoded always-on
> with no way to disable it, and it's genuinely sensitive to ANY pointer movement over the reading
> canvas restarting its 3s countdown (`OnReaderPointerMoved` → `NotifyCursorActivity` →
> `RestartOverlayAutoHideTimer`, unconditional, no throttling) - plausible real-world "never hides"
> for anyone whose hand rests near the mouse. Checked CE parity per standing rule before adding a
> field: no real precedent - CE's `ExtendedSettings.AutoHideCursorDuration` is a different, narrower
> feature (OS cursor hiding, not the chrome toolbar) and isn't exposed as an on/off checkbox in CE's
> own Settings UI either, so this is a clean Paperbunkr-original addition, not a parity gap.
> Added `AppSettings.ReaderAutoHideChrome` (bool, default true - matches the previous hardcoded
> behavior), a new EF migration (`AddReaderAutoHideChrome`), a "Auto-hide toolbar when idle" toggle
> in Preferences > Reader (mirrors `HighQualityPageDisplay`'s exact row/binding/persist shape), and
> gated `ReaderScreenViewModel.RestartOverlayAutoHideTimer` behind it (loaded once in `Load()`,
> same read-once-at-load pattern as `HighQualityPageDisplay`/`MouseWheelSpeed` - not a live in-reader
> toggle). Real bug caught and fixed while scaffolding the migration: EF's own tool defaulted the
> new column's SQL-level `DEFAULT` to `false`, contradicting the C# property's `true` default - an
> existing user's AppSettings row upgrading through this migration would have silently gotten
> auto-hide OFF instead of the "on" behavior they already had. Fixed by hand
> (`defaultValue: true` in the migration's `Up()`); a dedicated test
> (`AddReaderAutoHideChromeMigrationTests.Migration_ColumnDefault_IsTrue_NotFalse`) checks the raw
> `pragma_table_info` column default directly, since a fresh-row test can't catch this class of bug
> (`GetOrCreateAppSettings()` always writes the C# default explicitly regardless of the SQL default).
> **Verified:** full solution builds clean (0 errors/warnings); new tests pass in isolation
> (`AddReaderAutoHideChromeMigrationTests` ×2, `PreferencesScreenViewModelTests`
> `*ReaderAutoHideChrome*` ×2) - `Paperbunkr.Data.Tests` full suite (953 cases) shows 8 pre-existing
> failures, none touching this migration (migration-rollback-chain issues + one unrelated Insights
> test, matching this project's known pre-existing migration-chain flake); `Paperbunkr.App.Tests`
> full suite showed additional failures but all in an unrelated cover-cache/verify-covers area, and
> re-confirmed passing in isolation (matching the documented "full-suite-only" headless flake).
> **Not verified:** on-screen - never actually watched the chrome fade/stay-visible in the running
> app (standing no-computer-use caveat); no timer-elapse test exists for this mechanism at all (not
> even for the pre-existing Book reader equivalent), consistent with this codebase's established
> practice of not unit-testing real `DispatcherTimer` elapsing, but means the actual fade-or-not
> behavior itself is unverified beyond code reading.

> **Manual session note (2026-09-16, real cause of the comic reader chrome never fading found and
> fixed - the toggle above was necessary but not sufficient):** User reported the new toggle from
> the note above still didn't fade chrome, on multiple fresh comics, ruling out the "setting only
> re-reads on `Load()`" explanation. Found the actual root cause on a closer read of
> `ReaderScreen.axaml`'s own styles, entirely unrelated to anything built this session: a later
> style rule silently overrides the idle-fade's opacity regardless of the `.hidden` pseudo-class.
> `Border.chromeCluster.hidden` (line ~141, ships 2026-08-25 with the chrome system itself) sets
> `Opacity="0"`; `Border.floatingPanel.chromeCluster, Border.floatingPanel.readerDrawer` (line ~217,
> the 2026-09-14 "frosted glass" translucent restyle) sets `Opacity="0.92"` on the SAME element (every
> chrome cluster carries both `floatingPanel` and `chromeCluster` classes). Both selectors match 2
> classes each - equal specificity in Avalonia's cascade, which breaks ties by source order, and the
> frosted-glass rule comes later - so it always won, permanently pinning every cluster's opacity to
> 0.92 regardless of `ShowChrome`/`.hidden`. The idle-fade has been silently dead since the frosted-
> glass restyle shipped (2026-09-14), roughly 2 days after the chrome system itself - the toggle
> added earlier this session was real and correctly wired, but had nothing to actually gate: the
> visual effect it was supposed to enable/disable couldn't render either way. Fix: widened
> `.chromeCluster.hidden`'s selector to `Border.floatingPanel.chromeCluster.hidden` (3 classes),
> making it strictly more specific than the frosted-glass rule so it wins outright rather than
> depending on file order - narrows nothing, every real usage already carries all three classes.
> Checked the thumbnail rail's own separate hide mechanism (`Border.railOverlay`/`.railOverlay.hidden`)
> and the drawer's (`IsVisible` binding, not opacity) for the same class of bug - neither is affected
> (rail never combines with `.floatingPanel`; the drawer's visibility mechanism is unrelated to
> Opacity entirely). **Verified:** `Paperbunkr.App` builds clean. **Not verified:** on-screen - same
> standing no-computer-use caveat; this is a plausible, well-evidenced fix (a real, provable
> Avalonia-cascade tie explaining the exact symptom across multiple comics) but genuinely unconfirmed
> visually. User needs to check the actual fade this time, not just the toggle's own persistence.

> **Manual session note (2026-09-16, the fix immediately above was itself wrong - corrected):** User
> confirmed still no fade after a genuine rebuilt-and-restarted dev-build test (ruled out both a
> stale setting and a stale/wrong exe - asked directly). The previous fix's diagnosis of the
> conflict was right (two class-selector rules on the same element, one setting Opacity 0, one
> 0.92) but the ASSUMED resolution mechanism was wrong: it widened `.chromeCluster.hidden` to 3
> classes assuming "more classes = more specific = wins," modeling Avalonia's cascade on CSS. Pulled
> Avalonia's own docs directly rather than continuing to guess: "Two selectors with any conditional
> activation will have equal priority regardless of the number of activators present... Avalonia
> doesn't have CSS's concept of Specificity" (style classes count as conditional selectors) - so
> both rules sit at the identical `StyleTrigger` `BindingPriority` tier no matter how many classes
> either lists, both live in the same `UserControl.Styles` collection (identical visual-tree
> locality too), leaving exactly one tiebreaker: `Styles` collection order, last-declared wins. The
> widened selector was still positioned BEFORE the frosted-glass rule in the file, so it kept
> losing regardless of its extra class - the fix needed to be a reorder, not a widen. Reverted the
> selector back to plain `Border.chromeCluster.hidden` and moved its declaration to AFTER the
> frosted-glass rule (`ReaderScreen.axaml`, end of `UserControl.Styles`) instead - the only thing
> that actually decides the tie per Avalonia's own stated rules. Also checked for a LocalValue
> override that would trump both regardless of ordering (an inline `Opacity=` on the Border itself)
> - none of the 4 real chrome-cluster `Border` declarations set one. **Verified:** `Paperbunkr.App`
> builds clean. **Not verified:** on-screen, same standing caveat - but this time the mechanism is
> confirmed from Avalonia's own documented precedence rules (quoted directly, not inferred from
> CSS-adjacent assumptions), not just "plausible." If this still doesn't fade, the next place to
> look is whether `ShowChrome` itself is really flipping to `false` at all (i.e. whether
> `RestartOverlayAutoHideTimer`'s `DispatcherTimer` genuinely ticks) rather than another styling
> conflict - that mechanism has never been directly verified end-to-end, in this session or before.

> **Manual session note (2026-09-16, comic reader chrome made per-cluster hover-reveal):** The idle-
> fade fix above worked - user confirmed - then asked for it to be "more reactive... only when I try
> to hover above each item." Grilled 3 quick questions before touching code (scope: all 4 corner
> clusters vs. finer-grained; trigger zone: each cluster's own corner vs. a full edge strip; whether
> the old "any movement reveals everything" behavior should stay alongside the new per-corner hover
> or be replaced) - user chose the narrowest, cleanest option each time: 4 clusters, each cluster's
> own corner region, full replacement of the ambient reveal.
> Added 4 new `ReaderScreenViewModel` bools (`IsNavigateClusterHovered`/`IsActionsClusterHovered`/
> `IsViewClusterHovered`/`IsPageTurnClusterHovered`) and 4 composed read-only properties
> (`IsNavigateClusterVisible` etc. = `ShowChrome || <that cluster's own hover flag>`) - each of the 4
> cluster `Border`s in `ReaderScreen.axaml` now binds `Classes.hidden` to its own composed property
> instead of the shared `!ShowChrome`. `ShowChrome` itself stays as an explicit "show everything"
> override reachable via `ToggleChromeCommand` (center-tap/keyboard) - deliberately NOT removed,
> since touch input has no hover state at all and would otherwise lose any way to reveal chrome.
> The real wiring problem solved: a HIDDEN cluster has `IsHitTestVisible=False` (needed so clicks
> pass through to the page underneath when it's not shown), so it can never receive its own
> `PointerEntered` to un-hide itself - added 4 separate, always-hit-testable, invisible
> (`Background="Transparent"`) hotspot `Border`s, one in each cluster's own corner, each declared
> immediately BEFORE its real cluster in the Grid so the real cluster (once visible) still sits on
> top for its own clicks, with the hotspot only "showing through" in the gap for hover detection.
> Removed the old ambient reveal entirely: `ReaderScreen.axaml.cs`'s root-canvas `PointerMoved`
> (`OnReaderPointerMoved`) no longer calls `NotifyCursorActivity()` (which used to flip `ShowChrome`
> true on ANY movement anywhere) - it now only calls the newly-public `RefreshShortcutHints()`, an
> unrelated pre-existing side effect (keeps keyboard-shortcut tooltips fresh after a Preferences
> remap) that had piggybacked on the same event purely as a convenient "something happened" trigger
> and needed to keep working independently of the chrome-reveal behavior being removed.
> **Verified:** `Paperbunkr.App` builds clean; full `ReaderScreenViewModelTests` suite (197 cases,
> comic + PDF readers) shows the exact same 6 pre-existing failures already logged earlier this
> session (5 unrelated `BookReaderScreenViewModelTests` + 1 `PdfPageReaderScreenViewModelTests` file-
> leftover flake) - zero new failures, every existing `ShowChrome`/`ToggleChrome`/
> `NotifyCursorActivity` test (which the underlying mechanism is unchanged for) still passes.
> **Not done:** no new automated test added for the hover-reveal mechanism itself (would need UI-
> level PointerEntered/Exited simulation, not just VM-level property assertions - the 4 new bools
> are trivial `[ObservableProperty]`s with no logic of their own worth a dedicated test, matching
> this codebase's existing bar for similar simple hover/toggle flags elsewhere). **Not verified:**
> on-screen - standing caveat, though the underlying idle-fade styling fix (what "hidden" visually
> does) was just independently confirmed working by the user immediately before this change, so the
> remaining risk is narrower: mainly the 4 hotspots' size/position actually covering each cluster's
> real footprint well, and z-order not blocking real cluster clicks once shown.

> **Manual session note (2026-09-16, per-cluster hover fix above had a real bug - fixed the same
> day):** User's first on-screen try of the hover-reveal feature: a cluster would show on hover but
> then never hide again, stuck visible permanently. Root cause: `PointerEntered`/`PointerExited` were
> only wired on each corner's invisible hotspot `Border`, not on the real cluster `Border` itself.
> Once a cluster becomes visible it sits on top of (occludes) its own hotspot underneath in the same
> corner - Avalonia then stops routing pointer events to the now-occluded hotspot entirely rather
> than firing a final `PointerExited` for it, so the moment the cursor crossed from the hotspot onto
> the cluster's own surface, `IsNavigateClusterHovered` (etc.) got set `true` and nothing ever set it
> back to `false` again, regardless of where the cursor went afterward. Fixed by wiring the exact
> same `PointerEntered`/`PointerExited` handlers on each real cluster `Border` too (reusing the same
> 8 code-behind methods, not new ones) - now whichever of the hotspot/cluster pair the cursor is
> actually over keeps reporting hover correctly, and the real cluster's own `PointerExited` fires
> normally once the cursor truly leaves the corner. **Verified:** `Paperbunkr.App` builds clean;
> re-ran the directly relevant `ShowChrome`/`ToggleChrome`/`NotifyCursorActivity` VM-level tests (3
> cases, unaffected by this - the bug was purely in XAML event wiring, no VM logic changed). **Not
> verified:** on-screen - same standing caveat as every fix in this area; this is the third
> iteration of the chrome-visibility feature in one day, each based on real user-reported on-screen
> symptoms rather than guesses, so treat this as genuinely unconfirmed until checked again for real.

> **Manual session note (2026-09-16, per-cluster hover mechanism replaced entirely - Enter/Exit
> events abandoned after two straight failures):** The occlusion-based fix above (wiring
> PointerEntered/Exited on the real cluster too) was re-tested on-screen and made literally zero
> observable difference - same "shows once, then stuck visible forever" symptom as the original
> hotspot-only version. Rather than guess at a third Enter/Exit-based theory (occlusion, hit-test-
> visibility-flip timing, or something else in that family - genuinely couldn't tell which without
> live inspection, and user declined a UIA-harness check), abandoned per-element hover events
> entirely. Replaced with direct position math: removed all 4 hotspot `Border`s and every
> `PointerEntered`/`PointerExited` handler, and rewrote `ReaderScreen.axaml.cs`'s
> `OnReaderPointerMoved` (the SAME root-Grid handler already proven reliable all session - it's the
> handler whose ambient reveal was the ORIGINAL "HUD never hides" bug report, meaning it demonstrably
> already fires correctly across the full reading canvas, not just the margins) to compute each
> cluster's hover state directly from the pointer's position against the Grid's own live `Bounds` on
> every real move - `IsNavigateClusterHovered = pos.X < 300 && pos.Y < 60`, and the mirror image for
> the other 3 corners. No Entered/Exited semantics, no hit-test-visibility timing, no occlusion -
> just arithmetic on an event that was never in doubt. Zone sizes are the same rough numbers the
> retired hotspots used (not pixel-exact to each cluster's real content) - a legitimate tuning knob
> if a corner feels off, distinct from "not working at all." **Verified:** `Paperbunkr.App` builds
> clean; full `ReaderScreenViewModelTests` suite (197 cases) shows the exact same 6 pre-existing
> failures as every run earlier today, zero new ones. **Not verified:** on-screen, same standing
> caveat - this is the third attempt at this specific "hide again" behavior; the underlying idle-fade
> CSS-cascade fix (the actual show/hide mechanics) was independently confirmed working by the user
> before any of the hover-specific attempts, so what's actually unverified now is narrower: whether
> position math correctly drives `IsXClusterHovered` in practice, and whether the 4 zone sizes are
> reasonable.

> **Manual session note (2026-09-16, actual root cause of all 4 "still doesn't hide" reports found -
> the hover mechanism was never the bug):** User's 4th on-screen retest of the per-cluster hover
> feature: "nothing, it doesn't hide" - and, on being asked precisely, confirmed hovering doesn't
> even SHOW anything differently at all (chrome is just permanently visible regardless of hover).
> That single fact ruled out every hover-detection theory tried so far (all 3 previous fixes only
> ever touched HOW hover is detected, which was moot if visibility never responded to it in the
> first place) and pointed at `ShowChrome` itself: `IsNavigateClusterVisible` (and the 3 siblings)
> compose as `ShowChrome || IsXClusterHovered` - if `ShowChrome` is stuck `true`, the whole OR
> short-circuits permanently regardless of hover. Confirmed by code reading: `ShowChrome` defaults
> `true` at construction, and the ONLY thing that ever reset it to `false` was the ambient
> `OnReaderPointerMoved -> NotifyCursorActivity()` call - which was deliberately REMOVED earlier the
> same session, the moment the per-cluster hover reveal replaced the old ambient-reveal-everything
> behavior, and nothing was added to replace that reset. Nothing else in a normal reading session
> (only `ToggleFullscreen`/`ToggleChrome`/the idle timer's own `Tick` touch `ShowChrome`, none of
> which fire from ordinary reading) ever set it false again - so `ShowChrome` has been permanently
> `true` since construction for the ENTIRE per-cluster-hover arc today, meaning all 3 earlier hover-
> mechanism fixes (hotspot Enter/Exit, cluster Enter/Exit, position math) were correctly built but
> could never have produced a visible effect either way, because the composed visibility property
> they all fed into was already unconditionally `true` from a completely different, unrelated cause.
> Fix: `ReaderScreenViewModel`'s shared `Load()` (used by `LoadIssue`/`EnsureIssueLoaded`/adjacent-
> issue navigation) now explicitly sets `ShowChrome = false` right after reading the per-issue
> AppSettings - matching `BookReaderScreenViewModel.LoadBook`'s own identical `IsChromeVisible =
> false` reset, which makes the same "fresh reading session starts with chrome hidden" assumption
> that had always held for the Book reader but was only ever implicit (via the now-removed ambient
> reveal) for the comic reader. Also fixed 2 existing tests whose premise the old always-true default
> had baked in (`ToggleChromeCommand_WhenChromeShown_HidesIt`/`_WhenChromeHidden_ShowsItAgain`) -
> not just made them compile, restructured them to actually reflect and exercise the new correct
> default. **Verified:** `Paperbunkr.App` builds clean; full `ReaderScreenViewModelTests` suite (197
> cases) - same 6 pre-existing failures as every run today, the 2 restructured tests pass under the
> new default. **Not verified:** on-screen - 4th attempt at this exact "hide" behavior in one day;
> this is a structurally different, much more confidently-diagnosed root cause than the previous 3
> (found from the user's own precise "doesn't even show differently" answer, not a fresh guess), but
> genuinely still needs a real check before treating this thread as closed.

> **Manual session note (2026-09-12, grid type-ahead/Shift+arrow range-select/Ctrl+Q shipped):**
> Beta-backlog work, P0–P7 unchanged. Design + plan: `docs/superpowers/specs/2026-09-12-grid-
> typeahead-rangeselect-quit-{design,plan}.md`. Closed the last 3 of 4 items the 2026-08-31 keyboard-
> shortcuts spec had explicitly deferred (command palette shipped separately, 2026-09-03). All three
> verified against CE source first: type-ahead ports CE's `KeySearch` exactly (buffered prefix
> search, 2.5s idle reset, articles ignored), wired to Library/Books/Smart Lists only, matching CE's
> own narrow scope; Shift+arrow range-select extends `GridKeyboardNavigation` with a selection-extend
> callback reusing each screen's existing Shift+Click selection methods, wired only where a real
> selection model exists (Library, Books, Detail's Issue tiles); Ctrl+Q routes through the same
> tray-aware `Close()` path CE's own File>Exit accelerator uses. Found and fixed a real gap along the
> way: the custom virtualizing panels' `ScrollIntoView` was `protected`, so nothing outside the panel
> could scroll to an off-screen item — added a public `ScrollToIndex` forwarder. Verified:
> `Paperbunkr.App` builds clean, new/extended tests pass (`TypeAheadSearchTests`,
> `GridKeyboardNavigationTests`, `BooksScreenViewModelTests`).
> **Real pre-existing bugs found while verifying (not caused by this change, flagged separately):**
> the already-known `TwoStepConfirm` delete bug (Library/Smart Lists/Reading Lists) reproduced again
> here; one `DetailTabsViewModelTests` failure (`LinkMetadataAsync_CreatesLinkAndClosesSearch`)
> couldn't be cleanly isolated via git-stash due to a concurrent session repeatedly launching the app
> during this session, but has no plausible code overlap with this change — flagged as likely-
> pre-existing pending confirmation.
> **Not done:** on-screen verification of the type-ahead jump feel, Shift+arrow range-select, and
> Ctrl+Q's tray-vs-quit behavior (standing no-computer-use caveat).

> **Manual session note (2026-09-12, Entrance-animation v2 shipped):** Beta-backlog work, P0–P7
> unchanged. Design + plan: `docs/superpowers/specs/2026-09-12-entrance-animation-v2-{design,plan}.md`.
> Extended the shipped staggered-grid-entrance system (`EntranceAnimation`) from Library+Home to the
> three remaining v2 targets named in the original 2026-09-07 chrome-motion-polish spec's non-goals:
> Books, Smart Lists, Reading Lists. Also fixed a real latent risk while implementing: added a
> `MaxStaggerIndex` clamp to the shared control since Books/Reading Lists are unvirtualized (every
> item realizes in one burst), so a long list would otherwise get an ever-growing stagger tail.
> Verified: `Paperbunkr.App` builds clean, ~30 new/extended targeted test cases pass.
> **Real pre-existing bug found and flagged separately (not fixed here, confirmed unrelated via
> git-stash isolation):** the `TwoStepConfirm` two-click delete bug already known broken for Library
> collections also breaks Smart Lists and Reading Lists deletes identically; a separate brush-type
> test assertion is stale after an Avalonia upgrade. Spawned as a follow-up task
> (`Fix TwoStepConfirm delete not completing (Smart/Reading Lists) + brush-type test bug`).
> **Not done:** on-screen verification of the stagger visual, the large-N cap, and Reduced Motion
> across all three screens (standing no-computer-use caveat).

> **Manual session note (2026-09-12, Issue.AlternateCount gap closed):** Beta-backlog work, P0–P7
> unchanged. Design + plan: `docs/superpowers/specs/2026-09-12-issue-alternate-count-{design,plan}.md`.
> Closed a real, twice-previously-deferred gap (`2026-08-07-bulk-issue-editing-design.md` §3;
> `IssueToComicInfoMapper.cs`'s own unmodeled-elements list; split off from the same-day
> sort/group-axes work) — CE's `AlternateSeries`/`AlternateNumber`/`AlternateCount` ComicInfo.xml
> trio had its first two fields fully ported but never the third. New `Issue.AlternateCount`
> (`int?`) + migration + ComicInfo.xml round-trip + Issue Properties editor field + Library sort/group
> (group reuses `OpenCount`'s CE-exact bucket ranges, generalized for `null` → `"Unspecified"`) +
> Bulk Issue Editing + Smart Lists. Verified: `Paperbunkr.Data.Tests` and targeted
> `Paperbunkr.App.Tests` green (~19 new test cases; two pre-existing tests corrected since they
> asserted the old, now-wrong "unmodeled"/"excluded" behavior for this field).
> **Real pre-existing bug found and flagged separately (not fixed here, confirmed unrelated to this
> change via git-stash isolation):** `AddCoverAspectRatioMigrationTests` fails on `master` on its
> own — its hardcoded multi-migration rollback target now crosses the same-day
> `LibrarySortGroupAxesAndFinalIssueTriState` migration and hits a SQLite full-table-rebuild `NOT
> NULL` collision on `IsFinalIssue`. Spawned as a separate task
> (`Fix migration rollback: IsFinalIssue NOT NULL bug`). **Not done:** on-screen verification of the
> new Issue Properties field and Library sort/group toolbar entries (standing no-computer-use
> caveat).

> **Manual session note (2026-09-06, migration rollback-chain bug fixed):** `Paperbunkr.Data.Tests`
> had 7 tests failing on clean HEAD — every deep `Migrate(PriorMigration)` rollback died with
> `SQLite Error 1: 'no such column: "LibraryGroupField"'`. Root cause: two migrations
> (`20260905063939_AddLastCoverVerificationUtc`, `20260905064447_AddIssueDuplicateAcknowledged`)
> violated the established "new AppSettings/Issues columns get a NO-OP `Down()`, never `DropColumn`"
> rule (documented in `AddNavRailHoverExpandEnabled.Down()`). A `DropColumn` on SQLite forces a
> full-table rebuild from the prior model snapshot, which — since `UnifyLibrarySortGroupFields`
> unmapped `LibraryGroupField`/`LibrarySortField`/`LibrarySortDirection` without physically dropping
> them — silently drops those orphans, breaking every later `Down()` step in the chain. Fix: both
> `Down()` methods are now no-ops with an explanatory comment (columns left as orphans on
> down-migrate), matching `AddNavRailHoverExpandEnabled` / `AddBehaviorSettingsBatch2` /
> `AddMetadataWriteBackSettings` / `AddReadingEventLog`. `AddLastCoverVerificationUtcMigrationTests`
> updated to assert the no-op. `Paperbunkr.Data.Tests` now 881/881 green. Pre-existing bug, unrelated
> to any feature work (found during the Insights dashboard session 2026-09-05). P0–P7 unchanged.
>
> **Correction (2026-09-06, later same day, commit `eb43b66`):** the note above is wrong about
> `AddReadingEventLog` — it does *not* match the no-op pattern, and applying that pattern to it by
> analogy was itself a bug. `Books.CharacterCount` (added by `AddReadingEventLog.Up()`) is a live,
> EF-mapped `Book` property, not an unmapped orphan the way `LibraryGroupField` etc. are — nothing
> else on `Books` was ever silently unmapped, so the SQLite full-table-rebuild collateral-damage risk
> the no-op pattern guards against doesn't apply here. Leaving the column in place on `Down()` instead
> broke the opposite direction: rolling back past `AddReadingEventLog` and migrating forward again
> re-ran `Up()`'s `AddColumn` against a column that never went away, failing with "duplicate column
> name: CharacterCount" (`ReworkBookHighlightAnchorMigrationTests`, whose rollback target predates
> this migration). Fix: `AddReadingEventLog.Down()` now does a real `DropColumn`; the test's legacy
> row-insert was updated to raw SQL matching the pre-migration schema. Verified 2026-09-07: the
> targeted test and the full `Paperbunkr.Data.Tests` suite both pass (906/906, no other migration
> boundary currently hits this). Scoped, reactive fix per the same triage precedent as the note
> above — not a full audit of every no-op-`Down()` migration in the schema.
>
> **Manual session note (2026-09-09, reader decode/cache/prefetch pipeline — all 4 phases landed):**
> Beta-backlog perf work, P0–P7 unchanged. Branch `claude/reader-pipeline` (worktree, off `master`),
> ~20 commits, unmerged. Design: `docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-
> pipeline-design.md` (+ its `-plan.md`; + Phase 3 `2026-09-09-reader-webtoon-strip-band-decode-
> design.md`).
> **Landed & tested (unmerged, GUI-unverified — no computer-use for this project):**
> - `IComicAccessorSession` for every archive engine (7z.dll with solid-block forward-mark range
>   extract; SharpZipLib; SharpCompress; tar) + `IPdfDocumentSession` holding one PDFium
>   `PdfDocument`. Stateless `ReadByteImage` untouched.
> - `ReaderImagePipeline` / `IReaderPageSource` — one decode/cache/prefetch impl; the old
>   `PageImageDecoder`(sync paged) / `PageDecodeService`(continuous) split is **deleted** (three
>   one-shot callers moved to `PageDecodeCore.DecodeSinglePage`). Three byte-bounded `Cache<PageId,T>`
>   tiers under `ReaderMemoryBudget`; the compressed-bytes tier is process-wide (`SharedRawCache`)
>   for instant issue-back-nav. One bg consumer loop, adaptive prefetch fringe.
> - `AppSettings.ReaderMemoryLimitMb` (+ no-op-`Down()` migration). **Preferences UI control for it
>   not wired** — Auto works without it.
> - Comic (paged + continuous) + PDF readers all on the pipeline; paged/PDF now prefetch.
> - Phase 2: 2560px paged display cap; zoom **detail tier** (`ActivePageIndex` + a `PageCanvas`
>   settle-timer that fetches a higher-res decode from the bytes tier, strictly additive).
> - Phase 3: display-tier decode = full decode then downsample to the 2560px cap (`TryDecodeScaled`
>   SKCodec-into-framebuffer path was tried and **reverted** — it AV'd on fast flip).
> - Phase 4: `SkiaBitmapConverter` output cached per-frame (continuous + paged); WebP/HEIF/etc route
>   through the engine's `ConvertToJpeg`; `ReaderPerfStats` + `ReaderFrameStats` overlay (Ctrl+Shift+P).
> - `Paperbunkr.Benchmarks` (BenchmarkDotNet). ~1000 reader/cover/plugin/data tests green across
>   targeted runs; full solution builds clean.
> **Fast-flip AccessViolation (native, no managed crash log) — fix chain (9fb519b→988b54a):**
> serialise all container reads behind `_readerLock` (7z.dll COM not thread-safe); defer
> evicted-bitmap dispose 4s + timer sweep (no hard cap); coalesce paged render pushes to one per
> animation frame; pre-convert transition bitmaps to `SKImage` at message time; and a
> coalesced multi-turn flush now does an instant swap instead of animating from a
> `_lastRenderedPage` the virtualization window has already recycled. **Needs the user's
> with-transitions fast-flip retest to confirm closed.**
> **Deliberately deferred (design-complete):** true per-band progressive webtoon decode
> (`2026-09-09-…-band-decode-design.md`'s own pass); full async-paged swap-on-`PageReady`
> (detail tier + prefetch cover it); 1-reader/N-decoder thread split (one loop already decodes
> off-UI); the `ReaderMemoryLimitMb` Preferences checkbox.
> **A manual or FlaUI GUI pass on the running reader is the one gate before this merges.**
>
> **Manual session note (2026-09-10, reader backlog Batch A):** Beta-backlog reader work, P0–P7
> unchanged. Branch `claude/reader-backlog-batch-a` (off `master`), unmerged. Design + plan:
> `docs/superpowers/specs/2026-09-10-reader-backlog-batch-a-{design,plan}.md`; pipeline design doc
> gained §17 (decision record). Three items:
> 1. **`ReaderMemoryLimitMb` Preferences control** — the control PR #68 shipped the column+migration
>    for but deferred. Segmented Auto / 512 / 1024 in Preferences → Reader → PERFORMANCE; `segTab`
>    style promoted from `LibrarySection.axaml` to `Styles/Primitives.axaml`. No migration.
> 2. **Async-swap-on-cold-miss** — `ReaderScreenViewModel.RefreshCurrentPage`'s paged branch no
>    longer blocks the UI thread on a **large jump** (`|new−old| > 3` — thumbnail click, type-to-jump)
>    to an undecoded page: it keeps the outgoing page up, shows a `…` indicator (`IsPageLoading`),
>    and swaps in on `BackgroundDecodeCompleted` (primary, then the spread's pair via
>    `ResolveSecondaryPage`), with a 5 s `DispatcherTimer` → `ErrorMessage` fallback. **Adjacent
>    turns keep the synchronous path** — first attempt made *every* turn async and regressed
>    deterministic double-page pairing (3 pre-existing tests); scoping to large jumps fixed that.
>    §17 records the full async-paged restructure, N decode workers, and an `IReaderPageSource`
>    queue-cancel API all staying deferred/declined (the queue already drops stale entries at
>    dequeue — a 50-jump burst does ~3 real decodes).
> 3. **Type-to-jump-to-page** — deliberate deviation (CE has no numeric go-to-page; its `D1`–`D0`
>    are zoom/layout). New `ReaderGoToPage` command (default `G`) + an inline `TextBox` (`MaxLength=7`,
>    digit-only via `TextInput`/`DataObject.Pasting` filters, `long.TryParse`+clamp on commit) that
>    replaces the `PAGE x / y` readout. New `NavigateToPageIndex` helper shared with `SelectThumbnail`.
>    `PageCanvas` gained `GoToPageGesture`/`GoToPageCommand`/`PageInputActive` styled props.
> **Verified:** App + Tests build clean (obj-dll delete + rebuild, XAML weave confirmed via test run);
> `ReaderScreenViewModelTests` 206/206 (incl. 21 new + the 3 double-page fixes), `KeyBindingServiceTests`
> + `PreferencesScreenViewModelTests` green (149). **Not done:** on-screen verification (no computer-use).
> New `App.Tests/Fakes/FakeReaderPageSource.cs` for the async-swap tests. Landed on top of the
> concurrently-committed dropdown-freeze fix (`a03a29d`, a different session — the shared worktree
> briefly clobbered `ReaderScreen.axaml.cs` mid-edit, recovered via a checkpoint commit).
>
> **Manual session note (2026-09-10, reader-pipeline rev-3 addenda):** follow-up hardening on
> top of PR #68, from a technical review of the shipped code. Branch
> `claude/reader-rev3-addenda` (off `master`), spec §15 of
> `2026-09-08-reader-decode-cache-prefetch-pipeline-design.md`, plan
> `2026-09-10-reader-rev3-addenda-plan.md`. Five items, all coded + targeted reader tests green
> (312/312 on the reader/PDF/archive filter):
> (1) 7z.dll COM now runs on **one dedicated executor thread** per session (`SevenZipEngine.ComExecutor`,
>     STA on Windows) — thread affinity, not just a lock, for the apartment-bound `IInArchive`;
> (2) `PdfiumAccessorSession` keeps an **LRU of 3 `PdfPage`** objects instead of load+close per render;
> (3) **detail-tier bitmaps now count against `ReaderMemoryBudget`** — `GetDetailPage` reserves
>     `w*h*4`, drops the display cache's `SizeCapacity`, `ReleaseDetail()` restores it;
> (4) `MultiExtractToStreamsCallback` **pre-sizes its streams** from `kpidSize` (the two stream
>     sessions were already exact-size — item narrower than the review assumed);
> (5) the **prefetch-fringe recompute is debounced ~30 ms** so a held-key flip stops churning
>     superseded fringe decodes.
> **User GUI check (2026-09-10):** reader "way smoother" in normal use. User **hammered the
> fast-flip AccessViolation repro** (CBZ + image adjustment + machine-gunning the turn key)
> **both with and without the Slide transition — no crash, stayed stable.** The AV that survived
> multiple GUI-unverified fix chains now looks fixed — not formally declared closed (one session;
> native AVs can be load/timing-dependent) but strong evidence. Rev-3 items (1) 7z COM thread
> affinity and (5) fringe debounce are the likely closers.
> **Residual (smoothness, not crash):** page-turn transitions were "a bit choppy" — the
> low-priority prefetch fringe's `CreateScaledBitmap` churn on the decode workers competed with
> the compositor mid-slide. Follow-up: `IReaderPageSource.SuppressFringePrefetch(ms)` — `PageCanvas`
> calls it when it sends a `ReaderPageTransitionData` (hold = transition duration + 40ms), so the
> fringe pass defers past the animation while the visible window keeps decoding at high priority.
> GUI-unverified (check the Ctrl+Shift+P `ReaderPerfStats` overlay's compose p99 during a
> transition-flip). Committed on `claude/reader-transition-smoothing`.
> **`Paperbunkr.Benchmarks` note:** `SequentialFlip` returns `NA` — BDN's isolated child process
> doesn't survive the `[GlobalSetup]` headless-Avalonia bootstrap in this environment (same class
> as the full-suite headless flake). Not blocking; the rev-3 item-4 allocation change is covered
> by the 7z session unit tests. The harness itself needs a fix before it yields numbers.
>
> **Also 2026-09-10 — startup dead-splash bug (separate, startup-pipeline):** `App.RunDesktopStartupAsync`
> built `new SplashWindow()` (App.axaml.cs:53) — whose ctor reads `AppSettings` via
> `SkinService.GetReducedMotion()` — **before** the migration step and **outside** the try/catch.
> On a post-update cold start with a stale dev DB (`%APPDATA%` db was at `AddLastRunVersion`, missing
> PR #68's `AddReaderMemoryLimitMb`), the read threw `SqliteException`, the fire-and-forget startup
> Task faulted unobserved, no MainWindow → app looked frozen (message loop idle, no window).
> Diagnosed from a full dump. Fixes: `SplashWindow` ctor `TryGetReducedMotion()` (try/catch →
> motion-on) so the splash never hard-depends on DB schema; splash construction moved inside the
> `RunDesktopStartupAsync` try so failures hit `ReportFatalStartupError`. DB migrated forward with
> `dotnet ef database update`. **Merged to master with the rev-3 work as PR #71 (`1b0a56a`),
> 2026-09-10** — commits `03980eb` (reader rev-3) + `a923d7b` (splash fix).
>
> **Manual session note (2026-09-05, Duplicate Finder shipped + grouped review/bulk delete/scan
> alerts):** follow-up to the Plugin API v2 backlog-finish note directly below. Duplicate Finder
> moved from a `Paperbunkr.Plugins.Tests`-only fixture to a real, downloadable plugin
> (`sample-plugins/DuplicateFinder/` + `.zip`, documented in `wiki/Plugins.md`). Also adds a new
> general Plugin API capability: a `CreateBookList` command can return grouped results
> (`PluginBookGroup`) to open a Grouped Review overlay (per-group keep/skip, bulk "Resolve All"
> delete) and get a proactive Activity Center alert when a scan finds more duplicate groups than
> last time - Duplicate Finder's own "Possible Duplicates" command upgraded to use it as the real
> demonstration. Design: `docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-scan-alerts-
> design.md`. Two real bugs found via this session's own new tests (an `ActivityService.RaiseAlert`
> dedupe/stale-title bug, and a `CoverKey`-vs-`CoverIssueId` cover-rendering bug in the Smart Lists
> plugin-result path) - detail in `Paperbunkr-Roadmap.md`'s Plugin API v2 section. Verified: full
> solution builds clean, 45 `Paperbunkr.Plugins.Tests` + 461 targeted `Paperbunkr.App.Tests` green,
> re-checked after merging in the concurrently-landed native "Duplicate Files Review" feature
> (Needs Review queue) to confirm no interaction bugs between the two. **Not done:** on-screen
> verification.

> **Manual session note (2026-09-05, Plugin API v2 backlog finish):** closes the Plugin API v2
> backlog for good. Two parts: (1) merged the pushed-but-unmerged `plugin-api-gap-closure` branch
> (automation gaps - `AddNewBook`/icon methods/`SelectComics` - plus IronPython plugin scripting,
> found sitting unreviewed since 2026-08-31); (2) wired the remaining 8 hooks with no live UI
> trigger (Editor/Books/NewBooks/CreateBookList/ParseComicPath/NetSearch/ConfigScript/
> ReaderResized) and built the 3 net-new UI surfaces the original v2 spec called for but never got
> (ComicInfoHtml/UI, QuickOpenHtml/UI, DrawThumbnailOverlay). Also fixed a real bug the audit
> turned up: `BookOpened` was documented as one of "4 hooks with a real live trigger" but was
> actually a dead wire - nothing subscribed to `ReaderScreenViewModel.IssueOpened`. Full detail,
> every anchor's CE-source grounding, and the design tradeoffs (why `Books`' hook needed its own
> globals type, why `CreateBookList` needed a real non-DB-backed sidebar section, why
> `DrawThumbnailOverlay` isn't live/per-paint like CE's) are in
> `docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md`. New "Hook Coverage"
> sample plugin + 14 `Paperbunkr.Plugins.Tests` prove every wired hook end-to-end - closes the "no
> live sample plugin exercises this hook" gap the original 2026-08-24 audit flagged for
> ParseComicPath/NetSearch/ConfigScript specifically. Verified: full solution builds clean, 362
> targeted `Paperbunkr.App.Tests` + 43 `Paperbunkr.Plugins.Tests` green. **Not done this session:**
> on-screen GUI verification of any of the new surfaces - same standing caveat as every other
> backlog item that ships without a live click-through pass.

> **Manual session note (2026-09-05, metadata editor affordances):** Beta-polish work landed —
> CE-style editing affordances on both metadata editors (single-issue + bulk):
> library-learned + static autocomplete, editable dropdowns (Format / Publisher / Imprint / Age
> Rating / Book Age / Language), numeric up/down spinners, per-comma-item autocomplete on the
> multi-value fields. New `MetadataVocabularyService` + `MultiValueAutoComplete` / `TextSpinner`
> attached behaviors + `Styles/FormControls.axaml`. **No migration** (every field already on
> `Issue`; Alternate Count deliberately *not* added since it has no column). Corrects the
> 2026-08-07 editor spec's wrong "CE uses plain textboxes, no spinner/autocomplete" claim —
> verified against CE's real `ComicBookDialog` + `SpinButton` source. Design +
> plan: `docs/superpowers/specs/2026-09-05-metadata-editor-affordances-{design,plan}.md`.
> Verified green: new `MetadataVocabularyServiceTests` / `TextSpinnerTests` /
> `MultiValueAutoCompleteTests` + extended VM tests; targeted `Paperbunkr.App.Tests` subsets all
> pass; app rebuilds clean (`rm dll` + build, XAML weave confirmed). `Paperbunkr.Data.Tests` has 7
> pre-existing migration-replay failures (`no such column: "LibraryGroupField"` — a
> migration-history/snapshot desync from the earlier unified-sort/group work, unrelated to this
> App-only change). On branch `claude/metadata-editor-affordances`; on-screen GUI pass still
> outstanding. **P0–P7 status unchanged (still all done).**

> **Follow-up (2026-09-10) — the on-screen pass found a hard freeze, now fixed.** Opening *any*
> dropdown in the metadata editor permanently wedged the UI thread on this project's runtime target
> (Win11 IoT LTSC, degraded render stack). Root-caused via `dotnet-stack` to Avalonia's
> `ComboBox`/`AutoCompleteBox` templates: `Popup.Open()` re-enters an oscillating TwoWay
> `IsDropDownOpen`⇄`Popup.IsOpen` `bool` `TemplateBinding`, amplified by FluentAvalonia's
> unconditional high-contrast resource reload on popup-host creation and by native popup-window
> teardown. Fixes: (1) `FluentAvaloniaWorkarounds.SuppressColorValuesChangedHandler()` unsubscribes
> FA's `ColorValuesChanged` handler at startup; (2) `Win32PlatformOptions.OverlayPopups = true`;
> (3) **new `Controls/SuggestBox` replaces every `ComboBox`/`AutoCompleteBox`** in both editors — a
> `TextBox` + ▼ + plain `Popup` (the LibraryToolbar idiom), no TwoWay popup binding; (4)
> `FreezeWatchdog` now auto-captures a `dotnet-stack` trace on freeze. **Confirmed working
> on-screen by the user.** Not committed at session end. P0–P7 unchanged.

> **Follow-up 2 (2026-09-10) — `SuggestBox` migration completed app-wide.** The freeze fix above
> (`a03a29d`, cherry-picked here) only converted the 3 metadata editors. This branch converts
> **every remaining** `ComboBox`/`AutoCompleteBox` under `src/Paperbunkr.App/Views` (15 files,
> ~40 instances) to `Controls/SuggestBox` — see
> `docs/superpowers/specs/2026-09-10-suggestbox-migration-plan.md` for the per-site audit. The two
> *global* fixes (FA handler unsubscribe + `OverlayPopups`) already removed the freeze everywhere;
> this pass is idiom consistency (user chose "convert everything"). VMs gained
> `WeightText`/`WeightNames`-style string wrappers (enum pickers) and display-string wrappers
> (object-catalog pickers: `RelationTypeOption`, `EventMembershipRoleOption`, `ArcSourceOption`,
> `SmartListOption`, …). User-named/collide-able pickers (SmartLists, StoryEvent, VirtualTag)
> converted with accepted first-match-by-name semantics. `SuggestBox` `ControlTheme` gained
> `Background`/`BorderBrush`/`Foreground`/`CornerRadius` `TemplateBinding`s so the classed
> `.contentTypePicker` / `.conditionPicker` looks survive. Dead code removed:
> `Behaviors/MultiValueAutoComplete.cs` (+ test), both `WeightOptions` props, the now-unbound
> `*Options` enum arrays in `PreferencesScreenViewModel` / `ActivityCenterViewModel`. New
> `SuggestBoxTests`-style VM-wrapper coverage across ~10 test classes; App project + full test
> suite green; XAML weave verified (splash renders). On-screen click-through still outstanding
> (blocked locally by a corrupt shared dev DB — unrelated to this change). P0–P7 unchanged.

> **Manual session note (2026-09-04):** P0–P7 remain all done. Two things worth recording here since
> this is the doc a human opens first:
> 1. **The project is now `0.2.0-beta`** (`v0.2.0-beta` tag, `CHANGELOG.md`, README/wiki updated).
>    In-app auto-update via NetSparkle + a tag-triggered GitHub Release/appcast pipeline shipped
>    `32d82bf`. These `alpha-*.md` filenames are historical now; the Beta backlog itself is still
>    live in [`Paperbunkr-Roadmap.md`](Paperbunkr-Roadmap.md).
> 2. **On-screen verification debt largely cleared.** The user personally clicked through and
>    confirmed on 2026-09-04: Activity Center, Panorama variable widths, drag-and-drop import, file
>    metadata write-back, Library view-mode virtualization, manga detail / cover-art override /
>    MangaBaka picker, Saved Workspaces, Quick Open palette, double-page spread / remappable reader
>    shortcuts / auto-scroll, comprehensive keyboard operability, Smart Collections, MediaRelation
>    collection nodes, SmartList Engine v2, Plugin API v3, Specials tab. Full list + remaining
>    unverified edge cases in `Paperbunkr-Roadmap.md`'s "On-screen verification" section.

> **Manual session note (2026-08-27, Metadata Model Phases 4d-4g):** net-new metadata-platform work
> landed this session — Event Relations (4d, one new EF migration
> `20260827193943_MetadataModelPhase4dEventRelations`), Format-Signal Event Suggestions (4e),
> Continuity Browse view (4f), and Age Progression / Timeline (4g). All four extend the Story
> Events screen (`EventsScreenViewModel`) — 4f adds an Events|Continuities|Timeline mode switcher,
> 4g adds Timeline as the third mode. **P0-P7 status is unchanged (still all done)** — this is Beta
> backlog, so the full detail lives in [`Paperbunkr-Roadmap.md`](Paperbunkr-Roadmap.md)'s "Metadata Model
> platform" section, not here.
>
> **Follow-up (2026-08-28):** every deferred 4d-4g item then got built too — persisted suggestion
> dismissals, a transitive event graph + event-relation auto-suggestions, Timeline scopes (series
> family / continuity / whole library) + a character-aware toggle backed by a new first-class
> `Character` index over `Issue.Characters`, a bulk "review inferred ages" surface, cross-continuity
> comparison, "create reading list from continuity", a `BookAge` autocomplete editor, and a
> `SmartListField.Continuity`. One new migration
> `20260828104324_MetadataModelPhase4DeferredItems` (`EventSuggestionDismissal` / `Character` /
> `CharacterAppearance`). Verified green: `Paperbunkr.Data.Tests` 533/533, `Paperbunkr.App.Tests`
> 1098/1098, `Paperbunkr.Plugins.Tests` 11/11 (UiTests not run — flaky in this env); app
> smoke-launched OK. Still uncommitted on branch `books/browse-chrome`; on-screen GUI pass on the
> new surfaces still outstanding.

> **Manual session note (2026-09-01, Auto-update and changelog):** adds an in-app `UpdateService`
> (checks for new releases, downloads/applies updates) and a hand-authored `CHANGELOG.md` (repo
> root, Keep a Changelog format) rendered via a new Preferences → About section. Went through two
> engine choices in one session: first built on Velopack (which would have retired the P7 Inno Setup
> installer — `installer/Installer.iss` + `installer/BuildInstaller.ps1`), then reverted after a real
> `NotInstalledException` crash and user-surfaced evidence of rough Velopack installer UX prompted an
> actual library survey. Landed on **NetSparkleUpdater.SparkleUpdater** instead — installer-agnostic,
> so **the P7 Inno Setup installer is unchanged and stays exactly as it was**; NetSparkle just
> downloads and runs it. New `.github/workflows/release.yml` (tag-triggered): builds via
> `installer/BuildInstaller.ps1` as before, then generates and Ed25519-signs an `appcast.xml` via
> `netsparkle-generate-appcast`, uploaded alongside the installer to the GitHub Release. No install-
> location change, no per-machine-vs-per-user migration concern (a real one under Velopack, moot now).
> Design/plan, including the mid-session revision note:
> [`2026-09-01-auto-update-and-changelog-design.md`](superpowers/specs/2026-09-01-auto-update-and-changelog-design.md).
> P0–P7 status unchanged (still all done) — this is Beta-backlog infrastructure work, not an Alpha
> gap. Not yet verified via a real tagged release (the CI pipeline can't be tested any other way);
> full solution build clean, `dotnet test` on `Paperbunkr.App.Tests` green (1436/1436 relevant — one
> unrelated pre-existing timing-flaky `LiveFolderWatchServiceTests` test, confirmed flaky by rerunning
> in isolation, not reproducible at a fixed spot).

> **Manual session note (2026-09-03, Library view-mode virtualization + Comic List removal):**
> On a 2000+ issue library, only PosterGrid (ungrouped) was virtualized; switching to Details /
> List / Tiles / Panorama (or any grouped mode) realized one visual container per row up front —
> a multi-GB memory spike and long stall (surfaced while testing Saved Workspaces, where a preset
> that selected Details was repointed to PosterGrid as a stopgap). This pass: (1) **removed the
> "Comic List" view mode entirely** (`LibraryViewMode.IssueList`) — a redundant flat per-issue
> list that Details already covers; `IssueListScreen.axaml`/`.cs` deleted, `IssueListScreenViewModel`
> kept as the shared sort/group engine, migration `RemoveComicListViewMode` remaps a persisted
> `'IssueList'` to `'Details'`. (2) **List + Details → a single `ListBox`** (`VirtualizingStackPanel`)
> per granularity, bound to a new flat `FlatRows`/`FlatCovers` projection that interleaves
> `GridSectionHeader` rows so grouped and ungrouped share one virtualized control; `ListBoxItem`
> retemplated to a bare non-focusable `ContentPresenter` (inner `Button.card` stays the only tab
> stop). (3) **Tiles + Panorama ungrouped → `VirtualizingWrapPanel`**. (4) **Grouped Poster +
> Tiles + Panorama** → outer `VirtualizingStackPanel` over groups + inner `VirtualizingWrapPanel`
> per group. Panorama uses `PanoramaTileWidth` (~110, the exact clamped value its tiles already
> rendered at since the 2026-08-22 cover-memory change — a first attempt with 215 looked wrong and
> was reverted, then redone right when the user asked for "virtualize everything"). A-Z jump
> indexer uses real `ListBox.ScrollIntoView` for List/Details. **User-confirmed on their 2000+
> library 2026-09-03**: List / Tiles / Details / grouped stay ~500 MB–1.2 GB (was 3.2 GB), no
> spike. Full solution build clean; `Paperbunkr.Data.Tests` 727/727, `Paperbunkr.App.Tests`
> 1484/1486 (2 unrelated pre-existing flakes, pass in isolation). New `FlatRows`/`FlatCovers` +
> migration tests added. Remaining manual checks: `ListBoxItem` chrome neutral in both themes,
> group headers render, arrow-key nav on Poster. P0–P7 status unchanged — Beta-backlog perf work.
>
> **Follow-up same day — unified sort/group field pool:** the user asked for per-series and
> per-issue cards to share one sort/group field list instead of the two disjoint sets
> (`LibrarySortField` 7 / `LibraryGroupField` 4 vs `IssueListSortField` ~55 / `IssueListGroupField`
> ~40). Done: `LibrarySortField` / `LibraryGroupField` / `LibraryFieldCatalog` **deleted**;
> `IssueListScreenViewModel` (`IssueList.SortField` / `SortDirection` / `GroupField` +
> `IssueListFieldCatalog`) is now the single pool for **both** granularities. Per-series cards
> sort/group by delegating to that catalog through a new `SeriesCardSample.RepresentativeRow` (an
> `IssueListRow` built from the cover issue — so "sort series by Writer" = by cover-issue writer);
> `IssueListRow.SeriesIssueCount` / `SeriesUnreadCount` and new `IssueListSortField.SeriesIssueCount`
> / `SeriesUnreadCount` + `IssueListGroupField.ContentType` / `Alphabetical` / `SeriesIssueCount`
> carry the old series-only fields into the union. Toolbar Sort/Group tabs collapsed to one list
> each (no more `IsIssueGranularity`/`IsSeriesGranularity` branch). Migration
> `UnifyLibrarySortGroupFields`: best-effort carries a user's series selection into
> `LibraryIssueListSort*/GroupField` (only when untouched); the 3 now-unmapped columns are left as
> orphans (not dropped) so an older Paperbunkr build opening the shared dev DB still starts. Full
> build clean; `Paperbunkr.Data.Tests` 727/727, `Paperbunkr.App.Tests` 1484/1486 (2 unrelated
> pre-existing flakes, both pass in isolation). Same on-screen verification still pending. P0–P7
> unchanged.
>
> **Also fixed (2026-09-03): DB-recovery flow crashed instead of showing `DatabaseRecoveryWindow`.**
> `App.HandleDatabaseRecovery` → `BackupService.GetAvailableBackups` → `GetBackupLocation` opened
> the database to read the optional `AppSettings.BackupLocation` override - which throws
> `SqliteException: database disk image is malformed` on the exact code path that only runs when the
> DB is corrupt, so the recovery window never appeared (real incident: a corrupt `paperbunkr.db`,
> recovered from an auto-backup by hand). `GetBackupLocation` now try/catches that DB read and falls
> back to the default backups folder. Regression test in `BackupServiceTests`.
>
> **Follow-up (2026-09-03) — Panorama variable cover widths, while virtualized:** the previous
> pass put Panorama on the uniform-cell `VirtualizingWrapPanel` (every tile ~110px), which killed
> the whole reason Panorama exists — showing each cover at its real orientation. Fixed without
> reintroducing the eager decode-every-cover cost: new `Issue.CoverAspectRatio` (nullable, migration
> `AddCoverAspectRatio`) written at thumbnail generation (`CoverThumbnailService`) and by a
> header-only JPEG sweep (`CoverThumbnailService.BackfillAspectRatios`, folded into
> `GenerateAllAsync` so every scan / Generate Covers fills it); also learned progressively in-session
> as covers decode on screen (`CoverAspectRatioStore`, an in-memory dict + debounced write-back).
> New `VirtualizingVariableWrapPanel` (+ pure `VirtualizingVariableWrapMath`) — a sibling of
> `VirtualizingWrapPanel` that packs rows from each item's own `PreferredWidth`
> (`IVariableWidthTile` on `IssueListRow` / `SeriesCardSample`), same realize/recycle + arrow-nav
> machinery, layout cached and only re-packed on width/items change. Panorama's 4 ItemsControls
> switched to it; templates bind `Width="{Binding PanoramaWidth}"` (real per-cover). Library
> re-packs Panorama (debounced) when the store learns new ratios mid-session; after a backfill
> every ratio is persisted and that never fires. `VirtualizingWrapPanel` / Poster / Tiles / List /
> Details unchanged. Build clean, `has-pending-model-changes` none; `Paperbunkr.Data.Tests`
> 731/731, `Paperbunkr.App.Tests` 1625/1625. Manual on-screen check of real cover orientations +
> scroll memory still pending (no computer-use). P0–P7 unchanged — Beta-backlog perf/polish.

> This section drifted before: it was last hand-written at `7e2d3d3` and had already fallen behind
> five real commits by the time anyone reopened it. That's the whole reason for the live tracker —
> see [Live tracker](#live-tracker) below. It drifted a second, smaller way too: `d86cac7` (same
> day) hand-updated the "Open: nothing" content below to reflect P0–P7 all done, but left this
> heading's HEAD marker at the older `3e7ada3` — caught and fixed by a prior sync pass, which also
> confirmed six more commits landed for items previously marked "not yet committed" (icon,
> LRU-cache crash fix, dialog/Maintenance-toggle fix, installer project, file-association crash
> fix — commit refs added inline below) and found two more stale worktrees beyond the one already
> noted (see Housekeeping).
>
> **This sync (re-synced `5869ed0` → `9769cfc`):** two new commits, both Beta-backlog work, not
> Alpha P0–P7 — `963ef8c` (design spec) and `9769cfc` (implementation): first slice of Reader
> polish — fit modes (Original/Fit/FitWidth/FitHeight/BestFit), zoom presets, manual +
> auto-rotate-landscape, and a Preferences → Reader tab to back it. Verified directly against
> source, not the commit message: `PreferencesScreenViewModel.cs` has real
> `FitModeOptions`/`ResetZoomOnPageChange`/`DefaultPageFitMode` members wired to persisted
> settings. P0–P7 status is unchanged by this — see the updated [Bonus](#bonus-ahead-of-schedule-reader-zoompan-gestures-done)
> note below. The three worktrees under `.claude/worktrees/` (`quirky-borg-c5d364`,
> `compassionate-banach-c6e8bf`, `exciting-hypatia-eecfc9`) are still present but couldn't be
> re-checked this pass — their `.git` files point at absolute Windows paths not resolvable from
> this sync environment, so their status (clean vs. the noted uncommitted edit) is carried over
> unverified rather than guessed at. Working tree in the main repo itself: `git status --short`
> shows ~1040 modified files, all line-ending-only (CRLF vs. LF, confirmed by diffing
> `.gitignore`) — an artifact of this environment's Windows↔Linux mount, not real uncommitted
> work; not treated as a pending change. Treat this file as re-synced as of `9769cfc`; if you're
> reading it later than that commit, check the tracker artifact or `git log` before trusting it.
>
> **This sync (re-synced `9769cfc` → `103e3c3`):** four new commits, all Beta-backlog Reader
> polish, not Alpha P0–P7 — `b195f56` (design spec: continuous/webtoon scroll, chrome/overlays,
> magnifier, image adjustment, background/margins), `5d47b8d` (this doc's own prior sync commit,
> already reflected above), `a2cfb7d` (F11 added alongside F for fullscreen toggle), `103e3c3`
> (Reader polish: continuous/webtoon scroll, rendering unification, Stages 0-4). P0–P7 status is
> unchanged. Verified directly against source, not commit messages: F11 fullscreen toggle
> confirmed in `PageCanvas.cs` (`if (e.Key is Key.F or Key.F11)`, with a doc comment citing the
> Stage 0-4 design spec) and in `ReaderScreen.axaml`'s fullscreen button; continuous/webtoon
> scroll confirmed present across `ReaderScreenViewModel.cs`, `ReaderLayoutModel.cs`,
> `PageCanvas.cs`, `ReaderPageVisualHandler.cs`, and `ReaderScreen.axaml`/`.axaml.cs`. Both are
> Beta "Reader polish" backlog per this doc's own scope note at the top, not part of the P0–P7
> alpha checklist — see the updated [Bonus](#bonus-ahead-of-schedule-reader-zoompan-gestures-done)
> note below for the one concrete edit made to reflect them landing. Worktrees re-checked via
> `git worktree list` (works from this sync environment, unlike last time): all three
> (`quirky-borg-c5d364` at `6e8c9b7`, `compassionate-banach-c6e8bf` at `25c664a`,
> `exciting-hypatia-eecfc9`, detached at `d86cac7`) are still present, all marked `prunable` by
> git itself — still safe-to-discard candidates, no new worktrees found. Main worktree `git
> status --short` still shows ~1046 modified files, same CRLF/LF line-ending artifact as before,
> not treated as pending work. No local companion HTML tracker file found anywhere under this
> repo (searched for `*tracker*`/`*dashboard*` filenames and every `.html` file — only matches
> were an unrelated `_reference/ComicRackCE` file and stray worktree scratch content) — skipping
> that part of the sync rather than guessing at a path or fabricating one; the hosted dashboard
> artifact URL above is unaffected. Treat this file as re-synced as of `103e3c3`.
>
> **This sync (re-synced `103e3c3` → `8fde584`):** one new commit, `8fde584` — Reader polish:
> position tracking/persistence, fullscreen + chrome/overlays, live image adjustment
> (brightness/contrast/saturation/gamma), and background/margin, all Beta-backlog per this doc's
> own scope note, not Alpha P0–P7. P0–P7 status is unchanged. This commit is unusual: it already
> updated this doc's own Bonus section (see
> [Bonus](#bonus-ahead-of-schedule-reader-zoompan-gestures-done) below) in the same commit as the
> code — but left this heading's HEAD marker one commit behind its own SHA, the same
> heading-lags-content drift pattern noted earlier in this section (`d86cac7`). Fixed here: heading
> bumped from `103e3c3` to `8fde584`, no other content changes needed since the Bonus section
> already accurately describes this commit's work. Verified directly against source, not the
> commit message: `git show --stat 8fde584` confirms `ImageAdjustmentMath.cs` (new),
> `ReaderScreenViewModel.cs` (+444/-lines), `PreferencesScreenViewModel.cs`, `PageCanvas.cs`,
> `ReaderPageVisualHandler.cs`, and `ReaderScreen.axaml`/`PreferencesScreen.axaml` all touched,
> matching the described §6/§7/§9/§10 feature set. Worktrees re-checked via `git worktree list`:
> same three as last sync (`quirky-borg-c5d364` at `6e8c9b7`, `compassionate-banach-c6e8bf` at
> `25c664a`, `exciting-hypatia-eecfc9` detached at `d86cac7`), still all `prunable`, no new ones.
> `git status --short` shows the same ~1044 line-ending-only modified files as before (not treated
> as pending work), plus five new untracked paths not present in earlier syncs:
> `.claude/settings.local.json`, `installer/Assets/WizardImage.bmp`,
> `installer/Assets/WizardSmallImage.bmp`, `installer/Assets/welcome-source.png`,
> `src/Paperbunkr.App/Assets/welcome-source.png`. These look like in-progress installer-branding
> assets (wizard banner images) but aren't referenced by any commit or roadmap item yet, so noted
> here rather than guessed at — nothing in P0–P7 depends on them being committed. No local HTML
> tracker file found (same result as last sync). Treat this file as re-synced as of `8fde584`.
>
> **This sync (re-synced `8fde584` → `85fb681`):** two new commits, neither changing P0–P7 status
> (already all done): `8ac8cb4` (wizard branding for the alpha installer) and `85fb681` (adds
> `README.md`; also carries this doc's own pending resync-to-`8fde584` content, committed as-is —
> that's why the previous sync note above already matched HEAD `8fde584` despite this being a
> separate, later commit). Verified directly against source, not commit messages: `git show
> 8ac8cb4` confirms `installer/Installer.iss` now sets `WizardImageFile`/`WizardSmallImageFile`
> (composited from the app's own logo) and custom `WelcomeLabel1`/`WelcomeLabel2` text, plus
> `DisableWelcomePage=no` (a real bug fix — Inno Setup 6 defaults that to `yes` and was skipping the
> welcome page entirely); `git ls-files installer/Assets/` confirms `WizardImage.bmp`,
> `WizardSmallImage.bmp`, and `welcome-source.png` are now tracked, so 3 of the 5 previously-noted
> untracked installer-branding assets are resolved. Two untracked paths remain, unchanged in nature
> from last sync: `.claude/settings.local.json` (local-only, expected) and
> `src/Paperbunkr.App/Assets/welcome-source.png` (a leftover duplicate of the same source image now
> that `installer/Assets/welcome-source.png` is the tracked, wired copy — not referenced by any
> commit, no P0–P7 item depends on it). Worktrees re-checked via `git worktree list`: same three as
> every prior sync (`quirky-borg-c5d364` at `6e8c9b7`, `compassionate-banach-c6e8bf` at `25c664a`,
> `exciting-hypatia-eecfc9` detached at `d86cac7`), all still `prunable`, no new ones. Main worktree
> `git status --short` still shows ~1042 line-ending-only modified files (confirmed via `.gitignore`
> diff, same as every prior sync), not treated as pending work. No local HTML tracker file found
> (same result as every prior sync — searched for `*tracker*`/`*dashboard*` filenames and every
> `.html` file under the repo). Treat this file as re-synced as of `85fb681`.
>
> **This sync (re-synced `85fb681` → `2d0692e`, 2026-08-22):** a large batch — 17 new commits, none
> changing P0–P7 status (still all done). Most were Beta-backlog design specs committed one at a
> time (page transitions, double-page spread, remappable reader shortcuts, tracker/manga-UI
> research, auto-scroll, reveal-in-Explorer/fileless entries, manga/ContentType classification,
> saved List Layouts, Metadata Model Phase 1, Home screen, Library browse history), plus one
> doc-only self-correction (`f83fa5f`, undoing a stale-file accidental revert `e019825` picked up).
> Two commits carry real shipped implementation, not just specs — verified directly against source,
> not commit messages:
> - `c1e91a6` **"Ship reader polish, library UX, and Metadata Model Phases 2a-5a"** — lands a
>   backlog of already-tested-but-uncommitted work: reader page transitions, double-page spread,
>   remappable shortcuts (P5 seam extended to 25 commands), auto-scroll; reveal-in-Explorer/fileless
>   entries, manga/ContentType classification, Saved List Layouts, a global `KeyBindingService`;
>   Metadata Model Phases 2a–5a (proposals, series reassignment, field descriptors, Media Relations,
>   Continuity, Story Events, Reading List overhaul, External Metadata schema); and the first real
>   on-screen UI automation harness (`Paperbunkr.App.UiTests`, FlaUI/UIA3). Deliberately excluded
>   `current_alpha_todo.md` as "a stale duplicate of docs/alpha-todo.md" — matches this file's own
>   assessment (see the untracked-file note below).
> - `2b2da5e` **"Ship Home screen, Issue List sort/group, library browse history, AniList adapter,
>   recommendation engine"** — a real Home screen (continue-reading/because-you-read/spotlight
>   modules) with `HomeScreenViewModel` wired into rail-nav (`GoHomeCommand`, `IsHome`), backed by
>   `HomeFeedResolver`, which reuses `RecommendationResolver.GetRecommendations` as-is for the
>   "Because You Read" module — closes the "no homepage UI yet" gap Phase 6a's own spec had flagged.
>   Issue List sort/group confirmed merged into Library's own toolbar as `LibraryViewMode.IssueList`
>   ("Comic List") rather than living as a separate rail-nav screen — `IssueListScreenViewModel` is
>   composed inside `LibraryScreenViewModel`, `IssueListScreen.axaml` is embedded inside
>   `LibraryScreen.axaml`, confirmed via grep that no second toolbar exists. Also confirmed:
>   `LibraryContentGranularity`/`SearchMode` persistence and browse-history back/forward are real and
>   wired, not just schema.
>
> Full solution test run this sync: `Paperbunkr.Data.Tests` 300/300 and `Paperbunkr.App.Tests`
> 674/674 pass. `Paperbunkr.App.UiTests` initially showed 15/15 **failing** when run as part of the
> full-solution `dotnet test` — a real infrastructure bug, not a product regression: each of those
> tests launches a real on-screen app window via FlaUI, and the project had no parallelization guard,
> so xUnit's default parallel test collections ran several at once and let them steal each other's
> window focus. Confirmed by running one test in isolation (passed) versus the full suite (failed).
> Fixed by adding `xunit.runner.json` (`"parallelizeTestCollections": false`) to
> `Paperbunkr.App.UiTests`, wired into the `.csproj` so it copies to the output directory —
> confirmed fixed: re-run of the project alone (now serialized) is 15/15 passing, 4m35s.
>
> Working tree at this sync is **not clean** — it holds real, tested, previously-verified-live work
> from a 2026-08-19 session (Metadata Model review adoption R1–R4: `Series.ReadingStatus`,
> multi-value `SeriesTitle`, an AniList search-and-link flow (`MetadataLinkResolver`/
> `TitleMatchScorer`), and an `ArchitectureBoundaryTests` project-boundary guard), all confirmed
> wired into real UI (Library/Detail screens), not dormant entities. R5 (MangaDex second provider)
> and R6 (AniList tracker write-back sync) are sketched-only design specs, explicitly not built —
> R6 in particular is scoped by the user as "sketch now, build later," not to be started without a
> separate go-ahead. This working-tree state predates and is unrelated to the 17 commits above; it
> should get its own commit rather than being folded into this sync note. Also present: the
> already-known stale `current_alpha_todo.md` duplicate (untracked, not deleted this sync — pending
> the user's call) and five new untracked Metadata-Model-review design specs dated 2026-08-19
> matching R1–R6 above. Treat this file as re-synced as of `2d0692e`, with the caveat that the
> working tree still has real uncommitted work beyond it.

P0–P3 and P5 are done — shipped before this session (`f6bcee3`, `8e1bf55`), with P5 getting a
same-day follow-up (2D grid arrow-key nav, `34e1d39`). The `alpha` git tag already exists.

**P4 is now mostly done.** `275a348` and `0d08890` fixed the three hardcoded-literal findings
below directly (verified by re-grepping the source, not by trusting commit messages):
- `DetailTabs.axaml` — Issues/Related counts now bound to `Issues.Count`/`Related.Count` ✅
- `MainWindow.axaml` — Collections row now bound to `Library.Collections` with a real
  `"No collections yet."` empty state ✅; Duplicate Finder's hardcoded `"7"` badge and fake demo
  content removed, rail icon retitled to "Plugins" ✅
- ~~`Assets/avalonia-logo.ico` — **still open.** Still the default Avalonia project-template icon,
  still wired as the actual window icon (`MainWindow.axaml` line 11). Needs a real Paperbunkr icon.~~
  **Done** (see P4 section below) — replaced by `Assets/paperbunkr.ico`; re-confirmed present
  2026-09-19.

**P6 has substantial real progress**, not just the demo-data fix that was previously (wrongly)
credited to it. Since the doc was last written: `18d7ad8` (Reading Lists empty states), `8ace219`
(Book Folders scan toast), `275a348` (removed a decorative Favorite button with no command on
Detail, wired the previously-dead Reading Mode toggle on Reader), `0d08890` (Plugin screen dead
buttons → real empty state), plus a full Library toolbar (search/filter/sort/group/overlays) and
sidebar categorization pass that turned previously-decorative controls real. This covers most of
the rail-nav screens but hasn't been re-swept end-to-end against the original P6 checklist — see
the P6 section below for what's confirmed vs. still needs a look.

**Open: nothing — P0–P7 are all done as of 2026-08-10.** (History kept below for the record.)
- ~~**P4** — one item left: the app icon.~~ — **done**, see P4 section below.
- ~~**P6** — dialog close/save/cancel audit + full screen sweep.~~ — **done**, see P6 section below
  (two real gaps found + fixed: silent-discard on rail-nav, and a dead "▾ Maintenance" toggle).
- ~~**P7** — installer + real-device testing.~~ — **done 2026-08-10**, see P7 section below. Also
  turned up and fixed a real crash (file-association registry writes), not just a packaging
  exercise.
- ~~Manual interactive verification of the Reader zoom/pan gestures~~ — **done 2026-08-10**, user
  confirmed live: Ctrl+wheel/pinch zoom, plain-wheel pan/page-turn, click-drag pan, double-click
  zoom.
- Unrelated but landed since: `3e7ada3` fixed an unbounded memory leak (`CoverImageCache` now
  LRU-bounded) — not on the roadmap, worth knowing about. **That fix itself had a real bug, found
  and fixed 2026-08-09 evening, committed `04a1eb0` — see below.**
- ~~**New, not yet scoped:** Book Folders scan reads filenames only~~ — **done**, see below.
- Unrelated, also landed since: Novels (EPUB/PDF) support, Phases 1–3 (`3894723`, `2c3e140`,
  `8d94d11`, `25c664a`, merged via `5869ed0`) — tracked separately in `Paperbunkr-Roadmap.md` per this
  doc's own scope note above, not repeated here.

**Real bug found + fixed today (2026-08-09 evening session, committed `04a1eb0`) — a crash, not a
cosmetic gap:** `3e7ada3`'s LRU-bounding of `CoverImageCache` disposed evicted `Bitmap`s eagerly,
but `Get()` hands the exact same `Bitmap` instance to view models that bind it straight into a
still-visible `Image` control — browsing a large library (2000+ issues) evicts bitmaps still
on-screen elsewhere, and the next layout pass throws `ObjectDisposedException` out of
`Image.MeasureOverride`. Real repro: browse Library, then open Smart Lists → crash. Fixed in
`LruCache.cs` — eviction now only drops the cache's own reference, not an explicit `Dispose()`;
native memory still gets reclaimed via GC once nothing else references it. The test that had
asserted the old (unsafe) dispose-on-evict behavior now asserts the opposite. Confirmed fixed via a
live repro (Library → Smart Lists, no crash) and the full 312-test suite.

**Icon-pack sweep today (2026-08-09 evening session, committed `52a1ae6`):** every screen swept for
text/glyph standing in for icons (rail nav's `Li`/`Sm`/`Rd`/`Pl`/`Pf`/`Rx`, toolbar buttons, dialog
Save/Cancel, empty states, ~40 spots total) and wired to real icons from the user's `coolicons`
pack via a reusable `Border.icon` + `OpacityMask` pattern (`App.axaml`) so icons pick up the same
DynamicResource theming as text. One spot (`SmartScreen.axaml`'s "Add condition" button)
had the `Add_Plus.png` icon silently fail to render via that pattern for reasons not fully
root-caused (ruled out: Button/StackPanel layout, the asset file itself, and OpacityMask in
general — all confirmed working via a live repro at that exact spot; the same icon renders fine
elsewhere in the app) — replaced with two plain `Rectangle`s instead of chasing it further.
Genuine gaps in the icon pack (no arrow/chevron/caret assets) were left as their original text
glyphs (rail-nav back arrows, sort/group carets, reading-list move-up/down) rather than forcing a
bad fit.

**Real bugs found + fixed today (2026-08-09 afternoon session, not yet committed):**
- Book Folders scan never auto-generated cover thumbnails after adding new issues (had to find a
  separate manual "Generate Covers" button on the Library screen) — `ScanNow` in
  `PreferencesScreenViewModel.cs` now runs the same cover-generation pass Migration/Library already
  use, right after a scan finds new issues.
- Library screen loaded its data once at app startup and never reloaded on navigation — Smart
  Lists and Reading Lists already reload every visit, Library didn't. Surfaced as "migration
  didn't populate the library": the CE migration engine itself was verified working correctly
  (371 series / 2072 issues, tested directly against a real `ComicDb.xml`), but the Library screen
  only refreshed via the migration overlay's own "✕" close button — any other way out (e.g. "View
  Needs Review") left it showing stale pre-migration data. `MainViewModel.GoLibrary` now reloads
  from the database on every visit, matching Smart/Reading's existing pattern.
- `PageImageDecoder` had zero thread-safety: `RefreshCurrentPage()` (UI thread) and the Reader's
  background thumbnail-generation task raced on the same unsynchronized archive reader and cache
  dictionaries. Added a lock around `GetPage`/`GetThumbnail`.
- Reader thumbnail rail: `StartThumbnailGeneration`'s background loop captured its `for`-loop page
  index by reference in every `Dispatcher.UIThread.Post` closure instead of taking a per-iteration
  snapshot — a classic C# closure-over-loop-variable bug. The background loop races far ahead of
  the UI thread draining its queue, so queued closures read an already-advanced index by the time
  they ran, scrambling thumbnails onto the wrong tiles and leaving early pages (page 0 especially)
  permanently blank. This was the real cause of the blank-first-thumbnail report, not the
  `PageImageDecoder` race above (that was a separate, real bug found first, fixed too, but not the
  one actually causing the symptom — confirmed by instrumenting the code and reading a live trace
  from a real repro rather than assuming the first fix was sufficient).
- All four fixes have regression tests (301/301 passing); the closure-capture fix's test was
  proven to fail 3/3 against the old code and pass 5/5 against the fix before being accepted.

**Embedded ComicInfo.xml metadata + Migration relocation** (design spec:
docs/superpowers/specs/2026-08-09-embedded-metadata-and-migration-relocation-design.md,
2026-08-09 evening session; design spec committed `a12d9b0`, implementation committed `c4e7404`):
- Book Folders scan now reads embedded `ComicInfo.xml` via `IInfoStorage` (the mechanism already
  existed in the ported Engine, just never wired into the App layer — the original spec's "needs
  new archive-format plumbing" claim was wrong, confirmed with a real spike before writing the
  design doc). Embedded metadata wins per-field over filename parsing; full field set via a new
  shared `CeLibraryMigrator.MapStoryFields`, also used by Migration (behavior-preserving extract).
- New "Sync Metadata" action (`LibraryFolderScanner.SyncMetadataAsync`) — re-reads embedded
  ComicInfo.xml for issues *already* in the library and fills in currently-blank fields only,
  never overwriting anything already set. Added because the first version above only ever touches
  newly-scanned files; the user's real 2072-issue library was already fully migrated, so scanning
  found nothing new. Verified against the real library: found 88 issues missing `Writer`, checked
  5 of their actual files directly — all genuinely have an empty Writer field in the file itself
  (the CE database had it from some other source, never written back), so "no new metadata found"
  was confirmed correct, not a bug.
- CE migration's entry point moved from its own rail-nav icon into Preferences → Libraries,
  alongside Book Folders — the overlay itself is unchanged, just relocated.
- Generate Covers and Sync Metadata also moved into Preferences → Libraries (from the Library
  screen toolbar), next to Scan Now — all three "populate my library" actions in one place.
- New reusable live-progress toast (`ToastProgressViewModel`/`ToastProgressView`) — shows title +
  "X / Y comics" + a progress bar that updates in place via data binding while an action runs,
  closed programmatically (`WindowNotificationManager.Close(content)`, confirmed to exist via
  reflection before relying on it) when done, followed by a normal completion toast. Both Generate
  Covers and Sync Metadata use it.
- 312/312 tests passing; user confirmed all of the above working against their real library and
  real app.

**Housekeeping, not on the roadmap itself:**

**Worktree sweep 2026-09-04** (the branch/hash refs in the older sync notes above are all stale — a
lot of new worktrees came and went since):
- **Removed** (clean + HEAD already merged to `master`): `compassionate-banach-c6e8bf`
  (`9c6b0fd`, branch `claude/interesting-ritchie-e328cd`), `gallant-diffie-290bcb` (`1b6761e`,
  PR #36), `lucid-allen-96d8e0` (detached `497b689`) — the last one's git registration is gone but
  the on-disk folder was file-locked at removal time and is left as an orphan for a later
  `rm -rf` / reboot. Merged local branches `claude/interesting-ritchie-e328cd`,
  `claude/gallant-diffie-290bcb`, `claude/panorama-variable-width` deleted too.
- **Left in place, has uncommitted work — needs a human call:**
  - `exciting-hypatia-eecfc9` (branch `claude/admiring-merkle-3bbcc5`, `0de95e6`) — modified
    `src/Paperbunkr.Common/Win32/ShellRegister.cs` + an untracked `ShellRegisterTests.cs`. Looks
    like the "worth a dedicated regression test in a future pass" for the P7 file-association
    HKCU fix, started and never finished. Salvage the test or discard.
  - `quirky-borg-c5d364` (`6e8c9b7`, NOT merged) — the long-standing stale one; its commits
    (PageCanvas focus, Virtual Tags) are superseded by `8e1bf55`, still carries the same
    `LibraryFolderScannerTests.cs` edit. Been "safe to discard" in this doc for months — needs
    `git worktree remove --force` since HEAD isn't merged.
  - `serene-meninsky-08b528` (`1fd36bf`, merged) — one modified `TestAppBuilder.cs`, unclear
    provenance. Diff it, then discard.
- **Left alone, active:** `C:/Users/DeeDee/PaperBunkr-plugin-automation` (branch
  `plugin-api-gap-closure` — live Plugin API v2 gap-closure + IronPython work, pushed not merged)
  and `.claude/worktrees/agent-adacc7f060a74c906` (an in-flight agent session).
- **`current_alpha_todo.md` deleted** (`git rm`, staged) — the stale root-level duplicate of this
  file, flagged for removal since `c1e91a6`.

---

## P0 — Release prep (blocking the `alpha` git tag) ✅ Done

Shipped via `f6bcee3` ("Alpha catch-up ... #8"). Tag `alpha` exists in the repo.

- [x] Commit Preferences screen work — Appearance, Behavior, Libraries, Advanced tabs
- [x] Commit RTL page-turn navigation
- [x] Commit Issue Properties Editor
- [x] Commit Bulk multi-book editing
- [x] Commit Detail Screen selection-driven focus work
- [x] Split into commits by feature
- [x] Six rail-nav screens build/run after commits landed
- [x] Tag `alpha`

---

## P1 — Known gaps: core interaction bug ✅ Done

Shipped via `8e1bf55`.

- [x] **Fix `PageCanvas` requiring a click before arrow-key navigation registers** — root cause was
      the rail-nav screen switcher never re-firing `Loaded`/`AttachedToVisualTree`; fixed by
      reacting to `CurrentPage` changes instead and deferring `Focus()` to the next dispatcher cycle.

---

## P2 — Known gaps: feature completeness ✅ Done

Shipped via `8e1bf55`.

- [x] **Wire Virtual Tags into Smart Lists** — added `SmartListField.VirtualTag`
- [x] **Wire Virtual Tags into a display surface** — Virtual Tags pill row on the Detail screen

---

## P3 — Known gaps: consistency polish ✅ Done

Shipped via `8e1bf55`.

- [x] **Series.Genre vs Issue.Genre display pass** — full audit across Library grid/list, Detail
      Pills, and Smart Lists' filter fields (which were the actual bug — fixed to read the issue's
      own value instead of the series').

---

## P4 — Known gaps: placeholder content/assets ✅ Done

`f6bcee3`/`76fa3c6` fixed demo-*database*-seeding (fake Series rows on a fresh install) — a
different, narrower problem from the UI content sweep below, done separately. The UI sweep itself
landed via `275a348` and `0d08890`. Re-verified directly against source (not just commit messages):

- [x] Dummy text (lorem ipsum, sample labels, filler strings) — none found
- [x] Sample/mock data / hardcoded literals standing in for real bindings
  - [x] `DetailTabs.axaml` — counts now bound to `Issues.Count`/`Related.Count` (`275a348`)
  - [x] `MainWindow.axaml` — Collections row bound to `Library.Collections` with a real empty
        state; Duplicate Finder's hardcoded badge and fake demo content removed (`275a348`,
        `0d08890`)
- [x] Placeholder icons/images (default/stock art standing in for final assets) — **done
      2026-08-09 evening, committed `52a1ae6`**
  - [x] `Assets/avalonia-logo.ico` replaced with a real Paperbunkr mark (user-supplied artwork,
        flood-filled to transparent + packed into a multi-res `.ico`); wired as both the window
        icon (`MainWindow.axaml`) and `ApplicationIcon` in `Paperbunkr.App.csproj` (the exe/taskbar
        icon, which the old setup never set at all). Re-verified this sync: the file itself was
        renamed to `Assets/paperbunkr.ico` (old `avalonia-logo.ico` no longer present), and both
        `MainWindow.axaml`'s `Icon=` and the `.csproj`'s `ApplicationIcon` point at the new name.
  - [x] Rail nav's 6 text abbreviations (`Li`/`Sm`/`Rd`/`Pl`/`Pf`/`Rx`) and ~35 other
        glyph-standing-in-for-icon spots across every screen (toolbar buttons, dialog
        Save/Cancel, empty states, etc.) replaced with real icons from the user's `coolicons` pack
        — see the session note below for what's covered and one real bug found+fixed along the way

---

## P5 — Known gaps: full keyboard interactability (whole app) ✅ Done

Base audit shipped via `8e1bf55`; 2D grid navigation follow-up shipped today via `34e1d39`.

- [x] Tab order/focus traversal across all 6 rail-nav screens
- [x] Keyboard access for all dialogs (Issue Properties Editor, Bulk Editing, Preferences)
- [x] Visible focus indicators throughout
- [x] Standard shortcuts (Enter/Space to activate, Esc to close/cancel) wired consistently
- [x] Spatial 2D arrow-key movement through Library cards and Detail issue tiles (follow-up beyond
      the original P5 scope, per docs/superpowers/specs/
      2026-08-09-reader-gestures-and-grid-navigation-design.md)
- [x] 2026-09-29/30 follow-up (uncommitted): a shared `FocusReclaimer` keeps focus inside a screen across
      tab/section/collection resets - Library, Continuity, Books, Preferences, Wanted, the contextual sidebar, Home,
      Smart Lists, Detail, Manga/Book detail, Insights. Home keyboard reach (tiles, shelves, Spotlight carousel) is
      **user-confirmed on screen**. Paperbunkr's glow ring now replaces the theme's focus frame app-wide (one adorner
      style in `Primitives.axaml`, painted inside its own bounds; plain `ItemsControl`s and edge-hugging UserControls
      no longer clip it). Fixed along the way: arrow keys dead in every `WrapPanel` grid (`GridKeyboardNavigation`
      focused the item container) and in the Spotlight; closed modals' buttons were still Tab stops. App tokens moved
      to `Styles/AppTokens.axaml` so headless tests load the real ones. Verified: ring-clipping audit on 11 screens,
      render tests, mutation-checked key tests, full suite 4876/4876. Still to see on screen: every screen except Home,
      the sidebar path, reader focus-frame suppression. No headless setup: publisher/character detail, Plugin screen.
      Specs: docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-*.md

---

## P6 — Known gaps: make UI fully functional ✅ Done

- [x] Detail screen — decorative Favorite button (no command) removed (`275a348`)
- [x] Reader screen — Reading Mode pill was styled like a working toggle but had no command;
      now wired to a real LTR/RTL flip (`275a348`)
- [x] Reading Lists screen — empty states for "no lists" / "list has no items" (`18d7ad8`)
- [x] Library screen — toolbar (search/filter/sort/group/overlays) and sidebar categorization
      turned from decorative stubs into real controls; Book Folders scan now toasts on completion
      (`8ace219` + the Library Toolbar Phase A–D commits)
- [x] Plugin screen — fake Duplicate Finder demo content and dead buttons replaced with a real
      empty state (`0d08890`)
- [x] Confirm every dialog (Issue Properties Editor, Bulk Editing, Preferences) fully closes,
      saves, and cancels correctly from all entry points — **audited 2026-08-09 evening, committed
      `7f4b5eb`.** Traced (not just read commit messages) every navigation entry point and the
      Save/Cancel command bodies:
  - Issue Properties/Bulk Editing have exactly 2 entry points each (Detail's "Edit" toolbar button
    + DetailTabs' right-click menu), both funneling through the same `MainViewModel` methods — no
    divergent wiring found
  - Both editors' edit-buffer pattern is correct: `Load` copies fields off a disposed context,
    `Save` re-fetches and writes, `Cancel` never touches the database — confirmed by reading the
    command bodies directly, not assuming from the doc comments
  - The app-wide `Escape` handler correctly prioritizes migration overlay → Issue Properties →
    Bulk Editing and routes to each screen's real `CancelCommand`
  - Preferences has no Cancel concept by design — verified every toggle persists immediately via
    consistent `PersistBehaviorSetting`/`PersistVirtualTag` helpers, matching its doc comment
  - **Real gap found and fixed:** rail-nav buttons had zero `IsEnabled` gating, so clicking any
    other rail icon while Issue Properties/Bulk Editing was open silently discarded the in-progress
    edit with no warning — impossible in CE, whose equivalent `ComicBookDialog` is a true modal
    Windows dialog that blocks all other interaction by construction. Not a data-corruption risk
    (Cancel already discarded safely with no partial writes), but a real parity/UX gap. Fixed via
    `MainViewModel.TryLeaveCurrentEditor`: both edit screens now track unsaved changes
    (`IssuePropertiesScreenViewModel`/`BulkIssuePropertiesScreenViewModel.HasUnsavedChanges()`),
    and the six rail-nav commands route through a guard that shows a "Discard changes?" confirm
    banner instead of navigating away when the active editor is dirty. Deliberately *not* applied
    to Escape, which is already an explicit "cancel this" gesture. Along the way, also fixed a
    latent bug in `BulkIssuePropertiesScreenViewModel.Save()`: it never reset each field's
    `IsStaged` flag after writing, so `HasUnsavedChanges()` would've still read `true` immediately
    post-Save (harmless in practice today since `CurrentScreen` flips away first, but would have
    been a real bug for anything else that queried it). 12 new tests added (Paperbunkr.App.Tests:
    251/251 passing), committed `7f4b5eb`. Re-verified this sync directly against source
    (`MainViewModel.cs` line 124: `GoLibrary() => TryLeaveCurrentEditor(...)`), not just the
    commit message. Not yet manually clicked through in the live app — no desktop GUI automation
    available in this environment (same limitation noted for the Reader gestures below).
- [x] One more pass across all screens to confirm nothing was missed — **swept 2026-08-09 evening,
      committed `7f4b5eb`.** Structural search (not a manual click-through, see the note under the
      dialog audit above about why) across every `Views/*.axaml`: every `Button`/`CheckBox`/
      `ComboBox`/`ToggleButton`/`TextBox`/`MenuItem` for a missing command/binding, every
      `Cursor="Hand"` style for a matching gesture handler, and a grep for `TODO`/`FIXME`/
      `NotImplementedException`/empty command bodies. Found and fixed one real instance of the same
      "looks interactive, does nothing" pattern as the Favorite button and Reading Mode pill
      before it: the Smart Lists sidebar's "▾ Maintenance" section header (`MainWindow.axaml`) was
      a plain unbound `TextBlock` — the caret implied a collapse toggle that never existed, so the
      group was always shown. Wired to a real expand/collapse
      (`SmartScreenViewModel.IsMaintenanceExpanded`/`ToggleMaintenanceCommand`). Everything else
      found was either already correctly wired or an intentionally-disabled placeholder with its
      own explanatory tooltip (the 4 deferred external-tracker buttons on the Reading Lists
      screen — AniList/MyAnimeList/Auto-Build/Refresh). 1 new test added (252/252 passing).
      **Stale as of 2026-08-22:** Auto-Build (now "Search Story Arc…") and Refresh are real and
      wired now — see `Paperbunkr-Roadmap.md`'s "Reading Lists: story-arc auto-build" entry. AniList/
      MyAnimeList remain genuinely disabled/deferred (a different, unrelated tracker-sync feature).

---

## P7 — Known gaps: appshell + alpha build packaging ✅ Done

- [x] **Build/configure the appshell (installer) project** — **done 2026-08-09 night, committed
      `65fc777`.** Re-verified this sync: `installer/Installer.iss` and `installer/BuildInstaller.ps1`
      both present in the repo. Packaging approach: **Inno Setup** (`installer/Installer.iss` +
      `installer/BuildInstaller.ps1`), matching CE's own precedent
      (`_reference/ComicRackCE/Installer.iss`/`BuildInstaller.ps1`) rather than guessing at one —
      CE already ships this way. Two deliberate deviations from CE, both decided with the user
      before writing the script:
  - **Self-contained publish** (`dotnet publish -r win-x64 --self-contained`) instead of CE's
    detect-and-download-.NET-Framework-4.8 `[Code]` section — Paperbunkr bundles its own .NET 8
    runtime, so there's no prerequisite-install dance needed at all. Verified with a real test
    publish (not just assumed): 266 files, 229MB, and all native dependencies actually present —
    `x64\7z.dll`, `pdfium.dll`/`PDFiumSharp.dll`, `LibHeifSharp.dll`, the SQLite provider.
  - **No `[Registry]` file-association entries** in the installer, unlike CE's which writes the
    `.cbz`/`.cbr`/`.../.cbl` ProgID keys itself. Paperbunkr's own
    `FileAssociationService`/`ShellRegister.RegisterFileOpen` (Preferences → Advanced) already does
    the identical registry writes live, redirected to `HKCU` automatically by Windows for
    non-elevated processes — the installer doing it too would just be two systems racing to own
    the same keys. Installer only writes a minimal `App Paths` entry so the exe resolves by name.
  - **Install scope: per-machine** (`PrivilegesRequired=admin`, installs to Program Files) — matches
    CE, chosen over per-user even though the file-association piece above doesn't strictly need
    elevation.
  - No `LICENSE` file exists in the repo yet (CE's script references one), so `LicenseFile` was
    left out rather than inventing one.
- [x] **Produce a `setup.exe`** — **done 2026-08-09 night.** User installed Inno Setup 6 themselves
      (I don't install system software unilaterally, even with explicit permission — see the note
      above); `installer/BuildInstaller.ps1` then ran the publish + compile in one step, no script
      changes needed. Output: `installer/Output/PaperbunkrSetup-0.1.0-alpha-9cc0b62.exe`, 58.6MB
      (LZMA-compressed down from the 229MB unpacked self-contained publish). Not committed —
      `installer/Output/` and `installer/publish/` are gitignored build artifacts, regenerated by
      the script, not checked in.
- [x] Test clean install on a separate device (not the dev machine) — **done 2026-08-10, user
      confirmed: installs and runs correctly on a second PC.**
- [x] ~~Verify file associations register correctly post-install~~ — **real bug found + fixed.**
      Ticking any file-association checkbox in Preferences → Advanced crashed the app outright, on
      every machine, not just the freshly-installed one - not a packaging issue. Root-caused by
      reproducing directly (not guessed): `ShellRegister.RegisterFileOpen` (ported from CE,
      `src/Paperbunkr.Common/Win32/ShellRegister.cs`) writes through `HKEY_CLASSES_ROOT`, which -
      despite its merged *read* view - requires admin elevation to *create* a new key; .NET's
      `RegistryKey.CreateSubKey` resolves that write to `HKEY_LOCAL_MACHINE\SOFTWARE\Classes`, and
      the legacy UAC registry-virtualization fallback that would otherwise silently redirect a
      non-elevated write doesn't apply once an app manifest declares any `requestedExecutionLevel`
      - which CE's own `app.manifest` does (`asInvoker`), so **this bug exists in CE too**, just
      silently swallowed there by a bare `catch` in `FileFormat.RegisterShell`/`UnregisterShell`
      (confirmed by reading CE's source, not assumed) - CE's non-elevated users get a silently
      broken checkbox instead of a crash. Fix: every registry *write* in `ShellRegister.cs` now
      targets `HKEY_CURRENT_USER\Software\Classes` instead (no elevation needed, and it merges into
      the effective `HKEY_CLASSES_ROOT` *read* view, so `IsFileOpenRegistered` etc. needed no
      changes) - verified against the real registry twice: once confirming the original crash
      (`UnauthorizedAccessException` on `HKEY_CLASSES_ROOT\Paperbunkr.7zArchive`), once confirming
      the fix round-trips clean. Also wrapped the ViewModel command in try/catch with a real error
      toast, deliberately better than CE's silent swallow. 252/252 tests still passing (the
      registry-touching verification itself was a throwaway test, deleted after confirming - the
      existing `FileAssociationServiceTests` deliberately never touch the real registry, by design,
      per `IShellFileAssociation`'s own doc comment, and that boundary was kept). Installer
      rebuilt with the fix; new `setup.exe` sent to the user for retest.
- [x] Verify first-run experience end-to-end — **done 2026-08-10.** User confirmed: installed and
      ran all features smoothly on the second PC after the file-association fix landed.
- [x] Test uninstall leaves no orphaned state — **done 2026-08-10.** User uninstalled via
      Add/Remove Programs and confirmed nothing was left behind.

**Fixed in passing, found during self-contained win-x64 publish testing for this section:**
`LibHeifSharp` 3.2.0 ([Paperbunkr.Engine.csproj](../src/Paperbunkr.Engine/Paperbunkr.Engine.csproj))
is only the managed P/Invoke wrapper — it ships no native `libheif.dll`, and the LibHeifSharp
project deliberately leaves sourcing that binary to the consumer (confirmed against its docs and
its samples repo, neither of which bundle one). Unlike `7z.dll` (manually Content-included under
`x64\`), nothing was providing `libheif.dll`, so every `.heic`/`.avif` page threw
`DllNotFoundException` — pre-existing, not something packaging introduced, and apparently never
exercised end-to-end before. Added `LibHeif.Native.win-x64` 1.15.1 as a `PackageReference` next to
`LibHeifSharp` — it ships `runtimes/win-x64/native/libheif.dll` (+ `aom`/`libde265`/`libx265`
codec deps) via the standard NuGet native-asset convention, which `NativeInterop`'s
`runtimes/{rid}/native/` search path already picks up the same way it does for
`bblanchon.PDFium.Win32`'s `pdfium.dll`. Verified both a plain no-RID `dotnet build` (lands under
`bin/.../runtimes/win-x64/native/`) and a self-contained `win-x64` publish (flattened to the
output root, hitting the resolver's bare-filename fallback) place the DLL where the resolver
finds it. **Caveat:** `LibHeif.Native.win-x64` is an unofficial third-party package (publisher
"vforviolence"), not from the libheif or LibHeifSharp maintainers — a supply-chain trust call the
user made explicitly aware of the alternative (documenting the gap instead). `_reference/ComicRackCE`
wasn't available in-worktree to check how CE itself sourced this binary, so that side of the
standing CE-parity rule is still unverified.

---

## Bonus, ahead of schedule: Reader zoom/pan gestures ✅ Done

Not on the original P0–P7 list — pulled forward from the Beta "Reader polish" backlog today
(`4b1f6ed`) because trackpad pinch-zoom needed something real to control, per
docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md.

- [x] Ctrl+wheel/pinch zoom, anchored to the cursor
- [x] Plain wheel: pan while zoomed, page-turn while not
- [x] Click-drag pan (clamped at image edges)
- [x] Double-click to 2x zoom, centered on the click point / double-click to reset
- [x] Touch: 3-zone tap page-turn + horizontal flick
- [x] Manual verification — **done 2026-08-10.** User confirmed live, on top of the existing
      unit tests (`ZoomPanMathTests`, 13 cases).
- Remaining Reader polish backlog, still Beta scope: **fit modes/zoom presets/rotation shipped as
  a first slice 2026-08-10** (`963ef8c` design spec, `9769cfc` implementation — Original/Fit/
  FitWidth/FitHeight/BestFit, checked against CE's `ImageDisplayControl.GetScale` rather than
  guessed at, plus a Preferences → Reader tab). **Continuous/webtoon scroll and fullscreen shipped
  2026-08-11** (`b195f56` design spec, `103e3c3` implementation for continuous/webtoon scroll and
  rendering unification; `a2cfb7d` added F11 alongside F for the fullscreen toggle).
  **Position tracking/persistence, fullscreen chrome/overlays, live image adjustment, and
  background/margins all shipped 2026-08-11 in the same follow-on session** (design spec
  `b195f56`'s §6/§7/§9/§10) — user-verified live in the running app, not just via the test suite,
  including six real bugs found and fixed along the way: a resume-position bug (continuous mode's
  scroll position never followed a resumed/forced page index), an `ObjectDisposedException` crash
  reopening a second issue, F/F11 losing keyboard focus after a toolbar-button fullscreen toggle,
  a brightness color-matrix scale bug (SkiaSharp's `CreateColorMatrix` translation column turned
  out to be normalized -1..1 in this SkiaSharp version, not the legacy 0..255 the shipped package
  docs and CE's own GDI+ convention describe), a `SolidColorBrush` thread-affinity crash under
  xUnit's parallel runner (fixed by switching the static default to `ImmutableSolidColorBrush`),
  and a zoom-slider/double-tap path that could leave continuous mode's scroll/cross-axis pan
  pointing past the shrunk-down stack once zoomed below 100%, making the page appear to vanish.
  **Magnifier (§8) explicitly skipped this pass, per user direction** ("we have a zoom slider").
  **Page transition animations shipped 2026-08-15** (design spec 2026-08-13, Slide/Crossfade/None,
  off by default) — user-verified live; two real bugs found and fixed post-ship from that live
  testing (a live-refresh gap where the setting only took effect on the next book opened rather
  than an already-open one, and a real per-frame performance bug where crossfade rebuilt a full-
  resolution `SKImage` on every animation frame instead of once per transition), plus a Reader-
  toolbar quick-toggle added afterward per user follow-up (Preferences-only access felt hidden).
  **Double-page spread shipped 2026-08-16** (design spec 2026-08-15, `Single`/`Double` modes
  collapsing CE's three-way `PageLayoutMode`, global/series/issue-scoped setting, stateless local
  pairing test, spread rendering via a combined-virtual-size reuse of the existing single-image fit
  math, full integration with the page-transition system including RTL-aware spread placement, and
  a Crossfade reflow animation on layout/direction toggles) — 543 automated tests pass; **manual
  on-screen verification of the actual double-page rendering/pairing/reflow still pending** (no
  unattended desktop GUI automation available for this project, same standing caveat as every prior
  reader spec).
  **Remappable reader keyboard shortcuts shipped 2026-08-16** (design spec 2026-08-16, extends the
  P5 seam from 2 to 24 commands — pan/scroll/PageUp/PageDown/Home/End navigation, fullscreen, fit
  modes, zoom, rotate CW/CCW — verified against CE's actual keymap, `Key`→`KeyGesture` throughout
  for modifier support, Preferences' Keyboard Shortcuts split into Navigation/Zoom & Fit/Display
  sections with a new context-aware conflict check; new `Reader.RotateCounterClockwise`
  command+button, no CCW rotate existed before) — 550 automated tests pass (build clean, no new
  warnings); app verified to launch and stay running with the changes (no startup crash), but
  **on-screen verification of the actual keypress-to-action wiring still pending**, same standing
  GUI-automation caveat as every prior reader spec.
  **Reader auto-scroll / hands-free mode shipped 2026-08-16** (design spec 2026-08-16 — clarified
  that CE's actual `AutoScrolling` is an unrelated arrow-key-behavior switch for zoomed paged books,
  not a timer; built the modern webtoon-style passive scroll the "hands-free" backlog phrasing
  actually meant instead, layered on the existing continuous/webtoon mode and the just-shipped
  shortcut registry — a 25th remappable command, `S` default). `DispatcherTimer`-driven,
  `ClampScrollOffset` round-tripped from `PageCanvas` back to the ViewModel via the existing TwoWay
  binding rather than duplicated (a real architecture gap the design spec hadn't fully resolved,
  caught and fixed during planning), hard-stops on any manual scroll interaction or reaching the
  end, toolbar toggle shares the Double-page button's slot via complementary visibility (no new
  toolbar column) — 555 automated tests pass; app verified to launch and stay running, **on-screen
  verification of the actual scroll/stop behavior still pending**, same standing GUI-automation
  caveat. Split-page nav is now the only remaining open item in this backlog. Tracked in full in
  `Paperbunkr-Roadmap.md` per this doc's scope note at the top — not duplicated here.

---

## 2026-09-19 — Headless test-suite flake fixed (branch `fix/headless-test-dispatcher`)

Test infrastructure only; no production code or `<Version>` change.

- **Root cause (verified by logging thread ids):** `AvaloniaTestCollection`'s ctor bootstrapped Avalonia
  (binding `Dispatcher.UIThread`) on one xunit pool thread (tid 4) while test bodies ran on others
  (tid 22/26), so `CheckAccess()` was false. Per-class `PumpDispatcher()` guards then silently
  no-opped, and every "delete → assert sidebar" test saw a stale, un-drained `Dispatcher.UIThread.Post`
  queue; whether it passed depended on which classes ran first. The same thread mismatch is the
  full-suite mass-fail (~1000 tests).
- **Fix:** `PinnedThreadTestFramework` (custom xunit framework) runs every test case's synchronous
  execution on one dedicated thread that also runs `AppBuilder.Setup()`; `TestDispatcher.Drain()`
  throws instead of no-opping. Gotchas found: (1) a captured `SynchronizationContext` on that thread
  deadlocks the six test files that block on `GetAwaiter().GetResult()`; (2) Avalonia's Setup installs an
  `AvaloniaSynchronizationContext` that xunit's `AsyncTestSyncContext` forwards into the unpumped
  dispatcher, hanging tests, so the pinned thread clears it before each work item.
- **Test fixes:** added drains to Smart/Reading/ActivityCenter/Preferences delete-flow tests; DetailTabs
  and Events now use the shared helper; `ReaderScreenViewModelTests` thumbnail test no longer silently
  skips itself off-thread. `Results_MapCoverBrush…` was a separate stale assertion (PR #90 made
  `CoverBrushFor` return `Immutable*` brushes); now asserts `IGradientBrush`, same colour checks.
- **Verified:** narrow and wide filters twice each; full suite 2794/2794 twice. **Not done:** reversed
  or shuffled class order (the collection orderer is fixed and Avalonia-first by design).

## 2026-09-25 — Library organizer audit + automatic role detection

Two specs, both built this session: `docs/superpowers/specs/2026-09-25-library-organizer-audit-fixes-design.md` (+ `-plan`) and
`2026-09-25-reading-list-role-detection-design.md` (+ `-plan`). Tests were written alongside the code, not strictly before it; **nothing
here has been viewed on screen or run against live ComicVine/Metron.**

### Organizer (audited against the real Library Organizer 2.1.13 plugin source, supplied by the user)
- **Defects fixed** (each has a test): empty publisher/imprint no longer roots the path and drops the base folder; `/` `\` inside a value no
  longer nests folders; a file already at its calculated path is left alone (no move, no undo row, no write-back); two books headed for one
  path reach the collision resolver (also in Simulate); Replace goes to the Recycle Bin and leaves the replaced library entry without a file;
  a failed library update moves the file back; Simulate/Copy no longer queue write-back; a bad token or empty file name fails that book only
  with a reason; undo reverses only the latest run and removes the folders it created; the scheduled/interactive job no longer ends
  "Cancelled" on an exception.
- **Plugin parity added:** `manga`/yes-no polarity, `first(Series)` plugin field names, decimal/negative/auto-width padding, `(sep)(issue|series)`
  multi-value tokens, `UseFolder`/`UseFileName`, `EmptyFolder`, `EmptyData`, skip-on-empty-fields, excluded folders, ANY/ALL exclude match,
  Windows reserved names and the 259-character limit, save-time template validation, pre-run preview, saved run/simulation reports.
- **Deliberate deviations:** unknown token fails the book (plugin leaves the text in the path); bare date renders ISO; empty-folder cleanup
  is bounded to the base folder; Copy does not add the copy to the library; a Replace leaves the old entry fileless instead of deleting it;
  the importer keeps its own no-decimal-padding behaviour (`TemplateContext.PadDecimals`).
- **Follow-up round (same day), all built:** nested exclude groups in the editor (`ExcludeGroupViewModel`, recursive template, any depth; a new
  rule defaults to "Any", the plugin's default); a live preview in the profile editor (a few real comics, one per series, redrawn as you edit,
  `LibraryOrganizerService.PreviewAsync` reads only the sample's series; the exclude RULE is not evaluated there, excluded FOLDERS are); several
  profiles per run (`PlanManyAsync`/`ExecuteManyAsync`: every Copy profile copies, only the last Move profile that can place a book moves it,
  copies run first, one undo batch, one Activity job; the picker is now a multi-select and the scheduled task runs every profile marked for it).
- **Found while doing this (fixed):** the organizer read `Effective*` values (number, year, volume, format, title) from `issue.MetadataProposals`
  and the genre/tags/custom tokens from collections that no caller ever loaded, so a comic whose values were only parsed from its file name
  (or that had tags) produced empty template values. `PlanAsync` now evaluates every book through a fully loaded copy of itself. Tests
  `AComicWhoseNumberAndYearWereOnlyParsedFromItsFileName...` and `GenreAndTagTokens_AreFilled...`.
- **Not done:** fileless-book thumbnail export; undo does not re-queue write-back (moving a file does not change its contents); the recursive
  group template and the preview panel were checked only through view-model tests and template registration - the headless test app loads no
  theme, so item controls cannot be rendered there. **Look at both on screen.**
- **Unverified:** how the folder watcher reacts to moves into folders outside the watched library; whether an uncaught exception in an async
  command is swallowed globally.
- Migration `AddOrganizerParityOptions` (profile options, `OrganizeBatches.BaseFolder`); `UseFolder`/`UseFileName` default to true for existing profiles.

### Role detection
- `MemberRoleDetector` (pure) + `MemberRoleApplier` + `MemberRoleDetection`; migration `AddRoleDetectionColumns` adds `RoleSource`, `RoleReason`,
  `SuggestedRole`, `SuggestedReason`, `RoleSuggestionDismissed` to `EventMemberships` and `ReadingListItems`. A null source is treated as the user's.
- High-confidence roles apply only into an empty or automatic slot; existing event members (whose default Core is indistinguishable from a
  choice) only ever get a suggestion. Comic Book Reading Orders now keeps its section headings (h2-h6) and blue notes as `ArcIssue.Annotation`.
  Every UI path that sets a role marks it as the user's. "Detect roles" on events and lists; Activity Center job + toast.
- **Fixed on the way:** the Comic Book Reading Orders parser replaced `</p>`/`<br>` with an unclosed `<span style="color:`, which the tag strip
  then closed at the next `>` - swallowing text between two boundaries. Boundaries are now plain newlines; the existing parsing tests still pass.
- **Providers (follow-up):** neither ComicVine nor Metron has a role field (ComicVine's documented issue and story-arc fields contain none;
  Metron's docs could not be fetched). What they do give is used: ComicVine's per-issue `name` and `deck` now ride on `ArcIssue.Title`/`Summary`.
  The title is a high-confidence signal and, unlike the library's own title, exists for issues you do not own yet; the deck is only a low-confidence
  suggestion (prose, not a label). Metron's row parser reads a story title if the row has one - **unverified**, silent when absent.
- **Not done:** no Wikidata/Fandom lookup (nothing there is per-issue); the low-confidence first/last-of-a-long-arc rule may prove noisy.

### Scraper follow-ups recorded earlier this session
- Metron now supplies story-arc reading order too (`/arc/{id}/issue_list/`, unverified against a live response); the ComicVine search
  clean-up and fuzzy retry are ComicVine-only; Metron search is still punctuation-sensitive.

### 2026-09-25 (late) — duplicates after an organize, and "keep the file"
User report: after an organize, every issue of a series appeared twice; deleting a series deleted the files; resolving the "duplicate" deleted the
comic, and restoring it brought the duplicate back. **Cause not confirmed against the user's data** (a read-only copy of the live WAL database was
inconsistent, so nothing was read from it). What was verified in code:
- `LibraryDeletionHelper.RemoveIssue` sent `issue.FilePath` to the Recycle Bin without checking whether ANOTHER entry (the one being kept) points at
  the same file, so resolving a duplicate group of two same-path entries deleted the kept entry's file. Fixed: the file and the removed-paths
  list are only touched by the last entry that references a file.
- `AppSettings.DontReimportRemovedFiles` defaults to **false**, so the removed-paths list was recorded but never consulted; a restored file was
  re-imported at the next scan/watch event ("the duplicate returns").
- Copy mode into a watched library folder makes the watcher import every copy as a duplicate comic. A Copy profile is now refused up front
  (`OrganizerSafety`) with a plain explanation and nothing copied.
- New: "Merge entries that share one file" (Library Health → Duplicate Files; `SamePathEntryMerger`, touches no files, hands list/event memberships,
  reading progress and rating to the kept entry); "keep the file" variants everywhere entries are removed (tile/series context menus, duplicate
  groups: "Remove extras, keep files"); `RemovedFilePath.KeepFile` (migration `AddRemovedFileKeepFlag`) makes a kept-file removal stick even
  with the setting off.
- **Not done:** the folder-watcher race (an organize Move whose new path the watcher imports before the row is updated) is still unproven; the
  action-bar "Delete" and Book screens have no keep-file variant yet.

## 2026-09-26 - Library preview panel v2

Design [2026-09-26-library-preview-panel-v2-design.md](superpowers/specs/2026-09-26-library-preview-panel-v2-design.md), plan alongside it. Not committed.
- **Fixed:** the series issue rail rendered as thin slivers (its items carried only a placeholder brush; `PosterRailItem` now has `CoverKey`/`IsRead`
  and `PosterRail` loads through `AsyncCoverImage`, cover box has an explicit width); the hero cover was cropped to a landscape strip (now the
  whole cover at its own aspect ratio).
- **Built:** backdrop-hero series state and collapsible-section issue state, pinned action bars (series: Continue/Read, Mark all read/unread,
  Add to Collection, overflow; issue: Read, Mark read/unread, Edit, overflow), rail click drills into the issue with a "back" link, multi-select
  strip, persisted collapsed sections (`AppSettings.LibraryPreviewCollapsedSections`, migration `AddLibraryPreviewCollapsedSections`), 120 ms hero
  cross-fade (skipped on rapid focus changes and reduced motion), column min width 280.
- **Verified:** `Paperbunkr.App` builds with 0 errors and no XAML diagnostics on the touched files; 11 new view-model tests
  (`LibraryPreviewPanelViewModelTests`) pass; the Library/ContextMenu/PosterRail/Detail App test filter passes (703 tests); the generated migration is a
  single `AddColumn` and its Designer differs from the previous one only by that column. Rolling a scratch database up to head and back down with `dotnet ef`
  reverts the new migration and every one after `AddWorkspaces` cleanly.
- **Existing failures found (not from this work):** `Paperbunkr.Data.Tests` has 7 failures. Six are down-migration tests that target old migrations: reverting
  `20260903082832_AddWorkspaces` throws `no such column: "LibraryGroupField"` (reproduced with `dotnet ef database update 20260827045244_LibraryPosterGridConsolidation`),
  so any test that rolls back past it fails. The seventh is `RemoteRowIsolationTests.OptInSites_AreExplicit_AndMatchTheAllowlist`, which I did not investigate.
- **Seen on screen by the user (2026-09-26):** the whole panel works - hero cover and backdrop, chips, progress line, section headers, pinned action
  bars, issue rail with real covers for the whole series (a first version built the rail from the filtered grid rows and showed one cover; it now uses the
  projection's unfiltered rows), issue state and collapsible sections, drill-in and back link, and the flyouts; the last two cosmetic fixes (empty title row under "ISSUES" hidden in `PosterRail`, rail given clearance above its scrollbar) also confirmed. Not looked at separately: Detail-screen rails
  (their cover box now has a fixed 76 px width) and a short window height.
- **Deviations from the spec:** see its "Implementation notes" (width default kept at 320, no rail double-click, no `PanelSection` control).

## 2026-09-26 - About polish

Design [2026-09-26-about-polish-design.md](superpowers/specs/2026-09-26-about-polish-design.md), plan alongside it. Not committed.
- **Built:** About split into Overview / Changelog / Legal & notices tabs (`AboutTabs`, search anchors `about.project`/`about.legal` added);
  header with version once + **Copy version info**; Project group (GitHub, wiki, report an issue, releases, logs/data folders). One markdown parser
  (`MarkdownLite`, replaces `LegalDocumentParser`) and one renderer (`Controls/MarkdownView`) behind the changelog and the legal viewer; shared
  `Views/ChangelogEntryView` for About and What's New with coloured category tags. `ChangelogSelection` hides empty entries and picks the Current
  entry as the newest not newer than the running version. Legal viewer 680 px with "Last updated", Copy text, in-document links; `LICENSE`
  preformatted. Update-available overlay links to the offered version's release page instead of showing the installed changelog.
- **Texts:** `PRIVACY.md`, `TERMS.md`, `COMICVINE_NOTICE.md` rewritten from two code audits (update check and daily Wikidata task are on by default
  and now disclosed; DPAPI; sharing; plugins; real ComicVine/Metron throttles); new `THIRD-PARTY-NOTICES.md` (bundled via the csproj);
  0.7.0 changelog bullets split.
- **Fixed on the way:** empty "Unreleased" row in About; no Current badge between releases; About → What's new opened the empty Unreleased entry;
  What's New rows had stock ToggleButton chrome (its styles lived only in AboutSection).
- **Verified:** forced-`CoreCompile` build clean; 310 targeted tests pass (new `MarkdownLiteTests`, `ChangelogSelectionTests`,
  `AboutPolishHelpersTests`, About VM tests); the changelog and all five documents rendered headlessly with real Skia and checked by eye (link
  baseline fixed after the first render). **Seen on screen and confirmed by the user (2026-09-26).**
- **Follow-ups for the user (not done here):** PDFiumSharpV2 is MS-RL (FSF: GPL-incompatible); `libx265.dll` ships via `LibHeif.Native.win-x64`
  (GPL-2.0-or-later, listed in the notices); the unreferenced copies in `src/Paperbunkr.Engine/DLL/x64/`; `sharpPDF.dll` provenance; the CE
  publisher-pack/texture licensing; user-agent strings point at two different GitHub URLs.

## 2026-09-26 - Library focus ring + cosmetic toggle audit

Not committed.
- **Ring missing its top edge outside Matrix (the 2026-09-03 report, never actually fixed):** poster cards were arranged ~21 px shorter than their
  content (`PosterCardHeight` ignored `Border.posterCover`'s 15 px ring gutter, and the 34 px title allowance was below the real ~40 px row), so
  the star row squeezed the cover ~10 px upward and the ring's top edge landed outside the card, where only a full-window redraw paints it. Matrix
  looked fixed only because its code rain redraws the whole window every frame. `PosterCardHeight` now counts the gutter; the title row is pinned
  (`PosterTitleTextHeight`, 34 + 8 margin) so its height no longer depends on the theme font. Rows are ~23 px taller as a result.
- **Grey box behind the hovered card's title:** the Fluent button template's own pointer-over/pressed background; `Button.card` now keeps it transparent.
- **View & Sort toggles that did nothing in the current mode are now shown only where they work:** Dog-ear / Numeric rating / Tooltips (Poster +
  Panorama; Dog-ear was also shown in Tiles), Language badge / flag (List + Tiles), Unread / Publisher badge (not the Details table), Continue
  reading (series Poster / Panorama / Tiles only). Collection tiles' title now follows "Show titles" (it was always on while the height math dropped it).
- **Verified:** build clean; 7 new tests in `LibraryPreviewPanelViewModelTests` (scopes, card height); the Library App test group passes (440).
- **Not verified:** on screen - ring top edge in a non-Matrix theme, the hover box gone, row spacing.

## 2026-09-26 - Library page audit fixes

Not committed. From a gap audit of the Library screen (user screenshot + code read), all fixed:
- **"0 MB" / empty file fields:** nothing ever wrote `Issue.FileSize`/`FileModifiedTime`/`FileCreationTime`, so the status bar, the File
  Size/Modified/Created sort + columns, Smart List rules on them and the preview panel's series size were all empty. New
  `Paperbunkr.Data/Library/IssueFileStats` (CE `ComicBook.RefreshFileProperties` parity: `Length`/`LastWriteTimeUtc`/`CreationTimeUtc`) runs on
  scan, on CE migration (CE's cached values first, live file wins), after a CBZ metadata write-back, and as an idempotent startup backfill
  (`MainViewModel`, background thread) for rows with no size. Folder comics keep no size (FileInfo on a directory).
- **"1 issues":** series cards, collection series tiles and the Detail header now singularize (`SeriesCardSample.FormatIssueCount`).
- **Sort order:** Series/Title/Story Arc/Series Group/Alternate Series now use CE's `ExtendedStringComparer` with IgnoreCase|IgnoreArticles
  (CE's `ComicBookSeriesComparer` etc.); File Path/Name/Directory use it without articles. Natural numbers ("Vol 2" < "Vol 10"). CE's default
  article list is "the, der, die, das, le, la, les, l'" - not "a/an". The A-Z rail, its click target and the Alphabetical group use the same
  article skip (`AlphabetIndexEntry.LetterFor`), so "The Flash" lives under F everywhere.
- **Tiles density:** the Grid density slider now shows in Tiles too (`IsGridDensityScope`), which already scaled with it.
- **Series selection bar:** gained Mark Read / Mark Unread / Add to List (every issue of the selected series, card order then issue number -
  `SelectionBarIssueIds`). Plugins stays issue-only.
- **Collection sidebar rows:** hover actions overlay the row instead of reserving ~60 px at Opacity 0 (names trimmed early); counts right-aligned
  and vertically centred (Content Type counts centred too); actions still Tab-reachable (`:focus-visible`).
- **Status bar** says "issues", not "comics".
- **View & Sort popup** fades its bottom edge while settings remain below the fold (OpacityMask, theme-independent); its scroll
  bar is hidden (wheel/keyboard still scroll); "Show titles" now uses the rows' 12.5 size.
- **"I enable the cosmetics and don't see them work" - two real bugs:**
  - *Dog-ear preview never showed on Poster cards* since the 2026-09-19 LazyPart change: `FindDogEarPeekImage` searched only the
    cover Grid's direct children, and the Poster peek Image now sits inside a `LazyPart`. Finder looks inside it (plus a one-tick
    retry). Also: an unknown `PageCount` (93 of the user's 3,616) is now eligible and the page-2 decode decides - deviation from
    CE, which always knows the count.
  - *Fade in thumbnails never visibly faded:* Opacity was set 0 then 1 in one tick with the transition already attached (animated
    1 -> 1), and cache hits never faded at all - with the prefetching grid cache that was nearly every paint. Now CE's
    `ThumbnailViewItem.Animate`: 300 ms linear, on a cover's first appearance whether cached or not, once per session per cover
    (recycled cards don't re-fade; switching the toggle on resets that), skipped under Reduced Motion.
  - Numeric rating badge and Tooltips were wired correctly; the badge only shows on rated issues (4 in the user's library).
- **Not the app:** the "SP" bubble over the toolbar in the screenshot is another program's overlay.
- **Verified:** build clean; new tests `LibraryAuditFixesTests`, `IssueFileStatsTests` (Data), 2 series-selection tests in
  `LibraryScreenViewModelTests`, 4 new `AlphabetIndexEntryTests` rows.
- **Not verified:** on screen - sidebar rows, popup fade, series selection bar; the startup backfill against the real library.

## 2026-09-27 - Event Map

Design [2026-09-25-event-map-design.md](superpowers/specs/2026-09-25-event-map-design.md), plan alongside it. Not committed.
- **Built:** Map view for Story Events (**Members | Map | Timeline**). There's a spine (trunk) lane when a member series matches the event
  name, otherwise a relay chain, with compact column packing. It has tie-in, sequence and continuity edges, three density stops,
  All / Spine only / Hide optional filters, and a spine picker saved as `StoryEvent.SpineSeriesId` (migration `AddStoryEventSpine`;
  null = auto, 0 = forced relay). Cards are virtualized, real controls (UIA names). A custom-drawn edge layer reads skin colours
  at render time, with caches dropped on skin or theme change. There's an inspector drawer (connections, segment order, open reader,
  mark read/unread, series details), keyboard navigation, Ctrl+wheel density, and "Events in this continuity" with **Open map** on
  continuities. The reader now takes an explicit story-event anchor: paging, the staged next issue, the end card and the context strip
  follow the event's order when it's opened from the map.
- **Verified:** App builds with 0 errors, and the compiled-XAML index contains `EventMapView` and its themes. Final targeted run:
  590/590 (all Event Map tests, `ReadingOrderResolverTests` incl. 6 new event-anchor tests, `ReaderScreenViewModelTests`,
  `NextIssueStager*`, `EventsScreenViewModelTests`, `MainViewModelTests`). The migration is a single `AddColumn`; its forward-only
  test passes. Data suite: 1851 pass, 6 fail, all pre-existing down-migration failures (see the 2026-09-26 preview-panel entry).
- **Not verified:** on screen - look and feel, scroll smoothness at the Covers stop, reader round trip. A full App-suite run died
  about 61 minutes in on "not enough space on the disk": `%TEMP%` holds ~56k leftover `paperbunkr_*` test files (9.3 GB). The failures
  it reported were re-run: all pass except two load-timing flakes (a remote-reading loopback timeout, and one guided-view test that
  passes on its own).
- **Deviations:** see the design's "Implementation notes" (year-stripping name match, "Series details" instead of "Issue details",
  no tracker auto-sync on map mark-read, and more).

## 2026-09-27 - Story Event resolver (ComicVine/Metron duplicate events)

Design [2026-09-27-story-event-resolver-design.md](superpowers/specs/2026-09-27-story-event-resolver-design.md), plan alongside it. Not committed.
First of two sub-projects; the continuity-wide map is next.
- **Built:**
  - **Id completion** (`StoryEventIdCompletion`): member issues' arc credits > Metron `cv_id` > a name search checked against the
    arc's issue list. Metron's ArcSerializer carrying `cv_id` was verified against Metron's source.
  - **Matching and merging** (`EventNameKeys`, `StoryEventIdentityResolver`, `StoryEventMerger`): silent merges only between two
    provider-origin events that share an id or match name + ≥ 50 % issues; everything else goes to review.
  - **The sweep** (`StoryEventIdentitySweep`) and the weekly **Match duplicate story events** task (priority 17, default on).
  - **Sidebar:** "Possible duplicates" (Merge / Not the same / Check again, deferred), plus **Find duplicate events**.
  - **Prevention:** create-or-reuse, suggestion grouping and verification match name keys and aliases, so a merged-away
    spelling isn't re-created.
  - **Other:** a rename marks an event User; a background check runs after accept/look-up.
  - **Migration** `AddStoryEventIdentity`: `Origin` backfilled Provider where an arc id exists, alias and dismissal tables.
- **Verified:**
  - Data: 60/60 (new resolver tests, migration test, existing resolver/grouping/verification suites).
  - App: 237/237 (`EventsScreenIdentityTests`, `EventsScreenViewModelTests`, Event Map, scheduled-task catalogue checks, Issue Properties, organizer).
  - The migration `Up` holds only these changes.
- **Not verified:** live ComicVine/Metron behaviour on your library (no network in tests); the sidebar on screen.

## 2026-09-27 - Continuity map + smart connector

Design [2026-09-27-continuity-map-design.md](superpowers/specs/2026-09-27-continuity-map-design.md), plan alongside it. Not committed.
Second of three sub-projects (resolver done; a bundled GCD extract is next - the user has a GCD account and will provide the dump).
- **Built:**
  - **Continuity map:** continuities get **Series | Map | Timeline**. Every issue of every event touching the continuity, with events
    in chronological order and loose issues in "Between events" bands (publication order in year bands when there are no events).
    It has event bands on the ruler (click opens that event's map), connector arrows/brackets between bands (hover for the
    reason), Also-in links, outside-continuity lanes, an Events picker, Events only, and a pending-duplicates note.
  - **Smart connector:** local inference (direct continuation, name patterns, shared-series order; weak same-name / date+word /
    crossover suggestions) and Wikidata follows/followed-by. Strong results are saved as inferred/Wikidata relations; weak ones
    appear as typed "Looks like the sequel of …" suggestions (Accept / Dismiss). Removing an automatic relation dismisses the pair.
  - **Other:** the weekly task and sidebar button are now **Check story events** (resolver, then connector). Migration
    `AddEventChronology`.
- **Verified:**
  - Data: 123/123. App: 349/349 (Event Map + continuity map, Events screen, identity, scheduled tasks, Issue Properties,
    resolver, MainViewModel).
  - Live Wikidata, read-only: Planet Hulk (Q2526264) → World War Hulk (Q1048144), including the "nothing" placeholder.
  - Migration `Up` holds only these changes.
- **Not verified:** on screen (bands, connectors, hover, the Events flyout); the Wikidata matching against the real library.

## 2026-09-27 - Grand Comics Database data

Design [2026-09-27-gcd-data-design.md](superpowers/specs/2026-09-27-gcd-data-design.md) (implementation notes at its end), plan
alongside it. Not committed. Third of three sub-projects.
- **Built:**
  - **Tool** `tools/Paperbunkr.GcdExtract`: dump → extract + zip + `gcd-data.json`. Run on the 2026-09-15 dump: 229,836 series,
    2,203,939 issues and 6,798 bonds; 97.7 MB extract, 33.7 MB zip. The manifest is at the repo root and embedded in the app.
  - **Optional download:** Preferences → Connections → **Grand Comics Database data** (Download / Check for update / Remove,
    credit and licence), installed to `%AppData%/Paperbunkr/gcd` after size, SHA-256 and schema checks.
  - **Matching:** the scraper stores Metron's GCD issue id. Series are matched by those ids, by Metron's series record (capped),
    or by a unique name + start year + publisher. Issues are matched by number. Weekly task **Match series to GCD**.
  - **Uses:** GCD bonds become Continuation / Reboot / Related series relations with GCD evidence, and deleted ones stay deleted.
    The Related tab shows a GCD mark and "Not in your library" lines with comics.org links. Matched issues' on-sale dates drive
    event chronology and continuity-map order. Continued series sit in adjacent lanes marked "continues as ↓".
  - **Credit:** a Data section in `THIRD-PARTY-NOTICES.md`; `PRIVACY.md` updated, including the weekly "Check story events"
    Wikidata lookup it had been missing.
  - Migrations `AddGcdIds` and `AddSeriesRelationDismissals`.
- **Verified:**
  - Data: the full suite, 1971/1971. An earlier run had one failure that didn't come back on the re-run; its name wasn't
    captured, and every group these changes touch (migrations, relations, chronology, connector, Metron, scraping: 329 tests)
    passed on its own.
  - App: 398/398 (installer with a fake HTTP handler, Preferences row states, Related-tab GCD lines, scheduled tasks, Preferences,
    About, Detail tabs) and 113/113 Event Map / continuity map.
  - The real extract was spot-checked: counts match the trial, and the meta table carries the licence and dump date.
  - Both migrations' `Up` hold only these changes.
  - **Published** 2026-09-27 at [heisehis/paperbunkr-gcd-data](https://github.com/heisehis/paperbunkr-gcd-data) (public, CC BY-SA 4.0,
    homepage → Paperbunkr): release `gcd-2026-09-15` with the zip. The raw manifest and the zip's size and SHA-256 were checked by
    downloading them. The app's manifest URL and the bundled `gcd-data.json` point there.
- **Not verified:** on screen (the Preferences row, the Related-tab chips, the lane marker); a download from inside the app;
  matching against your own library.

## 2026-09-28 - Continuity screen redesign

Design [2026-09-28-continuity-screen-redesign-design.md](superpowers/specs/2026-09-28-continuity-screen-redesign-design.md)
(implementation notes at its end), plan alongside it. Not committed.
- **Built:** a new ContinuityScreen replaces the Story Events screen, all five phases, old screen deleted.
  - **Sidebar:** Continuities | Events switch (remembered, AppSettings.ContinuitySidebarTab), publisher logos and issue counts,
    one **Suggestions & checks** row + panel replacing the three sidebar queues.
  - **Continuity page:** hero band (cover collage, stats, GCD chip, Continue, Add series, Manage), Needs attention, series as runs
    with GCD "Not in your library" placeholders or a draggable custom wall, Events in order, Compare & merge overlay.
  - **Event page:** hero (stats, source and continuity chips, Continue), Follows / Followed by strip, Needs attention, filtered
    reading list with read ticks, Related events, Issue suggestions.
  - **Timeline:** year histogram, era counts / progress / colours (PbEra* tokens, optional 	heme.json keys), folding.
  - Publisher suggestion box with logo in the New/Edit continuity dialog.
- **Verified:** App 423/423 across everything touched after the deletion; a full App run before it was 4,660/4,679, with every
  failure outside this work (scratch-OutDir path/pdfium tests and timing tests that pass on re-run). Data migration test 2/2.
- **On screen (the user):** works. Follow-up fixes: hero height, publisher logos (text off, contrast plate), a stack-overflow crash
  in Related events (two-way `Run` binding with StringFormat), clipped attention flyouts, wider histogram bars on short spans.
- **Not verified:** drag-to-reorder, compare overlay, light-skin era colours, keyboard/screen-reader passes.

## 2026-09-29 - Insights History tab (Mihon-style reading history)

Design [2026-09-29-insights-reading-history-design.md](superpowers/specs/2026-09-29-insights-reading-history-design.md),
plan alongside it. Not committed. CE has no equivalent (only a Recent Books menu and a "Recently Read" smart list), so this is a
deliberate deviation.
- **Built:** a new **History** tab on Insights (Today · Trends · History · Recap).
  - **Rows:** one per comic series / book series / standalone book at its latest read, under TODAY / YESTERDAY / weekday / date headers.
  - **Filtering:** search plus All / Comics / Manga / Books chips.
  - **Actions:** ▶ resumes, or reads the next issue once one is finished; PDFs get Open; "CAUGHT UP" when there's nothing next.
  - **Removed items:** deleted series/books stay as greyed "NO LONGER IN LIBRARY" rows.
  - **Remove / Clear all:** these only *hide* rows. A new `ReadingEvent.HiddenFromHistory` flag is read by History alone, so stats, goals and Recap never change.
  - **Migration:** `AddReadingHistoryColumns` adds `SeriesTitle`/`ItemLabel` name snapshots (filled by the recorder, backfilled for existing rows) plus the flag.
- **Verified:** new/affected suites green:
  - Data 5/5 (backfill, migration, existing ReadingEvent backfill/migration);
  - App 62/62 across recorder, resolver, History VM, Insights VM/focus and a headless view render.
  - The view was also rendered headlessly to PNG and eyeballed.
  - Full suites: Data 2,028/2,029 and App 4,848/4,850.
    - The Data failure was `RemoteRowIsolationTests`' opt-in allowlist catching the two new `IgnoreQueryFilters` sites (resolver + recorder). Both were reviewed as read-only and added to the allowlist, and it now passes.
    - The two App failures (`MatrixRainOverlayRenderTests`, `ReaderScreenPanelTests` guided-view double-click) pass on re-run and sit outside this work.
- **Not verified:** on screen in the running app, light skins, keyboard/screen-reader passes, a real large library.
- **Backlog it created:** save the PDF reader's position (PDF rows get a real Resume); a book-series reading order (so finished books get
  Read next); incognito reading; "clear history older than X"; clickable Trends heatmap days jumping into History.

## 2026-09-29 - Library bulk actions

Design [2026-09-29-library-bulk-actions-design.md](superpowers/specs/2026-09-29-library-bulk-actions-design.md), plan alongside it. Not
committed. Every CE behavior was checked in `_reference/ComicRackCE` (`ComicBrowserControl.cs`, `ComicDataPasteDialog.cs`, `MainForm.cs`
RatingEditor, `ComicBook.ResetProperties`/`RefreshInfoFromFile`, `ItemView` selection).
- **One action catalog** (`ViewModels/LibraryActions/LibraryActionCatalog.cs`) now feeds the right-click menus, the selection bar and the
  keyboard. Labels count the set actually acted on, which fixes the old "Mark as" label on an unselected tile inside a selection.
- **New actions:** My Rating ▸ None–5 on a selection (Alt+Shift+0–5, one undo step); Mark as ▸ Read up to here; Show in List ▸; Copy Data
  (Ctrl+C) / Paste Data… (Ctrl+V, CE's field-picker dialog, ticks remembered in the new `AppSettings.PasteDataFields`, migration
  `AddPasteDataFields`) / Clear Data…; Refresh ▸ thumbnails / Re-read info from file… (Activity Center jobs, re-read undoable); Show in
  Explorer on many (Ctrl+G); Copy file paths (Ctrl+Shift+C); Merge series… (new dialog choosing the target; `SeriesMergeHelper`); Invert
  Selection; Esc clears the selection. Series ▸ setters now apply to every selected series; the series menu gained Mark as, Add to Reading
  List, Bulk Edit, Plugins, Refresh, Copy paths.
- **Selection bar** is icon-only (`Views/SelectionActionBar`), built from the catalog; its Delete ▾ now has the keep-file variant (closes the
  2026-09-25 "Not done" for the action bar - Book screens still have none).
- **CE parity change:** a search/filter change drops selected items it hides (CE's `ItemView` does the same); switching issue/series view
  clears the other kind's selection.
- **Found on the way:** Avalonia's `KeyGesture.Parse("Alt+Shift+3")` binds Alt+Shift+**Tab** (a bare digit is read as the enum value);
  the catalog maps digits itself, and `ContextMenuHost` no longer shows a wrong hint for digit gestures.
- **Verified:** new `LibraryBulkActionsTests` (25) and `SelectionActionBarViewTests` (3, headless render: one named 36 px button per action,
  Alt+Shift+3 rates, Ctrl+C in the search box doesn't copy data); full App suite 4,904/4,905 - the one failure was
  `RemoteLibraryUiIntegrationTests` expecting the old remote series menu, updated and green; Data migration tests 150/150.
- **Not verified:** nothing seen on screen in the running app yet (bar look at real widths, the two dialogs, flyout placement, light skins);
  keyboard shortcuts only exercised headlessly. Dead code left for a follow-up: the old bar's Plugins/Add-to-List popup toggles and
  `…Selection` commands the new bar no longer uses.

## 2026-09-30 - File Explorer cover thumbnails

Design [2026-09-30-explorer-cover-thumbnails-design.md](superpowers/specs/2026-09-30-explorer-cover-thumbnails-design.md), plan alongside
it. Not committed. Not a CE feature (CE has no thumbnail handler) - modelled on CDisplayEx's Thumbnails checklist.
- **Handler:** new `src/Paperbunkr.ShellThumbnails`, a Native AOT COM DLL (`IInitializeWithStream` + `IThumbnailProvider`, raw-vtable
  WIC, PDFium via `LibraryImport`, SharpCompress, linked engine sources for the natural sort and MOBI reader). Windows runs it in its
  thumbnail surrogate. Covers .cbz/.cbr/.cb7/.cbt (same page 0 as the library), .epub (declared cover, else first image in reading
  order), .mobi/.azw/.azw3 (EXTH cover), .pdf (page 1), .djvu (page 1 via `Resources\ddjvu.exe`). 3 s budget; no image, DRM or damage =
  Windows' normal icon.
- **App:** `ThumbnailHandlerService` (+ `IThumbnailRegistry`) registers per-user, also under Paperbunkr's own ProgID, saves and restores
  the previous handler, names another owner. Preferences › Advanced: FILE ASSOCIATION → **FILE TYPES** with "Open with" / "Thumbnail"
  columns (layout B). CLI `--register-thumbnails` / `--unregister-thumbnails`; installer task `explorerthumbnails` (unticked) + uninstall
  step; `BuildInstaller.ps1` AOT-publishes the DLL (fatal in CI, warning locally).
- **Found on the way:** Paperbunkr ships no `ddjvu.exe`/DjVuLibre, so its own DjVu reader likely can't decode pages (flagged as a separate
  task). DjVu thumbnails are only offered once that tool is present. The WebP SkiaSharp fallback was dropped: SkiaSharp isn't marked
  AOT-compatible and libwebp isn't shipped, so WebP covers rely on Windows' WebP codec. The existing Open-with toggle's list refresh is now
  deferred a tick (same routed-event detach trap as CLAUDE.md describes).
- **Verified (2026-09-30, real Windows):** MSVC tools + Windows SDK added to VS 2022 Community on the dev laptop; Native AOT publish
  links cleanly with no IL/AOT warnings (9.3 MB with `OptimizationPreference=Size`, exports `DllGetClassObject`/`DllCanUnloadNow`, OS
  imports only). Registered via `--register-thumbnails`, then thumbnails requested through the shell (`IShellItemImageFactory`,
  thumbnail-only) for real library files: CBZ, CBR (taking over from CDisplayEx), EPUB and PDF all came back from our handler
  inside dllhost.exe (opt-in trace confirmed), right covers. `AssocQueryString` showed ours as the effective handler for every type,
  so the feared ProgID shadowing did not happen here. `--unregister-thumbnails` restored CDisplayEx for .cbz/.cbr and removed
  everything else. Owner label reads "Thumbnail: CDisplayEx". 298 tests green (handler, service, Preferences, associations).
- **Bugs the real test found, fixed:** `IInitializeWithStream` carried `IInitializeWithFile`'s IID (managed tests and the direct
  harness shared the mistake - now pinned by `ComInterfaces_UseWindowsOwnIids`); inside the surrogate `AppContext.BaseDirectory` is
  System32, so pdfium.dll/ddjvu.exe are now located from the module itself (`ModuleLocation`); the opt-in trace lives in
  `%TEMP%\Paperbunkr\thumbnails.trace` because the surrogate couldn't resolve AppData. `BuildInstaller.ps1` puts the VS Installer
  folder (vswhere.exe) on PATH - ILCompiler's link step needs it.
- **Not verified:** the Preferences toggle in the running app (tested through the CLI + service), .cb7/.cbt/.mobi/.azw3 through the
  shell (unit-tested only), DjVu (no ddjvu.exe shipped), a full installer build/uninstall.

## 2026-10-03 - Input service (one app-wide keyboard / mouse / gamepad layer)

Design [2026-10-03-input-service-design.md](superpowers/specs/2026-10-03-input-service-design.md) (§13 lists where the build differs
from the plan). Not committed. CE precedent checked in `_reference/ComicRackCE` (`CommandKey` is one gesture space for keys, wheel and
mouse buttons 4/5; Ctrl+F, F5, Shift+F6, Ctrl+wheel zoom, MouseButton4/5 = previous/next list).
- **Service:** `Services/Input/` - `IInputService` takes keys, wheel, pointer buttons and gamepad state, resolves them against a keymap
  to string-id `InputAction`s, and delivers them to handlers by scope (Global / screens / modal Overlay) and context (paged,
  zoomed, continuous). One root Tunnel hook in `MainWindow` (`InputHost.Attach`); no per-page `KeyBindings` left in XAML. Text boxes
  swallow actions unless the action says `FiresInTextInput`.
- **Replaced** (deleted): `KeyboardCommandRegistry`, `KeyBindingService`, `KeyBindingIO`, `KeyOption`, `KeyCommandBinding`,
  `KeyBindingRowViewModel`, `GamepadMapper`, `PageCanvas.KeyBindings`. The DB `KeyBinding` table is read once by
  `LegacyKeyBindingImporter` and deliberately kept (worktrees share the dev DB).
- **Config:** overrides-only `keymap.json` next to the DB (temp+replace writes, bad file kept as `.bad`, schema migration chain).
  Preferences › Keyboard Shortcuts is rebuilt data-driven (chips, capture box for keys / middle + side buttons / wheel, per-row reset,
  conflict banner, layout import/export - old exports still import).
- **Migrated:** MainWindow shell shortcuts, reader (`PageCanvas`, `ReaderScreen`: keys, Ctrl+wheel zoom at cursor, thumb buttons, pad),
  Library actions, Book reader (incl. JS key forwarding via `WebKeyMap`), Compare. Focused-item widget keys (arrow nav among tiles,
  Enter/Delete/F2 on a row, type-ahead, EventMap) deliberately stay with their controls - spec §9.
- **Verified:** new/rewritten tests for the model, binding parser, keymap, service routing/scopes/suppression, gamepad processor, legacy
  import, editor view model, host wiring and capture box; whole solution builds with 0 errors; full App suite 5,193/5,194 - the one
  failure (`SelectionActionBarViewTests.AltShift3_OnACard_RatesTheSelection`, its window had no `InputHost`/real service) was fixed and
  re-run green together with `LibraryBulkActionsTests` (29/29). Other test projects were not re-run (Data only got a doc-comment edit).
- **Not verified:** nothing seen or driven in the running app - real keyboard / thumb buttons / wheel-zoom anchor in the reader, the
  Book reader's key forwarding in a real WebView, a real XInput controller, the Preferences editor look, remap persisting across a
  restart, FlaUI `KeyboardShortcutDiagnosticTests` (compile-only).
- **Follow-up, same day ("all screens", spec §14):** every screen now registers a scope (Home, Books, Reading Lists, Smart Lists, the detail screens, Continuity, Insights, Wanted, Preferences,
  the editors); the controller is app-wide (`AppGamepadHost`: D-pad/A/B/Y/bumpers/right stick work everywhere, reader keeps its own); shared actions `Refresh` F5, `NewItem` Ctrl+N, `Save` Ctrl+S,
  tabs on Ctrl+PageUp/PageDown and the bumpers (`TabStrip.Step`). Item keys (arrows, Enter, Space, F2, Delete, Home/End/Page) are remappable through canonical-key pass-through / forward / swallow
  rules with no per-control edits. Controller buttons can be captured in Preferences. Plugin commands are actions (Plugin API 4.3, manifest `shortcut`, CE's Ctrl+Shift+F1-F12 defaults).
  Fixed on the way: Library Ctrl/Shift-click selection (a Button handles the press first) and the shared focus ring being clipped to a speck on plain Buttons (Insights tabs, Home carousel; plus a
  double ring/gap on the carousel). Tests: `UiNavigationTests`, `ScreenInputTests`, `InputFollowUpTests`, `FocusRingPaintTests` (pixels).
- **Later the same day (spec §14.2, §14.3):** Library Enter/Space on tiles, Esc/Backspace to leave detail pages, arrows in the editors and on the nav rail, Up/Down in the Detail grid and Wanted,
  Reading Lists gallery Up to Continue Reading, Books edge navigation, SuggestBox no longer opening itself on focus/arrow, Continuity sidebar Right/Enter, the focus-ring clipping side effect
  (`FocusRingAdorner`), doubled/hardcoded rings (Detail, carousel, Insights), the Library empty-state Clear button, and real covers in the Merge series dialog.
- **Third round (spec §14.4, 2026-10-04):** text boxes only browse when reached by arrows/pad (`TextEntryMode`); Wanted Queue/Series Up/Down (Avalonia's `ItemsControl` dropped focus, found by walking the real
  `MainWindow`); Event Map arrows (focus lost when a scroll recycled the focused card; edge moves reach the shell) and its doubled ring; hover and focus rings on Books cards; Preferences sidebar focus ring (an inset
  shadow cannot draw on a bordered control); Left from the first Home tile or panel reaches the rail. Not reproduced: arrow navigation between groups in a grouped Library (walked Down, Up, Right in poster, list and
  details modes, by issue and by series, small and large groups, all fine in the real window).
- **Not verified on screen** (as above, plus): the controller driving non-reader screens with a real pad, capturing a pad button in the Preferences box, a real plugin's shortcut running, and how the
  focus ring now looks on every other page that uses the shared adorner (rendered headlessly only for Insights and the Home carousel).
- **Limits / follow-ups:** the metadata-entity detail page has no code-behind to register a scope on; the PDF reader relies on its page canvas's Reader scope; only Library-hook plugin commands become
  actions (the other hooks do not operate on a selection); Wanted, Continuity, Manga/Book detail and the editors are smoke-tested (register, decline when empty), not driven with real data.

## 2026-10-04 - Preferences > Keyboard Shortcuts: master-detail redesign

Design [2026-10-04-keyboard-shortcuts-master-detail-design.md](superpowers/specs/2026-10-04-keyboard-shortcuts-master-detail-design.md), plan
[2026-10-04-keyboard-shortcuts-master-detail-plan.md](superpowers/specs/2026-10-04-keyboard-shortcuts-master-detail-plan.md). Not committed. Presentation only: the bindings model,
`keymap.json`, capture box and conflict detection are unchanged. CE (`KeyboardShortcutEditor`) is a grouped list with an editor panel but has no search, filters or conflict detection; those are deliberate additions.
- **Page:** search + device filter (All / Keyboard & mouse / Controller) + Customised only over a grouped one-line-per-action list, beside an editor for the selected action (chips, capture, reset, defaults, "where it works").
  The red row glow and red banner are gone: a conflict is a quiet warning icon on the row and chip plus a note in the editor naming the other action, whether it can shadow this one, and a jump link.
- **Code:** `ShortcutsEditorViewModel` (filters, `FilteredGroups`, selection, `SelectGroupByTag`), `ShortcutChip` is now an observable class, `InputScopeDisplay`, `KeyboardShortcutsSection` rewritten. Preferences search hits on a
  `shortcuts.<group>` anchor select that group's first action and clear the filters; Ctrl+F on the page goes to its own search box.
- **Behaviour change to know about:** the controller's analogue axes (sticks, triggers) now show a lock and no remove button. They could be removed before but never re-added (no capture for axes), so this stops an unrecoverable unbind.
- **Verified:** 16 new view-model tests + 1 Preferences anchor test; a real-`MainWindow` test (Down walks the list and the editor follows, Right enters the editor, Left returns to the same row); headless renders of the dark skin at 1400 and 900 wide.
  Found by that test: the screen-wide `TryMoveDirectionally` hops to the Preferences sidebar from this list, so the list steps through its own rows.
- **User-seen:** on screen in the dark skin on 2026-10-04 ("way better"). **Not verified:** light and other skins, a controller driving the page with a real pad, Narrator, and capturing a pad button into the new editor pane.
- **Test note:** the full fast set (Debug) had 6 failures outside this work (4 `PdfBookSourceTests` "resolver is already set", `BookFolderScanServiceTests` PDF import, `ReaderImagePipelineTests` detail-page budget); all 41 tests in those classes pass when run alone, so it looks like test ordering, not checked against a clean tree.

## 2026-10-04 - Library redesign (lenses, two-row command bar, one-badge covers, inspector, continue strip)

Design [2026-10-04-library-redesign-design.md](superpowers/specs/2026-10-04-library-redesign-design.md) (23 grilled decisions; mockup https://claude.ai/artifact/JmsuQFjQYsZqq5HeMZ24Fp),
slice 1 plan [2026-10-04-library-redesign-slice1-plan.md](superpowers/specs/2026-10-04-library-redesign-slice1-plan.md). All four slices built in one session. Not committed. **Not seen on screen by anyone yet.**
- **Slice 1, command bar and lenses:** `LibraryLens` (All / Reading / Unread / Read) + `AppSettings.LibraryLens` (migration `AddLibraryLens`, no-op `Down`). `LibraryViewPipeline` classifies and tallies both granularities
  in its one pass (`LensCounts`), then applies the lens; counts follow scope, chips and search. The toolbar is two rows: row 1 gains Series | Issues and Covers / List / Details switches, row 2 is lens tabs, filter chips
  (Content type, Publisher, Has unread, Missing, Tracked, Library source, Clear) and a result summary. The Filter dropdown is gone. Lens tabs carry the `tab` class, so `TabStrip.Step` drives them (Library now claims `TabNext`/`TabPrevious`).
  `FilterUnreadOnly` is kept as the **Has unread** chip, so nothing saved changes meaning. Workspaces save the lens; built-in "Currently reading" is now the Reading lens and `EnsureBuiltInsSeeded` rewrites a changed Library starter.
- **Slice 2, covers:** `TileCosmeticsOverlay` draws a bottom progress bar for in-progress items instead of the hover ring (the `ProgressRing` setting now switches the bar; Preferences label updated). Series covers: stack peek
  (sibling Borders in the existing gutter, tile size unchanged, tested), all-read check in the unread badge's corner (`SeriesCardSample.IsAllRead`), hover Read + More buttons (not focusable; More opens the tile's context menu),
  the last hardcoded `#B814161B` replaced. `PinnedGroupHeader` floats the current group's heading in grouped cover grids (no push-off). New installs default the publisher badge on (CLR initializer only, no model change).
- **Slice 3, inspector:** Continue resumes the most recently opened in-progress issue, else the first unread (`ContinueReadingNumber`/`Page`, label "Continue #N · p. P"). Synopsis prefers `Series.Summary`. The cover rail in the
  panel is replaced by number chips (`PreviewIssueChip`, capped at 150) with one shared cover popup on hover or focus; a missing file is a dashed chip. Esc in the panel returns focus to the grid.
- **Slice 4, continue strip and sidebar:** `LibraryContinueStrip` heads each cover grid's scroller (up to 6 series with a mid-read issue, Home's rule, read off the cached cards; hidden unless All lens + no search + no chips + All Series).
  The sidebar's Content type group is removed in favour of the chip.
- **Deviations from the spec, on purpose:** (1) ~~the row-1 button is still "View & Sort"~~ resolved the same day, see the follow-up below. (2) The Content type chip drives the same
  selection the sidebar rows did, so picking a type still leaves a collection; combining the two is not done. (3) The Publisher chip is session-only. (4) The strip is in the cover grids only, not List or Details.
  (5) Hover buttons stay behind the existing toggle, off by default.
- **Not done:** the spec's responsive rules (inspector overlay below ~1100px, row-1 compaction below ~900px); the stack and hover buttons on the Tiles cover style; the all-read check on per-issue tiles; a Publisher/lens entry in browse history.
- **Verified:** new `LibraryLensTests`, `LibraryInspectorTests`, `LibraryContinueStripTests`, pipeline/workspace/overlay/view/keyboard tests (lens stepping with Ctrl+PageDown through the input service, Up/Down between strip and grid,
  Esc from the inspector, focus rings not clipped on the chips and strip cards, stack does not change tile size, pinned header). Data migration tests pass. UI-automation tests were edited for the removed Filter button and sidebar group but **not run**.

### 2026-10-04 follow-up - "View & Sort" split up after the user saw the build

The user ran the build and found the popup redundant against the new row-1 switches; audit and three decisions agreed in chat (all recommended). Not committed.
- **Gone from the popup:** the List and Details rows (row-1 switch) and the whole Card content group (Series | Issues switch).
- **Row-1 button:** icon-only **Display options** (`LibraryDisplayButton`), no tabs: Cover style (Poster / Panorama / Tiles), Cover size (was "Grid density"), Show titles, then "On the cover":
  Unread count, Publisher logo, Progress bar (new here; the same `AppSettings.ProgressRing` as Preferences), Rating, Language (Off / Text / Flag over the two stored settings), Hover buttons, Dog-ear preview.
  Disabled in the Details table, where nothing in it applies.
- **Sort and Group:** each its own popup on its row-2 chip ("Sort: …", "Group: …"). The group chip is always shown ("Group: None"). `ViewSortTab` and the tab state are deleted.
- **Moved to Preferences > Appearance:** Fade in covers and Cover tooltips (Cover cosmetics), Smooth scrolling (Interface). `LibraryScreenViewModel` no longer reads or writes these three, so a Library settings save
  cannot overwrite what Preferences wrote (tested). No schema change.
- **Hover "More" button fix (user report: "glitched out"):** its menu was anchored to the button, which exists only while the cover is hovered, so the menu was torn down when the pointer moved onto it.
  It now opens at the pointer, owned by the screen (`ContextMenuHost.ShowMenuAtPointer`), a tick after the click. Diagnosed from the code, not reproduced; no test clicks the button with a real pointer.
- **Known unrelated failure:** `PreferencesScreenViewModelTests.Screen_FocusRings_AreNotClipped` throws `Static resource 'PbRadius' not found` at `KeyboardShortcutsSection.axaml:48` (the concurrent Keyboard Shortcuts work), so the
  three new Appearance rows have not had their focus rings checked.
- **Verified:** new `LibraryDisplayPopupTests`, a Preferences view-model test for the three moved switches, the existing Library suites. UI-automation driver and two tests repointed (`LibraryViewSwitch_*`, the chips) but **not run**.
  Not seen on screen yet.

## 2026-10-04 - Insights gaps: goal outcomes, chart colour, publisher logos, goal scopes

Two design + plan pairs: `docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-{design,plan}.md` and
`2026-10-04-insights-goal-scopes-design.md`. All of it **built, uncommitted, not yet seen in the real app** (the user's running Paperbunkr locks `bin/Debug`, so tests ran with `-c Release`).

- **Goal outcomes.** `GoalOutcome` Active / Completed / Missed, derived in `GoalResolver` (no migration) - Missed for every period kind, not only Custom. Completed goals name the day they were
  completed. `GoalRing` draws a check / cross / amber-behind. Today hero prefers active goals; ended ones sit in a collapsed "Past goals" group with Renew + Delete; a Missed goal raises one
  `goal-missed:{id}` Activity Center alert.
- **Chart colour.** `InsightsChartPalette` (meaning colours for reading state and content rating, neutral grey for Unknown/Other, positional fallback), `DonutLegend` (swatch + count + %), hover on donut and legend
  (linked), score distribution on a red-to-green ramp with value labels and digit ticks (the `★` tofu was ScottPlot's font lacking the glyph), Top publishers rows show the publisher logo.
  Found on the way: the original donut sweep arithmetic subtracted the padding from the largest slice twice, so the ring never closed (round caps hid it) - fixed in `DonutMath`, flat caps with a hairline gap now.
- **Goal scopes.** Count goals and **Finish** goals; scopes: reading list, collection, story event, continuity, creator, media type (plus series/publisher/genre); several scopes per goal (AND); "count each once";
  no-deadline finish goals; a goal whose list was deleted reads "<name> was deleted" and never "Completed". New table `ReadingGoalScopes` + two columns (migration `AddGoalScopes`, legacy single scope backfilled into a row, `Down` drops the table only).
  Editor rebuilt (goal type, scope rows, SuggestBox pickers); "Set a goal…" on a reading list's Manage menu and on a collection's menu; a "Goal: 12 of 30" chip on the list page. **Reading status** was dropped as a scope
  (a finish goal over "Planned" items shrinks as you read them); smart lists are out of scope.
- **Verified:** Data.Tests `GoalResolverTests` + `GoalScopeResolverTests` + the two goal migration tests (37/37); App.Tests scoped filter `Goal|InsightsChart|DonutMath|InsightsScreen|ReadingList|Collection|Wanted|ScheduledTask`
  (393/393, Release); controls and the editor rendered headlessly to PNG (the editor's `SuggestBox` pickers do not paint in that harness, so they are the unseen part). The full fast suite did **not** complete
  in this session; four failures seen in its partial log were in reader / remote-reading tests (`ReaderScreenPanelTests`, `ReaderScreenViewModelTests`, `RemoteReadingTests`) and are not from this work.
- **Not verified:** the real Insights screen (Past goals, Renew, hover tooltips), the goal editor with its pickers, the reading-list chip and both "Set a goal…" menu items.

## 2026-10-04 - Publisher logos: user icon folder, publisher gaps, fill from siblings

Design: `docs/superpowers/specs/2026-10-04-publisher-icons-user-folder-and-gaps-design.md`. **Built, uncommitted, not seen in the real app.**
Diagnosis first: the bundled pack (751 files, identical to CE bar one rename) is fine - all 807 name keys resolve to an image - and the real library only has 16 distinct publishers, 12 of which already had a logo.
The real gaps were 441 issues with no publisher (mostly manhwa/manga scans) and no runtime place to add icons.
- `%APPDATA%\Paperbunkr\publisher-icons` (`PublisherIconFolder`): PNG/JPG, recursive, CE filename rules + `map.ini`, **consulted before every bundled mark**; loaded at startup and on *Reload icons*.
- Library Health > Review > **Publisher logos** (`PublisherGapsViewModel`, `PublisherCoverage`): publishers with no logo, issues with no publisher, *Fill N from the rest of the series* (confirmed, DB only, ties skipped, never overwrites), Open folder / Reload icons.
- Letter chips (not logos) for Penguin Random House, Abrams, Games Workshop via `publisher-aliases.tsv`.
- **Verified:** `PublisherCoverageTests` + `MarkResolverUserIconTests` + resolver/BrandMark filter (137/137, Release); Preferences + Library Health filter incl. the updated section counts (296/296). A read-only dry run on the real library:
  75 issues fillable (Green Arrow, Titans, The Flash, Gokurakugai -> Viz Media, Reality Quest -> Webtoon, ...); 366 issues in 17 series (the manhwa scans) have nothing local to infer from. **Not verified on screen.**

## 2026-10-04 - List layouts (CE list layouts: column editor, named layouts, per-list layouts)

Design: `docs/superpowers/specs/2026-10-04-list-layouts-design.md` (section 14 = as built); plan: `2026-10-04-list-layouts-plan.md`. **Built, uncommitted. The user confirmed the feature works in the Debug build on 2026-10-05; the itemised on-screen checks below were not each reported on.**
CE facts were looked up in `_reference/ComicRackCE` first: the saved object is `ListConfiguration` (`Name` + `DisplayListConfig`), CE ships no preset layouts, a layout holds no fonts or colours, and each list keeps its own auto-saved copy.
- **Data:** migration `AddListLayouts` (two new tables, nothing else): `ListLayouts` (named, per screen) and `ListLayoutAssignments` (one row per screen + list; key `*` is the screen default). `ListLayoutService` stores JSON only.
- **Per-list memory:** `ListLayoutsViewModel` (one per screen) compares the captured layout with the last one applied and writes the list's row only when it differs, from the existing settings-save hook. Library keys: `all`, `content:<type>`, `collection:<id>`; Books: `all`.
- **Library layout (`ListLayoutState`):** Details columns (order, visibility, **width**), view mode, cover style, up to three thumbnail caption lines, tile text elements, sort and group.
- **Details header:** drag a header to move the column, the grip after it to resize (`LibraryScreen.DetailsHeader.cs`); the right-click column picker still works.
- **Workspace menu** (Library and Books) gains *List layouts*: one row per saved layout, List options, Save list layout, Edit layouts, Default layout for this list.
- **Overlays:** List Options (Details / Thumbnails / Tiles; Books: sort and group) and Edit Layouts (use here, all lists, up, down, rename, delete).
- **Shortcuts** (input service): List options Ctrl+L, Edit layouts Ctrl+Alt+L (CE's keys), Save list layout unbound.
- **Starter layouts (2026-10-05, user request; CE ships none):** Cover wall, Compact tiles, ComicRack classic, Reading progress, File details - seeded once at startup, ordinary layouts, a deleted one never returns (design section 15).
- **Left out on purpose:** CE's series-statistics columns and the columns Paperbunkr has no data for, per-column date format, Ctrl+Shift+F6-F11, a Details table on Books, caption lines on Panorama cards.
- **Verified:** `ListLayoutServiceTests`, `LibraryListLayoutTests`, `ListLayoutViewTests` (43/43, Release, including the starter layouts). The fast set (`Speed!=Slow`) ran 5,130 tests with 6 failures, none in this area:
  four `PdfBookSourceTests` and one `BookFolderScanServiceTests` PDF test (all pass when run on their own) and one `LiveFolderWatchServiceTests` file-lock failure (that class fails a different test on each run).
  Those six were not investigated further.
- **Not verified:** anything on the real screen - the two header drags, both overlays in either skin, the menu section inside the Workspace popup, Ctrl+L / Ctrl+Alt+L in the running app, and a restart keeping a per-list layout.
  The migration has only run through `EnsureCreated` in tests, not against the real database.

## 2026-10-05 - MetronInfo.xml write-back and id import

Design: `docs/superpowers/specs/2026-10-05-metroninfo-write-back-design.md`; plan (with the deviations found while building): `2026-10-05-metroninfo-write-back-plan.md`. **Built, uncommitted, not seen on screen.**
Came out of a research pass over the Metron-Project repos. CE was checked first: it reads MetronInfo and deletes it on export (`Changes.txt`), so writing it is a deliberate deviation.
- **Setting:** `AppSettings.WriteMetronInfo` (migration `AddWriteMetronInfo`, no-op `Down`), default off, under the master write-back toggle. Preferences > Advanced > Comic File Metadata, "Also write a MetronInfo.xml".
- **Write:** `IssueToMetronInfoMapper` + `MetronIdContext` (Data), called from `MetadataFileWriteBackService`. `.cbz` and image folders, next to ComicInfo.xml. One `Credit` per creator from the eight credit fields.
  `<IDS>` holds the Metron / Comic Vine ids from `ComicMetadataExternalId` plus `Issue.GcdIssueId`; the scrape source is primary, and `id=` attributes are only ever that provider's.
- **Existing files:** setting off = an existing MetronInfo.xml is untouched. On = overwritten, keeping what we hold nothing for (prices, universes, reprints, store date, another tool's ids).
- **Schema:** targets MetronInfo v1.1. `AlternativeNumber` and `CommunityRating` live in `MetronInfo.Paperbunkr.cs`, so CE's generated `MetronInfo.cs` only changed in its GTIN types (`object` -> `string`).
- **Read:** `EmbeddedMetronIds` links a new file's Metron / Comic Vine / GCD ids on scan, only where the issue or series has no link yet. Hooked into the new-file scan only, not Sync Metadata or a single-issue rescan.
- **Left out:** `LastModified` (a fresh stamp would defeat the no-op guard; Metron-Tagger may therefore treat our files as stale), cover prices, universes, finer credit roles, non-CBZ archives.
- **Verified:** `IssueToMetronInfoMapperTests` (new, 25) + snapshot tests; all of `Paperbunkr.Data.Tests` (2,079/2,079); `MetadataFileWriteBackServiceTests`, `LibraryFolderScannerTests`, `MetadataWriteBackQueueTests` and the Preferences toggle test (74/74).
  A full and a minimal written document both validate against the official v1.1 XSD (Python `xmlschema`, XSD 1.1); the full one fails v1.0 only on the two v1.1 elements, as designed.
- **Not verified:** the Preferences row on screen, a write against a real library file, the migration against the real database, and whether Metron-Tagger / Codex accept the output. `avalonia-pro-max/review-checklist` applied by reading only.

## 2026-10-05 - Metron: change sweep, exact id lookups, cover-hash matching

Design: `docs/superpowers/specs/2026-10-05-metron-api-efficiency-and-matching-design.md`. **Built, uncommitted. Nothing here has run against the real Metron API.** CE has no Metron support, so there was no CE behaviour to match; the ComicVine paths are untouched.
- **Change sweep:** `MetronClient.GetSeriesModifiedSinceAsync` (`series/?modified_gt=`, 5-page cap, null = "can't tell") behind `ISeriesChangeSource`. `AcquisitionCycle.FindUnchangedAsync` runs one sweep when two or more due series qualify and only bumps `LastRefreshedAt` for the unchanged ones.
  New `WatchedSeries.LastCatalogFetchAt` (migration `AddWatchedSeriesCatalogFetch`) forces a real fetch every 7 days. A failed or capped sweep fetches everything as before. `If-Modified-Since` was deliberately not built.
- **Exact ids:** `IComicIssueLookup` (`issue/?cv_id=`, `issue/?upc=`). `ScrapeOrchestrator.TryResolveByIdAsync` runs before the name search on a Metron scrape that chooses for itself: the book's Metron id, then its ComicVine id, then its UPC, accepted only on exactly one hit.
  A run where the user confirms matches still shows its dialogs (that is how a wrong link is corrected).
- **Cover hash:** `MetronCoverHash` is a port of Python `imagehash.phash`, which is what Metron's `cover_hash` is. It reproduces the hash Metron's API docs publish for a public cover (`c585eb18bf1e5423`) exactly, on the full image and on a thumbnail.
  In the cover gate a Metron issue's published hash replaces the image download (match at 10 bits or fewer, Metron-Tagger's bar); when the top candidate fails, the second and third are tried and one is taken only if it alone matches.
- **Verified:** `MetronCoverHashTests`, `MetronExactMatchTests`, `MetronClientTests`, `ScrapeOrchestratorTests`, `CatalogChangeSweepTests`, all of `Paperbunkr.Daemon.Tests` (203/203), all of `Paperbunkr.Data.Tests` (2,131/2,131).
- **Not verified:** requests actually saved, match quality on a real library, and whether a 304 or a sweep counts against Metron's limit the way the docs imply.

## 2026-10-05 - Metron account sync (pull list, reading, collection, wish list, reading lists)

Design: `docs/superpowers/specs/2026-10-05-metron-account-sync-design.md` (its last section lists where the build departs from it). **Built, uncommitted, not seen on screen, and never run against a real Metron account** - every request and reply shape is from Metron's `api/README.md` alone.
- **Off by default.** Preferences > Connections > "Metron account": a master switch, a switch per area, and Sync now / Send my reading history / Import from Metron. Nothing is sent while the master is off.
- **Shape:** `MetronAccountSync` (Data) reconciles the database against a ledger (`MetronSyncLinks`, migration `AddMetronAccountSync`, plus six `AppSettings` columns) instead of hooking the dozen places follows, wants, reads and ratings change. A run spends at most 120 requests at background priority and stops cleanly on a rate limit; what wasn't sent is still a gap next run.
- **Areas:** pull list (additions both ways, unfollow here removes there, a removal on Metron is neither undone nor mirrored); reading (each `Finished` read above a watermark is scrobbled with its date and rating); wish list (Metron wants: add / remove / acquire); collection (issues with a file are added as DIGITAL, rating changes are PATCHed, nothing is ever deleted).
- **Import from Metron:** fills ratings and read state that are empty here, never overwrites; the reads it records are marked in the ledger so they are not scrobbled back.
- **Reading lists:** a "Metron reading lists" source (`MetronReadingListSource`) in the new-reading-list flow, for the user's own and public lists. Works without the master switch (it only reads, on request).
- **Hosting:** scheduled task "Sync with Metron" (hourly; a no-op with a plain message while sync is off or no login is saved).
- **Known limits:** "Mark as read" writes no `Finished` event, so only reads finished in the reader are sent. Wants that came from a ComicVine series are not mirrored. A book with neither a Metron nor a ComicVine id never syncs. Login is still username + password (Metron now recommends API tokens).
- **Verified:** `MetronAccountClientTests`, `MetronAccountSyncTests`, `MetronSyncSettingsViewModelTests`, `PreferenceIndexTests`, the model-drift check (`HasPendingModelChanges`) with all three new migrations.
- **Not verified:** anything against metron.cloud; the Preferences section on screen (`avalonia-pro-max/review-checklist` by reading only); the migrations against the real database. **First real use should be with only the Pull list switch on, watched.**

## 2026-10-05 (evening) - verification pass over today's Metron work

Checked against a copy of the real database (`%AppData%\Paperbunkr\paperbunkr.db`, taken while the app was running) and the network. Nothing on screen was checked: that still needs the user, or their go-ahead for UI automation.
- **Confirmed: the three new migrations applied to the real database.** `AddWriteMetronInfo`, `AddWatchedSeriesCatalogFetch` and `AddMetronAccountSync` are in `__EFMigrationsHistory`; the seven `AppSettings` columns, `WatchedSeries.LastCatalogFetchAt` and the `MetronSyncLinks` table exist; `PRAGMA integrity_check` is ok (4,261 issues, 571 series).
- **Confirmed: the account-sync switches and buttons work in the running app.** The database shows the master switch and all four areas on, the reading watermark at the newest event (333), the hourly task enabled and run once, and two hand-started syncs.
- **Both hand-started syncs failed with "Metron did not respond in time" - Metron was down.** `metron.cloud:443` refused connections from this machine and from an outside network at 18:20, while `static.metron.cloud`, GitHub and ComicVine answered. The run stopped at its first request and sent nothing (`MetronSyncLinks` is empty), which is the designed behaviour. **Still not verified: any account request against a working Metron.**
- **Found and fixed: `MetronSyncSettingsViewModelTests` wrote to the real activity history.** It built `new ActivityService()`, whose default recorder is the real `ActivityHistoryStore`. Fifteen rows titled "Syncing with Metron" / "Sending reading history to Metron" / "Importing from Metron" (ids 1574-1578 and 1582-1591, all 2026-10-05) are from test runs, not the user. The test now passes a no-op recorder; a rerun added no rows. The rows themselves were left in place.
  The same leak is older than this: five app classes fall back to `new ActivityService()` when a test passes none (`MetadataWriteBackQueueTests` left three "Writing metadata to files" rows today). Not fixed here.
- **Stale notes:** `git status` shows only today's Metron work uncommitted, so every earlier "uncommitted" note in this file and in `Paperbunkr-Roadmap.md` predates the 0.7.7 release commit (`a2c826f`) and is out of date.

## 2026-10-05 (night) - first real account sync: "Metron rejected your login", and a 24-hour block

The user's first real Sync now runs (all four areas on) ended "Metron rejected your login". The login was fine: Import from Metron, ten seconds apart from a failed sync, read the collection with it.
- **Cause: five write paths had no trailing slash.** Metron's `api/README.md` writes `pull_list/series/add`, both `remove`s, `wish_list/items/add` and `acquire` without one; its router only serves them with it (its own client, mokkari: `METRON_URL = "https://metron.cloud/api/{}/"`).
  Without the slash Metron redirects, .NET drops the `Authorization` header when it follows a redirect, and the request that arrives is a 401. Fixed in `MetronAccountClient`; a test now asserts every account request path ends in `/`.
- **Redirects are no longer followed** on the shared Metron client (`AllowAutoRedirect = false`); a redirect is reported as a Paperbunkr fault with its path, never as a bad login.
- **Metron blocks an address for 24 hours after three 401s in five minutes** (its published `fail2ban/jail.d/metron.conf`; ten 403s also). New `MetronLoginGuard`: a rejected login is not sent again for 10 minutes, and no third rejection can happen inside a 6-minute window, across every Metron request in the process (`MetronClient` and the older `MetronSource`).
  A 401 and a 403 now get different messages, each quoting Metron's own `detail` text.
- ~~This machine's address is very likely blocked until about 19:00 UTC on 2026-10-06~~ **Wrong - see the next section: Metron answered again about 90 minutes later.** What was written at the time: five test requests sent without a login each got a 401 within seconds, and Metron stopped answering this machine right after. The app's own failed syncs were spaced too far apart to trip the rule.
- **Verified:** all Metron test classes in `Paperbunkr.Data.Tests` (127), including the new `MetronLoginGuardTests` and the slash test.
- **Not verified:** the fix against the real Metron (blocked), so no account write has succeeded yet. The Debug app was not rebuilt because it was running; it needs closing first.

## 2026-10-05 (late) - account sync works against the real Metron; two runs collided

- **Confirmed against the real Metron:** the scheduled run at 20:36 UTC (on the build with the trailing-slash fix) sent the whole pull list and part of the wish list. The ledger holds 59 pull-list rows, one per followed Metron series with the right series id, and 60 wish-list rows, each for a real Metron want; no duplicates.
  That is exactly its 120-request budget (1 list + 59 adds + 60 adds); the other 42 open wants and the collection wait for later runs. So `pull_list/series/`, `pull_list/series/add/` and `wish_list/items/add/` are verified. **Still not verified live:** scrobble, collection add, rating PATCH, acquire, the two removes.
- **Correction: there was no 24-hour block.** Metron stopped answering this machine right after five no-login test requests, but answered again within about 90 minutes. Either its deployed ban time is shorter than the published 24 hours, or it was the same instability seen earlier that evening. The guard added because of it stays: the published rule is real.
- **Bug found by the user: "An error occurred while saving the entity changes."** Sync now was pressed 47 seconds into the hourly run; both reconciled the same ledger and the second hit the unique index on (`Kind`, `LocalId`). Fixed: `MetronAccountSync` runs one at a time, process-wide (a second run or import returns "another sync with Metron is already running"), and the settings page now shows the innermost exception message.
- **Verified:** the Metron test classes in `Paperbunkr.Data.Tests` (128), including a test that holds one run open and starts another.
- **Later the same night (user's Sync now, activity row 1623):** "48 issues added to the wish list, 53 issues added to the collection", stopped at the budget. The ledger then held 59 pull-list, 108 wish-list (every open Metron want) and 53 collection rows, no duplicates, so `collection/add/` is verified live too. The Debug app was rebuilt at 22:56 with the one-at-a-time fix.

## 2026-10-06 - content-type auto-classify pipeline (tracker-driven), all three stages (uncommitted)

Closes the "Content-type classification & manga metadata scraping" gap. Design `docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md` (grilled with the user; every answer "as recommended", except the queue layout: table + cards, built as table rows that expand into a card), plan beside it.
- **Data:** `Series` records where its type came from and whether a person locked it (`ContentTypeSource/Confidence/Locked`, previous type and reading mode for Undo, a queued suggestion and its evidence, last-checked date); `AppSettings.AskBeforeClassifying`. Migration `AddContentTypeProvenance` locks every series that already has a type; `Down` is a no-op. `SeriesContentTypeEditor` is the only writer of `Series.ContentType`: Library menu, bulk edit, both Detail pickers and the queue all lock. The old weekly publisher sweep skips locked rows (it could overwrite a deliberate "Unknown").
- **Classifier:** MangaBaka, then AniList (now fetches `countryOfOrigin`), then MangaDex (now fetches `originalLanguage`). Applies a type only for a near-exact title match that two sources, or one source plus a local guess, agree on; otherwise queues it. Series with Western evidence (GCD match, Western publisher, ComicVine/Metron scrape, `Manga=No`) are not searched. No tracker link and no file write-back from the pipeline.
- **Entry points:** scheduled task "Classify series from trackers" (off by default, daily, 150 searches a run, resumable), Preferences "Classify Library" / "Re-check publisher guesses" / "Ask before changing a content type", Library "Classify from trackers".
- **Queue (Library Health > Review > Content type):** rows with Accept / Keep / Not a comic, a card with evidence chips and Change-to buttons, Accept all high confidence, "Recently auto-classified" (30 days) with Undo, and an Undo in both Detail headers (no time limit).
- **Verified:** `Paperbunkr.Data.Tests` all 2,183 pass. `Paperbunkr.App.Tests` fast set (`Speed!=Slow`): 2,590 pass, 2 failed because of this work (a duplicate scheduler priority and the series context-menu shape test); both fixed and the affected classes re-run (51 pass), but the whole fast set was **not** re-run after the fix. New tests cover the mapper, decision rule, editor, service, migration backfill, sweep lock, runner, queue rows and actions, the row templates rendered headlessly, and the Detail Undo.
- **Bug the tests caught:** the queue query used correlated subqueries SQLite cannot translate; the app's refresh would have swallowed the error and shown a stale queue.
- **Not verified:** how any of it looks on screen, any call to the real MangaBaka / AniList / MangaDex (fakes only), a real scheduled run. The MangaUpdates / Kitsu / MAL / Shikimori adapters were deliberately not extended.

## 2026-10-07 - Performance and memory, first pass (uncommitted)

Spec: `docs/superpowers/specs/2026-10-07-performance-and-memory-design.md`; plan: `...-plan.md`. Trigger: the app at 2.3-2.6 GB
on a 2-core / 7.8 GB machine with ~5,000 comics.

**Built, covered by tests:**
- Memory readout: `Services/Performance/PerformanceSnapshot` (managed heap, private bytes, working set, native estimate, cache
  sizes, threads, heavy jobs). Appended to the crash-report header. No on-screen readout yet.
- Cover memory: budget is 5% of RAM clamped 150-500 MB (`ImageMemoryBudget`), split grid 60 / comics 30 / books 10.
  `CoverImageCache` and `BookCoverImageCache` are byte-bounded (`BitmapByteCache`) instead of 5,000 entries each.
  `GridCoverCache` disposes evicted bitmaps unless an `Image` holds a lease (`AsyncCoverImage` takes and releases it).
  `NativeBitmapPressure` reports the legacy caches' native bytes to the GC. `MemoryPressureTrimmer` halves the caches after two
  readings at 85% system memory (no timer, no blocking collection).
- `HeavyJobLane`: one heavy job at a time, user-started first. Wired into the ten Preferences jobs, the scheduler, scrape,
  organize and Metron sync. Off in tests (pass-through until `App` enables it).
- Verify, metadata sync and series resync read 250 issues at a time by id cursor, one `DbContext` per page.
- GC: `System.GC.ConserveMemory=1`. ONNX panel model: 2 intra-op threads, unloaded when the Reader is left.
- Bug fixed on the way: the ten Preferences jobs rethrew `OperationCanceledException`, so cancelling one from the Activity
  Center showed a crash report (the 0.7.7 crash log). They now end as Cancelled.

**Not done, needs the running app:** before/after numbers for the three scenarios in spec 4.1 (nothing here has been measured);
an on-screen readout; moving the legacy card screens onto the grid pipeline; the `TieredPGO` and ONNX-arena experiments; the
UI-thread trace and any timer consolidation. Nothing in this entry has been seen on screen.

**Behaviour changes to know about:** a cancelled Verify/sync keeps the pages it already saved (it used to save nothing);
scheduled tasks now run one at a time, not two.

### 2026-10-07 (evening) - first measurement of the new build: the real consumer was the dog-ear peek cache

The new build still sat at 1.85 GB private after a normal session, so a heap dump was taken from the running app (dotnet-dump)
plus a scan of its memory regions. What it showed:
- Managed heap 222 MB of 1,848 MB private: the growth is native.
- 674 decoded bitmaps, 802 MB. 646 of them are covers and total 168 MB (inside the new budget). The other 28 are whole comic
  pages (12 at 1988x3056, three 3975x3056 spreads, one 3816x5868) totalling 634 MB, every one held by `DogEarThumbnailCache`.
  The dog-ear hover peek decoded page 2 at full size for a 34 px image and kept up to 200 of them.
- Fixed: `PageDecodeCore.DecodeSinglePageToWidth`, dog-ear decodes at 96 px (`DogEarThumbnailCacheTests`). Not yet re-measured.
- Still unexplained: 21 native blocks of exactly 16,188 KB (331 MB), not Avalonia bitmaps. GPU shared memory was 233 MB.
- "Checking for comics" (acquisition daemon, mirrored by `AcquisitionActivityBridge`) is not in `HeavyJobLane`, so it can run
  beside a scheduled task.

### 2026-10-07 (night) - second and third measurements

- After the dog-ear fix: 1.15-1.3 GB. Dump: covers 185 MB, no full pages; 770 dead 128 KB byte arrays (100 MB) on the large
  object heap from the ported engine opening archives with a 128 KB `FileStream` buffer. Buffers cut to 64 KB
  (`ZipSharpZipEngine`, `TarSharpZipEngine`, `SharpCompressAccessorSession`, `XmlUtility`). Not re-measured.
- With the reader open in guided view: 2.1 GB private. Found and changed (none re-measured in the app yet):
  - ONNX Runtime CPU arena: measured in a probe, +300 MB with the arena against +20-60 MB without, same time per page
    (250-340 ms). Now off (`PanelDetectionService.UseCpuArena`). The 12.5 MB input tensor is reused instead of allocated per page.
  - Reader page budget: Auto was 25% of RAM clamped 128-512 MiB (always 512 on this machine); now RAM / 24 (about 330 MiB here,
    512 from 12 GB up). The Preferences override is unchanged.
  - Skia GPU cache: fixed 384 MB, now 2.5% of RAM clamped 96-384 MB (about 200 MB here). The 16,188 KB native blocks
    numbered 21, 21 and 24 across three dumps (331-379 MB), which tracks the 384 MB limit; that they are this cache is an
    inference, to be confirmed by the next dump.
- Still open: dog-ear peek reported not showing (decode verified good on real files; display not seen); two copies of the
  library's Issue entities and rows in memory; about 100 MB of strings.

### 2026-10-08 - fourth measurement (reader open, after the arena / budget / GPU-cache changes)

1,592 MB private with the reader open (was 2,143 MB in the same situation). Where it is:
- Managed heap 524 MB committed: about 360 MB live, 150 MB free space inside it. 73 MB of that is the reader's encoded-page
  tier (by design, 64 MB budget). The 128 KB archive buffers are gone (2 left, was 770).
- Native C heap about 315 MB, in twenty 16 MB segments. CORRECTION: the "unexplained 16,188 KB blocks" are Windows heap
  segments (header signature ffeeffee, 0xFCF pages each), not the GPU cache. The decoded cover bitmaps (200 MB) live inside
  them, so earlier tables counted the covers twice. Lowering the GPU cache was still reasonable but was not what those were.
- About 520 MB in individually allocated native blocks of 8-31 MB while the reader is open. Avalonia page bitmaps account for
  only 82 MB of it; the rest is not yet identified (the reader pipeline's own Skia copies of pages are the first suspect).
- Covers: 887 bitmaps, 283 MB including 82 MB of reader pages; cover part 200 MB.
- 9,608 Issue entities against 5,460 rows and one LibraryProjection: a second set of entities exists; 7 DbContexts were alive.
  Whether one of them tracks the whole library was not checked.
- `System.GC.ConserveMemory` raised 1 -> 5 for the internal free space. Not built or measured.

### 2026-10-08 - fifth measurement: reader open, guided view, after a full comic

1,341 MB private (the same situation measured 2,143 MB before the arena / reader budget / GPU cache / GC changes). The panel
model was loaded. Breakdown: managed heap 303 MB committed (14 MB free inside, was 150); native C heap 284 MB; reader pages
158 MB in 7 bitmaps; 4 further full-page pixel copies held by `ReaderPageVisualHandler._skImageCache` (made while an image
adjustment is on); GPU shared memory 259 MB (page textures; system RAM on this integrated GPU). The large native blocks
that were unidentified last time are these page pixels and their copies - read from the dump, not inferred.
One copy of the library this time (4,948 Issue entities, 5,434 rows): the doubling seen earlier was not present, which fits
an uncollected old load but does not prove it.
Not done: the fast test set has not been rerun since the dog-ear, buffer, ONNX, budget and GC changes (only the touched classes).

### 2026-10-08 - reader: shared page images, smaller auto budget (built, tests pass, not on screen)

- `SkiaBitmapConverter.ShareSkImage`: the reader's leased-canvas draw path (image adjustment on, crossfade transitions) now
  wraps the page bitmap's own pixels instead of copying them (the copy made three full-page buffers per call and the handler
  kept one image per visible page). Reads two non-public Avalonia 12.1 members (`Bitmap.PlatformImpl`,
  `Avalonia.Skia.ImmutableBitmap._image`), holds its own counted reference so the image outlives a disposed bitmap, and falls
  back to the old copy if either member is missing. `SharedSkImageTests` fails loudly if an Avalonia upgrade moves them.
  The pipeline's own processing calls (`ReaderImagePipeline` lines ~396/428) still copy: they read raw bytes in a fixed order.
- Reader Auto budget: RAM / 32 clamped 128-512 MiB (about 250 MiB on the 7.8 GB machine; 512 from 16 GB). User had no override set.
- To check on screen: a comic with auto levels / an adjustment on, page turns with the crossfade style, and a PNG page with
  transparency (the shared image keeps the page's alpha; the copy forced it opaque).

### 2026-10-09 - regression from the reader budget cut: long strips struggled in the continuous modes

User report: continuous modes struggle with webtoon/manhua/manhwa long strips. Cause found by arithmetic, not yet confirmed
on screen: the strip band cache was a quarter of the display budget, and the two Auto-budget cuts (512 -> ~330 -> 256 MiB)
took it from 120 MiB to 56 MiB. A band is 4,096 rows (13 MiB at 800 px wide, 26 MiB at 1,600 px) and the reader keeps the
visible bands, a margin band each side and one to three prefetched, so 56 MiB could not hold even the visible and margin
bands; they were evicted and re-decoded while on screen. Fix: `ReaderMemoryBudget.BandBytes` = a quarter of the display
budget with a 128 MiB floor (capped at the display budget). Reader tests pass (139 + 17). Not seen on screen.
Still possible and unchecked: PNG strips, which cannot be band-decoded and go whole through the display cache (now 224 MiB,
was 480), and strips wider than about 1,400 px, where even 128 MiB holds fewer than seven bands (also true before the cuts).

### 2026-10-10 - long strips, continued: band cache sized from the strip; progressive JPEG measured

- `ReaderMemoryBudget.BandBytesFor(width, bandHeight)`: the band cache grows to seven bands of the strip actually being read
  (floor 128 MiB, ceiling 512 MiB), set when the pipeline learns a page is a strip (`EnsureBandCapacity`). 92 MiB needed at
  800 px, 184 MiB at 1,600 px. 149 reader tests pass. Not seen on screen.
- The series the bug was found on (Reincarnation Of The Veteran Soldier, 221 chapters, 720 px wide, pages up to 14,635 rows,
  chapters 290-440 MB decoded): chapters are either all baseline or all progressive JPEG (10 of 41 scanned were progressive).
  Measured on this machine, one 720x12,041 page: a baseline strip session opens in 0-10 ms and adds 1 MB; a progressive one
  takes 70-113 ms and holds 25 MB while open (the decoder must read the whole image first). Going back to an earlier band
  needs a new session: 25 ms baseline, 90-100 ms progressive. So with the band cache thrashing, progressive chapters paid
  about four times as much per re-decode, which is why this series showed it and a larger baseline one did not.
- Not done: nothing changed for progressive pages themselves; the 25 MB each open session holds is outside every budget.

### 2026-10-10 (later) - long strips: user still saw hiccups scrolling back, app at 2,052 MB in Long Strip mode

Dump taken in that state (Chapter 1 of the test series, progressive JPEG, 720 px):
- 49 native blocks of exactly one band (720 x 4096 x 4 = 11.5 MB), 551 MB, for 16 cached bands and 28 writeable bitmaps.
  Bands were cached as `WriteableBitmap` (`SkiaBitmapConverter.FromSkBitmap`); Avalonia draws a writeable bitmap through a
  snapshot copy, so each band that reached the screen existed twice and the copy was made on the render thread at first
  draw. Fixed: bands are now immutable bitmaps (`ToImmutableBitmap`), one copy, no managed band-sized array on the way.
- 8 open strip sessions at about 34 MB each (progressive). Scrolling back needed a new session each time (90-100 ms).
  Fixed: `StripDecodeSession` holds a progressive JPEG decoded whole and serves any band in any order (`CanSeek`,
  `IsProgressiveJpeg`, `TryCreateWhole`); baseline files keep the forward-only scanline walk. `StripDecodeSessionTests` (new).
- 163 reader tests pass. Not seen on screen and not re-measured.
- Still true: the pipeline evicts bands more than two margins away regardless of cache room, so scrolling back through a
  baseline strip still re-decodes (about 25 ms a band plus skipping to it); open sessions' memory is outside the budget.

### 2026-10-10 (evening) - long strips: the decode-evict loop (root cause), 2,926 MB measured

After the immutable-band and whole-progressive changes the user re-tested and the app measured 2,926 MB private in Long
Strip mode: 150 band-sized native buffers (1.7 GB), 302 band-sized bitmap objects, 41 items in a band cache sized for 11,
and the pipeline deferred-dispose queue holding bands (4 of 14 sampled were rooted there; the rest were unrooted).
- Root cause, in `ReaderImagePipeline.SetStripBandWindow` since the band design landed: prefetch reached up to 4 bands
  beyond the screen (margin 1 + adaptive 1-3) but eviction dropped anything beyond 2. With the adaptive fringe above its
  minimum, every call evicted the bands the previous call had prefetched and queued them again. Each evicted band waits 4 s
  in the deferred-dispose queue (no count cap, by design), so live bands = churn rate x 4 s. Making progressive bands cheap
  to read (earlier today) raised the churn rate, which is why memory went up, not down.
- Fix: `StripBandPlan` (new, pure): decode +-1, prefetch +-2..3, keep one band beyond the prefetch; at most 10 resident
  bands. `ReaderMemoryBudget.ResidentBands` now comes from the plan (10, was 7), so the cache holds what the plan keeps
  (125 MiB at 800 px, 250 MiB at 1,600 px, floor 128).
- `ReaderImagePipelineBandTests...KeepsResidentBandBytesBounded` updated: it asserted the old 5-band bound, which held only
  because of the churn; it now uses an 11-band strip and the plan keep range (9 bands for one visible band).
- 167 reader tests pass. Not seen on screen, not re-measured. The fast test set has not been rerun since 2026-10-07.

### 2026-10-10 (16:30) - long strips NOT fixed: band-sized native buffers with no managed owner

Measured after `StripBandPlan`: 3,089 then 3,164 MB private in Long Strip mode on the progressive test chapter. 178 native
blocks of exactly one band. Of 810 band-sized bitmap objects in the heap only 14 still had a live Skia handle (9 held by the
view's band slots, 3 by the cache, 2 by shared images), so the buffers are not owned by any live managed bitmap. Ruled out
by inspection: offscreen render targets (all 256x256), GPU memory (53 MB shared). Ruled out by a headless probe (120
rounds each, no growth): whole-session `ReadBand`, `ToImmutableBitmap`, `CreateScaledBitmap`, `ShareSkImage` in either
dispose order. The old `FromSkBitmap` path grew 25 MB in the same probe.
Next step in place: `ReaderNativeStats`, static counters on both sides of every reader create/free pair, built into the
app; read them from a dump after one more strip session. Until then the cause is unknown.

### 2026-10-10 (17:15) - long strips: cause found with the counter log

`logs/reader-native-stats.log` (new: `ReaderNativeStats.WriteIfDue`, one line per 10 s while a book is open; the static
counters could not be read from a dump on .NET 10). One strip session, about 100 s:
- 3,074 bands read and cached for a chapter of about 30 bands: roughly 40 a second, continuously.
- Every create/free pair balanced (shared images 50/48, handler images 50/48, reader bitmaps 3,098/2,964). Nothing leaks.
- The deferred-dispose queue tracked the churn exactly (3,065 queued, 2,962 drained): live memory = 40 bands/s x 4 s grace
  x 11.5 MB = 1.8 GB, private bytes 2.6-3.9 GB. The "buffers with no managed owner" of the earlier dumps were this queue;
  that analysis was wrong.
- Cause, in `PageCanvas.BuildStripBandEntry`: the band range defaulted to the whole strip and was only narrowed when the page
  was on screen. Every OFF-SCREEN strip in the layout therefore asked for all of its bands, every frame. Several strips x 3-4
  bands is more than the band cache holds, so it evicted and re-decoded them without end. Present since the band design.
- Fix: `StripBandPlan.BandsInView` (pure, tested): on screen = the bands the viewport overlaps; within one viewport length =
  only the edge band it will enter by (`SetStripBandWindow(..., edgeOnly: true)`, no margin or prefetch); further = nothing.
  Prefetch for the visible strip cut to one band; `MaxResidentBands` 12 (8 visible strip + 2 each for a strip above and
  below), and the band cache is sized to that (135 MiB at 720 px, 300 MiB at 1,600 px).
- 176 reader tests pass. NOT yet seen on screen or re-measured. The counter log stays in until this is confirmed, then
  it and `ReaderNativeStats` should come out.

### 2026-10-10 (17:20) - long strips: fix confirmed by the counter log

Same chapter, Long Strip mode, about 2.5 minutes: 52 bands read (was 3,074 in 100 s), private bytes 1.09-1.49 GB (was
2.6-3.9 GB), settling at 1.12 GB; reader bitmaps live 200-450 MB. All pairs balanced. Smoothness on screen still to be
confirmed by the user. Still to do: remove `ReaderNativeStats` and its log once confirmed; rerun the fast test set (not run
since 2026-10-07); the pipeline decodes each strip page whole once before it knows it is a strip (about 34 MB each, seen as
200 MB live with only 3 bands read), which is a possible later saving.

### 2026-10-10 (17:45) - long strips: no whole-page decode for a JPEG strip

`ReaderImagePipeline.LearnsItIsABandableStrip`, called at the top of `ProcessQueuedPage`: reads the header from the cached
bytes, and if the page is a JPEG strip records the size and strip verdict (raising `PageSizeAvailable`) and skips the whole
decode. Before, a page not yet KNOWN to be a strip was decoded whole first (its size peek was queued behind it), about
34 MB each for every strip in the window, never drawn. PNG strips and JPEGs that fail to open a band session still decode
whole. Tests: `AJpegStrip_IsRecognisedFromItsHeader_AndNeverDecodedWhole`, `AnOrdinaryJpegPage_IsStillDecodedWhole`; the
band test helper no longer waits for a priming whole decode on a strip. 176 + 7 reader tests pass. Not on screen, not measured.

### 2026-10-10 (22:00) - long strips: confirmed by the user; diagnostics removed

- Last counter log: reader bitmaps 68 MB at the start of a strip chapter (was 204 MB) and 44-124 MB while scrolling (was
  200-450 MB); 31 bands in 2 minutes; every pair balanced. The user confirmed scrolling is smooth in both directions.
- `ReaderNativeStats`, its 20 counter calls, the log writer and the three log files are removed.
- Whole-app private bytes were 1.30-1.42 GB in that session, not lower than the one before, because the app started the
  reading at 1,339 MB (1,094 MB the time before). That part is outside the reader and was not investigated.
- The fast test set (`Speed!=Slow`) was started after the removal; its result belongs in the next entry.

### 2026-10-10 (23:00) - fast test set after the strip work; UI-thread measurement started

- Fast set (`Speed!=Slow`): 2,688 passed, 0 failed, 39 m 15 s. The run printed one stack trace from
  `StripDecodeSession.ReadBand` on the pipeline consumer thread without failing a test; rerunning `StripDecodeSessionTests`
  and `ReaderImagePipelineBandTests` alone did not print it. Not explained yet.
- Spec 4.6 (UI thread): `Services/Performance/UiThreadMonitor` is a temporary measurement. It writes
  `logs/ui-thread.log`: one `nav` line per screen change (`sync` = time the navigation code held the UI thread, `settled` =
  time until the UI thread was next idle), `step` lines for the stages of `Insights.Refresh()`, and `stall` lines when the
  UI thread took 100 ms or more to answer a ping. Started in `App.axaml.cs`; navigations are wrapped in
  `MainViewModel.LeaveAndNavigate` and `RunDrill`. Built, not yet read. Remove it when this work is done.
- Reading the code: every screen except Home loads its data synchronously on the UI thread before or just after the switch.

### 2026-10-10 (23:45) - UI thread: first measurement of screen switches; Insights load moved off the UI thread

Measured on the benchmark machine (`logs/ui-thread.log`, ~5,000 comics). `sync` is the navigation code holding the UI
thread; `settled` is until the UI thread was next idle.

| Switch to | sync | settled |
|---|---|---|
| Insights (first visit) | 4,143 ms | 4,248 ms |
| Insights (later visits) | 1,829-2,115 ms | 2,090-2,185 ms |
| Continuity | 2,581 ms | 3,083 ms |
| Smart Lists | 2,408 ms | 2,621 ms |
| Library | 1,505-2,038 ms | 1,668-2,470 ms |
| Preferences (first / later) | 1,807 / 683 ms | 1,852 / 831 ms |
| Reading Lists | 964 ms | 1,229 ms |
| Wanted | 728 ms | 829 ms |
| Home | 432 ms | 743 ms |
| Books | 296 ms | 521 ms |

- The data load is the freeze: building and drawing the new screen adds only 100-500 ms on top of `sync`.
- Insights steps: snapshot 1,395 ms and drop-off 288 ms (first visit only), recommendations 1,292-1,622 ms on every visit.
- Fix built, not yet measured: `InsightsScreenViewModel.RefreshAsync` runs every query on a thread-pool thread
  (`BuildToday`) and only repopulates the collections on the UI thread (`ApplyToday`), the same split Home uses.
  `GoInsights` and the history restore call it. `Refresh()` stays synchronous for the reading-event handler and the tests.
  The 67 Insights tests pass; there is no test of `RefreshAsync` itself.
- Per-call `step` timing added to the other screens' navigation for the next measurement. Those screens are unchanged.
- Also seen: a 6.8 s stall on Home right after startup and stalls of 0.3-2 s while sitting on a screen. Not investigated.

### 2026-10-11 (00:30) - UI thread: Insights measured; rail screens skip an unneeded reload

- Insights after `RefreshAsync`: 4,248 ms frozen on the first visit -> 463 ms; later visits 416-841 ms. The user confirmed
  it is faster.
- Second log, per call: `Library.LoadFromDatabase` 1,266-1,444 ms every visit; `Smart.RefreshSidebar` 1,101 ms +
  `Smart.EnsureListLoaded` 821 ms; `Reading.RefreshSidebar` 92-570 ms; `Events.RefreshStoryEventCandidates` 249-351 ms;
  `Wanted.Refresh` 431 ms; `Preferences.EnsureLoaded` 751 ms of a 1,819 ms switch (the rest not located). Every switch
  also carried about 300 ms that no load call accounts for.
- Built, not yet measured:
  - `Paperbunkr.Data/DataChangeStamp`: a process-wide counter moved by every non-SELECT/PRAGMA command and every
    transaction commit, through interceptors added in `PaperbunkrDbContext.OnConfiguring`. `MainViewModel.NeedsLoad`
    lets Library, Smart, Reading, Continuity and Wanted skip their reload on a visit when the counter has not moved and
    the last load is under 10 minutes old. `PersistLastScreen` writes inside `DataChangeStamp.Suppress()`.
    Limits: a write from another process is not seen; the first visit after startup costs what it did.
  - `OnCurrentScreenChanged` calls `ThemeService.SetTrueBlackReaderSuspend` only when entering or leaving a reader. It
    reloaded the theme and rewrote every theme resource on every switch. Suspected to be most of the 300 ms; unconfirmed.
- Tests: `DataChangeStampTests` 14 passed; `MainViewModelTests`, `NavRailKeyboardTests`, `ThemeServiceTests`, Insights and
  navigation-history classes 224 passed. No test covers `NeedsLoad` itself. The full fast set has not been rerun.
- Next: move the first-visit loads of Library and Smart Lists off the UI thread; find Preferences' missing second; the
  6.4-6.8 s stall on Home after startup.

### 2026-10-11 (07:00) - UI thread: skip-reload measured; back trip and detail queries

Measured (benchmark machine, `logs/ui-thread.log`, runs 3-6):

| Move | Before | After |
|---|---|---|
| Any switch, fixed cost (theme reload) | ~300 ms | gone: Books 20-80 ms, Home 10-50 ms |
| Rail screen to rail screen, nothing written | 0.5-2.7 s | 40-330 ms |
| Detail back to Library, nothing written | 1.2-1.4 s | 7-40 ms |
| Insights | 4.2 s first / 2.1 s later | 0.3-0.5 s / 0.1-0.4 s |

- `GoToRootScreen` (the back trip) now uses `NeedsLoad`/`IsFresh` too; it reloaded the whole Library on every return.
- `DataChangeStamp` does not count: the `ActivityRuns` insert (job log), the tracker throttle update
  (`TrackingLinks.LastSyncedAt` alone; only `TrackerAutoSyncService` reads it), and a commit whose connection ran no
  counted write. `LibraryScreenViewModel.SaveLibrarySettings` runs inside `Suppress()`.
- Detail pages: `manga.query` 0.3-3.2 s and `Detail.LoadSeries` 1.2-1.5 s. Both queries included `Issues.MetadataProposals`
  and `Issues.Tags` in one joined query (proposals x tags per issue). `AsSplitQuery()` added to both. Built, not measured.
- Tests: `DataChangeStampTests` 21 passed; Detail/MangaDetail view-model tests 76 passed. No test of `NeedsLoad`.
  The fast set has not been rerun since 2026-10-10.
- Open: first visit to each rail screen (Library 1.3-1.5 s, Smart 1.8-2.5 s, Continuity 0.9-2.6 s, Preferences 1.4-1.9 s);
  Home 5.8-6.6 s unresponsive after startup; `manga.tabs` 120-400 ms; stalls of 0.5-1.1 s while idle on Smart Lists;
  a reload after any real write still costs the full amount.

### 2026-10-11 (09:00) - UI thread: detail pages measured

| Move | Before | After |
|---|---|---|
| Open a manga page | 0.5-3.8 s | 0.2-0.45 s |
| Open a comic page (load) | 1.2-2.0 s | 0.32 s average, 0.66 s worst of 12 |
| Open a comic page (until idle) | 1.3-2.1 s | 0.65 s average, 1.4 s worst of 12 |
| Detail back to Library, nothing written | 1.2-1.4 s | 0.02 s average over 24 |

- Manga: the joined query was the cost (`manga.query` 0.3-3.2 s -> 13-190 ms with `AsSplitQuery`).
- Comic: the query was 14-55 ms after the same change; the cost was `DetailTabsViewModel.RefreshMoreLikeThis`
  (0.93 s average, 1.23 s worst), the recommendation query plus up to 15 synchronous cover decodes. It now runs on a
  thread-pool thread (`BuildMoreLikeThis`) and fills the rail when done, dropped if the page changed meanwhile. It takes
  1.5 s average in the background on this machine.
- Issue tiles on the comic page (`DetailTabs.axaml`, both templates) use `AsyncCoverImage.SourceId` in place of a
  synchronous `CoverImageCache.Get` per issue in `BuildIssueCard`. This was not the main cost, as first claimed.
  `RefreshIssueTile` (change/reset one issue's cover) sets the key too; not verified on screen.
- `DataChangeStamp` also ignores the `SeriesActivityEvents` insert (read only by the detail page's Activity tab).
- Tests: detail, manga detail, detail tabs, recommendation classes 193 passed; `DataChangeStampTests` 22 passed. Nothing
  asserts the rail's contents. The full fast set has not been rerun since 2026-10-10.
- Open, in order of size: Home 6-8.5 s unresponsive after startup; first visits (Library 1.3-1.9 s, Smart 1.8-2.5 s,
  Continuity 0.9-2.6 s, Preferences 1.4-1.9 s); a reload after a real write; 1.2-1.8 s stalls on Library while a scrape
  runs; `UiThreadMonitor` and its `Step` calls still in the code.

### 2026-10-11 (10:30) - UI thread: startup freeze after the window appears

- Measured cause: `PluginHostService.Initialize` held the UI thread 6.3 s of a 6.8 s freeze (`startup.pluginInit`), compiling
  each C# plugin script (`CSharpCommand.PreCompile`) with the main window already shown.
- Fix: `PluginEngine.WarmScripts` compiles the same scripts on a thread-pool thread, started right after
  `MainWindow.Show()`; `CSharpCommand` keeps compiled scripts in a static cache keyed by path, last write time and hook, and
  `PreCompile` reads it. `App.axaml.cs` awaits the warm-up before `pluginHost.Initialize`. The warm-up applies discovery's
  gates (Native tier, `requiresApi`, invalid settings definition) before touching a plugin's code.
- One launch measured after: `startup.pluginInit` 132 ms; Home idle 438 ms after its navigation (was 6.1-8.5 s); stalls of
  0.3-0.8 s remain in the following seconds. The warm-up itself took 9.2 s in the background, so plugin commands arrive
  about 7.5 s after Home on this machine. Python scripts are not warmed.
- Tests: full `Paperbunkr.Plugins.Tests` run: 228 passed, 2 failed (`CommandStatsTests.Every_dropped_event_...`,
  `DomainHookDispatcherTests.A_hung_plugin_costs_one_lane_...`), in a run that reported 20 m 48 s with single tests at
  1-5 minutes. Rerun of those two classes plus the script-engine tests: 43 passed in 26 s. Both failures are timing tests
  and the run was stretched (machine asleep or starved); not shown to be unrelated to this change beyond the clean rerun.

### 2026-10-11 (11:20) - UI thread: Library and Smart Lists load in the background

| | Before | After |
|---|---|---|
| Click Library, has to load | 1.7-2.4 s frozen | 0.13-0.5 s, data 1.0-1.2 s later |
| Click Smart Lists, has to load | 1.8-2.7 s frozen | 0.04-0.1 s, data 1.0-1.2 s later |
| UI-thread work when Library's data lands | 0.65 s | 0.03-0.05 s (`library.apply` + `library.applyView`) |

- `LibraryScreenViewModel`: `LoadFromDatabase` = `ApplyLibrary(ReadLibrary(...))`; `LoadFromDatabaseAsync` runs `ReadLibrary` on
  the thread pool and, for that path, computes the view through `ViewScheduler.Schedule` (a worker in the app) instead of
  `RunNow`. Dropped when `_dataVersion` moved; `EnsureLoadedNow()` for a caller that needs the data at once (the "addIssue"
  quick action). `IsLoadingLibrary` keeps the empty state hidden during the first background load.
- `SmartScreenViewModel`: `ReadSmartList` / `ApplySmartList` / `EnsureListLoadedAsync`. `GoSmart` no longer calls
  `RefreshSidebar` before it: the load already refreshed the sidebar, so the screen was loading the library twice.
- `MainViewModel.LoadScreensInBackground` (false by default, set true in `App.axaml.cs`) selects the background paths, so a
  view model built in a test still has its data when the navigation command returns.
- Tests: 3 new (`LoadFromDatabaseAsync_...` x2, `EnsureListLoadedAsync_...`); Library/Main/NavRail/Insights/Plugin classes
  517 passed before the view-compute change, the 17 Library load and scheduler tests after it.
- Still on the UI thread: `GoToRootScreen("library")` after a write (1.2 s; left because of the scroll-into-view it does);
  every `LoadFromDatabase()` a Library action triggers while the screen is open (one measured: 1.7 s stall after one
  issue update); first visits to Reading Lists (0.8 s), Continuity (0.9-1.5 s) and Preferences (1.4-1.9 s).

### 2026-10-11 (12:30) - UI thread: Library reloads after a change, Continuity suggestions

- `LibraryScreenViewModel.ReloadAfterChange()` (background in the app, synchronous when `ReloadInBackground` is false, and
  no reload at all when `IsCurrent`): used by ten Library actions (mark read/unread, the reading-list and collection
  "add" commands, collection reorder, set content type, drag-drop import, series resync) and by five shell call sites
  (quick-rate, collection properties, bulk series editor, migration overlay, the issue editor closing over Library).
  Still synchronous on purpose: delete issues, delete series, create collection from the sidebar, apply workspace.
- `Library.IsCurrent` (its own stamp + 10-minute age) replaces `NeedsLoad("library")`; `GoToRootScreen("library")` loads
  in the background when the snapshot is stale. Scroll position after that background reload: not verified on screen.
- `SuggestionsChecksViewModel.RefreshStoryEventCandidatesAsync`: both resolvers off the UI thread.
- Measured: Continuity click 0.18-0.67 s (was 0.9-2.6 s), suggestions 0.5 s later; Library click 0.12-0.5 s; no
  synchronous Library reload appeared in the run, but no Library action was exercised in it either.
- Tests: 433 run, 432 passed. `MainViewModelTests.GoReaderForIssue_FromHome_BackReturnsToHome_NotDetail` failed, then
  2 of 3 reruns alone: Home's fire-and-forget load from `GoHome` races the test's own `Home.LoadFromDatabase()` on a
  thread-pool thread. Believed to predate today's work (Home loading was not touched); not checked against a clean tree.
- Not done: Preferences first visit (1.4-1.9 s), Reading Lists first visit (0.8 s), removal of `UiThreadMonitor`.

### 2026-10-11 (14:00) - UI thread pass closed out

- `UiThreadMonitor`, every `Step(...)` call, `MeasureNavigation`, `DataChangeStamp.Trace` and the `ui-thread*.log` files
  are removed. Any further measurement has to add timing again.
- `HomeScreenViewModel`: a background load is dropped when a later load has started (`_loadGeneration`). This was the
  cause of the flaky `MainViewModelTests.GoReaderForIssue_FromHome_BackReturnsToHome_NotDetail` (failed 2 of 3 alone
  before, passed 5 of 5 after).
- `App.axaml.cs`: `Preferences.EnsureLoaded()` 20 s and `Reading.RefreshSidebar()` 24 s after startup, at
  `ApplicationIdle`, skipped while a reader is open. This moves a one-off 0.6-0.9 s each off the first click; it does not
  remove it, and it was not measured (the timing code was already gone). Preferences' first screen build (~1 s) is unchanged.
- Fast set (`Speed!=Slow`) on the final code: 2,700 run, 2,699 passed in 1 h 6 m.
  `ReaderScreenPanelTests.DoubleClick_WithSmartZoomOff_IsTheOldTwoHundredPercent_EvenOnAPanel` failed once (42 s in that
  run) and passed 3 of 3 alone (9-15 s). Reader code was not touched today; treated as load-sensitive, cause not found.
  `DataChangeStampTests` 22 passed. The slow classes were not run.
- Accepted without an on-screen check, at the user's direction: Library actions reloading in the background, scroll
  position after a background reload on the back trip, editors closing over Library without a reload.
- Everything from this pass is uncommitted.

## 2026-10-10 - ComicRack smart-list import and Library Organizer profile import

From an audit of files a user collected for the ComicRack Data Manager (`docs/superpowers/specs/2026-10-04-ce-preset-audit-overview-design.md`; the plan files are
`2026-10-10-smart-list-cbl-import-plan.md` and `2026-10-10-lop-profile-import-plan.md`). Pieces A and B were built on this date; C (proposed-values field), E (same-X-different-Y, Complete Count,
first-page-is-an-ad), D (scanner/noise lists) and F (generic Format presets) were built the next day (next entry).

### A. Smart-list `.cbl` import (built, tested, not seen on screen)
- A `.cbl` with `<Matchers>` and no books is a ComicRack smart list. `CblFile.Detect` tells the kinds apart; `CblImport.ImportAny` routes it; `CeSmartListReader` + `CeMatcherMap` read the
  XML (not CE's `XmlSerializer`, which cannot read the plugin matcher type) into a native Smart List; `SmartListFileImporter` saves it (name clashes get " (2)"). The three existing `.cbl` entry
  points (drag and drop, New Reading List dialog, the Reading screen's Import) route by content, and the Smart screen has an **Import** button that refuses a reading-list file.
- New `SmartListField`s (appended, never inserted - `ExcludeRuleJson` stores them by number): `NumberValue` (the numeric view of the issue number; `Number` stays Text), `Published` (date from
  year/month/day, CE's `ComicBook.Published`), and the four "Series: ..." figures `SeriesLastNumber` / `SeriesMaxYear` / `SeriesMaxCount` / `SeriesMaxGapSize`, computed over the **whole library**
  per `(series, volume)` like CE's `ComicBookSeriesStatistics` (gap size is the largest *step* between numbers, not the count of missing issues).
- A condition that cannot be imported is **dropped, never approximated**, and one Activity Center alert names each, saying whether the list may now match more or fewer books. A list where every
  condition was dropped is refused. Only the plugin matchers (`SameXDifferentY`, `findProposedValues`) hit this in the supplied files; they map through `PluginMatcherRegistry` (both registered on 2026-10-11, see the next entry).
- Found on the way: CE's root `<ReadingList MatcherMode="Or">` (10 of the 154 files); `Limit` / `BaseListId` on a list (reported, none of the files use them); a CE "Published is <empty>" means "no
  published date" (read through the year); a missing custom value compares as the empty string in CE, so **`SmartListQueryBuilder.EvaluateCustomValue` changed**: `Is ""` now matches comics
  that never had the value, and `Is not X` matches them too. Existing lists that used either spelling now behave like CE.
- Verified: reader tests against 20 real files plus inline cases (operator tables, `Not` groups checked over every truth assignment, De Morgan wording of warnings); all 154 supplied files read
  (on 2026-10-10: 1023 conditions kept, 11 script matchers dropped, 1 list refused because its only condition was a script; after 2026-10-11 all 154 import, see the next entry); `CblImportTests`, `CeImportRoutingTests`,
  `DragImportServiceTests`, `SmartListSeriesFieldsTests`. The corpus test runs with `PAPERBUNKR_CE_SMARTLIST_CORPUS=<folder;folder>`.
- **Not verified:** nothing on screen - the Smart screen's new Import button, the alert's look, a drag of a real `.cbl` onto the Library. The four series fields in the rule builder (they are
  Number fields in the existing picker, labelled "Series: ...").
- **Follow-ups completed 2026-10-25:** all four CE series-date matchers now import and evaluate over the full `(series, volume)` group. Every Smart List date operator now compares calendar days, matching CE's `IgnoreTime=true` behavior (including existing date fields).
- **ComicDb migration completed 2026-10-25:** migration preview now reports importable/skipped smart lists, name suffix collisions, and matcher warnings. It recursively imports only `ComicSmartListItem`s, prefixes folder paths (`Marvel > New Releases`), and saves the lists with books/series in the existing migration commit. `<ComicLists>` is still parsed separately and stripped before CE book deserialization so unsupported plugin matchers cannot block the library. Static reading lists and ID lists remain excluded.
- **Number caveat remains:** a CE "Number in range 0-1" imports as the numeric `NumberValue` field ("Number (numeric)" in the picker), not the text `Number`, so such a list reads differently from one built by hand with "Number".

### B. Library Organizer `.lop` profile import (built, tested, not seen on screen)
- `LopProfileReader` reads a profile file (one `<Profile>` or several in `<Profiles>`) into `OrganizerProfile`s; `LopProfileImporter` saves them (clashing names get " (2)"; nothing is replaced);
  **Preferences > Organize** has an **Import...** button (one file, which may hold several profiles; a notes card stays up until dismissed) and dropped `.lop` files import too. Code is in
  `Paperbunkr.App/Scraper/Import/` because the stored rule is a `Paperbunkr.Plugins.Abstractions` type.
- **`ExcludeMode = Only` is inverted**: five of the ten supplied profiles organize *only* the books matching their rules, our profile can only skip books, so the rules are stored as their
  complement by De Morgan. Checked by an equivalence test: 250 random rule trees in both modes, a port of the plugin's `check_metadata_rules` against the stored JSON run through the real rules
  engine, over 216 books.
- A rule that cannot be matched exactly **rejects the whole profile** (the reason is shown): an unknown or date field (`Start Year`, `Added Date`, `Manga`, `File Format`, ...), a numeric `is`
  (the plugin compares a number with text and it never matches), a text `greater than`, or a Yes/No rule for anything but `Yes`. Dropping a rule would change which files move.
- An imported profile never runs by itself: `UseForScheduledRun` is false, the base folder is kept as written, and a `Move` profile gets a note to check it first. Settings the profile model lacks
  are reported when the file uses them (per-token Prefix/Postfix/Separator/TextBox, a non-standard illegal-character table, move-fileless / move-failed, space handling switches, copy-adds-to-library,
  folders to keep when empty). No per-profile illegal-character table or migration was added: every supplied profile uses the standard one, which `Sanitizer` already is.
- Verified: reader tests against the nine distinct supplied files and inline cases, importer tests against a real temp database, converter equivalence tests, the view-model's Import command,
  dropped `.lop` files. Existing `OrganizerUiTests` still pass.
- **Not verified:** nothing on screen (the Import button, the notes card, the Organize tab's layout with the new row). The folder and file templates the supplied profiles use were run through the
  existing template validator only; none was run against a real library.
- **Follow-ups completed 2026-10-25:** Preferences > Organize now accepts several `.lop` files in one picker operation and shows one combined summary with file-specific errors and rejected-profile reasons. Pre-2.0 `Settings`/`Setting` roots are converted using the plugin's legacy mode and metadata-token mappings; 1.6-and-earlier profiles carry the upstream partial-support warning, while behavior-changing unsupported rules remain rejected or called out.
- **Verification 2026-10-26 (uncommitted working-tree changes; no commit ref yet):** focused Smart List/migration tests passed (154); focused `.lop` reader/import UI tests passed (54). Main-checkout `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj -c Debug --no-restore` succeeded with 0 errors. The migration and profile views compiled; interactive on-screen layout/theme review was not performed because desktop-app enumeration timed out.

## 2026-10-11 - The rest of the ComicRack preset audit: proposed values, same-X-different-Y, Complete count, first-page ad, scanner credits, format guess

Pieces C, E, D and F of `docs/superpowers/specs/2026-10-04-ce-preset-audit-overview-design.md` (specs and plans `2026-10-11-proposed-values-*`, `-series-checks-and-bulk-fixes-*`, `-scan-credits-*`,
`-format-presets-*`). All built and tested; **nothing seen on screen**.

- **Two new smart-list fields** (appended to `SmartListField`): `HasProposedValue` (an *accepted*, filename-inferred `MetadataProposal` for a field whose own stored value is empty; the value box is a field list such as
  `number, year`, blank = all; pending proposals do not count) and `SameXDifferentY` (ComicRack's script: X in the value box, Y in the second, `|`-separated; names are smart-list fields by name or label, otherwise custom values;
  no Y = every book of a group of two or more, i.e. a duplicate finder). Both hide the operator picker; Not negates. `SameXDifferentY` is computed over the **whole library** per evaluation.
- **Both ComicRack scripts now import.** `PluginMatcherRegistry.Default` maps `findProposedValues` and `SameXDifferentY`. All 154 supplied lists import: 1034 conditions kept, none dropped, none refused. One list gets an
  "approximated" note: its `SameXDifferentY` sits inside a group after other rules, where ComicRack only looked at the books those rules kept (in the other 8 uses it is the first rule, so the whole library is the same thing).
  `CeWarningEffect.Approximated` is new ("list may differ").
- **Library right-click → Tidy up** (one submenu, so the menu did not grow by five entries): *Commit proposed values* (books), *Complete count*, *Guess format from name*, *Find scanner from file name* (books and series),
  *First page is an ad…* (books). All but the last are one undo step with the usual file write-back, touch only the books that would change, and say so when there is nothing to do.
  - *Commit proposed values* writes accepted proposals into the book's own empty Title / Format / Volume / Number / Count / Year. **Design bug caught by its test:** with proposals loaded in the editing context, `Effective*()` already
    reads the value, so the undo snapshot and the write-back check saw no change; the values are now read in a separate query and applied to books loaded without proposals. Complete count and Guess format follow the same shape.
  - *Complete count* (ComicRack's script): books of a **Completed** series get the series' last issue number as Count; per series and volume; numbers of 1000 and over are not the last number; the script's numbering-jump rule is ported
    (a step above 12 issues per month once a series passes 30); placeholders for missing issues do not count; a series with no numbered book is left alone.
  - *First page is an ad…* tags page 1 `Advertisement` and page 2 `Cover` after a confirm (page tags are not in the metadata undo history). The scripts also moved the page to the end of the file; archives are not rewritten here.
  - *Find scanner from file name*: `ScanCreditDetector` reads the last bracketed piece of the name that is not noise and does not repeat one of the book's own fields, else a known group name, and adds it to Scan Information.
    Built-in group names and noise words (`ScanCreditCatalog`; the word list and rules are our own, written from the behaviour of ComicRack's script, not copied from its unlicensed blacklist) plus the user's additions in
    `scan-credits.json` next to the database, edited in **Preferences → Organize & Scrape → Filters & names → Scanner credits** (saved with the page; in Preferences search). No migration.
  - *Guess format from name*: `FormatGuesser`'s five starter rules (Director's Cut, Trade Paper Back, One Shot, Black & White, Annual; ComicRack's own format names) against the series name, title and file name, only for a book with
    no format, stored or proposed.
- **Deliberately not carried over:** a Data Manager `.dat` importer; stripping "Annual" from a series name (a series is an entity here); moving the first page inside the archive; the scanner script's "Unknown" marker and "Scanner:" prefix;
  bundling the reading orders (four real ones are test fixtures for the reading-list import).
- **Verified:** `Paperbunkr.Data.Tests` in full (2264 passed); `Paperbunkr.App.Tests` fast set (`Speed!=Slow`: 5085 passed, 0 failed); the slow-tagged set was not run. New or touched App classes: (`CommitProposedValuesTests`, `SeriesBulkFixesTests`, `FindScannerActionTests`, `LibraryContextMenuBuilderTests`, `LibraryBulkActionsTests`,
  `CeImportRoutingTests`, `DragImportServiceTests`, `LopImport*`, `ExcludeRuleConverterTests`); the corpus run over the 154 real lists.
- **Not verified:** every on-screen part: the Tidy up submenu, the two new condition rows in the Smart Lists editor (value boxes and their hints), the Scanner credits boxes in Preferences, the first-page confirm. The scanner detector was
  tested on release-name shapes I wrote, not on a real library's file names; expect group names it does not know and noise words it lacks (both can be added in Preferences).
- **Follow-ups:** a pipeline model for a `SameXDifferentY` that is not the first rule; user-editable format rules; a Library Health entry for "has proposed values".

## 2026-10-12 - Continuity sources: Metron-first universes, event order from CBRO + Metron lists + GCD dates

`docs/superpowers/specs/2026-10-12-continuity-sources-design.md` (section 8 is the as-built list). Built, tested, **not seen on screen**, and the Metron reading-list and universe calls have **never run
against the real Metron** (written from Metron's source). Migration `AddEventOrderEvidence`.

- **Removed:** the daily Wikidata shared-universe task and Wikidata in the event connector (Wikidata-only relations are deleted on the next check); the shared-character suggestions; the weak date-order guesses and the typed "Looks like the sequel of..." suggestions.
- **Added:** CBRO story-order ranks (5 s crawl delay), Metron EVENT reading-list previous/next links as Prequel relations, a median-fit of ranks to dates, the half-of-issues continuity placement rule, universe designations from Metron.
- **Corrected in build:** characters and teams already had provider ids (`ComicMetadataExternalId`); the fix was cleaning a pruned row's ids, not new columns.
- **Verified:** Data tests 2602 of 2603 (the one failure is another session's `MilestoneResolver.cs` allowlist); continuity-related App tests 324 passed; Debug build succeeded. The full fast App run was cut off by a session end after two failures that look unrelated
  (`ReaderScreenPanelTests.DoubleClick_WithSmartZoomOff...`, `LiveFolderWatchServiceTests.RapidCreation...`) and was not re-run.
- **Follow-up completed 2026-10-25 (working tree; implementation not committed):** the Metron universe-catalogue picker was already delivered in the guided creation flow; this follow-up adds the terminal Unplaced region and CBRO overlap notes/connectors. See [the approved design](superpowers/specs/2026-10-25-continuity-unplaced-overlap-design.md) and migration `AddEventOrderOverlapNotes`.
  - Unplaced events have no usable date, source rank, or directional relation; they appear alphabetically after the ordered chronology and continue to obey map filters. Undated events with a directional relation remain in the chronology.
  - CBRO notes support “Takes place during,” “Takes place simultaneously with,” and “Occurs during the same time as.” Original notes remain visible in map details; only a unique same-list title/year match gets a deduplicated, non-directional “Occurs during” bracket. These connectors do not create or change Event relations or chronology.
  - **Verified:** `EventOrderTests` (22 passed), `ContinuityMapTests` + `ContinuitySourcesAppTests` (23 passed), `ContinuityMapViewModelTests` (6 passed); `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj -c Debug --no-restore` succeeded with 0 errors. Launched `src/Paperbunkr.App/bin/Debug/net10.0/Paperbunkr.App.exe`; the Paperbunkr window appeared, but CUA could not bind to it for visual inspection, so narrow-width and theme rendering remain unverified.

## 2026-10-12 - Connections and Appearance redesign (Preferences)

`docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md`. Built, tested, Debug build in `src/Paperbunkr.App/bin/Debug`. **User-confirmed on screen 2026-10-12** (Appearance Theme tab seen in the default dark skin; Connections confirmed too).

- **Connections:** a service gallery (Metadata / Trackers / Downloads tiles with a status dot), an attention strip, a "4 of 12 connected" summary, and an inline pane per service instead of the modal. Metron is one tile with Account / Sync / Activity tabs; the GCD download and the tracker toggles are tiles too. Test connection and a two-step Disconnect (`TwoStepConfirm`) sit in the pane header.
- **Appearance:** three sub-tabs (Theme, Covers & Detail, Home & Interface). Theme is a grouped gallery (Dark / Light / Accessibility) beside an inspector for the active theme (large preview, palette, computed text and accent contrast badges, true black, accent override, font); Auto switch is its own group. Live glow-tier and density samples.
- **New:** `ConnectionStatus` + `ConnectionStatusResolver`, `ConnectionStatusStore` (`connection-status.json` next to the database, no migration), `PreferencesScreenViewModel.Connections.cs` / `.Appearance.cs`, `ThemeSummary` inspector fields and `ThemeService.GroupOf`. `maximum_contrast/theme.json` gained `"category": "accessibility"`.
- **Scope cut, on purpose:** only Prowlarr and qBittorrent have a real Test connection. No tracker adapter, ComicVine or Metron has a read-only call that changes nothing, so their button says "saved, verified on first use" and records nothing. `Expired` exists in the model and UI but nothing produces it yet; a real tracker probe is the follow-up.
- **Verified:** 28 new view-model tests plus 13 store/resolver tests, `PreferenceIndexTests` and `ThemeServiceTests` green (108 run, Release); `Paperbunkr.App` Debug build 0 errors. The full App suite was not run.
- **Not verified:** light themes and Matrix on these two sections, narrow widths, keyboard walk of the tiles, search jumping into a pane and scrolling to it, Narrator, and a real Prowlarr / qBittorrent test (including the check that runs when the page first opens).

## 2026-10-13 - Theme system v2 (phases 1-7)

`docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md` (section 11 is the as-built record). Built and tested
headless, Debug build in `src/Paperbunkr.App/bin/Debug`. **Uncommitted. Nothing here has been seen on a real screen.**

- **Engine:** a theme is now a family with variants, resolved by a pure `ThemeResolver` (family base, variant, the user's all-themes /
  this-theme / this-variant values, Boost contrast, True Black) and applied as one transaction that writes only what changed. Version 1
  `theme.json` files are adapted in memory and never rewritten; all 10 built-in themes are byte-identical through the new path (golden
  snapshots). Package install is staged and bounded (512 files, 64 MB, 16 MB per file, 8192 px); nothing in a package is run or fetched.
- **What a v2 theme can add:** new status/selection/reader roles, metrics, typography roles, semantic icon replacements, materials
  (gradients, images), component skins with hover/pressed/disabled states for five roles, named motion, and a layout for Home.
- **Appearance > Theme:** variants of the active family, a Customize group (eleven values, three scopes, per-row and per-scope reset,
  Boost contrast, Effect quality, Theme layouts, Reset layout), Export this theme, Export / Import my customizations, install notes.
  Overrides live in `theme-overrides.json` beside the database; no migration.
- **Home** is the layout pilot: `ThemeLayoutHost` shows a theme's layout when Theme layouts is on and the layout validates, otherwise the
  built-in one. Home publishes five components, four values and five commands; a layout can use nothing else.
- **Reader:** `Match app` follows the live theme (`reader.background.page` / `.text`); the PDF reader's error box uses reader roles.
- **Authoring without an editor:** `docs/themes/README.md`, `theme.schema.json`, two sample packages (`harbor`, `gazette`).
- **Behaviour change to call out:** `Match app` on the default theme is now the theme's own surface, not the old fixed `#14161B`.
- **Cut or partial, on purpose:** packaged fonts are checked but not loaded; no per-element blur; only the nav rail uses semantic icons;
  motion plays on appear only (no hover/focus/navigation triggers, no theme-change crossfade); extension providers are an attachment
  point with a fake provider in tests and no plugin runtime; no timing baseline was taken, so no speed claim.
- **Open:** the Home layout pilot go/no-go (D10) needs real use before other screens get contracts.
- **Not verified:** every new visual on a real screen in any theme; keyboard, focus and screen-reader behaviour of Home inside a theme
  layout in the real window; scrolling performance inside a layout; switching theme with a reader open; the real file pickers.

## 2026-10-13 - CBL Manager Smart Match, part 1: the matching core (built, tested, not seen on screen)

Design: `docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md`; plan beside it. Uncommitted. How the CBL manager works
overall: `docs/cbl-manager-context.md`.

Why: a read-only audit of the real library found 80 reading-list entries reading "missing", 67 of them comics already owned. 50 sat in
the same series row with the same number and had simply never been re-matched after a scan; 17 sat in a second series row with an
equivalent name, which the old first-match lookup never searched.

Built:
- `ReadingLists/Matching/`: `IssueNumberKey`, `SeriesKeys`, `LibraryIndex` (one library read per operation), `MatchEngine` (provider
  ids first, then series + number across every equivalent series row; year/volume disagreement or several volumes = ask, never guess),
  `ReadingListResolver` + `ImportReport`, `ReadingListHealer`, `ReadingListRepair`.
- `ReadingListItemSource` table (migration `AddReadingListItemSource`, with a backfill for entries already waiting on a placeholder):
  what the source asked for, kept apart from the linked issue.
- ComicVine/Metron adapters pass issue ids through; ComicVine, Metron lists and Comic Book Reading Orders report entries they drop.
- `.cbl` import reads `<Database Name="cv" Series= Issue= />` ids (format confirmed against the public CBL-ReadingLists collection).
- Every scan path relinks waiting entries (`LibraryFolderScanner.HealReadingLists`); Activity Center alert when something was linked.
- List page: "Recheck library" in ⋯, counts in import/refresh messages, "needs a choice" rows, a Suggested group in Find & link.
- Library Health > Review > "Reading list repairs": Check (changes nothing), then Link.

Verified: 696 data tests and 246 targeted app tests pass (new: `MatchEngineTests`, `ReadingListHealingTests`,
`ReadingListSmartMatchTests`). Run against a **copy** of the real database: migration applies, backfill writes 80 records, the repair
links 63, leaves 4 as "needs a choice" (the owned copy's year is the series' start year, 2-3 years off the issue's), 13 really missing.
Not verified: anything on screen; the Metron row fields (`cv_id`, `series.id`) against a live response.

Deliberately different from the design: the repair does not delete emptied series rows (Library Health's Empty Rows queue already does
that safely), and its section is on demand like Publisher logos rather than appearing by itself.

Next (own specs): the review UI (side panel, bulk review, import preview, undo), then the remaining healing triggers (scrape, metadata
edit, rename) and a source refresh that keeps hand-added issues.

## 2026-10-14 - Manga series-centric metadata (Edit Series, inheritance, durable file sync, Sources)

Spec: [`2026-10-14-manga-series-metadata-design.md`](superpowers/specs/2026-10-14-manga-series-metadata-design.md), plan: [`...-plan.md`](superpowers/specs/2026-10-14-manga-series-metadata-plan.md). Built from a ChatGPT spec that was audited against the code first; the parts that did not match the repo (issue columns are the source of truth, the write queue is in memory, a provenance/identity stack already exists) changed the design (spec §2). **Uncommitted, on `master` in the shared tree. Not yet looked at on screen.**

- **Edit Series replaces Edit** on the Manga Detail hero (rooted in the series, works with no chapters). A large overlay: Identity & titles, Creators, Publication, Classification, Description, Sources, Advanced. The chapter context menu has "Edit Chapter Details" and "Edit Series…".
- **Inheritance by fan-out** (D2): Save writes publisher, imprint, the seven credit roles, genres, tags and age rating into every chapter without an override, in one transaction; chapter number, title, volume, translator, scan group, language, summary, files and reading state are never touched. Chapters that already differ get a Keep-as-exceptions (default) / Replace question. New chapters and re-read files take the series values. Manga to Comic restores each chapter from a one-time snapshot.
- **Durable file sync** (Slice 2): `EmbeddedDirty` + fingerprint + file stamp + state on `Issue`; startup catch-up queues only dirty chapters; unchanged files are not rewritten; a file changed outside Paperbunkr is left alone until "Write anyway"; retry 5 s / 30 s / 5 min then Needs attention; live progress row for 20+ files; Verify embedded metadata reads and writes nothing. Comics are written exactly as before.
- **Sources / locks** (Slice 3): hand-set fields are locked against providers (a provider's value becomes a pending candidate to accept or dismiss); provider genres/tags go onto the series and down to non-overridden chapters; a joined author/artist list is never applied without choosing a role; "Needs review" chip when chapters disagree.
- **Migration** `AddMangaSeriesMetadata` (4 tables, 10 columns); all 157 earlier migration tests still pass.
- **Verified:** the whole Data test project (2,693 tests; two failed: the remote opt-in allowlist, which I fixed for my files, leaving only `MilestoneResolver.cs`, untracked work from another session, and `SynopsisGenreInferrerTests.Infer_PicksTheGenreWithTheMostWords...`, which passes when run alone, so a flake under full-suite load) and a targeted App run of 945 + 164 tests over scanner, detail screens, editors, write-back, input and remote-library; the overlay is shown by a real `MainWindow` in a headless test. **Not verified:** on screen with the real app and library (the migration only runs on the first real launch), file write-back against a real CB7/CBR (never written, by design), the real-window Escape key (the existing Issue Properties overlay behaves the same in the headless harness).
- **Deviations from the ChatGPT spec, deliberate:** no `MangaSeriesProfile` (dates, expected counts, platform and country deferred); chapter values are a fan-out cache rather than a read-time resolver; no job table (a column on the chapter instead); embedded write-back stays off until the user turns it on in Preferences; services use default contexts, so another library's series are invisible to them (read-only by construction).
- **Known limits:** no "from series" marker on the chapter rows themselves (the Issue Properties editor shows the exceptions); the content type is still changed from the series header, not inside the overlay; a provider link from the Details tab queues its file writes through the same dirty flags.

## 2026-10-14 (later) - Weighted tags, full MangaBaka scrape, reading mode picker

Spec: section 14 of [`2026-10-14-manga-series-metadata-design.md`](superpowers/specs/2026-10-14-manga-series-metadata-design.md). Follow-up to the entry above; **uncommitted, on `master`, not yet looked at on screen.**

- **Weighted tag editor** in Edit Series (Classification tab): rows with category and weight (none / Incidental / Recurrent / Defining / Core), add by pasted comma list, remove, lock marker. Found and fixed on the way: tag comparison looked at names only, so changing just a weight was never saved or passed down.
- **MangaBaka full scrape:** the provider now reads tag weights (incl. genre tags), separate authors and artists, publishers by type, content rating, original language, the 0-100 rating, all alternate titles and `merged_with` (followed to the live entry). A refresh of a confirmed manga series follows three tiers: always applied (status, rating, titles, tags with weights, additive, unset weights only), filled only when blank otherwise offered as a candidate (summary, Writer, Penciller, original publisher, age rating), only ever suggested (reading mode). Spoiler tags are skipped unless the Sources switch is on. Sources gained "Accept all".
- **Reading mode:** one `ReadingModeCatalog` for the reader, the Detail screen, the manga badge and Edit Series; the Detail card now has a picker for all seven modes (it flipped between two, and showed Webtoon and the RTL strip as "Left to Right"); Edit Series has the same field. A manga series' chapters are marked for a file write only when the right-to-left flag changes.
- **Verified:** Data tests for the scrape (normalizer against a realistic payload; each tier, locks, dismissed candidates, merges, Comics unchanged) and App tests for the editor, the picker and the catalog; a real `MainWindow` shows the weight selectors and the reading-mode picker. **Not verified:** a live refresh against the real MangaBaka from inside the app (the payload shape was read with curl and mirrored in a test), and nothing on screen.
- **Deliberately not done:** series start/end dates, `is_licensed`, the provider's links (nowhere to store free-form links), edition publishers.

## Explicitly not in scope here

- **Content-type classification manual dropdown** — flagged as a known gap, but the real
  auto-classify pipeline (§7/§9) is scoped as Beta work. No Alpha-side fix needed beyond what's
  already shipped; leave the manual dropdown as-is until Beta.

---

*Beta backlog is tracked in [`Paperbunkr-Roadmap.md`](Paperbunkr-Roadmap.md) and not duplicated here.*

### 2026-10-14 — detail-screen follow-ups (built, unit-tested, not yet seen on screen)
- Edit Series: dropdown/smart suggestions (`SuggestBox`) on every credit/publisher/age-rating field and the genre/tag lists, filled from the library's own vocabulary.
- MangaBaka refresh now populates **unconfirmed** manga series too (seeded from chapters; tags additive).
- Descriptions (comic + manga `DetailBand`): `MarkdownLite.ParseDescription` renders bold/italic/underline, provider HTML, BBCode, `[text](url)` and bare URLs (clickable via `ExternalLinks.OpenWebCommand`); one paragraph per line.
- Reading-mode description on the Details card left-aligned.
- Comic Continuities card: "Look up on Wikidata" replaced by "Look up on Metron" (`ContinuityMetronSeriesLookup`; needs the Metron login; matches issues by Metron id, ComicVine id, then barcode). Hidden on the manga screen.
- Details-tab Web field: several distinct addresses are now one clickable row each instead of a joined blob.

### 2026-10-15 — remaining provider metadata and integration audit (uncommitted)

- AniList now requests the score, adult flag, MAL id, synonyms and tag category that its normalizer already read, plus full partial publication start/end dates and popularity. The linked provider facts appear with attribution on both detail screens.
- MangaBaka retains extra upstream source IDs; ComicVine volume descriptions fill blank series summaries through the existing search result. Repaired ComicVine's `field_list` URL regression.
- Pull-list release requests fail on incomplete pagination before replacing the cache; missing series enrichment is retried after a day. Automatic tracker push skips an unverified remote read; Kitsu updates preserve privacy and report incomplete score/date saves.
- Audited Wikidata, GCD, pull lists and all eight trackers in [provider-utilization-audit.md](provider-utilization-audit.md). Wikidata's active fetch was deliberately retired. The audit records remaining OAuth refresh, three-way tracker read, and durable GCD lookup work as separate integration tasks.
- Verification: Data and App test builds succeeded; consolidated provider, manga, ComicVine, pull-list and Kitsu Data tests passed (173/173), and detail-screen/tracker App tests passed (142/142). No live authenticated API requests or real-library launch in this session.

### 2026-10-15 — existing comic series summary backfill (uncommitted)

- A successful scrape could fill a blank comic series summary, but already-scraped series remained blank. The new enabled weekly network maintenance task fetches descriptions using confirmed Metron/ComicVine series IDs, starting on the next launch. It fills blanks only and reports unlinked series, missing credentials, missing provider descriptions and lookup failures. It never substitutes an individual issue's plot or overwrites a user edit.
- This cannot promise a real synopsis for **every** comic series: an unlinked series or a provider record without a description has no trustworthy source text. The detail screen continues to say “No summary available” for those cases.
- Verification: focused Data backfill tests passed (3/3), the App test build succeeded, and the scheduler descriptor test passed (1/1). A read-only count of the current library found 773 local Comic/Unknown series with blank summaries; 484 have a confirmed comic-provider **series** ID eligible for this backfill, while 289 do not. No live provider run or library write was made in this session.

### 2026-10-15 — Reading List tags, source marks, and Continuity event details (implemented, locally verified)

- Reading List tags now reconcile generated metadata from source, list type, story event, continuity, and member issue Genre/Tags. Generated rows keep provenance separate from manual tags, update when list membership or metadata changes, and are safely non-editable as curated tags. Migration: `AddGeneratedReadingListTags`.
- Reading List source marks reuse the bundled ComicVine and Metron artwork next to the existing source text. `Assets/Marks/SOURCES.md` records the active sources still needing marks: Comic Book Reading Orders, ComicArc, ReadingOrders.net, and ReadThingsRight.
- Continuity event rows now expand to show descriptions, curated tags and member-comic tags, covers, and persisted related events. The existing event editor now accepts optional ISO dates, curated tags, and a manually selected cover. Compatible provider arc IDs are enriched in a throttled best-effort task; non-empty descriptions and manual covers are preserved. Migration: `AddContinuityEventDetails`.
- Verification: App build succeeded (Avalonia XAML compilation included); focused Data tests passed (15/15, including migrations, tag reconciliation, event persistence, and source-mark mappings); focused Continuity overview tests passed (14/14). The checklist review covered theme tokens, accessible labels, and deferred row replacement. Real-window visual checks across themes and sizes remain unverified; no performance benchmark or live provider request was run.

### 2026-10-16 — Bulk Metron series discovery for Continuities (implemented, uncommitted)

Spec and plan: [`2026-10-16-bulk-metron-continuity-discovery-design.md`](superpowers/specs/2026-10-16-bulk-metron-continuity-discovery-design.md) and its plan. Current base HEAD: `1d714d48739ffd33f03cc4b5922a3745b0aefdf2` (shared `master`; feature changes are uncommitted).

- Added a paginated Metron universe search and `/series/?universe_id=...` catalog query. The Continuity hero now offers **Find series on Metron**; a review panel stages exact matches by namespaced Metron/ComicVine IDs or GCD series ID, identifies conflicts and current members, and offers Select all exact / Clear / Apply. No title-only matching or placeholder creation. A chosen universe ID is saved only after a complete review is applied; Apply remains disabled after failure/cancellation.
- Membership additions use the existing `ContinuityResolver` in a transaction, preserving the established append order and idempotence. No migration was needed.
- Verified: app build succeeded with Avalonia XAML compilation; focused Data tests passed (4/4: paginated universe and series catalog queries plus identity matching/conflict cases); focused Continuity page/ViewModel and compiled-view tests passed (21/21). Existing unrelated build warnings remain. Not verified: live authenticated Metron responses or visual/keyboard inspection in the running desktop app. Cancellation/apply transaction behavior lacks direct ViewModel tests.

### 2026-10-18 — Comic and Manga detail layouts (implemented in source, unverified)

Spec and plan: [`2026-10-18-detail-screen-list-layouts-design.md`](superpowers/specs/2026-10-18-detail-screen-list-layouts-design.md) and its plan.

- Comic Issues and Specials share one saved detail-type default and named layouts. Poster, List, and Card each expose their own configurable fields; Comic List fields have editable order and widths.
- Manga Chapters have configurable ordered columns and saved sort field/direction. Volume grouping remains and the active chapter filter is unchanged by layout application.
- Detail types use separate `WorkspaceScreen` keys and the existing `*` assignment; no migration or per-series layout assignment was added. Detail List Options and Edit Layouts reuse the existing layout service.
- **Not verified:** no build/tests or live UI review were run in this session; compiled XAML, keyboard behavior, and theme/layout checks remain open.

### 2026-10-19 — App-wide UI polish, sub-project 1: Foundation (implemented, tests pass, not seen on screen)

Audit: [`2026-10-19-ui-polish-audit.md`](superpowers/specs/2026-10-19-ui-polish-audit.md). Spec and plan: [`2026-10-19-ui-polish-foundation-design.md`](superpowers/specs/2026-10-19-ui-polish-foundation-design.md) and its plan. Sub-projects 2–5 (shared controls; icons and a11y; screen passes for dialogs, Library and Reader first; leftovers) are designed in outline only.

- **Type scale:** one whole-pixel scale, 10 · 11 · 12 · 13 · 15 · 18 · 22. 704 literal sizes were snapped; icons and sizes of 24+ were left alone. New `pbTextTitle`, `pbTextSection`, `pbTextMeta` and `pbTextOverline` roles.
- **Radii:** 237 literal radii and 29 `StaticResource PbRadius*` now use `DynamicResource` tokens, so the theme's corner style applies everywhere. Radii of 2 or less and true circles stay literal.
- **Text colour:** readable text no longer uses faint (3.1–3.8:1 in most themes). 460 text, button and tab uses moved to muted. Faint stays for disabled states, icons and dividers.
- **Fields:** every TextBox, ComboBox, NumericUpDown, AutoCompleteBox and SuggestBox is an underlined field. It's drawn by the new `Styles/FluentBridge.axaml`, which maps FluentAvalonia's `TextControl*` / `ComboBox*` keys onto Pb tokens. Placeholders are muted and italic. Toolbar search boxes (`searchBox`) keep a rounded border; boxes embedded in a pill use the new `bare` class.
- **Popups and tooltips:** `FlyoutPresenter` and `ToolTip` use the floating-panel look.
- **Dialogs:** the `OverlayShell` ✕ now sits inside the card. One shared `fieldLabel` replaces 12 local copies. New `dialogCard` / `dialogTitle` / `dialogFooter` / `inlineError` styles are ready for the sub-project 4 dialog pass.
- **Other:** the Detail band's stripe and Virtual Tags chip use the accent, not the badge colour. Page margin is 24 px (was 28/32) on standard screens and their sub-views.
- **Verified:** the app builds with a fresh XAML weave. New `XamlConsistencyLintTests` (5) and `FoundationStyleTests` (6) pass, and so does the existing theme, focus-ring and view render set (74/74 in total). Headless renders in Default, Daylight and Matrix show the underlined fields, focus line and placeholders as designed.
- **Not verified:** the running app on screen (dialogs, Edit Series, Preferences > Connections, a continuity popup, tooltips, the Detail band), and the red error underline under FluentAvalonia. Spacing inside screens and per-dialog anatomy are deliberately left for sub-project 4.

### 2026-10-19 — UI polish, sub-project 2: Shared controls (implemented, tests pass, not seen on screen)

Spec and plan: [`2026-10-19-ui-polish-shared-controls-design.md`](superpowers/specs/2026-10-19-ui-polish-shared-controls-design.md) and its plan.

- **One file:** the new `Styles/Controls.axaml` owns eight families: buttons (28/22 px, danger, iconButton, toggle state), segmented switch (solid accent), underline tab, sidebar item (accent bar plus tint), chips (filterChip, pbChip, statusBadge), card, clickCard and menu row.
- **Outlines:** the theme roles for secondary/ghost button and chip borders default to the faint colour (`ThemeComponentRegistry`).
- **Migration:** 700 class renames, 175 local style blocks deleted and 46 trimmed to layout-only setters, 276 per-button size overrides stripped, 38 buttons switched to `.sm`.
- **State styles restored after the migration:** `primary.destructive`, the Detail band tag weights, Connections tile states, gamepad key chips, and selected cards. Library lens tabs are now `segItem segToggle`, so the bumpers still step through them.
- **Lint:** `XamlConsistencyLintTests` gained three rules: retired classes, local restyling of a family, and size overrides on family buttons.
- **Verified:** build OK. Lint, `SharedControlsStyleTests` (7 plus a render sheet), `FoundationStyleTests`, the colour lint, Reading Lists and Library lens tests pass: 51/51. The earlier 164-test run also passed apart from 2 Reading Lists ring tests, which fail only late in a long run and pass alone.
- **Not verified:** on screen.
- **Caveat:** the migration deleted local style blocks in place. For other sessions' *uncommitted* work there's no backup. Every state class still bound in XAML was checked and has a style, but a purely decorative local look in uncommitted files may have been replaced by the shared family look.

### 2026-10-19 — UI polish, sub-project 3: Icons and accessible names (implemented, tests pass, not seen on screen)

Spec and plan: [`2026-10-19-ui-polish-icons-a11y-design.md`](superpowers/specs/2026-10-19-ui-polish-icons-a11y-design.md) and its plan.

- **Icons:** text characters used as icons are now FluentIcons.
  - 40 glyph buttons: 22 became `iconButton sm`, 18 became icon plus label.
  - Also converted: the ▾ on dropdown chips, the ★ rating, ● (now an Ellipse), ✓, 🔖, ↻, ↔ and the ⠿ drag handle. The Reading List drag
    check now accepts the icon.
- **Names:** every icon-only button has an `AutomationProperties.Name`. 34 came from tooltips; the rest (ratings, goal delete, collection
  create/cancel, zoom in, rail cards, reorder/rename/remove) were written by hand.
- **Logos:** the Continuity sidebar keeps a fixed 34 px logo column, and Connections tile logos have a max width.
- **Lint:** `IconButtonsHaveNames` and `NoTextGlyphIcons` added.
- **Verified:** build; lint, shared controls, Library lens, Reading Lists, Smart templates, Insights focus and colour lint pass (59, then 25).
- **Not verified:** on screen.

### 2026-10-19 — UI polish, sub-project 4a: Dialogs (implemented, tests pass, not seen on screen)

Spec and plan: [`2026-10-19-ui-polish-dialogs-design.md`](superpowers/specs/2026-10-19-ui-polish-dialogs-design.md) and its plan.

- **`Controls/DialogCard`:**
  - Sizes: Small 420, Medium 560, Large 760, XLarge 960.
  - Header holds the title, subtitle and an ✕ inside the card (it takes over the enclosing `OverlayShell`'s close command and hides the
    shell's ✕).
  - Max height is the window height minus 48, tracking resizes; the body scrolls; the footer has a divider.
- **Migrated onto it:** workspace name, confirm, quick rate, update available, merge, goal editor, new reading list, new event/continuity,
  list options, detail list options, edit layouts, book / bulk book / book series properties, collection properties, paste data,
  reading-list properties, migration, legal viewer, template gallery, manga series editor (XLarge), continuity compare (XLarge), and the
  plugin modal host (scraper dialogs).
- **Fixes:**
  - Reading-list and collection titles: no double quotes; the name is the subtitle.
  - Template gallery: `UniformGrid` with 12 px spacing.
  - Goal title uses "N", not "?".
  - Cover picker: no nested card; the tabs are an equal-width `segGroup`.
  - MangaDex / credential fields have labels.
  - Connection status errors use `errorText`.
  - Tracker sync failures show a plain sentence with the raw reply behind Details.
  - Disabled buttons have a visible neutral style.
- **Lint:** `DialogsUseDialogCard`.
- **Verified:** build; DialogCard (8), lint, style, and the dialog view-model and view tests pass (146 of 150). The 3
  `ReadingListPropertiesScreenViewModelTests` tag tests fail on auto-generated tags from other uncommitted work, unrelated (only
  `HeaderLabel` changed there). Renders checked in Default and Matrix.
- **Not verified:** on screen.

### 2026-10-20 — UI polish, sub-project 4b: Library (implemented, tests pass, not seen on screen)

Spec: [`2026-10-20-ui-polish-library-design.md`](superpowers/specs/2026-10-20-ui-polish-library-design.md).

- **Headings:** the sidebar title uses `pbTextHeading`, Library group headers and "Continue reading" use `pbTextSection`, and sidebar
  group labels use the overline style.
- **Hover card on a series tile:** shows series name, publisher, issues · unread and status, not the first issue's title, credits,
  format and language. It also uses the floating-panel look and is capped at 320 px.
- **Tile corner badges:** all inset 6 px with a minimum height of 20.
- **Preview panel actions:** use primary / secondary / iconButton; the local `previewAction` look is gone.
- **Continue Reading strip:** right edge fades.
- **Narrow toolbar** (< 1000 px): the Workspace button shows only its icon, and the search box keeps a 140 px minimum. At 860 px nothing
  overlaps. Scan and Resync exist only in this toolbar, so they stay.
- **Spacing:** 34 odd margin/padding values in the Library files snapped to the 4 px scale; even values (6/10/14) left as they were.
- **Verified:** build; Library lens, screen view, open keys, list layout, lint and style tests pass (55). Toolbar render checked at
  860 px.
- **Not done:** a dedicated test for the series hover card.
- **Not verified:** on screen.

### 2026-10-20 — UI polish, sub-project 4c: Reader (implemented, tests pass, not seen on screen)

Spec: [`2026-10-20-ui-polish-reader-design.md`](superpowers/specs/2026-10-20-ui-polish-reader-design.md).

- **Reader Tools drawer:** 280 wide, starts below the top row (no longer covers zoom / bookmark / more), and tools are one per row
  (no truncated labels).
- **Breadcrumb:** trims in a grid column instead of overflowing into the reading-order navigator. "←" is now an `ArrowLeft` icon.
- **Reading-stats chip:** hidden while the drawer or Info panel is open.
- **Chrome buttons:** `clusterPill` and `clusterIcon` are 28 tall (icons 28×28), and 10 per-button padding overrides were removed, so the
  bottom bars match.
- **Info panel:** "Show summary" is a link button. Previous / Next / Open details / Edit properties are `secondary sm`.
- **Drawer actions:** Save note / Delete / clip Copy / Save… / Delete are `secondary sm`. "Export notes and clips…" is left-aligned.
- **Verified:** build; ReaderChrome, ReaderChromeTint, ReaderInfoPanel, ReaderScreenKey, ReaderScreenPanel and lint (90) pass.
- **Not verified:** on screen.

### 2026-10-20 — UI polish, sub-project 4d: Remaining screens (implemented, tests pass, not seen on screen)

Spec: [`2026-10-20-ui-polish-remaining-screens-design.md`](superpowers/specs/2026-10-20-ui-polish-remaining-screens-design.md).

- **Screen titles:** Insights, Smart, Plugins, Issue properties, Bulk issue properties and Bulk series properties use `pbTextHeading` with no
  local size / weight / colour.
- **Manga detail:** the band no longer repeats the content-type picker's value as a special mark ("Manga" twice); the picker is
  `PbControlHeight` tall.
- **Series status chip:** icon only (the dot beside it is hidden).
- **Home:** the spotlight sliver shade is a token, `PbCoverShadeColor`, instead of a hex in HomeStyles.
- **Synopsis:** lines of only dashes / underscores / equals signs / en or em dashes are dropped (`MarkdownLite.ParseDescription`, new test).
- **Spacing:** 138 odd margin/padding values in the remaining views snapped to the nearest multiple of 4. Kept: even values, 1, negative
  overhang offsets, the toast text indent, the MainWindow sidebar offsets and the splash screen.
- **Tests updated:** `DetailTabsFocusTests` finds the card-view tile by the shared `card` class (renamed in SP2); the hardcoded-colour
  baseline moves one literal from HomeStyles to AppTokens.
- **Verified:** build; lint, colour baseline, MarkdownLite, DetailTabs, Manga detail, Home, Insights, Smart, Wanted, focus ring, shared
  controls, dialog card, Continuity, Books, Reading Lists, Activity, Event Map, Welcome, Connections and status bar tests pass. Seven
  reading-list tag tests fail because of the separate, uncommitted auto-tags work (generated "List type" tags), not this pass.
- **Not verified:** on screen.

### 2026-10-20 — UI polish: fixes from the user's on-screen check (implemented, not yet re-checked on screen)

Ten screenshots after SP4. Causes and fixes:

- **Dialogs clipped on both sides** (Scrape complete, Delete reading list): `DialogCard` had a fixed `Width`, and the template frame's
  `Border.dialogCard` style a 560 width. In a host narrower than the card it was centred and clipped. The card now measures at
  `min(size width, available width)`; the frame has no width. The confirm dialog's message sat in a horizontal StackPanel and never wrapped;
  it's a Grid now. New tests: `ANarrowHost_ShrinksTheCardInsteadOfClippingIt`, `ConfirmDialog_LongMessageWrapsInsideTheCard`.
- **Edit Issue tabs ran together** ("SummaryDetailsPlot & Notes"): `Button.tab` has no side padding and that strip had no Spacing. Every tab
  strip is now `Spacing="20"` (it was 4, 18, 20, 22 or none); Insights' local tab margin was removed.
- **Edit Issue "Unknown" above Age Rating:** `BrandMark` prints a value with no mark as text. New `TextFallback="False"` on the editor's
  Format / Age Rating / Language marks.
- **"Comic824" in the content-type dropdown:** `menuRow` left-aligned its content, so a `*,Auto` row collapsed. It stretches now.
- **Workspaces menu ran past the window:** fixed `MaxHeight="620"`. New `Controls/PopupFit` (`PopupFit.ToWindow="True"`) caps a dropdown
  at the room left above/below its anchor; applied to all 14 `dropdown` popups, each now scrolling.
- **Smart Lists sidebar badges over names:** `sideItemLabel` had no base style; it trims now with 8 px before the pill. Maintenance rows use
  the same count pill, and the expand arrow is a chevron like the collapsed one.
- **Smart rule rows out of line:** fixed field (160) and operator (150) widths, the remove button in its own right column, `iconButton`.
- **Insights goal card glowed on hover** though it isn't clickable: `heroTile.inert` (no glow, arrow cursor).
- **Test harness:** `FocusTestHarness.AppStyles()` now loads Controls.axaml too (card padding lives there since SP2).
- **Not changed, asked the user:** the Library tile publisher mark (white, by design via `EnsureContrast`) and the cover-style popup's right
  edge — the cause wasn't clear from the screenshots.
- **Verified:** build; DialogCard (2 new tests), dialog service, lint, colour baseline, shared controls, BrandMark, Smart, Insights focus,
  DetailTabs, issue properties, Library lens/open keys/toolbar, Wanted, list layouts, manga detail and all 30-odd `AppStyles()` focus classes
  (296) pass. Four fail only in that 17-minute run and pass alone (2 theme skin/leak tests, 2 Reading Lists ring tests).

### 2026-10-20 — UI polish, sub-project 5: Leftovers (implemented, tests pass, not seen on screen)

Spec: [`2026-10-20-ui-polish-leftovers-design.md`](superpowers/specs/2026-10-20-ui-polish-leftovers-design.md).

- **Width animations:** the nav rail, contextual sidebar and reader page dot keep their `Width` transitions; each now says why in the XAML.
- **Easings:** 8 transitions gained `PbMotionEase`. New lint rule `TransitionsHaveEasing`.
- **Matrix rain:** each glyph draws with Cascadia Code / Consolas when it has the glyph, else the system font Skia matches
  (`MatrixRainVisualHandler.TypefaceFor`, cached per glyph, disposed with the paint). New test.
- **Narrow Library toolbar:** below 900 px an empty, unfocused search pill becomes a search icon; a click, Ctrl+F or a query set elsewhere
  opens it. New test `NarrowToolbar_CollapsesTheEmptySearch_AndFocusSearchOpensIt`.
- **DPI sweep:** toolbar renders at 750 and 1066 px, ×1 / ×1.25 / ×1.5, and the dialog at ×1.25 / ×1.5 (`PB_RENDER_SCALE`): crisp and
  aligned, nothing to fix. The sweep found a test-harness gap instead: `FoundationStyleTests.ApplyTheme` didn't write `surface0`/`surface1`,
  so light-theme renders showed dark option cards. Fixed.
- **1366×768 pass:** covered by the user's own ten screenshots at that size (fixed in the entry above) plus the toolbar renders; the
  full-screen renders in the spec were not done.
- **Verified:** build; lint, foundation/shared-controls styles, DialogCard, Matrix rain, Library lens, toolbar and reader/activity/books
  tests pass (77 + 87).
- **Not verified:** on screen.

### 2026-10-20 — UI polish: second on-screen check (implemented, tests pass, not yet re-checked)

- **Cropped icons in Preferences > Library folder rows:** icon-only `ghost` buttons squeezed to `Width="30"`; ghost padding is sized for a
  label. 52 icon-only buttons across 16 views moved to `iconButton` (the hand-styled 20/24 px sidebar buttons — the white squares in the
  Smart Lists header — to `iconButton sm`); names taken from tooltips where missing. Insights' Delete goal (was the `attnRow` row style) and
  an EventMap button too. New lint rule `IconOnlyButtonsAreNotTextButtons` (ghost/primary/danger icon-only, or any sized text-family one).
- **Virtual Tags row icon cropped:** a `pbChip` used as a 24 px icon tile (chip padding). Now a plain 26 px `vtIcon` tile like the folder rows.
- **Verified:** build; lint, shared controls, Insights focus, Reading Lists view, sidebar keyboard, Wanted, EventMap, collection properties
  tests (184) pass. **Not verified:** on screen.
- **Detail screens' content-type picker: arrow over the label** ("Manhwa"). The SuggestBox chevron button overlaps the text box's right edge
  (`Margin="-28,0,0,0"`) and nothing reserved that space, so any SuggestBox with a long enough value or a custom Padding ran under it. The
  shared template now puts a 22 px `InnerRightContent` spacer in PART_TextBox. Tests: SuggestBox, detail screens, foundation, lint (117) pass.

### 2026-10-20 — Motion system v2, sub-projects A + B: Foundation and Overlays (implemented, tests pass, user-verified on screen 2026-10-21)

Spec `docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md`, plan alongside. Replaces the 10-17 motion
vocabulary (one cubic curve, four durations, `BackEaseOut` as "spring") while keeping its policy layer.

- **Roles and tokens:** `MotionRole` (Acknowledge/Hover/Reveal/Replace/Travel/Ambient) + `MotionPolicy.Resolve`; role tokens with separate
  fade/move durations (Reduced keeps short fades, zeroes all travel); `PbMotionFast/Standard/Large/Slow` kept as aliases. Curves are
  `SplineEasing`s plus a real damped spring (`DampedSpringEasing`, ~3 % once). `MotionClass` deleted; ~25 call sites moved to roles.
- **Motion preference:** System / Full / Reduced / Off (was System/On/Off). Data migration `MotionPreferenceFourModes`: old "On" → Reduced,
  old "Off" → **Full** (it meant "reduced motion off"). Performance quality now keeps dialogs/popups/hover, drops Travel and Ambient.
- **Feel fixes:** no overshoot on every click (Button `BackEaseOut` removed); press `scale(0.97)` only on compact families (rows/tabs/menu
  rows colour-only); Library cover lift −3 px/1.02 with the spring; reader bookmark glow animates opacity, not BoxShadow.
- **Overlays:** every `OverlayShell` dialog fades and settles from 0.98, exits faster, never blocks the close (shared `Controls/Reveal.cs`
  lifecycle); legacy discard/tour modals moved onto it; Activity + Reading Lists drawers 20 px with a spring and a real exit (fixes the
  closed RL drawer being Tab-reachable); toasts rise 8 px; every popup/menu/flyout gets a 4 px entrance (`PopupMotion`), instant close.
- **Guard rails:** 7 new lint rules (no overshoot easings, no BoxShadowsTransition, justified layout transitions, token-only transitions,
  distance scale, scoped press scale, legacy-token baseline 45). `PB_MOTION_SCALE=5` slows everything ×5 for tuning.
- **Verified:** build; Data migration tests (22); motion policy/spring/overlay lifecycle/theme tests; lint + shared controls + splash (31);
  ~700 targeted view and real-window tests. Fixed one pre-existing order-dependent test (`ThemeSkinTests.InstantMotion`).
- **Not verified:** on screen. Check per spec §7 at `PB_MOTION_SCALE=5`, then at 1.
- **Next:** sub-project C (navigation: lateral settle, intent supersession, cover-flight hardening), then D (on-page), E (reader chrome).

### 2026-10-20 — Motion system v2, sub-project C: Navigation (implemented, tests pass, user-verified on screen 2026-10-21)

Spec `docs/superpowers/specs/2026-10-20-motion-system-v2-navigation-design.md`, plan alongside.

- **One screen host, views kept:** `Controls/RouteHost` replaces the lateral + drill-down `TransitioningContentControl`s. One view per
  screen view model, reused (readers rebuilt: `ITransientScreen`). Fixes: crossing lateral ↔ drill-down was a cut; Back to Library was a
  full-width slide from blank in a stale direction; **every return rebuilt the screen's view, and `LibraryScreen`'s un-unsubscribed VM
  handlers kept every old copy alive** (probe-verified rebuild; now built once + wire-once guard).
- **Route transitions by intent** (`MainViewModel.RouteIntent`): rail switch fades + settles 12 px up (220 ms), drill-in grows from 0.985,
  Back reverses, reader crossfades; rapid clicks retarget, never queue; Off swaps instantly (old screen hidden, removed a tick later —
  never detached inside the navigating click). Drill-in into a kept view starts at the top; Back/rail keep scroll and focus.
- **Cover flight hardened:** real covers hidden at both ends during the flight (restored in `finally`), destination resolved on a layout
  pass instead of a 16 ms poll, a newer navigation / resize / window-state change cancels it.
- **Breadcrumb:** segments enter 6 px on the Replace token; bar shows/hides instantly (layout row).
- **Verified:** build; RouteHost, route intent, cover-flight + coordinator, MainViewModel (83), lint, ~120 real-window/keyboard/Library/
  Compare/Wanted tests. **Not verified:** on screen — rail clicks, rapid clicks, Library → Detail → Back (scroll + focused card kept),
  Detail → Reader → Back, Reduced/Off, at `PB_MOTION_SCALE=5`.
- **Next:** D (on-page: cards, tab indicator, expanders, entrance stagger, Spotlight, charts), then E (reader chrome).

### 2026-10-20 — Motion system v2, sub-project D: On-page (implemented, tests pass, user-verified on screen 2026-10-21)

Spec `docs/superpowers/specs/2026-10-20-motion-system-v2-on-page-design.md`.

- **Entrances:** one-shot everywhere (Books/Home/Smart/Reading list used to replay on every recycled container); owners pulse the flag on
  real result changes; stagger 20 ms × 6 groups (≤ 320 ms total), tail on one shared timer; Reduced = 100 ms fade, no stagger/travel.
- **Tabs:** app-wide sliding underline (`Controls/TabIndicator`, adorner, snaps on first placement/resize); tab panels fade in on show
  (`Controls/Replace`, wired on Insights, Wanted, Reading list, Continuity, Detail, Manga).
- **Spotlight:** accordion on the Ambient role + move curve, frame-synced (was a 450 ms hard-coded 16 ms UI timer ignoring Reduced).
- **Charts:** donut sweep removed; data changes crossfade. **Cards:** Home shelf cards lift the artwork (+ ring) like Library instead of
  scaling the hit-tested tile. **Expanders:** 9 icon-swap pairs → one rotating `chevronToggle`. Cover fade-in on Replace token + curve.
- **Verified:** build; OnPageMotionTests (11), 346 targeted view/real-window/lint tests. Fixed an order-dependent Reading-list ring test pair
  (view-test harnesses now pin motion durations to zero). **Not verified:** on screen.
- **Next:** E (reader chrome).

### 2026-10-21 — Motion system v2, sub-project E: Reader (implemented, tests pass, user-verified on screen 2026-10-21)

Spec `docs/superpowers/specs/2026-10-20-motion-system-v2-reader-design.md`. Closes the Motion v2 programme (A–E).

- **Panels & chrome:** Tools/Info drawers on the Reveal lifecycle (20 px, spring, focus returns to the opener); chapter cards fade/settle;
  hidden chrome (4 s pointer idle, unchanged) leaves tab order and hands focus to the page.
- **New page turns:** Vertical (CE TopDown), Book (CE Paging: sweep + spine shadow), Curl (new: corner fold, paper back, shadows);
  RTL-mirrored, Reduced/Off force none; no migration (enum stored as string).
- **Ambient:** real two-layer gradient crossfade (BrushTransition was cutting gradients halfway); new "Ambient motion" toggle (off by
  default, migration `AddReaderAmbientMotion`) adds parallax against pan/scroll and a slow sway.
- **Continuous mode:** wheel glide, near page jumps glide / far jumps fade, on-screen decode fades in once.
- **Caught by tests:** drift crash (ScaleTransform keyframes), focus not returned under Motion Off, reader test hosts stubbing the spring
  as a TimeSpan. Legacy-token lint baseline lowered 45 → 38.
- **Stale test fixed in house cleaning (2026-10-21):** `RefreshDisplaySettings_TextureMode_SetsShowPageShadow…` became
  `…_SoftShadowFrame_SetsShowPageShadow…` - the shadow follows `ReaderPageFrameStyle` since 10-18.
- **Not verified:** on screen (spec §4 list).

### 2026-10-21 — Motion system v2: sign-off and house cleaning

- User confirmed the whole programme on screen. avalonia-pro-max review checklist run over the Motion v2 surface (details in the reader
  spec §5): fixed a CS0108 in `RouteHost` (helper renamed `LayerTransitions`), added a Polite live region to toasts, dropped two unused
  `xmlns:easings`, corrected three stale motion comments.
- Main Debug build rebuilt with a forced CoreCompile (compiled-XAML markers verified in the fresh assembly); 199 Motion v2 / lint / reader
  tests pass.

### 2026-10-21 — Reader: spread curl, curl polish, book look for spreads (implemented, tests pass, not seen on screen)

- Spread-aware curl: one leaf turns over the spine (reported on screen).
- Curl polish: corner-first peel, lift-driven cast shadow, curvature band and edge highlight, a spine shadow on spreads, a 420 ms floor
  for Curl and Book.
- **Book look for spreads** (on by default, per profile, migration `AddReaderBookLook`): CE "Realistic pages" bows and outline, plus
  page-stack edges showing progress. Spec: reader spec §6–§7.
- **Verified:** build; reader / profile / lint tests (140, one known timing-flaky guided-view double-click test passes on its own); Data
  tests 22. **Not verified:** on screen.

### 2026-10-21 — Scraper & Organizer A/B/C: reliability, speed, automation (implemented, tests pass, not seen on screen)

Specs: `docs/superpowers/specs/2026-10-21-scraper-organizer-{a-reliability,b-speed-core,c-automation-ux}-design.md` (each has an "As built" note).

- **A — Metron API token:** new `CredentialKind.ApiToken` / `AuthScheme` (appended); one `MetronAuth` builder used by `MetronClient`,
  `MetronSource`, the factories, the daemon's `AcquisitionCycle` and account sync. Connections > Metron shows an API token field, a
  collapsed "Legacy login", and Test connection, which probes `Token` then `Bearer` and saves whichever Metron accepts (Metron's docs
  could not be read, so the scheme is learned, not assumed). Old logins keep working.
- **A — leftover swap files:** `SwapFileSweeper` (Data/Library) removes `*.crswap`, `*.pbwrite-*.tmp`, `~RF*.TMP` beside intact comics;
  a copy whose comic is missing or damaged is kept and listed in Library Health > Files > Leftover files (Restore / Delete). Runs at start,
  after each write-back batch, from that section, and in the new "Scrape health check" task. Write-back now always deletes its scratch
  (`finally`) and the organizer waits for an in-flight metadata write on a file before moving it (`FileWriteBackCoordinator.WaitForSettledAsync`).
  **Not found:** what writes the `.crswap` name — no source, DLL, install or log contains it; the settle-wait is the fix for the
  likely cause (Scrape then Organize racing the write-back), not yet proven by a reproduction.
- **B — speed:** Metron is the default source (stored settings switch once), ComicVine is tried for what Metron found nothing for;
  provider answers cached on disk (`provider-cache/`, 24 h searches, 7 d lists/details, cover hashes forever, stale answers used while
  rate-limited, Refresh in Preferences); a series is searched and confirmed once per run; the 2 s pause between comics is gone except at
  Speed = Careful; candidate covers fetched together; 1–3 file writes at once by Speed; batches of 10+ are written to `scrape-jobs/` and can
  be resumed; "Scrape all unmatched" in Preferences > Organize & Scrape > Automation.
- **C — automation:** Strictness (Lenient/Balanced/Strict moves the cover thresholds around CE's 0.87/0.77); unattended runs put what
  they can't confirm in a review list (`scrape-review.json`, a dialog grouped by series: Review / Skip / Never); undo of the last scrape
  batch (`scrape-undo/`, 30 days, restores the comic's own fields); "Scrape and organize…" in the issue and series menus (scrape, flush
  writes, organize); scrape on import after a library scan (on by default, needs Metron, skips manga); "Scrape health check" scheduled task.
- **Not built (still in the C spec):** per-field dry-run preview, the per-field provider priority table, filename/folder hints, the
  strictness live preview from history, a full keyboard-first review screen. B's per-book parallel matching was dropped: both sources are
  rate-limited, so the gain comes from fewer requests, not more workers.
- **Verified:** builds; Data tests (scraper, auth, sweeper, cache, undo, organizer: 360 + 94), Daemon pull-list 7, App targeted sets
  (Spec A 82; scraper/settings/write-back/menus/scheduler/lint ~380). One theming test (`GhostOutline_IsTheFaintColour`) fails only when
  run with other classes and passes alone. **Not verified:** anything on screen; a live Metron request with a real token; the full suite.

### 2026-10-21 (later) — Scraper C, second pass + fixes from the user's check (implemented, tests pass, not seen on screen)

- **Fixed (user-reported):** Metron wants `Authorization: Bearer <token>` (verified live: `Token` -> 401); Bearer is now the default. A refused
  credential (HTTP 401 or a held login) now ends the scrape with a message instead of being read as "issue not found", which had written only
  Publisher/Imprint/Volume/Count/series. Series-first now also applies with "Confirm the issue too" on and in the review pass. The match dialog's
  title names its source; its selected row is a soft accent fill (not seen on screen).
- **Built:** dry-run preview of every field change before applying; format and filename hints (Annual/TPB/HC/Omnibus/One-Shot/GN/Special/FCBD)
  in matching and in series-first reuse; fill Metron's blanks from ComicVine via `cv_id` (off by default); keyboard review list.
- **Verified:** Data tests 420, App targeted 108 (scraper, dialogs, settings, menus, lint). **Not verified:** the preview and review dialogs on
  screen; formats against a real library.

### 2026-10-21 (third pass) — Scraper C: which source wins, Strictness preview, .crswap origin (implemented, tests pass, not seen on screen)

- Per-field "Which source wins" (Matched source / Metron / ComicVine) through the shared issue id; Strictness description shows what each level
  would have applied to the last 500 automatic cover checks (20+ needed). Fixed: the runner-up limit moved the wrong way with Strictness.
- `.crswap` files come from a Chromium browser or web app writing through the File System Access API, not from Paperbunkr (searched the real
  AppData, plugins and install folder via PowerShell; the earlier Bash search saw a stale AppData copy).
- **Verified:** Data tests 427; App targeted 51 (settings, scraper, lint). Full suite not run (user's call).


### 2026-10-21 — Freeze watchdog redesign (implemented, tests pass, not seen on screen)

- Spec/plan: `docs/superpowers/specs/2026-10-21-freeze-watchdog-redesign-{design,plan}.md`. Quiet **stall** log + `dotnet-stack` snapshot at
  10 s (once per episode, max 3 per session); **"not responding" dialog at 30 s** (was 10 s) — a TaskDialog with Keep waiting / Close
  Paperbunkr, a live counter, an "Open log folder" link and a lost-changes warning. It closes itself when the app answers again; Keep waiting
  re-arms 30 s from the click (fixes the immediate re-pop after a long-open dialog). After a dialog-level freeze an Activity Center alert
  ("stopped responding for 47 s", Open logs). Silent under a debugger and across machine sleep.
- CE check: CE's `CrashWatchDog` is effectively off in shipped CE (`StartWatchDog` passes 0 s and the `now - d` guard resets every poll), so
  the 30 s dialog is a deliberate addition, not a parity change.
- `app.manifest` now declares Common Controls v6 (needed for `TaskDialogIndirect`); if it's ever missing the old `MessageBoxW` is the fallback.
- **Verified:** `FreezeWatchdogServiceTests` 24/24. **Not seen on screen** — check with a Debug build and `PAPERBUNKR_SIMULATE_FREEZE=45`
  (dialog at 30 s, closes at ~50 s, then the alert), `=75` + Keep waiting, and once with Close Paperbunkr.

## 2026-10-22 - Brand kit logos adopted (built, not seen on screen)

- New flat vector logo set (Brand Kit v1.0, 10 Oct 2026) replaced the old glowing raster everywhere the app and site show it: `Assets/paperbunkr.ico`
  (16-256), `paperbunkr-logo-source/rail/splash.png`, installer `WizardImage`/`WizardSmallImage`, site `logo.png`, favicons, apple-touch icon,
  `og-card.png` (top-left lockup repainted), new `site/src/assets/wordmark.png` in Nav/Footer, `docs/assets/paperbunkr-logo.png`. File names kept, so no XAML changed.
- **Fonts and the app accent were deliberately left alone** (Bebas Neue / Source Serif 4; `#C9803F` accent vs the kit's `#F7931E`).
- **Verified:** `Paperbunkr.App` builds 0 errors; `astro build` passes. **Not seen on screen:** splash, About, rail, tray, installer wizard, light taskbar.
- **Open:** the guide asks for a dedicated simplified symbol at 16-24 px; the small `.ico` frames are plain downscales.

### 2026-10-22 — ComicVine scraping "hangs" (fix implemented, tests pass, not yet confirmed by the user)

- ComicVine's API answered fully from outside the app (search, issue list, details), and today's freeze reports were unrelated (navigation),
  so the wait was inside the scrape. Most likely cause: the speed work fetched ComicVine cover images in bursts (no pause between comics, two
  candidates at once) from comicvine.gamespot.com - the API's own site - outside the API limiter, drawing a 420 "slow down", after which the
  limiter silently pauses every ComicVine call for 1/5/15/60 minutes. Not proven: the limiter doesn't log its pauses.
- Fix: `ComicVineImagePacer` (one ComicVine image at a time, 1.1 s apart, none during a pause, not counted against the hourly API budget);
  a scrape now reports a ComicVine pause in its Activity Center job ("continuing at HH:MM") instead of sitting silent.
- **Real cause found (same day, later):** ComicVine's own `/search` endpoint degraded - it timed out (20 s + retry: the "hang") or answered every
  volume with no publisher, 0 issues and the blank cover (so every candidate scored as impossible). Its `/volumes` endpoint answered the same
  query fully. ComicVine series searches now go through `/volumes` by name first (two pages, most issues first) and fall back to `/search` only
  when that finds nothing; degraded answers are never cached and older cached searches are ignored (cache key `v2|`). Verified live by probe;
  Data tests 432, App scraper tests pass. The image pacer and pause reporting above stay.
- **Follow-up:** `/volumes` matches only names containing the whole query ("The Punisher Showdowns: Black Widow" found nothing, then the degraded
  `/search` answered). The adapter now tries name variants on `/volumes` first (full, without "The", before the colon, after it) and, only if
  all are empty, `/search`, filling its first five degraded results from `/volume/4050-id`. Live check: the Punisher title resolves to
  "Punisher Showdowns: Black Widow" (Marvel, 1 issue) in under 3 s; ComicVine's `/search` timed out both ways at the time (with and without
  `description` in field_list, so that field is not the cause).
- **"X-Men" never matched after 2007 (both sources):** Metron's name search stopped at 25 results, alphabetical, crowded out by "All-New/Astonishing
  X-Men"; ComicVine ignores `sort` on /volumes and returns 1,559 X-Men volumes oldest first, so 200 never reached recent runs. Now: series are also
  found through the comic's issue number (`IIssueFirstSeriesSearch`: Metron `issue/?series_name=&number=`, ComicVine `issues/?filter=issue_number:,
  cover_date:<year>`), merged into the candidates; Metron's name search takes 100; ComicVine's takes both ends of the list (oldest 200 + newest
  200). Live: X-Men #37 -> Metron X-Men (2024) id 7914 among 14 series with a #37; ComicVine (cover year 2026) X-Men (2024) id 158814, 39 issues.
- **Specials named only in the file (2026-10-22):** "The_Amazing_Spider-Man_(2025-)_#Halloween_Special_2026.cbz" is filed under "The Amazing Spider-Man",
  while Metron lists it as its own series "Amazing Spider-Man: Halloween Special" (id 16936; ComicVine has no 2026 one yet). A worded "#…" in the
  file name (`ComicFormatHints.IssueDesignation`) is now searched too and added to the name candidates are scored against; Metron's issue-number
  search drops a leading "The".
- **"Skip this book" did nothing on click (both scraper dialogs):** the handler was a XAML `PointerPressed` on the Button, which an Avalonia 12 Button
  swallows (CLAUDE.md); Enter/Space likewise. Now tunnelled from the button, and the close is posted a tick later (routed-event detach rule).

### 2026-10-22 — Scrape no longer renames a shared series; series-level fields filled (implemented, Data tests pass, not seen on screen)

Spec: `docs/superpowers/specs/2026-10-22-scrape-series-split-and-series-fields-design.md` (grilling Q1-Q7, recommendations taken).
- A match to a different source series than the library series is linked to moves the comic out (`SeriesSplit.Resolve`: to the series linked to
  that source series, else one of that name, else a new one copying content type, reading mode and reader settings) instead of renaming the
  shared series. Only an agreeing match renames, fills series fields and links; the first rename keeps the old name as a `BeforeScrape` title
  ("Restore name ..." in the series menu). A comic alone in its series is corrected in place.
- A manual merge marks the series keep-together (`KeepTogether` in `LockedFields`; "Let scrapes split this series" clears it) and now carries the
  removed series' provider links, alternate titles and tags.
- Series-level Publisher, Imprint, Genre, Sort Name (Metron `sort_name`), Age Rating are written under the field toggles and "Overwrite existing",
  never over a locked field. No new columns.
- Existing mixed series (Secret Wars + 2099, "The Amazing Spider-Man" renamed to the Halloween Special): restore the name, then re-scrape.
- **Verified:** Data tests (split to new/linked series, keep-together, rename + restore, series publisher); app built; App tests 86 (merge keeps
  links/titles + keep-together, context menus, bulk bar, scraper, lint). **Not verified:** on screen.
- **Follow-up, "doesn't work all the time":** three holes closed. (1) An unlinked series was owned by its first match, so "Secret Wars 2099" scraped
  first renamed the whole series; now a match whose name doesn't share the series' title splits out when the series holds other comics
  (`ComicFormatHints.SameTitle`). (2) Series-first reuse let "Secret Wars #1" follow the 2099 match; the reuse key now includes the comic's file
  title. (3) A 2099 comic under "Secret Wars" searched "Secret Wars"; it now searches its file title when that extends the series name
  (`SearchName`). A lone comic joins the series already linked to its match; a new series whose name is taken gets its start year. Tests cover
  the 2099-first order.
- **2026-10-28, "bulk scrape freezes, and again after a restart":** diagnosed from a live async dump (`dotnet-dump` + `dumpasync`), not guessed.
  (1) The hourly Metron account sync (collection upload, 120 requests at 14/min, about 10 min a run) held the heavy-job lane, so a scrape the
  user started queued behind it with nothing on screen; a sync cut off by closing the app is overdue at the next launch, hence "after restart".
  `ScheduledTaskDescriptor.UsesHeavyJobLane` (default true) is false for `metron-sync` only; its requests were already Low priority.
  (2) With "fill blanks from ComicVine" on, every Metron match made a Low-priority ComicVine call, and the fallback ran at Low too; after 150 in an
  hour the limiter held each one for a free slot (up to an hour) - the 105-comic batch stopped at 100. Interactive runs now use their own
  priority for both, and an optional other-source lookup gives up after 20 s (`SecondaryLookupPatience`). (3) A resumed job's `Done` added
  itself on every progress report ("3870 of 111"); fixed. Tests: `SchedulerHeavyLaneTests` + scheduler/lane/scrape classes (43 pass).
  Not yet seen on screen.
- **2026-10-29 follow-up, "the scraper speed is extremely slow":** caused by (1) above - out of the lane, the sync now ran *during* scrapes and
  took up to 14 of Metron's 18 requests a minute (Low's share of the window), leaving a scrape about 4. `ComicVineRateLimitHandler.Options.
  ForegroundQuietPeriod` (15 s on both shared clients, zero by default): a Low request waits until no High request has been granted for that
  long, so background work only uses the gaps. The optional other-source lookup now waits 10 s at most, and after one timeout the run skips
  the rest (or skips at once while ComicVine is paused). Tests: 2 new limiter tests; Data scrape/limiter/Metron, App scraper/scheduler, Daemon (all pass).
- **2026-10-29, "X-Men never matches; ComicVine's results stop at 2007":** the real cause of the 2026-10-22 report too. Both searches did return the
  right series (verified in the user's provider cache: Metron 7914 and ComicVine 158814, X-Men 2024), but `MaxSearchResults` (100) was applied before
  scoring to the joined candidate list in arrival order - Metron's alphabetical first page of 100 ("Astonishing", "Giant-Size", "Immortal"...),
  ComicVine's oldest-first volumes, and only then the issue-first hits. The cap now keeps the best 100 by score (deviation from CE, which caps one
  relevance-ordered search). Test: `MaxSearchResults_keeps_the_best_scoring_candidates_not_the_first_to_arrive`. User-verified 2026-10-29 ("it worked immediately").
- **2026-10-29, reading list from Metron: "Metron API error (429): Request was throttled. Expected available in 19 seconds.":** the reading-list
  `MetronSource` had its own HttpClient spaced 0.5 s apart, outside the shared Metron limiter, and failed on a 429. It now sends through
  `MetronHttp.Client` (High priority, quota observed) and waits out a throttle (Retry-After, else the "available in N seconds" text; up to 65 s,
  two retries) before asking again; a longer wait (the daily cap) is reported. Tests: `MetronSourceThrottleTests` (3). Not seen on screen.
  `ArcReadingListBuilderTests.RefreshAsync_PreExistingDuplicateItemsForSameIssue_SelfHeals_NoCrash` fails, from the in-flight uncommitted CBL
  refresh/healing work (`ArcReadingListBuilder.cs`, `ArcReadingListRefreshPlanner.cs`), not from this change.

## 2026-10-26 - Chapter/volume alignment, "You vs Community", Metadata Model Phase 7 (built, uncommitted)

Design: `docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md`; plan: `...-plan.md`. Three roadmap items that had been
deferred, built together because alignment is the use case that finally justified Phase 7.

- **Alignment (onboarding.md section 9).** `MangaDexAggregateClient` (`/manga/{id}/aggregate`, shape verified live 2026-10-26: `volumes -> {vol|"none"} -> chapters`,
  plus `isUnavailable`), `ChapterAlignmentResolver` (missing / extra / volume conflicts; a library of volume files is compared against volumes),
  `ChapterAlignmentScanner`. The aggregate is cached 24 h in an `ExternalMetadataSnapshot` (`SchemaVersion "aggregate-1"`), no migration. Library Health gets a
  "Chapter alignment" section (reads the cache on open, "Check now" fetches, "Not a problem" dismissals); manga Detail gets a "Missing chapters" card with a
  have/total badge. Report-only: nothing edits an issue. MangaDex volume numbers come from uploaders, so a volume disagreement is only noted and never lists a series by itself.
- **You vs Community.** `CommunityComparisonResolver` (your 0-5 stars x20 vs the mean of provider `ExternalRating`s and Metron's issue ratings weighted by vote count),
  Insights Trends > Top Lists card: ScottPlot scatter with the y = x line, biggest disagreements each way, coverage line. "Fetch community ratings" runs
  `CommunityRatingBackfillService` (rated, linked manga series with no stored score from that provider; Activity Center job, cancellable, stops cleanly on a rate limit).
  `ExternalRating.VoteCount` is now filled (MangaDex: sum of its rating distribution; AniList and MangaBaka expose none - verified live).
- **Deviations worth knowing.** (1) `ScrapeField.CommunityRating` now defaults **on** (CE leaves it off); saved profiles keep their choice. (2) Comics are not backfilled by id:
  an issue doesn't remember its Metron issue id, so a comic's community rating arrives through the normal scrape. (3) ComicVine reports no community score at all, so there is no
  ComicVine adapter. (4) The Stats v2 spec and the roadmap said no adapter wrote `ExternalRating`; the manga adapters already did (`MangaProviderApply`), which is why the card needed no new adapter work for manga.
- **Phase 7, shipped minimal.** One entity, `CollectionContent` (an issue's chapter range, `Manual` or `MangaDexVolume`), migration `AddCollectionContent`, `CollectionContentService`,
  and "Map volumes to chapters" / "Clear mapping" on the Missing chapters card. A mapped volume file owns its chapters, so a volume library stops reading as every chapter missing.
  The source doc's `Volume`/`Chapter`/`Story`/`Collection` set was **not** built: `PAPERBUNKR_METADATA_MODEL.md` is not in the repo, and nothing here needed more. No manual range editor yet.
- **Verified:** Data tests (alignment 16, comparison 9, backfill 7, content 8, migration 2, plus the touched manga/scrape classes) and App tests (alignment VM 6, missing chapters 9,
  Insights card 6, plus the Insights, Library Health, manga Detail and lint classes) pass. The one failing lint test, `FamilyButtonsCarryNoSizeOverrides`, is in the ComicVine review dialogs
  another change left modified, not in these files. **Not verified:** on screen (Library Health section, Missing chapters card, the scatter and its tooltip, the fetch button), and nothing was run against a real library.

### Books improvement arc, Slice 1: data foundation (2026-10-27)

Spec: `docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md`; plan: `...-plan.md`. Pitch items 1, 2, 11, 12, 13, 21 (status, rating, tags, richer metadata, scraper, provenance), plus write-back.

- **Data.** Migration `AddBookMetadataFoundation`: `Book` gains Subtitle, Publisher, LanguageISO, ISBN, SeriesIndex, Rating (0-5, half steps), CommunityRating(+Count), `BookReadingStatus` (Unread, WantToRead, Reading, Finished, Abandoned), SkipMetadataMatch; new `BookTag` and `BookFieldSource`. `Book.Finished` is `[NotMapped]` over `Status`; the legacy column is deliberately kept (no DropColumn). The enum is `BookReadingStatus` because `ReadingStatus` already means a comic series' reading state.
- **Files.** EPUB (calibre + EPUB3 collection), FB2, MOBI EXTH and PDF metadata are read at import and by a weekly, on-by-default scheduled task "Refresh book metadata from files" that only fills empty fields. `BookMetadataApplier` is the single place the "never overwrite a user edit" rule lives (Manual provenance wins).
- **UI.** Book Properties and bulk overlays gained status, rating (new half-star `StarRating` control), publisher, language, ISBN, number in series, genres/tags, a "Suggested series" chip and provenance chips; Rate / Set status / Match metadata in the grid menu; "Your shelf" card on Detail; Match metadata dialog (Open Library + Google Books, per-field diff, skip-forever) and a bulk Match action as an Activity Center job.
- **Write-back.** `BookFileWriteBackService` patches only OPF metadata nodes in EPUBs, validated sibling copy then `File.Replace` under `FileWriteBackCoordinator`, gated on the existing write-back settings.
- **Verified (tests).** Data: migration 4, applier 13, suggester 18, providers 11. App: file metadata 53 (with the existing format classes), scan 13, refresh 4, write-back 8, StarRating 4+, editors 17, match service 8, match dialog 5, `MainViewModelTests` 83, Books/Detail/Reader classes. Lint: all but `FamilyButtonsCarryNoSizeOverrides`, which fails only on the ComicVine review dialogs another change left modified.
- **Not verified / not done.** Nothing was run on screen or against the real library database; the new migration has not been applied to it. `AddBooksBrowseStateMigrationTests` fails migrating far back (`LibraryGroupField`); it looks like an older Down-chain problem, not checked against a clean checkout. Deferred from the plan: Connections tile for the two sources and a Google Books key field (Google works keyless), Re-fetch from a provenance chip (the Match dialog covers per-field choice), a Books-menu "Refresh now" button (the scheduled task has Run now). Slices 2 (shelves/Detail hero), 3 (reader), 4 (Calibre/OPDS/dictionary/TTS) are not started.

### Books improvement arc, Slice 2a: browse and shelves (2026-10-27)

Spec: `docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md`; plan: `...-plan.md`. Builds on Slice 1's data.

- **Browse.** Status tabs with counts (All, Reading, Want to read, Unread, Finished, Abandoned), persisted; new sort axes (Rating, Progress, Publisher, Published, Series order, missing values last) and group axes (Status, Publisher, Language, Genre); the filter/sort/group logic moved out of `BooksScreenViewModel` into the pure `BooksBrowseQuery`; one-badge cards (progress ring while Reading, check when Finished, stars on hover). Migration `AddBooksBrowseViews` (two settings columns).
- **Shelves ▾.** Built-ins (Favorites = rating 4 or more, Unfinished, Not rated), the user's novel smart lists (novel fields added: rating, status, tag, publisher, language, series number, progress %), and collections that hold books. Session-only choice, composes with tab and search.
- **Bookshelf view.** Grid/Bookshelf switch reusing the Library's `SpineTile`, `ShelfBoards` and `VirtualizingVariableWrapPanel`; spine width from book length; cover colour through a book-specific key (`book:{id}`) so it never reads a comic cover with the same number.
- **Series gaps.** "#3 missing" ghost cards only for holes between owned numbers, only when the series is fully numbered, and only in an unfiltered grouped-by-Series view and in Series mode of Book Detail (which now lists by series order). Ghosts are inert.
- **A-Z rail** on the covers view (flat Title/Author sort, or any grouping except Status).
- **Library Health › Review › Duplicate books** (ISBN, or title + author): Reveal and a two-step Delete to the Recycle Bin. Report-only.
- **Home.** Two new, hideable, reorderable sections: "Next in your book series" and "New books"; each leaves Home while empty. The mixed Continue Reading row is unchanged.
- **Verified (tests).** App: 340 across the Books, shelf, duplicate, Detail, metadata, reader, Home and lint classes; Data: new classes for the migration, gaps, duplicate finder, novel smart-list fields and Home book rows. Fixed `ReworkBookPositionAnchorMigrationTests`, which loaded whole `Book` rows while pinned to an older schema and so broke on Slice 1's new columns.
- **Not verified / not done.** Nothing on screen. **Pinned group header** not built (only the rail). The rail is not offered in the Bookshelf view (virtualized rows). Three older migration tests (`AddFb2MobiBookFormat`, `AddBooksBrowseState`, `AddBookReaderErgonomicsAndAnnotations`) fail migrating far back with `no such column "LibraryGroupField"`, an older Down-chain problem that does not involve these migrations (not checked against a clean checkout). `FamilyButtonsCarryNoSizeOverrides` still fails only on the ComicVine review dialogs another change left modified. The new migrations have not been applied to the real database.

### Books improvement arc, Slice 2b: Book Detail (2026-10-27)

Spec: `docs/superpowers/specs/2026-10-27-books-slice2b-detail-design.md`; plan: `...-plan.md`.

- **Tabs.** Book mode now has Overview / Chapters / Notes (Chapters hidden for a PDF; Notes shown once there is anything to list, always for a PDF). Opening a different book returns to Overview; reloading the same one keeps the tab.
- **Read button.** "Start reading" / "Continue — Ch. 4 · 38%" / "Read again" (percent from `BookProgress`).
- **Reading dashboard** (Overview): sessions, time read, last/first read, pace, and "About 3h 20m" time left from the new `BookReadingStatsResolver`. Only Opened events count; a session needs a minute of active time and some pages to count for pace; the estimate needs two such sessions and a known length, rounds to 5 minutes, and is hidden when finished.
- **Chapters.** Each row is Read / Current / Unread with a length bar (from the one parse Detail already does) and the current chapter's percent.
- **Notes.** Highlights, bookmarks and PDF captured regions in book order, with kind and colour filters, open-at-spot, delete and the existing export. The old Bookmarks card is folded into it.
- **Author.** A "More by {author}" button opens the Books grid on the All tab with an "Author: X" chip, plus "N books · M finished" and a thin progress bar (over all of the author's books, not narrowed by the tab).
- **Verified (tests).** Data: stats resolver (5). App: `BookDetailTabsTests` (14), the existing Detail class (its Read-label assertion was updated deliberately), the author strip, `MainViewModelTests`, lint (all but `FamilyButtonsCarryNoSizeOverrides`, which fails only on the ComicVine review dialogs another change left modified).
- **Not verified / not done.** Nothing on screen. Opening a PDF region cannot jump to its page: the PDF reader ignores a start position, so it opens at the saved page. The author link is a button in the "Your shelf" card, not the author text in the hero (that control is shared with comics). The hero Read button was not given the percent separately from the label. Notes are read-and-navigate only; writing notes on highlights is Slice 3. The unused `OpenBookmark`/`DeleteBookmark` commands remain on the view model.

### Books improvement arc, Slice 3: the reader (3a, 3b, 3c) (2026-10-27)

Specs: `docs/superpowers/specs/2026-10-27-books-slice3{a,b,c}-*-design.md`; plan: `2026-10-27-books-slice3-reader-plan.md`. One deviation from the specs, forced by the WebView: it composites above every Avalonia element, so the jump-back chip and the auto-scroll pill are **HTML inside the reading page** (driven from code-behind), not `floatingPanel` controls.

- **3a.** After a TOC, search, bookmark or highlight jump that changes chapter, or moves 10+ blocks (or more than 15 % without block ids) inside one, a "Back to Ch. N" chip shows for 6 s; clicking it or Alt+Left (`BookReader.JumpBack`, declines when no chip) returns without recording another jump. A position report inside 1.5 s of the jump does not dismiss it. Leaving a book after 5+ minutes of active reading shows a "Reading session" toast (minutes, pages, first live goal's progress).
- **3b.** Justify and hyphenation (global default + per-book override, `lang` taken from the book), "use the book's own styling" and an installed-font box (global; picking a font switches the family to Custom), in a new TEXT card; `BookReaderCss` now builds the injected CSS, pinned to the old output. A line ruler (two-line clear band following the pointer, `R`, Alt+Up/Down) and auto-scroll (`A`, `.`/`,` for speed, in-page pill, stops at chapter end or on any unbound input) with remembered ruler state and speed; auto-scroll starts off each time and its own scrolling does not count as reading activity. Migration `AddBookReaderTypographyAndAids` (6 AppSettings, 2 Book columns).
- **3c.** Highlights with a note carry a `pb-has-note` dot at their end (absolutely positioned, so no reflow); the highlights drawer already listed notes, now capped at two lines.
- **Verified (tests).** New: jump tracker/summary, `BookReaderJumpBackTests` (chip per jump, settle window, return, toast), `BookReaderCssTests` (old output pinned), `BookReadingAidMathTests`, `BookReaderAidsViewModelTests` (settings chain, persistence, speed bounds, activity), `BookHighlightPayloadTests`; the Book reader key-set pin in `InputWiringTests` updated for the 7 new actions; the lint trio passes except `FamilyButtonsCarryNoSizeOverrides` (ComicVine dialogs from another change).
- **Not verified / not done.** **Nothing on screen**: none of the JS (chip, ruler, auto-scroll, note dot, hyphenation, custom font) has run in the WebView, and the migration has not been applied to the real database. "Book's own styling" only covers inline styles, classes and `<style>` blocks inside a chapter; the chapter HTML is the raw document, so a linked stylesheet file is not loaded. Auto-scroll assumes the vertical-scroll mode the reader ships in (`UseColumnPaging = false`). No gamepad bindings for the ruler or auto-scroll. Not built from the original pitch: margin-notes rail, tags on highlights, bionic reading, typography profiles (dropped in the Slice 3 grilling round).

### Books improvement arc, Slice 4: Calibre, dictionary, OPDS, read aloud (2026-10-27)

Specs: `docs/superpowers/specs/2026-10-27-books-slice4{a,b,c,d}-*-design.md`; plan: `2026-10-27-books-slice4-plan.md`. Three migrations (`AddBookCalibreLink`, `AddBookDictionarySettings`, `AddOpdsCatalogs`, `AddBookReaderSpeech` — four in all), none applied to the real database.

- **Found and fixed on the way: chapter HTML never had block ids.** `BlockIdInjector` had no production caller since it was written, so `pb-p<n>` ids did not exist in the reading page; highlights, bookmarks, the saved position, Slice 3's jump chip and read aloud all anchor to them. `BookReaderScreenViewModel` now injects them when it loads a chapter (deterministic). This changes behaviour for existing books the first time they are opened after this build: worth a look on screen (resume, highlights, bookmarks).
- **4a Calibre.** `Paperbunkr.Data/Calibre` (read-only reader with a copy fallback, mapper, importer) + an "Add books > Import from Calibre…" dialog. One Paperbunkr book per Calibre book (EPUB, FB2, MOBI, PDF order), files referenced in place; match by Calibre id, then file path, then ISBN / title+author (those two go to a review list with Link / Skip); Manual fields are never overwritten, even with "refresh"; optional read-status column; covers set for new books only. Tested against a reconstructed `metadata.db` and, read-only, against the real library (40 books imported into a scratch database, second run adds none).
- **4b Dictionary.** Look up button + definition card in the highlight popup (`L`), offline WordNet file built on first use from Princeton's `WNdb-3.0.tar.gz` (downloaded by the app, about 12 MB), morphy-style suffix rules, optional dictionaryapi.dev fallback behind a sheet switch (off by default).
- **4c OPDS.** `Add books > Browse catalogs…`: add catalogs (DPAPI-protected passwords, sent only to the catalog's host), browse navigation and acquisition feeds, search, load more, download the best of EPUB/FB2/MOBI/PDF into a folder asked for once; the file is imported at once and the catalog's series, summary, tags and language are applied as source "OPDS".
- **4d Read aloud.** `System.Speech` voices, paragraph by paragraph from the top of the page to the chapter end, in-page pill and highlight; `T` start/stop, `Shift+T` pause, `Ctrl+Up/Down` speed. Voice and speed remembered.
- **Verified (tests).** Data: `CalibreImportTests` (17, including the real-library smoke test). App: Calibre dialog, dictionary (parser, lookup, database, download pipeline on a synthetic tar.gz, online parsing, popup state), OPDS (parser, client auth/redirect/download, store, download service), read aloud (plan, fake-engine view model), key-set pin; the lint trio passes except `FamilyButtonsCarryNoSizeOverrides` (the ComicVine review dialogs from another change).
- **Not verified / not done.** **Nothing on screen**, no WebView script run, and the Windows speech engine itself, the real WordNet download, and a real OPDS server were not exercised. Deviations from the specs: the catalog browser is an overlay (not a `ScreenInput` screen), add/edit is an inline form, no Activity Center job for Calibre import, the downloaded file is imported directly (the Downloads folder is not added as a Books folder), read-aloud speed keys are `Ctrl+Up/Down` (the web key map cannot express Shift+period), the dictionary online switch lives in the font sheet, per-sentence highlighting is not built. The highlight popup may still sit behind the WebView (old airspace note), which would hide the Look up card.

### Books reader: on-screen fixes, contents, paged/scroll modes and book pages (2026-10-27)

Found by the user on screen after Slice 4, then designed in a grilling round. Spec `docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md`, plan `…-plan.md`. One new migration, `AddBookReaderLayout`, not applied to the real database.

- **Fixed (user-verified on screen).** The highlight popup was drawn under the reading page (a Popup is embedded in the window, so the NativeWebView covers it): it is now an owned `OverlayHostWindow`, closes when another window is activated or on Esc, and `L` works from it. The reader's drawers (Chapters, Bookmarks, Highlights, Search) were empty since the 2026-09-16 move into owned windows: the window never got the reader's DataContext; `OverlayWindowController` now wraps the content in a `UserControl` carrying it. The contents list counted EPUB *files* (Glass Sword: 6 rows for 26 chapters): `EpubBookSource` now hands each file its nav entries (`BookChapter.Sections`) and `BookTocBuilder` flattens them for the drawer and the Detail Chapters tab.
- **Reading units.** The reader shows one contents entry at a time (`BookUnitSlicer`): a file is cut at its entries' anchors after block ids are assigned over the whole file, so stored positions, bookmarks and highlights keep their meaning. Next/previous move by unit; going back lands on the previous unit's last page.
- **Paged and scroll layouts.** Font sheet LAYOUT card (`AppSettings.BookReaderLayout`, default Paged). Paged mode does not use CSS columns: the page script measures line boxes and builds whole-line page edges, shows a page with a translate plus a clip, keeps a top margin on every page, and rebuilds (keeping the place) after load, font and size changes. Wheel turns one page per gesture; in scroll mode a wheel past either end opens the neighbouring unit after a 400 ms rest. Auto-scroll is scroll-only; the ruler and read aloud turn pages. Page turn slide uses the Replace motion role (none under Reduced).
- **Book pages.** Numbered by word count (`BookPageMap`, Preferences > Reader > Books, default 250–300) so "p. 214 of 640" is the same at any font size; every unit starts a page; long paragraphs split at sentence ends. A print page list (EPUB 3 nav page-list or NCX pageList, read from the archive) replaces the numbers when present — six of the user's 35 EPUBs (the Herbert Dune set) have one. Footer: "p. 214 of 640 · Nine · 3 / 12" paged, "… · 37%" scroll. Go to page field at the top of the Chapters drawer. Detail's "Ch. n of m" counts units.
- **Verified.** Tests: `BookUnitSlicerTests`, `BookPageMapTests`, `EpubPrintPageTests` (nav and NCX), `BookTocBuilderTests`, `BookReaderLayoutTests` (units, last-page landing, finish, contents row, layout persistence, auto-scroll refusal, footer both modes, Go to page, print pages), migration test; reader/Detail/Preferences/lint suites pass except `FamilyButtonsCarryNoSizeOverrides` (ComicVine dialogs from another change). The paging script was run in a real browser against a test page at 1000×640: every line on exactly one page at 17 px and 24 px (101 and 140 lines), no page taller than the window, place kept across a font change, one page per wheel gesture. Also fixed two stale tests: the Detail focus test (chapter rows moved to the Chapters tab in Slice 2b) and the Library Health section counts (17 / 13 Review), and a dictionary test that read the machine's real dictionary.
- **User-verified on screen (2026-10-27):** the user ran the build (so `AddBookReaderLayout` is applied to their database) and confirmed paged/scroll, chapter units and the footer look good.
- **Not done.** A unit cut from a file loses the class styling of a wrapper it started inside. A paragraph split across book pages counts from the page where it starts. No two-page spread; no book pages on Detail/Home; `Book.ChapterCount`/`LastChapterIndex` and Home progress still count files. The "redesign" these modes are for (answered 2026-10-27): a full Books section redesign, Books screen through Detail to the reader, to match the other sections and the design language; to be started in a new session.

### Books section redesign, Slice 1: Books screen (2026-10-27)

Spec `docs/superpowers/specs/2026-10-27-books-section-redesign-design.md` ("Reading Room with shelves", four grilling rounds with the visual companion), plan `…-plan.md` (Steps 1–11). One new migration, `AddBookLastExcerpt`, **not applied to the real database**.

- **Built.** `PbBookTitleFontFamily` + `bookTitle` / `bookQuote` text classes (fixed Source Serif, ignores the body-font override). `Book.LastExcerpt`, written by the reader with the position (`QuoteExcerpt`, ~160 chars at a word boundary; the page script now reports 200). `BooksSpreadResolver` (spread book: newest Reading → next in a started series → oldest Want to read → newest added; nightstand; quote fallbacks excerpt → newest highlight → synopsis; "This week" from novel `ReadingEvents`). Shared `Controls/Books/BookCoverCard` (one-badge rule: progress bar while reading, else one status corner; hover stars; select box; serif title + author + status caption; ghost variant; `book-cover:{id}` shared-element key) and `Controls/Books/CoverWash` (radial spine-colour wash, lighter on light themes, `PbCoverWashAccentMix` token). `SpineTile` draws ghosts as dashed outlines (fixes ghosts drawn as real spines). Books screen rebuilt: header (count, search, Add books ▾ with Scan / Manage folders / Calibre / Catalogs), tabs row with ghost Shelves / Group / Sort, Workspace icon, view switch; selection bar replaces it (Set status ▾, Rate ▾, Match, Edit, Delete on `danger`, Clear); spread + nightstand on All only; virtualized cover grid (was a plain WrapPanel); serif group headers with "3 of 4 · 1 missing" in both views; pinned group header (Library's pattern); A–Z rail in both views via `ScrollIntoView`; narrow window sheds toolbar labels (<1100) and the nightstand (<900).
- **Deviations from the spec.** The tabs row sits *above* the spread, outside the scroll area, instead of below it with a pinned duplicate (a duplicate would duplicate four bound popups). `CoverWash` cannot tell Matrix apart, so instead of "accent on Matrix" there is `PbCoverWashAccentMix` (0 by default) — check Matrix on screen. A–Z rail letters are no longer focusable (keyboard has type-ahead; the rail was the arrow keys' nearest neighbour).
- **Verified.** New tests: `BookTitleTypographyTests`, `QuoteExcerptTests`, `BookLastExcerptTests`, `AddBookLastExcerptMigrationTests` (Data), `BooksSpreadResolverTests`, `BooksSpreadViewModelTests`, `BookCardStatusTests`, `CoverWashTests`; Books screen VM/focus/tabs/workspace/bookshelf, FocusGaps/FocusStatesPaint (Books), ScreenInput and lint suites pass except `FamilyButtonsCarryNoSizeOverrides` (ComicVine dialogs from another change). `BooksScreenRenderTests` rendered default/Daylight/Matrix at 1310 and 1000 px, grid and bookshelf, checked by eye.
- **Not done.** On-screen check by the user. Detail (Slice 2) and the reader (Slice 3).

### Books section redesign, Slice 2: Book Detail (2026-10-28)

Same spec and plan (Steps 12–17).

- **Built.** `Controls/Books/ChapterRibbon` (segments weighted by chapter length with a floor, `RibbonMath`; read/current/unread fill; dashed ghosts; each segment a focusable button that opens that chapter, or that book in Series mode) on a `RibbonPanel`. Book mode is a jacket in a `CoverWash`: flat cover with a spine edge (`book-cover:{id}` shared-element key; the Books grid → Detail navigation now flies the cover), overline (format · year), 42 px serif title, author and series links, rating + status, flap copy with Show more, Continue / Edit / ⋯ (Match metadata, Reveal, Mark finished, Delete with an inline confirm; rows defer one tick before closing the popup), chapter ribbon with the series as spines (current book outlined, ghosts dashed). A compact title bar with Continue pins once the jacket scrolls away. Tabs: Overview (About this edition facts, genres and tags, More by with three covers, latest highlight), Contents (part-grouped rows), Notes (only Export), Your reading (stat tiles; new `BookDetailTab.YourReading`, offered only when the reading log has something). Series mode: fanned stack (`FannedStackPanel`), per-book ribbon, Continue at the book in progress, shared `BookCoverCard`s with ghosts. Home's "Next in your book series" / "New books" rows open Detail (`HomeScreenViewModel` optional `goBookDetail`). Hex `#E06060` gone.
- **Verified.** `BookDetailJacketTests`, `RibbonMathTests`, `BookDetailRenderTests` (default/Daylight/Matrix, book and series, checked by eye), BookDetail* focus/tabs/VM suites (the stats test now expects the dashboard on Your reading).
- **Not done.** On-screen check.

### Books section redesign, Slice 3: the readers (2026-10-28)

Same spec and plan (Steps 18–24).

- **Built.** The EPUB/FB2/MOBI page fills the window again (the 56 px strips are gone). Page CSS (`BookReaderCss` `Paper`): the page is a 720 px sheet on a darker desk with a shadow, page-edge stacks either side sized 2–24 px by the share of the book before and after (`PageEdgeMath`, paged mode), a faint folio, and margin notes beside noted highlights when the window is ≥1200 px (click opens the highlight); OLED Black and High Contrast keep the full-window page. In-page pills get the app accent and UI font as CSS variables. Highlight colours are tokens (`PbHighlight*`), used by the page CSS, popup swatches and Notes lists. Chrome is three floating clusters (`Views/Reader/ReaderCluster`: navigate top-left, actions top-right, page bottom-centre with part ticks) in the book theme's colours; in the EPUB reader each lives in its own small owned window (`ClusterWindowController`, anchored by `ClusterAnchorMath`, never activated: `WS_EX_NOACTIVATE`), shown with the existing auto-hide (now 3 s; the page script forwards pointer moves, edge ones reveal). Panels are floating cards whose tokens are re-tinted from the book theme (`BookThemePanelResources`), scrim token `PbReaderScrimBrush`; Bookmarks and Highlights merged into one Notes panel with All/Highlights/Bookmarks chips and Export. Aa sheet: active theme ring, `.on` layout switch, "Keep toolbar visible" (inverted `BookReaderAutoHideChrome`). Highlight popup on tokens and the ghost family. Reader button hovers use theme tokens (no more `#22FFFFFF`). PDF reader: the same clusters in-window, book-theme Captures panel, page edges around the page (`PageEdges`). Paper layout checked in a browser engine.
- **Deviations.** No live scrubbing on the page cluster's progress line (Go to page in Contents jumps); no time-left on the page cluster; no ⋯ menu in the actions cluster (read aloud, auto-scroll and ruler stay in Aa; Export lives in Notes). Clusters appear instantly (no reveal fade). `ReaderChrome` is no longer used by either reader but is kept because `ReaderChromeTests` still covers it. Hex-colour baseline: `AppTokens.axaml` 51 → 56 (five new tokens), the Books/Detail/reader views 17 → 0.
- **Not done / risks.** The owned cluster windows were not spiked on screen: check they follow window move/resize/minimize, that clicks beside them reach the page, and that keys still work after clicking one. On-screen check of everything.

### Books section redesign: on-screen fixes (2026-10-28)

Fixes from the user's first on-screen pass of the reader and Detail.

- **Reader clusters did not show.** Each cluster window is now measured and placed before `Show` (it opened at the screen's default spot), the chrome shows when a book opens, edge pointer moves and keys reveal it whether or not auto-hide is on, a click on the desk around the page toggles it, and the clusters re-sync on page load and viewport changes. `SyncClusters` and `ClusterWindowController` log to `startup.log`.
- **Cluster buttons did nothing.** Handing activation back to the reader on `Activated` moved focus off the pressed button between press and release, and an Avalonia `Button` that loses focus drops its press. The windows are now `WS_EX_NOACTIVATE` and never take activation. The auto-hide countdown pauses while the pointer is over a cluster (`SetPointerOverChrome`; the page sees no pointer moves there).
- **Aa theme swatches did nothing.** Their template found the sheet with `$parent[views:ReaderSettingsSheet]`, which no longer exists once the sheet's content is moved into its own window. The swatch list carries the sheet in `Tag`; the swatches bind through `RelativeSource AncestorType=ItemsControl` (compiled bindings off on that button: a cast inside a `$parent` path does not parse, and a `ReflectionBinding` element cannot resolve `$parent[ItemsControl]` at runtime). Gotcha for any templated row inside `ReaderListDrawer`/`ReaderSettingsSheet` content.
- **Description showed raw Markdown** (`***`, `###`, `[1]: https://…`). `Services/Books/SynopsisText.Clean` turns Markdown and HTML descriptions into plain text; used by Book Detail's flap copy and the Books spread's quote fallback.
- **Show more / Show less arrow** overlapped the label: now a fixed-size down chevron that flips up when open.
- **Verified.** User checked all of the above on screen (2026-10-28). `ReaderSettingsSheetThemeTests` (opens the sheet in its own window, checks the ring and that a swatch runs `SetThemeCommand`), `SynopsisTextTests`; reader, Books, Detail and lint suites pass except the known `FamilyButtonsCarryNoSizeOverrides` (ComicVine dialogs).

### Continuity overview: "An item with the same key has already been added" (2026-10-28)

- **Bug.** Earth 616's Overview failed with `Key: 89541`. `ContinuityOverviewLoader` keyed member series by `GcdSeriesId` with `ToDictionary`, assuming one library series per GCD series. The real database has three GCD ids shared by two series: 89541 (Free Comic Book Day 2015 (Secret Wars) and Secret Wars 2099, both `GcdMatchSource = Metron`), 35540 (Dark Reign: Hawkeye, two rows) and 71608 (Green Lantern Corps and its Annual).
- **Fix.** The loader groups series by GCD id (as `GcdBondSync` already did) and attaches a GCD placeholder to every series sharing the id; `ContinuityOverviewBuilder.BuildRuns` already handled that. Test: `ContinuityOverviewLoaderTests` (failed with the same exception before the fix).
- **Not fixed (data).** The FCBD 2015 issue being matched to GCD 89541 looks like a wrong match from Metron's `gcd_id`, and Dark Reign: Hawkeye exists as two series rows. Neither was changed.
- **Verified.** `ContinuityOverviewLoaderTests`, `OverviewBuilderTests`, `ContinuityPageViewModelTests`, `ContinuitySourcesAppTests` (36) pass; app built into `bin`. Not checked on screen.

### Series relation connector: automatic Related series (2026-10-29) (implemented, tests pass, not seen on screen)

Spec `docs/superpowers/specs/2026-10-29-series-relation-connector-design.md`, plan `...-plan.md` (agreed in a grilling pass, Q1–Q18). CE has no
series relations (it only groups by AlternateSeries/SeriesGroup), so this is Paperbunkr's own feature on the existing `MediaRelation` graph.

- **What it links.** Saved automatically as `Inferred` evidence: later volumes (`Continuation`), companions (Annual/Special/Rebirth/Giant-Size/…
  → `SideStory`), slash team-up titles and 2–3-series events with no main series (`Crossover`), and event tie-ins (new `RelationType.TieIn` /
  `MainEventSeries`, each tie-in linked only to the event's main series via `EventSpineResolver`, the Event Map's spine rule moved into Data).
  Metron's associated series are fetched weekly and saved as `Metron` "associated" evidence; a series with no Metron id is resolved through
  Metron's `cv_id`/`gcd_id` filters (`gcd_id` confirmed in Metron's `comicsdb/filters/series.py`), and an association is matched back by Metron
  id, then ComicVine id, then GCD id. Suggestions (shared lead, same writer, spin-off, similar title) are computed live, max 8.
- **Where.** `Paperbunkr.Data/Metadata/SeriesConnector/` (snapshot, five rules, sweep, Metron fetch/mapping, suggestions), weekly task
  `find-related-series` (priority 25), `SeriesConnectorReactions` after `LibraryScanCompleted` and the new `LibraryEvents.ScrapeCompleted`.
  Related tab: reasons under cards ("Later volume" / "Earlier volume" by side), dimmed suggestion cards with Accept/✕, per-event rows (main series
  first, tie-ins with issue ranges, 3 then Show all) and an Other events rail, replacing the flat Same Event rail; "Find relations" button.
  Preferences → Library → "Find related series automatically". Removing any automatic relation now writes a `SeriesRelationDismissal`.
  Migration `AddSeriesRelationConnector` (5 columns; `Down` leaves the Series/AppSettings ones as orphans per the rollback-chain rule).
- **Real-library dry run** (on a copy, 2026-10-29): 350 relations (253 tie-ins, 85 crossovers/team-ups, 15 companions) in under a second; hubs are
  Convergence, King in Black, Flashpoint (not Batman). Zero later volumes: this library keeps volumes inside one series (69 series hold 2+ volumes).
  The dry run also caught two weak suggestion rules, fixed: one-shots counted as having a "lead" (now 3+ issues), and the spin-off rule called
  Superman Unlimited a spin-off of Justice League (now: the character's first own title, from where they first appeared). Suggestions cost ~2 s
  cold, 45–115 ms warm (library reads cached 2 min, cleared by each sweep).
- **Verified.** Data: 50 `SeriesConnector` tests, migration test, `RelationTypeCatalogTests`. App: `DetailTabsViewModelTests` (incl. new event-row,
  suggestion and dismissal tests), `SeriesConnectorReactionsTests`, the toggle test, EventMap/Continuity/CollectionProperties classes, lint
  (only the known `FamilyButtonsCarryNoSizeOverrides` failure in the ComicVine dialogs). Known unrelated failure: `AddReleaseListProviderMigrationTests`
  breaks in the rollback chain from other uncommitted migrations (fails the same with this migration's `Down` emptied).
- **Slowness fixes after the first real run (2026-10-29 evening).** The weekly task held the heavy-job lane while it waited ~1 h on Metron's rate
  limit (510 series asked in the first run), so scrapes and scans queued behind it: now `UsesHeavyJobLane: false`, like Metron sync. The Related
  tab's event rows decoded every card's cover on the UI thread (dozens for Batman or a Convergence tie-in; two compositor timeouts in the log):
  now built on the thread pool like More Like This, one decode per series. `DetailTabsViewModelTests` + reactions + catalog (140) pass; built into `bin`.
- **Not done.** Not seen on screen. The first real Metron run stopped when the app was closed (510 series asked, 48 associations saved);
  the next run picks up from there, since checked series are stamped. Metron associated series not in the library aren't shown.
