# Reading Lists visual identity - design

Date 2026-10-20. Asked for by the user ("time to revamp the visual identity of the reading lists") after the app-wide UI polish; grilled over
three rounds with the visual companion (mockups in `.superpowers/brainstorm/7-1792487600/content/`: `gallery-directions`, `detail-header-5-takes`,
`detail-header-mix-1-4`, `gallery-final-mock`). The user picked **B "The Editorial"** for the gallery, a **mix of take 1 (tidied) and take 4
(cinematic)** for the detail header, and signed off the final gallery mock ("it looks awesome").

Scope is deliberately partial ("I don't necessarily need to change EVERYTHING"): the gallery tiles, the Continue row, the tag-filter row and the
detail page's hero block. The journey path, cover wall, cover rail, Edit mode, Checks strip and Add-issues drawer are unchanged. Everything follows
the `paperbunkr-design-language` skill (type scale, tokens, 4 px grid, shared families, `PbMotionEase`); nothing here adds a shared family.

## 1. Gallery (`ReadingGalleryView`)

**Cards, not square tiles.** A list is a wide card (`Border.rlListCard`, `PbSurface1`, `PbBorder`, `PbRadius`, 12 px padding) in a grid of 2 columns
at 1366 px (3 from about 1700 px; never narrower than 460 px). Left: the cover, 84x118 (2:3). Right: a caps **type caption** (accent, 10 px bold)
with the source and issue count after a dot, the name (`pbTextTitle`-sized, 15, semibold, 2 lines max), a **two-line synopsis** (muted, 11/12 px,
hidden when the list has none so the card shrinks), the existing progress dots, and a footer row: read state on the left ("Not started" /
"13 / 38 read" / "✓ Finished" in accent), forecast on the right ("~ Nov 2026", from `ReadingListForecast`, empty when there is no pace or nothing left).

**Cover.** An arc cover (downloaded `CoverImageUrl`) shows alone, full 84x118. Otherwise a **fanned stack of up to 3** member covers (offset 12 px,
back ones dimmed to 0.55/0.8). Replaces the square 2x2 mosaic. Lists with no cover at all keep the glyph on a `PbSurface2` 2:3 plate.

**Type caption** (`ReadingListTypeCaption.For`): continuity-linked -> CONTINUITY; arc-linked (has a source) -> EVENT when `Type == Event`, else ARC;
`Event` -> EVENT; Official/Community/Chronological/PublicationOrder/Recommended/Custom -> their words in caps; a plain `User` list with no
source or continuity -> no caption (the card just shows the count). The source name comes from `ReadingListSourceRegistry.GetDisplayName`.

**Folders** are compact cards (`rlFolderCard`, same column width as list cards, about 64 px tall): a small fanned stack of the folder's lists'
covers (3 max), the name, "5 lists · 38%". They stay first in the one grid, so drag-to-reorder, drop-into-folder, insertion bars, inline rename,
`Tiles` ordering and keyboard navigation all keep working through the single `TileGrid`. (The mock's separate "FOLDERS"/"LISTS" captions are dropped
for that reason; the shape difference does the job.)

**Hover / focus** keep the Library ring (`PbGlowRing` plus an accent edge on the card Border, never transitioned, as `rlTileCover` does today).

**Continue reading** keeps its place above the grid. Cards become a compact surface with cover 34x51, name, "next: X · 10/29" and a 3 px progress line.

**Tag filter row** is a single horizontal row (`ScrollViewer`, hidden bar, vertical wheel scrolls it sideways, right edge faded) instead of a
wrapping block, active chip scrolled into view. Same `filterChip` family, same commands.

**Width.** The view owns `ListCardWidth` and `ListColumns` styled properties, recomputed when the scroller resizes
(`columns = max(1, floor((available + 12) / (460 + 12)))`, `width = (available - (columns - 1) * 12) / columns`). The template binds each host's
`Width` to it; the keyboard `Columns` uses `ListColumns` instead of the old fixed 182 px tile.

## 2. Detail hero (`ReadingListPageView`)

The cinematic banner is the existing blurred-cover `ParallaxBackdrop` + gradient, with the fade into the page strengthened. Changes:

- Cover 74x110 -> 84x124 (straight, not tilted: it stays aligned with the path rows).
- The caption line becomes the **type caption** (accent overline, same helper) followed by folder link, source mark and "via source".
- Title uses the 42 px hero size (display font, `LineHeight` set explicitly so capitals are not clipped), up to 2 lines, ellipsis + tooltip.
- Tag wall collapses to **one row** (`CollapsedRows` 2 -> 1) with the existing "Show all tags (N more)" toggle; chips keep `filterChip`.
- Synopsis collapses to **2 lines** (was 3); "more" still expands.
- The hero's text on the banner uses `PbTextBrush` / `PbTextMutedBrush` only.
- Path | Covers toggle, Up next card, Edit bar, actions: unchanged.

## 3. Data

- `ReadingGalleryTile` gains `TypeCaption`, `CaptionLine` (the muted "METRON · 29 ISSUES" part), `Synopsis`, `FooterLeft`, `FooterRight`, `IsFinished`,
  `ShowSynopsis`, `ShowCaption`.
- `ReadingGalleryViewModel.Refresh` already loads each list's entity; it now also keeps `Type`, `Source`, `ContinuityId`, `Description` and, once per
  refresh, loads the finished-comic timestamps for the forecast. No new queries per list.
- `ReadingContinueCard` gains `Progress` (0..1).
- Synopsis text goes through `ReadingListPageViewModel.CleanSynopsis` so the heading-line stripping matches the detail page.

## 3b. Implementation notes (built 2026-10-20)

Where the build differs from the sketch above, after rendering it in the default, Daylight and Matrix skins:

- **One grid, one width.** Folder cards use the same column width as list cards (not the mock's 3-up row), so `TileGrid`'s uniform column count keeps
  the arrow keys honest. List cards have `MinHeight` 144 (cover 118 + padding) so a row stays aligned whether or not the list has a synopsis; a
  folder card keeps its natural height. Columns: `floor(available / 472)`, width `available / columns - 12`.
- `Button.rlTile` now stretches (without it a card with no synopsis shrank to its text).
- The tag filter row sits in a ScrollViewer with an 8 px inset (negative margin outside) because the focus ring reaches 6 px and is clipped to the viewport.
- `CollapsibleTagWall` got a `CollapsedRows` property (default 2); the hero sets 1. The tag-wall view test now expects one row.
- The hero's `ProgressLabel` text line is gone (it repeated the meta line right under it; the bar keeps it as its accessible name), and the bar is
  left-aligned (it floated in the middle of the hero).
- Not touched: the Daylight hero's text contrast over a dark blurred backdrop (pre-existing, needs its own pass).

## 4. Not in scope / unchanged

Rail, path, cover wall, Edit mode, drawer, Checks strip, drag semantics, folder breadcrumbs, new-list dialog, Home's reading-list section, any
migration (no schema change).

## 5. Testing

- Unit: `ReadingListTypeCaption.For` mapping; tile footer/forecast strings; card-width/column maths.
- Existing `ReadingLists/*` view-model and view tests updated for the new template (`rlTileCover` -> `rlListCard`), keyboard column count.
- `XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests` must pass (no new hex, on-scale sizes, easing, names).
- Render the gallery and a list page in default, daylight and matrix themes; user checks on screen at 1366x768.
