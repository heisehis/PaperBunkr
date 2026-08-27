# Library Multi-Selection, Slice 3: Series Selection + Bulk Series Editor

**Date:** 2026-08-24
**Status:** Approved, pending implementation

## Context

Final slice of the Library multi-selection feature (Slices 1-2:
docs/superpowers/specs/2026-08-24-library-multiselect-slice1-design.md /-slice2-design.md). This
slice covers series-card selection plus series-level bulk edit/delete - the two actions explicitly
deferred at the end of Slice 1 because `SeriesCardSample` is a separate model from `IssueListRow`
and there was no existing series-level bulk-edit screen to wire into.

**Research findings, per this project's standing CE-verification rule:** CE has no series-level
properties editor at all, single or bulk - grepped `_reference/ComicRackCE` for
`SeriesProperties`/`SeriesInfo`/`EditSeries`, found nothing. `Series.cs`'s own doc comment confirms
why: "CE has no equivalent: its `ComicBook`/`ComicInfo` only carries a flat `Series` string field."
There's also no existing *single*-series properties screen in Paperbunkr today - only four
standalone per-value context-menu commands (`SetSeriesContentType`/`SetSeriesStatus`/
`SetSeriesReadingStatus`/`SetSeriesReadingMode`, `LibraryScreenViewModel.cs:1264+`). This slice is
building genuinely new territory with no precedent to verify against or extend - confirmed by
research, not assumed.

**User's explicit choices** (asked directly, given the above): build a real bulk editor screen
mirroring the issue-level one's architecture (not just extending the four existing per-value
commands), and include `Genre`/`Publisher` in it despite `Series.cs` marking those columns
stale/non-authoritative (Issue-level `Genre`/`Publisher` is the real display source - see below for
how the editor surfaces that caveat rather than silently hiding it).

## Architecture: parallel to the issue-level bulk editor, not shared with it

`BulkFieldDescriptor`/`BulkFieldViewModel` ([Models/BulkFieldDescriptor.cs](../../../src/Paperbunkr.App/Models/BulkFieldDescriptor.cs),
[ViewModels/BulkFieldViewModel.cs](../../../src/Paperbunkr.App/ViewModels/BulkFieldViewModel.cs)) are
concretely typed to `Issue` (`Func<Issue, string?>`), and `BulkFieldViewModel` carries Issue-only
concepts (rating stars, `{Token}` template-insert wired to `TemplateTokenCatalog.Expand(value,
issue)`) that Series has no equivalent of. Generalizing them to a shared `<TEntity>` base would touch
already-shipped, already-tested issue-editing code for a series-level feature - not worth the risk.
Instead: a parallel, smaller pair -

- `SeriesBulkFieldDescriptor` (new `Models/SeriesBulkFieldDescriptor.cs`) - same shape as
  `BulkFieldDescriptor` but `Func<Series, string?>`/`Action<Series, string?>`, reusing the existing
  `FieldKind` enum (already public in `BulkFieldDescriptor.cs`, no reason to duplicate it).
- `SeriesBulkFieldViewModel` (new `ViewModels/SeriesBulkFieldViewModel.cs`) - same `Value`/`IsStaged`
  auto-stage-on-edit shape as `BulkFieldViewModel`, but only `Text`/`Enum` kinds (no Rating, no
  token-insert) - Series has no field needing either.
- `SeriesBulkFieldRegistry.All` (inside `SeriesBulkFieldDescriptor.cs`, mirroring
  `BulkFieldRegistry`'s own co-location): `Name`, `SortName`, `Publisher`, `Genre`, `Summary` (Text),
  `Content Type`/`Status`/`Reading Status`/`Reading Mode` (Enum, matching the four existing
  per-value commands' options exactly - `Enum.GetNames<ContentType/SeriesStatus/ReadingStatus/ReadingMode>()`).
  `Publisher`/`Genre` rows carry a visible caveat label in the editor ("not shown elsewhere - Issue-
  level Publisher/Genre is what Library/Detail actually display") rather than pretending they're
  equivalent to the Issue-level fields of the same name.

## `BulkSeriesPropertiesScreenViewModel`

New (`ViewModels/BulkSeriesPropertiesScreenViewModel.cs`), same `Load(IReadOnlyList<int> seriesIds)`/
`Save`/`Cancel`/`HasUnsavedChanges()` shape as `BulkIssuePropertiesScreenViewModel`: per-field
intersection-populate at Load (identical value across every selected series → show it, differing →
blank), per-field staging, write-only-staged-fields on Save, one `SaveChanges()`. No Undo/Redo
integration this pass - `MetadataEditHistoryService` is Issue-snapshot-shaped
(`CaptureSnapshot(Issue)`); extending it to Series is real additional scope not requested and not
needed for this slice to be useful (Cancel before Save already means "no database change happened,"
the same safety net the field-level staging already provides).

## Series selection mechanism

`SeriesCardSample` ([Models/SeriesCardSample.cs](../../../src/Paperbunkr.App/Models/SeriesCardSample.cs))
gets the identical treatment `IssueListRow` got in Slice 1: `sealed class` → `sealed partial class :
ObservableObject`, add `[ObservableProperty] private bool _isSelected`, implement `ISelectableCard`
(already exists from Slice 1, no changes needed to it or to `TileSelectionController<TCard>` itself -
it's already generic). `LibraryScreenViewModel` gets a second, independent controller:
`SeriesSelection { get; } = new TileSelectionController<SeriesCardSample>();`, alongside the existing
issue-granularity `Selection` - the two are never active at once in practice (series-card templates
only render when `IsSeriesGranularity`), but keeping them as separate properties avoids any
cross-granularity id confusion (`IssueListRow.Id` and `SeriesCardSample.SeriesId` are different id
spaces entirely).

Same interaction model as Slice 1: hover-revealed checkbox (stays visible for every card once any
card is selected), ctrl-click/shift-click on the card body, plain click still navigates
(`GoToSeries`), right-click union unchanged for existing single-series context-menu items. Applied to
all seven series-card `DataTemplate`s in `LibraryScreen.axaml` (Compact/Comfortable/CoverOnly/
Panorama/List/Details/Tiles - `SeriesCompactGridItemTemplate` etc., currently completely untouched by
Slices 1-2 per research).

## Series action bar

A second selection action bar, shown when `IsSeriesGranularity && SeriesSelection.Count > 0`
(mutually exclusive with the issue-granularity bar from Slices 1-2, matching the templates'
existing `IsSeriesGranularity` gating): "N series selected", **Bulk Edit**, **Delete**, **Clear**. No
mark-read/unread or add-to-reading-list here - those are issue-level concepts with no obvious
series-level equivalent, and weren't asked for at series granularity.

- **Bulk Edit**: `LibraryScreenViewModel` gains a `goBulkSeriesProperties: Action<IReadOnlyList<int>>?`
  constructor parameter (mirroring `goBulkIssueProperties`'s exact shape), wired through
  `MainViewModel` to the new `BulkSeriesPropertiesScreenViewModel`, dispatching by selection count
  exactly like the issue-level editor does (though unlike issues, there's no existing single-series
  editor to dispatch to at count == 1 - a lone-series "bulk" edit just opens the same screen with one
  series, which is a perfectly coherent editor for one item, same as how a "bulk" edit of 1 issue
  would look on the issue side if it were ever reached that way).
- **Delete**: `DeleteSeries(int seriesId)` ([LibraryScreenViewModel.cs:1229](../../../src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs))
  refactored the same way Slice 1 refactored `DeleteIssue` - a shared `DeleteSeriesList(IReadOnlyList<int>
  seriesIds)` looped from both the existing single-tile context-menu command and a new
  `DeleteSeriesSelectionCommand`, reusing `LibraryDeletionHelper.RemoveSeries` per series, unchanged.

## Overlay wiring (mirrors `BulkIssueProperties` exactly)

`MainViewModel`: `BulkSeriesProperties` property (constructed alongside `BulkIssueProperties`),
`IsBulkSeriesPropertiesOverlayOpen` bool + `IsBulkSeriesProperties` alias, `GoBulkSeriesPropertiesForSeries`/
`CloseBulkSeriesPropertiesOverlayAndReload`/`CloseBulkSeriesPropertiesOverlay`, folded into the
existing `HasUnsavedChanges`/`Escape` branches the same way `IsBulkIssueProperties` already is.
`MainWindow.axaml` gets a second borderless-overlay `Border` block, identical structure to the
existing Bulk Issue Editing one (docs/superpowers/specs/2026-08-23-issue-editor-borderless-overlay-
design.md), hosting a new `BulkSeriesPropertiesScreen.axaml` view.

## Explicitly not changing

- The issue-level bulk editor (`BulkFieldDescriptor`/`BulkFieldViewModel`/
  `BulkIssuePropertiesScreenViewModel`) - untouched, no shared base type introduced.
- `MetadataEditHistoryService`/Undo-Redo - not extended to series edits this pass.
- The four existing single-series per-value commands (`SetSeriesContentType` etc.) - stay as they
  are, still reachable from wherever they're used today; the new bulk editor is an additional path,
  not a replacement.

## Testing

- `SeriesBulkFieldRegistry`/`SeriesBulkFieldViewModel`: field population/staging, same shape as the
  issue-level equivalents' existing coverage.
- `BulkSeriesPropertiesScreenViewModelTests`: Load population (same-value vs differing-value
  intersection), Save writing only staged fields, Cancel leaving the database untouched.
- `LibraryScreenViewModelTests`: series selection gestures (toggle/shift-range/ctrl-additive) via
  `SeriesSelection`, bulk delete acting on multiple series, bulk-edit dispatch wiring.
