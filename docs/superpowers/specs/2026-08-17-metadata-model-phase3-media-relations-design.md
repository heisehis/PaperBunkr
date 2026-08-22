# Metadata Model — Phase 3: Media Relations

**Date:** 2026-08-17
**Status:** Approved, pending implementation plan
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md` (79-section architectural spec),
§25-28 ("Media Relations", "Relation Types", "Relation Direction", "Relation Provenance") and
§46/§65 (homepage/Detail-page consumers). Phase 1 (Canonical Metadata) and Phase 2a/2b/2c
(Proposals/Series Reassignment/Library Field Descriptors) are all shipped.

## Context

Verified against real code and CE source before designing this (standing project rule):

- CE has **no** related-comics concept at all - confirmed by search, not assumed. The source doc's
  own compatibility matrix already says as much ("Related Comics: New `MediaRelation` system").
  This phase is a from-scratch feature, not a port.
- The Detail screen's "Related" tab already exists as scaffolded-but-dead UI:
  `DetailTabsViewModel.Related` (an always-empty `ObservableCollection<RelatedSeriesSample>`) and
  its AXAML carousel, with an explicit comment - *"no relatedness data/schema exists yet... ready
  the day real related-series data exists."* `RelatedSeriesSample` (`Title`/`Name`/`Note`/
  `CoverBrush`) is Series-shaped, not Issue-shaped. This settles Media = **Series**, confirmed
  directly against the code rather than inferred from the source doc's ambiguous "Media" wording.
- No Series Properties editor exists in this codebase (only Issue Properties/Bulk Issue
  Properties) - the Related tab itself is the only sensible existing home for a relation-creation
  action.

**Scope, discussed and confirmed**: full relation-type vocabulary (~20 types) with real
direction-inversion, `RelationEvidence` built now even though only one source (`User`) exists this
phase, and a manual creation UI - without the last one, this phase would ship nothing visible at
all, since (unlike every prior phase) there's no automatic population path yet (no scanner
signal, no migration backfill, no external provider until Phase 5).

## Scope

### Entities

```csharp
MediaRelation
--------------
Id
SourceSeriesId (FK Series)
TargetSeriesId (FK Series)
RelationType (enum)
Notes (string?)
CreatedAt (DateTime)

RelationEvidence
----------------
Id
MediaRelationId (FK MediaRelation)
Provider (enum: User | Other)
ProviderRelationType (string?)
ProviderSourceId (string?)
Confidence (decimal)
RetrievedAt (DateTime)
```

`RelationEvidenceProvider` is deliberately scoped to `User | Other` this phase - same pattern as
2a's `MetadataProposalSource` (built with room for future members, only the real one populated
now). A user-created relation gets exactly one `RelationEvidence` row: `Provider = User,
Confidence = 1.0m` (a manual assertion is maximally confident by definition), `RetrievedAt = now`.
Real multi-provider reconciliation is Phase 5's problem once external providers exist to disagree
with each other.

**Validation**: `SourceSeriesId != TargetSeriesId` (no self-relations); no exact duplicate
(same `SourceSeriesId`/`TargetSeriesId`/`RelationType` triple) - enforced at creation time, not a
DB constraint (matches this codebase's existing validation style, e.g. `ReadingListMatcher`).

**Found during implementation, not anticipated in this design's first pass**: `SourceSeriesId`/
`TargetSeriesId` use `DeleteBehavior.Cascade`, not `Restrict` (this codebase's usual default for a
required FK, e.g. `ReadingListItem.Issue`). Every existing Series-deletion path
(`SeriesReassignmentResolver.Apply`, `NeedsReviewViewModel.MergeSeriesInto`, both from Phase 2b) is
automatic empty-series cleanup with no interactive moment to check for relations first - `Restrict`
would turn those into a real runtime FK-violation regression the first time a relation exists on a
series that later empties out. `Cascade` just removes a relation that's lost one of its two
endpoints, the only sane outcome given how series actually get deleted in this app today.

### Relation type vocabulary and direction

Full vocabulary from the source doc's §25.2:

```
Narrative:    Prequel, Sequel, SideStory, SpinOff, AlternateStory, AlternateVersion, ParallelStory
Universe:     SameUniverse, SharedUniverse, SameContinuity, Crossover, SharedCharacters
Production:   Adaptation, SourceMaterial, Remake, Compilation, Contains, CollectedFrom
Publication:  DifferentEdition, Variant, SecondPrinting, Continuation, Reboot, Reimagining
Generic:      Related, Similar, Other
```

The source doc's §26 only explicitly classifies 13 of these ~20 as directional or symmetric.
**The rest is my own classification, not the source doc's** - flagged explicitly so it isn't
mistaken for a cited fact later:

- **Symmetric** (identical display label from either series' side): `SameUniverse`,
  `SharedUniverse`, `SameContinuity`, `Crossover`, `SharedCharacters`, `Similar`, `Related`
  (all explicit in §26), plus `AlternateStory`, `AlternateVersion`, `ParallelStory`,
  `DifferentEdition`, `Variant`, `SecondPrinting`, `Other` (my extension - each of these describes
  an inherently mutual relationship, e.g. "alternate version of" reads the same from both ends).
- **Directional, named inverse pair** (explicit in §26): `Prequel` ↔ `Sequel`, `Adaptation` ↔
  `SourceMaterial`, `Contains` ↔ `CollectedFrom`.
- **Directional, no named inverse** (my extension - stored with a clear source/target for data
  integrity, but the source doc never coined a distinct inverse word, so the *same* label displays
  on both ends rather than inventing new vocabulary): `SideStory`, `SpinOff`, `Remake`,
  `Compilation`, `Continuation`, `Reboot`, `Reimagining`.

```csharp
public enum RelationDirectionality { Symmetric, Directional }

public static class RelationTypeCatalog
{
    // Field descriptor shape, consistent with 2c's LibraryFieldCatalog/this codebase's established
    // SmartListCatalog/BulkFieldRegistry idiom: one dictionary, keyed by RelationType, describing
    // directionality and (for the 3 named pairs) the inverse type to display from the target side.
    public static readonly IReadOnlyDictionary<RelationType, RelationTypeInfo> All = ...;
}

public sealed record RelationTypeInfo(RelationDirectionality Directionality, RelationType DisplayFromTargetSide);
```

For a symmetric type or a directional-no-named-inverse type, `InverseType` is just the type itself.

**A real direction bug, found and fixed before it shipped**: the first implementation inverted the
*target*-side display and left the *source*-side as-stored - backwards. `RelationType` always
describes the **source**'s own role relative to the target (`SourceSeriesId --Prequel-->
TargetSeriesId` means "Source is the Prequel of Target"). So viewed from the **target**'s page, the
source's card is already correctly labeled as-stored ("Prequel") - no inversion needed. Viewed from
the **source**'s page, the *target*'s card needs the inversion (the target's own role is "Sequel",
the opposite of what's stored). Caught by working through the source doc's own example
("A -> Prequel -> B ... displayable from B as B -> Sequel -> A") by hand, sentence by sentence,
before trusting the first instinctive implementation - not by a failing test this time, though the
test suite was rewritten to assert the corrected direction explicitly rather than just re-testing
the bug.

### `MediaRelationResolver`

New file, `src/Paperbunkr.Data/Metadata/MediaRelationResolver.cs`:

```csharp
public static class MediaRelationResolver
{
    public static IReadOnlyList<(Series OtherSeries, RelationType DisplayType)> GetRelatedSeries(PaperbunkrDbContext context, int seriesId);
}
```

Queries `MediaRelation` where the given series is either `SourceSeriesId` or `TargetSeriesId`.
For a row where the series is the **source**, the other series is `TargetSeriesId` and the
displayed type is `RelationType` as-stored. For a row where the series is the **target**, the
other series is `SourceSeriesId` and the displayed type is
`RelationTypeCatalog.All[RelationType].DisplayFromTargetSide` - the whole reason the catalog
above exists, so a `Prequel` relation stored from the earlier series' side shows as `Sequel` when
viewed from the later series' side, without a duplicate row.

`MediaRelationResolver` also owns the write-side operations (not sketched in the original design
pass, added during implementation since the validation rules below need one tested home rather
than being duplicated in the view model): `TryCreate(context, sourceSeriesId, targetSeriesId,
relationType)` enforces both validation rules and returns `false` without writing anything on
failure; `Remove(context, mediaRelationId)` deletes a relation (cascading to its evidence), a no-op
if it's already gone.

### Creation and removal UI

The Related tab (`DetailTabs.axaml`/`DetailTabsViewModel`) gets:

- A "+ Add Related Series" action opening a picker: search another series by name (reusing the
  existing series-search pattern from `ReadingScreenViewModel`'s "Add Issue" search - same
  in-memory `Contains`-on-`Series.Name` shape, not a new search mechanism), pick a `RelationType`
  from the full vocabulary (grouped by category in the picker UI for scannability, matching how
  the vocabulary itself is categorized above). Saving creates the `MediaRelation` +
  `RelationEvidence` row and refreshes the tab.
- A small remove/unlink action on each related-series card in the carousel - without one, a
  mistaken relation could never be undone through the UI. Removing deletes the `MediaRelation` row
  (cascading to its `RelationEvidence`).

### Display wiring

`DetailTabsViewModel.LoadSeries` populates `Related` for real via
`MediaRelationResolver.GetRelatedSeries` - `RelatedSeriesSample.Note` becomes the (possibly
target-side-inverted) `RelationType`'s display label. No categorized/sectioned display (the source
doc's §65 "Same Series / Same Continuity / Crossovers / Prequels-Sequels / ..." grouping) - stays
the flat carousel that already exists today; grouping is a natural, separate follow-up once real
relation data exists to make sections meaningful, not something to build speculatively now.

## Testing

- Entity/migration tests: `MediaRelation`/`RelationEvidence` table shape, cascade delete
  (removing a `MediaRelation` removes its `RelationEvidence`), existing rows untouched (purely
  additive schema, following 2a/2b's precedent - no rename/backfill risk).
- `MediaRelationResolverTests`: source-side lookup (type as-stored), target-side lookup for all
  three classification buckets (symmetric shows same label, named-pair shows the inverse,
  no-named-inverse shows the same label), a series with no relations returns empty, a series
  related to itself is impossible to create (validation test, not a resolver test).
- Creation/removal UI tests (`DetailTabsViewModelTests`): adding a relation makes it appear in
  `Related` from both series' `LoadSeries` calls (with correct inversion on the target side),
  removing it clears it from both, duplicate/self-relation attempts are rejected with no DB
  mutation.

## Explicitly out of scope

External provider integration as a relation source (Phase 5). Categorized/sectioned Related-tab
display (a follow-up once real data exists). Homepage "Because you read X" recommendation modules
(§46, Phase 6 - `Recommendation`/`RecommendationReason`, a distinct concept built *on top of*
relations, not this phase). Confidence-based filtering or display (every relation this phase is
user-asserted at `Confidence = 1.0`; meaningful confidence variance arrives with Phase 5).
`StoryEvent`/`Continuity`/`ReadingList` overhaul (Phase 4, a separate concept per the source doc's
own §51 "don't conflate ReadingList and StoryEvent").
