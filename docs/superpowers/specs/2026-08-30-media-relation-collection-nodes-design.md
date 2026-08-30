# MediaRelation edges with Collections as nodes

*Date: 2026-08-30. Closes the last of the three items from `docs/superpowers/specs/2026-08-27-
collections-design.md`'s deferred list — the other two (`RecommendationReason.SameCollection` +
`RecommendationResolver` wiring, and the Home-feed collections shelf) were found already shipped
during the audit that led to this spec; that doc's Deferred list has been corrected accordingly.*

## Problem

`MediaRelation` is the app's typed pairwise fictional-universe graph (Prequel/Sequel/Crossover/
SameUniverse/Contains/Adaptation/etc.) — but it's strictly `Series ↔ Series` today
(non-nullable `SourceSeriesId`/`TargetSeriesId`). There's no way to express a fact like "this
omnibus Collection is a Crossover tying into this standalone Series" or "this Collection is the
CollectedFrom compilation of this Series" — the collecting/compilation use case this spec targets.

`CollectionRelation` already exists as a separate, parallel entity for Collection↔Collection links
(e.g. "these two collections are the same fictional universe") — it does not, and should not, need
to change. The gap is specifically a **mixed** edge: a Collection on one side, a Series on the
other.

## Scope

**In scope:**
1. `MediaRelation`'s FK columns become nullable, with two new nullable Collection FK columns added
   (one exactly-one-per-side pair each) — a Collection or a Series on either end, any combination
   except Collection↔Collection (rejected — see below).
2. `MediaRelationResolver` gains mixed-kind read/write entry points rooted at either a Series or a
   Collection.
3. `IMetadataGraph` (the plugin-facing contract) gains additive overloads so plugins can traverse
   the relation graph from either a `Series` or a `Collection`.
4. UI: the Series Detail "Related Series" rail (now just "Related") can show and add
   Collection-sided edges; the Collection editor gets a new parallel "Related" section.

**Explicitly out of scope:**
- Any change to `CollectionRelation` (Collection↔Collection) — untouched, and this spec's
  `TryCreate` guard actively keeps that combination out of `MediaRelation`'s territory so there's
  never two inconsistent ways to link two collections.
- Exposing `CollectionRelation` through `IMetadataGraph` — a separate concern from this spec, which
  is only about `MediaRelation`.
- Any change to `RecommendationResolver`'s existing `CollectionScore` signal — that's
  `CollectionResolver.GetOtherSeriesSharingCollection` (membership-derived: "these series share a
  collection"), a completely different, already-shipped mechanism from the typed graph edge this
  spec adds. Naming collision only — flagged explicitly so a future reader doesn't conflate them.

## Data model

### `MediaRelation` (extended)

```
MediaRelation
    Id
    SourceSeriesId?        // CHANGED: now nullable
    SourceSeries?
    SourceCollectionId?    // NEW, nullable
    SourceCollection?      // NEW
    TargetSeriesId?        // CHANGED: now nullable
    TargetSeries?
    TargetCollectionId?    // NEW, nullable
    TargetCollection?      // NEW
    RelationType
    Notes?
    CreatedAt
    Evidence (List<RelationEvidence>)   // unchanged - already relation-agnostic (Provider/
                                         // ProviderRelationType/ProviderSourceId, no entity-
                                         // specific fields), needs no changes.
```

Two `CHECK` constraints, mirroring `CollectionItem`'s existing exactly-one-target pattern rather
than introducing a new discriminator-enum shape:
```sql
CHECK ((SourceSeriesId IS NOT NULL) + (SourceCollectionId IS NOT NULL) = 1)
CHECK ((TargetSeriesId IS NOT NULL) + (TargetCollectionId IS NOT NULL) = 1)
```

Every existing row already has both `SourceSeriesId`/`TargetSeriesId` set — the migration
(`AddMediaRelationCollectionNodes`) just relaxes those two columns to nullable and adds the two new
nullable Collection FK columns (`ON DELETE CASCADE`, matching the existing Series FKs' cascade
choice — "no interactive delete path in this codebase should be blocked by a relation existing",
same rationale already on record for `MediaRelation`'s and `CollectionRelation`'s config). Zero data
loss, zero behavior change for every pre-existing row.

**Collection↔Collection is rejected**, enforced in `MediaRelationResolver.TryCreate` (not the DB —
the CHECK constraints alone can't express "not both Collection"): a caught, logged no-op, same
posture as every other guard in this codebase. `CollectionRelation` is the only path for linking
two collections.

## Resolver & service layer

`MediaRelationResolver.GetRelatedSeries(context, seriesId)` (today: `Series`-only results) is
replaced by two entry points returning a mixed-kind result:

```csharp
public enum MediaRelationEndpointKind { Series, Collection }

public sealed record MediaRelationEndpoint(
    MediaRelationEndpointKind Kind,
    Series? Series,
    Collection? Collection,
    RelationType DisplayType,
    int MediaRelationId);

GetRelatedFromSeries(context, seriesId)     -> IReadOnlyList<MediaRelationEndpoint>
GetRelatedFromCollection(context, collectionId) -> IReadOnlyList<MediaRelationEndpoint>
```

mirroring `CollectionResolver.CollectionMember`'s existing discriminated-result shape rather than
inventing a new pattern. Both keep the existing directional-inversion logic (`RelationTypeCatalog`'s
`Symmetric`/`Directional`/`InverseType`) byte-for-byte — only which table backs "the other side"
changes.

`TryCreate` gains an overload set: `(sourceKind, sourceId, targetKind, targetId, relationType)`,
covering Series↔Series (existing behavior, unchanged), Series↔Collection, and Collection↔Series —
rejecting Collection↔Collection as above. Self-relation and exact-duplicate-triple rejection
(existing behavior) applies across all allowed combinations.

## `IMetadataGraph` (plugin-facing)

Collections become a first-class plugin-visible type for the relation graph, via additive overloads
only — nothing existing changes signature or behavior, so no plugin using today's contract breaks:

```csharp
// Existing, unchanged:
IReadOnlyList<MediaRelation> GetRelations(Series series);
IReadOnlyList<Series> GetRelatedSeries(Series series);

// New:
IReadOnlyList<Collection> GetRelatedCollections(Series series);
IReadOnlyList<MediaRelation> GetRelations(Collection collection);
IReadOnlyList<Series> GetRelatedSeries(Collection collection);
```

No `GetRelatedCollections(Collection collection)` — a Collection's own `MediaRelation` edges can
only point to a Series (Collection↔Collection is rejected), so that method would always return
empty; not worth adding. `PaperbunkrMetadataGraph` (the concrete adapter) implements each new
overload by delegating to `MediaRelationResolver.GetRelatedFromSeries`/`GetRelatedFromCollection`
and filtering to the requested kind.

## UI

### Series Detail — "Related Series" rail becomes "Related"

`RelatedSeriesSample` (feeds `Related`/`RelatedRail`) is extended rather than replaced:
```
RelatedSeriesSample
    Title, Name, Note, CoverBrush   // unchanged
    RelatedSeriesId?                 // CHANGED: now nullable
    RelatedCollectionId?             // NEW, nullable
    Kind (MediaRelationEndpointKind) // NEW
    MediaRelationId                  // unchanged
```
`RefreshRelated` switches on `MediaRelationEndpoint.Kind` when building each sample (a Collection
endpoint's title/cover come from `Collection.Name`/`CollectionResolver.GetCoverHint`, matching how
`HomeCollectionCard.FromCollection` already resolves a collection's cover). `PosterRailItem` itself
needs no changes — already generic, `Payload` already carries the originating sample.

`OpenRelatedSeries(object? payload)`'s `RelatedSeriesSample` case branches on `Kind`: `Series` →
existing `_navigateToSeries(id)`; `Collection` → a new `_navigateToCollection(id)` callback into
`MainViewModel`, mirroring `HomeScreenViewModel`'s existing `OpenCollection`/`_goLibraryWithCollection`
pattern (select the collection and switch to Library).

The add-flow (`ToggleAddRelationCommand` → search → `AddRelationCommand`) becomes a mixed search:
the existing series-name search extends to also match collection names, each result tagged by kind
(reusing `CollectionSearchResult`'s existing shape as the collection-side half of the result list,
alongside the current series-side search results) so the picker UI can show "Series" or "Collection"
next to each match. Section header text changes from "Related Series" to "Related".

### Collection editor — new "Related" section

`CollectionPropertiesOverlay` gains a new section, structurally parallel to its existing "Related
Collections" section (which stays untouched, still backed by `CollectionRelation`): search box +
relation-type picker + result chips, backed by `MediaRelationResolver.GetRelatedFromCollection`/
`TryCreate`/`Remove`. Search here can match Series only, not other Collections — a Collection↔
Collection match found through *this* search box would just be rejected by `TryCreate` per the
guard above, so scoping the search to Series-only up front avoids a confusing "found it, but can't
add it" dead end.

## Error handling

- Exactly-one-per-side is enforced by the DB `CHECK` constraints (backstop) and guarded in
  `MediaRelationResolver.TryCreate` before insert (same two-layer posture as `CollectionItem`).
- Collection↔Collection is an app-side guard only (the CHECK constraints can't express it) — a
  caught, logged no-op.
- Deleting a `Series` or `Collection` cascades its `MediaRelation` rows (and their `Evidence`) via
  the existing `OnDelete(DeleteBehavior.Cascade)` posture, extended to the two new FK columns.

## Testing

- **`MediaRelationResolverTests`** (extend existing or new) — `GetRelatedFromSeries`/
  `GetRelatedFromCollection` mixed-kind results; `TryCreate` for Series↔Collection and
  Collection↔Series; Collection↔Collection rejection; self-relation and duplicate-triple rejection
  still hold across the new combinations; cascade-on-delete for both new FK columns.
- **`PaperbunkrMetadataGraphTests`** (extend existing or new) — the 3 new `IMetadataGraph` overloads
  return the expected mixed results; existing `GetRelations(Series)`/`GetRelatedSeries(Series)`
  tests still pass unchanged (regression check that the additive change didn't alter old behavior).
- **`DetailTabsViewModelTests`** — `RefreshRelated` builds a `RelatedSeriesSample`/`PosterRailItem`
  correctly for a Collection-sided edge; `OpenRelatedSeriesCommand` routes to the collection
  navigation callback for a Collection payload; the mixed add-flow finds both kinds and only
  persists an allowed combination.
- **`CollectionPropertiesScreenViewModelTests`** — new "Related" section's add/remove against
  `MediaRelationResolver`, Series-only search scoping.
- **Migration round-trip test** — nullable-column relaxation + two new columns preserve every
  existing row's data and behavior.

## Roadmap

Update `docs/superpowers/specs/2026-08-27-collections-design.md`'s Deferred list (mark this item
done) and `docs/alpha-roadmap.md`'s Beta backlog once landed, matching the pattern the smart
collections spec used.
