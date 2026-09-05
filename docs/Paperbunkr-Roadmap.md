# Paperbunkr Roadmap

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

Carry these into Beta polish, not blocking the Alpha declaration itself.

**Manual session note (2026-09-05): 3 of 4 were already fixed, this list just never got updated.**
Checked each against `docs/alpha-todo.md`, the doc a human actually maintains status in:

- ~~`PageCanvas` requires a click before arrow-key navigation registers in the Reader~~ — **fixed**,
  `alpha-todo.md` line 467.
- ~~Virtual Tags compute correctly but aren't wired into Smart Lists or any display surface yet~~ —
  **fixed**, wired into both (`alpha-todo.md` lines 477-478).
- ~~Series.Genre vs Issue.Genre display inconsistency~~ — **fixed**, full audit done
  (`alpha-todo.md` line 486).
- **Content-type classification is a manual dropdown on Detail — no real §7/§9 auto-classify
  pipeline.** Still genuinely open, and it's not a small fix — see "Content-type classification &
  manga metadata scraping" below, which is the real tracking entry for this. Partially built
  already (publisher-based heuristic classifier, tracker sync stages 1-4, manga detail screen,
  MangaBaka + MangaUpdates + Kitsu adapters, Apply-from-Provider, MangaDex metadata scraping,
  two-way tracker sync all shipped); only the Stage 5 stats dashboard remains unbuilt.

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

### On-screen verification — cleared by the user 2026-09-04

Almost every Beta-backlog entry below carries a trailing "on-screen GUI pass still pending" /
"manual verification still pending" caveat — the standing "no unattended desktop GUI automation in
this environment" limitation, which meant a lot of shipped, test-green work had never actually been
clicked through by a human. **The user personally verified the following on 2026-09-04 and reports
them working on screen.** The per-section caveats below are superseded for these items; they are
left in place only as history.

- **Activity Center** — status bar + background-job/alert peek/drawer (committed `504e717`, local,
  not yet pushed to `origin/master` as of this note).
- **Panorama variable cover widths** — real cover orientations while virtualized (merged, PR #42).
- **Drag-and-drop import** — files / folders / `.cbl` onto the Library and Reading List screens,
  including real OS-level drag (the part that was explicitly not automatable here).
- **File metadata write-back** — the Preferences → Advanced "Comic File Metadata" group and the
  six edit-flow triggers.
- **Library view-mode virtualization** — `ListBoxItem` chrome neutral in both themes, group
  headers render, arrow-key nav on the Poster grid.
- **Manga detail screen**, **cover-art override** (all three entry points), **MangaBaka provider
  picker**.
- **Saved Workspaces** (Library + Books switcher) and the **`Ctrl+P` Quick Open palette** — the
  FlaUI tests for both are written but unrunnable in this environment; the user covered them by hand.
- **Double-page spread** rendering / pairing / reflow, **remappable reader keyboard shortcuts**
  (keypress-to-action wiring), **auto-scroll / hands-free mode** stop behavior.
- **Comprehensive keyboard operability** — Menu key / Shift+F10 opening context menus at the
  focused element, arrow-key nav feel on every rolled-out grid (including the Smart Lists
  virtualized case), tag-pill and column-picker menus.
- **Smart Collections**, **MediaRelation collection nodes**, **SmartList Engine v2** (nested
  groups + operators + AllProperties), **Plugin API v3** (metadata/rules/writer facade).

Still genuinely unverified on screen (no code change, just never checked): the app-shell nav
history trackpad two-finger swipe and Backspace-in-a-textbox edge case; the crash reporter /
minimize-to-tray interactive parts; auto-update through a real tagged release (see that entry).

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
slider"). **On-screen overlays (clock/battery + "Part X/Y" label), split-page part navigation
(including a double-page-spread extension), and the touch center-zone → chrome-toggle gesture all
shipped 2026-09-05** (docs/superpowers/specs/2026-09-05-reader-polish-backlog-finish-design.md) —
this closes the Reader polish backlog entirely, aside from the explicitly-declined magnifier.
**On-screen verification of double-page spread rendering/pairing, remappable-shortcut keypress
wiring, and auto-scroll behavior — cleared by the user 2026-09-04** (see the "On-screen
verification" section near the top of this backlog); this session's three new items — clock/
battery + "Part X/Y" label, split-page part navigation, and touch center-zone chrome toggle —
user-confirmed live 2026-09-05.

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

**Saved Workspaces shipped 2026-09-03** (design + plan: `docs/superpowers/specs/2026-09-03-library-
saved-workspaces-{design,plan}.md`; committed on branch `claude/library-saved-workspaces`, commit
`65e2a22`, not yet merged). Named, switchable snapshots of everything a browsing screen already
auto-persists — the CE `DisplayWorkspace` equivalent. New `Workspace` entity (`Screen` / `Name` /
`SortOrder` / `IsBuiltIn` / a JSON `StateJson` blob) + `WorkspaceService`, a `▦ Workspace ▾`
switcher pill on **both** the Library and Books toolbars, and a shared naming overlay.
**Deviations from CE, all confirmed with the user:** the lists are **per-screen**, not one global
list; only CE's "Views setup" group is captured (no window-layout or reader-display snapshot —
Paperbunkr already drives reading direction/layout from per-series/per-issue overrides +
`ContentType`); it ships **3 Library + 3 Books read-only starter workspaces** (CE ships none). Apply
is one-shot (CE model) and re-saving is "Save current view as…" with an existing name (overwrites,
exactly like CE's `SaveWorkspaceDialog`) — there is no separate "Update" affordance. Every Library
starter is PosterGrid because it's the only virtualized view mode; a Details-view "Recently added"
starter was cut after it spiked memory on a 2000+ issue library, and `ApplyLibraryState` was
tightened to render the issue list once instead of up to four times. "Comic List" was removed from
the Library view-mode picker in the same change (it duplicated Details; the enum + rendering path
stay for older persisted state). `dotnet-ef` was bumped 8.0.10 → 10.0.11 (the pin mismatched EF
Core 10 and silently mis-scaffolded the enum column). Verified in a clean worktree: builds, 32
workspace ViewModel/service tests + 2 migration tests green; the FlaUI on-screen test is written
but unrunnable in this environment (same UIA barrier as every other UI test here).

**Filesystem folder browsing mode: dropped 2026-09-03** by the user's decision. Paperbunkr already
reaches "comics not yet in the library" through drag-and-drop import, live folder-watch, and
fileless entries; a second persistent browse surface wasn't worth the weight.

**Recent/MRU + Quick Open shipped 2026-09-03** (design + plan: `docs/superpowers/specs/2026-09-03-
quick-open-command-palette-{design,plan}.md`) — a **`Ctrl+P` fuzzy command palette**, not CE's
`File ▸ Open Recent` path-MRU menu nor its `QuickOpenView` recency cover-wall (Home already covers
that). A modal overlay over a scrim: type to subsequence-match any series / issue / book / reading
list / smart list / collection / event / continuity / shell screen, or run an action verb
(`Add folder…`, `Add issue…`, `New reading list…`, `Import from ComicRack…`); `↑`/`↓` to move,
`Enter` to activate, `Esc` to close. Before you type it shows your recently-opened comics + books
(so it also *is* the "recent" list — no separate surface). `QuickOpenService.BuildIndex` runs one
projected `AsNoTracking` query per entity type (single-digit-ms even on a big library, no covers);
`QuickOpenMatcher` is a ~40-line hand-rolled subsequence scorer (score → recency boost → kind
priority, capped at 50). `MainViewModel.ActivateQuickOpenEntry` dispatches on kind, mirroring the
existing `OpenDeepLink`. Bound in `MainWindow`'s tunnel key handler (before the TextBox
early-return, so it fires with a search box focused), inert inside the reader. Scan / Backup /
Check-for-updates were left out of the v1 action set — they're `PreferencesScreenViewModel`
commands, not reachable from `MainViewModel` without lifting them. 25 matcher/service/VM unit tests
+ 6 `MainViewModel` dispatch tests green (full `Paperbunkr.App.Tests` 1550 green); the FlaUI
on-screen test is written but unrunnable here (same UIA barrier).

**Drag-and-drop import shipped 2026-09-03** (design `docs/superpowers/specs/2026-08-31-drag-and-drop-
import-design.md`). One shared `DragImportService` (`Paperbunkr.App/Services`) behind a `Drop`
handler on both the Library and Reading List screen roots (`DragDrop.AllowDrop` on the root `Grid`,
thin code-behind → `DragDropPaths.Extract` → ViewModel). It expands dropped folders to their comic
files (registering each as a `WatchedFolder`, `Watch = false`, exact-path dedup — a dropped folder
is an explicit "this belongs in my library" gesture), buckets loose files by extension
(`.cbl`/`.csv` → new reading list via the existing `CblReadingListIO`/`CsvReadingListIO`, comic
extensions → `LibraryFolderScanner.ImportNewFilesAsync` which already dedupes on `Issue.FilePath`,
anything else → counted skipped), and resolves every dropped comic back to an `IssueId`. The Library
screen reloads the grid + one summary toast; the Reading List screen additionally attaches every
resolved issue as a member of the open list (gated on a new `IsListOpen` — deliberately *not*
`!IsEmptyList` as the design first sketched, so dropping onto a freshly-created empty list works).
No drag-over affordance (matches CE). Out of scope, unchanged from the design: window/reader-level
"drop to open", drag-*out* export, drag-reorder. 8 `DragImportServiceTests` + 4 VM-wiring tests
green (full `Paperbunkr.App.Tests` 1610/1611, the one failure an unrelated book-scan flake that
passes in isolation); real OS-level file-drag is not automatable here — needs a manual on-screen
check by the user.

**File metadata write-back shipped 2026-09-03** (design + plan:
`docs/superpowers/specs/2026-09-03-file-metadata-write-back-{design,plan}.md`). CE parity in shape:
three `AppSettings` toggles in Preferences → Advanced ("Comic File Metadata"), all default **off** —
`WriteMetadataToFiles` (master), `WriteMetadataAutomatically` (auto vs. manual-only),
`WriteNativeSidecar`. Mirrors CE's `UpdateComicFiles`/`AutoUpdateComicsFiles`/`UpdateComicBookFiles`
(all also default false).

- **What's written:** the full `ComicInfo.xml` field set the editors touch, via a new
  `IssueToComicInfoMapper` (Data/CeMigration — the inverse of `CeLibraryMigrator.MapStoryFields`,
  effective values incl. accepted proposals). The file's *current* embedded `ComicInfo.xml` is
  loaded first (`EmbeddedComicInfoReader`), so unmodeled elements survive. Optionally a versioned
  `paperbunkr.json` archive sidecar for fields with no ComicInfo home (tag categories/weights,
  personal rating, IsFinalIssue, `Book*` collector fields) — the deliberate deviation from CE's
  proprietary `ComicBook.xml`.
- **How:** `MetadataFileWriteBackService` + a debounced serial `MetadataWriteBackQueue`
  (`MainViewModel`-owned, like `LiveFolderWatchService`). **`.cbz` only** via
  `System.IO.Compression.ZipArchive` update mode (no page re-encode, temp-copy + atomic swap) plus
  image folders; **`.cb7`/`.cbt` are a visible skip** — the ported `7z u` path needs a `7z.exe`
  Paperbunkr doesn't bundle. Retired the narrow Genre/Tags-only `ComicInfoWriteBackService` +
  `ComicExporter` write path.
- **Triggers** (when both toggles on): Issue Properties, Bulk Issue Properties, Detail
  (content-type + tag reweight), Manga Detail (content-type), Bulk Series (Content Type / Reading
  Mode), Quick Rate — each guarded by a `MetadataFileFieldSnapshot` before/after diff so a no-op
  edit doesn't rewrite the file. Needs-Review proposal-accept deferred (covered by the next edit +
  the manual action). Manual **"Write metadata to files"** on the Library context menu (issue +
  series) and a Preferences "Write all library metadata now" button (CE's `UpdateComics()`).
- Verified: `Paperbunkr.Data.Tests` 753/753, all write-back `Paperbunkr.App.Tests` green (mapper,
  snapshot, sidecar, service round-trip against real synthetic CBZ, queue debounce/gating, the 6
  trigger sites, Preferences persistence, context-menu gating). Real end-to-end file writes are
  headless-testable here; a manual GUI check of the Preferences group is the only flagged gap.

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
per-list sharing). Substantial subsystem, not named anywhere in the
original onboarding.md — needs its own brainstorm → design spec before any implementation starts.
The **background job/task monitor** that CE bundled with this is now decoupled and **shipped for
local jobs** as the Activity Center (`docs/superpowers/specs/2026-09-03-activity-center-design.md`,
committed `504e717`, on-screen verified by the user 2026-09-04; local-only, not yet pushed to
`origin/master` as of this note); surfacing *remote/server* jobs in it is the part still waiting on
this subsystem. The ported CE Engine classes for the sharing side (`ComicLibraryClient` /
`ComicLibraryServer` / `RemoteComicBookProvider` / `NetworkManager`) already exist in
`Paperbunkr.Engine` with zero App-layer call sites — same "ported early, never wired" pattern as
several other pieces; the App-side client/server/UI is what still needs the brainstorm → spec.

### Metadata Model platform (user-supplied `PAPERBUNKR_METADATA_MODEL.md`, 2026-08-17/18)
79-section implementation spec covering canonical metadata, relationships, events/reading lists,
external providers, and recommendations — its own §68 "Migration Strategy" defines 7 phases.
**Phases 1-6a shipped (plus net-new Phases 4d-4g, 2026-08-27), Phase 7 explicitly deferred, plus
the Specials Tab — shipped 2026-09-02, `32ec248`** (not dropped — the source doc itself gates it:
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
  - **Series Detail — Specials Tab** — **shipped 2026-09-02, `32ec248`** ("Series Detail: run
    separator (Volume grouping) + Specials tab"; designed 2026-08-28 following a Kavita comparison
    session — see `event-section-planning` project memory). Design spec
    `docs/superpowers/specs/2026-08-28-series-detail-specials-tab-design.md`. New
    `SpecialFormatCatalog` (Kavita's real special-triggering Format values, intersected with
    CE's actual 16-value list, plus 10 Kavita-only additions bundled into the Format
    autocomplete — CE has none of these, confirmed by grep, so flagged as a deliberate
    addition, not a port) + `Issue.IsSpecial()` extension. New Specials tab on the comic
    `DetailScreen`, hidden when a series has none; pulls Format-flagged
    issues fully out of the Issues tab rather than duplicating them, reusing the Issues tab's
    existing tile templates (extracted to shared `StaticResources` in the same commit).
    **No migration** (reads the already-shipped `Issue.Format`). Deliberately Format-only for
    this phase — Kavita's other two detection mechanisms (no-parsed-`Number` auto-detection,
    `SP##` filename marker) and a manual per-issue override are explicitly out of scope, per the
    design doc. On-screen verified by the user 2026-09-04. **Landed alongside** an unrelated
    "run separator" feature: the Issues tab now groups by `Issue.Volume` with a
    `"{Writer} ({Volume})"` separator bar when a series has more than one distinct run (e.g.
    "Venom (2018)" vs "Venom (2022)"), collapsing to the flat rendering for the single-run case;
    numbering / gaps / completion stats stay series-wide. Both turned out to be display-layer
    work over already-shipped fields — neither needed the deferred Phase 7 schema.
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

**Publisher-based classification shipped 2026-09-02, `254b36e`** — a third fallback branch in the
scanner's classification chain (after embedded `Manga` flag and `LanguageISO`): a new series with
neither hint is classified Comic/Manga/Manhwa/Manhua from its publisher name alone (e.g.
VIZ Media → Manga/RightToLeft) via `PublisherContentTypeClassifier`. Publishers that genuinely span
categories (Dark Horse, Tapas) are deliberately excluded rather than guessed. A periodic re-sweep is
wired fire-and-forget off a new `AppSettings.LastContentTypeSweepUtc`, same shape as the auto-backup
trigger. One migration (`20260902142325_AddLastContentTypeSweepUtc`). This is a heuristic pre-filter,
not the tracker-driven pipeline below — that remains unbuilt.

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
new screen itself — **cleared by the user 2026-09-04**.

Stages 1–4 (the `Track`/`TrackingLink` entity, `ITrackerAdapter` abstraction with AniList/
MyAnimeList/Shikimori/Bangumi adapters, one-way sync-to-tracker, and `CredentialStore`-backed token
handling) were already implemented in the codebase — surfaced while researching this session's
work, not built by it. MangaUpdates/Kitsu adapters, two-way sync, and Stage 5's statistics
dashboard remain unbuilt.

**MangaUpdates tracker adapter shipped 2026-09-05** — the 5th of the 6 originally-scoped services.
MangaUpdates publishes no public API reference; verified against Mihon's real
`MangaUpdatesApi.kt`/`MangaUpdates.kt`/DTO source (fetched live via `gh api` against
`mihonapp/mihon`) rather than guessing the shape, per this project's standing rule to verify before
assuming. Auth is its own username/password → session-token login (`PUT /v1/account/login`), not
OAuth and not a pasted PAT like Bangumi/MangaBaka — the password is used once and never stored,
only the returned `session_token` (kept in `CredentialStore` under `CredentialKind.OAuthAccessToken`,
same "bearer token" storage kind AniList/Shikimori use for their own tokens). `PushEntryAsync`
mirrors the real API's own two-call shape confirmed from Mihon's source: a `GET /lists/series/{id}`
existence check, `POST /lists/series` to add a series not yet on any list (that endpoint doesn't
accept a status payload), then always `POST /lists/series/update` to set list/chapter — so a
first-time push costs three requests, a repeat push two. `MangaUpdatesListMapper` maps
`ReadingStatus` to the service's five list IDs (Reading=0/Wish=1/Complete=2/Unfinished=3/OnHold=4,
confirmed against Mihon's companion constants); no dedicated re-reading list exists on this service
(Mihon's own `getRereadingStatus()` returns "unsupported"), so `ReReading` collapses into Reading,
same lossy-collapse precedent as `BangumiCollectionTypeMapper`. Wired into `DetailTabsViewModel`'s
tracker-service picker/search/sync-loop and a new Preferences username/password connect field
(`ConnectionsSection.axaml`) — `MangaUpdates` already had a `MU` `BrandMark` abbreviation registered,
so no new iconography needed. 14 new `Paperbunkr.Data.Tests` (770 total in that project, all
green); `Paperbunkr.App.Tests` DetailTabsViewModel/PreferencesScreenViewModel suites (130 tests)
green. On-screen verification of the new Preferences connect UI is the standing gap, same caveat as
every other tracker's connect flow.

**MangaDex metadata provider shipped 2026-09-05** — closes the "MangaDex second provider" item that
`2026-08-19-metadata-model-second-provider-mangadex-design.md` had explicitly sketched-only ("build
later"). Built to that spec almost exactly as sketched: `MangaDexMetadataProvider` mirrors
`AniListMetadataProvider`'s shape (throws `MetadataProviderUnavailableException` on a failed call,
rather than `MangaBakaMetadataProvider`'s looser "return empty" behavior — matches the documented
contract on `IMetadataProvider` itself) but calls MangaDex's REST API (`GET /manga?title=`,
`GET /manga/{id}`, both unauthenticated) instead of AniList's GraphQL. Rate-limited to one request
per 400ms (~2.5 req/s), under MangaDex's documented ~5 req/s/IP global limit (re-verified live this
session, matches the figure already recorded in `docs/open_items_resolved.md` §2 before this
provider existed) — same conservative-under-the-limit posture as every other provider in this file.
Title normalization handles MangaDex's richer language-keyed `title`/`altTitles` maps (checks the
primary map first, then the first matching entry in the plural `altTitles` array — "first title per
language only" per the spec's own open question) into the same `TitleEnglish`/`TitleRomaji`/
`TitleNative` fields `MetadataLinkResolver` already consumes, so `MetadataLinkResolver`/
`TitleMatchScorer` needed zero changes, exactly as the spec predicted. `Genre` only pulls
`group: "genre"` tags (MangaDex separately tags theme/format/content) so it doesn't over-broaden
what this codebase treats as Genre. `ChapterCount`/`VolumeCount` are left null - MangaDex doesn't
expose per-series totals on this endpoint (would need `/manga/{id}/aggregate`, deliberately out of
scope - a different, larger feature from onboarding.md §9's own deferred "chapter/volume alignment"
item, not started). No schema change (`ExternalMetadataProvider.MangaDex` already existed, unused).
UI is one line: added to `DetailTabsViewModel.MetadataProviderOptions` and `GetMetadataProviderFor`
- the picker in `DetailTabs.axaml` is a plain `ComboBox` bound to that static array, so the new
option appears with no XAML change. Metadata-only, deliberately not an `ITrackerSearchProvider`/
`ITrackerAdapter` like AniList/MangaBaka - MangaDex has no authenticated per-user library API to
push progress to. 10 new `Paperbunkr.Data.Tests` (780 total in that project, all green);
`DetailTabsViewModel` suite (55 tests) green. On-screen verification of the new provider-picker
entry is the standing gap, same caveat as every other metadata/tracker UI here.

**Kitsu tracker adapter shipped 2026-09-05** — the 6th and last of the originally-scoped services,
closing out the tracker-sync half of this section entirely (Stages 1-4 now cover all six).
GraphQL (`https://kitsu.app/api/graphql`), confirmed against Mihon's real `KitsuApi.kt`/`Kitsu.kt`/
DTO source. **Real wrinkle, resolved with the user before building:** Kitsu has no self-serve
third-party app registration, so unlike AniList/MyAnimeList/Shikimori (each user registers their
own OAuth app), there's no client_id/secret a Paperbunkr user could generate themselves. Every
open-source manga reader that supports Kitsu (Mihon, Komikku, forks) ships the same hardcoded
credential pair; the user explicitly chose to reuse it (2026-09-05) rather than skip the adapter,
understanding it isn't Paperbunkr's own registered app and Kitsu could revoke/rate-limit it without
notice — the one tracker here with that specific risk called out. Login is OAuth2 password grant
(`POST /api/oauth/token`, form-encoded username/password/grant_type=password/client_id/
client_secret) storing both `access_token` and `refresh_token`. Unlike every other tracker's search
(public, or gated only by a Client ID header for MyAnimeList), Kitsu's GraphQL API requires a full
bearer token for search too — confirmed from Mihon's own `search()` using its authenticated client
— so `KitsuTrackerAdapter.SearchAsync` returns empty (not connected yet, not a provider failure,
same idiom as MyAnimeList's null-Client-ID case) rather than working pre-connection.
`PushEntryAsync` mirrors Kitsu's real two-mutation shape: a `findMangaById { myLibraryEntry { id } }`
query to check for an existing library entry, then either an `AddManga` or `UpdateManga` GraphQL
mutation - handling Kitsu's own "two different error shapes on HTTP 200" quirk (a transport-level
`errors`/`error` alongside a separate mutation-payload-level `errors` nested under the result,
confirmed from Mihon's own doc comment on this). `KitsuStatusMapper` maps to Kitsu's
`LibraryEntryStatusEnum` (`CURRENT`/`PLANNED`/`COMPLETED`/`ON_HOLD`/`DROPPED`); no dedicated
re-reading status exists on this service (Mihon's own `getRereadingStatus()` returns "unsupported"),
so `ReReading` collapses into `CURRENT`, same lossy-collapse precedent as
`BangumiCollectionTypeMapper`/`MangaUpdatesListMapper`. Wired into `DetailTabsViewModel`'s
tracker-service picker/search/sync-loop and a new Preferences username/password connect field
(`ConnectionsSection.axaml`) — `Kitsu` already had a `KT` `BrandMark` abbreviation registered.
17 new `Paperbunkr.Data.Tests`; `DetailTabsViewModel`/`PreferencesScreenViewModel` suites (132
tests) green. On-screen verification of the new Preferences connect UI is the standing gap, same
caveat as every other tracker's connect flow. **Unrelated to this adapter, found while running the
full suite:** 7 pre-existing `Paperbunkr.Data.Tests` migration round-trip tests
(`LibraryDetailsColumnsMigrationTests`, `AddBookReaderErgonomicsAndAnnotationsMigrationTests`,
`ReworkBookHighlightAnchorMigrationTests`, `AddLastContentTypeSweepUtcMigrationTests`,
`AddFb2MobiBookFormatMigrationTests`, `AddBooksBrowseStateMigrationTests`, one more) are currently
failing (`no such column: "LibraryGroupField"` on an up/down round-trip) - not caused by this
session's changes (nothing here touches migrations, `Issue.cs`, or the model snapshot); most likely
from a concurrent session's in-progress `AddIssueDuplicateAcknowledged` migration work sitting
uncommitted in the same working tree. Flagged, not fixed - not this session's scope to untangle
another session's in-flight migration.

**Two-way tracker sync shipped 2026-09-05** (design: docs/superpowers/specs/2026-09-05-two-way-
tracker-sync-design.md) — the last of the two remaining items in this section, and the reason
`ITrackerAdapter.cs`'s own doc comment had been reserving a spot for a `GetEntryAsync` method since
2026-08-23 ("Phase A scope only... add it back only when a Phase B pull/bidirectional spec actually
needs it" - this is that spec). **Two real decisions the user made before this was built, not
assumed:** (1) a pulled remote chapter-progress number auto-marks local `Issue`s read up to that
number (via the existing `IssueReadStateResolver.MarkAsRead`, which already safely no-ops on an
issue whose `PageCount` isn't known yet) rather than only being displayed — accepted with the
caveat that `Issue.Number` isn't guaranteed to line up with a tracker's own chapter numbering
(TPB folding, variant issues); (2) conflicts resolve silently to "whichever side is further along"
(`TrackerSyncResolver.RemoteWins`, extending Komikku's own "keep the higher last-read chapter" rule
to also cover `ReadingStatus` as a tie-break when progress is equal) rather than a confirmation
prompt — even though the original research doc had recommended a prompt if two-way sync were ever
built.

`GetEntryAsync` is now implemented on all 7 tracker adapters (the six originally scoped plus
MangaBaka), each verified against a real source rather than guessed: AniList uses `Media.
mediaListEntry` (confirmed against AniList's own docs - no numeric user id lookup needed, simpler
than Mihon's own `Page.mediaList(userId:, mediaId:)` approach); MyAnimeList re-fetches `/manga/{id}?
fields=my_list_status{...}`; Shikimori/Kitsu/MangaUpdates each got their existing "does an entry
already exist" push-side lookup extended to also return status/progress, so pull costs no extra
HTTP round trip beyond what push already made; Bangumi and MangaBaka each gained a new `GET` call
(`/v0/users/-/collections/{id}` and `/v1/my/library/{id}` respectively) that push never needed
before. `SyncToTrackersAsync` (button relabeled "Sync with Trackers") now calls `GetEntryAsync`
first for each link, applying `TrackerSyncResolver`'s verdict before falling back to the existing
push path - local progress/status are recomputed fresh before each link's comparison rather than
once up front, so a pull applied earlier in the same pass is visible to the next link. A pulled
mark-as-read swaps the same `Issues`/`Specials` tiles and fires the same `_onSelectionChanged`
callback the manual mark-as-read context-menu action already uses, so the Detail screen's
unread-count hero doesn't go stale. 32 new `Paperbunkr.Data.Tests` (`TrackerSyncResolverTests` plus
`GetEntryAsync`/reverse-mapper cases across all 7 adapter test files); full suite 880 total, 873
green (the 7 pre-existing unrelated migration failures noted above, unchanged by this work);
`DetailTabsViewModel`/`PreferencesScreenViewModel` suites (132 tests) green. On-screen verification
of the new pulled-progress behavior (does a real mark-as-read actually show up in the Issues tab
after a sync) is the standing gap, same caveat as every other tracker UI here - doubly so here since
it's the one feature in this file that mutates local library state as a side effect of a sync
action, not just tracker-side state.

**Gap found 2026-08-23, closed same day** (this section previously said "deferred" — stale, fixed
here): linking a series to external metadata (AniList or MangaBaka) had never applied the fetched
data to any editable field. `MetadataLinkResolver.LinkAsync` only ever wrote an `ExternalMediaId`
reference, an audit-log `ExternalMetadataSnapshot`, and alt-titles — `Description`/`Status`/
`ChapterCount`/`VolumeCount` were fetched and stored but never read back anywhere. Closed same
session via a real "Apply from [Provider]" feature (design spec `2026-08-23-apply-from-provider-
design.md`), built around the existing `MetadataProposal` accept/reject system rather than a
parallel one: `MetadataProposal.IssueId` widened to nullable, new nullable `SeriesId`/`ProviderKey`
columns (one polymorphic table), `MetadataProposalField` gained `Summary`/`Status`/`Genre`.
Series-scoped proposals auto-accept-and-overwrite in `MetadataLinkResolver.LinkAsync` (the user's
own choice over a "pending, needs review" default) since there's no filename-vs-XML race to
arbitrate for these fields. A new `SeriesStatusNormalizer` maps provider status strings to
`SeriesStatus`, unrecognized → `Unknown` rather than clobbering a good stored value.

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
cover-art entry points, the MangaBaka provider picker) — **cleared by the user 2026-09-04**.

### Plugin API v2 (onboarding.md §10) — engine + 4 real hooks shipped 2026-08-24, rest of the hooks + all 3 UI surfaces closed 2026-09-05
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
**Explicitly deferred, not yet wired to a live UI trigger (2026-08-24):** Editor/Books/NewBooks/
CreateBookList (sidebar)/ParseComicPath/NetSearch/ConfigScript/ReaderResized hooks (engine supports
them, no menu/lifecycle anchor wired to a real screen yet — `IBrowser.SelectComics` is a documented
no-op since Library grid's selection model isn't exposed for plugin control yet); the three net-new
UI surfaces the design spec called for (ComicInfoHtml/UI info panel tab, QuickOpenHtml/UI command
palette, DrawThumbnailOverlay paint hook) were not built this session despite being in scope on
paper — full skeleton for all 17 hooks plus all 3 stub surfaces is a larger follow-on, not a quick
add-on.

**Follow-up (2026-09-05) — every deferred item above closed, plus one real bug fixed:** design spec
`2026-09-05-plugin-api-v2-remaining-hooks-plan.md`, grounded against `_reference/ComicRackCE` for
every anchor point. **Bug found by this session's own audit:** `BookOpened` was listed above as one
of "4 hooks with a real live trigger" but was actually a dead wire —
`ReaderScreenViewModel.IssueOpened` fired, nothing subscribed — now genuinely wired in
`App.axaml.cs`. **Hooks:** `ReaderResized` (reader canvas size-changed); `Editor` (Issue Properties
+ Bulk Editing overlay toolbars, one menu entry per enabled command — CE's own per-command
File-menu-item shape, not Library's single-hardcoded-label shortcut); `Books` (Books screen context
menu — needed its own `NovelBooksHookGlobals` since that screen's `Book` entities are a wholly
separate schema from `Issue`, a type mismatch the original spec's shared `BooksHookGlobals` would
have had); `NewBooks` (peer "Add via {command}" buttons in Library's Add-issue overlay, mirroring
CE's peer File-menu items rather than replacing the manual flow); `CreateBookList` (a real Smart
Lists sidebar section — genuinely can't reuse `SmartListQueryBuilder`'s DB-row model since a
plugin-backed list has no `SmartList` row, so this is parallel plumbing, not a shim); `ParseComicPath`
(`LibraryFolderScanner`, first non-null override wins, no live plugin uses it yet so scan behavior
is unchanged); `NetSearch` (Detail's Apply-from-Provider picker generalized via a new
`MetadataSearchProviderOption` wrapper so a plugin entry can sit alongside AniList/MangaBaka without
touching the persisted `ExternalMetadataProvider` enum — search works for a plugin match, linking
one doesn't, since there's no enum slot to persist it against, and says so rather than pretending);
`ConfigScript` (Plugin screen gear icon — `Command.Configure` pairing already existed from
2026-08-24, only the click-to-invoke action was missing). **UI surfaces:** `ComicInfoHtml`/`UI` (a
new "Plugins" tab on Detail's tab strip, scoped to the single focused issue — CE's own anchor is a
per-comic Library-explorer sidebar panel, which Paperbunkr has no equivalent of); `QuickOpenHtml`/
`UI` (extends this app's own independently-built Ctrl+P palette — CE's literal "QuickOpen" is a
recently-opened-books grid with attached info panels, which doesn't map onto this UI at all, so
this is a deliberate adaptation, not a stopgap); `DrawThumbnailOverlay` (new
`AsyncPluginOverlayImage`, shaped exactly like `AsyncCoverImage`'s off-UI-thread-decode/cache
pattern — CE invokes this hook live, per paint, via a raw GDI+ callback with no Avalonia
equivalent, and firing a Roslyn script synchronously on every tile repaint in a virtualized grid
would undo the work `AsyncCoverImage` itself exists to fix, so this trades live-per-paint for a
one-decode-per-issue cache; wired into the primary Poster grid tile only, not every one of
Library's other view modes' cover images, since no live plugin implements this hook yet).
**Testing:** a new "Hook Coverage" sample plugin (`src/Paperbunkr.Plugins.Tests/SamplePlugins/
HookCoverage/`) + 14 new `Paperbunkr.Plugins.Tests` invoke every one of the hooks above end-to-end
through the real `PluginEngine` — closes "no live sample plugin exercises this hook", the exact gap
the 2026-08-24 audit flagged for ParseComicPath/NetSearch/ConfigScript specifically. Plus
`AsyncPluginOverlayImageTests` (generation-guard, same shape as `AsyncCoverImageTests`). Verified:
full solution builds clean; 362 targeted `Paperbunkr.App.Tests` + 43 `Paperbunkr.Plugins.Tests`
green (was 29 before this session). **Separately, this session also merged the
`plugin-api-gap-closure` branch** (pushed 2026-08-31, sitting unmerged) — the three
`IApplication`/`IBrowser` automation gaps (`AddNewBook`/`GetOrCreateSeriesId`, the four comic icon
methods + `GetComicFields`, a real `SelectComics`) plus IronPython plugin scripting
(`PythonCommand`) alongside the existing C# `.csx` path. **Not done this session:** on-screen GUI
verification of any of the above — same standing caveat as every other backlog item that ships
without a live click-through pass.

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
applying to a fresh DB; app smoke-launched to "Startup complete". On-screen GUI pass cleared by the
user 2026-09-04.

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
cleared by the user 2026-09-04.

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
"Startup complete". On-screen GUI pass cleared by the user 2026-09-04.

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
column-picker menus) — **cleared by the user 2026-09-04**.

### App-wide & library keyboard shortcuts, sidebar arrow-key movement (shipped 2026-08-31)
Design spec: `2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md` (no separate plan doc —
user asked to implement directly). Started as a review of an external draft spec the user dropped
into the session proposing to (re)build keyboard control from scratch; most of it turned out already
shipped (reader shortcuts + `KeyboardCommandRegistry` + remapping UI, card-grid arrow nav). What
survived review, plus sidebar movement added after the user flagged it as something they specifically
wanted from the draft:

- **App-wide** (`MainWindow.axaml`'s `Window.KeyBindings`, same mechanism as the existing Escape/Back
  entries, not `KeyboardCommandRegistry` which is deliberately Reader-scoped): `Ctrl+,` → Preferences
  (`GoPreferencesCommand`, already existed), `Ctrl+Tab`/`Ctrl+Shift+Tab` → cycle the 7 `RailOrder`
  screens via two new `MainViewModel` commands that dispatch to each screen's own existing `Go*`
  method (not a raw `CurrentScreen` set) so cycling keeps the same load/history-reset/unsaved-editor
  guard a rail click gets.
- **Library grid** (`LibraryScreen.axaml`'s `UserControl.KeyBindings`, same mechanism as its existing
  `Ctrl+I`): `Ctrl+A`/`Delete` via two new granularity-dispatching `LibraryScreenViewModel` commands
  (`SelectAllVisibleCommand`/`DeleteCurrentSelectionCommand`) that just point at the existing
  select-all/delete commands the context menu and action bar already use — no new selection or
  deletion logic. `/` focuses the search box and Esc-with-text now clears the query and returns focus
  to the grid (previously Esc only closed the suggestions popup) — both via a new
  `LibraryToolbar.FocusGridRequested` event, since the toolbar has no reference to its sibling grid.
- **Sidebar arrow-key movement** — genuinely new, not covered by the comprehensive-keyboard-
  operability spec above (which explicitly excluded the sidebar, reasoning Tab-order alone was
  enough). One `KeyDown` handler on `MainWindow`'s contextual-sidebar `Border`, reusing
  `GridKeyboardNavigation.Navigate`'s existing pure core unchanged (a single-column list's Up/Down
  row-search degenerates to plain previous/next) with a new live wrapper collecting
  `Button.sideItemButton` descendants, since these are hand-authored buttons in a `StackPanel`, not
  an `ItemsControl` `TryHandleArrowKey` can attach to.

Real finding during implementation, not anticipated by the design doc: **`GridKeyboardNavigation.Navigate`'s
Left/Right case is plain previous/next-in-list index math, not spatial column math** — for a
single-column sidebar that's identical to what Up/Down already do, which would make Left/Right
silently move focus too if wired the same way as the grids. Deliberately left unwired for the
sidebar (only Up/Down/Home/End), found by reading `Navigate`'s actual implementation rather than
assuming symmetry with the card-grid case.

Explicitly out of scope, flagged rather than silently dropped: command palette, type-ahead-by-letter,
`Shift+`arrow range-select, and a `Ctrl+Q` quit binding (no evidence anything needs it beyond the OS
default) — each is real interaction/design work, not plumbing.

Tests: 10 new (`MainViewModelTests` cycle-forward/back incl. wraparound and drill-down no-op;
`LibraryScreenViewModelTests` Ctrl+A/Delete dispatch-by-granularity incl. empty-selection no-op) —
192/192 green in the filtered run. The sidebar `KeyDown` handler and the `/`/Esc code-behind wiring
have no unit tests, matching this codebase's existing convention that View-code-behind `KeyDown`
handlers (`OnCardKeyDown`, `OnLibraryScreenKeyDown`, etc.) aren't unit-tested elsewhere either. App
smoke-launched via `PowerShell Start-Process`, confirmed running 6+ seconds later. On-screen
keyboard-feel verification (all six new gestures, sidebar Up/Down across all four screens including
Library's grouped-with-headings layout and the inline-rename-shouldn't-steal-focus case) still
pending — same standing caveat as the spec above.

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
Plugins.Tests 16/16, no regressions. On-screen GUI pass cleared by the user 2026-09-04.

### Auto-update + changelog + customized installer (shipped 2026-09-01, `32d82bf`) — 0.2.0-beta
Design/plan: `docs/superpowers/specs/2026-09-01-auto-update-and-changelog-{design,plan}.md`.
**In-app auto-update via `NetSparkleUpdater.SparkleUpdater`** — checks for new releases on startup
and on demand from Preferences → About, downloads and applies updates in-app. Installer-agnostic, so
the P7 Inno Setup installer is unchanged; NetSparkle just downloads and runs it. New
`.github/workflows/release.yml` (tag-triggered): builds via `installer/BuildInstaller.ps1`, then
generates and Ed25519-signs an `appcast.xml` via `netsparkle-generate-appcast`, uploaded alongside
the installer to the GitHub Release (one real bug fixed post-ship, `f0a5878` — the appcast generator
was silently skipping signing when `SPARKLE_PUBLIC_KEY` was unset). A hand-authored `CHANGELOG.md`
(repo root, Keep a Changelog) is rendered in a new Preferences → About section (`ChangelogParser`);
the installer's pre-install "what's new" page is generated from the same file. The installer also
gained optional "Launch at Windows startup" / "Associate comic-manga files" tasks, Restart Manager
integration, and an opt-in delete-my-library-data prompt on uninstall (backed by a headless
`--register/--unregister-file-associations` CLI mode).

**This is the alpha → beta cutover.** The `v0.2.0-beta` git tag exists and `CHANGELOG.md`'s
`[0.2.0-beta]` section documents the release; README / wiki / GitHub Pages landing page are updated
to beta. The `docs/alpha-*.md` filenames are now historical — the backlog itself is still live, just
no longer "alpha". **Still open:** an end-to-end check that a real tagged release actually produces a
working signed appcast and that a prior install upgrades itself from it — the tag exists but nobody
has watched an older build pull `0.2.0-beta` down and reinstall.

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

**Navigation transition system shipped 2026-09-05** (sub-project 1 of "full app chrome animations" -
sub-project 2, chrome/content motion polish, not yet started), design spec + plan
`2026-09-04-navigation-transition-system-{design,plan}.md`, superseding/extending the 2026-08-24
navigation-shell-motion spec's own deferred drill-down motion. The six drill-down screens (Detail/
MangaDetail/Reader/BookDetail/BookReader/PdfReader) move off instant-cut `IsVisible` toggles onto one
`TransitioningContentControl` with a push/pop cross-fade (`PbDrillTransition`, direction from a new
`MainViewModel.IsDrillTransitionReversed`), and Library ↔ Detail ↔ Reader all get a real
shared-element cover flight - a floating clone flies from the grid tile to the hero (or the Reader's
first page) and back, via a new `SharedElement` attached-property pair +
`ISharedElementTransitionService` + `NavigationTransitionCoordinator`. Every Library view mode
(Poster/Panorama/List/Details/Tiles × issue+series granularity, 10 templates) is wired, plus a
back-trip `ScrollIntoView` realization (`LibraryScreenViewModel.RequestScrollIntoView`) so a
scrolled-away grid tile gets scrolled back into view before the flight looks for it. Only
`CollectionTileTemplate` (the mixed series/issue/book Collections grid) stays unwired - a real,
narrow follow-up if ever wanted. Reader's own participation turned out cheaper than first assessed:
`PageCanvas` already had a public `Page` bitmap property, already bound - the destination rect is
just `PageCanvas.Bounds` (whole control), not a computed zoom/fit-mode-aware sub-rect.
`SharedElementFlightMath` (5 tests), `SharedElementTransitionService` (5 headless tests, two
Avalonia-headless gotchas found and worked around: no dispatcher loop to re-layout after a tree
mutation, and a bare `async Task` xUnit test's `await` hopping off Avalonia's owning thread and
tripping the compositor's thread-affinity check - both documented inline), `NavigationTransitionCoordinator`
(4 tests), and 6 more covering `RequestScrollIntoView`/`ReaderScreenViewModel.SharedElementKey` are
new automated coverage; the actual on-screen motion feel is **not yet verified** - no unattended GUI
automation in this environment, same standing `[[feedback_no_computer_use]]` limitation as
everywhere else in this project.

### Novels: EPUB/PDF support (Phase 1+2 landed 2026-08-09/10, Phase 3 landed 2026-08-10)
Not a CE-parity item — ComicRackCE has no prose-reading equivalent, see the design spec's own
CE-verification note. Design: docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md.

**FB2 + MOBI/AZW3 ingestion shipped 2026-09-02, `b9f74f5`** (plan
`docs/superpowers/specs/2026-09-01-books-format-ingestion-fb2-mobi-plan.md`): new `Fb2BookSource`
and `MobiBookSource` (hand-rolled PalmDB reader, PalmDoc decompressor, MOBI header parsing, CP1252
decoding) behind the existing `IBookTextSource`, wired into `BookFolderScanService` /
`BookTextSourceFactory`, plus the `BookFormat` schema migration and the Library/reader UI updates
that depend on it. **The Books/PDF reader was also reworked 2026-09-03** (`51b51c3`, PR #38):
WebView-based reflow renderer, a shared HUD, and a touchpad-scroll fix — see the Books reader
ergonomics project memory / `2026-09-*-books-reader-*` specs. **OPDS catalog client** (Kavita/Komga)
is design-only so far (`33ad867`).
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

### Preferences: Behavior / CE-parity toggle remainder

Second Behavior batch (`RestoreSessionOnStartup`, `ScanFoldersOnStartup`, `PromptReviewOnFinish`,
`EnableDragDropImport`) **shipped 2026-09-04**, uncommitted
(`docs/superpowers/specs/2026-09-04-behavior-settings-batch2-design.md` +
`-plan.md`; migration `AddBehaviorSettingsBatch2`). Four checkboxes on Preferences → General
(Startup / Reading / Library groups). That spec's §5 leaves these CE `Settings` checkboxes still
deferred:

- **`AddToLibraryOnOpen`** ("Opened Files are added to the Library") — *blocked on a prerequisite*.
  Paperbunkr has no shell-open-a-loose-file path: `ShellRegister.RegisterFileOpen` writes
  `"…\Paperbunkr.exe" "%1"` but `App.axaml.cs` only parses `--open <kind>:<id>` and ignores a bare
  file-path arg (startup falls through to restore-on-launch). Needs: handle a path arg → open it in
  the Reader (import-on-demand or transient book), *then* an add-to-library toggle has something to
  gate. Own small feature + brainstorm.
- **`HideCursorFullScreen` / `AutoMinimalGui`** — Paperbunkr already auto-hides chrome *and* cursor
  after ~3s idle in every reading mode (`ReaderScreenViewModel.ShowChrome` + `NotifyCursorActivity`,
  no `IsFullscreen` gate). A toggle would only re-expose the pre-unification "cursor always visible
  in windowed mode" split — build only if a user asks for it.
- **Cosmetic browser micro-toggles** — `FadeInThumbnails`, `CoverThumbnailsSameSize` (mostly
  already the PosterGrid-vs-Panorama view-mode choice), `DogEarThumbnails`, `ShowToolTips` (library
  tile tooltips aren't built), `NumericRatingThumbnails`, `ExportedListsContainFilenames`. Low
  value; revisit only on request.

### Deferred / dropped (no action needed)
- **News reader** (`Help > News` RSS) — deferred, live idea to repurpose the feed mechanism for
  something Paperbunkr-relevant; needs its own brainstorm before scoping
- **Export to another format** — dropped, format conversion is a separate concern from this app
- ~~**GitHub self-updater** — dropped for now, revisit once there's a real release/distribution
  pipeline~~ — **superseded**: in-app auto-update shipped 2026-09-01 (`32d82bf`) on
  `NetSparkleUpdater` + a tag-triggered GitHub Release / appcast pipeline. See the "Auto-update"
  section above.
- **Portable device / wireless sync** — excluded, feature doesn't exist in Paperbunkr's model
- **Multi-tab/multi-window book-open (CE's MDI model)** — confirmed non-goal, single-screen
  rail-nav is the deliberate design target (Mihon/Komikku-style)
