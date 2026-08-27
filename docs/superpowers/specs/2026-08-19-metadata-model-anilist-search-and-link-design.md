# Metadata Model: AniList Search-and-Link Flow

**Date:** 2026-08-19
**Status:** Approved, implemented (on-screen verification pending - no unattended desktop GUI
automation available for this project, same standing caveat as every prior reader/UI spec)

## Context

Third item from the same architecture-review roadmap as the two adjacent specs. Phase 5b
(`2026-08-18-metadata-model-phase5b-anilist-adapter-design.md`) shipped a real, live
`AniListMetadataProvider` (`IMetadataProvider.SearchAsync`/`GetAsync`) but explicitly deferred all
search/match UI to "the tracker-service-sync backlog item" - confirmed this session:
`AniListMetadataProvider`/`IMetadataProvider` had **zero callers anywhere in `Paperbunkr.App`**. The
review's original "codify the match-confidence thresholds into the AniList matching path"
recommendation assumed a matching path already existed to codify into; it didn't, so this spec
builds the minimal real one rather than leaving `TitleMatchScorer` as more speculative dead code
(the exact anti-pattern `IMetadataProvider`'s own doc comment warns against).

## Design

### Confidence scoring (`TitleMatchScorer`, `Paperbunkr.Data/Metadata`)

Normalized Levenshtein-distance similarity (0.0-1.0) between two titles, case/punctuation/whitespace-
insensitive. `BestScore` takes the max across a series' primary name plus every alternate
`SeriesTitle` (2026-08-19-metadata-model-multi-value-titles-design.md) - a native-script alternate
title can out-score the localized primary name against a native-script search result. Thresholds
straight from the review's own recommendation: `>= 0.95` `MatchTier.Auto`, `0.75-0.949`
`MatchTier.NeedsReview`, below `MatchTier.Reject`. Deliberately just this one signal (not the
review's fuller ISBN/creator-overlap/publication-date scorer) - those signals don't exist for a
manga-only provider search yet; boring version now, richer scorer only if a real need shows up.

### Search + link workflow (`MetadataLinkResolver`, `Paperbunkr.Data/Metadata`)

Provider-agnostic (`IMetadataProvider`, not `AniListMetadataProvider` directly) even though AniList
is the only real implementation - matches this codebase's existing provider-interface discipline.

- `SearchAsync`: calls the provider, scores every result, orders best-first.
- `LinkAsync`: fetches full metadata for the chosen external id, then:
  - Upserts `ExternalMediaId` (one per series+provider - re-linking replaces rather than duplicates).
  - Always appends a fresh `ExternalMetadataSnapshot` (append-only audit log, per its own doc
    comment - unlike the link, snapshots are never overwritten).
  - Adds any new `SeriesTitle` rows the provider returned (native/romanized/localized) that this
    series doesn't already have, case-insensitively, never duplicating `Series.Name` itself - the
    concrete writer `SeriesTitle` needed to not be speculative schema.

AniList's `GetByIdQuery` (only the by-id query, not search - search results only need a display
title) gained `native` alongside the existing `romaji`/`english`, and `ExternalMediaMetadata` gained
`TitleEnglish`/`TitleRomaji`/`TitleNative` fields (all optional, existing `Title` unchanged) to carry
them without breaking the one existing caller shape.

### HTTP client

No DI container/`IHttpClientFactory` registration exists in this app. `AniListHttpClient.Shared` is
a single static `HttpClient` (the simplest correct option at this app's scale - one user, occasional
manual searches, not the high-throughput scenario `IHttpClientFactory` exists for).

### UI (`DetailTabsViewModel` / `DetailTabs.axaml`, Details tab)

Placed on the Details tab (Publisher/Reading Mode today) rather than a new tab or a separate
inspector panel - this app has no Tier-3 "inspector" concept, and the Details tab is already the
right depth for provenance-adjacent metadata, not the main Issues/Related grid. Same search-flyout
idiom as the Related tab's "+ Add Related Series" (`IsAddingRelation`/`RelationSearchQuery`), but
async/network rather than instant local-DB filtering, hence explicit Search button (not live-as-you-
type) plus loading/error states. Each result row shows a confidence badge (Best Match/Possible
Match/Low Confidence, from `AniListMatchSample.TierLabel`/`TierClass`) and a Link button - manual
click required regardless of tier; `MatchTier.Auto` only affects the badge, it does not auto-link
without a click. Already-linked providers show as removable chips (`ExternalLinks`, mirrors
`ContinuityChips`' exact shape) - `UnlinkMetadataCommand` removes only the `ExternalMediaId` row,
per the "removing a metadata provider must not delete the Work" invariant; snapshots and any titles
it contributed stay.

A stale in-flight search is cancelled (`CancellationTokenSource`) if the user retriggers before it
resolves, so a slow AniList response can't race stale results into the list.

## Testing

- `TitleMatchScorerTests`: identity/normalization/tier-boundary cases.
- `MetadataLinkResolverTests` (fake `IMetadataProvider`, real SQLite): search scoring/ordering,
  alternate-title matching, link upsert-not-duplicate, append-only snapshots, title population and
  dedup, provider-failure and unknown-series paths.
- `DetailTabsViewModelTests`: `LoadSeries` populates `ExternalLinks`; search populates scored
  results; link creates the row and closes the search flyout; unlink removes the link but leaves the
  series and its data intact.

297 `Paperbunkr.Data.Tests`, 674 `Paperbunkr.App.Tests` all pass. XAML compiled clean (Avalonia's
compiler resolves binding paths/converters/DataTemplate types at build time). On-screen interactive
verification blocked this session by a computer-use tooling limit (its access grant is scoped to the
installed `Program Files` executable path; a `dotnet run` dev build launches from a different path
and gets masked out of screenshots even under the same window title) - not attempted further per
this project's standing accepted caveat for GUI automation gaps.
