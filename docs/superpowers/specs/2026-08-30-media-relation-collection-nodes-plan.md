# MediaRelation Collection Nodes — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-design.md*

Survey notes (exact current shapes, confirmed by reading the files):
- `MediaRelationResolver.GetRelatedSeries(context, seriesId)` returns
  `IReadOnlyList<(Series OtherSeries, RelationType DisplayType, int MediaRelationId)>`. `TryCreate`
  is `(context, sourceSeriesId, targetSeriesId, relationType) -> bool`. Both are called from:
  `PaperbunkrMetadataGraph` (`GetRelatedSeries`/nothing for TryCreate), `DetailTabsViewModel`
  (`RefreshRelated`/`AddRelation`/`RemoveRelation`), and `MediaRelationResolverTests.cs` (10 tests,
  all against the current tuple shape and 4-arg `TryCreate`).
- `RelatedSeriesSample` (`src/Paperbunkr.App/Models/RelatedSeriesSample.cs`) has `required int
  RelatedSeriesId` — becomes nullable, plus new `Kind`/`RelatedCollectionId` fields.
- `SeriesSearchResult` (`src/Paperbunkr.App/Models/SeriesSearchResult.cs`) implements
  `ISelectableCard` and is shared with Continuity's "add series" flow — **not** touched; a new
  `RelationSearchResult` model is added instead for the Related tab's mixed search, so nothing else
  that consumes `SeriesSearchResult` is affected.
- `CollectionPropertiesScreenViewModel`'s existing "Related Collections" section (lines ~199-309,
  already read this session) is the template to mirror for the new Series-only "Related" section:
  `RelatedCollections`/`IsAddingRelation`/`RelationSearchQuery`/`SelectedRelationTypeOption`/
  `RelationSearchResults`/`ToggleAddRelationCommand`/`SearchRelationCandidates`/`AddRelationCommand`/
  `RemoveRelationCommand`/`RefreshRelatedCollections` — same names already exist there for the
  Collection↔Collection flow, so the new Series-relation section needs distinctly-named
  counterparts (`RelatedSeries`/`IsAddingSeriesRelation`/etc.) to avoid colliding.
- `PaperbunkrMetadataGraph` (`src/Paperbunkr.App/Plugins/PaperbunkrMetadataGraph.cs`) is tested in
  `src/Paperbunkr.App.Tests/PluginApiV3Tests.cs` (not a separate file) — one existing test at
  line ~71-95 exercises `GetRelations(seriesA)`/`GetRelatedSeries(seriesA)`.
- Latest migration: `20260829235157_AddSmartCollections`. New one goes after it.

## Step 1: Entity + migration
**Files:**
- `src/Paperbunkr.Data/Entities/MediaRelation.cs` (edit) — `SourceSeriesId`/`TargetSeriesId` become
  `int?`; add `SourceCollectionId`/`SourceCollection`/`TargetCollectionId`/`TargetCollection` (all
  nullable).
- `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit, `MediaRelation` config block ~line 522) — add
  the two new `HasOne(...).WithMany().OnDelete(DeleteBehavior.Cascade)` blocks for
  `SourceCollection`/`TargetCollection`; add the two `CHECK` constraints via `builder.ToTable(t =>
  t.HasCheckConstraint(...))`, mirroring `CollectionItem`'s existing pattern (~line 291-295).
- New migration `AddMediaRelationCollectionNodes` via `dotnet ef migrations add
  MediaRelationCollectionNodes --project src/Paperbunkr.Data --startup-project src/Paperbunkr.Data`.
  **Watch for the same enum/nullable-default pitfall hit in the Smart Collections session** — verify
  the generated migration doesn't need a `defaultValue` for the two relaxed-to-nullable columns
  (relaxing `NOT NULL` to nullable never needs one; only new `NOT NULL` columns do) and that the
  `AlterColumn` calls correctly preserve existing data.

**Depends on:** none
**Verify:** `dotnet build` on `Paperbunkr.Data`; migration applies cleanly to a scratch DB (existing
rows keep their Series FKs untouched).

## Step 2: `MediaRelationResolver` — mixed-kind resolver
**Files:** `src/Paperbunkr.Data/Metadata/MediaRelationResolver.cs` (edit)
**What:**
- New `MediaRelationEndpointKind` enum (`src/Paperbunkr.Data/Entities/MediaRelationEndpointKind.cs`,
  new file) and `MediaRelationEndpoint` record (`Kind, Series?, Collection?, RelationType
  DisplayType, int MediaRelationId`) — put the record in `MediaRelationResolver.cs` itself,
  matching how `CollectionResolver.cs` keeps its own `CollectionMember`/`CollectionMemberKind` in
  the same file rather than a separate one.
- Rename `GetRelatedSeries(context, seriesId)` → `GetRelatedFromSeries(context, seriesId)`,
  returning `IReadOnlyList<MediaRelationEndpoint>` — for each relation touching `seriesId`, resolve
  the *other* side (Series or Collection) with the same source/target directional-inversion logic
  as today, just branching on which FK pair is populated.
- New `GetRelatedFromCollection(context, collectionId)` — same shape, rooted at a Collection (a
  Collection's own relations can only have a Series on the other side, per the Collection↔Collection
  rejection, but the query still walks both `SourceCollectionId`/`TargetCollectionId` for
  directionality).
- `TryCreate` gains a general overload: `TryCreate(context, MediaRelationEndpointKind sourceKind,
  int sourceId, MediaRelationEndpointKind targetKind, int targetId, RelationType relationType) ->
  bool`. Rejects `sourceKind == Collection && targetKind == Collection` up front. Self-relation
  check compares `(sourceKind, sourceId) == (targetKind, targetId)`. Duplicate check matches the
  existing triple logic but keyed on the 4-tuple (sourceKind, sourceId, targetKind, targetId) in
  either direction. The **existing 4-arg `TryCreate(context, sourceSeriesId, targetSeriesId,
  relationType)` stays as a convenience overload** that calls the general one with
  `Kind.Series`/`Kind.Series` — so `MediaRelationResolverTests.cs`'s existing Series↔Series tests
  keep compiling and passing unchanged.

**Depends on:** Step 1
**Verify:** existing `MediaRelationResolverTests.cs` updated for the `GetRelatedSeries` rename
(→ `GetRelatedFromSeries`, tuple destructuring → `entry.Series!.Id`/`entry.DisplayType`) plus new
cases (Step 6).

## Step 3: `IMetadataGraph` + `PaperbunkrMetadataGraph`
**Files:**
- `src/Paperbunkr.Plugins/Automation/IMetadataGraph.cs` (edit) — add the 3 new overloads:
  `GetRelatedCollections(Series series)`, `GetRelations(Collection collection)`,
  `GetRelatedSeries(Collection collection)`. Existing `GetRelations(Series)`/`GetRelatedSeries(Series)`
  unchanged.
- `src/Paperbunkr.App/Plugins/PaperbunkrMetadataGraph.cs` (edit) —
  `GetRelatedSeries(Series series)` now filters `GetRelatedFromSeries` to `Kind == Series` (same
  observable behavior as before, since the rename in Step 2 changed the underlying call).
  `GetRelatedCollections(Series series)` filters to `Kind == Collection`.
  `GetRelations(Collection collection)` queries `context.MediaRelations` where either Collection FK
  matches (mirrors the existing `GetRelations(Series)` shape).
  `GetRelatedSeries(Collection collection)` delegates to `MediaRelationResolver.GetRelatedFromCollection`
  filtered to `Kind == Series`.

**Depends on:** Step 2
**Verify:** existing `PluginApiV3Tests.cs` test at line ~71-95 (`GetRelations`/`GetRelatedSeries` on
a Series) still passes; new cases (Step 6) for the 3 new overloads.

## Step 4: Series Detail — "Related" rail (mixed)
**Files:**
- `src/Paperbunkr.App/Models/RelatedSeriesSample.cs` (edit) — `RelatedSeriesId` becomes `int?`; add
  `RelatedCollectionId` (`int?`) and `Kind` (`MediaRelationEndpointKind`).
- `src/Paperbunkr.App/Models/RelationSearchResult.cs` (new) — `{ Kind, SeriesId?, CollectionId?,
  Name }` for the mixed search-results list (deliberately separate from `SeriesSearchResult`, which
  stays Series-only for its other consumers — see survey note above).
- `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit):
  - `RefreshRelated` (~line 232) calls `MediaRelationResolver.GetRelatedFromSeries`, branches on
    `endpoint.Kind` per entry (Collection endpoint: title/cover via `Collection.Name`/
    `CollectionResolver.GetCoverHint`, mirroring `HomeCollectionCard.FromCollection`'s resolution).
  - `SearchRelationCandidates` (~line 301) searches both `context.Series` (existing) and
    `context.Collections` (new), populating a `RelationSearchResult` list instead of
    `SeriesSearchResult`.
  - `AddRelation` (~line 323) takes `RelationSearchResult?`, calls the new general
    `MediaRelationResolver.TryCreate` overload with `Kind.Series` for the current series and the
    target's own `Kind`.
  - `OpenRelatedSeries(object? payload)` (~line 1373) gains a `RelatedSeriesSample` branch on
    `Kind`: `Series` → existing `_navigateToSeries`; `Collection` → new `_navigateToCollection`
    callback (new constructor parameter, mirroring `HomeScreenViewModel`'s `goLibraryWithCollection`
    pattern — wired from `MainViewModel` the same way `GoDetailForSeries`/`GoBookDetailForBook` are
    today).
- `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit) — thread a
  `Action<int> navigateToCollection` into `DetailTabsViewModel`'s constructor, delegating to
  `Library.SelectCollectionByIdCommand`-equivalent nav (check `LibraryScreenViewModel.SelectCollectionById`
  from the Smart Collections session — reuse that exact entry point) plus switching the active
  screen to Library, mirroring how `OpenCollection` in `HomeScreenViewModel` does it.
- `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit) — rename the "Related Series" section header to
  "Related"; the add-flow's result-list `DataTemplate` switches from `x:DataType="models:SeriesSearchResult"`
  to `models:RelationSearchResult` and shows a small kind label/icon per row so a Series and a
  Collection result are visually distinguishable.

**Depends on:** Step 2
**Verify:** `DetailTabsViewModelTests` updated/extended (Step 6).

## Step 5: Collection editor — new "Related" (Series) section
**Files:**
- `src/Paperbunkr.App/ViewModels/CollectionPropertiesScreenViewModel.cs` (edit) — new section
  parallel to the existing "Related Collections" one (~line 199-309), distinctly named to avoid
  colliding with it: `RelatedSeriesChips` (`ObservableCollection<RelatedSeriesChip>`, new model
  mirroring `RelatedCollectionChip`'s shape), `IsAddingSeriesRelation`, `SeriesRelationSearchQuery`,
  `SelectedSeriesRelationTypeOption`, `SeriesRelationSearchResults`
  (`ObservableCollection<SeriesSearchResult>` — Series-only search here, per the design doc's
  "scoping the search to Series-only up front avoids a confusing dead end" call), backed by
  `MediaRelationResolver.GetRelatedFromCollection`/`TryCreate`/`Remove`.
- `src/Paperbunkr.App/Models/RelatedSeriesChip.cs` (new) — mirrors `RelatedCollectionChip.cs`'s
  exact shape (`MediaRelationId, SeriesId, Name, RelationTypeLabel`).
- `src/Paperbunkr.App/Views/CollectionPropertiesOverlay.axaml` (edit) — new "Related" `groupBox`
  section, structurally identical to the existing "Related Collections" one just above/below it,
  bound to the new Series-relation properties/commands.

**Depends on:** Step 2
**Verify:** `CollectionPropertiesScreenViewModelTests` extended (Step 6).

## Step 6: Tests
**Files:**
- `src/Paperbunkr.Data.Tests/MediaRelationResolverTests.cs` (edit) — update all 10 existing tests
  for the `GetRelatedFromSeries` rename + `MediaRelationEndpoint` shape (`entry.Series!.Id` instead
  of `entry.OtherSeries.Id`); add new cases: `GetRelatedFromSeries`/`GetRelatedFromCollection`
  returning a Collection-kind endpoint; `TryCreate`'s new overload for Series↔Collection and
  Collection↔Series; Collection↔Collection rejection; self-relation/duplicate rejection still hold
  across the new combinations; cascade-on-delete for both new FK columns.
- `src/Paperbunkr.App.Tests/PluginApiV3Tests.cs` (edit) — new cases for
  `GetRelatedCollections(Series)`, `GetRelations(Collection)`, `GetRelatedSeries(Collection)`;
  confirm the existing Series-rooted test (~line 71-95) still passes unchanged.
- `src/Paperbunkr.App.Tests/DetailTabsViewModelTests.cs` (edit, if it exists — confirm during
  implementation) — `RefreshRelated` builds a correct sample/rail item for a Collection-sided edge;
  `OpenRelatedSeriesCommand` routes to the collection-navigation callback for a Collection payload;
  mixed search finds both kinds.
- `src/Paperbunkr.App.Tests/CollectionPropertiesScreenViewModelTests.cs` (edit) — new "Related"
  (Series) section's add/remove; confirms it doesn't interfere with the existing "Related
  Collections" section's own tests.
- Migration round-trip test (wherever the pattern from `AddSmartCollections`/`AddCollections` lives)
  — new nullable columns + CHECK constraints preserve every existing row.

**Depends on:** all prior steps
**Verify:** full solution build; full test suite (`Paperbunkr.Data.Tests`, `Paperbunkr.App.Tests`,
`Paperbunkr.Plugins.Tests`) green; app smoke-launched via a properly-detached process (per this
session's own earlier finding: launching a long-lived GUI process from a backgrounded bash job gets
killed by shell teardown, not a real crash — use `PowerShell Start-Process` instead).

## Roadmap
Update `docs/superpowers/specs/2026-08-27-collections-design.md`'s Deferred list (mark this item
done) and add a `docs/Paperbunkr-Roadmap.md` Beta-backlog entry, matching the pattern the smart
collections and prior sessions used.
