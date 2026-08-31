# Paperbunkr Alpha Roadmap

*Declared: 2026-08-07. This document marks the Alpha checkpoint and lays out the Beta backlog.
Supersedes onboarding.md §16 as the up-to-date release-staging reference — §16 defined the
original minimum bar; this doc records what actually shipped (considerably more) and re-stages
everything after it.*

## Alpha is declared

The original Alpha bar (`docs/onboarding.md` §16) closed 2026-08-06 with Migration UX. Rather than
cut the release right there, work continued for one more day and pulled in a substantial slice of
what would otherwise have been early Beta scope. **This document declares that combined state —
everything below — as Alpha**, effective 2026-08-07.

## What's in Alpha

**Foundation**
- net8 retarget of the ported CE engine (`Paperbunkr.Common`, `Paperbunkr.Engine`)
- Avalonia app shell, rail-nav across 6 screens
- SQLite/EF Core data layer (`Series`/`Issue`/`Category`/`TrackingLink`/... schema)
- CE `ComicDb.xml` migration: detection, dry-run scan, series-conflict check, commit (full §14
  scope, not just the Alpha minimum)

**Reading**
- Reader canvas: real page decode/render, paged mode end-to-end (CBZ/CBR)
- RTL page-turn navigation + Reader preferences tab + Detail reading-mode toggle
- Cross-issue navigation (auto-advance to next/previous issue at page boundaries)

**Library & organization**
- Library grid view (real cover art) + list/detail row view
- Smart Lists — CE field-parity rule engine, in-memory query
- Reading Lists — CBL/CSV import/export
- Book Folders (scan-now import, filename-parsed metadata) + Virtual Tags (computed fields, not
  yet consumed by Smart Lists/display)

**Metadata editing**
- Single-book Issue Properties Editor
- Bulk multi-book editing (data-driven field registry, list-field diff/merge)
- Detail Screen selection-driven issue-focused display

**Settings & infrastructure**
- Preferences screen: Appearance (skin/theme system, `.crpck` install, `windows_11` reference
  skin), Behavior (real cross-issue auto-navigate + open-last-page settings), Libraries (Virtual
  Tags + Book Folders), Advanced (File Association, Backup Manager — manual backup/restore)
- Scripts tab explicitly skipped — no plugin engine exists yet, nothing real to back it

## Known gaps in shipped Alpha work

Carry these into Beta polish, not blocking the Alpha declaration itself:

- `PageCanvas` requires a click before arrow-key navigation registers in the Reader
- Virtual Tags compute correctly but aren't wired into Smart Lists or any display surface yet
- Content-type classification is a manual dropdown on Detail — no real §7/§9 auto-classify pipeline
- Series.Genre vs Issue.Genre display inconsistency — mostly fixed during Detail-focus work, worth
  a final pass

## Before tagging a release

The implementation for everything shipped 2026-08-07 (Preferences, RTL nav, Issue Properties
Editor, Bulk Editing, Detail Screen focus) is still **uncommitted** in the working tree. Needs to
land in git — likely as several commits split by feature, mirroring the already-committed design
specs — before an actual `alpha` git tag goes on this state.

A prioritized, checkbox-tracked version of this release prep plus the known gaps above (P0–P7,
sequenced by risk and user-facing impact) lives in [`alpha-todo.md`](alpha-todo.md).

## Beta backlog

Pulled from `docs/ce-feature-inventory.md`'s full CE parity audit (2026-08-07), organized by area.
Nothing here is sequenced yet — this is the full confirmed "decided: build" list, not a sprint plan.

### Preferences: Reader tab (last of 5 tabs)
**Was stale — a Reader tab already existed** (shipped 2026-08-07 alongside RTL navigation:
Right-to-Left/Display/Keyboard Shortcuts groups), this entry just hadn't been updated to say so.
**Shipped 2026-08-10** (design spec docs/superpowers/specs/2026-08-10-preferences-reader-tab-
design.md): checked CE's actual `Settings.cs` source directly, confirmed most of it does gate
reader capabilities that don't exist yet (magnifier, overlays, auto-scroll, continuous-scroll,
hardware-accel) — added only the three that back real, shipped capability: reset-zoom-on-page-
change, mouse wheel scroll speed (replaces a fixed `PageCanvas` constant), and default fit
mode/auto-rotate (closes a TODO this session's own earlier Reader Polish work left pending this
tab's existence). 400 tests pass across the whole solution.

### Reader polish (onboarding.md §8, the largest single backlog)
**First slice shipped 2026-08-10: fit modes, zoom presets, rotation** (design spec
docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-design.md). Original/Fit/Fit
Width/Fit Height/Best Fit (CE's `FitWidthAdaptive` and its anamorphic-tolerance non-uniform-XY
scaling deliberately not ported — named deviations, not gaps), zoom presets (100/125/150/200/400%)
layered on the existing gesture zoom, manual rotate + auto-rotate-landscape-pages. Fit mode and
auto-rotate persist per-Issue (mirrors the existing-but-previously-dormant
`Issue.ReadingModeOverride` shape, now with a real write path); zoom level and manual rotation stay
session-only, matching CE precedent (confirmed from `_reference/ComicRackCE` source — CE itself
never persists an exact zoom%/rotation angle per book either). 25 new `ZoomPanMathTests` (pure
fit-mode/rotation-composition math), 11 new `ReaderScreenViewModelTests`, 2 new
`IssueReaderOverridesTests` — 390 tests pass across the whole solution.

Real pre-existing test-isolation bug found and fixed along the way, unrelated to Reader polish
itself: `PreferencesScreenViewModelTests` constructed `MigrationOverlayViewModel`/
`NeedsReviewViewModel` with no injected DB context factory, so ~13 of its tests silently queried
the *real* production database instead of the test's isolated one — invisible until the new
`Issue` columns made the real (unmigrated) database's schema actually diverge from what the
compiled EF model expected. Fixed by setting `PaperbunkrDbContext.DatabasePathOverride` at the
test class's constructor/`Dispose` level, matching the pattern other test classes already use.

**Two more real bugs found via the user's actual manual testing after this shipped** (not caught by
build/tests — this exact class of interaction bug only surfaces on-screen):
1. `PageCanvas`'s drag/wheel/arrow-key pan was gated purely on `ZoomLevel > MinZoom` — correct
   before fit modes existed (the only base scale was always contain-within-bounds, so "zoomed in"
   and "content bigger than the canvas" were the same condition), but `Original`/`FitWidth`/
   `FitHeight`/`BestFit` can all overflow the canvas at the default zoom too, leaving the
   overflowing parts unreachable. Fixed with `ZoomPanMath.HasOverflow` (pure function) and a
   `PageCanvas.CanPan()` gate combining it with the original zoom check.
2. More fundamental: `PageCanvas` never set `ClipToBounds` (Avalonia default `false`), so oversized
   content painted straight through into the toolbar/thumbnail rail regardless of pan offset — fix
   #1 alone changed the pan offset but had nothing to actually reveal/hide by doing so. One line,
   `ClipToBoundsProperty.OverrideDefaultValue<PageCanvas>(true)`. **Pre-existing gap predating fit
   modes** — plain zoom-past-100% could already trigger it, just rarely hit before fit modes made
   default-zoom overflow trivial to reach. Also fixes the Novels PDF reader, which shares this
   control. 5 new `ZoomPanMathTests`. 406 tests pass across the whole solution; both fixes confirmed
   working via the user's own on-screen testing.

**Continuous/webtoon scroll, fullscreen, position tracking, live image adjustment, and
background/margins shipped 2026-08-11**; **page transition animations shipped 2026-08-15**; **double-
page spread shipped 2026-08-16**; **remappable reader keyboard shortcuts shipped 2026-08-16**;
**auto-scroll/hands-free mode shipped 2026-08-16** — all committed `2026-08-22` (`c1e91a6`) after
sitting tested-but-uncommitted for several sessions. This doc's "still open" list above had gone
stale; full per-item detail (design specs, bugs found+fixed, test counts) lives in
[`alpha-todo.md`](alpha-todo.md)'s "Bonus, ahead of schedule" section rather than duplicated here.
**A Preferences → Reader tab already existed and is not open** — see the entry above this one.

**Vertical paged reading mode shipped 2026-08-27** (`ReadingMode.TopToBottom`, design +
plan under `docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-*.md`) — a paged
top-to-bottom mode (one page fills the viewport like LTR, page-turns run along Y: Up/Down keys,
wheel, top/bottom click-and-tap zones, vertical flick), with vertical `Slide` animation
(`PageTransitionDirection` gained `Up`/`Down`). New `PageTurnGestureMath` pure helper.
Automated tests green; on-screen gesture/animation verification done (user-confirmed 2026-08-27).

**Still genuinely open:** magnifier overlay (explicitly skipped per user direction — "we have a zoom
slider"), on-screen overlays (scrubber, page/status text, clock/battery), split-page part navigation,
touch gestures beyond what's already shipped. **On-screen (not just automated-test) verification of
double-page spread rendering/pairing, remappable-shortcut keypress wiring, and auto-scroll behavior
is still pending** — same standing no-unattended-GUI-automation caveat that applied when those
shipped; `Paperbunkr.App.UiTests` (see Library browsing extras below) has since closed this gap for
other screens and could close it here too.

### Metadata editing extras
Copy/paste fields between books, templated/token text field editor, Quick Rating + free-text
Review popup (Review isn't a schema field yet), undo/redo for metadata edits, per-page type
tagging (cover/story/ad/deleted), per-page persisted rotation override, named bookmarks (distinct
from `LastPageRead`).

### Library browsing extras
**Decomposed 2026-08-16 into ~5 ordered sub-projects** (too large for one spec) —
**reveal-in-Explorer and manual fileless book entries shipped 2026-08-16** (design spec 2026-08-16):
CE's `FileExplorer` shell helper (already ported, previously zero call sites) wired into Detail
screen's issue tiles, bulk multi-select, and Library series cards (opens the first-issue's folder,
since a series has no single file); a new "+ Add" Library-toolbar entry creates a fileless
placeholder Issue (reusing `ReadingListMatcher.ResolveOrCreatePlaceholder`, extended with a
non-breaking `out bool wasCreated` overload) and hands off to the existing Issue Properties editor,
which now safely deletes the placeholder if the user cancels without editing anything — safe by
construction, since the delete-on-cancel flag is only ever set when the row was actually newly
created this session, never an existing match. 566 automated tests pass; app verified to launch and
stay running (checked via Windows Event Viewer for crash events, not just process-alive) — **on-
screen verification of the actual context-menu/toolbar interactions still pending**, same standing
GUI-automation caveat as every reader spec.
**Manga/ContentType classification shipped 2026-08-16** (design spec 2026-08-16, surfaced while
scoping pluggable sort/group — paused, still open below): CE's flat `Manga` field already had a
better home in `Series.ContentType` (`Comic`/`Manga`/`Manhua`/`Manhwa`, distinguishing Japanese/
Chinese/Korean origin — a real improvement over CE, matching this project's "more manga support
than ComicRack" direction) but was only ever set by one-time CE migration, with zero edit UI and no
scan-time detection despite the scanner already having CE's embedded `Manga` value available for
free. Added: Bulk Edit's new `FieldKind.Enum` (the first bulk field that writes through to a
selection's owning Series rather than the Issue itself, surfacing "N series will be affected"), a
per-card Library context-menu picker, the "+ Add" flow's picker, and scan-time auto-detection
reusing `CeLibraryMigrator.MapMangaField` exactly as CE migration itself does. A conditional Reading
Direction row appears alongside Content Type whenever it's Manga-family, defaulting to
right-to-left on first classification without ever clobbering an already-set value. Two real bugs
caught and fixed during implementation: `Series.ContentType`'s own property initializer is
`Unknown`, not the enum's raw default (`Comic`, first-declared) — an incorrect early assumption,
corrected in both code comments and the spec itself; and `ResolveOrCreatePlaceholder`'s
`wasCreated` reflects whether the *Issue* was newly created, not the *Series* — attaching a new
issue to an *existing*, already-classified series also reports `wasCreated=true`, which would have
silently overwritten real classifications via the "+ Add" flow had it been used as the write-gate
instead of an explicit series-existence check. 584 automated tests pass (build clean); app verified
to launch and stay running (Event Viewer checked, no crash events) — on-screen verification of the
actual picker interactions still pending, same standing caveat.
**Saved List Layouts shipped 2026-08-17** (spec: `docs/superpowers/specs/2026-08-17-library-saved-
list-layouts-design.md`), the third sub-project. Verified against CE source first: CE's own
`DisplayListConfig` (columns/sort/group/captions/search/filters, auto-persisted, no naming) and
`DisplayWorkspace` (named, multiple, switchable bundles including window/page-layout state) are two
distinct concepts — this sub-project is the `DisplayListConfig` equivalent only; named/multiple
presets are the separate, still-unbuilt "saved Workspaces" item below. `LibraryScreenViewModel`'s
entire session-only sort/group/display/filter state (sort field+direction, group field, view mode,
grid density, 5 overlay badge toggles, search query, sidebar content-type/category selection, 3
filter checkboxes) now round-trips through `AppSettings` — no new UI, immediate write on every
change (no debounce, matching this ViewModel's existing no-debounce search philosophy), matching
CE's own choice to persist search/filter state alongside sort/group/display rather than treat them
as session-only. A stale `LibraryActiveCategoryId` (its category deleted since last session) falls
back to "All Series" rather than silently rendering an empty grid. Required moving 4 enums
(`SortDirection`, `LibrarySortField`, `LibraryGroupField`, `LibraryViewMode`) from `App.Models` into
`Data.Entities` so `AppSettings` could reference them directly, following the precedent the
`PageLayoutMode`/`PageTransitionStyle` work already set rather than inventing parallel mapped types.
13 new `LibraryScreenViewModelTests` cover load-reflects-settings, per-field write-back, and the
stale-category fallback; full suite (594 tests) passes, build clean. Pluggable sort/group strategies
remains explicitly paused/skipped this session (still waiting on the user's ideas for the 5
unmappable comparer/grouper concepts — AlternateCount/variant tracking, a Bookmarks system,
OpenCount/access tracking, a Proposed-metadata workflow, per-issue SeriesComplete — none of which
List Layouts needed).

**On-screen verification gap closed the same session** (docs/onboarding.md §17): the "no
unattended desktop GUI automation available" caveat repeated across ~15 specs/roadmap entries is no
longer categorically true. `src/Paperbunkr.App.UiTests` (FlaUI/UIA3) now drives the real compiled
exe — real process, real window, real clicks — with an isolated-database mechanism
(`PAPERBUNKR_DB_PATH`) so it never touches a real library. First real test:
`LibraryListLayoutPersistenceTests` (3 cases: sort field, view mode, filter checkbox), all verifying
the exact restart-survives-it claim above by actually restarting the app and reading the toolbar
state back via UI Automation, not by reading `AppSettings` from the database directly. Full solution
suite including this new project: 597 tests, all passing. Scope so far is limited to the Library
toolbar's `AutomationId`s; extending to other screens is unblocked infrastructure work, not a new
investigation — see §17 for the pattern.

**Pluggable sort/group strategies, previously noted "paused" above, did in fact ship** — as Issue
List Sort/Group (design spec `2026-08-18-issue-list-pluggable-sort-group-design.md`), later merged
into Library's own toolbar so there's only one Sort/Group control, not two. This doc's own "paused"
claim had gone stale; corrected here rather than left to mislead the next reader.

**Browse history (back/forward) shipped 2026-08-19** (design spec `2026-08-19-library-browse-
history-design.md`) — CE's real `IBrowseHistory`/`CursorList<IComicBookListProvider>` model
(`ComicListBrowser.cs`), ported onto Library's sidebar filter (Content Type/Collection) plus, as a
deliberate deviation the user asked for, the search query too (debounced to a pause in typing, not
per-keystroke). Reused `cYo.Common.Collections.CursorList<T>` as-is — already ported into this
codebase (`Paperbunkr.Common`) with zero call sites before this, same "ported early, never wired
up" pattern as several other pieces along the way. Two new toolbar buttons (`‹`/`›`, matching the
existing precedent from `BookReaderScreen.axaml`'s own prev/next buttons), gated by
`CanBrowsePrevious`/`CanBrowseNext`. 652 `Paperbunkr.App.Tests` + 274 `Paperbunkr.Data.Tests` +
15 `Paperbunkr.App.UiTests` all passing (one new live on-screen case: buttons start disabled, a
sidebar click enables Back, clicking Back actually returns to the prior selection).

Still open, in rough sequence: saved Workspaces (CE's `DisplayWorkspace` — named/multiple presets,
depends on List Layouts, now available); then independently, drag-and-drop import, Recent/MRU +
Quick Open overlay (CE's own version is recency-grouped, not fuzzy-search — a deliberate deviation
to decide on, not CE parity), filesystem folder browsing mode; file metadata write-back deliberately
sequenced last (CE itself gates it behind explicit opt-in settings — real risk surface, mutates user
files). A tracker/manga-UI research doc (Beta-scoped, see its own backlog entry) surfaced during
this work — its Stage 6 "manga-specific detail view, selected by ContentType" depends on ContentType
actually being real/editable/populated, which the Manga/ContentType entry above now provides.

**Live folder-watch scanning shipped 2026-08-23** (design spec `2026-08-23-live-folder-watch-
scanning-design.md`) — checked CE's own `FileSystemWatcher` usage first rather than assuming: it's
narrower than the name suggests, wiring only `Renamed` (path-sync, to avoid a renamed file
transiently reading as missing) and never `Created`/`Deleted`. Paperbunkr deliberately goes beyond
that (confirmed with the user, not assumed): per-folder `Watch` toggle in Preferences → Libraries;
new `LiveFolderWatchService` debounces `Created`/`Deleted` bursts into one batch flush (so a bulk
drag-drop produces one import pass and one toast, not hundreds), retries a locked/still-writing file
before giving up on it for that flush, and applies `Renamed` immediately as CE-parity path-sync.
`LibraryFolderScanner`'s per-file import body was extracted into a shared method so a live-watched
file gets identical embedded-metadata/proposal/series-matching treatment to a manual Scan Now. Real
pre-existing gap found and fixed along the way: nothing in the App layer ever set
`Issue.FileIsMissing` from a real-time disk check before this — a natively-scanned file deleted
outside the relink flow was never flagged missing at all; the watcher's `Deleted` handling is the
first fix for that, not just a live-UI nicety. 8 new `Paperbunkr.App.Tests` (real `FileSystemWatcher`
against a real temp directory, short debounce/retry windows via a test-only ctor seam) all passing;
a Preferences on-screen checkbox test was written but couldn't be executed in this session's
environment — even the pre-existing `HomeScreenTests` UI-automation baseline fails the same way here
(`FlaUI`/UIA3 needs an interactive desktop this session doesn't have), not something this feature
broke.

### Reading Lists: story-arc auto-build (CBL Manager port) ✅ Shipped 2026-08-22
Built and live-verified against all six sources (not just built — each one hit with a real request
this session, including a live re-check of catalog sizes/markup since the original `CBLManager`
port predates this by weeks): **ComicVine**, **Metron** (both credentialed, confirmed working with
the user's own real accounts), **Comic Book Reading Orders**, **ComicArc**, **ReadingOrders.com**,
**ReadThingsRight** (all four confirmed working; the latter three's initially-sparse-looking results
turned out to be their real, genuinely small catalogs — not a bug, cross-checked live via each
site's actual current markup). "Search Story Arc…" replaces the old disabled "Auto-Build from
Tracked Arc" button; a live "Refresh" button appears on any arc-linked list. A same-session
follow-up added curated browsing: picking a small-catalog source auto-lists its whole catalog with
a count instead of requiring a query first, so someone unfamiliar with a source's coverage can
browse rather than guess. AniList/MyAnimeList buttons are a *different* feature (personal tracker
sync, not story-arc lookup) and stay disabled/deferred, unrelated to this backlog item. New
credential store (`ProviderCredential`/`CredentialStore`) built generically enough to reuse for that
future AniList tracker-sync pass. Design docs: `docs/superpowers/specs/2026-08-22-cbl-manager-arc-
lookup-design.md`, `docs/superpowers/specs/2026-08-22-cover-memory-virtualization-design.md` (an
unrelated memory-usage fix done the same session), `docs/superpowers/specs/2026-08-22-cbl-manager-
curated-browse-design.md`. **Not yet committed as of this write-up** — still sitting as local
changes in this working tree.

### Remote/server library sharing
Client (connect to another instance's shared library) + server (host, password-protected,
per-list sharing) + background job/task monitor. Substantial subsystem, not named anywhere in the
original onboarding.md — needs its own brainstorm → design spec before any implementation starts.

### Metadata Model platform (user-supplied `PAPERBUNKR_METADATA_MODEL.md`, 2026-08-17/18)
79-section implementation spec covering canonical metadata, relationships, events/reading lists,
external providers, and recommendations — its own §68 "Migration Strategy" defines 7 phases.
**Phases 1-6a shipped (plus net-new Phases 4d-4g, 2026-08-27), Phase 7 explicitly deferred, plus a Specials Tab design (2026-08-28, not yet implemented)** (not dropped — the source doc itself gates it:
"Implement only when needed by the reader and collected-edition use cases," and there's no concrete
driving use case yet; revisit if one shows up). Design specs:
`docs/superpowers/specs/2026-08-17-metadata-model-phase{1,2a,2b,2c,3,4a,4b,4c,5a}-*-design.md`,
`docs/superpowers/specs/2026-08-18-metadata-model-phase{5b,6a}-*-design.md`.

- **Phase 1 — Canonical metadata**: `OpenCount`/`ColorMode`/`Series.Status`/`Volume`-as-string/
  `IssueBookmark` + resolvers, replacing CE's `BlackAndWhite`/`Volume`-as-int/`IsComplete`.
- **Phase 2a/2b/2c — Metadata proposals, series reassignment, library field descriptors**:
  `MetadataProposal`/`MetadataResolutionPolicy`, a resolver for moving issues between series, a
  data-driven field catalog driving Library sort/group/filter.
- **Phase 3 — Media Relations**: `MediaRelation`/`RelationEvidence`, first-class Series-to-Series
  connections (CE has no equivalent) — powers the Detail screen's Related tab.
- **Phase 4a/4b/4c — Continuity, Story Events, Reading List overhaul**: `Continuity` (M:M with
  Series), `StoryEvent`/`EventMembership` (+ a new Events screen), `ReadingList`/`ReadingListItem`
  gained `Type`/`CreatedAt`/`UpdatedAt`/`StoryEventId`/`Role`/`Notes` (CBL/CSV wire formats
  untouched — separate CE-plugin-tied overhaul planned).
- **Phase 4d/4e/4f/4g — Event Relations, Format-Signal Suggestions, Continuity Browse, Age
  Progression** (design specs `docs/superpowers/specs/2026-08-27-metadata-model-phase4{d,e,f,g}-*-
  design.md`; shipped 2026-08-27, **uncommitted** on branch `books/browse-chrome` as of writing —
  code + full new-and-existing test suites verified green, on-screen GUI pass not yet done):
  - **4d** — `EventRelation`/`EventRelationEvidence` (one new EF migration,
    `20260827193943_MetadataModelPhase4dEventRelations`; both endpoint FKs `Cascade`), reusing the
    Phase 3 `RelationType`/`RelationEvidenceProvider` enums wholesale. `EventRelationResolver`
    (source-side reads the stored type, target-side reads its `RelationTypeCatalog` inverse). New
    "Connected Events" section on the Story Events detail pane: search-and-connect (picker scoped to
    Prequel/Sequel/Continuation/Crossover/SameUniverse/SharedUniverse/Related/Other), per-card
    unlink, click-to-walk-the-chain. Verified: 11 resolver/migration tests + 4 VM tests; cascade
    both directions confirmed against a real pre-migration SQLite db.
  - **4e** — `FormatSignalCatalog` (classifies CE's 16 shipped `[Book Formats]` defaults; only 10
    carry an event signal, `Prologue`/`Epilogue`/`Minus 1` also map to an `EventMembershipRole`)
    and `EventSuggestionResolver` (Format signal **plus** either issue `Year` in the event's date
    range or event name in the issue's `SeriesGroup`/`StoryArc` — Format alone never surfaces a
    row). New collapsible "Suggested for this Event" queue with per-row role picker + Add/Dismiss
    (dismissals were session-only here; made persistent in the 2026-08-28 follow-up below).
    `Issue.Format` got its first real editor — an autocomplete combo on both the single and bulk
    Issue Properties editors, seeded with the CE vocabulary. **No migration** (additive over
    existing `Issue.Format`). Verified: catalog + resolver Data tests + 4 VM tests.
  - **4f** — Continuities mode: a segmented-control mode switcher (Events | Continuities | Timeline)
    on the Story Events screen; the sidebar and detail pane swap per mode, lazy-loaded on switch.
    `ContinuitySummary` sidebar rows, a member-series poster grid (click → series Detail),
    `+ Add Series` / per-card remove / `+ New Continuity` — all writing through the same
    `ContinuityResolver` calls the Related-tab UI uses (verified via `GetOtherSeriesSharingContinuity`
    round-trip in tests), `GetOrCreate`'s case-insensitive dedup covered. **No migration** (pure
    browse/edit UI over Phase 4a data). Verified: 6 VM tests + existing Events suite re-run green.
  - **4g** — Timeline mode: `ComicAge` enum + `ComicAgeCatalog` (CE's five `[Book Ages]` stages;
    `FromYear` seams verified at 1937/38, 1955/56, 1969/70, 1979/80), `BookAgeResolver`
    (explicit CE label wins → year inference; the 1980-84 window returns `Modern` at `0.6m`
    confidence with the disputed-window reason), `SeriesFamilyResolver` (BFS over `MediaRelation`
    edges ∪ shared `Continuity`, cycle-guarded; documented not character-aware). Read-only
    horizontal timeline: non-empty era sections only, year-ordered, cover thumbnails, unread dot,
    reduced-confidence `?` badge with reason tooltip, click → reader. **No migration**. Verified:
    `ComicAgeCatalog`/`BookAgeResolver`/`SeriesFamilyResolver` Data tests + 5 VM tests.
  - Full-suite run after all four: `Paperbunkr.Data.Tests` 514/514, `Paperbunkr.App.Tests`
    1063/1063, `Paperbunkr.Plugins.Tests` 11/11. `Paperbunkr.App.UiTests` not run (flaky in this
    environment, per prior sessions).

- **Phase 4d-4g deferred follow-ups — all implemented 2026-08-28** (second pass, same branch, still
  **uncommitted**; one new migration `20260828104324_MetadataModelPhase4DeferredItems`:
  `EventSuggestionDismissal` + `Character` + `CharacterAppearance` tables, all FKs `Cascade`.
  Full-suite verified: `Paperbunkr.Data.Tests` 533/533, `Paperbunkr.App.Tests` 1098/1098,
  `Paperbunkr.Plugins.Tests` 11/11; app smoke-launched, no XAML-weave crash):
  - **BookAge editor** — free-text autocomplete field (CE's five `[Book Ages]` labels) on the
    single and bulk Issue Properties editors, plus a `Character` index (`CharacterResolver`,
    materialized from the free-text `Issue.Characters` field on every Save, one-time
    `PaperbunkrDb.EnsureCreated` backfill guarded on "no `Character` rows yet").
  - **Persisted suggestion dismissals** — `EventSuggestionResolver.Dismiss/Restore/GetDismissed`;
    a "Dismissed" collapsible list on the Events pane with per-row Restore.
  - **Transitive event graph** — `EventRelationResolver.GetEventFamily` (BFS with hop depth); an
    "Event chain" collapsible, indented by depth, any node clickable.
  - **Event-relation auto-suggestions** — `EventRelationSuggestionResolver` (shared significant
    name word / overlapping-or-adjacent dates / shared member series); a "Suggested connections"
    list with one-click Connect using the current relation-type picker.
  - **Timeline scopes** — segmented `Series family | Continuity | Whole library` selector; a
    "character-aware" toggle (`SeriesFamilyResolver.GetFamily(..., characterAware: true)` does one
    extra one-hop expansion via `CharacterResolver.GetSeriesIdsSharingCharacterWith` — deliberately
    bounded, not transitive, so a ubiquitous character doesn't pull in a whole publisher).
  - **Bulk "review inferred ages"** — `BookAgeReviewResolver.GetInferred/Accept`; a collapsible
    panel in Timeline listing issues whose age is year-inferred, per-row + Accept-all, writing the
    CE-style label into `Issue.BookAge`. (The lightweight version 4g's spec described; not a full
    `MetadataProposal` integration.)
  - **Cross-continuity comparison** — `ContinuityResolver.GetOverlappingContinuities` /
    `GetSeriesInBothContinuities`; a "Compare" affordance in Continuities mode showing overlap
    counts and the shared-series set.
  - **Continuity → reading list** — `ContinuityReadingListBuilder.CreateFromContinuity` builds a
    `ReadingListType.PublicationOrder` list, issues interleaved chronologically across every member
    series; a "Reading list" button in Continuities mode, then navigates to the Reading screen.
  - **Continuity Smart List field** — new `SmartListField.Continuity` (enum-as-string, no
    migration), reads `Series.Continuities` joined as text; `SmartListQueryBuilder` loads that nav
    only when a Continuity condition is present.
  - First-class `Character` entity landed here (the gap 4g's spec documented). It's an index over
    `Issue.Characters`, not an editable entity — no character-management UI, and family expansion
    is one-hop by design. A fuller character model / character-scoped browse remains future work.
  - **Series Detail — Specials Tab** (designed 2026-08-28, following a Kavita comparison
    session — see `event-section-planning` project memory; **not yet implemented**): design
    spec `docs/superpowers/specs/2026-08-28-series-detail-specials-tab-design.md`. New
    `SpecialFormatCatalog` (Kavita's real special-triggering Format values, intersected with
    CE's actual 16-value list, plus 10 Kavita-only additions bundled into the Format
    autocomplete — CE has none of these, confirmed by grep, so flagged as a deliberate
    addition, not a port) + `Issue.IsSpecial()` extension. New Specials tab on the comic
    `DetailScreen`, between Issues and Related, hidden when empty; pulls Format-flagged
    issues fully out of the Issues tab rather than duplicating them, reusing the Issues tab's
    existing Poster/List/Card templates and view-mode setting. **No migration** (reads the
    already-shipped `Issue.Format`). Deliberately Format-only for this phase — Kavita's other
    two detection mechanisms (no-parsed-`Number` auto-detection, `SP##` filename marker) and a
    manual per-issue override are explicitly out of scope, per the design doc.
- **Phase 5a — External Metadata schema**: `ExternalMediaId`/`ExternalMetadataSnapshot`/
  `ExternalRating` + the `IMetadataProvider` adapter contract, schema-only, zero adapters/network/UI.
- **Phase 5b — Real AniList adapter**: `AniListMetadataProvider`, live GraphQL calls, rate-limit-
  aware (respects AniList's actual current 30 req/min degraded limit), licensing-verified against
  AniList's real terms (`github.com/AniList/docs`). **No longer backend-only** — a real search-and-link
  UI landed 2026-08-19 (`MetadataLinkResolver`/`TitleMatchScorer`, wired into `DetailTabsViewModel`;
  still uncommitted as of this sync, see `alpha-todo.md`'s live-tracker section for status). Every
  *other* provider (MAL/MangaDex/GCD/etc.) is still deliberately deferred — MangaDex has a sketched-
  only design spec (R5, not implemented); full tracker-service *sync* (as opposed to read-only
  search/link) remains the item below, and reuses this adapter rather than rebuilding it.
- **Phase 6a — Recommendation engine**: `RecommendationResolver`, a relationally-anchored (not
  whole-library-similarity) 7-signal explainable scoring engine reusing the Phase 3/4a/4b resolvers.
  Live-computed, not a persisted table. **No longer backend-only — a real Home screen shipped
  2026-08-22** (`c1e91a6`/`2b2da5e`), whose "Because You Read" module reuses
  `RecommendationResolver.GetRecommendations` as-is, per this doc's own note above about doing
  exactly that.

### Content-type classification & manga metadata scraping (onboarding.md §7/§9)
Tracker-driven classification pipeline (MangaUpdates/AniList), MangaDex metadata scraping,
search-and-confirm UI shared across classification/tracking/scraping. Explicitly Beta-scoped from
the start.

**Expanded scope, per user-supplied research (2026-08-12):** full tracker-service *sync*
integration (not just classification-time metadata lookup) — one `Track` row per series-per-
service, six services (MyAnimeList, AniList, Kitsu, MangaUpdates, Shikimori, Bangumi), one-way
progress push to start (Paperbunkr → tracker, on chapter-read/last-page), plus a manga-specific
detail-page UI (chapter-list-first, blurred-cover header, tag chips, icon-led metadata rows,
tracking status shown inline) ported from Mihon/Komikku's UX patterns, selected by `ContentType`
alongside Paperbunkr's existing Western comic detail view. Full findings, per-service auth/API
notes, and a staged implementation recommendation (data model → service abstraction → sync engine
→ token security → stats differentiation → Avalonia UI): [docs/tracker-manga-ui-research.md](tracker-manga-ui-research.md).

**Stage 6 (manga detail-page UI) designed + shipped 2026-08-23** (design spec
`2026-08-23-manga-detail-screen-design.md`, brainstormed with the user off the research doc above).
New `MangaDetailScreenViewModel`/`MangaDetailScreen.axaml`, routed by `ContentType` at
`MainViewModel.GoDetailForSeries` — Manga/Manhua/Manhwa get a chapter-list-first screen (blurred-
cover header via Avalonia's declarative `BlurEffect`, not a pre-rendered/cached bitmap; icon-led
status/type/source rows; outlined-pill tag chips; a new list-row Chapters tab with read-state
dimming, bookmark/missing icons, filter/sort, and row-click-to-read) while Comic/Unknown keep the
existing `DetailScreenViewModel`. The Related/Details(tracker linking + external metadata)/Activity
tabs are the same `DetailTabsViewModel` embedded, not duplicated (new `ShowIssuesTab`/`ShowTabStrip`
flags let it suppress its own Issues tab + tab strip when hosted this way). Reclassifying a series'
`ContentType` from either screen's header picker now re-routes live via a new `goDetailForSeries`
callback threaded through both view models. Floating Resume button and the recommendations row from
the research doc's reference screenshot were explicitly deferred, per the brainstorm. 732
`Paperbunkr.App.Tests` passing (new `MangaDetailScreenViewModelTests`); on-screen verification of the
new screen itself is still pending as of this write-up — build + tests pass and the app launches
clean, but nobody has clicked through the actual rendered UI yet.

Stages 1–4 (the `Track`/`TrackingLink` entity, `ITrackerAdapter` abstraction with AniList/
MyAnimeList/Shikimori/Bangumi adapters, one-way sync-to-tracker, and `CredentialStore`-backed token
handling) were already implemented in the codebase — surfaced while researching this session's
work, not built by it. MangaUpdates/Kitsu adapters, two-way sync, and Stage 5's statistics
dashboard remain unbuilt.

**Confirmed gap found 2026-08-23, deferred to a future session on the user's own call**: linking a
series to external metadata (AniList or MangaBaka) has never applied the fetched data to any
editable field. `MetadataLinkResolver.LinkAsync` only ever writes an `ExternalMediaId` reference,
appends a raw-JSON `ExternalMetadataSnapshot` (audit log), and adds alt-titles to `SeriesTitle` -
`Description`/`Status`/`ChapterCount`/`VolumeCount` are fetched and stored in the snapshot but never
read back anywhere; `ExternalMetadataResolver.GetLatestSnapshot` is called only from its own unit
tests, confirmed by grep. Been true since Phase 5b, not something this session broke. The existing
`MetadataProposal` accept/reject system (filename/embedded-metadata inference) doesn't cover this
cleanly either - it's Issue-scoped (Title/Format/Volume/Number/Count/Year/Series only), while
Summary/Status are Series-level. A real "Apply from [Provider]" feature is needed, not a reuse of
what's there.

**MangaBaka tracker adapter shipped 2026-08-23** (design spec `2026-08-23-mangabaka-tracker-
adapter-design.md`) — corrects this same session's own earlier "MangaBaka can't be a tracker"
conclusion, found wrong after a user-supplied OpenAPI spec surfaced a real authenticated
`PUT/PATCH /v1/my/library/{series_id}` endpoint. New `MangaBakaTrackerAdapter` (PAT-authenticated,
no OAuth flow, mirrors `BangumiTrackerAdapter`'s own reasoning for skipping OAuth), a lossless 1:1
`ReadingStatus`↔MangaBaka-`state` mapping (unlike Bangumi's forced 5-state collapse), wired into
`DetailTabsViewModel`'s tracker picker/sync loop and a new Preferences PAT field (instructions
pointing at the real page location the user found live: My profile → Settings → API and Apps → New
token). `TrackingService.MangaBaka` needed no migration (string-backed enum storage).
`MangaBakaMetadataProvider` also now implements `ITrackerSearchProvider` for free — search is
shared with the metadata-provider half, only the push logic is new. 1152 tests passing (415 + 737,
12 new for this adapter); the one open question flagged in the spec — whether `PUT` upserts a
library entry on first push or needs a separate `POST` — is explicitly unverified, since it needs a
real PAT to test against and none was generated this session (by design: the token was never meant
to pass through this session at all, only entered directly into Preferences by the user later).

**MangaBaka metadata-model/UI research memo added 2026-08-23**
([docs/mangabaka-metadata-ui-research.md](../mangabaka-metadata-ui-research.md)) - the user's real
interest in MangaBaka turned out to be broader than a tracker/metadata source: its site has a
categorized+weighted tag taxonomy (Genres/Themes/Settings/Character Archetype/etc.), typed
relations, a crowd-sourced multi-cover archive, ID-prefix/URL-paste cross-referencing, and
tag-based + collaborative-filtering recommendation rails, gathered by actually browsing the live
site (not just its API). Research only, not a design spec - feeds a future brainstorm before any
of it gets built, same status this doc's other unimplemented research items carry.

**Two follow-on features shipped same session, 2026-08-23**, from user feedback on the new manga
screen: **cover art override** (design spec `2026-08-23-cover-art-override-design.md`) — a
deliberate CE deviation letting any series/issue's displayed cover be replaced with a user-picked
image regardless of file-linked status, three entry points (both screens' headers, the Western
Issues tab tile menu, the manga Chapters tab row menu), reusing the existing on-disk thumbnail
cache with no schema change; and a **second metadata provider, MangaBaka** (design spec
`2026-08-23-mangabaka-metadata-provider-design.md`) — confirmed live that MangaBaka has no
user-account/library API at all, so it's a search/link `IMetadataProvider` alongside AniList, not a
tracker. The user's actual interest in MangaBaka is broader (its site's rich tag taxonomy, typed
relations, recommendations) — flagged as a separate future research pass, not built this session.
1136 tests passing (`Paperbunkr.App.Tests` 737 + `Paperbunkr.Data.Tests` 399, both suites run in
full, not just the new cases); on-screen verification of all three items (manga screen itself, the
cover-art entry points, the MangaBaka provider picker) is still pending as of this write-up.

### Plugin API v2 (onboarding.md §10) — engine + 4 real hooks shipped 2026-08-24, remaining hooks/UI surfaces still open
Design spec: `2026-08-24-plugin-api-v2-design.md`. Shipped this session: new `Paperbunkr.Plugins`
project (`PluginEngine`, `Command`/`CSharpCommand`, `IPluginEnvironment` + all 5 sub-interfaces as
typed abstractions, all 17 hook constants + typed globals/payload types, `XmlPluginInitializer`
manifest parsing, Roslyn C# scripting compile via `Microsoft.CodeAnalysis.CSharp.Scripting` —
`pythonnet`/IronPython replacement deliberately deferred, matching the design doc); real
Paperbunkr-native adapters in `Paperbunkr.App/Plugins/` wired to actual services (library CRUD,
cover thumbnails, page decode, reader navigation, skin key); `PluginCommandState` EF entity +
migration for enable/disable persistence; a rebuilt Plugin screen (list/toggle/compile-error
display, replacing the placeholder from `2026-08-09-plugin-screen-cleanup-design.md`); and a real
"Duplicate Finder" test plugin (`src/Paperbunkr.Plugins.Tests/SamplePlugins/DuplicateFinder/`)
exercising Startup/Library/CreateBookList end-to-end. **4 of 17 hooks have a real live trigger**
(Startup, Shutdown, BookOpened, Library via the test plugin's engine-level invocation) — 10 tests in
`Paperbunkr.Plugins.Tests` cover discovery/compile-success/compile-failure/dispatch/exception-
capture/ConfigScript-pairing plus the full Duplicate Finder fixture end-to-end; 2 tests in
`Paperbunkr.App.Tests` cover `PluginCommandState` persistence; full existing suite (814 App +
447 Data) still green, no regressions. **On-screen verified 2026-08-24** — user screenshotted the
Plugin screen showing Duplicate Finder's 3 commands with working checkboxes; one real bug found and
fixed from that screenshot (hook badge bound to the long group description instead of the short
hook name, overflowing illegibly — fixed to show `Hook` with the description as a tooltip instead).
**Explicitly deferred, not yet wired to a live UI trigger:** Editor/Books/NewBooks/CreateBookList
(sidebar)/ParseComicPath/NetSearch/ConfigScript/ReaderResized hooks (engine supports them, no menu/
lifecycle anchor wired to a real screen yet — `IBrowser.SelectComics` is a documented no-op since
Library grid's selection model isn't exposed for plugin control yet); the three net-new UI surfaces
the design spec called for (ComicInfoHtml/UI info panel tab, QuickOpenHtml/UI command palette,
DrawThumbnailOverlay paint hook) were not built this session despite being in scope on paper — full
skeleton for all 17 hooks plus all 3 stub surfaces is a larger follow-on, not a quick add-on.

### SmartList Engine v2 — nested groups + operators + AllProperties split (shipped 2026-08-29)
Design spec: `2026-08-28-smartlist-engine-v2-design.md`. Three CE-parity gaps closed:
(§2) `SmartList.Conditions` flat always-AND list → nested `SmartListConditionGroup` tree
(`SmartListGroupMode` And/Or, self-referencing, per-condition `Not`); one EF migration with a
zero-data-loss backfill (one And root group per existing list, every condition repointed,
`Not=false`/`IgnoreCase=true`); `SmartListQueryBuilder.Build` now recursive; SmartScreen rule
builder rebuilt as a recursive group card (a single flat group renders like the pre-v2 pill list).
(§3) `SmartListOperator.ListContains` (whole `,`/`;`-delimited item, verified against
`_reference/ComicRackCE/…/ComicBookStringMatcher.cs`) + `.RegularExpression` (250ms timeout,
malformed→no-match, never throws); `SmartListCondition.IgnoreCase` (default true) replaces the
hardcoded `OrdinalIgnoreCase`; "Aa" toggle + the two operators in the Text-field row editor.
(§4) new shared `SearchFieldBundleCatalog` (Paperbunkr.Data): `LibraryScreenViewModel.MatchesSearch`
refactored onto it (behaviour-preserving, golden-list parity test) and `SmartListField.AllProperties`
+ `SmartListCondition.SearchMode` special-cased in the query builder, two-dropdown field picker.
Tests: 1704 (App 1134 + Data 565 + the new v2/parity/migration suites) green; migration verified
applying to a fresh DB; app smoke-launched to "Startup complete". On-screen GUI pass still pending.

### Smart Collections — rule-based Collection membership (shipped 2026-08-30)
Design spec: `2026-08-30-smart-collections-design.md`, plan: `2026-08-30-smart-collections-plan.md`.
Closes the deferred "smart collections" item from `2026-08-27-collections-design.md` — scope grew
beyond that item's one-line note during brainstorming (user chose the larger option at each fork).
`SmartList` gains `TargetKind` (Issue/Series/Novel, default Issue — zero behavior change for every
pre-existing list). Ten new `SmartListField` values for Series/Novel (`SeriesStatus`/
`SeriesSortName` plus `Novel*` — prefixed to avoid colliding with the pre-existing "Book-collection"
comic-collector fields, unrelated to the `Book`/novel entity). New `SeriesSmartListCatalog`/
`NovelSmartListCatalog` + `SeriesSmartListQueryBuilder`/`NovelSmartListQueryBuilder`, mirroring the
Issue builder's shape; leaf operator evaluation (`EvaluateText`/`Number`/`Toggle`/`Date`) extracted
into a shared `SmartListLeafEvaluator` used by all three (a mid-flight refactor, not just new code —
those methods were already kind-agnostic in everything but visibility). `Collection` gains three
optional rule slots (`IssueSmartListId`/`SeriesSmartListId`/`NovelSmartListId`); `CollectionResolver.
GetMembers` unions manual `CollectionItem` rows with each slot's live matches, deduped by target id
(hybrid membership — approved design, not the original minimal "100% rule-derived" option). Also
fixed two real pre-existing bugs found during implementation: `CollectionSummary.Count` and
`LibraryScreenViewModel`'s non-series-member check read raw `CollectionItem` rows instead of
`CollectionResolver.GetMembers`, and `GetOtherSeriesSharingCollection` queried `CollectionItem`
directly rather than going through `GetMembers` — both now correctly reflect rule-matched
membership. Smart Lists screen generalized to 3 kinds (sidebar SERIES/NOVELS sections, per-kind
field-picker scoping, per-kind results grids). `CollectionPropertiesOverlay` gained three rule-slot
pickers (dropdown + Set + Clear — no inline "New rule…" round-trip to the Smart Lists screen, a
deliberate scope trim); rule-matched member rows render with disabled Remove/Move and an explanatory
tooltip. One EF migration (`AddSmartCollections`) — a real enum-string default-value bug
(`defaultValue: ""` instead of `"Issue"`) caught and fixed before shipping, same class of bug as the
Preferences Reader tab session's `HasSentinel` catch. Tests: 1909 total (Data 633 + App 1260 +
Plugins 16), all green; app smoke-launched to "Startup complete" via a properly-detached process
(an earlier attempt to launch it from a backgrounded bash job was killed by shell teardown, not a
real crash — worth remembering for future smoke-launches in this environment). On-screen GUI pass
still pending.

### MediaRelation collection nodes (shipped 2026-08-30)
Design spec: `2026-08-30-media-relation-collection-nodes-design.md`, plan: `2026-08-30-media-
relation-collection-nodes-plan.md`. Closes the last item from `2026-08-27-collections-design.md`'s
deferred list — the other two (`RecommendationReason.SameCollection` wiring, Home-feed shelf) were
found already shipped during the audit that led to this spec; that doc's own note was stale.
`MediaRelation`'s `SourceSeriesId`/`TargetSeriesId` relaxed to nullable, plus two new nullable
`SourceCollectionId`/`TargetCollectionId` columns, each side guarded by an exactly-one `CHECK`
(mirrors `CollectionItem`'s existing polymorphic-target pattern). Collection↔Collection is rejected
in `MediaRelationResolver.TryCreate` — that combination stays `CollectionRelation`'s job, avoiding
two inconsistent ways to link two collections. `MediaRelationResolver.GetRelatedSeries` replaced by
`GetRelatedFromSeries`/`GetRelatedFromCollection`, both returning a new mixed-kind
`MediaRelationEndpoint` (mirrors `CollectionResolver.CollectionMember`'s discriminated shape).
`IMetadataGraph` gains 3 additive overloads (`GetRelatedCollections(Series)`,
`GetRelations(Collection)`, `GetRelatedSeries(Collection)`) — user chose the larger "full first-class
plugin type" scope over the smaller additive-only option during brainstorming. Series Detail's
"Related Series" rail renamed "Related" with a mixed Series+Collection add-flow; Collection editor
gains a parallel Series-only "Related" section (deliberately scoped to Series-only search — a
Collection↔Collection match found there would just be rejected). Two real bugs caught during
implementation: `TryCreate`'s duplicate check used a custom C# method EF couldn't translate to SQL
(fixed by materializing the candidate rows first, `AsEnumerable()` before the in-memory filter); a
migration round-trip test's own directional-inversion assertions were initially written backwards
and caught by the test itself. Tests: 1926 total (Data 642 + App 1268 + Plugins 16), all green (one
known-flaky, unrelated concurrency test confirmed passing in isolation); app smoke-launched to
"Startup complete". On-screen GUI pass still pending.

### App shell navigation history — back/forward, breadcrumbs, restore-on-launch, CLI deep-linking (shipped 2026-08-31)
Design spec: `2026-08-30-app-shell-navigation-history-design.md`, plan: `2026-08-30-app-shell-
navigation-history-plan.md`. Follow-on to Phase 2 of the UI rework (`2026-08-24-navigation-shell-
motion-system-design.md`), which explicitly left drill-down history/breadcrumbs out of scope. New
`NavigationHistoryService` (list + cursor, same shape as CE's own `IBrowseHistory` — confirmed by
reading `ComicListLibraryBrowser.cs` — generalized from "library list selection" to "drill-down
screen + entity") replaces `MainViewModel`'s old single-slot `_screenBeforeReader`/
`_screenBeforeBookReader` hacks, which only ever supported exactly one level and didn't cover
Detail/MangaDetail/BookDetail at all. Every drill-down `GoX()` method split into a Core (no history
side effect, reused by Back/Forward/restore/CLI replay) + a thin history-pushing wrapper; every
lateral rail `GoX()` resets the stack and becomes the new root. Breadcrumb bar (new `Breadcrumb`
control) shown only on the six drill-down screens. Backspace = Back (no keyboard binding for
Forward, by design); trackpad two-finger swipe is a best-effort `PointerWheelChanged` heuristic,
flagged unverified on real hardware. Restore-on-launch + `--open <kind>:<id>` CLI deep-linking both
persist/read `AppSettings.LastScreenKey`/`LastScreenEntityId` (new migration
`AddLastScreenState`), falling back to Home when the referenced entity was deleted.

Three real issues found and fixed during implementation: (1) a design-doc assumption that
`KeyboardCommandRegistry` was a general shell shortcut system turned out wrong — it's Reader-scoped
infrastructure (`ConflictContext` is defined purely in terms of `PageCanvas`'s paged/continuous
states) — caught during planning, before any code was written, fixed by using the same plain
`<KeyBinding>` mechanism `Escape` already uses. (2) A real startup crash shipped in the first build:
the XAML gesture string `"Backspace"` isn't a valid `Avalonia.Input.Key` enum member — the correct
name is `Back` — confirmed via reflection against the installed `Avalonia.Base.dll` after the app
crashed on launch (`KeyGesture.Parse` → `ArgumentException`), fixed and re-verified by an actual
smoke launch (not just a clean build) before calling this done. (3) The breadcrumb bar's first cut
was a same-cell overlay (topmost z-order sibling in the drill-down `Grid`, not its own layout row) —
its opaque background visually covered each drill-down screen's own top-of-content controls, e.g.
`DetailScreen.axaml`'s "← Back to Library" link, found by the user via actual on-screen testing (two
screenshots) after a clean build had already passed. A first attempted fix (adding real
`Grid.RowDefinitions`/`Grid.Row` so the breadcrumb pushes content down instead of overlaying it) was
structurally correct but appeared not to work on the first retest — turned out the user's own running
app instance was locking the exe the whole time, so the rebuild's copy-to-output step kept silently
failing (`2 Error(s)`, both `MSB3021`/`MSB3027` copy locks) and the binary being tested was never
actually updated; caught by re-checking the build output rather than assuming "user says it's still
broken" meant the code fix was wrong. One test (`GoReaderForIssue_FromDetail_BackStillReturnsToDetail`)
was rewritten to navigate via the real `Library.GoToSeries` entry point instead of a raw
`CurrentScreen` poke — the poke bypassed the new history system entirely, so the test was asserting
behavior no production code path can actually reach.

Built and verified while a different concurrent session was actively landing an unrelated
first-run-onboarding feature (`WelcomeScreenShown`/`WelcomeTourOffered` in the same `AppSettings.cs`,
same shared working tree) — both landed cleanly with no data loss on either side, confirmed by
`git diff` and a fully green test run after their WIP settled. Tests: `Paperbunkr.App.Tests` 1327/1327
green, `Paperbunkr.Data.Tests` 679/679 green. App smoke-launched via `PowerShell Start-Process`
(per this project's own documented gotcha about backgrounded shell jobs) and confirmed still running
5+ seconds later — not just "0 Errors" on a build. Breadcrumb layout (Row 0/Row 1 split, not
overlaying the "← Back to Library" link) user-verified on screen 2026-08-31 after the exe-lock false
negative above. Still pending: Backspace-while-focus-is-in-a-textbox and the actual trackpad-swipe
gesture — no unattended GUI automation available in this environment for those, same standing caveat
as every other desktop-input spec here. The comprehensive-keyboard-operability follow-on (context
menus, flyout menus, card navigation on screens built after P5) was deliberately split out as a
separate future spec, not folded in here.

### Comprehensive keyboard operability (shipped 2026-08-31)
Design spec: `2026-08-31-keyboard-operability-design.md`, plan: `2026-08-31-keyboard-operability-
plan.md`. Split out from the app-shell navigation history spec's own brainstorm (above). Three
pieces: (1) Menu key/Shift+F10 added to `ContextMenuHost` — the one shared right-click mechanism —
rooted at the focused element instead of pointer position; (2) migrated 4 dead `<ContextMenu>`
instances (MangaDetail chapter rows, BookDetail bookmarks/series cards, Reader page thumbnails) plus
4 brand-new menus (comic Detail issue tiles, Collection editor members, Reading List rows, Events &
Continuity sidebar rows) onto that mechanism; (3) rolled `GridKeyboardNavigation` spatial nav out to
Books and BookDetail's series-mode grid, plus a separate fix for Smart Lists' virtualized grid.

Real findings during implementation, beyond what the design/plan anticipated:
- **Events & Continuity has no card grid at all** — the actual browse surface is a single-column
  sidebar list in `MainWindow.axaml` (`DataContext` = `MainViewModel`, not `EventsScreenViewModel`),
  found by reading the real XAML instead of trusting the design doc's assumption. Dropped from the
  grid-nav rollout entirely (a single column has no 2D spatial meaning); the context-menu provider
  ended up living on `MainViewModel` for the same reason, which turned out to simplify the "needs a
  new compose command" problem the plan worried about, since `MainViewModel` already owns both the
  select and edit-dialog entry points.
- **Smart Lists' `VirtualizingWrapPanel` already had the correct navigation logic built in**
  (`GetControl(NavigationDirection)`, Avalonia's native `INavigableContainer` extension point,
  confirmed via reflection against the installed `Avalonia.Controls.dll`) — just never invoked,
  since a bare `ItemsControl` doesn't wire arrow keys to it the way `ListBox` does. The fix was
  invoking Avalonia's own mechanism, not duplicating `GridKeyboardNavigation`'s realized-container
  assumption (which would have silently broken for off-screen items).
- **A broader sweep found 5 more dead `<ContextMenu>` instances** beyond the 4 the design doc
  cataloged: a tag-pill weight-picker menu duplicated in both `ReadingScreen.axaml` and the shared
  `DetailBand.axaml` (fixed once with one shared `TagPillContextMenuBuilder`, covering all 3 detail
  screens `DetailBand` serves); Library's Details-table column-picker (`ItemsControl.ContextMenu`,
  a different shape — element-scoped via a second `ContextMenuHost.Provider` on just the header
  `Grid`, not target-type-scoped like every other builder, since the picker always shows the same
  full column list regardless of what was clicked); and an already-dead `IssueContextMenu` resource
  in `DetailTabs.axaml` that fully overlapped the new comic-Detail issue-tile menu — merged rather
  than left duplicated, so that menu ended up with more content (Show in Explorer, Mark Read/Unread,
  Set/Reset Cover) than originally scoped. User explicitly approved fixing all 5 mid-session rather
  than deferring them.
- **A real cross-test-class bug, not a build fluke**: several new test classes were missing
  `[Collection(nameof(AvaloniaTestCollection))]`, letting them run in true parallel with the
  Avalonia-collection test classes despite constructing Avalonia-touching ViewModels, corrupting
  shared state and cascading into ~34 failures across unrelated pre-existing tests when run together
  — traced by re-running individually vs. batched, not assumed from the first failing run (which
  looked identical to an unrelated transient build-lock issue this session, so it took two separate
  investigations to tell them apart).

Tests: `Paperbunkr.App.Tests` 1385/1385 green (up from 1327 before this spec). App smoke-launched via
`PowerShell Start-Process`, confirmed running 6+ seconds later, left open for on-screen verification.
On-screen visual/keyboard verification (Menu key opening menus in the right place, arrow-key nav
feel on each rolled-out grid including the Smart Lists virtualized case specifically, tag-pill and
column-picker menus) still pending — no unattended GUI automation available in this environment,
same standing caveat as every other input-focused spec in this project.

### Plugin API v3 — metadata/rules/writer for Data-Manager plugins (shipped 2026-08-29)
Design spec: `2026-08-28-plugin-api-v3-data-manager-design.md`. Extends the v2 host (no new hooks).
(§2) `IMetadataGraph` — 6th `IPluginEnvironment` sub-interface, read facade over the Phase 3-4g
resolvers. (§3) `IApplication.GetLibraryBooks()`/`GetBook()` now eager-load Tags/CustomValues/
MetadataProposals/Bookmarks (were silently empty). (§4) `IRulesEngine` — `PluginCondition`/
`PluginConditionGroup` DTOs + adapter calling `SmartListQueryBuilder` directly (zero duplicated
matching); `EvaluateSmartList(id)` for the common case. (§5) `IMetadataWriter` — audited per-field
setters + `confirmWrites="true"` manifest gate (per-invocation `PluginInvocationContext`, writes
fail closed until `AskQuestion` is answered affirmatively). (§6) `PluginSettingState` entity +
migration, `IPluginConfig.GetSetting`/`SetSetting` scoped per `PluginKey`. (§7) sandbox fence —
`SmartListQueryBuilder` + the 7 metadata resolvers are now `internal` (`InternalsVisibleTo` App +
both test assemblies, never Plugins); `BlockedMetadataReferenceResolver` closes the verified
`#r "Microsoft.EntityFrameworkCore"` hole; `PaperbunkrDbContext` ctor kept public (broad test
usage) but a script still can't open one; `wiki/Plugins.md` updated with the "accidental overreach,
not adversarial isolation" framing. §8 tests incl. an end-to-end Data-Manager fixture plugin.
Plugins.Tests 16/16, no regressions. On-screen GUI pass still pending.

### App chrome (crash reporter + minimize-to-tray shipped 2026-08-23/24)
Crash reporter dialog, minimize-to-tray, external "open with" app associations.
*(Backup manager and file association are already shipped as part of Alpha's Advanced tab.)*

**Crash reporter dialog and minimize-to-tray shipped**, design spec
`2026-08-23-app-chrome-crash-reporter-and-tray-design.md`. Built on the diagnostics/crash-capture
infra that already existed (`DiagnosticsService` - see its own doc comment) rather than duplicating
it: `CrashReportWindow` (a plain `Window`, not this app's usual borderless-overlay pattern, since it
must survive whatever just broke) now shows on `AppDomain.UnhandledException` and
`Dispatcher.UIThread.UnhandledException`, with Restart/Exit always offered and a real Continue option
for the Dispatcher source specifically - found and used `DispatcherUnhandledExceptionEventArgs.Handled`
along the way, correcting a stale comment in `DiagnosticsService` that claimed no such swallow-and-
continue mechanism existed in Avalonia. `FreezeWatchdogService` adds CE's background freeze-watchdog
(1s poll/10s threshold, matching `CrashWatchDog.LockWatcher`), reporting via a native `MessageBoxW`
P/Invoke shim rather than an Avalonia window, since Avalonia has one Dispatcher/UI thread per process
and can't render a window from the watchdog thread while the main one is stuck. Minimize-to-tray adds
a Preferences → Advanced "Minimize to tray" toggle (off by default, matching CE) via `TrayIconService`
wrapping Avalonia's `TrayIcon`; deliberately diverges from CE by also redirecting the window's own
close (X) button to the tray while enabled - guarded by `WindowCloseReason` so OS/session shutdown and
the tray's own Exit still pass through for a real quit rather than hanging the OS. 811 App.Tests
(including 8 new `FreezeWatchdogServiceTests` driving `Tick()` with a fake clock/ping, and 2 new
`PreferencesScreenViewModelTests`) + 447 Data.Tests passing; the app was confirmed to start cleanly
with all the new wiring active (`startup.log` reaches "Startup complete." on every run), but the
interactive parts (checkbox toggle, tray icon appearing, restore/exit) were **not** verified live
on-screen - computer-use access was denied for this app, same as a prior session's manga-detail work.

### Novels: EPUB/PDF support (Phase 1+2 landed 2026-08-09/10, Phase 3 landed 2026-08-10)
Not a CE-parity item — ComicRackCE has no prose-reading equivalent, see the design spec's own
CE-verification note. Design: docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md.
**Phase 1 shipped** (independent `Book`/`BookSeries`/`BookBookmark`/`BookFolder` schema, `VersOne.Epub`-
and raw-`pdfium`-text-API-backed parsers behind a shared `IBookTextSource`, folder-scan import with
cover/metadata extraction, a Books nav section with a covers grid — no reader yet, verified via 17
new xUnit tests against real synthetic EPUB/PDF fixtures plus a full migration-chain smoke test).

**Phase 2 shipped, with one deliberate deviation from the original design spec §5: PDF reading was
moved off the reflowable-text pipeline entirely.** EPUB got the real reflowable reader as designed
(`BookPaginator` pure paragraph-fitting math + real Avalonia `TextLayout` measurement, immersive
tap-to-reveal chrome, TOC drawer, font/theme sheet — size/family/line-spacing/theme). PDF was
confirmed via manual testing against real e-book PDFs to be a poor fit for text-extraction reflow
(exactly the risk §9.1 flagged going in — footnotes/columns/running headers interleaved
unpredictably), so PDFs now open in a separate comic-panel-style reader instead
(`PdfPageReaderScreenViewModel`), reusing `PageCanvas`/`PageImageDecoder`/`ZoomPanMath` directly -
`PageCanvas` has zero Issue/comic coupling, so this was a straight port of the existing zoom/pan/
page-turn interaction onto `Book`, not a new implementation. `PdfBookSource` (text extraction) is
still used for import-time metadata only, not for reading.
Two real bugs found via manual testing and fixed along the way: the reflow reader could get stuck
permanently blank the first time it was ever shown (viewport size never reported - fixed with a
`Loaded`-event fallback), and a chapter with zero paragraphs (a real EPUB's cover/title-page spine
item) left it stuck showing nothing (fixed by skipping to the first chapter with content). 12 new
tests.

**Phase 3 shipped (2026-08-10): resume position, bookmarks, in-book search for the EPUB reader**
(design spec §6/§7 — PDF's separate comic-panel reader was out of scope, per the user's explicit
ask for "the EPUB reader"). Resume and bookmark position both use the same (ChapterIndex,
CharacterOffset) paragraph-boundary identity `BookPosition` already established in Phase 2, so both
stay stable across font-size/theme changes and window resizes, same as live pagination does.
- **Resume:** `Book.LastChapterIndex`/`LastCharacterOffset`/`LastOpenedTime` are read on `LoadBook`
  (chapter index clamped defensively; `BookPaginator.FindParagraphIndex` already clamps a stale
  offset) and written via a new `PersistPosition()` after every explicit navigation (chapter/page/
  bookmark/search jump) — deliberately *not* called from `RecomputeCurrentPage` itself, since that
  also runs on every font/theme change and would otherwise fire a DB write per slider tick. Same
  "fresh context per write" shape `ReaderScreenViewModel.GoToPage` already uses for
  `Issue.LastPageRead`.
- **Bookmarks:** a 🔖 icon opens a new Bookmarks drawer (mirrors the TOC drawer) with a toggle button
  for the current page plus the full list, each row jumping via `GoToBookmarkCommand` or deletable
  via `DeleteBookmarkCommand`. `BookBookmark.Excerpt` is the paragraph the page starts on, truncated.
- **Search:** a 🔍 icon opens a top-anchored search sheet; typing runs a linear substring scan
  (case-insensitive, one match per paragraph, capped at 200 results) over the already-parsed
  in-memory chapters — no persistent index, per design spec §7. Results jump like TOC/bookmarks do.
- Real bug found and fixed along the way: `LoadBook`'s `context.Books.Single(...)` query didn't
  `.Include(b => b.Bookmarks)`, so a book's saved bookmarks silently came back empty on every reopen
  (no lazy-loading proxies configured in this project - caught by a test, not manual testing this
  time).
- 8 new tests (resume-survives-reopen, bookmark add/remove/persist/navigate/delete, search
  match/no-match/short-query) — all against the existing `EpubFixture`, no new fixture needed.
  282 tests total in the suite now pass. **Manual-only, not yet done:** actual on-screen bookmark
  drawer / search sheet interaction — no unattended desktop GUI automation available for this
  project (same caveat as Phase 2's TOC/font-sheet verification).

**Two real, pre-existing bugs found and fixed during manual testing against a real 1992-series
library and real e-book files** (neither caused by this Novels work, both unrelated to it):
1. `CoverImageCache`'s LRU cache (`src/Paperbunkr.App/Services/LruCache.cs`) disposes a `Bitmap`
   the instant it's evicted; `LibraryScreenViewModel.LoadFromDatabase` requests a cover for every
   series in one synchronous pass before anything renders. Past the cache's 1000-entry cap (sized
   against a 371-series test library), the earliest covers in that pass got disposed before the
   layout pass that displays them ever ran — crashed Library on startup with `ObjectDisposedException`
   on `Ref<IBitmapImpl>` for any real library over ~1000 series. Fixed by raising the cap to 5000
   (comfortable margin over the real 1992-series case that found it); same fix applied to the new
   `BookCoverImageCache`, which copied the identical pattern, before it could bite there too.
2. `PdfiumReaderEngine` (`PDFiumSharpV2`, the existing comic-PDF-reading pipeline) P/Invokes
   against `pdfium_x64.dll`, but the native binary actually bundled (`bblanchon.PDFium.Win32`)
   ships a plain `pdfium.dll` — the names never matched, so PDFiumSharpV2 silently failed to load
   its native library for every real PDF, swallowed by `ComicProvider.Open()` into a silent
   `Count == 0` rather than a visible error. This means **PDF-as-comic reading has likely never
   actually worked** for a real PDF file, not just Book PDF cover generation (which reuses this
   same pipeline) — nobody had tested it against a real, non-synthetic PDF before. Fixed with a
   `NativeLibrary.SetDllImportResolver` on PDFiumSharpV2's own assembly (in `PdfiumReaderEngine`'s
   static constructor) redirecting its DllImport names to the same already-resolved `pdfium.dll`,
   verified against two real e-book PDFs. Worth a dedicated regression test in a future pass — today's
   verification was manual/diagnostic, not a checked-in test, since it depends on files outside the repo.

### Deferred / dropped (no action needed)
- **News reader** (`Help > News` RSS) — deferred, live idea to repurpose the feed mechanism for
  something Paperbunkr-relevant; needs its own brainstorm before scoping
- **Export to another format** — dropped, format conversion is a separate concern from this app
- **GitHub self-updater** — dropped for now, revisit once there's a real release/distribution
  pipeline
- **Portable device / wireless sync** — excluded, feature doesn't exist in Paperbunkr's model
- **Multi-tab/multi-window book-open (CE's MDI model)** — confirmed non-goal, single-screen
  rail-nav is the deliberate design target (Mihon/Komikku-style)
