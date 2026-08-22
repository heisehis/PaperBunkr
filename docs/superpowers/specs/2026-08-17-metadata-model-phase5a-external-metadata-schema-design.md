# Metadata Model — Phase 5a: External Metadata Schema

**Date:** 2026-08-17
**Status:** Approved, pending implementation
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md`, §29-32 ("External Metadata
Providers", "External IDs", "External Metadata", "External Ratings"), §56-57 ("Provider
Normalization", "Provider Adapter Interface"), §68 ("Phase 5: External providers"). Continues
[[phase4a-continuity]]/[[phase4b-story-events]]/[[phase4c-reading-list-overhaul]] (Phase 4,
shipped). Phase 1-4 are all shipped.

## Context

**Deliberately scoped to schema only this pass** (user's explicit choice when offered "schema +
real AniList adapter" vs "schema only, revisit adapters later"): `ExternalMediaId`,
`ExternalMetadataSnapshot`, `ExternalRating` entities, the `IMetadataProvider` adapter contract
(§57), and a read-side resolver - no HTTP calls, no real provider implementation, no UI. §68's own
Phase 5 bundles the schema and "start with providers appropriate to the content type" together,
but building the schema with zero adapters to populate it mirrors this project's own Phase 1
precedent (canonical metadata landed before Phase 2's resolver had real proposal sources feeding
it) - a stable, tested contract now, real network integration as a deliberately separate follow-up
once this shape is confirmed.

Confirmed via search (standing project rule): CE has no external-provider concept of any kind -
unsurprising, since AniList/MAL/MangaDex/GCD-style APIs simply didn't factor into ComicRack's
original design. This is pure greenfield, same situation Phase 3/4b were in.

**Why schema-only is a real, complete unit of work and not a half-measure**: unlike Phase 3's
`MediaRelation` (which needed a manual-creation UI to be visible at all, since it had no automatic
population path), this phase's entities are explicitly row-per-provider-fetch records with **no
manual-entry story** - a `ExternalMediaId` with no adapter to populate it isn't meant to be
user-creatable, it's infrastructure waiting for a producer. Landing the schema, FK behavior, and
resolver now (fully testable without any network dependency) de-risks the adapter work later by
settling the shape first.

## Scope

### Entities

```csharp
ExternalMediaId
----------------
Id
SeriesId (FK Series)
Provider (enum: ExternalMetadataProvider)
ExternalId (string)
Url (string?)
LastFetchedAt (DateTime?)

ExternalMetadataSnapshot
------------------------
Id
SeriesId (FK Series)
Provider (enum: ExternalMetadataProvider)
ExternalId (string)
RetrievedAt (DateTime)
Payload (string - raw provider JSON, opaque to the core domain per §31: "Do not expose raw
    provider payloads as the normal application metadata model" - this is the debugging/
    reprocessing/audit copy, not something the UI reads directly)
SchemaVersion (string)

ExternalRating
--------------
Id
SeriesId (FK Series)
Provider (enum: ExternalMetadataProvider)
Value (decimal)
Scale (decimal - the rating's maximum, e.g. 10 or 100, so values from different providers can be
    normalized for display without hardcoding per-provider knowledge)
VoteCount (int?)
RetrievedAt (DateTime)
```

**Granularity: Series, not Issue** - matches Phase 3's `MediaRelation` precedent and the source
doc's own framing throughout §29-32 ("Media" in this context means the publication line a
provider's catalog entry corresponds to, e.g. an AniList manga entry maps to a `Series`, not to one
scanned file). All three FKs use `DeleteBehavior.Cascade`, same reasoning as `MediaRelation`/
`SeriesContinuity`: every existing Series-deletion path is automatic empty-series cleanup with no
interactive moment to check for external-data rows first, and an external-ID/snapshot/rating row
has no value once its Series is gone.

No uniqueness constraint enforced at the DB level for `(SeriesId, Provider)` on `ExternalMediaId` -
the source doc's own §28 notes multiple providers can independently corroborate the same
relationship/fact, and nothing here rules out a series matching two different external entries at
the same provider being a real (if unusual) situation an adapter might need to record; the resolver
below is where "one canonical ID per provider" policy, if wanted, would be enforced later once a
real adapter exists to need it.

### `ExternalMetadataProvider`

Straight from §29's provider list, not narrowed to just AniList even though this pass adds no
adapter for any of them - the enum needs to be complete now so a future single-provider adapter
doesn't force a schema migration just to add sibling values later:

```
AniList
MyAnimeList
MangaDex
MangaBaka
MangaUpdates
Kitsu
AnimePlanet
Shikimori
GrandComicsDatabase
LeagueOfComicGeeks
```

### `ExternalMetadataResolver`

New file, `src/Paperbunkr.Data/Metadata/ExternalMetadataResolver.cs` - read-only this pass (no
adapter exists yet to drive writes; a future adapter phase owns its own write path, likely calling
straight into `context.ExternalMediaIds.Add(...)` the way `MediaRelationResolver.TryCreate` does,
not through this resolver):

```csharp
public static class ExternalMetadataResolver
{
    public static IReadOnlyList<ExternalMediaId> GetExternalIds(PaperbunkrDbContext context, int seriesId);
    public static IReadOnlyList<ExternalRating> GetRatings(PaperbunkrDbContext context, int seriesId);
    public static ExternalMetadataSnapshot? GetLatestSnapshot(PaperbunkrDbContext context, int seriesId, ExternalMetadataProvider provider);
}
```

### `IMetadataProvider` (§57)

New file, `src/Paperbunkr.Data/Metadata/IMetadataProvider.cs` - the adapter contract every future
provider integration implements, defined now with **zero implementations** so the shape is settled
before the first real adapter needs to satisfy it:

```csharp
public interface IMetadataProvider
{
    ExternalMetadataProvider ProviderKey { get; }

    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken);
}
```

`MetadataSearchResult`/`ExternalMediaMetadata` are plain DTOs (`ExternalId`, `Title`, `Url`, and for
the latter, enough normalized fields - description, status, chapter/volume counts - to eventually
feed a `MetadataProposal`, per §56's "provider DTO → normalizer → canonical model" pipeline). No
`GetRelationsAsync` this pass (the source doc's own §57 sketch includes one, but Phase 3's
`MediaRelation` already has no provider-driven population path yet either - adding a method nothing
can call is speculative; revisit alongside whichever future phase wires external relations into
`MediaRelation`).

## Testing

- Entity/migration tests: three new tables, cascade delete on Series removal for all three,
  existing rows untouched (purely additive schema, no rename/backfill risk - matches 2a/2b/3/4a/4b
  precedent).
- `ExternalMetadataResolverTests`: `GetExternalIds`/`GetRatings` return empty for a series with no
  rows; return the right rows scoped to the queried series only (not another series' rows);
  `GetLatestSnapshot` returns the most recent `RetrievedAt` row for a given provider, `null` if none
  exists for that provider even when other providers have snapshots.

## Explicitly out of scope

Any real `IMetadataProvider` implementation (AniList or otherwise) - zero network calls this phase.
Any UI (nothing to search/match/display without a real adapter; a manual "paste an AniList URL"
entry point is a plausible future UI but wasn't part of what was asked for here). `ProviderPriority`
config (§59) - meaningless with zero real providers. Licensing/attribution enforcement (§60) - a
real concern for whichever phase adds the first real adapter, not applicable to a schema with no
data flowing through it yet. `MetadataProposal` integration (normalizing a future `GetAsync` result
into a proposal) - the resolver here is read-only and provider-write-side is deferred with the
adapter work itself.
