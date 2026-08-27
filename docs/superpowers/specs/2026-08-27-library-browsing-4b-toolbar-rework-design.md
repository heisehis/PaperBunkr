# Library Browsing — Phase 4b: Toolbar Rework, Row Modes, Details Columns

**Status:** Implemented 2026-08-27. Plan + what was verified:
[2026-08-27-library-browsing-4b-toolbar-rework-plan.md](2026-08-27-library-browsing-4b-toolbar-rework-plan.md).

Two deviations from this design, both deliberate: (1) a **Filter pill stays in Row 1** (opens the
filter popup) rather than existing only as a chips-row "+ Add filter" entry — the chips row is
`IsVisible`-gated on `HasVisibleChips`, so a chip-only entry point would vanish exactly when the
user has no filters and wants to add one. (2) The **Details table** uses a fully declarative
`ItemsControl`-over-`DetailsColumns` (fixed per-column widths, hidden columns drop from a
horizontal `StackPanel`) rather than code-behind-generated `ColumnDefinitions` — simpler, no
width-sync code, and the dynamic-column spike the design asked for succeeded so the flagged
fixed-superset fallback wasn't needed.
**Slice 2 of 2 in Sub-project 4 of 7** (Library browsing) of the full UI rework — see
[Design Language Foundation](2026-08-24-design-language-foundation-design.md) for the phase
breakdown. 4a (poster grid consolidation) shipped 2026-08-27. This slice covers everything else
on the Library screen: the toolbar (a real rework, not a reskin, per the user), selection mode,
the Add-issue form, the List/Details/Tiles row modes, group headers, empty state, and a
**configurable Details column table** with click-header sorting.

## Background

`LibraryScreen.axaml` is ~1,750 lines after 4a. Its toolbar
(`<!-- Toolbar (pills variant) -->`, ~lines 1092–1420) is a flat horizontal row of fully-round
(`CornerRadius 999`) pills on the **pre-Phase-1** token names (`PbChromeBrush`, not the
`PbSurface*` scale), with icons still on the old raster `Border.icon` + `OpacityMask` pattern
(`Filter.png`/`Sort_Ascending.png`/`Layers.png`/`Window.png`). It has:

- browse `‹`/`›` history buttons (`BrowsePreviousCommand`/`BrowseNextCommand`)
- a `TextBox` search (`SearchQuery`, obsolete `Watermark`) + a search-mode pill →
  `Popup` (`SearchMode` enum: All/Series/Writer/Artists/Descriptive/File/Catalog)
- Filter / Sort / Group / Display pills, each → its own `Popup`
  (`IsFilterOpen`/`IsSortOpen`/`IsGroupOpen`/`IsDisplayOpen`, `Toggle*Command`)
- an Add pill → `Popup` with an inline form (`NewIssueSeriesName`/`NewIssueNumber`/
  `NewIssueContentType`/`NewIssueReadingMode` + `SetNewIssue*Command`s, `AddIssueCommand`)
- a selection action bar that **replaces** the whole row when `HasAnySelection`
  (`IsVisible="{Binding !HasAnySelection}"` on the browse row; a sibling bar for the issue-
  selection actions and another for series-selection)

Sort/Group have two field sets — Issue granularity delegates to `IssueList.SortField`/`GroupField`
(catalogued in `IssueListFieldCatalog`, ~30 fields), Series granularity uses this ViewModel's own
`SortField`/`GroupField` (`LibrarySortField`/`LibraryGroupField`, ~7 fields).

Row view modes: `ListItemTemplate` (44px thumb + title/series + unread badge),
`DetailsItemTemplate` (a fixed 6-column `Grid`: thumb, Title, Series, #, unread dot, Publisher),
`TilesItemTemplate` (thumb + 2-line), each with a `Series*` sibling. Group headers use a local
`TextBlock.groupHeader` (bold 13px). No `Avalonia.Controls.DataGrid` dependency exists.

Overlay-hosting precedent: `MainViewModel` has `_isXOverlayOpen` bools + an overlay VM +
`MainWindow.axaml` renders `<Border IsVisible="{Binding IsXOverlayOpen}" Background="#B0000000">
<views:XOverlay .../></Border>` (`QuickRateOverlay`, `ReadingListPropertiesOverlay`).

## Decisions (from the visual brainstorm)

| Area | Decision |
|---|---|
| Toolbar shape | **Two-zone**: browse `‹`/`›` + prominent search on the left; one **"View & Sort" button** (`☰`) + a **"+"** button on the right. Phase 1 tokens, rounded-rect (`PbRadius`), vector icons. |
| View & Sort popup | **Tabbed** — View / Sort / Group. View tab: display mode (6), card content (issue/series), show-titles, density. Sort tab: field list + direction toggle. Group tab: field list. |
| Search scope | **Right edge of the search box** — `All ▾`, opens the 7-mode menu. No separate pill. |
| Filters / current state | **Chips row** under the bar: removable filter chips (`Unread only ✕`, `Publisher: … ✕`, `Missing ✕`, `Tracked ✕`), a `+ Add filter` chip, **and** read-only `Sorted: Name ↑` / `Grouped: Publisher` chips that jump to the matching popup tab. Row hidden entirely when there are no filters and sort/group are at defaults. |
| Selection mode | **Second row** that slides in under the toolbar (toolbar stays put); the issue- and series-selection action sets as before. |
| Add-issue form | **"+" opens a centered FloatingPanel overlay** — the same `Border Classes="floatingPanel"` + dim-backdrop pattern as the editors. Rendered within `LibraryScreen.axaml` over the grid; the form state stays on `LibraryScreenViewModel` (no `MainViewModel` extraction). |
| Row modes | List / Tiles / Details + `Series*` siblings restyled onto tokens (surface hover row, `PbText*` scale). Structure unchanged for List/Tiles. |
| Details table | **Configurable columns** — right-click header → check/uncheck from the field catalog, persisted; **every visible header is a click-to-sort control** (click = sort by it, click again = flip direction). Hand-rolled (no `DataGrid` package). |
| Group headers | **Bebas `pbTextHeading` + count + rule**, in the row modes too (matches 4a's poster grid). |
| Empty state | Vector glyph + a muted line + a contextual action button (`Clear filters` / `Scan folders`). |

## 1. Toolbar extraction

The toolbar markup + its 6 popups + the selection row + the Add overlay is enough new/changed XAML
that it should move out of `LibraryScreen.axaml` into a new **`Views/LibraryToolbar.axaml`**
(`UserControl`, `DataContext` inherited — it binds the same `LibraryScreenViewModel`). `LibraryScreen.axaml`
keeps the grid/list content area and hosts `<views:LibraryToolbar Grid.Row="0" />`. The A-Z jump
indexer and the grid `ScrollViewer`s stay in `LibraryScreen.axaml`. No ViewModel split — one
`LibraryScreenViewModel` still backs both, matching how `IssueListScreen` is composed today.

## 2. Toolbar chrome (`LibraryToolbar.axaml`)

**Row 1** (`Border`, `PbSurface1` bg, bottom border `PbBorderBrush`, `24,14` padding):

- Browse `‹`/`›` — `Path Classes="pbIcon"` `PbIconChevronLeft`/`PbIconChevronRight` (exist), a
  borderless `Button` each, `IsEnabled` bound as now, dimmed when disabled.
- Search — a `Border` (`PbSurface3`, `PbRadius`, 1px `PbBorderBrush`) containing:
  a leading `PbIconSearch` (exists), a `TextBox` (`Classes="pbInput"` or inline token setters,
  `PlaceholderText` not `Watermark`), and a trailing scope control: a borderless `Button` showing
  the current `SearchMode` label + a caret, `Command="{Binding ToggleSearchModeCommand}"`, its
  `Popup` unchanged in content (7 `modeOption` rows) but restyled. `HorizontalAlignment="Stretch"`,
  takes the row's slack.
- `View & Sort` — one `Button Classes="toolbarPill"` (restyled, see §3), `☰` `Path` + label,
  `Classes.open="{Binding IsViewSortOpen}"`, `Command="{Binding ToggleViewSortCommand}"`. The label
  is static "View & Sort" (the chips row carries the live state).
- `+` — a `Button` (`PbAccentBrush` bg, `PbBadgeTextBrush` fg, `PbRadius`), `Command="{Binding
  OpenAddIssueCommand}"` (renamed from the popup toggle).

**Row 2 — chips** (`WrapPanel`, only rendered when `HasVisibleChips`):

- Filter chips: one per active filter, `Border Classes="pbChip"` + an `✕` `Button` running the
  matching clear command (`FilterUnreadOnly`, `FilterMissingIssues`, `FilterTrackedOnly`, plus the
  sidebar-driven `ActiveContentType`/`ActiveCategory` if we choose to surface those — **decide at
  implementation: sidebar filters probably stay sidebar-only, not chipped**).
- `+ Add filter` chip → a small `Popup` with the filter toggles (the old Filter popup's content).
- `Sorted: {SortLabel} {↑|↓}` chip and `Grouped: {GroupLabel}` chip — read-only style, `Command`
  opens `IsViewSortOpen` on the Sort / Group tab respectively (a new `OpenViewSortTabCommand(tab)`).
  The Group chip is hidden when group is `None`.

VM changes:
- `IsFilterOpen` / `IsSortOpen` / `IsGroupOpen` / `IsDisplayOpen` + their `Toggle*Command`s are
  **removed**, replaced by one `IsViewSortOpen` + `ToggleViewSortCommand`, plus `ViewSortActiveTab`
  (enum `View`/`Sort`/`Group`) + `OpenViewSortTabCommand(tab)`.
- `IsSearchModeOpen` + `ToggleSearchModeCommand` **stay** (the scope menu is still its own popup).
- New computed: `HasVisibleChips`, `SortLabel` / `GroupLabel` / `SortDirectionGlyph` (granularity-
  aware), `EmptyStateMessage` / `EmptyStateActionLabel` / `EmptyStateActionCommand` (§9).
- `IsAddOpen` + its toggle → `IsAddIssueOpen` + `OpenAddIssueCommand` / `CloseAddIssueCommand`.
- `ClearAllFiltersCommand` (clears the filter toggles + resets `SearchMode` to `All`; leaves the
  sidebar content-type/category filter alone).

UI automation ids: `LibraryFilterButton` / `LibrarySortButton` / `LibraryGroupButton` /
`LibraryDisplayButton` → `LibraryViewSortButton`; new `LibraryViewSortTab_View` /
`_Sort` / `_Group`; `LibraryViewModeOption_*` and `LibraryGranularityOption_*` ids keep their
names (they just relocate into the View tab).

## 3. Popup + pill restyle

- `Button.toolbarPill` → `PbSurface3` bg, 1px `PbBorderBrush`, `CornerRadius="{DynamicResource
  PbRadius}"` (not 999), `PbTextMutedBrush` fg; `.open` → `PbAccentBrush` border + `PbAccentSoftBrush`
  bg + `PbAccentTextBrush` fg (unchanged semantics). Icon child becomes `Path Classes="pbIcon"`.
- `Border.dropdown` → `PbSurface3` bg, `PbElevationShadow`, `PbRadiusLg`, `PbBorderBrush`.
- `Button.modeOption` → keep (already token-based); `.active` unchanged.
- New icons needed in `Styles/Icons.axaml`: `PbIconViewSort` (`☰` / sliders), `PbIconSortAsc`
  (`⇅` or an A→Z arrow), and reuse `PbIconFilter` (exists), `PbIconLayers` (exists, for Group),
  `PbIconSearch`/`PbIconChevronLeft`/`Right`/`PbIconPlus` (exist). `PbIconGrid` for the View tab if
  wanted. Same hand-computed stroked style as the rest of the set; `icon-mapping.md` gets a
  "Phase 4b" table.

## 4. View & Sort tabbed popup

One `Popup` off the `View & Sort` button, `Border Classes="dropdown"`, ~240px wide. A 3-tab strip
(`View` / `Sort` / `Group`, bound to `ViewSortActiveTab`), then the tab body:

- **View**: `Display mode` (6 `modeOption` rows: Poster grid / Panorama grid / List / Details /
  Tiles / Comic List); `Card content` (Per-issue tiles / Series cards); `Show titles` `CheckBox`
  (`IsVisible="{Binding IsPosterGrid}"`); `Grid density` `Slider` (`IsVisible="{Binding
  IsPosterGrid}"`). This is the current Display popup's content, moved verbatim.
- **Sort**: the granularity-aware field list (the current Sort popup's two `StackPanel`s gated on
  `IsIssueGranularity`/`IsSeriesGranularity`, moved verbatim) + a real direction toggle — two
  `Button`s (`↑ Ascending` / `↓ Descending`) with `.active`, replacing the
  `Content="Direction: {0}"` text row.
- **Group**: the granularity-aware group field list (moved verbatim), `None` first.

## 5. Selection second row

`Border`, `PbAccentSoftBrush` bg, top border `PbAccentBrush`, rendered **below** Row 1/Row 2 with
`IsVisible="{Binding HasAnySelection}"` and a height/opacity `DoubleTransition` on `PbMotionFast`
so it slides in. Content: `{Count} selected` + the existing issue-selection buttons
(`BulkEditSelectionCommand` / `MarkSelectionReadCommand` / `MarkSelectionUnreadCommand` /
`AddToList` / `DeleteSelectionCommand` / `ClearSelectionCommand`) or the series-selection set
(`BulkEditSeriesSelectionCommand` / `DeleteSeriesSelectionCommand` / `ClearSeriesSelectionCommand`),
switched on `HasSeriesSelection` vs `HasSelection`. The buttons become `Button Classes="pbChip"`
(button variant). Row 1 no longer hides on selection — drop `IsVisible="{Binding !HasAnySelection}"`.

## 6. Add-issue overlay

A `Border Classes="floatingPanel"` centered in a full-bleed dim `Border` (`#B0000000`), both gated
`IsVisible="{Binding IsAddIssueOpen}"`, layered as the last child of `LibraryScreen.axaml`'s root
`Grid` (over everything). Content = the current inline Add form (`AutoCompleteBox` series name,
`TextBox` number, content-type `modeOption` list, reading-mode `modeOption` list, `AddIssueCommand`)
re-laid-out with a header ("Add issue to library"), `PlaceholderText`, and Add / Cancel buttons.
`Escape` and a backdrop click close it (`CloseAddIssueCommand`). The old `AddButton` `Popup` is
deleted.

## 7. Row modes restyle

`ListItemTemplate` / `TilesItemTemplate` / `DetailsItemTemplate` + `Series*` siblings:
- surface tokens for the row hover (`ListBoxItem`/`Button` root gets a `:pointerover` →
  `PbSurface2Brush` background), `PbText*` type scale, `PbRadiusSm` on the thumb.
- unread indicator → the same small `PbAccentTextBrush` dot the poster tiles use (4a).
- List/Tiles structure otherwise unchanged.

## 8. Configurable Details table

### Field catalog

Extend `IssueListSortFieldDescriptor` (`Models/IssueListFieldCatalog.cs`) with
`Func<IssueListRow, string?>? Display` — the cell text for that field (null = not offered as a
column; e.g. `Status` stays sort-only). Populate `Display` for every field with a sensible string
projection (`r => r.Year?.ToString()`, `r => FormatFileSize(r.FileSize)`, `r => $"{r.Rating:0.#}"`,
`r => r.Publisher`, …). A new `IssueListFieldCatalog.ColumnFields` read-only list exposes just the
descriptors with a non-null `Display`, in a stable display order.

### Persistence

New `AppSettings.LibraryDetailsColumns` (`string`, nullable) — a comma-joined list of
`IssueListSortField` enum names, e.g. `"Title,Series,Number,Volume,Year,PageCount,Publisher,Format,
Rating,Added,ReadPercentage"`. Null → a curated default set (defined as a `static readonly
IssueListSortField[]` on the catalog). Loaded/saved by `LibraryScreenViewModel` alongside the other
`Library*` settings; exposed as `ObservableCollection<DetailsColumn>` (`DetailsColumn` = `Field` +
`DisplayName` + `IsVisible`).

**The configurable table is Issue-granularity only.** Series granularity keeps its existing fixed
`SeriesDetailsItemTemplate` (token-restyled, not column-pickable) — series aggregates don't carry
the ~40-field surface issues do, and a second parallel catalog + persistence isn't worth it. The
right-click "Columns" menu and the header sort-toggles appear only in Issue granularity.

### Rendering

Hand-rolled, no `DataGrid`:
- A header `Grid` whose `ColumnDefinitions` are generated in code-behind from the visible-column
  list (a `*`-ish width per text column, `Auto` for the thumb), rebuilt when the column list
  changes. Each header cell is a `Button Classes="detailsHeader"` — `Command` = sort-by-this-field
  (`SetDetailsSortCommand(field)`), showing a `↑`/`↓` glyph when it's the active sort field. A
  right-click `ContextMenu` on any header lists `ColumnFields` as checkable `MenuItem`s bound to
  each `DetailsColumn.IsVisible`.
- The rows: an `ItemsControl` over `IssueList.Rows` (grouped: over `IssueList.Groups`), item
  template = a `Grid` with the **same generated `ColumnDefinitions`** and one `TextBlock` per
  visible column, `Text` bound through a `DetailsCellConverter` (`{Binding ., Converter=…,
  ConverterParameter={x:Static IssueListSortField.Year}}` → `descriptor.Display(row)`), or the
  code-behind builds the row `Grid`s directly. Keep the existing `Button` row root for click-to-
  open + context menu + selection.
- `SetDetailsSortCommand` writes `IssueList.SortField`/`SortDirection` (reuses the existing sort
  pipeline — the Details header is just another way to set it, and the `Sorted: …` chip reflects
  it). Clicking the already-active field flips `SortDirection`.

## 9. Group headers + empty state

- Replace `TextBlock.groupHeader` usages in the List/Details/Tiles grouped `ItemsControl`s with the
  same `DockPanel` header 4a introduced for the poster grid (`pbTextHeading` + count `Run` + 1px
  rule). Delete the `TextBlock.groupHeader` style.
- Empty state: a centered `StackPanel` (`IsVisible` when the active collection is empty) —
  `Path Classes="pbIcon"` (`PbIconLayers` or `PbIconSearch` depending on whether a search/filter is
  active), a `pbTextBody` line ("No comics match this filter." / "No results for \"{query}\"." /
  "This library is empty."), and a `Button` (`Clear filters` when filters/search active →
  `ClearAllFiltersCommand`; `Scan folders` when the whole library is empty → opens Preferences →
  Libraries). One shared control, message + action chosen by VM computed properties
  (`EmptyStateMessage`, `EmptyStateActionLabel`, `EmptyStateActionCommand`).

## 10. Testing

**Automated (`LibraryScreenViewModelTests` + a new `IssueListFieldCatalogTests` case):**
- `ViewSort` popup state: `ToggleViewSortCommand` flips `IsViewSortOpen`; `OpenViewSortTabCommand(Sort)`
  sets `IsViewSortOpen` + `ViewSortActiveTab == Sort`.
- Chips: `HasVisibleChips` true when any filter on or group ≠ None or sort ≠ default; the
  `Sorted:`/`Grouped:` labels are granularity-correct.
- `IsAddIssueOpen` open/close; `AddIssueCommand` still creates the placeholder issue (existing test
  retargeted from the popup toggle).
- Selection: `HasAnySelection` no longer gates Row 1 (n/a to VM tests — visual).
- `IssueListFieldCatalog`: every `ColumnFields` descriptor has a non-null `Display` and it returns
  without throwing for a fully-populated and a fully-null `IssueListRow`; `Display` count matches
  the intended column set.
- Details columns: `LibraryDetailsColumns` round-trips through `AppSettings` (default when null,
  parsed list otherwise, unknown enum names skipped); toggling a `DetailsColumn.IsVisible` persists;
  `SetDetailsSortCommand(field)` sets `IssueList.SortField`, calling it again flips
  `IssueList.SortDirection`.
- Build with the AVLN2000 guard (new `.axaml` file `LibraryToolbar.axaml` — add its `.axaml.cs`
  in the same step per `CLAUDE.md`).

**`Paperbunkr.App.UiTests`:** `LibraryListLayoutPersistenceTests` / `LibraryComicListModeTests` use
`LibraryViewModeOption_List` / `_IssueList` — those move into the View & Sort popup's View tab but
keep their ids; update the tests to open `LibraryViewSortButton` first. Add a smoke test: open the
View & Sort popup, switch to Details, confirm a `detailsHeader` is present and click-sorts.

**Manual on-screen:** the full toolbar (two-zone bar, tabbed popup, chips row appearing/clearing,
search scope, selection second row sliding in, Add overlay), both skins; Details column
add/remove + persistence across relaunch + header click-sort + right-click header menu; row-mode
hover; Bebas group headers in a list; each empty-state variant.

## Risks / notes

- **Biggest risk is scope** — this is the largest single UI-rework slice. The plan must sequence it
  so there's a green build after each numbered area, and the toolbar extraction (§1) lands first as
  a pure move (no behaviour change) before the rework starts.
- **`LibraryScreenViewModel` is already ~1,600 lines** and this adds ~150. Not splitting the VM
  (the toolbar and grid genuinely share filter/sort/selection state), but the new toolbar members
  should be grouped into a clearly-commented region.
- **Hand-rolled Details table** with code-behind-generated columns is the fiddliest piece.
  Fallback if it fights Avalonia: a fixed superset of ~14 columns with per-column `IsVisible`
  (no true dynamic `ColumnDefinitions`, just collapse-to-zero-width) — uglier but simpler. The
  plan should spike the dynamic approach first and fall back only if needed.
- **Search-mode chip**: a non-`All` search scope is easy to forget — surface it as a
  `Search: Writer ✕` chip in Row 2 (clearing it resets to `All`). Cheap, worth doing.
- **`Watermark` → `PlaceholderText`** also silences the pre-existing `AVLN5001` obsolete warnings
  on the Library search / Add form fields (a small free cleanup).
