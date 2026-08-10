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

**Open:**
- **P4** — one item left: the app icon. See above.
- **P6** — see below; likely close to done, not confirmed done.
- **P7** — appshell + installer packaging. Not started (no installer project exists in the repo).
- Manual interactive verification of the Reader zoom/pan gestures (drag, pinch, double-click,
  touch flick) — built and unit-tested, but nobody has actually clicked through them yet.
- Unrelated but landed since: `3e7ada3` fixed an unbounded memory leak (`CoverImageCache` now
  LRU-bounded) — not on the roadmap, worth knowing about.
- ~~**New, not yet scoped:** Book Folders scan reads filenames only~~ — **done**, see below.

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

## P4 — Known gaps: placeholder content/assets 🟡 Mostly done

`f6bcee3`/`76fa3c6` fixed demo-*database*-seeding (fake Series rows on a fresh install) — a
different, narrower problem from the UI content sweep below, done separately. The UI sweep itself
landed via `275a348` and `0d08890`. Re-verified directly against source (not just commit messages):

- [x] Dummy text (lorem ipsum, sample labels, filler strings) — none found
- [x] Sample/mock data / hardcoded literals standing in for real bindings
  - [x] `DetailTabs.axaml` — counts now bound to `Issues.Count`/`Related.Count` (`275a348`)
  - [x] `MainWindow.axaml` — Collections row bound to `Library.Collections` with a real empty
        state; Duplicate Finder's hardcoded badge and fake demo content removed (`275a348`,
        `0d08890`)
- [ ] Placeholder icons/images (default/stock art standing in for final assets)
  - [ ] `Assets/avalonia-logo.ico` — still the default Avalonia template icon, wired as the actual
        window icon (`MainWindow.axaml` line 11) — needs a real Paperbunkr icon

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

## P6 — Known gaps: make UI fully functional 🟡 Substantial progress, not confirmed done

- [x] Detail screen — decorative Favorite button (no command) removed (`275a348`)
- [x] Reader screen — Reading Mode pill was styled like a working toggle but had no command;
      now wired to a real LTR/RTL flip (`275a348`)
- [x] Reading Lists screen — empty states for "no lists" / "list has no items" (`18d7ad8`)
- [x] Library screen — toolbar (search/filter/sort/group/overlays) and sidebar categorization
      turned from decorative stubs into real controls; Book Folders scan now toasts on completion
      (`8ace219` + the Library Toolbar Phase A–D commits)
- [x] Plugin screen — fake Duplicate Finder demo content and dead buttons replaced with a real
      empty state (`0d08890`)
- [ ] **Not yet re-confirmed:** Preferences screen, and the Issue Properties/Bulk Editing dialogs
      specifically for close/save/cancel correctness from all entry points — the above commits
      didn't touch these, so they're still exactly where the original P6 write-up left them
  - [ ] Confirm every dialog (Issue Properties Editor, Bulk Editing, Preferences) fully closes,
        saves, and cancels correctly from all entry points
  - [ ] One more pass across all screens to confirm nothing was missed, now that most of the
        obvious dead controls are gone

---

## P7 — Known gaps: appshell + alpha build packaging ⬜ Not started

- [ ] **Build an appshell and package the alpha build for install on other devices**
  - [ ] Build/configure the appshell (installer) project
  - [ ] Produce a `setup.exe` (Squirrel/Velopack/Inno Setup/MSIX — pick a packaging approach)
  - [ ] Test clean install on a separate device (not the dev machine)
  - [ ] Verify file associations register correctly post-install
  - [ ] Verify first-run experience end-to-end
  - [ ] Test uninstall leaves no orphaned state

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

## Bonus, ahead of schedule: Reader zoom/pan gestures

Not on the original P0–P7 list — pulled forward from the Beta "Reader polish" backlog today
(`4b1f6ed`) because trackpad pinch-zoom needed something real to control, per
docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md.

- [x] Ctrl+wheel/pinch zoom, anchored to the cursor
- [x] Plain wheel: pan while zoomed, page-turn while not
- [x] Click-drag pan (clamped at image edges)
- [x] Double-click to 2x zoom, centered on the click point / double-click to reset
- [x] Touch: 3-zone tap page-turn + horizontal flick
- [ ] Manual verification — built and unit-tested (`ZoomPanMathTests`, 13 cases), but nobody has
      clicked through the live gestures yet (no desktop GUI automation available to do this
      unattended)
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
