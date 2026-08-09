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

## What's left (as of 2026-08-09, HEAD `3e7ada3`)

> This section drifted before: it was last hand-written at `7e2d3d3` and had already fallen behind
> five real commits by the time anyone reopened it. That's the whole reason for the live tracker —
> see [Live tracker](#live-tracker) below. Treat this file as re-synced as of `3e7ada3`; if you're
> reading it later than that commit, check the tracker artifact or `git log` before trusting it.

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
  and fixed 2026-08-09 evening — see below.**
- ~~**New, not yet scoped:** Book Folders scan reads filenames only~~ — **done**, see below.

**Real bug found + fixed today (2026-08-09 evening session, not yet committed) — a crash, not a
cosmetic gap:** `3e7ada3`'s LRU-bounding of `CoverImageCache` disposed evicted `Bitmap`s eagerly,
but `Get()` hands the exact same `Bitmap` instance to view models that bind it straight into a
still-visible `Image` control — browsing a large library (2000+ issues) evicts bitmaps still
on-screen elsewhere, and the next layout pass throws `ObjectDisposedException` out of
`Image.MeasureOverride`. Real repro: browse Library, then open Smart Lists → crash. Fixed in
`LruCache.cs` — eviction now only drops the cache's own reference, not an explicit `Dispose()`;
native memory still gets reclaimed via GC once nothing else references it. The test that had
asserted the old (unsafe) dispose-on-evict behavior now asserts the opposite. Confirmed fixed via a
live repro (Library → Smart Lists, no crash) and the full 312-test suite.

**Icon-pack sweep today (2026-08-09 evening session, not yet committed):** every screen swept for
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
2026-08-09 evening session, not yet committed):
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
  still present as of `3e7ada3`. Its two commits (PageCanvas focus fix, Virtual Tags wiring)
  predate and are superseded by `8e1bf55`'s versions of the same fixes. Also has an uncommitted
  edit to `LibraryFolderScannerTests.cs`. Safe to discard once confirmed nothing else is needed
  from it.
- ~~`docs/alpha-roadmap.md` uncommitted edit~~ — resolved; working tree is clean as of `3e7ada3`.

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
      2026-08-09 evening, not yet committed**
  - [x] `Assets/avalonia-logo.ico` replaced with a real Paperbunkr mark (user-supplied artwork,
        flood-filled to transparent + packed into a multi-res `.ico`); wired as both the window
        icon (`MainWindow.axaml`) and `ApplicationIcon` in `Paperbunkr.App.csproj` (the exe/taskbar
        icon, which the old setup never set at all)
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
      saves, and cancels correctly from all entry points — **audited 2026-08-09 evening, not yet
      committed.** Traced (not just read commit messages) every navigation entry point and the
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
    251/251 passing). Not yet manually clicked through in the live app — no desktop GUI automation
    available in this environment (same limitation noted for the Reader gestures below).
- [x] One more pass across all screens to confirm nothing was missed — **swept 2026-08-09 evening,
      not yet committed.** Structural search (not a manual click-through, see the note under the
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

---

## P7 — Known gaps: appshell + alpha build packaging ✅ Done

- [x] **Build/configure the appshell (installer) project** — **done 2026-08-09 night, not yet
      committed.** Packaging approach: **Inno Setup** (`installer/Installer.iss` +
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
- Remaining Reader polish (fit modes, page layout, rotation, magnifier, transitions, fullscreen,
  overlays, live image adjustment, continuous/webtoon scroll, split-page nav, remappable shortcuts,
  auto-scroll) stays Beta scope, unchanged.

---

## Explicitly not in scope here

- **Content-type classification manual dropdown** — flagged as a known gap, but the real
  auto-classify pipeline (§7/§9) is scoped as Beta work. No Alpha-side fix needed beyond what's
  already shipped; leave the manual dropdown as-is until Beta.

---

*Beta backlog is tracked in [`alpha-roadmap.md`](alpha-roadmap.md) and not duplicated here.*
