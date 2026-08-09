# Paperbunkr Alpha Release To-Do

*Scope: git/release prep + known gaps only, per `alpha-roadmap.md` (2026-08-07). Beta backlog is
tracked separately in that document and not repeated here.*

Priority order below is suggested — highest-risk / release-blocking first, then polish ordered by
user-facing impact.

## What's left (as of 2026-08-09)

P0–P3 and P5 are done — shipped before this session (`f6bcee3`, `8e1bf55`), with P5 getting a
same-day follow-up (2D grid arrow-key nav, `34e1d39`). The `alpha` git tag already exists.

**P4 is NOT done** (corrected — a previous pass here wrongly marked it done based on `76fa3c6`,
an Aug 6 demo-*database*-seeding fix that predates P4 and isn't the same thing as a UI content
sweep). An actual code sweep found real placeholders still shipping:
- `DetailTabs.axaml` — Issues/Related tab counts are hardcoded `"42"`/`"4"`, not bound to real data
- `MainWindow.axaml` — a hardcoded `"7"` badge on the Duplicate Finder rail icon, and a hardcoded
  `"12"` on the sidebar "Collections" row (which isn't even a real feature — its bound siblings
  like "All Series" prove what this should look like)
- `Assets/avalonia-logo.ico` — still the default Avalonia project-template icon, wired as the
  actual window icon (`MainWindow.axaml` line 11)

**Open:**
- **P4** — placeholder sweep, see above. Not started.
- **P6** — dead/non-responsive UI sweep across the 6 rail-nav screens. Not started.
- **P7** — appshell + installer packaging. Not started.
- Manual interactive verification of today's Reader zoom/pan gestures (drag, pinch, double-click,
  touch flick) — built and unit-tested, but nobody has actually clicked through them yet.

**Housekeeping, not on the roadmap itself:**
- Stale worktree `.claude/worktrees/quirky-borg-c5d364` (branch `claude/quirky-borg-c5d364`) —
  its two commits (PageCanvas focus fix, Virtual Tags wiring) predate and are superseded by
  `8e1bf55`'s versions of the same fixes. Also has an uncommitted edit to
  `LibraryFolderScannerTests.cs`. Safe to discard once confirmed nothing else is needed from it.
- `docs/alpha-roadmap.md` has an uncommitted edit (cross-link to this file) from earlier today.

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

## P4 — Known gaps: placeholder content/assets ⬜ Not started

`f6bcee3`/`76fa3c6` fixed demo-*database*-seeding (fake Series rows on a fresh install) — a
different, narrower problem, not a UI content sweep. That fix stands, but P4 itself hasn't been
worked. Actual sweep findings so far (not exhaustive — a real pass should check all 6 screens):

- [ ] Dummy text (lorem ipsum, sample labels, filler strings) — none found in a first grep pass,
      but not exhaustively checked
- [ ] Sample/mock data / hardcoded literals standing in for real bindings
  - [ ] `DetailTabs.axaml` — Issues tab count hardcoded `"42"`, Related tab count hardcoded `"4"`
  - [ ] `MainWindow.axaml` — Duplicate Finder badge hardcoded `"7"`; sidebar "Collections" row
        hardcoded `"12"` (bind to real counts, or remove the row if Collections isn't a real
        feature yet)
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

## P6 — Known gaps: make UI fully functional ⬜ Not started

- [ ] **Sweep all 6 rail-nav screens for dead or non-responsive UI elements** — no button, toggle,
      or control should be visually present but inert
  - [ ] Audit each screen for stubbed/decorative controls that don't yet do anything
  - [ ] Confirm empty/error/loading states are handled, not just the happy path
  - [ ] Confirm every dialog (Issue Properties Editor, Bulk Editing, Preferences) fully closes,
        saves, and cancels correctly from all entry points

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
