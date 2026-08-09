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
