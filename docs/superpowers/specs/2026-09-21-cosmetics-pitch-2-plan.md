# Cosmetics pitch, part 2 — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md*

Working-tree note: the tree has uncommitted edits from a concurrent session (Wanted screen redesign, incl. `LibraryScreen.axaml`,
`Badges.axaml`, `Primitives.axaml`, `ActivityTemplates.axaml`, `App.axaml`). Every edit here was made on top of them; nothing was reverted or staged.

Survey finding that reshaped the plan: several items already partly existed (Detail backdrop blur, `StatusBadge` glyph set, A–Z rail, live skin
preview cards, context-menu icons/gestures, service brand marks). Steps below are what was actually built; the design doc's "Implementation notes"
records each deviation.

## Slice A — Library tiles and rows
1. **#10 Series status.** `Models/SeriesStatusStyle.cs` (one mapping), `Controls/SeriesStatusChip.cs` (code-only chip, theme-bound), used in the
   Detail hero badge (`DetailMetaBadge.StatusKind`) and Library series list rows. Verify: `SeriesStatusStyleTests`.
2. **#20 Read-state glyphs.** `StatusBadgeVariant.Unread` + `IssueTileGlyphs.Resolve`; one `Ellipse.unreadDot` look across Library tiles/rows; Detail
   list shows the Unread badge. Verify: `ReadStateGlyphTests`.
3. **#18 Placeholder covers.** `Views/PlaceholderCoverText.cs` + `IPlaceholderCoverSource`; `AsyncCoverImage` shows it only after an EMPTY decode.
   Verify: `PlaceholderCoverTests` (incl. render + stale-generation cases).
4. **#26 Letter rail.** `AlphabetIndexEntry`, rail now shows for grouped-by-letter views and dims empty letters; grouped jump scrolls to the group header.
   Verify: `AlphabetIndexEntryTests`.

## Slice B — Detail screens
5. **#8 Backdrop toggle** (`HeroBackdrop`), **#9 series accent** (`CoverPalette`, `DetailAccentScope`, `DetailCosmetics`; `SeriesAccentColor`),
   migration `AddDetailCosmeticSettings`. Verify: `CoverPaletteTests`.
6. **#14 BrandMark pass.** Reading-list arc-source chip uses `BrandMark Family="Service"`.

## Slice C — App-wide
7. **#13** `Controls/EmptyIllustration.cs` on the Events and Reading Lists empty states. Verify: `EmptyIllustrationTests`.
8. **#25** Menu audit: icons + danger flags filled in six builders.
9. **#16** `InsightsChartHover`, `InsightsChartTheme.Visible` (≥3:1), growth cursor, `CategoryDonut` entry sweep. Verify: `InsightsChartPolishTests`.
10. **#21** `ActivityJobGrouping` + `ProgressArcRing`; drawer Active tab is one flat list with headings. Verify: `ActivityPolishTests`.
11. **#15** skin preview tile gains a poster tile + chip.
12. **#17** `DensityPresets`, three `Pb*Padding` tokens, `AppSettings.DensityPreset`, migration `AddDensityPreset`. Verify: `DensityPresetTests`.

## Test strategy
xUnit per project conventions; pure logic tested directly; custom-drawn controls verified through headless Skia frames; migration tests per new column;
full `Paperbunkr.App.Tests` run with `--blame-hang-timeout`. On-screen verification is the user's (no UI automation without permission).
