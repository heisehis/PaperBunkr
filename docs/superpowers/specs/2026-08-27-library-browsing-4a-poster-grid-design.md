# Library Browsing — Phase 4a: Poster Grid Consolidation

**Status:** Implemented 2026-08-27 (plan: `2026-08-27-library-browsing-4a-poster-grid-plan.md`).
Solution build clean; `Paperbunkr.App.Tests` 941/941, `Paperbunkr.Data.Tests` 453/453 green
(incl. new `LibraryPosterGridMigrationTests` + the poster-grid VM tests); app launches, the
migration applies cleanly to the real DB. On-screen verification of the grid layouts (grouped /
titles-off / density / Panorama, both skins, scrim hover, badges, multi-select, context menus)
is still pending — the standing GUI-automation caveat. Two small deviations from this spec, both
noted in the plan: `PosterTile` keeps its fixed 140×196 (Library uses bespoke templates, not the
control) and gets no `ShowText` property; and the card root stays a `Button` (the glow moved to
the inner cover `Border`).
**Slice 1 of 2 in Sub-project 4 of 7** (Library browsing) of the full UI rework — see
[Design Language Foundation](2026-08-24-design-language-foundation-design.md) for the full phase
breakdown. Phases 1–3 and 6 (with its icon-pass follow-up) are done; Phases 4/5/7 pending.
4a is the poster-forward grid; **4b** (its own later spec) is the toolbar / dropdown chrome
restyle and the row-based view modes (List/Details/Tiles).

## Background

`LibraryScreen.axaml` is ~2,200 lines. Its grid content area has **8 `LibraryViewMode` values**
(`CompactGrid`, `ComfortableGrid`, `CoverOnlyGrid`, `PanoramaGrid`, `List`, `Details`, `Tiles`,
`IssueList`), each rendered by a hand-rolled `DataTemplate` in `UserControl.Resources` — **~14
templates** counting the parallel Issue-granularity (`IssueListRow`) and Series-granularity
(`SeriesCardSample`) variants — and each with its own `ScrollViewer` block in the layout, split
again into grouped / ungrouped `ItemsControl`s.

The four grid modes are near-duplicates:

| Mode | Base card | Title | Differs by |
|---|---|---|---|
| `CompactGrid` | 110 × 160 | overlaid on cover | size |
| `ComfortableGrid` | 150 × 216 | below cover (`SeriesName` + `#N`) | size |
| `CoverOnlyGrid` | 150 × 216 | none | text hidden |
| `PanoramaGrid` | landscape, fixed 146h | below, wide | **orientation** |

`GridDensity` already exists (persisted `AppSettings.LibraryGridDensity`, a continuous multiplier)
and every fixed-box card width is `<base> * GridDensity`. Display toggles already exist:
`LibraryShowUnreadBadge` (default on), `LibraryShowPublisherBadge`, `LibraryShowLanguageBadge`,
`LibraryUseLanguageIcon`.

The [`PosterTile`](../../../src/Paperbunkr.App/Views/PosterTile.axaml) primitive (surface2 border,
`PbGlowRing` hover/focus, badge + progress slots) was built for this — Phase 3's Home screen
consumes the control directly. Its visual shell also lives as style classes in
[`Styles/Primitives.axaml`](../../../src/Paperbunkr.App/Styles/Primitives.axaml) (`Border.posterTile`
+ `:pointerover`/`:focus-visible` glow).

## Decisions (from the visual brainstorm)

- **Card:** cover, then title/meta in a row **below** it. A soft gradient scrim sits on the bottom
  of the cover — **slight at rest, stronger on `:pointerover`/`:focus-visible`** — for badge
  legibility and a consistent finished edge. **Nothing is drawn on the cover itself** (no overlaid
  title).
- **Badges on the tile:** publisher → a `pbChip` (top-left) showing the publisher **name as text**
  for now; a later "brand iconography" spec swaps that for a simplified monochrome letter-mark
  (initial + brand colour) with the text `pbChip` as the fallback — a drop-in, no structural change
  to 4a. Unread → a small accent dot (top-right). The **language badge is removed from the grid**
  — it moves to the Details view only
  (handled in 4b). `LibraryShowLanguageBadge`/`LibraryUseLanguageIcon` settings stay in the schema,
  just no longer consumed by the grid.
- **Density:** keep the **continuous `LibraryGridDensity` slider** (a `– density +` control — 4b
  styles it). Titles auto-hide below a card-width threshold regardless of the toggle.
- **Show titles:** new toggle. Off = the text row disappears entirely (today's `CoverOnlyGrid`).
- **Grouped view:** each group is a Bebas Neue (`PbTextHeading`) header + item count + a hair-line
  rule, then that group's own wrapping poster row.
- **Panorama:** stays as its **own** mode, restyled onto Phase 1 tokens; layout unchanged (wide
  crop + title + a short meta line — no synopsis).

## 1. `LibraryViewMode` enum

```csharp
public enum LibraryViewMode
{
    PosterGrid,     // was CompactGrid + ComfortableGrid + CoverOnlyGrid
    PanoramaGrid,
    List,
    Details,
    Tiles,
    IssueList,
}
```

`PosterGrid` is **first** deliberately: it becomes the CLR default (0), the desired default, *and*
the EF sentinel — all three coincide, the same clean case as `LibraryGroupField.None` /
`PageTransitionStyle.None` already in this codebase. That sidesteps the current
`HasSentinel(LibraryViewMode.CompactGrid)` (a removed value).

**EF config** (`PaperbunkrDbContext.cs`):
`.HasDefaultValue(LibraryViewMode.PosterGrid).HasSentinel(LibraryViewMode.PosterGrid)`, comment
updated to "PosterGrid (0) is both the CLR default and the desired default here."

Stored via `HasConversion<string>()`, so persisted rows hold `"CompactGrid"` / `"ComfortableGrid"`
/ `"CoverOnlyGrid"` strings that will no longer parse. A **data migration** fixes the singleton
`AppSettings` row (see §3).

## 2. `AppSettings.LibraryShowTileTitles`

New `bool`, default `true`. EF: `.HasDefaultValue(true)`. Loaded/saved by `LibraryScreenViewModel`
alongside the existing badge toggles (`_showTileTitles` field, `ShowTileTitles`
`[ObservableProperty]`, `OnShowTileTitlesChanged => SaveLibrarySettings()`, read in the settings-
load block, written in `SaveLibrarySettings`).

The grid binds an **`EffectiveShowTileTitles`** computed property:
`ShowTileTitles && PosterCardWidth >= TitleAutoHideThreshold` (threshold ≈ 108px — below that the
`SeriesName` line is unreadably cramped). It notifies on both `ShowTileTitles` and `GridDensity`
changes.

## 3. EF migration

One migration, `LibraryPosterGridConsolidation`:

- `AddColumn` `LibraryShowTileTitles` (bool, default `true`).
- Raw SQL data-fix on the singleton row, **in this order**:
  ```sql
  UPDATE AppSettings SET LibraryShowTileTitles = 0 WHERE LibraryViewMode = 'CoverOnlyGrid';
  UPDATE AppSettings SET LibraryViewMode = 'PosterGrid'
    WHERE LibraryViewMode IN ('CompactGrid', 'ComfortableGrid', 'CoverOnlyGrid');
  ```
- `Down()` reverses: drop the column; `UPDATE ... SET LibraryViewMode = 'ComfortableGrid' WHERE
  LibraryViewMode = 'PosterGrid'` (best-effort — the Compact/CoverOnly distinction is lost on
  downgrade, which is acceptable for a dev-only down-migration).
- Regenerate `PaperbunkrDbContextModelSnapshot.cs`.

A `CompactGrid` user's `GridDensity` is **not** rescaled — they land on `PosterGrid` at the same
density value, one base size larger (150 vs 110), and can slide density down. Not worth a
per-user rescale.

## 4. `LibraryScreenViewModel`

- Replace `IsCompactGrid` / `IsComfortableGrid` / `IsCoverOnlyGrid` with **`IsPosterGrid`**
  (`ViewMode == LibraryViewMode.PosterGrid`). `OnViewModeChanged` notifies the new name; drop the
  three old notifications.
- Replace `CompactCardWidth`/`CompactCardHeight`/`ComfortableCardWidth`/…/`CoverOnlyCardWidth`/…
  with **`PosterCardWidth` (`150 * GridDensity`)** and **`PosterCardHeight`** — the cover box
  (`~216 * GridDensity`, mirroring today's `ComfortableCardHeight`) **plus** a fixed text-row
  allowance (~34px) when `EffectiveShowTileTitles`, so the `VirtualizingWrapPanel`'s `ItemHeight`
  reserves the right space in both toggle states (today's `ComfortableGrid` overflows its
  `ItemHeight` box with the text row — this fixes that in passing).
  `TilesThumbWidth`/`TilesThumbHeight`/`TilesCardWidth` (row-based Tiles mode) are untouched.
  `GridDensity`'s setter notifies `PosterCardWidth`/`PosterCardHeight`/`EffectiveShowTileTitles`
  instead of the removed names.
- `DisplayModeLabel` switch: `PosterGrid => "Poster grid"`, drop the three old cases.
- `ShowTileTitles` / `EffectiveShowTileTitles` per §2.
- `SetViewMode` / `SetViewModeCommand` unchanged (still a bare `ViewMode = mode`).

## 5. `LibraryScreen.axaml`

### Shared styles (`Styles/Primitives.axaml`)

Add `Border.posterScrim` — an absolutely-filling gradient (transparent → `#8C000000` at the
bottom ~45%), `Opacity` 0.55 at rest, with a transition on `PbMotionFast`. Add
`Border.posterTile:pointerover Border.posterScrim` / `:focus-visible` → `Opacity` 1.0. `PosterTile`
itself gains a `Classes="posterScrim"` border in its cover grid (so Home benefits too) plus a new
`ShowText` styled `bool` property gating its Row-1 `StackPanel`.

### Templates

The 6 grid templates (`CompactGridItemTemplate`, `ComfortableGridItemTemplate`,
`CoverOnlyGridItemTemplate` and their `Series*` siblings) are **deleted** and replaced with **2**:

- **`PosterGridIssueTemplate`** (`x:DataType="models:IssueListRow"`) — root
  `<Border Classes="posterTile">`, `Grid RowDefinitions="*,Auto"`:
  - Row 0: `ClipToBounds` border → `Grid` with the cover `Image`
    (`{Binding Id, Converter={x:Static views:CoverImageConverter.Instance}}` over `CoverBrush`),
    `Border.posterScrim`, publisher `pbChip` (`ShowPublisherBadge` + `HasPublisher`), unread dot
    (`ShowUnreadBadge` + `!IsRead`), and the selection `CheckBox.tileSelect`
    (`forceVisible` on `HasSelection`).
  - Row 1: `StackPanel` `IsVisible="{Binding …EffectiveShowTileTitles}"` — `SeriesName` (semibold,
    `PbTextBrush`, ellipsis) + `Number` (`#{0}`, `PbTextFaintBrush`).
  - The existing `Button.ContextMenu` (Go to Series / Show in Explorer / Set Content Type / … /
    Delete Issue), `KeyDown="OnCardKeyDown"`, `PointerPressed="OnTilePointerPressed"`, and
    click → `IssueList.OpenIssueCommand` all carry over. Root becomes a `Border`, not a `Button`,
    so click wiring moves to a `PointerPressed` handler on the border (mirroring `PosterTile`'s own
    `OnPointerPressed`) — or the root stays a `Button Classes="card posterTile"` if `Button`
    accepts the `BoxShadow` the glow needs (confirm at implementation time; `PosterTile` uses a
    `Border` specifically because `Button` was uncertain — match whatever works).
- **`PosterGridSeriesTemplate`** (`x:DataType="models:SeriesCardSample"`) — same shape, cover from
  `{Binding CoverIssueId, Converter=…}`, Row 1 shows `Name` + `Sub`, the Series context menu
  (Set Status / Set Reading Status / Go to Series / Delete Series), click → series detail.

### Layout

`CompactScrollViewer` / `ComfortableScrollViewer` / `CoverOnlyScrollViewer` (three blocks, each
~50 lines with grouped + ungrouped × Issue + Series `ItemsControl`s) collapse to **one
`PosterGridScrollViewer`** (`IsVisible="{Binding IsPosterGrid}"`) with the same 4-way inner split
but pointing at the 2 new templates and `PosterCardWidth`/`PosterCardHeight`. Grouped headers use
`PbTextHeading` + a count `Run` + a 1px `PbBorderBrush` rule (replacing the local `groupHeader`
style — that style's other consumers in the row modes are 4b's problem, leave it defined for now).

`PanoramaScrollViewer` + `SeriesPanoramaGridItemTemplate` / `PanoramaGridItemTemplate` stay; only
their hardcoded colors/local styles swap to `PbSurface*`/`PbBorder*`/`PbRadius*` tokens and the
`posterScrim` class. No structural change.

### Toolbar view-mode dropdown

The view-mode `Popup` list (`LibraryViewModeOption_*` buttons, currently 8) drops to 6:
`PosterGrid`, `PanoramaGrid`, `List`, `Details`, `Tiles`, `IssueList`. New AutomationId
`LibraryViewModeOption_PosterGrid`; `_CompactGrid`/`_ComfortableGrid`/`_CoverOnlyGrid` removed.
The dropdown also gains a **"Show titles"** checkbox row (bound to `ShowTileTitles`). Full visual
restyle of this dropdown is 4b; 4a only changes the option set + adds the one checkbox.

## 6. Tests

- `LibraryScreenViewModelTests`:
  - `SetViewMode_UpdatesIsXProperties` — retarget the `Assert.False(vm.IsComfortableGrid/…)` lines
    to `IsPosterGrid`.
  - New: `PosterGrid_IsDefault_AndCardSizeTracksDensity` — default `ViewMode` is `PosterGrid`;
    `PosterCardWidth == 150`, and `GridDensity = 1.5` → `225`.
  - New: `ShowTileTitles_PersistsAndAutoHidesAtLowDensity` — toggling persists to `AppSettings`;
    `EffectiveShowTileTitles` is `false` once `GridDensity` drops the card below the threshold even
    with the toggle on.
  - New: `Migration_MapsLegacyViewModes` — seed an `AppSettings` row string-valued
    `"CoverOnlyGrid"`, run the DB up-migration against a scratch DB, assert `LibraryViewMode ==
    "PosterGrid"` and `LibraryShowTileTitles == false`; same for `"CompactGrid"` →
    `"PosterGrid"` + titles still `true`. (Follows the existing migration-test pattern — a real
    `dotnet ef`-applied scratch DB, not a mock.)
- `Paperbunkr.App.UiTests`: `LibraryListLayoutPersistenceTests` uses
  `LibraryViewModeOption_List` — unaffected. No UI test targets the removed grid option ids
  (grepped — only `_IssueList` and `_List` are referenced). If a UI smoke test for the poster grid
  is cheap to add (open Library, click `LibraryViewModeOption_PosterGrid`, assert a
  `PosterTileCard` is present), add one; otherwise flag poster-grid rendering for manual check.
- Build with the `CLAUDE.md` AVLN2000 guard (`.axaml` + new `.cs` property on `PosterTile`).
- Manual on-screen: the 4 layout states from the brainstorm — ungrouped poster grid, grouped
  (Bebas headers), titles-off, Panorama — at low / mid / high density, both skins; hover
  strengthens the scrim; publisher chip + unread dot; multi-select checkbox still works; context
  menus intact; a `CompactGrid`/`CoverOnlyGrid` user's persisted state lands correctly after the
  migration.

## Risks / notes

- **`Button` vs `Border` as the card root.** `PosterTile` uses a `Border` because `Button`'s
  `BoxShadow` support was uncertain. The current grid templates use `Button Classes="card"`. If a
  `Border` root is needed for the glow, the click/keyboard wiring (`OnCardKeyDown`,
  `OnTilePointerPressed`, `Command`) moves onto the border via code-behind handlers — a real but
  contained change. Decide with a 5-minute spike at implementation time; the plan sequences it as
  its own step.
- **`groupHeader` local style** is shared with the row-based modes (List/Details). 4a introduces
  the new `PbTextHeading` group header for the poster grid only and leaves `groupHeader` defined;
  4b removes it once the row modes are restyled.
- **Density → column count.** `VirtualizingWrapPanel` already derives columns from
  `ItemWidth`/`ItemHeight`; feeding it `PosterCardWidth`/`PosterCardHeight` is the same mechanism
  the fixed-box modes use today. No new responsive-layout code.
- **Language badge removal** is a deliberate scope call, not an oversight — it's rarely set and
  clutters the tile corner budget. It reappears in Details (4b). Settings columns are retained so
  the toggle in Preferences (if any) doesn't dangle — confirm at implementation whether a
  Preferences control references it.
