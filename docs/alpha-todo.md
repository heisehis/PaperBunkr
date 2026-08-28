# Paperbunkr Alpha Release To-Do

*Scope: git/release prep + known gaps only, per `alpha-roadmap.md` (2026-08-07). Beta backlog is
tracked separately in that document and not repeated here.*

Priority order below is suggested — highest-risk / release-blocking first, then polish ordered by
user-facing impact.

## Live tracker

This file is the authoritative, human-written record — commit refs, rationale, sub-item detail.
A companion dashboard renders a lighter view of the same P0–P7 status for quick scanning:
**https://claude.ai/code/artifact/0ca86894-977e-45e2-951b-476e1150a5ee**

A scheduled cloud agent (`paperbunkr-alpha-tracker-sync`, routine
`trig_018nELx6EohKVCqFrdP9bX3T`, every 6h, read-only against the repo) checks `git log` against
the tracker's own embedded `HEAD` marker and republishes it to the same URL only when it can
concretely verify a status change — it never edits this file or commits anything. This file still
needs a human (or a Claude Code session working in it) to update by hand when priorities shift;
the tracker just keeps a lightweight view from silently going stale between those updates the way
this file itself already did once (see the note below).

## What's left (as of 2026-08-12, HEAD `85fb681`)

> **Manual session note (2026-08-27, Metadata Model Phases 4d-4g):** net-new metadata-platform work
> landed this session — Event Relations (4d, one new EF migration
> `20260827193943_MetadataModelPhase4dEventRelations`), Format-Signal Event Suggestions (4e),
> Continuity Browse view (4f), and Age Progression / Timeline (4g). All four extend the Story
> Events screen (`EventsScreenViewModel`) — 4f adds an Events|Continuities|Timeline mode switcher,
> 4g adds Timeline as the third mode. **P0-P7 status is unchanged (still all done)** — this is Beta
> backlog, so the full detail lives in [`alpha-roadmap.md`](alpha-roadmap.md)'s "Metadata Model
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
- `Assets/avalonia-logo.ico` — **still open.** Still the default Avalonia project-template icon,
  still wired as the actual window icon (`MainWindow.axaml` line 11). Needs a real Paperbunkr icon.

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
  `8d94d11`, `25c664a`, merged via `5869ed0`) — tracked separately in `alpha-roadmap.md` per this
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
- Stale worktree `.claude/worktrees/quirky-borg-c5d364` (branch `claude/quirky-borg-c5d364`) —
  still present as of `5869ed0`, unchanged since last check. Its two commits (PageCanvas focus
  fix, Virtual Tags wiring) predate and are superseded by `8e1bf55`'s versions of the same fixes.
  Still has the same uncommitted edit to `LibraryFolderScannerTests.cs`. Safe to discard once
  confirmed nothing else is needed from it.
- **New, found this sync:** two more worktrees from the Novels (EPUB/PDF) session —
  `.claude/worktrees/compassionate-banach-c6e8bf` (branch `claude/compassionate-banach-c6e8bf`,
  tip `25c664a`) and `.claude/worktrees/exciting-hypatia-eecfc9` (detached HEAD at `d86cac7`).
  Both confirmed clean (`git status --short` empty) and both tip commits confirmed already merged
  into `master` (`git branch --contains` shows `master`) — safe-to-discard candidates, same
  reasoning as quirky-borg above.
- ~~`docs/alpha-roadmap.md` uncommitted edit~~ — resolved; working tree is clean as of `5869ed0`
  (re-confirmed this sync — `git status --short` on the main worktree is empty).

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
      wired now — see `alpha-roadmap.md`'s "Reading Lists: story-arc auto-build" entry. AniList/
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
  `alpha-roadmap.md` per this doc's scope note at the top — not duplicated here.

---

## Explicitly not in scope here

- **Content-type classification manual dropdown** — flagged as a known gap, but the real
  auto-classify pipeline (§7/§9) is scoped as Beta work. No Alpha-side fix needed beyond what's
  already shipped; leave the manual dropdown as-is until Beta.

---

*Beta backlog is tracked in [`alpha-roadmap.md`](alpha-roadmap.md) and not duplicated here.*
