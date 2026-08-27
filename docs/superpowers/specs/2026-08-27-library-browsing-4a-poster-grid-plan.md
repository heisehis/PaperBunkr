# Library Browsing — Phase 4a: Poster Grid Consolidation — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-27-library-browsing-4a-poster-grid-design.md*

**Done 2026-08-27** — all 8 steps. Build clean, `Paperbunkr.App.Tests` 941/941 +
`Paperbunkr.Data.Tests` 453/453. Deviations: `PosterTile` keeps its fixed size + no `ShowText`
(Library doesn't consume the control — bespoke templates instead); card root stayed a `Button`,
glow moved to the inner cover `Border`. On-screen grid verification pending.

Surveyed against current source before writing:

- `LibraryViewMode` (`src/Paperbunkr.Data/Entities/LibraryViewMode.cs`): 8 values, `CompactGrid`
  first. `AppSettings.LibraryViewMode` default `ComfortableGrid`
  (`src/Paperbunkr.Data/Entities/AppSettings.cs:203`); EF `PaperbunkrDbContext.cs:591-594`
  `.HasDefaultValue(ComfortableGrid).HasSentinel(CompactGrid)`, `HasConversion<string>()`.
- Display toggles already in `AppSettings` (`:208-218`) + wired in `LibraryScreenViewModel`
  (load `:255-258`, save `:305-308`): `LibraryShowUnreadBadge` (default true),
  `LibraryShowPublisherBadge`, `LibraryShowLanguageBadge`, `LibraryUseLanguageIcon`. No
  `ShowTileTitles` equivalent.
- `LibraryScreenViewModel`: `_viewMode` `[ObservableProperty]` (`:1478`), `IsCompactGrid`/
  `IsComfortableGrid`/`IsCoverOnlyGrid`/`IsPanoramaGrid`/… (`:1480-1487`), `OnViewModeChanged`
  fires 8 `OnPropertyChanged` + `SaveLibrarySettings` (`:1489-1501`), `SetViewMode` = bare
  `ViewMode = mode` (`:1507`), `DisplayModeLabel` switch (`:1509-1520`). `GridDensity`
  `Math.Clamp(value, 0.6, 1.6)` (`:1538`), setter notifies 9 card-size props + saves (`:1541-1550`).
  `CompactCardWidth => 110 * GridDensity` … `ComfortableCardWidth => 150 *` … `CoverOnlyCardWidth
  => 150 *` … `TilesThumbWidth/Height/CardWidth` (`:1555-1563`), `PanoramaCardHeight =>
  SeriesCardSample.PanoramaHeight` (`:1566`).
- `LibraryScreen.axaml`: local styles `Button.card` (`:55`, transparent, stretch), `Border.cover`
  (`:63`, radius 7, `BoxShadow "0 8 24 -12 #80000000"`, `ClipToBounds`), `Button.modeOption`
  (`:72`), `TextBlock.groupHeader` (`:90`, bold 13, `PbTextBrush`). Templates in
  `UserControl.Resources`: `CompactGridItemTemplate` (`:139`), `ComfortableGridItemTemplate`
  (`:250`), `CoverOnlyGridItemTemplate` (`:369`), `PanoramaGridItemTemplate` (`:481`), row modes
  `ListItemTemplate`/`DetailsItemTemplate`/`TilesItemTemplate`, then the `Series*` variants
  (`:883-1495`). Grid `ScrollViewer` blocks: `CompactScrollViewer` (`:1839`), `ComfortableScrollViewer`
  (`:1889`), `CoverOnlyScrollViewer` (`:1939`), plus a Panorama one further down — each an outer
  `Grid` with `Panel IsVisible="{Binding IsIssueGranularity}"` (grouped + ungrouped `ItemsControl`
  on `IssueList.Rows`/`IssueList.Groups`) and `Panel IsVisible="{Binding IsSeriesGranularity}"`
  (`Covers`/`Groups`). View-mode dropdown: 8 `Button Classes="modeOption"` rows,
  `AutomationProperties.AutomationId="LibraryViewModeOption_<Mode>"` (`:1704-1719`).
- `LibraryScreen.axaml.cs`: `OnCardKeyDown` (`:31`), `OnTilePointerPressed` (`:56`, "adapted for a
  Button root instead of a Border"), `OnSeriesTilePointerPressed` (`:76`), `OnAlphabetIndexLetterClick`.
- `IssueListRow` (`src/Paperbunkr.App/Models/IssueListRow.cs`): `Id`, `SeriesId`, `SeriesName`,
  `Number`, `Title`, `Publisher`, `IsRead`, `LanguageIso`, `FilePath`, `CoverBrush`,
  `ContentTypeLabel`; computed `HasFile`/`HasPublisher`/`HasLanguage`/`IsMangaFamily`; `IsSelected`
  is a mutable `ObservableObject` property. Cover image via
  `{Binding Id, Converter={x:Static views:CoverImageConverter.Instance}}`.
- `SeriesCardSample`: `SeriesId`, `Name`, `Sub`, `Publisher`, `CoverBrush`, `CoverIssueId`,
  `IsSelected`, `HasFile`, `PanoramaWidth`, `PanoramaHeight` const 146.
- `PosterTile.axaml` / `.axaml.cs`: `Border x:Name="Root" Classes="posterTile"`, `Grid
  RowDefinitions="*,Auto" Width="140"`, cover `Border Height="196"`, `CoverSource`/`TitleText`/
  `MetaText`/`BadgeText`/`ShowProgress`/`ProgressFraction`/`Command`/`CommandParameter` styled
  props, `OnPointerPressed` fires `Command`.
- `Styles/Primitives.axaml:116-131`: `Border.posterTile` (surface2, `PbRadiusSm`, hand cursor,
  `BoxShadowsTransition` on `PbMotionFast`), `:pointerover`/`:focus-visible` → `PbGlowRing`
  (`BoxShadows` resource `:111`). `Border.pbChip` (`:73`), `Styles/Typography.axaml:19`
  `TextBlock.pbTextHeading`.
- Migration pattern: `dotnet ef migrations add <Name>` scaffolds `AddColumn`; hand-add
  `migrationBuilder.Sql("""…""")` for data fixes (see
  `20260823123052_AddIssueTags.cs`). Migration test pattern
  (`src/Paperbunkr.Data.Tests/AddIssueTagsMigrationTests.cs`): own temp `.db`,
  `context.GetService<IMigrator>()`, `.Migrate(PriorMigration)`, seed raw SQL, `.Migrate()`,
  assert; `SqliteConnection.ClearAllPools()` in `Dispose`.
- Tests referencing the removed modes: `LibraryScreenViewModelTests.cs:341-343`
  (`Assert.False(vm.IsComfortableGrid/IsCompactGrid/IsCoverOnlyGrid)`). `Paperbunkr.App.UiTests`
  only references `LibraryViewModeOption_IssueList` / `_List` (grepped — none of the removed ids).

**Resolved from the design's flagged spike:** the card root **stays a `Button Classes="card"`**
(keeps `ContextMenu`, `Command`, `OnCardKeyDown`, `OnTilePointerPressed` unchanged). `Border.cover`
already carries a `BoxShadow`, so the glow ring goes on the inner cover `Border` via a
`Button.card:pointerover`/`:focus-within` selector — no `Border`-root migration needed.

**One YAGNI trim from the spec:** `PosterTile` (the control) gets the shared `posterScrim` border
but **not** a `ShowText` property — nothing consumes it (Home always shows titles; the Library
templates don't use the control, they gate their own text row's `IsVisible`).

---

## Step 1: `LibraryViewMode` enum + `AppSettings` + EF config

**Files:** `src/Paperbunkr.Data/Entities/LibraryViewMode.cs` (edit),
`src/Paperbunkr.Data/Entities/AppSettings.cs` (edit),
`src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit)

**What:**
- `LibraryViewMode`: reorder to `PosterGrid, PanoramaGrid, List, Details, Tiles, IssueList`.
  Update the doc comment: `PosterGrid` replaces Compact/Comfortable/Cover-only (density slider +
  show-titles toggle carry the old distinctions).
- `AppSettings`: `LibraryViewMode` default → `LibraryViewMode.PosterGrid`. Add
  `public bool LibraryShowTileTitles { get; set; } = true;` with a `<summary>` next to the other
  `LibraryShow*Badge` toggles.
- `PaperbunkrDbContext` `:591-594`:
  `.HasDefaultValue(LibraryViewMode.PosterGrid).HasSentinel(LibraryViewMode.PosterGrid)`; comment
  → "PosterGrid (0) is both the CLR default and the desired default here." Add
  `builder.Property(a => a.LibraryShowTileTitles).HasDefaultValue(true);` beside the other
  `LibraryShow*` lines (`:616-619`).

**Depends on:** none
**Verify:** `dotnet build src/Paperbunkr.Data/Paperbunkr.Data.csproj` — Data project compiles
(the App project won't until Step 3+5).

## Step 2: EF migration `LibraryPosterGridConsolidation`

**Files:** `src/Paperbunkr.Data/Migrations/<timestamp>_LibraryPosterGridConsolidation.cs` (new,
scaffolded then hand-edited), its `.Designer.cs` (new, generated),
`src/Paperbunkr.Data/Migrations/PaperbunkrDbContextModelSnapshot.cs` (regenerated)

**What:** `dotnet ef migrations add LibraryPosterGridConsolidation
--project src/Paperbunkr.Data`. It scaffolds `AddColumn<bool>("LibraryShowTileTitles", "AppSettings",
defaultValue: true)`. Hand-add, after the `AddColumn`, in `Up`:
```csharp
migrationBuilder.Sql("UPDATE AppSettings SET LibraryShowTileTitles = 0 WHERE LibraryViewMode = 'CoverOnlyGrid';");
migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'PosterGrid' WHERE LibraryViewMode IN ('CompactGrid', 'ComfortableGrid', 'CoverOnlyGrid');");
```
`Down`: `migrationBuilder.Sql("UPDATE AppSettings SET LibraryViewMode = 'ComfortableGrid' WHERE
LibraryViewMode = 'PosterGrid';");` then the scaffolded `DropColumn`.

**Depends on:** Step 1
**Verify:** `dotnet ef database update --project src/Paperbunkr.Data` applies cleanly against a
scratch DB (`PaperbunkrDbContext.DatabasePathOverride` or a throwaway `Data Source=`); migration
test lands in Step 7.

## Step 3: `LibraryScreenViewModel`

**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit)

**What:**
- Replace `IsCompactGrid`/`IsComfortableGrid`/`IsCoverOnlyGrid` with
  `public bool IsPosterGrid => ViewMode == LibraryViewMode.PosterGrid;`. `OnViewModeChanged`:
  drop the three old `OnPropertyChanged`, add `nameof(IsPosterGrid)`.
- Card sizes: replace `CompactCardWidth`/`Height`, `ComfortableCardWidth`/`Height`,
  `CoverOnlyCardWidth`/`Height` with:
  - `PosterCardWidth => 150 * GridDensity;`
  - `PosterCardHeight => 216 * GridDensity + (EffectiveShowTileTitles ? PosterTitleRowHeight : 0);`
    (`PosterTitleRowHeight` const ≈ 34)
  `GridDensity`'s setter: notify `PosterCardWidth`/`PosterCardHeight`/`EffectiveShowTileTitles`
  instead of the removed names; keep the `TilesThumb*`/`TilesCardWidth` notifications.
- `[ObservableProperty] private bool _showTileTitles = true;` +
  `partial void OnShowTileTitlesChanged(bool value)` → `OnPropertyChanged(nameof(PosterCardHeight));
  OnPropertyChanged(nameof(EffectiveShowTileTitles)); SaveLibrarySettings();`
- `public bool EffectiveShowTileTitles => ShowTileTitles && PosterCardWidth >= PosterTitleHideThreshold;`
  (`const double PosterTitleHideThreshold = 108;`)
- `DisplayModeLabel`: `LibraryViewMode.PosterGrid => "Poster grid"`, drop the three old cases.
- Settings: load `_showTileTitles = settings.LibraryShowTileTitles;` (`:255` area, direct field
  write), save `settings.LibraryShowTileTitles = ShowTileTitles;` (`:305` area).
- `SetViewMode` unchanged.

**Depends on:** Step 1
**Verify:** compiles once Step 5 lands (XAML still references removed props until then); the VM
unit tests in Step 7.

## Step 4: shared scrim style + `PosterTile` scrim

**Files:** `src/Paperbunkr.App/Styles/Primitives.axaml` (edit),
`src/Paperbunkr.App/Views/PosterTile.axaml` (edit)

**What:**
- `Primitives.axaml`: add `Border.posterScrim` — `IsHitTestVisible="False"`, a bottom-anchored
  `LinearGradientBrush` background (transparent → `#8C000000`, stops ~0.5→1.0), `Opacity` 0.55,
  a `DoubleTransition` on `Opacity` (`PbMotionFast`/`PbMotionEase`). Add
  `Border.posterTile:pointerover Border.posterScrim` and `Border.posterTile:focus-visible
  Border.posterScrim` → `Opacity` 1.0.
- `PosterTile.axaml`: add `<Border Classes="posterScrim"/>` into the cover `Grid` (row 0), above
  the badge/progress children. Remove the hardcoded `Width="140"` on the outer `Grid` and
  `Height="196"` on the cover `Border` — let the consumer's container size it (Home's rows set an
  explicit tile size on the control already; confirm Home still renders at the right size, adjust
  Home's usage if it relied on the intrinsic 140×196).

**Depends on:** none
**Verify:** `dotnet build` (after Step 5); manual — Home rows still look right, covers now have the
scrim edge.

## Step 5: `LibraryScreen.axaml` — templates, layout, dropdown

**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit)

**What:**
- **Styles:** add `Button.card:pointerover Border.posterCover` / `Button.card:focus-within
  Border.posterCover` → `BoxShadow="{StaticResource PbGlowRing}"` (+ a `BoxShadowsTransition` on
  `Border.posterCover`). Add `TextBlock.posterGroupHeader` (or reuse `pbTextHeading`) for the
  Bebas group header. Keep `Button.card`, `Border.cover`, `TextBlock.groupHeader` (row modes still
  use them — 4b removes `groupHeader`).
- **New templates** in `UserControl.Resources`:
  - `PosterGridIssueTemplate` (`x:DataType="models:IssueListRow"`): `Button Classes="card"`,
    `Command="{Binding …IssueList.OpenIssueCommand}" CommandParameter="{Binding}"
    KeyDown="OnCardKeyDown" PointerPressed="OnTilePointerPressed"`, the full existing issue
    `ContextMenu` (copy verbatim from `CompactGridItemTemplate`). Content: `Grid
    RowDefinitions="*,Auto"` — row 0 `Border Classes="cover posterCover"` →
    `Grid`{ cover `Image` over `CoverBrush`; `Border Classes="posterScrim"`; publisher `Border
    Classes="pbChip"` top-left `IsVisible="{Binding HasPublisher}"` gated by
    `…ShowPublisherBadge`, text `{Binding Publisher}`; unread dot top-right
    `IsVisible="{Binding !IsRead}"` gated by `…ShowUnreadBadge`; `CheckBox Classes="tileSelect"`
    bottom-right, `forceVisible` on `…HasSelection` }; row 1 `StackPanel
    IsVisible="{Binding …EffectiveShowTileTitles}"` — `SeriesName` (semibold `PbTextBrush`
    ellipsis) + `Number` (`#{0}` `PbTextFaintBrush`). **No language badge.**
  - `PosterGridSeriesTemplate` (`x:DataType="models:SeriesCardSample"`): same shape,
    `PointerPressed="OnSeriesTilePointerPressed"`, cover `{Binding CoverIssueId, Converter=…}`,
    the existing series `ContextMenu` (from `SeriesCompactGridItemTemplate`), row 1 = `Name` +
    `Sub`, click → series-detail command (whatever `SeriesCompactGridItemTemplate` used).
- **Delete** `CompactGridItemTemplate`, `ComfortableGridItemTemplate`, `CoverOnlyGridItemTemplate`,
  `SeriesCompactGridItemTemplate`, `SeriesComfortableGridItemTemplate`,
  `SeriesCoverOnlyGridItemTemplate`.
- **Layout:** delete `CompactScrollViewer` / `ComfortableScrollViewer` / `CoverOnlyScrollViewer`.
  Add one `ScrollViewer x:Name="PosterGridScrollViewer" Grid.Row="1" IsVisible="{Binding
  IsPosterGrid}"` with the same 4-way inner structure (Issue grouped/ungrouped + Series
  grouped/ungrouped), `VirtualizingWrapPanel ItemWidth="{Binding PosterCardWidth}"
  ItemHeight="{Binding PosterCardHeight}"`, pointing at the 2 new templates. Grouped branches:
  header `TextBlock Classes="pbTextHeading"` + a count `Run` + a 1px `PbBorderBrush` rule, then the
  group's `VirtualizingWrapPanel`.
- **Panorama:** leave `PanoramaScrollViewer` + `PanoramaGridItemTemplate` /
  `SeriesPanoramaGridItemTemplate` structurally intact; swap any hardcoded hex / local color
  setters to `PbSurface*`/`PbBorder*`/`PbRadius*` tokens; add `Border Classes="posterScrim"` on
  its cover crop. `AutomationId` on the option becomes `LibraryViewModeOption_PanoramaGrid`
  (unchanged).
- **View-mode dropdown** (`:1703-1719`): remove the `CompactGrid`/`ComfortableGrid`/`CoverOnlyGrid`
  rows; add one `Button Classes="modeOption" Classes.active="{Binding IsPosterGrid}" Content="Poster
  grid"` `CommandParameter="{x:Static entities:LibraryViewMode.PosterGrid}"`
  `AutomationProperties.AutomationId="LibraryViewModeOption_PosterGrid"`. Below the mode rows add a
  `CheckBox Content="Show titles" IsChecked="{Binding ShowTileTitles}"
  AutomationProperties.AutomationId="LibraryShowTitlesToggle"` (full restyle of this dropdown is 4b).

**Depends on:** Steps 1, 3, 4
**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` with the CLAUDE.md AVLN2000
guard (delete `obj/Debug/net8.0/Paperbunkr.App.dll` + `.pdb`, confirm the weave ran by launching);
**manual on-screen** is the real check for this step.

## Step 6: code-behind check

**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit if needed)

**What:** `OnCardKeyDown` / `OnTilePointerPressed` / `OnSeriesTilePointerPressed` are already
written for a `Button` root with `DataContext` an `IssueListRow`/`SeriesCardSample` — the new
templates keep that shape, so likely **no change**. Confirm the handlers don't reference a
now-removed element `x:Name`. `OnAlphabetIndexLetterClick` is unrelated.

**Depends on:** Step 5
**Verify:** build green; keyboard nav (arrows/Enter) + range-select (shift/ctrl-click) still work
on the poster grid.

## Step 7: tests

**Files:** `src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit),
`src/Paperbunkr.Data.Tests/LibraryPosterGridMigrationTests.cs` (new)

**What:**
- `LibraryScreenViewModelTests`:
  - `SetViewMode_UpdatesIsXProperties` — retarget `Assert.False(vm.IsComfortableGrid/…)` to
    `Assert.False(vm.IsPosterGrid)`; the `DisplayModeLabel` assert already checks `"Tiles"`.
  - New `PosterGrid_IsDefault_AndCardWidthTracksDensity` — fresh VM: `ViewMode ==
    LibraryViewMode.PosterGrid`, `IsPosterGrid` true, `PosterCardWidth == 150`; `GridDensity =
    1.5` → `PosterCardWidth == 225`.
  - New `ShowTileTitles_PersistsAndAutoHidesAtLowDensity` — `ShowTileTitles = false` round-trips
    through `AppSettings` (new VM instance reads it back); with `ShowTileTitles = true` and
    `GridDensity = 0.6` (`PosterCardWidth == 90 < 108`), `EffectiveShowTileTitles` is `false`.
  - New `DisplayModeLabel_PosterGrid` — `"Poster grid"`.
- New `LibraryPosterGridMigrationTests` (mirror `AddIssueTagsMigrationTests`): `PriorMigration =
  "<AddRenderingBackendSettings timestamp>"`; migrate to prior, `INSERT INTO AppSettings`
  (Id 1, `LibraryViewMode='CoverOnlyGrid'`, other NOT NULL cols filled), `.Migrate()`, assert
  `LibraryViewMode == "PosterGrid"` and `LibraryShowTileTitles == 0`. Second case: seed
  `'CompactGrid'` → assert `'PosterGrid'` + `LibraryShowTileTitles == 1`.

**Depends on:** Steps 1-3
**Verify:** `dotnet test src/Paperbunkr.App.Tests` + `src/Paperbunkr.Data.Tests` green.

## Step 8: full build + manual verification + doc status

**Files:** `docs/superpowers/specs/2026-08-27-library-browsing-4a-poster-grid-design.md` (status)

**What:** solution build (AVLN2000 guard), full `dotnet test`, launch the app. Manual checklist
from the design §6: ungrouped poster grid, grouped (Bebas headers + count + rule), titles-off,
Panorama — at low/mid/high density, both skins; hover strengthens the scrim; publisher `pbChip` +
unread dot; multi-select checkbox + keyboard nav intact; context menus intact; a persisted
`CompactGrid`/`CoverOnlyGrid` state lands as `PosterGrid` (+ titles-off for CoverOnly) after the
migration runs on first launch. Flip the design doc status to Implemented with what was verified
vs. left to manual.

**Depends on:** Steps 1-7
