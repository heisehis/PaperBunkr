# Cosmetics pitch — design

Date: 2026-09-21. Source: "Cosmetics pitch" in `docs/Paperbunkr-Roadmap.md` (pitched 2026-09-14).
Status: approved, implemented and signed off by the user on screen 2026-09-21 (all 7 items). See
"Implementation notes" at the end for where the built version differs from the pitch's assumptions.

## Scope and CE parity

All 7 pitch items, built in three slices. **ComicRack CE has no equivalent of any of them** — CE's
thumbnail cosmetics stop at the toggles already shipped (`CosmeticThumbnailSettings`: fade-in,
dog-ear, tooltips, numeric rating). Every item here is therefore a *deliberate deviation*, not a
parity gap, and is flagged as such. Nothing here changes CE-compatible data or file formats.

Slices (order of build): **B** (tile overlays: #1, #2, #3) → **A** (#5, #7) → **C** (#4, #6).
Slice B first because it touches the hot grid-tile path and shares decisions with the dog-ear work.

## Shared decisions

1. **Settings.** Every cosmetic is a per-user toggle in Preferences → Appearance (not per-skin).
   Skins may supply defaults later; not in this spec. New `AppSettings` columns follow the existing
   cosmetic-toggle migration pattern (`AddCosmeticThumbnailToggles`): explicit defaults, migration
   test that asserts the columns, and a fresh Designer snapshot (see the stale-snapshot and
   `HasDefaultValue` gotchas in memory).
2. **Hot-path reads** go through `CosmeticThumbnailSettings` (static snapshot, refreshed at startup
   and on live change), never a per-tile DB read.
3. **Reduced motion.** Any animated cosmetic is gated on the existing `ReducedMotion` setting
   (`ThemeService.GetReducedMotion`); static ones are unaffected.
4. **Activity Center / feedback:** none of these are jobs or alerts; not applicable.
5. **Shapes.** Chips and badges use `PbRadiusChip` (9), never ovals.
6. **Colors** come from theme resources (`PbGlowColor`, `PbAccentBrush`, …), never hardcoded hex, so
   they follow the runtime skin system.

## Slice B — tile overlays

### #1 Binding spine texture
- A static overlay on Poster/Panorama tiles: a narrow (≈9 px) strip on the cover's left edge — a dark
  outer band stepping to transparent plus a 1 px light highlight line. Drawn as a plain gradient
  `Border` overlay; **no** per-tile bitmap, blur or `Effect` (virtualized grid, 2000+ items).
- Toggle: Appearance → "Binding spine". Default **on**.
- RTL/manga: spine stays on the physical left in Poster; for manga tiles flip to the right when the
  series reading direction is RTL (verify the field name in the plan).
- Files to touch (confirm in plan): `PosterTile.axaml`, `PosterRail.axaml`, the Panorama tile
  template in `LibraryScreen.axaml`.

### #2 Read-progress ring
- Shown **on hover and focus, alongside the unread badge** (badge stays at rest). **Finished series
  also show a full ring at rest** (user decision, 2026-09-21) — uses a success-colored stroke; check
  visual weight against dog-ear/rating badges on-screen.
- Measures issues read / total for series tiles, pages read / total for single-issue tiles. Books
  and manga tiles get the same behavior.
- New small control `ProgressArc` (single arc, `StreamGeometry.ArcTo`, round cap, track + value
  stroke, center percentage). The arc-geometry helper is extracted from `CategoryDonut.cs` so both
  share it; `CategoryDonut` behavior unchanged (its tests must stay green).
- Toggle: "Progress ring". Default **on**.
- Data: use fields the tile view-model already has (unread count / total); if a needed count isn't
  on the tile model, add it in the same query that produces the badge — no new per-tile query.

### #3 Accent glow tiers
- Today's glow is the `PbGlowRing` `BoxShadows` resource, rebuilt by `ThemeService` from the theme's
  `PbGlowColor`. Tiers rebuild that one resource with scaled blur radius and alpha; no tile XAML
  changes.
- Selector: Appearance → "Accent glow": **Off / Subtle / Normal / Vivid**. **Normal reproduces
  today's exact values** so existing users see no change. Default Normal.
- Applies live (resource re-application, as skin changes already do). Off yields an empty
  `BoxShadows` — verify every consumer of `PbGlowRing` (PosterTile/Rail, Preferences tiles, reader
  bookmark pulse) tolerates that; the bookmark pulse should probably ignore the tier.
- Test: extend `ThemeServiceTests.ApplyTheme_RebuildsGlowRing_FromThemeGlowColor` per tier.

## Slice A

### #5 Library Health chips
- Severity chip becomes dot + FluentIcons glyph + the existing text label (color is never the only
  signal). Three levels: **red** missing/unreadable, **amber** suspect (content-empty, identity
  mismatch), **green** OK. Colors from theme danger/warning/success brushes. `PbRadiusChip`.
- No toggle (pure restyle of an existing chip). Map each existing severity value to a level in the
  plan after reading the current chip code.

### #7 Reading-list CBL cover mosaic
- 2×2 grid of the first four covers; fewer than four repeats the first cover to fill; one or two
  covers keep today's single cover.
- Built once per list and cached through the **existing cover cache** (no new cache system —
  `ArcCoverImageCache` is a separate system, don't confuse them); invalidated when the list's first
  four members change. Never composed per frame.
- Toggle: "Reading-list cover mosaic". Default **on**.

## Slice C

### #4 Continuity / Event timeline connector art
- Events timeline view: a 2 px vertical line down the left with a node dot per entry. Dot is the
  skin accent for the current/next event, muted otherwise. Skin-colored, no toggle (styling only).
- **CE deviation, flagged:** CE has no timeline view.

### #6 Splash ambient motion
- 12–16 slow-drifting low-opacity dots on the splash/welcome. Starts after first paint; stops when
  the splash closes; skipped under reduced motion and in bootstrap-crash safe mode. Must not add
  work to the startup-critical path (the splash reads settings pre-migration — reuse the existing
  `TryGetReducedMotion` guard pattern in `SplashWindow.axaml.cs`).
- Toggle: "Splash ambient motion". Default **on**.

## Verification plan
- Unit: settings persistence + migration test per new column; `ThemeService` glow tiers; `ProgressArc`
  math (0/50/100 %, tiny fractions); mosaic fill rule (1–4+ covers); chip severity mapping.
- Perf: virtualized Library scroll with spine + ring on, 2000+ items, compared to off — no regression.
- Avalonia: load the `avalonia` skill and read the matching subskills (design-system, components,
  motion, accessibility) before each slice; run `avalonia-pro-max/review-checklist` before calling
  UI work done. New views need code-behind in the same step as the `.axaml`.
- On-screen check by the user for every item (no UI automation without permission).

## Out of scope
Per-skin cosmetic overrides; dog-ear / cosmetic-thumbnail toggles (already shipped); any
animation beyond #6.

## Open items (resolve during planning, not user decisions)
- Exact `PbGlowRing` values for Subtle/Vivid (choose from current Normal; check on-screen).
- Whether a finished-series ring at rest looks crowded next to dog-ear/rating (on-screen check).
- Field names for RTL direction, per-series read counts, and health severity values.

## Implementation notes (added 2026-09-21, after building)

The design held, but code review of the real surfaces turned up several places where the pitch's assumptions did
not match what exists. Recorded here so the spec is honest about what shipped:

- **#2 ring position: bottom-centre, not bottom-right.** Bottom-right is already taken (selection checkbox, dog-ear
  peek, Remote pill), bottom-left is the rating badge, top-right is the unread badge. The ring is a 34 px disc at
  the cover's bottom-centre. Hover shows it; a finished series/issue shows it at rest (user decision).
- **#2 focus:** the ring shows on **pointer hover only**, not keyboard focus. Hover is pushed in from the existing
  cover `PointerEntered/Exited` handlers, so realizing a tile adds no binding or subscription; focus would have
  needed a per-tile subscription (the scroll-smoothness work priced those). Finished tiles show it at rest either way.
- **#1/#2 mechanism:** one custom-rendered `TileCosmeticsOverlay` control (no template, no bindings) reading its
  DataContext through `ITileProgressSource`, inserted into the four Poster/Panorama cover templates (issue + series).
- **#3 tiers:** Off / Subtle / Normal / Vivid rebuild `PbGlowRing` in `ThemeService`. Normal is byte-identical to the
  old ring. The reader's bookmark-toggle pulse moved to a fixed `PbGlowPulseRing` so "Off" never disables that feedback.
- **#5 Library Health:** there was **no** text severity chip; severity was a `· confirmed missing` / `· unreadable`
  suffix baked into each row's label. It is now real data (`HealthSeverity`) rendered as a dot + icon + text chip:
  amber "Missing", red "Confirmed missing", red "Unreadable" (content-empty). The green "all clear" state already
  existed as the empty-state checkmark, so no green row chip was added. (The spec's "identity mismatch" amber tier has
  no row type in the current health lists, so it wasn't invented.)
- **#7 mosaic:** a reading list's header cover is the *arc* cover (`ArcCoverImage`) with a "☰" placeholder. The mosaic
  fills that placeholder only when the list has no arc cover; an arc cover always wins. The sidebar rows never showed
  a cover (v2 design, name + progress bar), so nothing changed there. Covers go through the Library's cover cache
  (`AsyncCoverImage`), not `ArcCoverImageCache`. Fill rule is a pure, tested function (`ReadingListCoverMosaic`).
- **#4 timeline:** the Events timeline is era sections (comic ages), not per-event rows, and there is no "current/next"
  notion. Connector = continuous 2 px line with a node per era; accent node while the era has unread issues, muted
  once all read.
- **#6 splash:** 14 dots, pure `DotAt(t)` drift function, driven by `TopLevel.RequestAnimationFrame` from
  `SplashWindow.Opened` (after first paint); off under Reduced Motion, safe mode, or the preference; stopped on fade-out.
- **Settings:** five `AppSettings` columns in one migration (`AddCosmeticsPitchSettings`, no-op `Down()` per the
  AppSettings rebuild bug). Live toggles: spine, ring, glow. Read on next use: mosaic (next list open), splash (next launch).
- **Not verifiable here:** all on-screen appearance (no UI automation without permission). Unit + ground-truth headless
  render tests cover the overlay's spine edge/flip and the ring's show/hide rules.
- **Follow-up after on-screen use (2026-09-21):** the hover multi-select checkbox crowded the read-progress ring, so it is now
  opt-in (Appearance → "Selection checkbox on tiles", default **off**, column `ShowSelectionCheckbox`, migration
  `AddShowSelectionCheckbox`). With it off, Ctrl/Shift+click is the way to multi-select (already wired), and a *selected* tile still
  shows its checked box, since the checkbox is the only selection indicator there is. The binding spine was also redrawn as one
  soft crease (a 1 px hard highlight line read as a stray scan line on dark covers).
- **Ring position follow-up (2026-09-21, after on-screen use):** with the selection checkbox off (the default), the ring now sits **bottom-right** of the
  cover. It steps to bottom-centre whenever the checkbox owns that corner - the "Selection checkbox on tiles" setting is on, or the tile is selected (a
  selected tile always shows its checked box; the overlay listens for `IsSelected` only while its ring is showing). Known overlaps at bottom-right: the
  dog-ear page peek (hover) and the "Remote" pill; the ring is drawn above the peek. Placement is `TileCosmeticsOverlay.RingCenterX` (unit-tested).
