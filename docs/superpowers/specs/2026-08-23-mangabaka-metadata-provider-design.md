# MangaBaka Metadata Provider — Design

*Follow-on from the manga detail screen work — user asked why MangaBaka wasn't in the tracker list.
Investigated live: MangaBaka has no user accounts, no per-user library/list endpoints — it cannot
be a "tracker" the way AniList/MAL/Shikimori/Bangumi are (there's nothing to push reading progress
to). It's structurally the same shape as AniList's own metadata-search half instead, so it's added
as a second `IMetadataProvider`, not a tracker.*

**Correction (same day, after a user-supplied OpenAPI spec):** the conclusion above was wrong. A
real authenticated tracker API does exist — `PUT`/`PATCH /v1/my/library/{series_id}` (state/
progress/rating/notes, PAT or OAuth `library.write`) — it just isn't reachable from the
*unauthenticated* endpoints this provider calls. See `docs/mangabaka-metadata-ui-research.md`
findings 12/18 for the full picture; a real `MangaBakaTrackerAdapter` is a legitimate future option,
not ruled out. The rate-limit paragraph below was also corrected: 30 req/min on search is real and
documented (found via `mangabaka.org/data/api`), not just inferred good-citizen pacing — the
provider's 500ms gate (120/min) was tightened to 2000ms (30/min) to actually respect it.

## API (confirmed live, not from docs — MangaBaka's own API-explorer page returned no usable
schema for an unauthenticated fetch)

- `GET https://api.mangabaka.org/v2/series/search?q=` — no auth headers, returns
  `{ status, pagination, data: [...] }`; each item has a flat `title` string plus `id`,
  `description`, `status`, `total_chapters`, `final_volume`.
- `GET https://api.mangabaka.org/v2/series/{id}` — returns `{ status, data: {...} }`; the item has
  **no flat `title` field**, only a `titles` array of `{ language, title, traits: string[],
  is_primary }` (e.g. `en`/official, `ja`/native, `ja-Latn`/native-romanized entries observed on a
  real series). `MangaBakaSeriesDto` covers both shapes in one type. No canonical series-page URL
  field exists anywhere in the response — `Url` is left `null` on every result rather than guessing
  a URL pattern (one guess, `mangabaka.org/manga/{id}`, was checked live and 404'd).
- No documented rate limit (MangaBaka's own docs note caching applies to every endpoint including
  search) — a 500ms client-side pacing gate was added anyway as good-citizen behavior, not because
  a limit was observed.

`ExternalMetadataProvider.MangaBaka` already existed in the enum (authored complete up front,
per that enum's own doc comment, specifically so a later single-provider adapter wouldn't force a
migration) — no schema change needed at all.

## Implementation

`MangaBakaMetadataProvider : IMetadataProvider` (`src/Paperbunkr.Data/Metadata/`), same structural
shape as `AniListMetadataProvider` (private rate-limited `FetchAsync<T>` helper, DTOs with
`JsonPropertyName` attributes, a static `MangaBakaNormalizer`) but plain REST GET instead of
GraphQL POST. `MangaBakaNormalizer.ResolveDisplayTitle` prefers the search endpoint's flat title,
then an English `titles` entry, then the first primary-flagged one, then whatever's first, and
never throws even if every title field is somehow absent (falls back to `"Untitled"`).

`DetailTabsViewModel` gained a `SelectedMetadataProvider` picker (mirroring the Trackers section's
own `SelectedTrackerService`) and `GetMetadataProviderFor(ExternalMetadataProvider)`, constructing
a fresh `MangaBakaMetadataProvider` per call (same "no DI container" precedent as trackers) while
AniList keeps using the existing injected/test-seam `_metadataProvider` instance.
`SearchMetadataAsync`/`LinkMetadataAsync` now route through the selected provider instead of a
hardcoded AniList instance; `MetadataLinkResolver` itself needed no changes (already
provider-agnostic). The Details tab's "+ Link to AniList" button/watermark were generalized to
"+ Link External Metadata"/"Search…" with a provider `ComboBox` added.

## Explicitly out of scope for this pass

The user's actual motivation for MangaBaka is broader — its site has a much richer taxonomy (typed
relations, layered Genres/Themes/Settings/Character-Archetype tags, multi-cover browsing, "readers
also like" recommendations) than this basic search/link adapter touches. That's a separate future
research pass (a `tracker-manga-ui-research.md`-style memo) to inform a real metadata-model/UI
initiative, not folded into this small provider addition.

## Testing

`MangaBakaMetadataProviderTests` (`Paperbunkr.Data.Tests`) — 7 cases against a fake
`HttpMessageHandler`, using trimmed copies of real live response JSON captured during development
(not guessed): flat-title search parsing, `titles`-array parsing with English preference, the
no-title-at-all fallback, non-integer id short-circuit, not-found/network-failure resilience,
`ProviderKey`. All passed on first run against the real captured shapes. On-screen verification of
the provider picker UI is pending as of this write-up.
