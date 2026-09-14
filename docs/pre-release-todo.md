# Paperbunkr Pre-release To-Do

*Scope: everything still undone, plus new items as they come up, now that the Alpha milestone is
closed. Alpha's own release-prep checklist (P0–P7) lives in [`alpha-todo.md`](alpha-todo.md) as a
closed historical record — don't re-open or re-word it, it documents a milestone that already
happened. This doc is the live one going forward.*

## Where things stand

Alpha closed 2026-08-07 (`Paperbunkr-Roadmap.md`), tagged `v0.1.0-alpha`/`v0.1.1-alpha`. Since then
the project moved through `v0.2.0-beta` → `v0.3.0-beta` → `v0.3.1-beta` → `v0.4.2-beta` (real git
tags — verify with `git ls-remote --tags origin`, not a local `git tag --list`, which is empty on
a shallow clone). Almost the entire Beta backlog `Paperbunkr-Roadmap.md` laid out has since shipped
and been source-verified — see that doc and the live dashboard for the full landing history. What's
below is what's left, plus a place to log what comes up next.

**"Pre-release" replaces "alpha"/"beta" as the term for the project's current, ongoing stage.**
Historical references to the alpha milestone itself (the P0–P7 checklist, the `v0.1.x-alpha` tags,
changelog entries, old design-spec pointers to `alpha-todo.md`) are correct as written and aren't
being retitled — they're describing something that already happened. Only forward-facing mentions
("PaperBunkr is currently alpha", doc framing that implies alpha is still the active phase) get
corrected to reflect where the project actually is.

## Still genuinely open (verified against source, 2026-09-14, HEAD `a4a1c0c`)

**Preferences**
- Appearance redesign's preview-card feature — design spec (`750d69e`) exists, implementation still
  uncommitted.
- 5 cosmetic thumbnail toggles (`FadeInThumbnails`/`DogEarThumbnails`/`ShowToolTips`/
  `NumericRatingThumbnails`/`ExportedListsContainFilenames`) exist as `AppSettings` columns
  (migration `AddCosmeticThumbnailToggles`) but have zero UI wiring and zero consumers — schema
  stub only, matching the separately-drafted design spec (`7b9c2de`), which also has no
  implementation.
- `ToggleSwitch` control not adopted in `ConnectionsSection.axaml` or
  `KeyboardShortcutsSection.axaml`, despite being used everywhere else in Preferences.

**Metadata editing**
- Copy/paste fields between books, templated/token text editor, undo/redo — not started.
- Quick Rating + free-text Review popup, per-page type tagging, per-page rotation override, named
  bookmarks — not started.

**Content-type classification & manga scraping**
- MyAnimeList/Shikimori/Bangumi tracker adapters — not built (MangaBaka/MangaUpdates/Kitsu are
  done).
- Stage 5 stats dashboard — design spec (`707660f`) exists, not implemented.

**App chrome**
- Crash-report *dialog UI* — logging/breadcrumb infra (`DiagnosticsService`) exists, no
  WinForms-CE-style dialog equivalent.
- External "open with" file associations beyond the CLI `--register/--unregister` flags.
- Open-a-comic-on-launch / shell association — design spec (`f51e035`) exists, confirmed zero
  implementation (no argv-path or `ActivatedEventArgs` handling in `Program.cs`).

**Books/Novels**
- OPDS catalog client (Kavita/Komga) — design spec (`33ad867`) only, zero implementation
  (repo-wide search for "OPDS" hits nothing else).
- Reading-list import from AniList/MyAnimeList trackers — still not wired up (no
  `ImportReadingList`-shaped symbol anywhere in source); the wiki previously described this as "not
  wired up yet in the alpha", corrected to just describe current status without the alpha framing.

**Remote/server library sharing** — needs a design spec first. Client, server (password-protected,
per-list sharing), background job/task monitor. Fully open, nothing built.

**Plugins**
- Cluster Library Manager scraper UI redesign spec (`310fd8c`/`b941193`) is now orphaned: the
  plugin it targets was removed from this repo (`de8394d`, moved to its own repo). Needs a human
  call — drop the spec, or re-point it at the plugin's new repo.

**Release hygiene**
- `installer/BuildInstaller.ps1`'s header comment still says "Builds the Paperbunkr alpha
  installer" — stale independent of any rename, since the default build version is a beta/
  pre-release string. Worth a one-line fix.

**On-screen GUI verification pending** (no unattended desktop GUI automation in this dev
environment — these need a human pass):
- Maintenance scheduler & cover-cache durability's "Repair Missing Covers"/"Verify & Repair Covers"
  buttons.
- Backspace-while-focused-in-a-textbox nav-history shortcut, and the trackpad-swipe gesture.
- Split-page part navigation's actual scroll/stop behavior (automated tests pass; live feel
  unverified).

**Not re-verified in the last sync, carry forward for a future check:**
- Books reader pagination retry (design-only, last flagged 2 syncs ago).
- Chrome & Content Motion Polish (design-only, last flagged 2 syncs ago).
- Scan-time missing file handling (design-only, last flagged 2 syncs ago).
- `6114f18` ("Design: shared toast/dialog/indicator/badge feedback system") — design landed,
  implementation status not independently re-checked.

## Explicitly decided against (not open items, listed so nobody re-proposes them)

- Magnifier — declined, "we have a zoom slider" (reader).
- Filesystem folder browsing mode — dropped (drag-and-drop/watch/fileless entries cover the need).
- News reader — deferred, repurpose idea.
- Export to another format — dropped.
- GitHub self-updater — dropped for now (NetSparkle auto-update covers the need).
- Portable device / wireless sync — excluded.
- Multi-tab/multi-window (MDI model) — confirmed non-goal.

## Housekeeping carried forward

- 3 stale worktrees in `.claude/worktrees/` (`quirky-borg-c5d364`, `compassionate-banach-c6e8bf`,
  `exciting-hypatia-eecfc9`) — all confirmed clean/merged, safe to discard.

## Live tracker

The dashboard at **https://claude.ai/code/artifact/0ca86894-977e-45e2-951b-476e1150a5ee** and its
scheduled sync routine (`paperbunkr-alpha-tracker-sync`, every 6h, read-only) today still diff
against `alpha-todo.md`'s historical P0–P7 section for its `HEAD` marker — that mechanism hasn't
been re-pointed at this file yet. Until it is, update this doc by hand (same convention
`alpha-todo.md` used: session notes with date, commit refs, and what was actually verified against
source — not just what a commit message claims).

## Session log

*(Newest first. Add an entry here whenever pre-release-phase status changes or a new item surfaces
— mirror `alpha-todo.md`'s own convention: date, what changed, commit refs, and what was actually
verified.)*

> **2026-09-14 — doc created.** Alpha declared closed (already true as of 2026-08-07 per
> `Paperbunkr-Roadmap.md`); this doc replaces `alpha-todo.md` as the place to track what's still
> undone and log new items, since `alpha-todo.md` itself is scoped entirely to the (closed) Alpha
> release-prep checklist and known gaps, and rewriting its terminology in place would make it
> describe a git tag that isn't actually named what the word-swap would imply. Open-items list
> above compiled from `Paperbunkr-Roadmap.md`'s Beta backlog section and a full source-verification
> pass against HEAD `a4a1c0c`.
