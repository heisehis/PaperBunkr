# Cosmetics pitch completion - design

2026-10-12. Closes the last items of the Cosmetics pitch (`docs/Paperbunkr-Roadmap.md` "Cosmetics pitch" #11, #19, #22, #23, #24).
Decisions from the 2026-10-12 grilling round were accepted as recommended ("go with recommended"); the survey below corrected two of the pitch's premises.

## Status of each item

| # | Item | Verdict after surveying the code |
|---|---|---|
| 24 | Taskbar and tray badge | **Built and user-verified 2026-10-12.** Counts pending Activity Center alerts (the user chose this over "wanted releases this week", which kept showing 6 with an empty panel). Setting `ShowTaskbarBadge`, migration `AddShowTaskbarBadge`. Nothing left. |
| 11 | Page-turn feel | **Closed, no work.** `PageTransitionStyle` already has `None`, `Slide`, `Crossfade` (spec `2026-08-13-reader-page-transition-animations-design.md`). Paper-curl was ruled out then because CE never had a real turn animation. |
| 23 | Page-thumbnail scrubber | **Mostly exists.** Each page dot in the reader's progress strip already shows a 90x126 thumbnail tooltip above the dot (`ReaderScreen.axaml`, `PageDotsItemsControl`). Gaps only: no page number on the preview, and an empty grey box until the background thumbnail pass reaches that page. |
| 22 | Reader ambient letterbox | **New.** The reader background is Auto / Color / Texture; nothing derives colour from the page. |
| 19 | Bookshelf spine view | **New and large.** `LibraryViewMode` is `PosterGrid`, `List`, `DetailsTable`. |

So this spec builds three things: **A** (#23 polish), **B** (#22 ambient letterbox), **C** (#19 bookshelf). Build order A, B, C; the user checks each on screen before the next starts (B changes the reader, C is the biggest).

## A. Page preview polish (#23)

- The existing tooltip content becomes a small vertical stack: the thumbnail, and under it the page number ("Page 14 / 32", using the page's displayed number, i.e. after any skipped-page logic the dots already use).
- While `CoverImage` is null the thumbnail area shows the page number large on `PbSurface3Brush` instead of an empty box, so the hover is never blank.
- No new decode: the preview keeps using `ReaderThumbnailSample.CoverImage`, which the thumbnail pass fills in. Nothing here may request a decode or compete with prefetch.
- Not doing: a preview on the thumbnail rail (its tiles already are the thumbnails), or a separate slider (the dots are the progress control).
- Files: `ReaderScreen.axaml` (tooltip template), `ReaderThumbnailSample` (a `PageNumberText` property; it already carries enough to compute it, confirm at plan time).
- Tests: view-model level for the page text; headless render of the template with and without an image.

## B. Ambient letterbox (#22)

**Setting.** Add `Ambient` to `ImageBackgroundMode`. The enum is persisted as an integer, so it is appended after `Texture` (value 3) and nothing is renumbered. `Paperbunkr.Engine.Display.ImageBackgroundMode` is the ported CE enum and stays untouched (CE has no such mode; this is a deliberate deviation, noted in the enum's doc comment). No new column: it reuses `AppSettings.ImageBackgroundMode`, and reader profiles already carry `ImageBackgroundMode?`, so per-profile choice works for free. Preferences > Reader's background mode list picks it up through `Enum.GetNames`; give it a one-line description.

**Colour source.** The current page's colours come from its small thumbnail bitmap (`ReaderThumbnailSample.CoverImage`), not from the full decode: it is already in memory and costs nothing against the decode budget. A pure helper `PageEdgeColors.FromPixels(ReadOnlySpan<uint> argb, int width, int height)` returns the average colour of the outer 6% on the top, bottom, left and right edges (all four averaged over the whole strip, ignoring nothing; unlike `CoverPalette` there is no "colourful only" filter, because a letterbox should match a white or black page border too).

**Rendering.** `ReaderScreenViewModel` holds an `AmbientBackdropBrush` (`IBrush`): a vertical `LinearGradientBrush` from the top-edge colour to the bottom-edge colour, with the left/right colours blended in at 25% so side margins don't clash. The colours are darkened by a fixed factor (about 0.55) and desaturated slightly so the page stays the brightest thing on screen. `CanvasBackgroundBrush` returns this brush when the mode is `Ambient`.

**Change handling.**
- Recomputed on every page change (paged mode: the current page; double-page spread: the average of the two pages' edges; continuous/webtoon: the page at the viewport centre, throttled to every 250 ms).
- The Grid's `Background` gets a `BrushTransition` so the change cross-fades over `PbMotionStandard` (zero when reduced motion is on, like every other motion token).
- If the page's thumbnail is not ready, keep the previous colours (first page: fall back to `DefaultCanvasBackgroundBrush`). Never block or request a decode.
- A missing or failed thumbnail must never throw; the whole computation sits behind the same catch-and-fall-back the Texture mode uses.

**Interactions.** `ShowPageShadow` stays Texture-only. Ambient mode and "true black" auto-suspend: true-black suspends only Color/Auto, so Ambient is left as the user set it (confirm against `ThemeService` at plan time).
- Files: `ImageBackgroundMode.cs`, `ReaderScreenViewModel.cs` (+ a new partial `ReaderScreenViewModel.Ambient.cs`), a new `Services/Reader/PageEdgeColors.cs`, `ReaderScreen.axaml` (brush transition), `PreferencesScreenViewModel` / Reader section text.
- Tests: `PageEdgeColors` on synthetic buffers (solid, split top/bottom, white border); gradient brush stops; fallback when no thumbnail; mode round-trip through a profile.
- On-screen check list: colour looks right on a white-bordered page, on a full-bleed splash page, and on a double-page spread; the cross-fade is not distracting while flipping quickly; no extra decode shows in the perf overlay.

## C. Bookshelf spine view (#19)

**What it is.** A fourth `LibraryViewMode`, `Bookshelf`, appended after `DetailsTable` (integer-persisted, nothing renumbered; the `AppSettings.LibraryViewMode` default is unchanged). Series/issues (whatever the current lens shows) render as vertical book spines standing on shelf rows.

**Spine look.**
- Width varies by page count (clamped 22 to 54 px), height by a fixed band with a few px of deterministic jitter derived from the item id, so the shelf looks natural but never reshuffles between renders.
- Colour: the first colour of `CoverPalette.FromBitmap` on the item's cover (the same extraction the per-series accent uses; no new extractor), darkened for the body with a lighter top band. Items with no usable colour use the skin's `PbSurface3Brush` with a muted accent stripe.
- Text: the title runs vertically (rotated 90 degrees) in a legible contrast colour chosen from the spine colour's luminance; the issue number or volume sits in a small cap at the top. The binding-spine texture from pitch #1 is reused as a subtle overlay.
- Read state: a thin bottom progress strip on the spine; finished items get the check glyph from the shared read-state glyph set.
- Hover lifts the spine 6 px and widens its hit area; a tooltip shows the cover thumbnail and the title. Click selects (same selection model as the grid), double-click or Enter opens, the context menu is the shared Library menu. Keyboard: arrow keys move between spines through the same directional handling the grid uses, registered as the owning view per CLAUDE.md's input rules (no per-screen hotkey handlers).

**Virtualization (the hard part).** One custom-rendered control, in the style of `TileCosmeticsOverlay`'s "one control, reads DataContext, no template parts" rule that the 2026-09-19 scroll work established:
- Layout is computed up front from item widths into shelf rows (a pure `BookshelfLayout` class: given widths and a row width it returns the row and x position of every item). It is unit-testable without any UI and runs off the UI thread for 2000+ items.
- Only the rows intersecting the viewport (plus one row above and below) are drawn; the control sits in the Library's existing `ScrollViewer`, and its extent height is `rows * rowHeight`.
- Cover thumbnails for colour extraction go through the existing cover lease path (the 2026-10-07 performance pass), loaded for visible spines only, colour results cached by issue id in memory for the session so scrolling back is free. Until a colour is known the spine shows the placeholder look; it fades to its colour when ready.
- The control is the shelf; shelf boards between rows are drawn with the skin's surface and border tokens so it follows every skin and the high-contrast variants.

**Wiring.** Add the mode to the Library toolbar's view toggle and the saved-layout persistence (the 19 existing `LibraryViewMode.List/DetailsTable` references in the App project are the survey list for where a fourth mode must be handled; the plan enumerates each). Sorting, grouping, search, selection and the preview panel work unchanged because the mode only changes how the same collection is drawn. Group headers render as shelf labels.

**Deliberate limits.** No drag-to-reorder, no per-spine custom colour, no 3D tilt. Covers and series without pages data use a default width.

- Tests: `BookshelfLayout` (row breaking, jitter determinism, row count for large inputs, resize), spine colour pick and contrast text choice, enum append-only guard, headless render of a few spines in two skins.
- On-screen check list: 2000+ item library scrolls smoothly, spine colours settle without flashing, keyboard navigation, selection, context menu, group labels, high-contrast skin.

## Migrations and compatibility

No migration for A or B (enum append only; `ImageBackgroundMode` and `LibraryViewMode` are stored as integers, verify with the model snapshot at plan time). C needs none either. Any settings column added later follows the project rule: never regenerate a migration the user's database already ran; add a new one with a no-op `Down()`.

## Out of scope

Paper-curl or any new page-turn animation; a slider-based scrubber; per-page ambient settings; bookshelf drag-reorder.

## Risks

- Ambient on slow flips: the colour work is a sampling of an in-memory thumbnail, but a flip storm still posts many brush changes; the 250 ms throttle and the `BrushTransition` keep that bounded.
- Bookshelf performance is the real risk and is why it is the last slice and its own control; if the colour extraction cost shows up in scrolling, the fallback is to extract only for spines that have been visible for 150 ms.
