# Metadata Model: Second Metadata Provider (MangaDex)

**Date:** 2026-08-19
**Status:** Design sketch only, not implemented - fifth item from the same architecture-review
roadmap as the four adjacent specs, scoped by the user as "sketch now, build later" rather than
built this session (unlike R1-R4, which are shipped).

## Context

`IMetadataProvider` (docs/superpowers/specs/2026-08-18-metadata-model-phase5b-anilist-adapter-
design.md) was deliberately designed provider-agnostic with exactly one implementation
(`AniListMetadataProvider`) so a second provider costs an adapter, not a redesign. This sketch picks
a concrete second provider and outlines the adapter, rather than leaving "second provider" abstract.

## Why MangaDex, not MyAnimeList/Metron/ComicVine

This is a recommendation, not a locked decision - revisit at implementation time if priorities
shift.

- **MyAnimeList** covers materially the same ground as AniList already does (title/status/chapter-
  count metadata) - low marginal value as the *second* provider.
- **Metron/ComicVine** are Western-comics-specific (`ExternalMetadataProvider` already has slots for
  both) and are a better fit for testing the "Western comic provider" architecture-acceptance case
  the source review's own document emphasized (§126) - but that's a *different* kind of provider
  (issue/volume/creator-credit shaped, not chapter-shaped) and deserves its own separate sketch, not
  bundled into this one.
- **MangaDex** is a genuine complement to AniList, not a duplicate: real per-chapter/scanlation-group
  data AniList doesn't expose, a REST API needing no auth for reads, and richer localized titles
  (a keyed map, not three fixed fields) - a good stress test for the `SeriesTitle`/`SeriesTitleType`
  shape (docs/superpowers/specs/2026-08-19-metadata-model-multi-value-titles-design.md) added this
  session.

## Design

### `MangaDexMetadataProvider` (`Paperbunkr.Data/Metadata/`)

Mirrors `AniListMetadataProvider`'s exact shape: `IMetadataProvider` implementation, own
`MangaDexDtos`/`MangaDexNormalizer`, a private `SendAsync` wrapper owning retry/rate-limit state, no
provider-specific type ever crossing into `Paperbunkr.App`. REST, not GraphQL - MangaDex's API is
`https://api.mangadex.org`:

```text
GET /manga?title={query}&limit=10     (search)
GET /manga/{id}                       (get by id)
```

MangaDex documents its own rate limits at api.mangadex.org's docs - **verify the current published
figures at implementation time** (they've changed before, and hardcoding a stale value here would be
worse than not writing one down) and apply the same conservative-under-the-documented-limit posture
`AniListMetadataProvider`'s own doc comment already commits to.

### Title normalization

MangaDex's `attributes.title`/`attributes.altTitles` are a language-code-keyed map (`en`, `ja`,
`ja-ro`, ...), richer than AniList's three fixed fields. Normalize into the same
`ExternalMediaMetadata.TitleEnglish`/`TitleRomaji`/`TitleNative` shape
`MetadataLinkResolver.LinkAsync` already consumes (`en` -> `TitleEnglish`, `ja-ro` -> `TitleRomaji`,
`ja` -> `TitleNative`) - no change needed to `MetadataLinkResolver` or `SeriesTitle` itself, this is
purely `MangaDexNormalizer`'s job, same "provider vocabulary never leaks past the mapper" rule
`AniListNormalizer` already follows.

### No schema change

`ExternalMediaId`/`ExternalMetadataSnapshot` are already provider-keyed
(`ExternalMetadataProvider.MangaDex` already exists in the enum, unused until now) with a
`(SeriesId, Provider)` link shape - a series can already carry both an AniList link and a MangaDex
link simultaneously. This is Phase 5a's schema (2026-08-17) working exactly as designed for a second
provider three sessions later.

### UI: provider picker, not a second flow

`DetailTabsViewModel`'s search flyout (docs/superpowers/specs/2026-08-19-metadata-model-anilist-
search-and-link-design.md) already takes an `IMetadataProvider` instance - the only new UI is a
small provider selector (two pill buttons, "AniList"/"MangaDex", matching this app's established
enum-picker idiom) next to the search box, swapping which `IMetadataProvider` `SearchMetadataAsync`/
`LinkMetadataAsync` use. `TitleMatchScorer`/`MetadataLinkResolver` need no changes - both are already
provider-agnostic.

### Testing

Same fixture pattern as `AniListMetadataProviderTests` - a fake `HttpMessageHandler` returning
recorded JSON, no real network calls in CI. `MetadataLinkResolverTests` already covers the provider-
agnostic linking logic against a fake `IMetadataProvider`, so `MangaDexMetadataProvider` only needs
its own DTO-mapping tests, not a second copy of the link-workflow tests.

## Open questions for implementation time

1. MangaDex's actual current rate limits (verify, don't assume).
2. Whether `altTitles` (plural, MangaDex allows multiple titles per language) should feed more than
   one `SeriesTitle` row per type, or just the first - lean toward first-only for v1, matching this
   session's "boring version first" precedent elsewhere.
3. Whether search should query both providers at once (merged, deduplicated results) or stay
   single-provider-at-a-time (simpler, recommended for v1).
