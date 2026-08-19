# Metadata Model — Phase 6a: Recommendation Engine

**Date:** 2026-08-18
**Status:** Approved, pending implementation
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md`, §46-49 ("Related Media and Homepage
Recommendations", "Recommendation Scoring", "Recommendation Filtering", "Similarity vs
Relationship"), §67 ("Recommendation Explanation"), §68 ("Phase 6: Recommendations"). Continues
[[phase5b-anilist-adapter]] (Phase 1-5b all shipped).

## Context

§68's Phase 6 is small on paper ("Implement: `Recommendation`, `RecommendationReason`,
Recommendation scoring") but §47 lists twelve separate scoring signals, several of which have no
data source yet in this codebase:

- `ReadingHistoryScore` needs §8's `ReadingSession` entity - explicitly marked optional/not-yet-built
  in the source doc itself, and this codebase hasn't built it.
- `PopularityScore`/`CommunityScore`/`ExternalRecommendationScore` need real external data flowing
  into `ExternalRating`/`ExternalMetadataSnapshot` - Phase 5b shipped a working `AniListMetadataProvider`
  but explicitly with **no caller and no write path** (its own scope note). Nothing populates those
  tables yet.
- `RecommendationReason`'s `Trending`/`RecentlyAdded`/`ContinueReading`/`IncompleteReadingOrder`
  values aren't pairwise "because you read X, try Y" reasons at all - they're absolute/temporal
  homepage-module queries (§66) with a fundamentally different shape (no source series, just a
  ranked list). `ExternalRecommendation` needs the same missing external data as above. `SameSeries`
  doesn't fit this project's established Series-granularity for relation-style concepts (Phase 3/4a/
  5a precedent) - it reads like an issue-level "another issue in this series" idea that doesn't apply
  once `Recommendation` is Series-to-Series.

**Scope for 6a**: a real, explainable, tested recommendation engine using only signals this codebase
can already compute from shipped data - `MediaRelation` (Phase 3), `Continuity` (4a),
`EventMembership`/`StoryEvent` (4b), and `Series`/`Issue`'s existing Genre/Characters/Creator/Tags
fields. No homepage UI (doesn't exist yet - would need its own screen, a bigger undertaking than a
first slice), no `ReadingHistoryScore`/`PopularityScore`/`CommunityScore`/`ExternalRecommendationScore`,
no `Trending`/`RecentlyAdded`/`ContinueReading`/`IncompleteReadingOrder`. Same "real, complete unit
of work now, the rest layers on later" reasoning as 5a (schema before adapters) and 5b (one adapter,
no UI yet).

## Scope

### Candidate pool: relationally-anchored, not whole-library similarity

Per §49, "similarity" (algorithmic, content-based) and "relationship" (factual, asserted) are
explicitly different concepts, and the doc warns not to store similarity as a permanent factual
relationship. Phase 6a takes this a step further operationally: **a target series only becomes a
recommendation candidate if it shares an existing asserted relationship, continuity, or event with
the source series** (`MediaRelationResolver.GetRelatedSeries`, `ContinuityResolver.
GetOtherSeriesSharingContinuity`, `EventMembershipResolver.GetOtherSeriesInSharedEvents` - all three
already exist and are reused directly, not reimplemented). Content-similarity signals
(genre/character/creator/tag overlap) then *refine* an already-anchored candidate's score and can
supply its explanation, but never independently surface an otherwise-unrelated series through genre
overlap alone. A pure whole-library content-similarity search ("recommend anything genre-similar,
related or not") is a different, larger feature - out of scope here, not attempted.

### `RecommendationReason`

New enum, `src/Paperbunkr.Data/Entities/RecommendationReason.cs` - the subset of the source doc's
§46 list this phase can actually produce:

```csharp
public enum RecommendationReason
{
    Related,
    SameUniverse,
    SameContinuity,
    SameEvent,
    Crossover,
    Prequel,
    Sequel,
    SharedCharacters,
    SharedCreators,
    SharedGenre,
    SharedTags,
}
```

### Live-computed, not a persisted table

The source doc's §1.1 vocabulary itself classifies a value like this as **Derived** ("Calculated
from other authoritative data... must not become competing sources of truth"), and Rule 7 says
"avoid duplicate sources of truth... prefer derivation." A `Recommendation` row would be a cache of a
computation over `MediaRelation`/`Continuity`/`EventMembership`/`Series`/`Issue` data that's already
fully authoritative and already changes whenever the user edits any of those - persisting it risks
exactly the staleness problem Rule 7 warns about, with nothing yet driving a "regenerate" job to keep
it fresh. This also matches this project's own established precedent: Smart Lists (2026-08-06) chose
live in-memory query evaluation over persisted materialized results for the same reason.

So `Recommendation` here is a **plain record returned by a resolver**, not an EF entity/DbSet - same
shape the source doc describes (`SourceSeriesId`/`TargetSeriesId`/`ReasonType`/`Score`/
`Explanation`/`GeneratedAt`), computed fresh on each call. `ExpiresAt` (§46) is dropped entirely -
it only has meaning for a cached/persisted value, and there's nothing here to expire. If a future
phase adds real caching (e.g. once this actually feeds a homepage and computation cost matters),
that phase can reintroduce persistence and `ExpiresAt` then, informed by real performance data
instead of guessed now.

```csharp
namespace Paperbunkr.Data.Metadata;

public sealed record RecommendationCandidate(
    int SourceSeriesId,
    int TargetSeriesId,
    RecommendationReason ReasonType,
    decimal Score,
    string Explanation,
    DateTime GeneratedAt,
    RecommendationSignals Signals);

/// <summary>Per-signal breakdown (§47: "expose separate signals," not one opaque number) - also what the dominant <see cref="RecommendationReason"/> is picked from.</summary>
public sealed record RecommendationSignals(
    decimal RelationScore,
    decimal ContinuityScore,
    decimal EventScore,
    decimal CharacterScore,
    decimal CreatorScore,
    decimal GenreScore,
    decimal TagScore);
```

### Scoring

New file, `src/Paperbunkr.Data/Metadata/RecommendationResolver.cs`:

```csharp
public static class RecommendationResolver
{
    public static IReadOnlyList<RecommendationCandidate> GetRecommendations(
        PaperbunkrDbContext context, int seriesId, int limit = 10);
}
```

Per-candidate signals (each 0.0-1.0 decimal):

- **RelationScore**: the target's `MediaRelation` evidence confidence (`RelationEvidence.Confidence`,
  already 0.0-1.0; a user-created relation is always 1.0 per Phase 3's own doc comment) - the
  strongest single signal, since it's a direct factual assertion. 0 if no direct `MediaRelation`
  (the pair can still be a candidate via shared Continuity/Event alone).
- **ContinuityScore** / **EventScore**: binary 1.0/0.0 - shares/doesn't share a `Continuity` or
  `StoryEvent` membership with the source series (via the existing resolvers).
- **CharacterScore** / **CreatorScore** / **GenreScore** / **TagScore**: token-overlap ratio between
  the two series' aggregated values - `Issue.Characters`/creator fields (`Writer`/`Penciller`/
  `Inker`/`Colorist`/`Letterer`/`CoverArtist`/`Editor`/`Translator`)/`Issue.Tags` unioned across all
  of each series' issues, `Series.Genre` used directly (already series-level, no aggregation
  needed). Comma-separated tokenization, case-insensitive - same convention as
  `Paperbunkr.App.Models.ListFieldTokens` (can't reference it directly, wrong dependency direction:
  `Paperbunkr.Data` doesn't depend on `Paperbunkr.App`), reimplemented locally at the same shape.
  Overlap ratio = `|intersection| / |union|` (Jaccard), 0 when either side has no tokens.

**Weights** (placeholder, per §47's own "these weights are placeholders, not a fixed product
requirement" framing - centralized in one place, easy to retune later):

```
FinalScore =
    RelationScore   * 0.30
  + ContinuityScore * 0.15
  + EventScore      * 0.15
  + CharacterScore  * 0.15
  + CreatorScore    * 0.10
  + GenreScore      * 0.10
  + TagScore        * 0.05
```

Weighted toward the asserted/factual signals (Relation/Continuity/Event = 0.60 combined) over
content-similarity ones (0.40 combined), matching §49's own framing that relationship and similarity
are different in kind, not just degree.

**Dominant reason** (drives `ReasonType`/`Explanation`): whichever named signal has the highest
*weighted* contribution (`signal * its weight`) for that candidate. Continuity/Event/Character/
Creator/Genre/Tag map directly to their matching `RecommendationReason`. Relation maps through the
underlying `RelationType`: `Prequel`→`Prequel`, `Sequel`→`Sequel`, `Crossover`→`Crossover`,
`SameUniverse`/`SharedUniverse`→`SameUniverse`, `SameContinuity`→`SameContinuity` (a *pairwise*
`MediaRelation` assertion, distinct from the `ContinuityScore` signal's first-class `Continuity`
grouping - both can independently exist per §49/Phase 4a's own precedent), `SharedCharacters`→
`SharedCharacters`; every other `RelationType` (`SideStory`/`SpinOff`/`Adaptation`/etc.) falls back
to generic `Related`. If a pair has more than one `MediaRelation` row, the highest-confidence one
drives this mapping.

**Explanation**: a short templated string from the dominant reason and the target series' name, e.g.
`"Prequel of {target}"`, `"Shares continuity with {target}"`, `"Shares 3 characters with {target}"`
(character/creator/genre/tag explanations include the overlap count, relation/continuity/event ones
don't need a count).

Results sorted by `FinalScore` descending, capped at `limit`.

## Explicitly out of scope

Homepage UI/modules (§66) - no screen exists yet to show these on. `ReadingHistoryScore`,
`PopularityScore`, `CommunityScore`, `ExternalRecommendationScore` - no data source
(`ReadingSession` entity, real `ExternalRating`/`ExternalMetadataSnapshot` rows) exists yet.
`Trending`/`RecentlyAdded`/`ContinueReading`/`IncompleteReadingOrder` reasons - different shape
entirely (absolute/temporal ranked lists, not pairwise "because you read X"), belongs with whichever
phase builds the actual homepage. §48's full filtering pipeline (content-rating settings, "read next
from my library" availability, canonical-vs-variant dedup) - most of those concerns don't exist yet
either (no content-rating settings, no edition/variant model); the one filter that's meaningful now
(exclude the source series itself) is implicit since the candidate pool is built from *other* series'
relations. Persisting `Recommendation` rows / `ExpiresAt` - deliberately deferred to whenever real
caching is needed, see the live-computed reasoning above.

## Testing

`RecommendationResolverTests` (real SQLite database, same pattern as
`MediaRelationResolverTests`/`ContinuityResolverTests`):

- A series with a direct `MediaRelation` (e.g. `Prequel`) to another: candidate appears, `ReasonType`
  = `Prequel`, `RelationScore` reflects the evidence confidence.
- A series sharing only a `Continuity` (no direct relation): candidate appears with `ReasonType` =
  `SameContinuity`, `RelationScore` = 0.
- A series sharing only a `StoryEvent` membership (via an issue): candidate appears with `ReasonType`
  = `SameEvent`.
- Two relationally-anchored series with heavy character/genre overlap: the content-similarity signals
  visibly raise `FinalScore` and can flip `ReasonType` to `SharedCharacters`/`SharedGenre` when that
  signal's weighted contribution exceeds the relational one's.
- A series with genre/character overlap but **no** relation/continuity/event link: does not appear
  as a candidate at all (the relationally-anchored-pool boundary).
- `limit` is respected; results are ordered by `Score` descending.
- Token-overlap helper: case-insensitivity, empty-on-both-sides, empty-on-one-side, partial overlap
  ratio math.
