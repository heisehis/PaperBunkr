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
> - **Step 17 (cover-image cycling + alt-cover memory) NOT done, deliberately:** CE's real field is
>   `associated_images.original_url` on the issue detail response (`cvdb.py`, verified directly), but
>   a live fetch of ComicVine's own current public API docs today shows **no `associated_images`
>   field on the Issue resource at all** — it may have been removed/renamed since CE's plugin was
>   written. Building the parser/UI around an unconfirmed field would be exactly the kind of
>   assumption this project's own standing rule says not to make; needs a real ComicVine API key and
>   a live call (or a maintainer of ComicVine's docs) to confirm before this step can be built.
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
>   apply share it. Not ported: checking an issue's alternate covers (needs `associated_images`).
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

## Explicitly not in scope here

- **Content-type classification manual dropdown** — flagged as a known gap, but the real
  auto-classify pipeline (§7/§9) is scoped as Beta work. No Alpha-side fix needed beyond what's
  already shipped; leave the manual dropdown as-is until Beta.

---

*Beta backlog is tracked in [`Paperbunkr-Roadmap.md`](Paperbunkr-Roadmap.md) and not duplicated here.*
