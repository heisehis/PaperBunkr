# Library Browsing — Phase 4b: Toolbar Rework, Row Modes, Details Columns — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-27-library-browsing-4b-toolbar-rework-design.md*

**Status:** Steps 1–7 done (2026-08-27); Step 8 (doc status) below. Build clean on net10.0,
`Paperbunkr.App.Tests` 949/949, `Paperbunkr.Data.Tests` 454/454, all Library/ComicList UI tests
green (13/13 — the one failing `HomeScreenTests` case is pre-existing, unrelated, Home untouched).

**Step 6 — configurable Details table:** landed the **dynamic (declarative) approach**, no
fallback needed. `DetailsCellConverter` / `DetailsSortGlyphConverter` (`Views/DetailsCellConverters.cs`);
the header and every data row are an `ItemsControl` over `DetailsColumns` with fixed per-column
`Width` (hidden columns drop out of the horizontal `StackPanel`, so the two stay aligned with no
width-sync code). Header cells are `Button Classes="detailsHeader"` → `SetDetailsSortCommand`, with
a live `↑`/`↓` glyph via `MultiBinding`; right-click `ContextMenu` lists every `ColumnFields`
descriptor as a checkable `MenuItem` two-way-bound to `DetailsColumn.IsVisible`. `DetailsColumn`
gained a `Width` (`WideDetailsColumns` set → 220, else 150). Series granularity keeps its fixed
`SeriesDetailsItemTemplate`. New `LibraryDetailsColumnsTests` (header renders + click-sorts +
flips). On-screen verified via screenshots: two-zone bar, chips row, tabbed popup (capped at
`MaxHeight 420` + scroll so the tab strip stays pinned), Details header with sort glyph, Add
overlay, empty state.

**Step 7 — row modes + group headers:** all 8 `TextBlock.groupHeader` usages replaced with the 4a
`DockPanel` header (`pbTextHeading` + count + rule); `groupHeader` style deleted. List/Tiles rows:
`PbRadius`/`PbRadiusSm` token radii + a `Border.libRow` `:pointerover` → `PbSurface2Brush` tint.

---

**Superseded status line (Step 5):** Toolbar extracted; new icons; `IssueListFieldCatalog`
column projections + tests; `AppSettings.LibraryDetailsColumns` + migration + `DetailsColumn` VM
wiring + tests; **Step 5** — VM toolbar rework (one `IsViewSortOpen` tabbed popup, `ViewSortTab`
enum, chips computed props, `IsAddIssueOpen` + open/close, `ClearAllFilters` + per-chip clears,
empty-state props, `GoLibraryFoldersPreferences` ctor callback wired in `MainViewModel`),
`LibraryToolbar.axaml` full rewrite (two-zone Row 1, chips Row 2, selection second row, tabbed
View/Sort/Group popup), Add-issue FloatingPanel overlay + Escape/backdrop in `LibraryScreen.axaml`,
new empty state, all 6 affected UI test files rewritten around a new `LibraryToolbarDriver` helper
(+ new `LibraryAddIssueOverlayTests`). Build clean on net10.0, `Paperbunkr.App.Tests` 949/949,
`Paperbunkr.Data.Tests` 454/454, updated Library UI tests green.

Deviations from the design: a **Filter** pill stays in Row 1 (opens the filter popup) rather than
living only as an "+ Add filter" chip — the chips row is `IsVisible`-gated so a chip-only entry
point would vanish when no filters are active. The View & Sort button's *accessible* name carries
the active display mode (`"View and Sort: {mode}"`) for test observability; its visible label is
still the static "View & Sort".

> Pre-existing failure unrelated to 4b: `HomeScreenTests.AllFiveModules_RenderTheirEmptyStates_OnAFreshLibrary`
> fails in isolation; that file is already `M` in the working tree (net10 migration churn), Home was
> not touched by this work.

> **Note (pre-existing, flagged to user):** `PaperbunkrDbContextModelSnapshot.cs` at HEAD was badly
> stale (missing ~10 migrations' worth of model — `IssueTags`, `SeriesTitles`, `IssuePages`,
> `RenderingBackend`, etc.), and several migrations on this branch (`AddRenderingBackendSettings`,
> `LibraryPosterGridConsolidation`, …) are uncommitted. `dotnet ef migrations add` regenerated the
> snapshot to match the true model — a large but *correct* diff. `dotnet ef database update` replays
> all migrations cleanly through the new one; `has-pending-model-changes` is clean.

## Environment note (changed since the design was written)

The solution now targets **`net10.0`** (was `net8.0`) on **Avalonia 12.1.1**, and the App project
picked up new package refs (FluentAvaloniaUI 3.1.0, FluentIcons.Avalonia, Optris.Icons.Avalonia,
DialogHost.Avalonia, Avalonia.AvaloniaEdit) plus a `Paperbunkr.Plugins` project ref — all
currently **uncommitted** in the working tree. Impact on this plan:

- The `CLAUDE.md` AVLN2000 guard paths become `src/Paperbunkr.App/obj/Debug/net10.0/Paperbunkr.App.dll`
  (+ `.pdb`). `dotnet build -t:Rebuild` still works as the alternative.
- No `Avalonia.Controls.DataGrid` package was added and none is planned — the Details table stays
  hand-rolled (design §8). FluentIcons/Optris are available but the design (§3) wants new icons
  hand-drawn in the existing `StreamGeometry` house style, so we do that, not a font-icon control.
- Nothing else in the design is affected.

## Survey (verified against current source)

- **`LibraryScreen.axaml`** (1722 lines). `UserControl.Styles`: `Button.toolbarPill` (`:16`,
  `CornerRadius 999`, `PbChromeBrush` bg — pre-Phase-1 token), `.open` (`:27`), `toolbarPill Border.icon`
  (`:32`), `Border.dropdown` (`:41`, `PbChromeBrush`, radius 7, `BoxShadow`), `TextBlock.dropdownRow`
  (`:49`), `Button.card` (`:55`), `Border.cover` (`:63`), `Border.posterCover` + glow (`:73`),
  `Button.modeOption` / `.active` (`:94`), `TextBlock.groupHeader` (`:112`, bold 13),
  `Button.continueReading` (`:120`), `CheckBox.tileSelect` / `.forceVisible` (`:144`).
- **Templates** in `UserControl.Resources`: `PanoramaGridItemTemplate` (`:161`), `ListItemTemplate`
  (`:279`), `DetailsItemTemplate` (`:375` — fixed `Grid ColumnDefinitions="52,*,110,70,70,140"`,
  columns thumb/Title/Series/Number/Read-dot/Publisher), `TilesItemTemplate` (`:466`),
  `Series*` siblings `SeriesPanoramaGridItemTemplate` (`:563`), `SeriesListItemTemplate` (`:667`),
  `SeriesDetailsItemTemplate` (`:742`), `SeriesTilesItemTemplate` (`:809`); poster grid
  `PosterGridIssueTemplate` (`:885`), `PosterGridSeriesTemplate` (`:989`). Every issue template
  carries the same ~40-line `ContextMenu`; the poster grid's grouped header already uses the 4a
  `DockPanel` + `pbTextHeading` + count + rule pattern (`:1447`, `:1473`).
- **Toolbar markup** `:1092–1428`: one `<Border Grid.Row="0" Padding="24,14">` → `<Panel>` holding
  (a) the browse row `StackPanel` `IsVisible="{Binding !HasAnySelection}"` (browse `‹`/`›`, search
  `TextBox` `Width=260` `Watermark`, `SearchModeButton`, `FilterButton`, `SortButton`, `GroupButton`,
  `DisplayButton`, `AddButton` — all `Classes="toolbarPill"`), (b) 6 `Popup`s each keyed to an
  `x:Name`'d button (`IsSearchModeOpen`/`IsFilterOpen`/`IsSortOpen`/`IsGroupOpen`/`IsDisplayOpen`/`IsAddOpen`),
  (c) issue-selection action `StackPanel` `IsVisible="{Binding HasSelection}"`, (d) series-selection
  action `StackPanel` `IsVisible="{Binding HasSeriesSelection}"`, (e) the `AddToListButton` `Popup`.
- **Content-area `ScrollViewer`s** `:1435–1683`: `PosterGridScrollViewer`, `PanoramaScrollViewer`,
  `ListScrollViewer`, Details `DockPanel` (`:1577` — two fixed header `Grid`s, one per granularity,
  then `DetailsScrollViewer`), `TilesScrollViewer`, the `ContentControl` for Comic List. Then the
  A-Z indexer `Border` (`:1690`) and the current empty state (`:1711`, a `StackPanel` with a raster
  `Border.icon` + `"No series match your filters."`, gated on `!HasAnyResults`).
- **`LibraryScreen.axaml.cs`** (153 lines): `OnCardKeyDown` (`:31`), `OnTilePointerPressed` (`:56`),
  `OnSeriesTilePointerPressed` (`:76`), `OnAlphabetIndexLetterClick` (`:101`).
- **`LibraryScreenViewModel.cs`** (1633 lines):
  - single-active-dropdown mechanism: `[ObservableProperty] string? _activeDropdown` (`:846`) +
    computed `IsFilterOpen`/`IsSortOpen`/`IsGroupOpen`/`IsDisplayOpen`/`IsAddOpen`/`IsSearchModeOpen`/`IsAddToListOpen`
    (`:848–858`), `OnActiveDropdownChanged` raises all seven (`:860`), `Toggle*` commands
    (`:871–897`) set `ActiveDropdown` to their string or `null`. `ToggleAdd` (`:886`) also resets
    the 4 `NewIssue*` fields.
  - filters: `[ObservableProperty]` `_filterUnreadOnly` / `_filterMissingIssues` / `_filterTrackedOnly`
    (`:810–835`), each hook = `SaveLibrarySettings(); LoadFromDatabase();`. Sidebar
    `_activeContentType` / `_activeCategoryId` (`:58`) are private, set by `SelectAllSeries` /
    `SelectContentType` / `SelectCollection`.
  - search: `_searchQuery` (`:762`), `_searchMode` (`:795`), `SearchModeLabel` (`:798`),
    `SetSearchModeCommand` (`:807`).
  - series-granularity sort/group: `_sortField` (`LibrarySortField`, `:385`), `_sortDirection`
    (`:397`), `_groupField` (`LibraryGroupField`, `:416`), `ToggleSortDirectionCommand` (`:408`),
    `SortLabel` (`:412`, name + ` ↑`/` ↓`), `GroupLabel` (`:421`), `IsGrouped` (`:419`),
    `SetSortFieldCommand` / `SetGroupFieldCommand` (`:435`).
  - granularity: `_granularity` (`:441`), `IsIssueGranularity` / `IsSeriesGranularity` (`:444`),
    `ActiveSortLabel` (`:469` = series `SortLabel` or `IssueList.SortLabelWithDirection`),
    `ActiveGroupLabel` (`:471`).
  - `IssueList` (`IssueListScreenViewModel`, `:230`): `SortField`/`SortDirection`/`GroupField`
    `[ObservableProperty]`, `SetSortFieldCommand` / `SetGroupFieldCommand` / `ToggleSortDirectionCommand`,
    `SortFieldOptions` / `GroupFieldOptions` (static, `IssueListFieldCatalog.*.Values.ToList()`),
    `SortLabelWithDirection` (`:72`), `GroupFieldLabel` (`:67`), `Rows` / `Groups` / `IsGrouped` /
    `HasAnyResults`, `SetRows(IEnumerable<Issue>)`.
  - Add form: `_newIssueSeriesName` / `_newIssueNumber` / `_newIssueContentType` /
    `_newIssueReadingMode` (`:330–346`), `ShowAddReadingModePicker` (`:348`),
    `SetNewIssueContentTypeCommand` / `SetNewIssueReadingModeCommand`, `CreatePlaceholderIssueCommand`
    (`:1436` — resets the 4 fields + `ActiveDropdown = null` at the end).
  - selection: `Selection` / `SeriesSelection` `TileSelectionController<>`, `SelectionCount` /
    `SeriesSelectionCount`, `HasSelection` / `HasSeriesSelection` / `HasAnySelection` (`:1248`),
    `DeleteConfirmLabel` / `DeleteSeriesConfirmLabel`, action commands
    `BulkEditSelectionCommand` / `MarkSelectionReadCommand` / `MarkSelectionUnreadCommand` /
    `RunLibraryPluginsOnSelectionCommand` / `ToggleAddToListCommand` / `DeleteSelectionCommand` /
    `ClearSelectionCommand`, and `BulkEditSeriesSelectionCommand` / `DeleteSeriesSelectionCommand` /
    `ClearSeriesSelectionCommand`.
  - display toggles: `_viewMode` (`:1479`), `Is{PosterGrid,PanoramaGrid,ListView,DetailsView,TilesView,IssueListView}`,
    `SetViewModeCommand`, `DisplayModeLabel` (`:1509`), `GridDensity` (`:1531`), `_showTileTitles`
    (`:1577`), `_showUnreadBadge` / `_showPublisherBadge` / `_showLanguageBadge` / `_useLanguageIcon` /
    `_showContinueReadingButton`.
  - settings: `LoadLibrarySettings` (`:241`, direct field writes), `SaveLibrarySettings` (`:292`,
    every field). `HasAnyResults` (`:465`), `ShowAlphabetIndex` (`:841`).
- **`AppSettings.cs`**: `Library*` block `:161–256`. `LibrarySearchQuery` is a nullable `string?`
  with no explicit EF config (`:231`). No `LibraryDetailsColumns`.
- **`IssueListFieldCatalog.cs`** (156 lines): `IssueListSortFieldDescriptor(Field, DisplayName, Compare)`
  positional record (`:8`); `SortFields` dict has ~60 entries (`:21–95`), `GroupFields` ~45
  (`:97–144`). `IssueListRow` (`Models/IssueListRow.cs`, 114 lines) — every column-worthy field is
  `init`-only, mostly `string?` / `int?` / `float?` / `long?` / `DateTime?` / `double`; `bool IsRead`,
  `bool IsMissing`.
- **Overlay-hosting precedent:** `MainWindow.axaml:656` / `:675` render
  `<Border IsVisible="{Binding IsXOverlayOpen}" Background="#B0000000"><Border ...><views:XOverlay/>…`
  driven by `MainViewModel._isXOverlayOpen` + Open/Close commands. **The design (§6) deliberately
  keeps the Add overlay inside `LibraryScreen.axaml`** with state on `LibraryScreenViewModel` — no
  `MainViewModel` extraction — so we reproduce the `#B0000000` backdrop + `Border Classes="floatingPanel"`
  shape locally as the last child of `LibraryScreen.axaml`'s root `Grid`.
- **`Styles/Primitives.axaml`**: `Border.pbChip` / `Button.pbChip` (`:73`/`:80`, `CornerRadius 999`,
  `PbSurface2`), `Border.floatingPanel` (`:160`, `PbSurface3`, `PbRadiusLg`, `PbElevationShadow`).
  **`Styles/Typography.axaml`**: `pbTextHeading` (Bebas, 22), `pbTextBody` (13), `pbTextCaption` (11).
  **`App.axaml`**: `PbSurface1/2/3Brush`, `PbRadius` (7) / `PbRadiusSm` (5) / `PbRadiusLg` (14),
  `PbTextMutedBrush`, `PbAccentSoftBrush`, `PbAccentTextBrush`, `PbBadgeTextBrush`, `PbMotionFast`
  (0.15s), `PbMotionEase`. **`Styles/Icons.axaml`**: `Path.pbIcon` class (`:108`); has
  `PbIconSearch`, `PbIconFilter`, `PbIconLayers`, `PbIconChevronLeft/Right`, `PbIconPlus`,
  `PbIconArrowDown`; **no** `PbIconViewSort` / `PbIconSortAsc` / `PbIconGrid`.
- **UI tests that drive the old toolbar ids** (all in `src/Paperbunkr.App.UiTests/`):
  `LibraryListLayoutPersistenceTests`, `LibraryComicListModeTests`, `LibraryGranularityTests`,
  `ComicListSlice1FieldsTests`, `ComicListStoryArcFieldsTests` — they `Invoke()`
  `LibraryDisplayButton` / `LibrarySortButton` / `LibraryGroupButton` / `LibraryFilterButton` then a
  `LibraryViewModeOption_*` / `Comic*Option_*` child. `Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs`
  touches `CreatePlaceholderIssueCommand`, selection, `OverlayBadgeToggles` — **not** the popup
  toggle commands.
- **EF migration pattern:** `dotnet ef migrations add <Name> --project src/Paperbunkr.Data`
  (design-time factory `PaperbunkrDbContextFactory` exists). Latest migration
  `20260827045244_LibraryPosterGridConsolidation`. Migration test pattern =
  `src/Paperbunkr.Data.Tests/AddIssueTagsMigrationTests.cs` (own temp `.db`,
  `context.GetService<IMigrator>().Migrate(prior)` → seed SQL → `.Migrate()` → assert;
  `SqliteConnection.ClearAllPools()` in `Dispose`). **Memory flag:** the EF scaffolder has produced
  a wrong `RenameColumn` before — review the generated `Up`/`Down` by hand.

---

## Step 1: Extract the toolbar into `LibraryToolbar.axaml` (pure move, no behaviour change)

**Files:** `src/Paperbunkr.App/Views/LibraryToolbar.axaml` (new),
`src/Paperbunkr.App/Views/LibraryToolbar.axaml.cs` (new),
`src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit)

**What:**
- New `LibraryToolbar` `UserControl`, `x:DataType="vm:LibraryScreenViewModel"`, same `xmlns`
  set as `LibraryScreen.axaml` (`vm`, `views`, `models`, `entities`, `conv`, `controls`). Add the
  code-behind `.cs` in the **same commit** (`partial class LibraryToolbar : UserControl { public
  LibraryToolbar() => InitializeComponent(); }`) per the `CLAUDE.md` AVLN2000 rule.
- Move verbatim into it: the whole `<Border Grid.Row="0" Padding="24,14" …>` toolbar block
  (`LibraryScreen.axaml:1092–1428`) — browse row, all 6 popups, both selection action `StackPanel`s,
  the `AddToListButton` popup.
- Move the toolbar-only styles from `LibraryScreen.axaml`'s `UserControl.Styles` into
  `LibraryToolbar.axaml`'s: `Button.toolbarPill` (+ `.open`, `+ Border.icon`), `Border.dropdown`,
  `TextBlock.dropdownRow`, `Button.modeOption` (+ `.active`). Leave `Button.card`, `Border.cover`,
  `Border.posterCover`, `TextBlock.groupHeader`, `Button.continueReading`, `CheckBox.tileSelect`
  in `LibraryScreen.axaml` (content area still uses them).
- `LibraryScreen.axaml`: replace the moved `<Border Grid.Row="0">` with
  `<views:LibraryToolbar Grid.Row="0" />` (DataContext inherits). The `Grid RowDefinitions="Auto,*"`
  root, A-Z indexer, all content `ScrollViewer`s, and empty state stay put.
- No VM changes. No test changes (every `AutomationId` keeps its current name and value).

**Depends on:** none
**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` with the AVLN2000 guard
(`rm obj/Debug/net10.0/Paperbunkr.App.dll obj/Debug/net10.0/Paperbunkr.App.pdb` then rebuild, or
`-t:Rebuild`); **launch the exe** to confirm the XAML weave actually ran (0 Errors alone is not
proof for a new `.axaml`). `dotnet test src/Paperbunkr.App.Tests` + `src/Paperbunkr.App.UiTests`
still green (behaviour unchanged).

## Step 2: New toolbar icons

**Files:** `src/Paperbunkr.App/Styles/Icons.axaml` (edit),
`docs/superpowers/specs/icon-mapping.md` (edit)

**What:**
- Add `StreamGeometry` keys in the existing hand-computed thin-outline style (24×24 viewbox,
  ~1.5px visual stroke, matching the neighbours):
  - `PbIconViewSort` — a "sliders" glyph (two horizontal rails each with a knob), the "View & Sort"
    button.
  - `PbIconSortAsc` — short-bar → long-bar ascending stack with a down-arrow, the Sort-tab
    direction affordance / `Sorted:` chip.
  - `PbIconGrid` — 2×2 rounded squares, the View tab (optional, only if the tab strip shows icons).
- `icon-mapping.md`: add a "Phase 4b" table row per new icon (action → key), same format as the
  existing phase tables.

**Depends on:** none
**Verify:** `dotnet build src/Paperbunkr.App` green (additive resource dictionary entries); eyeball
each new `Path` at 16px in the showcase or a scratch view.

## Step 3: `IssueListFieldCatalog` — per-field `Display` projection + column set

**Files:** `src/Paperbunkr.App/Models/IssueListFieldCatalog.cs` (edit),
`src/Paperbunkr.App/Models/IssueListRow.cs` (no change — read only),
`src/Paperbunkr.App.Tests/IssueListFieldCatalogTests.cs` (new)

**What:**
- On `IssueListSortFieldDescriptor`, add an **optional init property** (not a positional param, so
  the ~60 existing `new(field, name, compare)` sites are untouched):
  `public Func<IssueListRow, string?>? Display { get; init; }`.
- Populate `Display` for the fields that make sense as a text column (design §8 — `Status` stays
  sort-only, so no `Display`). Use plain, null-safe projections, e.g.:
  - `Title` → `r => r.Title`, `Series` → `r => r.SeriesName`, `Number` → `r => r.Number`,
    `Volume` → `r => r.Volume`, `Publisher`/`Imprint`/`Writer`/`Penciller`/… → `r => r.<Field>`
  - `Year` → `r => r.Year?.ToString()`, `PageCount` → `r => r.PageCount?.ToString()`,
    `Month`/`Day`/`Count`/`OpenCount`/`BookmarkCount` similar
  - `FileSize` → `r => FormatFileSize(r.FileSize)` (small private helper: B/KB/MB/GB, `null → null`)
  - `Rating`/`CommunityRating`/`BookPrice` → `r => r.<Field>?.ToString("0.#")`
  - `ReadPercentage` → `r => $"{r.ReadPercentage:0}%"`
  - `Added`/`Opened`/`Released`/`FileModified`/`FileCreated` → `r => r.<Time>?.ToString("yyyy-MM-dd")`
  - `Read` → `r => r.IsRead ? "Read" : "Unread"`
  - `FilePath`/`FileName`/`FileDirectory`/`FileFormat`/`Format`/`Genre`/`Tags`/`Characters`/`Teams`/
    `Locations`/`StoryArc`/`SeriesGroup`/`AgeRating`/`Language`/`ISBN`/`ScanInformation`/`AlternateSeries`/
    `AlternateNumber` → direct string field.
- Add `public static readonly IssueListSortField[] DefaultDetailsColumns` — a curated ordered set,
  e.g. `Title, Series, Number, Volume, Year, PageCount, Publisher, Format, Rating, Added, ReadPercentage`.
- Add `public static IReadOnlyList<IssueListSortFieldDescriptor> ColumnFields` — every `SortFields`
  value with a non-null `Display`, in `SortFields` insertion order (stable, matches CE's column
  ordering intent).
- New `IssueListFieldCatalogTests`: (a) every `ColumnFields` descriptor has non-null `Display`;
  (b) calling `Display` on a **fully-populated** `IssueListRow` and on a **fully-null** one (only
  `required` members set) returns without throwing for all of `ColumnFields`; (c) `DefaultDetailsColumns`
  entries all resolve in `SortFields` and all have a `Display`.

**Depends on:** none
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter IssueListFieldCatalogTests` green; full
`Paperbunkr.App.Tests` still green.

## Step 4: Details-column persistence (`AppSettings` + migration + VM model, no UI yet)

**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit),
`src/Paperbunkr.Data/Migrations/<ts>_LibraryDetailsColumns.cs` (+ `.Designer.cs`, snapshot) (new),
`src/Paperbunkr.App/Models/DetailsColumn.cs` (new),
`src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit),
`src/Paperbunkr.Data.Tests/LibraryDetailsColumnsMigrationTests.cs` (new)

**What:**
- `AppSettings`: `public string? LibraryDetailsColumns { get; set; }` next to the other `Library*`
  fields, `<summary>` = comma-joined `IssueListSortField` names; `null` → curated default.
- `dotnet ef migrations add LibraryDetailsColumns --project src/Paperbunkr.Data`. Expect a bare
  `AddColumn<string>("LibraryDetailsColumns", "AppSettings", nullable: true)` / `DropColumn` — **hand-review**
  the generated `Up`/`Down` and the snapshot diff (memory: scaffolder has emitted a bad `RenameColumn`
  before). No data backfill needed (`null` is the valid "use default" sentinel).
- New `DetailsColumn` (`Models/DetailsColumn.cs`): `ObservableObject` with `IssueListSortField Field`
  (init), `string DisplayName` (init), `[ObservableProperty] bool _isVisible`.
- `LibraryScreenViewModel`:
  - `public ObservableCollection<DetailsColumn> DetailsColumns { get; }` built once in the ctor from
    the parsed setting. Parse: split the stored string on `,`, `Enum.TryParse` each (skip unknown /
    non-`ColumnFields` names), fall back to `IssueListFieldCatalog.DefaultDetailsColumns` when the
    setting is null/empty/all-invalid. Each entry marked visible; then append the remaining
    `ColumnFields` **not** in the stored list as `IsVisible = false`, so the right-click menu can
    offer them. (Serialization writes only the `IsVisible == true` ones, in collection order.)
  - `LoadLibrarySettings` (`:241` area): read `settings.LibraryDetailsColumns` into a field the ctor
    uses to build `DetailsColumns` (direct-field pattern like its neighbours).
  - `SaveLibrarySettings` (`:292` area): `settings.LibraryDetailsColumns =
    string.Join(",", DetailsColumns.Where(c => c.IsVisible).Select(c => c.Field));` (or `null` when
    that yields the exact default set / is empty — keep it simple: store the CSV always, treat only
    literal `null` as "never set").
  - Subscribe to each `DetailsColumn.PropertyChanged` (and `DetailsColumns.CollectionChanged` if the
    menu reorders — v1 doesn't reorder) → `SaveLibrarySettings()` + raise a
    `DetailsColumnsVersion`-style notify the code-behind can react to (see Step 6). Simplest: a
    `public event EventHandler? DetailsColumnsChanged;` the view subscribes to.
  - `[RelayCommand] private void SetDetailsSort(IssueListSortField field)` — if
    `IssueList.SortField == field` then `IssueList.ToggleSortDirectionCommand.Execute(null)`, else
    `IssueList.SetSortFieldCommand.Execute(field)`. (Reuses the existing sort pipeline; the
    `Sorted:` chip added in Step 5 reflects it automatically.)
- Tests:
  - `LibraryScreenViewModelTests`: `LibraryDetailsColumns_DefaultWhenNull_ParsedOtherwise` (fresh VM
    → visible set == `DefaultDetailsColumns`); `LibraryDetailsColumns_UnknownNamesSkipped`
    (seed `"Title,Bogus,Series"` → visible == `[Title, Series]`);
    `TogglingDetailsColumnVisibility_Persists` (flip one `IsVisible`, new VM instance reads it back);
    `SetDetailsSort_SetsThenFlipsIssueListSort` (`SetDetailsSortCommand(Year)` → `IssueList.SortField
    == Year`; again → `IssueList.SortDirection` flipped).
  - `LibraryDetailsColumnsMigrationTests` (mirror `AddIssueTagsMigrationTests`): prior =
    `20260827045244_LibraryPosterGridConsolidation`; migrate to prior, `INSERT INTO AppSettings`
    (Id 1, all NOT NULL cols), `.Migrate()`, assert the row survives and `LibraryDetailsColumns IS
    NULL`; `Down` then drops the column.

**Depends on:** Step 3
**Verify:** `dotnet ef database update --project src/Paperbunkr.Data` clean against a scratch DB;
`dotnet test src/Paperbunkr.App.Tests src/Paperbunkr.Data.Tests` green.

## Step 5: Toolbar rework — VM members + `LibraryToolbar.axaml` rewrite + Add overlay + empty state

This is the largest step; VM renames and the toolbar XAML must land together for a green build.

**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/LibraryToolbar.axaml` (rewrite),
`src/Paperbunkr.App/Views/LibraryToolbar.axaml.cs` (edit — `Escape`/backdrop handlers if any),
`src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — Add overlay + empty state),
`src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit — `Escape` to close Add overlay),
the 5 UI test files listed in the survey (edit).

**VM — `#region 4b toolbar` grouping the new members:**
- Replace `IsFilterOpen`/`IsSortOpen`/`IsGroupOpen`/`IsDisplayOpen` + `ToggleFilter`/`ToggleSort`/
  `ToggleGroup`/`ToggleDisplay` with:
  - `public bool IsViewSortOpen => ActiveDropdown == "viewSort";`
  - `public enum ViewSortTab { View, Sort, Group }` + `[ObservableProperty] ViewSortTab _viewSortActiveTab;`
    (its `OnChanged` raises the three `Is*TabActive` computed bools used for the tab-strip `.active`).
  - `[RelayCommand] void ToggleViewSort() => ActiveDropdown = ActiveDropdown == "viewSort" ? null : "viewSort";`
  - `[RelayCommand] void OpenViewSortTab(ViewSortTab tab) { ViewSortActiveTab = tab; ActiveDropdown = "viewSort"; }`
  - keep `IsSearchModeOpen` + `ToggleSearchModeCommand` and `IsAddToListOpen` + `ToggleAddToListCommand`.
  - update `OnActiveDropdownChanged` to raise `IsViewSortOpen` instead of the 4 removed.
- Add `[ObservableProperty] string? _addFilterDropdown`-less: reuse `ActiveDropdown == "addFilter"`
  → `public bool IsAddFilterOpen`, `[RelayCommand] ToggleAddFilter`.
- Rename Add-form popup state: `IsAddOpen`/`ToggleAdd` → `IsAddIssueOpen` (`ActiveDropdown`-independent
  `[ObservableProperty] bool _isAddIssueOpen`, since it's now a full overlay not a light-dismiss
  popup) + `[RelayCommand] OpenAddIssue` (sets `true`, resets the 4 `NewIssue*` fields) +
  `[RelayCommand] CloseAddIssue` (sets `false`). `CreatePlaceholderIssue` end: `IsAddIssueOpen =
  false` instead of `ActiveDropdown = null`.
- Chips:
  - `public bool HasActiveFilters => FilterUnreadOnly || FilterMissingIssues || FilterTrackedOnly
    || SearchMode != SearchMode.All;`
  - `public bool IsSortNonDefault` / `IsGroupNonDefault` (granularity-aware: series vs `IssueList`,
    compared to the `AppSettings` defaults `DateAdded`/`Added` + `Descending`, and `None`).
  - `public bool HasVisibleChips => HasActiveFilters || IsSortNonDefault || IsGroupNonDefault;`
  - `public string SortChipLabel => ActiveSortLabel;` (already granularity-aware) —
    reuse; `public bool ShowGroupChip => IsGroupNonDefault;`
    `public string GroupChipLabel => ActiveGroupLabel;`
  - `public string SearchScopeChipLabel => SearchModeLabel;`
    `public bool ShowSearchScopeChip => SearchMode != SearchMode.All;`
  - `[RelayCommand] void ClearAllFilters() { FilterUnreadOnly = FilterMissingIssues =
    FilterTrackedOnly = false; SearchMode = SearchMode.All; }` (each setter already saves + reloads).
  - raise `HasVisibleChips` + the chip labels from the existing filter / sort / group / searchmode /
    granularity `On*Changed` hooks (extend them).
- Empty state:
  - `public string EmptyStateMessage` — `!string.IsNullOrWhiteSpace(SearchQuery)` → `$"No results
    for “{SearchQuery.Trim()}”."`; else `HasActiveFilters` → `"No comics match this
    filter."`; else `"This library is empty."`
  - `public string EmptyStateActionLabel` — filters/search active → `"Clear filters"`, else
    `"Scan folders"`.
  - `public IRelayCommand EmptyStateActionCommand` — `HasActiveFilters || SearchQuery != ""` →
    `ClearAllFiltersCommand`; else a new `[RelayCommand] OpenLibraryPreferences` that calls a new
    ctor `Action` (add `Action? goLibraryPreferences = null` to the ctor, wire in `MainViewModel`
    to navigate to Preferences → Libraries; default no-op keeps tests simple).
  - `public bool ShowEmptyState => !HasAnyResults;` (rename target for the current `!HasAnyResults`
    binding; raise it wherever `HasAnyResults` is raised).
- UI automation ids: keep `LibraryViewModeOption_*` and `LibraryGranularityOption_*`. Rename the
  button: new `LibraryViewSortButton`; new `LibraryViewSortTab_View` / `_Sort` / `_Group`; keep
  `LibrarySearchModeButton` + its option ids; new `LibraryAddIssueButton` (was `LibraryAddButton`);
  new `LibraryClearFiltersChip` / `LibrarySortChip` / `LibraryGroupChip` / `LibraryAddFilterChip`.

**`LibraryToolbar.axaml` — rewrite (design §2–§5):**
- **Row 1** `<Border BorderThickness="0,0,0,1" BorderBrush="{DynamicResource PbBorderBrush}"
  Background="{DynamicResource PbSurface1Brush}" Padding="24,14">` → `Grid ColumnDefinitions="Auto,*,Auto,Auto"`:
  - col 0: browse `‹`/`›` as borderless `Button`s with `Path Classes="pbIcon"` `PbIconChevronLeft`/
    `Right`, `IsEnabled="{Binding CanBrowsePrevious/Next}"`, ids unchanged
    (`LibraryBrowsePreviousButton` / `NextButton`).
  - col 1: search `Border` (`PbSurface3Brush`, `CornerRadius="{DynamicResource PbRadius}"`, 1px
    `PbBorderBrush`, `HorizontalAlignment="Stretch"`, `Margin="12,0"`) → `DockPanel`: leading
    `Path Classes="pbIcon" Data="{StaticResource PbIconSearch}"`, trailing scope `Button`
    (`x:Name="SearchModeButton"`, borderless, `Content="{Binding SearchModeLabel, StringFormat='{}{0} ▾'}"`,
    `Command="{Binding ToggleSearchModeCommand}"`, id `LibrarySearchModeButton`), center `TextBox`
    (`Text="{Binding SearchQuery}"`, `PlaceholderText="Search library…"`, **not** `Watermark`, id
    `LibrarySearchBox`, transparent bg/no border).
  - col 2: `Button x:Name="ViewSortButton" Classes="toolbarPill" Classes.open="{Binding IsViewSortOpen}"
    Command="{Binding ToggleViewSortCommand}"` id `LibraryViewSortButton` → `Path Classes="pbIcon"
    PbIconViewSort` + `TextBlock Text="View &amp; Sort"` (static; chips carry live state).
  - col 3: `Button Classes="primary"` (Primitives) `Command="{Binding OpenAddIssueCommand}"` id
    `LibraryAddIssueButton` → `Path Classes="pbIcon" PbIconPlus`.
- **`Button.toolbarPill` restyle** (in this file's styles): `PbSurface3Brush` bg, 1px `PbBorderBrush`,
  `CornerRadius="{DynamicResource PbRadius}"` (drop 999), `PbTextMutedBrush` fg; `.open` →
  `PbAccentBrush` border + `PbAccentSoftBrush` bg + `PbAccentTextBrush` fg. `Border.dropdown` →
  `PbSurface3Brush` bg, `BoxShadow="{StaticResource PbElevationShadow}"`,
  `CornerRadius="{DynamicResource PbRadiusLg}"`, `PbBorderBrush`. `Button.modeOption` unchanged.
- **Row 2 chips** `<WrapPanel IsVisible="{Binding HasVisibleChips}" Margin="24,0,24,12">`:
  - one `Border Classes="pbChip"` per active filter (`Unread only` / `Missing` / `Tracked` /
    `Search: {SearchScopeChipLabel}`) each with a trailing `✕` `Button` bound to the matching
    clear command (`FilterUnreadOnly`/`FilterMissingIssues`/`FilterTrackedOnly` setters to `false`
    via tiny relay commands, or reuse `ClearAllFiltersCommand` for the search-scope one).
    Sidebar `ActiveContentType`/`ActiveCategory` are **not** chipped (design §5 note — sidebar-only).
  - `Button Classes="pbChip" Content="+ Add filter"` id `LibraryAddFilterChip`
    `Command="{Binding ToggleAddFilterCommand}"` with a small `Popup` (`x:Name` target) holding the
    3 filter `CheckBox`es (the old Filter popup content).
  - `Button Classes="pbChip"` `Content="{Binding SortChipLabel, StringFormat='Sorted: {0}'}"` id
    `LibrarySortChip` `Command="{Binding OpenViewSortTabCommand}"
    CommandParameter="{x:Static vm:LibraryScreenViewModel+ViewSortTab.Sort}"`.
  - `Button Classes="pbChip"` `IsVisible="{Binding ShowGroupChip}"`
    `Content="{Binding GroupChipLabel, StringFormat='Grouped: {0}'}"` id `LibraryGroupChip`
    → `OpenViewSortTabCommand` / `Group`.
- **View & Sort tabbed `Popup`** (`PlacementTarget="{Binding #ViewSortButton}"`,
  `IsOpen="{Binding IsViewSortOpen}"`, `IsLightDismissEnabled="True"`) → `Border Classes="dropdown"
  Width="248"` → `DockPanel`: top a 3-button tab strip (`View`/`Sort`/`Group`, `Classes="modeOption"`,
  `.active` bound to `ViewSortActiveTab` equality, ids `LibraryViewSortTab_View/_Sort/_Group`,
  `Command="{Binding OpenViewSortTabCommand}"`), then a `Panel` with three mutually-`IsVisible`
  bodies:
  - **View** (`IsVisible` tab == View): move the current Display popup's content **verbatim** —
    the 6 `LibraryViewModeOption_*` rows, the `LibraryGranularityOption_Issue/Series` rows, `Show
    titles` `CheckBox` (`IsVisible="{Binding IsPosterGrid}"`), `Grid density` `Slider`. Keep the
    overlay-badge `CheckBox`es here too (they were in the Display popup).
  - **Sort** (tab == Sort): move the current Sort popup's two granularity-gated `StackPanel`s
    verbatim (issue `ItemsControl` over `IssueList.SortFieldOptions`; series 7 fixed rows). Replace
    the `Content="Direction: {0}"` row with two `Button Classes="modeOption"` — `↑ Ascending` /
    `↓ Descending` — `.active` on the current direction, `Command` = the granularity's
    `ToggleSortDirectionCommand` (or set-direction; a toggle is fine since there are two).
  - **Group** (tab == Group): move the current Group popup's two granularity-gated `StackPanel`s
    verbatim (`None` first).
- **Search-mode `Popup`** — unchanged content (7 `modeOption` rows), just restyled via the shared
  `dropdown` class.
- **Selection second row** `<Border IsVisible="{Binding HasAnySelection}" Background="{DynamicResource
  PbAccentSoftBrush}" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource PbAccentBrush}"
  Padding="24,10">` rendered **below** Row 1/Row 2 (so Row 1 no longer hides — drop
  `IsVisible="{Binding !HasAnySelection}"` from the browse row). Give it a height/opacity
  `DoubleTransition` on `PbMotionFast`. Content = `{Count} selected` + the existing issue-selection
  buttons (restyled `Button Classes="pbChip"`) when `HasSelection`, or the series-selection set when
  `HasSeriesSelection`. Keep every command binding and the `AddToListButton` popup.
- Delete the old `FilterButton`/`SortButton`/`GroupButton`/`DisplayButton`/`AddButton` + their 5
  popups.

**`LibraryScreen.axaml` — Add-issue overlay + empty state:**
- As the **last children** of the root `Grid`:
  ```
  <Border IsVisible="{Binding IsAddIssueOpen}" Background="#B0000000" ... />   <!-- backdrop, PointerPressed → CloseAddIssueCommand -->
  <Border IsVisible="{Binding IsAddIssueOpen}" Classes="floatingPanel" Width="380"
          HorizontalAlignment="Center" VerticalAlignment="Center" Padding="24">
      <!-- header "Add issue to library"; AutoCompleteBox series name (ItemsSource ExistingSeriesNames,
           PlaceholderText); TextBox number (PlaceholderText); content-type modeOption list;
           reading-mode modeOption list (IsVisible ShowAddReadingModePicker);
           Add (Classes=primary, CreatePlaceholderIssueCommand) + Cancel (CloseAddIssueCommand) -->
  </Border>
  ```
  Reuse the field bindings from the old Add popup verbatim; swap `Watermark` → `PlaceholderText`
  everywhere it appears here.
- `LibraryScreen.axaml.cs`: handle `KeyDown` (`Escape`) on the root → `CloseAddIssueCommand` when
  `IsAddIssueOpen` (mirrors the editor overlays).
- Empty state (`:1711`): replace with a centered `StackPanel IsVisible="{Binding ShowEmptyState}"`
  → `Path Classes="pbIcon"` (`PbIconSearch` when `HasActiveFilters || SearchQuery != ""` else
  `PbIconLayers`), `TextBlock Classes="pbTextBody" Text="{Binding EmptyStateMessage}"`,
  `Button Classes="secondary" Content="{Binding EmptyStateActionLabel}" Command="{Binding
  EmptyStateActionCommand}"`.

**UI tests:** in each of `LibraryListLayoutPersistenceTests`, `LibraryComicListModeTests`,
`LibraryGranularityTests`, `ComicListSlice1FieldsTests`, `ComicListStoryArcFieldsTests` — replace
the `LibraryDisplayButton` / `LibrarySortButton` / `LibraryGroupButton` / `LibraryFilterButton`
`Invoke()` calls with: `Invoke LibraryViewSortButton` → `Invoke LibraryViewSortTab_View`/`_Sort`/`_Group`
→ then the same `LibraryViewModeOption_*` / `Comic*Option_*` / filter-checkbox child. The filter
checkboxes now live behind the `+ Add filter` chip — open `LibraryAddFilterChip` first for those.

**Depends on:** Steps 1, 2, 4
**Verify:** `dotnet build src/Paperbunkr.App` (AVLN2000 guard + launch). `dotnet test
src/Paperbunkr.App.Tests` (VM rename tests) + `src/Paperbunkr.App.UiTests` (updated flows) green.
Manual: two-zone bar both skins, tabbed popup, chips appear/clear, search scope menu, selection
second row slides in, Add overlay opens/`Escape`/backdrop-closes and still creates the placeholder.

## Step 6: Configurable Details table (hand-rolled)

**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — Details section only),
`src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit),
`src/Paperbunkr.App/Models/DetailsCellConverter.cs` (new, if the converter route is taken),
`src/Paperbunkr.App.UiTests/LibraryDetailsColumnsTests.cs` (new)

**What (Issue granularity only — Series keeps its fixed `SeriesDetailsItemTemplate`, token-restyled):**
- **Spike the dynamic path first** (design "biggest fiddly piece" / fallback note): code-behind
  builds `ColumnDefinitions` from `DetailsColumns.Where(IsVisible)` — `Auto` for the leading thumb
  column, `*` (or `Pixel`-min + `*`) per text column — and rebuilds on the VM's
  `DetailsColumnsChanged` event and on `DetailsColumns` item `IsVisible` changes.
  - **Header:** a `Grid x:Name="DetailsHeaderGrid"` (code-behind-populated). Each visible column →
    `Button Classes="detailsHeader"` (new style: borderless, `PbTextFaintBrush`, bold 11,
    `HorizontalContentAlignment=Left`, hand cursor) → `DockPanel` { `TextBlock` DisplayName +
    `Path Classes="pbIcon"` `PbIconArrowDown`/`PbIconSortAsc` shown only when
    `IssueList.SortField == column.Field`, rotated for direction }. `Command="{Binding
    SetDetailsSortCommand}" CommandParameter="{x:Static entities:IssueListSortField.<Field>}"`.
  - **Header `ContextMenu`** (right-click any header): an `ItemsControl`/generated `MenuItem` per
    `IssueListFieldCatalog.ColumnFields`, each a checkable `MenuItem` `IsChecked`/`Command` bound to
    the matching `DetailsColumn.IsVisible` (toggle). Built in code-behind from `ColumnFields` once.
  - **Rows:** keep the existing `ItemsControl` over `IssueList.Rows` / `IssueList.Groups` but swap
    `DetailsItemTemplate` for a code-behind-built row: reuse the `Button Classes="card"` root (keeps
    click-to-open + the shared issue `ContextMenu` + `PointerPressed="OnTilePointerPressed"` +
    selection checkbox in the thumb cell), inner `Grid` sharing `DetailsHeaderGrid`'s
    `ColumnDefinitions`, one `TextBlock` per visible column bound via a `DetailsCellConverter`
    (`ConverterParameter={x:Static entities:IssueListSortField.<Field>}` → `descriptor.Display(row)`),
    or built entirely in code-behind. Rebuild the row template/host when columns change.
  - **Fallback if the dynamic `ColumnDefinitions` fights Avalonia virtualization/measure:** a fixed
    superset of the ~14 `DefaultDetailsColumns` + a few, each cell `IsVisible`-bound to its
    `DetailsColumn.IsVisible`, widths fixed — no code-behind column generation. Uglier, simpler.
    Decide during the spike; note which was taken in the design doc status.
- Delete the two fixed Details header `Grid`s at `LibraryScreen.axaml:1578/1585` (issue one is
  replaced; series one moves into `SeriesDetailsItemTemplate`'s own restyle in Step 7 or stays as a
  fixed header for series granularity — keep the series header `Grid`, just token-restyle it).
- Right-click "Columns" menu + header sort-toggles render **only** when `IsIssueGranularity`.
- `LibraryDetailsColumnsTests` (UiTest): open `LibraryViewSortButton` → `LibraryViewSortTab_View` →
  `LibraryViewModeOption_Details`; assert a `detailsHeader` element is present; `Invoke()` one
  header, assert `IssueList` sort changed (e.g. via a visible order change or a sort chip label);
  `Invoke()` again, assert direction flipped. (Right-click menu + relaunch persistence stay in the
  manual checklist — FlaUI right-click + app restart is flaky in this env per prior notes.)

**Depends on:** Steps 4, 5
**Verify:** `dotnet build` (AVLN2000 guard + launch). `dotnet test src/Paperbunkr.App.UiTests
--filter LibraryDetailsColumns` green. Manual: add/remove columns via right-click header, relaunch →
set persists; click-header sort + direction flip; both skins.

## Step 7: Row-mode restyle + Bebas group headers + empty-state polish

**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit)

**What:**
- `ListItemTemplate` / `TilesItemTemplate` / `SeriesListItemTemplate` / `SeriesTilesItemTemplate` /
  `SeriesDetailsItemTemplate`: swap hardcoded hex / stale tokens for `PbSurface*` / `PbText*` scale;
  `PbRadiusSm` on the thumb `Border`s; add a `:pointerover` → `PbSurface2Brush` row background on the
  `Button.card` root (new style `Button.card:pointerover Border.rowShell` or similar). Unread
  indicator → the small `PbAccentTextBrush` dot the poster tiles use (replace the `Consolas ●` badge
  Border with the 4a dot treatment). List/Tiles **structure** otherwise unchanged.
- Group headers: replace every `<TextBlock Text="{Binding Header}" Classes="groupHeader" />` in the
  Panorama / List / Details / Tiles grouped `ItemsControl`s (`LibraryScreen.axaml:1506, 1528, 1552,
  1566, 1601, 1615, 1640, 1662`) with the 4a `DockPanel` header: `TextBlock Classes="pbTextHeading"`
  + a count `TextBlock Classes="pbTextCaption"` (`{Binding Items.Count}`) + a 1px `PbBorderBrush`
  rule. Then **delete** the `TextBlock.groupHeader` style (`:112`).
- Empty-state control from Step 5 — final visual polish pass (icon size, spacing, `pbTextBody`
  colour) once it can be seen against real data.

**Depends on:** Steps 5, 6
**Verify:** `dotnet build` + launch. Manual: List/Tiles/Details rows in both skins, hover row
highlight, Bebas group headers with count + rule in a grouped list, each empty-state variant
(zero-result search, filter-only, empty library).

## Step 8: Full build, full test, manual checklist, doc status

**Files:** `docs/superpowers/specs/2026-08-27-library-browsing-4b-toolbar-rework-design.md` (status),
`docs/alpha-todo.md` (if 4b maps to a roadmap line — check `git log` vs the doc's HEAD marker first).

**What:** solution build with the AVLN2000 guard (`net10.0` obj paths), full `dotnet test` across
`Paperbunkr.App.Tests` / `Paperbunkr.Data.Tests` / `Paperbunkr.App.UiTests`, launch the app and walk
the design §10 "Manual on-screen" list: two-zone bar, tabbed popup (all 3 tabs, both granularities),
chips row appearing/clearing + tab-jump, search scope, selection second row slide-in (issue +
series), Add overlay (open/Escape/backdrop/create), Details column add/remove + persistence across
relaunch + header click-sort + right-click menu, row-mode hover, Bebas group headers, each
empty-state variant — **both skins** (`default` + `windows_11`). Flip the design doc status to
Implemented with what was verified on-screen vs. left to manual, and note whether the Details table
used the dynamic or fallback approach.

**Depends on:** Steps 1–7
