# Metadata Model — Phase 5b: Real AniList Adapter

**Date:** 2026-08-18
**Status:** Approved, pending implementation
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md`, §29/§33 ("External Metadata
Providers", "Manga Metadata Provider Notes"), §56-60 ("Provider Normalization", "Provider Adapter
Interface", "Metadata Matching", "External Source Priorities", "Data Licensing"), §68 ("Phase 5:
External providers" - "Start with providers appropriate to the content type"). Continues
[[phase5a-external-metadata-schema]] (schema-only, shipped).

## Context

Phase 5a shipped the schema (`ExternalMediaId`/`ExternalMetadataSnapshot`/`ExternalRating`,
`IMetadataProvider` contract, read-only `ExternalMetadataResolver`) with zero real adapters,
explicitly deferring "any real `IMetadataProvider` implementation" to a follow-up. This is that
follow-up - the first real, network-calling adapter.

**Scope, per explicit user direction this session:** AniList only. Every other provider in §29's
list (MyAnimeList, MangaDex, MangaBaka, MangaUpdates, Kitsu, Anime-Planet, Shikimori, GCD, League of
Comic Geeks) and all search/match UI are deliberately deferred - not to a future "Phase 5c," but
folded into the already-scoped **tracker-service sync** backlog item
(`docs/alpha-roadmap.md`'s "Content-type classification & manga metadata scraping" section,
research doc at `docs/tracker-manga-ui-research.md`), which already covers exactly this ground
(AniList as one of six tracker services, a shared search-and-confirm UI, a manga-specific detail
page) and is explicitly Beta-scoped pending its own brainstorm/design pass. Building the adapter now
as tested, working infrastructure means that future work reuses it rather than re-implementing
AniList integration from scratch.

**Why backend-only is a complete unit of work, not a half-measure:** same reasoning as Phase 5a's
own "schema-only is real work" argument - `AniListMetadataProvider` is fully testable (DTO parsing,
rate-limit handling, error handling) without any UI to drive it, and settles the hardest, most
provider-specific part (the actual HTTP/GraphQL contract, real rate limits, real response shapes)
before the shared multi-provider UI work has to accommodate it.

### AniList terms verified before implementation (source doc §33/§60's explicit requirement)

`docs.anilist.co` 403s to automated fetches; verified instead against `github.com/AniList/docs`
(the actual source repository the docs site is generated from):

- **Licensing**: free for non-commercial use; free even commercially under $150/mo revenue.
  Paperbunkr is neither commercial nor anywhere near that threshold.
- **Prohibited**: "Using the AniList API as a backup or data storage service" and "hoarding or mass
  collection of data" are both explicitly banned. This adapter only ever fetches data for one
  series at a time, on an explicit caller-driven search/get call - no bulk crawling, no catalog
  mirroring. `ExternalMetadataSnapshot`'s raw-payload cache (Phase 5a) is scoped to entries the user
  has actually matched, which is normal per-app caching, not the prohibited use.
- **Naming**: can't brand a feature as an official AniList product; plain "Source: AniList"
  provenance labeling (required anyway by §60) is fine.
- **Rate limiting**: currently degraded to **30 requests/minute** (nominally 90/min). Real headers:
  `X-RateLimit-Limit`, `X-RateLimit-Remaining` on every response; `Retry-After` (seconds) and
  `X-RateLimit-Reset` (Unix timestamp) on a `429`. The adapter must respect these for real, not just
  nominally.
- **Availability**: severe outages return a `403` with a GraphQL error body, not a `5xx` - the
  adapter's error handling needs to treat AniList-signaled unavailability as a normal, expected
  failure mode, not an exceptional one.
- **Adult content**: AniList entries can include adult/ecchi content that isn't always reliably
  filterable. Not a blocker for a desktop app with no parental-control system today; noted, not
  acted on further.

## Scope

### `AniListMetadataProvider : IMetadataProvider`

New file, `src/Paperbunkr.Data/Metadata/AniListMetadataProvider.cs`. Implements the existing (Phase
5a) contract exactly - no interface changes:

```csharp
public sealed class AniListMetadataProvider : IMetadataProvider
{
    public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.AniList;

    public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken ct);
    public Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken ct);
}
```

- Takes an injected `HttpClient` via constructor (test seam - production uses one shared static
  instance per .NET's own HttpClient-reuse guidance, the same reasoning `PaperbunkrDb.CreateContext`
  documents for why *its* seam exists, applied to a different resource).
- Endpoint: `POST https://graphql.anilist.co`, `Content-Type: application/json`, body
  `{"query": "...", "variables": {...}}` - AniList's GraphQL API takes no API key for the queries
  this adapter needs (public read-only media queries).
- `SearchAsync`: `Page(page: 1, perPage: 10) { media(search: $q, type: MANGA) { id, title { romaji,
  english }, siteUrl } }` - `type: MANGA` because Paperbunkr's own `ContentType` enum's AniList-
  relevant values (Manga/Manhwa/Manhua/Webtoon/Doujinshi) all map to AniList's single `MANGA` media
  type (AniList doesn't subdivide manga/manhwa/manhua at the type level - that's a `countryOfOrigin`
  distinction Phase 5b doesn't need yet since nothing consumes it). Maps each result's `id` (AniList
  media IDs are integers, stringified for the `string ExternalId` contract) and best-available title
  (English if present, else romaji) into `MetadataSearchResult`.
- `GetAsync`: `Media(id: $id) { id, title { romaji, english }, siteUrl, description(asHtml: false),
  status, chapters, volumes }` - maps into `ExternalMediaMetadata` per its existing shape
  (`Description`→`description`, `Status`→AniList's status enum stringified, `ChapterCount`→
  `chapters`, `VolumeCount`→`volumes`).
- Rate limiting: a simple in-process throttle (min-interval gate, not a full token bucket - one
  provider, one adapter instance, no concurrent-request fan-out anywhere yet) capped at AniList's
  *current* 30/min rather than the nominal 90/min, erring conservative since the doc flags the lower
  limit as (indefinitely) active. Reads `X-RateLimit-Remaining` from each response to short-circuit
  the next call to a wait if AniList reports we're close to empty, and honors `Retry-After` verbatim
  on an actual `429` rather than guessing a backoff.
- Error handling: network failure, non-200/429 status, or a GraphQL `errors` array all surface as a
  `null` return (`GetAsync`) or empty list (`SearchAsync`) rather than throwing - matches this
  interface's existing nullable/empty-collection contract for "not found," and callers (none yet)
  shouldn't need provider-specific exception handling.

### `AniListNormalizer`

New file, `src/Paperbunkr.Data/Metadata/AniListNormalizer.cs` - the provider-DTO -> canonical-model
step §56 requires kept separate from both the raw JSON deserialization types and the adapter's HTTP
mechanics, so the mapping logic is unit-testable against literal DTOs without any HTTP involved.
Internal (not part of the public `IMetadataProvider` contract) - `AniListMetadataProvider` is the
only caller.

### DTOs

Plain internal records for AniList's GraphQL response shape (`AniListSearchResponse`,
`AniListMediaDto`, `AniListTitleDto`, etc.) - private to this feature, not shared entities.
`System.Text.Json` (already implicit via the .NET 8 BCL, no new package needed) with
`JsonPropertyName` attributes for AniList's camelCase field names.

## Explicitly out of scope

Every other provider (§29's remaining nine). Any UI - nothing in `Paperbunkr.App` calls this
adapter yet; it lands as tested infrastructure with no caller, same as Phase 5a's entities landed
with no adapter. `MetadataProposal` integration. `ProviderPriority` config. `GetRelationsAsync`
(AniList's media-relations data - Phase 5a's `IMetadataProvider` already deliberately excludes this
method; still nothing to call it). Writing `ExternalMediaId`/`ExternalMetadataSnapshot`/
`ExternalRating` rows to the database (this adapter only *fetches and normalizes*; a future caller,
per Phase 5a's own resolver doc comment, would write via `context.ExternalMediaIds.Add(...)`
directly, the same shape `MediaRelationResolver.TryCreate` already uses - that caller doesn't exist
yet, so there's nothing to wire the write path into). All of the above tracked as future
tracker-service-sync work, not a numbered Phase 5c.

## Testing

- `AniListMetadataProviderTests`: constructs with an injected `HttpClient` wrapping a fake
  `HttpMessageHandler` returning canned AniList JSON (no real network calls in the automated suite,
  consistent with "don't hammer AniList" even for CI runs) -
  - `SearchAsync` parses a multi-result search response into the right `MetadataSearchResult` list,
    preferring English title over romaji when both present, falling back to romaji when English is
    null.
  - `GetAsync` parses a single-media response into `ExternalMediaMetadata` with all fields populated,
    and handles partially-null fields (e.g. `description: null`) without throwing.
  - A GraphQL `errors` response body (still HTTP 200, AniList's actual error shape) returns
    null/empty rather than throwing.
  - A `429` response is not retried in-test (would require real timing) but the adapter reads and
    would honor `Retry-After` - covered by a focused test on the header-parsing helper, not a live
    retry loop.
  - Network exception (simulated via a throwing handler) returns null/empty, doesn't propagate.
- One manual, one-off smoke test against the real live API (not part of the automated suite) to
  confirm the adapter works end-to-end against AniList's actual current schema before calling this
  done - documented in the session notes, not committed as a repeatable test.
