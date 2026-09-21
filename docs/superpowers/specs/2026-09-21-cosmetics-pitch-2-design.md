# Cosmetics pitch, part 2 (items 8–26, slices A–C) — design

Date: 2026-09-21. Source: "Cosmetics pitch" items 8–27 in `docs/Paperbunkr-Roadmap.md` (added 2026-09-21).
Follows `2026-09-21-cosmetics-pitch-design.md` (items 1–7, built). Status: **approved, implemented and signed off by the user on screen 2026-09-21.** See "Implementation notes" at the end — several items already partly existed, so the built scope differs.

## Scope

Built now (slices A, B, C): **#10, #18, #20, #26** (Library tiles/rows), **#8, #9, #14** (Detail), **#13, #15, #16, #17, #21, #25**
(app-wide). Explicitly **not** in this spec:

| Item | Why |
|---|---|
| #27 auto light/dark + true-black | Already shipped: `ThemeAutoMode` (Follow System / Scheduled) and `TrueBlackDark`. Only sunrise/sunset would be new; dropped. |
| #12 reader chrome auto-dim | Already shipped: `ReaderAutoHideChrome` + per-cluster hover reveal. |
| #11 page-turn feel | Reader has page-transition code (`PageTransitionMath`, `PageTurnGestureMath`); first task of slice D is to find what's missing. |
| #22, #23 (reader), #24 (taskbar badge), #19 (bookshelf view) | Later slices: D reader (heavy on-screen checking), E Windows integration, F its own project. |

**CE parity:** ComicRack CE has none of these. Every item is a deliberate deviation. Nothing changes CE-compatible data or
file formats. New columns are additive (older builds sharing the DB ignore them).

## Shared decisions

1. **Settings.** Toggles live in Preferences → Appearance as sub-groups (Cover cosmetics — exists; **Detail**; **Interface**).
   Pure restyles (#10, #13, #25) get no toggle. Persisted via `AppSettings` columns + a migration with a **no-op `Down()`**
   (AppSettings rebuild bug), hot-path reads through `CosmeticThumbnailSettings`.
2. **Cover palette, extracted once, persisted.** A `CoverPalette` service extracts up to 3 colors from a cover thumbnail
   off the UI thread, stores them on `Issue` (nullable string column next to `CoverAspectRatio`), and a series uses its
   cover issue's palette. Filled lazily the first time a surface needs it (Detail open), never during scroll. Read by #8, #9,
   #18 (and later #19). Failure → null → surfaces fall back to the skin accent.
3. **Reduced motion** gates every animation added here (`ThemeService.GetReducedMotion`).
4. **Theme-only colors, squircle chips** (`PbRadiusChip`), no color-only signals (icon or text always paired), no hardcoded hex
   except neutral black/white scrims over cover art (as in the tile overlay).
5. **Hot-path rule** (scroll-smoothness work, 2026-09-19): anything on a Library tile is a single lightweight custom-rendered
   control or a cached brush, no extra template parts or per-tile bindings.
6. **Activity Center rule:** nothing here creates jobs/alerts; #21 only restyles existing ones.
7. **Concurrent-work caution:** the working tree holds another session's uncommitted edits (`ActivityTemplates.axaml`,
   `Badges.axaml`, `Primitives.axaml`, `LibraryScreen.axaml`, …). Edit on top, never revert or stage them.

## Slice A — Library tiles and rows

### #10 Series-status color language
One `SeriesStatusStyle` mapping (single source, unit-tested): **Ongoing** → success + play icon, **Completed** → accent +
check-circle, **Hiatus** → warning/badge + pause-circle, **Cancelled** → danger + dismiss-circle, **Unknown** → no chip. Rendered as a
dot + icon + label chip (`PbRadiusChip`) via one reusable control. Used in: Library series tiles/rows (as a small status
glyph, not a second badge on posters), Detail hero, Wanted, tracker views. Brushes are the existing `PbSuccess/Accent/Badge/Danger`
tokens, so it follows the skin.

### #18 Generated placeholder covers
When an issue has no cover, or the cover failed to decode, show a typographic cover: series initials (≤3 letters) large, series
title small, on a gradient derived deterministically from a hash of the series name, tinted toward the skin accent. Rendered
**once per (series name, size bucket) into a cached bitmap** through the existing cover cache path, never per frame; stable across
loads. Integrates at the point `AsyncCoverImage` currently leaves a blank tile. If the cover palette exists it is not used here
(the cover is what's missing).

### #20 Read-state glyph set
Extend `IssueTileGlyph` (today `None/Read/InProgress`, Detail tabs only) to **Unread / InProgress / Read / New** ("recently added",
≤7 days, the same window `RecentAddBadgeLabel` uses) and render it with **one** small custom control used identically on
Library issue tiles (replacing the ad-hoc unread "●" pill), Library list/details rows, and the Detail issue list. Complements the
dog-ear and the read-progress ring; series-level tiles keep the ring/count and do not get a per-issue glyph.

### #26 Slim scrollbars + A–Z letter rail
- **Scrollbars:** thin (≈6 px), auto-hiding, accent-tinted on hover, applied as a style on the Library's scroll viewers only in
  this slice (widening app-wide is a follow-up if it looks right). Must not change `SmoothScrollViewer` behaviour.
- **Letter rail:** in name-sorted Library views (sort field = series name, i.e. the existing "Grouped: Alphabetical" case), a
  vertical strip of A–Z (+ #) at the right edge; letters with no items are dimmed, click/drag jumps via the existing
  `ScrollToIndexRequested`, current letter highlighted from the visible group header. Hidden for other sorts. Keyboard type-ahead
  keeps working.

## Slice B — Detail screens

### #8 Detail hero backdrop
The comic/manga/book Detail hero gets an ambient backdrop from the series cover: a **heavily downscaled copy scaled up with
bilinear filtering** (cheap blur, no GPU `Effect`), color-graded toward the palette, under a **skin-aware scrim** (`PbBg` at high
alpha) so text stays legible in light themes. Built once with the cover, cached. Toggle: Appearance → Detail → "Hero backdrop",
default **on**.

### #9 Dominant-color accent per series
Tint the Detail screen's accent (chips, progress, primary button) from the series palette, contrast-adjusted against the
background with the existing accent contrast helper (so text stays ≥4.5:1), falling back to the skin accent when no palette. Toggle:
Appearance → Detail → "Series accent color", default **off** (it changes the app's look per screen; opt-in).

### #14 BrandMark consistency pass
Audit every surface showing a publisher or tracker source (ComicVine, Metron, AniList, MangaBaka) and route them through
`MarkResolver`/`BrandMark` with consistent sizes; add tracker families where missing. Text-only fallbacks stay for unknown
values. No new third-party artwork is invented; families without a supplied mark keep the text fallback.

## Slice C — App-wide polish

### #13 Empty-state illustrations
One reusable `EmptyState` control: small line-art illustration (drawn as vector path data in the skin's foreground color), one
headline, one line of copy, and one call-to-action bound to the existing command. Replaces the bare text on Events, Continuities,
Collections, Reading Lists, Wanted (and the other spots found by grepping "No … yet" style strings). 4–5 shared illustrations
(stack, list, timeline, calendar, bookmark).

### #15 Live skin previews
In Preferences → Appearance, each skin tile shows a mini mock (sidebar, a tile, a chip) drawn by a custom control from that skin's
own `ThemeDefinition.Colors` (the same values `ThemeService` applies), so a preview cannot drift from the real theme and needs no
app-wide resource swap.

### #16 Insights/Stats chart polish
Hover value tooltips and crosshair on the ScottPlot charts, an entry sweep on `CategoryDonut` (reduced-motion gated), and a unit
test asserting the shared `InsightsChartTheme` categorical palette keeps ≥3:1 against the chart background in both light and dark.
Reuses `ArcGeometry` from the tile ring work.

### #17 Density presets
**Compact / Comfortable (default) / Spacious** scale a small named set of tokens together: Library poster density (the existing
`GridDensity`), list/details row height and padding, and sidebar item padding — through resource tokens, so nothing hardcodes
sizes. **Deferred / out:** a global type-scale (Avalonia has no cheap global font scale) and the "reading-friendly font for
EPUB/PDF chrome" (needs a font asset/licensing decision); both stay in the roadmap.

### #21 Activity Center visual polish
Per-job progress rings (reusing `ArcGeometry`), grouping by job type in the drawer, and a source icon per job/alert in the peek
popover and drawer. Presentation only; routing unchanged. Touches `ActivityTemplates.axaml`/`ActivityDrawerView.axaml`, which the
other session has uncommitted edits in — edit on top.

### #25 Context-menu polish (audit-sized)
`ContextMenuEntry` already supports leading icons and shortcut hints. Scope is an audit: fill entries missing an icon, make
destructive entries use the danger color, and tidy separator grouping across the menu builders. No mechanism change.

## Verification plan
- Unit: `SeriesStatusStyle` mapping; `IssueTileGlyph` state logic (incl. "New" window); placeholder determinism (same name → same
  colors/initials, different names differ); `CoverPalette` extraction on synthetic bitmaps + null/fallback; letter-rail letter set
  and jump index; density preset → token values; empty-state control renders; chart palette contrast; migration test for new columns.
- Ground-truth headless render tests (as `TileCosmeticsOverlayTests`) for the custom-drawn controls (glyph, status chip, skin
  preview, progress ring in Activity Center).
- Perf: Library scroll with the new glyph/status/placeholder in a 2000+ library must not regress vs. today.
- Avalonia: load the `avalonia` skill + matching subskills before each slice; run `avalonia-pro-max/review-checklist` before
  calling UI work done; new views ship their code-behind in the same step as the `.axaml`.
- On-screen check by the user for every item (no UI automation without permission).

## Suggested build order
A (#10 → #20 → #18 → #26), then B (palette service → #8 → #9 → #14), then C (#13 → #25 → #16 → #21 → #15 → #17).
#17 last: broadest blast radius.

## Open items (resolve in the plan, not user decisions)
- Where `Issue.CoverAspectRatio` is learned today, to hook palette extraction next to it.
- Exact status glyph placement on series tiles so it doesn't crowd the ring/badges.
- Which surfaces still show text-only publisher/tracker names (#14 audit).

## Implementation notes (added 2026-09-21, after building)

Survey of the real code showed several items already partly existed, so the built scope differs from the design above:

- **#8 backdrop:** `BackdropBlurRenderer` already produced a blurred cover backdrop with skin-token gradient scrims. Built: only the on/off switch
  (Appearance → Detail screens → "Hero backdrop", default on). No colour grading was added.
- **#9 series accent:** built as designed (default **off**), with two deviations: the palette is **not persisted** — extraction samples a 24×24 grid
  from the already-decoded cover (well under 1 ms), so the planned `Issue` column and migration were dropped; and the accent is applied by overriding
  `PbAccent*` on the *screen's own* `Resources`, so only that screen re-tints.
- **#10 status:** chip in the Detail hero and Library series **list** rows. Not added to poster tiles (already crowded), Wanted, or tracker views — those
  don't carry a series status today.
- **#14 BrandMark:** service marks were already used in Detail tabs and Connections; the one text-only spot found was the reading-list arc-source chip
  ("via ComicVine"), now a real mark.
- **#15 skin previews:** live preview cards from real `theme.json` colours already existed; added a poster tile and chip to the mock.
- **#16:** hover tooltips on the three Insights charts (Pace, Publication year, Growth) with a cursor line on Growth; the Ratings chart is not wired.
  Chart colours are lightened/darkened only when they fall under 3:1 against the skin background. Donut entry sweep uses `PbMotionLarge` (zero under Reduced Motion).
- **#18 placeholders:** the tile already had a deterministic per-series gradient; built the initials + title over it, shown only after an empty decode.
  Grid tiles only (Poster and Panorama, issue and series).
- **#20 glyphs:** `StatusBadge` already had Read/InProgress/New; added Unread and one shared resolver, and unified the Library's three ad-hoc unread marks.
  "New" is resolved by `IssueTileGlyphs` but only the Detail manga chapter list uses the New badge today.
- **#21:** per-source icons already existed. Added the progress ring and grouping (headings appear only when running jobs span 2+ sections).
- **#25:** audit only, as designed — icons and danger flags filled in six builders; the two menus not touched (reader page, plugin commands) have no natural icon.
- **#26:** an A–Z rail already existed for ungrouped views. Extended to letter-grouped views, dims empty letters, jump-to-group, and (added after a
  follow-up request) highlights the letter the view is scrolled to (grouped: first realized group on screen; ungrouped grid: item at the estimated first
  row; lists: first realized row). Scrollbars: the theme's thin auto-hiding scrollbars stay; hovering/dragging a Library scrollbar thumb now tints to the skin
  accent by overriding FluentAvalonia's `ScrollBarThumbFillPointerOver` / `ScrollBarThumbFillPressed` on the Library screen only (key names verified in the
  installed FluentAvalonia.dll and guarded by a test). Neither the highlight nor the tint has been seen on screen.
- **#13:** illustrations added above the existing text/CTAs at the Events (nothing, no members, no series) and Reading Lists (empty list, no lists) empty states.
  Wanted already had designed empty states; sidebar one-liners and the collection overlay were left alone.
- **#17:** `Density` (Compact / Comfortable / Spacious) drives Library list and details row padding and sidebar item padding via three tokens.
  Comfortable is byte-for-byte the previous spacing. The poster grid keeps its own density slider.
- **Settings added:** `HeroBackdrop`, `SeriesAccentColor`, `DensityPreset` (migrations `AddDetailCosmeticSettings`, `AddDensityPreset`, no-op `Down()`).
- **Not verifiable here:** on-screen appearance of every item.
