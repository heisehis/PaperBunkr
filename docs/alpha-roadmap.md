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

**Still open, not part of this slice:** page layout (single/double-spread/adaptive), magnifier
overlay, page transition animations, fullscreen/minimal-chrome mode, on-screen overlays (scrubber,
page/status text, clock/battery), live image adjustment (brightness/contrast/saturation/gamma),
background/texture/margins, **continuous/webtoon vertical scroll** (genuinely new, not CE parity —
also the highest-risk unproven piece per onboarding.md §8's memory-management warning), split-page
part navigation, touch gestures beyond what's already shipped, remappable keyboard shortcuts,
auto-scrolling/hands-free mode. A Preferences → Reader tab (to make the fit-mode/auto-rotate
global defaults user-editable, currently fixed code constants) is also still open.

### Metadata editing extras
Copy/paste fields between books, templated/token text field editor, Quick Rating + free-text
Review popup (Review isn't a schema field yet), undo/redo for metadata edits, per-page type
tagging (cover/story/ad/deleted), per-page persisted rotation override, named bookmarks (distinct
from `LastPageRead`).

### Library browsing extras
Filesystem folder browsing mode (not just library-backed), browse history (back/forward), saved
Workspaces (display-setting presets), saved List Layouts (grid/sort/group presets — `LibraryScreen`
already has decorative UI stubbed for this), pluggable sort/group strategies, drag-and-drop import,
Recent/MRU + Quick Open overlay, reveal-in-Explorer, live folder-watch scanning (`FileSystemWatcher`,
vs. today's scan-now only), file metadata write-back (edits saved into on-disk ComicInfo.xml/tags —
real risk surface, mutates user files), fileless book entries (catalog a physical book with no file).

### Reading Lists: story-arc auto-build (CBL Manager port)
Currently-nonfunctional "AniList"/"MyAnimeList"/"Auto-Build from Tracked Arc" buttons on the
Reading Lists screen — Paperbunkr's own `.cbl` import/export (`CblReadingListIO`) already works,
but nothing powers arc auto-build. `_reference/CBLManager/` is a real, separate ComicRack CE
plugin (not part of Paperbunkr, same reference/porting treatment as `_reference/ComicRackCE`) that
already does this against real sources: ComicVine, Metron, Comic Book Reading Orders,
ReadingOrders.com — auto-pulls an arc's issues in reading order, matches against the owned
library, and adds fileless placeholders for what's missing. Note: those sources don't match
Paperbunkr's current UI labels ("AniList"/"MyAnimeList") — needs its own brainstorm → design spec
(source selection, credentials, matching logic) before implementation, not a quick wire-up.

### Remote/server library sharing
Client (connect to another instance's shared library) + server (host, password-protected,
per-list sharing) + background job/task monitor. Substantial subsystem, not named anywhere in the
original onboarding.md — needs its own brainstorm → design spec before any implementation starts.

### Content-type classification & manga metadata scraping (onboarding.md §7/§9)
Tracker-driven classification pipeline (MangaUpdates/AniList), MangaDex metadata scraping,
search-and-confirm UI shared across classification/tracking/scraping. Explicitly Beta-scoped from
the start.

### Plugin API v2 (onboarding.md §10)
C# scripting initializer first, `pythonnet` interop as a follow-on spike. Needs a real test plugin
to prove against. Explicitly Beta-scoped from the start.

### App chrome
Crash reporter dialog, minimize-to-tray, external "open with" app associations.
*(Backup manager and file association are already shipped as part of Alpha's Advanced tab.)*

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
