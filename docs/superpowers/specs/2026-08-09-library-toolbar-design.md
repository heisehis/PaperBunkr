# Library Toolbar — Display Modes, Search/Filter, Sort/Group, Overlays — Design Spec

*Date: 2026-08-09. Scope: `LibraryScreenViewModel`/`LibraryScreen.axaml` only. By far the largest
sub-project of the P4-adjacent/UI-polish pass this doc's siblings (Smart Lists results view, Plugin
screen cleanup, Library sidebar categorization) came out of. One spec, four implementation phases,
landed as separate commits — matching how docs/superpowers/specs/
2026-08-09-reader-gestures-and-grid-navigation-design.md split Phase A/B.*

## 0. Problem

Every Library toolbar control is decorative. The search box is a static `TextBlock` inside a
`Border`, not a real `TextBox`. "Filter ▾"/"Sort ▾"/"Display ▾" open `Popup`s full of hand-written
`TextBlock` rows with fake checkmarks (`"Tracked series ✓"`) — none bound to anything, no
`CheckBox`/`Command` in sight. Display only has two real modes (Grid/List). There's no Group
feature at all.

## 1. Scope decisions carried in from brainstorming (not re-litigated here)

- **All 7 display modes ship now**, not a smaller first pass.
- **Panorama grid sizes each tile from its cover's real `Bitmap.PixelSize`** (landscape covers
  render wide, portrait covers render portrait), not a single fixed crop-box.
- **Search covers `Series.Name`/`Publisher`/`Genre`** — not CE's full
  `ComicBookAllPropertiesMatcher` sweep across every field, and not its `NOT`/`MATCH` query-syntax
  power-user mode. Both are real CE behavior but more machinery than this pass needs; flagged as a
  Beta stretch goal, not committed to.
- **Sort reuses Smart Lists' *catalog as inspiration*, not its fields directly** — Smart Lists
  fields are mostly per-Issue; Library sorts *series*. The 7 sort keys below are Series-level
  aggregates of real existing data, chosen because they aggregate cleanly (not a 1:1 catalog port).
- **Group ships on all 7 display modes**, via one shared card template per mode reused in both a
  flat (ungrouped) and grouped rendering shape — not 14 independently hand-written templates.
- **Group dimensions are Content Type, Publisher, and Alphabetical** — Category/Collection is
  explicitly excluded as a group dimension (many-to-many: a series in 2 categories would need to
  appear in 2 groups, not solved here).
- **Overlay toggles are session-only state**, matching the existing (already-unpersisted)
  `ViewMode` precedent. Persisting them is explicitly Beta-scoped already —
  `alpha-roadmap.md`'s Beta backlog literally lists "Saved Workspaces (display-setting presets)"
  and "Saved List Layouts (grid/sort/group presets — `LibraryScreen` already has decorative UI
  stubbed for this)". This pass makes that decorative UI real; persisting the *choice* stays Beta.
- **Komikku's "Tabs" section (show category tabs / show hidden categories / show number of items)
  is entirely out of scope** — it's a mobile tab-bar paradigm; Paperbunkr's sidebar (just wired to
  real Content Type/Collections data in the prior sub-project) already serves that purpose, "hidden
  categories" needs an unrequested schema field, and "show number of items" is already always-on.

## 2. Phase A — Display modes + density

### A1. `Models/LibraryViewMode.cs`
Extend the enum: `CompactGrid, ComfortableGrid, CoverOnlyGrid, PanoramaGrid, List, Details, Tiles`.
(Renamed from `Grid`/`List` — confirmed no other file references those two names besides
`LibraryScreenViewModel`/`LibraryScreen.axaml`.)

### A2. `Models/SeriesCardSample.cs`
Add:
```csharp
public double PanoramaWidth { get; init; }   // computed per-series, see below
public string? Publisher { get; init; }      // Series.Publisher, for Details view + Publisher badge (Phase D)
```
`FromSeries` computes `PanoramaWidth`:
```csharp
const double PanoramaHeight = 146, MinWidth = 110, MaxWidth = 320, DefaultAspectRatio = 2.0 / 3.0;
double aspectRatio = coverImage is { } bmp && bmp.PixelSize.Height > 0
    ? (double)bmp.PixelSize.Width / bmp.PixelSize.Height
    : DefaultAspectRatio;
double panoramaWidth = Math.Clamp(aspectRatio * PanoramaHeight, MinWidth, MaxWidth);
```

### A3. `LibraryScreenViewModel`
- `[ObservableProperty] private double _gridDensity = 1.0;` — width multiplier applied to
  Compact/Comfortable/Cover-only/Panorama/Tiles card widths (`baseWidth * GridDensity`), range
  clamped `0.6`–`1.6` in the setter. No effect on List/Details (single-column). Real, functional
  version of the currently-fake "Grid density" slider — reinterpreted from Komikku's mobile
  "items per row" as a continuous density control, since a fixed items-per-row count doesn't map
  cleanly onto a resizable desktop window the way it does a fixed mobile viewport.
- `IsCompactGrid`/`IsComfortableGrid`/etc. — seven `bool` computed properties off `ViewMode`,
  same pattern `IsGridView`/`IsListView` already use, all raised in `OnViewModeChanged`.
- `[RelayCommand] SetViewMode(LibraryViewMode mode) => ViewMode = mode;` replaces
  `ShowGridView`/`ShowListView`.

### A4. `LibraryScreen.axaml`
- Each of the 7 card `DataTemplate`s defined once in `UserControl.Resources` with an `x:Key`
  (`CompactGridItemTemplate`, etc.) — referenced via `{StaticResource}` from Phase A's flat
  `ItemsControl`s *and* reused as-is by Phase C's grouped rendering, so nothing is duplicated.
- Seven `ScrollViewer`+`ItemsControl` pairs (`WrapPanel` for the 5 grid-family modes incl. Tiles,
  plain vertical `StackPanel` for List/Details), each `IsVisible` bound to its `Is*` property.
- Card widths for the 5 grid-family modes bind directly to *computed* pixel-width properties on
  `LibraryScreenViewModel` (`CompactCardWidth`, `ComfortableCardWidth`, ...), each
  `baseWidth * GridDensity`, recomputed and raised in `OnGridDensityChanged` — avoids introducing
  this codebase's first `IValueConverter` for what's otherwise a one-line multiply.
- **Details mode**: a header `Grid` (Cover/Name/Content Type/Issues/Unread/Publisher columns) above
  the `ItemsControl`, each row using matching `ColumnDefinitions` — hand-rolled, no DataGrid
  dependency added.
- **Tiles mode**: small (48×68) thumbnail + 2-line text block (`Name` bold, then
  `"{ContentType} · {N} issues"`), wrapping via `WrapPanel` like the grid modes, just narrower
  entries than Comfortable/Compact.
- Display dropdown popup content replaced: 7 mode-select buttons (radio-style, active state =
  current `ViewMode`) + the `GridDensity` slider (real `Slider` control now, not a fake `Border`).
  Overlay toggles join this same popup in Phase D.

## 3. Phase B — Search, Filter, A-Z indexer

### B1. `LibraryScreenViewModel`
- `[ObservableProperty] private string _searchQuery = string.Empty;` — matches against
  `Series.Name`/`Publisher`/`Genre`, case-insensitive substring.
- `[ObservableProperty] private bool _filterUnreadOnly;` — `UnreadCount > 0`.
- `[ObservableProperty] private bool _filterMissingIssues;` — any issue `FileIsMissing`.
- `[ObservableProperty] private bool _filterTrackedOnly;` — `Series.TrackingLinks.Any()`.
- All three `partial void On*Changed` call `LoadFromDatabase()`; search box debounces via a
  `DispatcherTimer` (150ms) rather than requerying on every keystroke — the one new piece of
  infra this phase needs, since nothing else in this codebase debounces text input yet.
- Filtering order in `LoadFromDatabase()`: base series list → sidebar Content
  Type/Collection filter (existing, prior sub-project) → search text → the 3 checkboxes — all AND,
  narrowing the same set, not alternate views.

### B2. `LibraryScreen.axaml`
- Search `Border`+`TextBlock` → real `TextBox` bound `Text="{Binding SearchQuery}"`,
  `Watermark="Search all series…"`.
- Filter popup's 3 static rows → real `CheckBox`es bound to `FilterUnreadOnly`/
  `FilterMissingIssues`/`FilterTrackedOnly`.
- Filter pill moves to sit directly adjacent to the search box (same toolbar segment), not
  scattered with Sort/Display elsewhere.

### B3. A-Z indexer (`LibraryScreen.axaml` + new code-behind method)
- A vertical strip of `Button`s (A–Z, `#` for non-alphabetic) along the grid area's right edge.
  `IsVisible` only when `Sort == LibrarySortField.Name` — jumping alphabetically only means
  something when the list is alphabetically ordered.
- Click handler (code-behind, `LibraryScreen.axaml.cs`) finds the first visible series starting
  with that letter in the *currently rendered* (filtered/sorted/possibly-grouped) sequence, looks
  up its flat index, and sets the active `ScrollViewer`'s `Offset` to `index * estimatedRowHeight`
  for the current mode. **Approximate, not pixel-perfect** — there's no virtualized `ListBox`
  backing this grid to give a real `ScrollIntoView`, and building one is out of scope here.

## 4. Phase C — Sort + Group

### C1. New: `Models/LibrarySortField.cs`, `Models/SortDirection.cs`, `Models/LibraryGroupField.cs`
```csharp
public enum LibrarySortField { Name, DateAdded, LastRead, Size, IssueCount, UnreadCount, Publisher }
public enum SortDirection { Ascending, Descending }
public enum LibraryGroupField { None, ContentType, Publisher, Alphabetical }
```

### C2. New: `Models/SeriesCardGroup.cs`
```csharp
public class SeriesCardGroup
{
    public required string Header { get; init; }
    public required ObservableCollection<SeriesCardSample> Items { get; init; }
}
```

### C3. `SeriesCardSample` / `FromSeries`
Add `DateTime? LastAddedTime`, `DateTime? LastOpenedTime`, `long TotalFileSize` — each a `MAX`/`SUM`
over `series.Issues`, computed once in `FromSeries` (not recomputed per-sort-click).

### C4. `LibraryScreenViewModel`
- `[ObservableProperty] private LibrarySortField _sortField = LibrarySortField.DateAdded;`
  `[ObservableProperty] private SortDirection _sortDirection = SortDirection.Descending;`
  `[ObservableProperty] private LibraryGroupField _groupField = LibraryGroupField.None;`
- `public bool IsGrouped => GroupField != LibraryGroupField.None;`
- `LoadFromDatabase()` (extended, not a new method — same "always rebuild from a fresh query"
  convention every other ViewModel in this codebase already uses): after the existing
  filter/search chain, sort the resulting series by `SortField`/`SortDirection`. If `IsGrouped`,
  partition into `Groups` (new `ObservableCollection<SeriesCardGroup>`); else populate the existing
  flat `Covers` as today. Group order: `ContentType` groups by enum order (matches sidebar
  convention), `Publisher`/`Alphabetical` groups alphabetically by header; `Sort` still governs
  order *within* each group.

### C5. `LibraryScreen.axaml`
- Sort popup: 7 sort-field rows (radio-style) + an ascending/descending toggle, replacing the
  current single hardcoded "Recently Added" row.
- Group popup (new — the toolbar pill this feature needs, next to Sort): 4 rows (None/Content
  Type/Publisher/Alphabetical), radio-style.
- Every one of Phase A's 7 flat `ItemsControl`s gets an `IsVisible="... AND !IsGrouped"` added, and
  a sibling outer `ItemsControl ItemsSource="{Binding Groups}" IsVisible="... AND IsGrouped"` whose
  `DataTemplate` is a header `TextBlock` + an inner `ItemsControl` using the *same*
  `{StaticResource}` template and panel type as its ungrouped sibling, bound to `Items`. 14
  `ItemsControl`s total, 7 template definitions.

## 5. Phase D — Overlay toggles

### D1. `SeriesCardSample` / `FromSeries`
Add `string? LanguageIso` (cover issue's `LanguageISO`), `int? ContinueReadingIssueId` (first issue
with `LastPageRead is null or 0` in `OrderByNumber()` order — reuses the exact ordering
`DetailTabsViewModel`/`ReaderScreenViewModel` already rely on elsewhere).

### D2. `LibraryScreenViewModel`
- `[ObservableProperty] private bool _showUnreadBadge = true;` (matches today's implicit
  always-on behavior as the default)
  `[ObservableProperty] private bool _showPublisherBadge;`
  `[ObservableProperty] private bool _showLanguageBadge;`
  `[ObservableProperty] private bool _useLanguageIcon;` (globe glyph vs. raw ISO text — no
  per-country flag asset set, that's real new-asset scope this pass doesn't take on)
  `[ObservableProperty] private bool _showContinueReadingButton;`
- New constructor param `Action<int> goReaderForIssue`, wired in `MainViewModel` to the already-
  existing `GoReaderForIssue` (no new `MainViewModel` method needed).
- `[RelayCommand] private void ContinueReading(SeriesCardSample? card)` →
  `if (card?.ContinueReadingIssueId is int id) goReaderForIssue(id);`

### D3. `LibraryScreen.axaml`
- Each of the 7 card templates gains: unread badge (`IsVisible` now bound through
  `Library.ShowUnreadBadge`, was previously unconditional), a publisher badge, a language badge
  (text or globe glyph per `UseLanguageIcon`), and a play-icon overlay button (bound to
  `ContinueReadingCommand`, separate hit target from the card's own click-to-Detail `Command`
  the same way `PageCanvas` already separates click zones from keyboard handling elsewhere).
- Display dropdown popup gains 5 `CheckBox` rows for the toggles above, alongside Phase A's mode
  buttons and density slider — one popup, matching Komikku's own single "Display" tab bundling
  mode + density + overlays together.

## 6. Explicitly not doing

- CE's full `ComicBookAllPropertiesMatcher` search sweep and `NOT`/`MATCH` query syntax.
- Persisting `ViewMode`/`GridDensity`/sort/group/overlay-toggle choices — Beta-scoped already
  (`Saved Workspaces`/`Saved List Layouts`).
- Grouping by Category/Collection (many-to-many).
- Komikku's Tabs section (category tabs, hidden categories, item-count toggle).
- Per-country flag icon assets for the language badge.
- A true virtualized/pixel-perfect A-Z scroll-to-item.

## 7. Testing

Per phase, extending `LibraryScreenViewModelTests` (existing file, DB-override pattern):
- **A**: `SetViewMode_UpdatesIsXProperties`, `GridDensity_ClampsToRange`,
  panorama-width unit coverage added to a `SeriesCardSampleTests` (new — this model has had no
  direct tests before; `FromSeries`' aspect-ratio/clamp math is exactly the kind of pure-ish logic
  worth covering directly, same reasoning `ZoomPanMathTests` used).
- **B**: `SearchQuery_FiltersByNamePublisherGenre`, `FilterUnreadOnly_NarrowsCovers`,
  `FilterMissingIssues_NarrowsCovers`, `FilterTrackedOnly_NarrowsCovers`, combined-filter case
  (search text + a checkbox + sidebar content-type filter all active together).
- **C**: `SortField_OrdersSeriesCorrectly` (one case per field), `GroupField_PartitionsIntoGroups`
  (one per dimension), `Sort_AppliesWithinGroups_NotJustAcrossThem`.
- **D**: `ContinueReadingCommand_NavigatesToFirstUnreadIssue`,
  `ContinueReadingIssueId_NullWhenAllIssuesRead`.
- Manual (all phases, since no desktop GUI automation is available in this environment): switch
  through all 7 display modes and confirm each renders sensibly; verify Panorama shows a landscape
  cover wide and a portrait cover narrow side by side; type in search, toggle each filter checkbox;
  click every A–Z letter and confirm the scroll lands in the right neighborhood; try every sort
  field/direction and every group dimension together; toggle every overlay checkbox and confirm
  each badge/button appears only when its toggle is on; click Continue Reading and confirm it lands
  in the Reader on the right issue.
