# Library Master-Detail Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-14-library-visual-redesign-design.md*

Grounded against the real current code (not the design doc's assumptions) during planning — see
that doc's "Implementation-time shape corrections" section and its §1 correction for the two real
surprises: the Collections/Reading Lists/Smart Lists sidebar lives in `Views/MainWindow.axaml`
(shell chrome), not inside `LibraryScreen.axaml`; and hover state (dog-ear peek) is control-level
code-behind, not a row-model property, so "Selection > Dog-ear" is a one-line gate change, not a
new `CornerBadgeKind` pipeline.

## Step 1: Data layer — enum consolidation, new settings, migration

**Files:**
- `src/Paperbunkr.Data/Entities/LibraryViewMode.cs` (edit) — reduce to `{ Grid, List, DetailsTable }`
- `src/Paperbunkr.Data/Entities/LibraryGridCoverFit.cs` (new) — `{ Poster, Panorama }`
- `src/Paperbunkr.Data/Entities/LibraryListDensity.cs` (new) — `{ Comfortable, Compact }`
- `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit) — add 4 properties, following the file's
  existing style exactly (XML-doc `<summary>` citing this design doc, inline default):
  - `public LibraryGridCoverFit LibraryGridCoverFit { get; set; } = LibraryGridCoverFit.Poster;`
  - `public LibraryListDensity LibraryListDensity { get; set; } = LibraryListDensity.Comfortable;`
  - `public double LibraryPreviewPanelWidth { get; set; } = 320;`
  - `public bool IsLibraryPreviewPanelVisible { get; set; } = true;`
  - Place near the existing `LibraryViewMode`/`LibraryGridDensity` block (`AppSettings.cs:220-245`).
- New migration, name `ConsolidateLibraryViewModes` (mirror
  `20260827045244_LibraryPosterGridConsolidation.cs` exactly — that file is the template for
  ordering: `AddColumn` first for the 4 new plain columns, then `migrationBuilder.Sql("UPDATE ...")`
  raw-SQL remaps of the persisted `LibraryViewMode` string column (old values are the enum member
  names as strings, per that file's convention — confirm the exact stored string format by reading
  the `LibraryViewMode` column's current `AlterColumn`/`HasConversion` setup in
  `PaperbunkrDbContext.OnModelCreating` before writing the SQL), then `AlterColumn` for
  `LibraryViewMode`'s new default/reduced value set last (SQLite full-table-rebuild ordering, per
  that file's own comment explaining why). Remap logic:
  - `LibraryViewMode = 'PosterGrid'` → `LibraryViewMode = 'Grid'`, `LibraryGridCoverFit = 'Poster'`
  - `LibraryViewMode = 'PanoramaGrid'` → `LibraryViewMode = 'Grid'`, `LibraryGridCoverFit = 'Panorama'`
  - `LibraryViewMode = 'List'` → stays `'List'`, `LibraryListDensity = 'Comfortable'`
  - `LibraryViewMode = 'Tiles'` → `LibraryViewMode = 'List'`, `LibraryListDensity = 'Compact'`
  - `LibraryViewMode = 'Details'` → `LibraryViewMode = 'DetailsTable'`
  - `Down()`: best-effort lossy reverse (mirror the template's `Down` — `DetailsTable`→`Details`,
    `Grid`+`CoverFit=Panorama`→`PanoramaGrid`, `Grid`+other→`PosterGrid`,
    `List`+`Density=Compact`→`Tiles`, `List`+other→`List`; drop the 4 new columns).
- Run `dotnet ef migrations add ConsolidateLibraryViewModes --project src/Paperbunkr.Data` (or
  hand-author following the template, whichever this repo's convention actually is — check whether
  prior migrations were scaffolded then hand-edited, per the design doc's citation of that pattern)
  and confirm `PaperbunkrDbContextModelSnapshot.cs` regenerates correctly.

**Depends on:** none.

**Verify:** New test `src/Paperbunkr.Data.Tests/ConsolidateLibraryViewModesMigrationTests.cs`
mirroring `LibraryPosterGridMigrationTests.cs`'s exact pattern (temp SQLite file, migrate to the
migration immediately prior — `20260914121101_AddConfirmBeforeClose` — seed a legacy
`LibraryViewMode` string via raw SQL, `migrator.Migrate()` to latest, read back via
`SqlQueryRaw<T>`), one case per old value confirming the correct new value + toggle combination.
`dotnet ef migrations has-pending-model-changes` clean.

---

## Step 2: `LibraryScreenViewModel` — view-mode/toggle properties

**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit)

**What:**
- Update the `ViewMode`-derived booleans at (current) lines 3275-3279 from the 5-way switch to the
  new 3-way + 2 independent toggles:
  ```csharp
  public bool IsGridView => ViewMode == LibraryViewMode.Grid && !IsCollectionView;
  public bool IsListView => ViewMode == LibraryViewMode.List && !IsCollectionView;
  public bool IsDetailsTableView => ViewMode == LibraryViewMode.DetailsTable && !IsCollectionView;
  ```
  Grep this file and `LibraryScreen.axaml`/`LibraryToolbar.axaml` for every use of the old
  `IsPosterGrid`/`IsPanoramaGrid`/`IsTilesView`/`IsDetailsView` names before deleting them — update
  each call site to the new names (`IsPanoramaGrid` becomes `IsGridView && GridCoverFit ==
  LibraryGridCoverFit.Panorama`, etc., wherever a template needs to distinguish the sub-toggle, not
  just the mode).
- Add `[ObservableProperty] private LibraryGridCoverFit _gridCoverFit` and
  `[ObservableProperty] private LibraryListDensity _listDensity`, loaded/saved alongside `ViewMode`
  wherever that's currently persisted (find the existing load/save block — likely near where
  `FadeInThumbnails`/etc. were added per the cosmetic-toggles work, and near `SetViewMode` at line
  3314) — mirror that exact load-on-init / save-on-change pattern, don't invent a new one.
- Update `SetViewMode`/`DisplayModeLabel` (lines 3314-3322) for the 3-value enum; add
  `SetGridCoverFit`/`SetListDensity` command methods analogous to `SetViewMode`.
- `WorkspaceState.cs:25`'s default (`LibraryViewMode.PosterGrid`) → `LibraryViewMode.Grid`.

**Depends on:** Step 1 (enum values must exist).

**Verify:** `dotnet build` compiles (this step alone will not yet compile if XAML still references
old boolean names — acceptable transiently within this step's own commit boundary is not assumed;
in practice do this step and Step 5's XAML boolean-rename together before attempting a build, since
Avalonia's compiled bindings are checked against these exact property names per this project's own
AVLN2000 build gotcha).

---

## Step 3: `LibraryScreenViewModel` — preview panel state & collapse toggle

**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit)

**What:**
- New fields for "last single-focused item" (per the design doc's implementation-time correction —
  not from `TileSelectionController<T>`, which has no such concept): find every call site of
  `Selection.ReplaceSelection(...)`/`Selection.Toggle(...)` and `SeriesSelection.ReplaceSelection(...)
  /.Toggle(...)` in this file (the tile click/selection command methods), and at each, also set a
  new `[ObservableProperty] private IssueListRow? _previewIssue` /
  `[ObservableProperty] private SeriesCardSample? _previewSeries` to the `item` argument passed into
  that call — this is the panel's content source, independent of how many items end up selected.
- New computed properties for the panel's 4 states (series/issue/idle-empty/no-results), e.g.:
  ```csharp
  public bool ShowSeriesPreview => IsSeriesGranularity && PreviewSeries is not null;
  public bool ShowIssuePreview => IsIssueGranularity && PreviewIssue is not null;
  public bool ShowNoResultsPreview => !HasVisibleItems; // name TBD against the real "zero results" flag already backing the grid's own empty state
  public bool ShowIdlePreview => !ShowSeriesPreview && !ShowIssuePreview && !ShowNoResultsPreview;
  ```
  Find the existing property this screen already uses to render its own empty-grid state (it must
  exist today for an empty library) and reuse it for `ShowNoResultsPreview` rather than
  re-deriving zero-results logic independently.
- `[ObservableProperty] private bool _isLibraryPreviewPanelVisible` loaded from/saved to
  `AppSettings.IsLibraryPreviewPanelVisible` (Step 1), plus a `ToggleLibraryPreviewPanelCommand`
  RelayCommand method flipping it — this is what both the toolbar button and the `Ctrl+B` shortcut
  (Step 8) invoke.
- New transient (non-persisted) scroll-position field(s) for Step 9 — hold off wiring the actual
  restore logic until Step 9, but the storage field can be added here alongside the other new state.

**Depends on:** Step 1 (settings), Step 2 (granularity/view-mode booleans it references).

**Verify:** Unit tests (added in Step 10) target these computed properties directly.

---

## Step 4: New `LibraryPreviewPanel` component

**Files:**
- `src/Paperbunkr.App/Views/LibraryPreviewPanel.axaml` (new)
- `src/Paperbunkr.App/Views/LibraryPreviewPanel.axaml.cs` (new — **must be added in this same step**,
  per this project's own documented build gotcha: a fresh `.axaml`'s `x:Class` with no matching
  compiled partial class fails `CompileAvaloniaXamlTask` with `AVLN2000`, and a plain retry after
  that failure can silently ship a never-rewoven assembly)

**What:** `UserControl`, `x:DataType="vm:LibraryScreenViewModel"` (same pattern as
`LibraryToolbar.axaml`). Root content wrapped in `ScrollViewer HorizontalScrollBarVisibility="Disabled"`
per the design doc's §4 correction. Four `IsVisible`-gated sections bound to `ShowSeriesPreview`/
`ShowIssuePreview`/`ShowIdlePreview`/`ShowNoResultsPreview` (Step 3):

- Series section binds directly to `PreviewSeries` (`SeriesCardSample`, `x:DataType` on that
  section) — cover (`CoverBrush`), `Title`/`Name`, `IssueCountLabel`, `UnreadCount`, `Publisher`/
  `HasPublisher`, `LanguageIso`/`HasLanguage`, `SeriesStatusLabel`, `RepresentativeRow.SummaryExcerpt`,
  a `PosterRail` bound to the series' issue list (find how the existing Detail screens' related-rail
  sources its `ItemsSource` and mirror that shape) with `IsTabStop="False"` on `Button.railCard`
  inside this specific instance (style selector scoped to this panel, not a global `PosterRail`
  change — other `PosterRail` usages on Detail screens should stay Tab-reachable), and quick-action
  buttons (Continue/Mark Read/Add to Collection) bound to whatever commands the series card/tile
  context menu already uses (reuse, don't duplicate, `LibraryContextMenuBuilder`'s command
  references).
- Issue section binds directly to `PreviewIssue` (`IssueListRow`) — cover, `SeriesName`+`Number`,
  `Publisher`/`LanguageIso`, `IsRead`, `Rating`/`HasRating`, `WriterAndPenciller`, `FileSizeDisplay`,
  `Format`, quick actions (Read/Mark unread/Edit metadata) bound to existing
  `LibraryScreenViewModel` commands (`GoReaderForIssue`-backed command, mark-read toggle, issue
  properties command — find their exact current names rather than inventing new ones).
- Idle section: static "Select a series or issue to preview" text.
- No-results section: static "No results for this search" text — distinct copy from idle.

**Depends on:** Step 3 (all bound properties must exist first, since Avalonia's compiled bindings
are checked against the real VM shape at compile time in this project).

**Verify:** `dotnet build` (full rebuild if this is genuinely the first build after adding the file —
per the project's gotcha, delete `Paperbunkr.App.dll`/`.pdb` from `obj/Debug/net8.0/` first if any
earlier build in this session already failed inside XAML compilation, don't just retry).

---

## Step 5: `LibraryScreen.axaml` — 3-column layout, splitter, toolbar span

**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit),
`src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit, only if new code-behind wiring is needed
for the splitter — check first, `GridSplitter` is usually markup-only)

**What:**
- Root `Grid` (currently `RowDefinitions="Auto,*"`, line 980) gains
  `ColumnDefinitions="*,Auto,Auto"` on the row-1 content area — concretely, since row 1 today holds
  multiple mutually-`IsVisible`-exclusive view-mode containers directly at `Grid.Row="1"` with no
  wrapping element (per the real structure found during planning), the cleanest change is: wrap all
  existing row-1, column-less content in a `Grid.Column="0"` (implicit, no attribute needed if it's
  the first column) — i.e. every existing `Grid.Row="1"` child additionally needs nothing added
  (column defaults to 0), then add two new siblings at `Grid.Row="1" Grid.Column="1"` (the
  `GridSplitter`) and `Grid.Row="1" Grid.Column="2"` (`<views:LibraryPreviewPanel>`).
- `<views:LibraryToolbar x:Name="Toolbar" Grid.Row="0" />` (line 984) → add `Grid.ColumnSpan="3"`.
- New `GridSplitter` at `Grid.Row="1" Grid.Column="1"`: `Width="6"`, `ResizeDirection="Columns"`,
  `IsVisible="{Binding IsLibraryPreviewPanelVisible}"` **and** bound to also hide when
  `IsDetailsTableView` (per design §2) — combine both conditions (a small
  `IValueConverter`/`MultiBinding`, or a computed VM bool `ShowPreviewPanelColumn =>
  IsLibraryPreviewPanelVisible && !IsDetailsTableView` is simpler and consistent with this
  codebase's preference for VM-computed booleans over XAML-side multi-bindings elsewhere in this
  file). Bind `<views:LibraryPreviewPanel Grid.Row="1" Grid.Column="2" Width="{Binding
  LibraryPreviewPanelWidth}" IsVisible="{Binding ShowPreviewPanelColumn}" />` to the same computed
  bool. Persist width changes back to `AppSettings.LibraryPreviewPanelWidth` (find how
  `LibraryDetailsColumns`-style live-persisted settings save on change and mirror it — likely a
  `PropertyChanged`/`SizeChanged` handler in code-behind calling into the VM's existing
  `SaveLibrarySettings()`-style method).
- Rename every `IsPosterGrid`/`IsPanoramaGrid`/`IsTilesView`/`IsDetailsView` binding reference in
  this file to the Step 2 names (`IsGridView`, `IsListView`, `IsDetailsTableView`, plus
  `GridCoverFit`/`ListDensity` where a template needs the sub-distinction — e.g. the Panorama vs
  Poster tile templates likely still need separate `DataTemplate`s selected by `GridCoverFit`, not
  fully unified markup; keep both templates, just re-gate their `IsVisible` conditions).

**Depends on:** Step 2 (new boolean names), Step 3 (`ShowPreviewPanelColumn` and
`LibraryPreviewPanelWidth` binding target), Step 4 (`LibraryPreviewPanel` control must exist to
reference it in markup).

**Verify:** `dotnet build` clean (0 errors, verified as an actual XAML reweave per the project's own
gotcha, not just "0 Errors" from a stale skip). On-screen: 3-pane layout renders, splitter drags,
panel hides in `DetailsTable` mode and via the manual toggle, list reclaims width both times.

---

## Step 6: Corner-badge fix, rating relocation, language removal

**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — 4 grid templates + hover-tooltip
popup), `src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit — `TryShowDogEarPeek` gate)

**What:**
- In `LibraryScreen.axaml.cs`'s `TryShowDogEarPeek` (find its current eligibility check, likely
  reading `row.DogEarEligible`), add `&& !row.IsSelected` (or the tile's own selection-state
  equivalent for series cards) to the condition before showing the peek image — this alone
  implements "Selection wins over Dog-ear," no new enum/property needed (per the design doc's
  implementation-time correction).
- In each of the 4 grid templates (`PosterGridIssueTemplate`/`PosterGridSeriesTemplate` and their
  Panorama equivalents — confirm exact template names/line numbers by re-reading this file, they
  may have shifted from the original 2026-08-27 line numbers after the in-flight cosmetic-toggles
  diff): move the rating `Border` from bottom-right to bottom-left (swap `Margin`/alignment with
  where the language `Border` currently sits), remove its `!HasSelection` condition from its
  `IsVisible` binding (no longer needed — nothing else competes for bottom-left), and delete the
  language `Border` entirely from all 4 templates.
- In the hover-tooltip popup (`LibraryScreen.axaml:1387-1415` per the survey — bound directly to
  `IssueListRow`, no separate content model), add one more bound `TextBlock`/row for
  `LanguageIso`/`HasLanguage` (already exist on `IssueListRow`, confirmed — no new field needed)
  alongside the existing Title/WriterAndPenciller/SummaryExcerpt/FileSize/Format rows.
- Update the toolbar's "Overlay" checkbox group copy (`LibraryToolbar.axaml`, the 4 rows added by
  the in-flight cosmetic-toggles work) if any label text explicitly says "bottom-right" or similar —
  check current copy first; if it's generic ("Numeric rating badge", "Dog-ear preview") no text
  change is needed, only confirm nothing describes stale corner positioning.

**Depends on:** none structurally (independent of Steps 1-5, could be done in parallel), but
sequenced after Step 5 here just to keep `LibraryScreen.axaml` edits from conflicting mid-flight in
the same session.

**Verify:** New/updated tests around `DogEarEligibility`/the peek-gate condition confirming a
selected+multi-page+eligible row does not show the peek. On-screen: rating badge renders
bottom-left independent of selection/hover state; dog-ear peek no longer appears on a selected
tile; language no longer renders on any tile; hover tooltip shows language; publisher chip
unaffected (top-left, untouched).

---

## Step 7: Color/token pass

**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — publisher/rating chip brushes,
tile hover `BoxShadow`), `src/Paperbunkr.App/Views/AccentColorToBrushConverter.cs` (edit or new
sibling converter), `src/Paperbunkr.App/Views/MainWindow.axaml` (edit — `Border.colRow`/
`Classes.active` style, lines ~358-365)

**What:**
- Replace every `Background="#B814161B"` occurrence (publisher chip across all 4 templates, now
  also the relocated rating badge) with `{DynamicResource PbBadgeBrush}` /
  `{DynamicResource PbBadgeTextBrush}` foreground.
- Tile hover style: add a `{DynamicResource PbGlowBrush}`-based `BoxShadow` on the existing
  hover-state selector (find it — likely `Border.cover:pointerover` or similar in this file's
  `<Styles>` block), replacing/supplementing the current flat border-lightening treatment.
- `AccentColorToBrushConverter` (full current implementation already read — no alpha parameter
  today): add an alpha-parameter overload — either a `ConverterParameter`-driven variant of the same
  class, or a new sibling `AccentColorToBackgroundTintConverter` returning the same parsed color at
  a fixed ~16-18% alpha (match `PbAccentSoftColor`'s `#29...`/`PbBadgeSoftColor`'s `#2E...` prefix
  convention for the alpha byte). Keep the existing converter's fallback-to-`PbAccentTextBrush`
  behavior for null/unparseable input in both.
- `MainWindow.axaml`'s `Border.colRow`/`Classes.active` style (find the actual `<Style
  Selector="Border.colRow.active">` setters, likely in this file's `<Window.Styles>` block near the
  top or in a shared style file it references): change `BorderBrush` to bind
  `{Binding AccentColor, Converter={x:Static views:AccentColorToBrushConverter.Instance}}` (full
  opacity, matches the existing `Ellipse` dot's converter usage at line 362) and add/change
  `Background` to the new alpha-tint converter on the same `AccentColor` property. Confirm the
  style's current selector applies per-row (via the `DataTemplate x:DataType="models:CollectionSummary"`
  context, line 357) so `AccentColor` resolves to that row's own value, not some ambient one.

**Depends on:** none (independent of Steps 1-6).

**Verify:** New converter unit test(s) — alpha-tint converter produces the documented alpha byte
from a sample hex input, full-opacity path unaffected. On-screen: publisher/rating chips render
gold instead of near-black; tile hover shows a soft glow; each Collection's sidebar row tints with
its own accent color at low opacity with the border at full saturation; text stays legible against
all 6 swatches.

---

## Step 8: `Ctrl+B` shortcut

**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml.cs` (edit)

**What:** In the `KeyDown`/`OnKeyDown` handler's `if`/`else if` chain (the block containing the
`Ctrl+P`/`Ctrl+Q` handlers, confirmed lines ~127-182), add:
```csharp
else if (e.Key == Key.B && e.KeyModifiers == KeyModifiers.Control)
{
    if (viewModel.CurrentScreen == "library")
    {
        viewModel.Library.ToggleLibraryPreviewPanelCommand.Execute(null);
        e.Handled = true;
    }
}
```
(guard on the Library screen specifically, matching how `Ctrl+P`'s handler guards against the
reader screens — check the exact `CurrentScreen` string/enum this project uses for "on the Library
screen" before finalizing the condition).

**Depends on:** Step 3 (`ToggleLibraryPreviewPanelCommand` must exist).

**Verify:** Manual/on-screen — Ctrl+B toggles the panel while on the Library screen, no-ops
elsewhere, doesn't conflict with Ctrl+P/Quick Open.

---

## Step 9: Scroll-position preservation across density/cover-fit toggle

**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit — reading/setting the actual
`ScrollViewer.Offset` of whichever container is currently visible)

**What:** In the `OnGridCoverFit`/`OnListDensity` `partial void` changed-handlers (added in Step 2),
capture the currently-visible view-mode container's `ScrollViewer.Offset` into the transient field
added in Step 3, before the rebuild; restore it after (likely needs a `Dispatcher.UIThread.Post`
one-tick defer, consistent with this project's own documented "don't act on a control mid-rebuild"
gotcha for other unrelated cases — confirm whether a defer is actually needed here by checking
whether the rebuild is synchronous or already dispatcher-scheduled). Explicitly **not** touching
`LibraryBrowseState`/`_browseHistory` (`LibraryScreenViewModel.cs:92`) — this field is entirely
separate.

**Depends on:** Step 2 (the changed-handlers to hook into), Step 5 (need to know which `ScrollViewer`
is currently visible in the new layout).

**Verify:** New test/on-screen check: scroll partway down a long Grid, flip `LibraryGridCoverFit`,
confirm approximate scroll position is preserved; confirm `LibraryBrowseHistory`'s own persisted
entries are unaffected by the toggle (still only content-type/collection/search).

---

## Step 10: Test suite pass

**Files:** `src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit),
`src/Paperbunkr.Data.Tests/ConsolidateLibraryViewModesMigrationTests.cs` (new, per Step 1),
new `src/Paperbunkr.App.Tests/LibraryPreviewPanelTests.cs` or equivalent VM-level tests, new
converter test file, `LibraryToolbarDriver`-based UI test updates.

**What:**
- Migration test (Step 1, already specified there).
- Dog-ear/selection precedence test: selected + eligible + hovering does not show the peek (Step 6).
- Preview panel state tests: series/issue/idle/no-results each populate the right VM-computed
  booleans and bound values; a multi-select (2+ items) leaves the last single-focused preview
  showing rather than blanking or crashing.
- Bulk-action reachability: existing selection-bar commands remain bound and enabled identically
  across `Grid`/`List`/`DetailsTable` (this should already hold with zero changes, since the
  selection bar lives inside `LibraryToolbar.axaml` which now simply spans wider — write the test
  primarily to lock in that this redesign doesn't regress it).
- Accent-tint converter test (Step 7).
- Scroll-position test + browse-history-unaffected companion test (Step 9).
- Update `LibraryToolbarDriver`-based FlaUI tests for the toolbar's new `ColumnSpan="3"` container —
  expect layout-only differences, no behavioral changes.
- Full `Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests` suites green (watch for the project's known
  full-suite-only flake pattern — run the whole suite at least once, not just the new/touched
  files, per the standing project caveat about tests that only fail under full-suite parallelism).

**Depends on:** all prior steps.

**Verify:** `dotnet test` clean on both test projects. On-screen verification per the design doc's
own Testing section (3-pane layout at a few widths including ultrawide, corner-badge states against
real cover art, preview panel for a real series/issue, no-results state, sidebar accent tinting
across at least 2 differently-colored Collections, bulk actions in `DetailsTable` mode) — flagged
explicitly since this project has no unattended GUI automation for final visual confirmation.
