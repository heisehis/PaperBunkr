# You vs Community, Chapter/Volume Alignment, and Metadata Model Phase 7 — Design

**Date:** 2026-10-26
**Status:** Approved 2026-10-26; built the same day (see "As built" at the end)
**Source:** Grilling rounds 1–2 with Ehis (2026-10-26). Closes three long-deferred roadmap items:
"You vs Community" (Stats v2 spec §non-goals, roadmap Insights pitch), onboarding.md §9 "Chapter/volume
alignment", and Metadata Model Phase 7 (deferred 2026-08-18).

## Decisions (from the grilling)

| # | Decision |
|---|---|
| Order | 1. Alignment, 2. You vs Community, 3. Phase 7 (last, scoped by what alignment needs). |
| Phase 7 | **Is built** (user answered yes to Q2), but sequenced last and shaped by alignment's real needs — not the abstract source-doc schema. |
| Alignment scope | Manga only, source MangaDex `/manga/{id}/aggregate`. Read-only: flags mismatches, never edits `Number`/`Volume`. |
| Mismatch surface | Library Health (existing review surface) + an inline "missing chapters" list on the manga Detail screen. |
| You vs Community | User `Series.Rating` vs mean of that series' external ratings, both on 0–100. Series with both only. |
| Comics | Series score is aggregated from `Issue.CommunityRating` weighted by `CommunityRatingCount` (Metron, 1–5 → ×20). No new `ExternalMetadataProvider` value, no migration. ComicVine reports no score — documented gap, no adapter. |
| Scrape default | `ScrapeField.CommunityRating` becomes **on by default** (user answered yes to Q10; see "Interpretation notes"). |
| Backfill | One-off, rate-limited background refresh of linked series that lack a rating (Q7). |

## Verified facts (code, 2026-10-26)

- `ExternalRating` (`Entities/ExternalRating.cs`): SeriesId, Provider, Value, Scale, `VoteCount?`, RetrievedAt. Written by
  `MangaProviderApply.ApplyRating` (`Metadata/Manga/MangaProviderApply.cs:156`), Scale always 100; `VoteCount` never set.
- AniList (`AniListNormalizer.cs:44`), MangaBaka (`MangaBakaMetadataProvider.cs:309`, pass-through), MangaDex
  (`MangaDexMetadataProvider.cs:92`, bayesian ×10) all feed `ExternalMediaMetadata.Rating` on 0–100.
- Metron issue ratings: `MetronClient.cs:681` → `Issue.CommunityRating` (float, 0–5) + `CommunityRatingCount`, gated by
  `ScrapeField.CommunityRating`, currently excluded from defaults (`ScrapeSettings.cs:83`, asserted in `ScrapeStateTests.cs:20`).
- Comic series link to providers via `Issue.Volume` + `Issue.MetadataSource`, not `ExternalMediaId`.
- Insights cards: `FooResolver.Build(context)` in `Data/Metadata`, `LazySlot` + `LoadSlotAsync` in
  `StatsScreenViewModel.Insights.cs`, card in the Insights axaml (plain inline `Border.card`, not `StatCard`).
- `MangaDexMetadataProvider` leaves `ChapterCount`/`VolumeCount` null; `MangaDexTrackerAdapter.cs:33` already anticipates `/aggregate`.

## To verify before/during build (not yet confirmed)

1. MangaDex `/aggregate` live response shape (expected `volumes → {vol: {count, chapters: {ch: {chapter, id, others[], count}}}}`, a `"none"` volume bucket) and language/group filter params.
2. MangaBaka `rating` scale (assumed 0–100) and whether the API exposes a vote count we don't parse.
3. Whether Metron's issue *list* endpoint returns `average_rating` (else it costs one detail request per issue).
4. CE parity (standing rule): CE has no ComicVine scraper and nothing for provider ratings; `ComicBookSeriesStatistics.AverageCommunityRating` (per-series mean of books with rating > 0) is the one CE precedent. The aggregate here must stay consistent with it when only one provider contributes.

## Part 1 — Chapter/volume alignment

**Goal:** for a manga series linked to MangaDex, compare MangaDex's chapter/volume list with the library's `Issue.Number`/`Volume` and surface differences.

- **Fetch:** `MangaDexAggregateClient` calls `/manga/{id}/aggregate` (language filter from the user's preferred language setting, falling back to all). Result cached on the series' `ExternalMetadataSnapshot` pattern (reuse, don't add a new cache), refreshed with the normal provider refresh.
- **Counts:** also fills `ChapterCount`/`VolumeCount` on the snapshot (currently null for MangaDex).
- **Compare:** `ChapterAlignmentResolver.Build(context, seriesId)` returns `AlignmentReport`: matched, **missing locally** (in MangaDex, not in library), **extra locally** (in library, not in MangaDex), **volume conflicts** (same number, different volume). Numbers compared with the existing `NumberSortKey()` parsing so `10.5`, `Ch. 12` style variants match.
- **Never silent:** nothing is skipped or auto-corrected. Mismatches are listed for manual review (onboarding.md §9).
- **UI:**
  - Library Health gets an "Alignment" group: one row per series with N mismatches, opening the series' detail. Dismissable rows follow the existing dismissed-rows mechanism.
  - Manga Detail screen gets a collapsible "Missing chapters" list on the chapters tab (uses `ChapterVolumeGroup`), plus a small badge showing `have/total`.
  - UI follows `paperbunkr-design-language`: `Border.card`, `pbTextSection`/`pbTextMeta`, `countPill`, FluentIcons, tokens only, `Reveal.IsOpen` for the collapse, 1366×768 minimum.
- **Out of scope:** per-chapter downloading/acquisition, writing back to files, non-MangaDex sources.

## Part 2 — Insights "You vs Community"

**Data (`CommunityComparisonResolver.Build(context)`):**
- Per series: `UserScore = Series.Rating` normalized to 0–100 (its existing scale, verified at build). `CommunityScore`:
  - manga: mean of the series' `ExternalRating.Value / Scale × 100` across providers (plain mean; no vote counts exist).
  - comics: `Σ(Issue.CommunityRating × 20 × count) / Σ count` over issues with a rating > 0 (count defaults to 1 when absent).
  - both present → mean of the two; the card shows provider count.
- Include only series with `UserScore` and `CommunityScore`. Result: list of `CommunityComparisonRow` (series, user, community, delta, source count) plus coverage `N of M rated series have community data`.

**UI:** new card in the Insights screen's existing lazy group (`LazySlot` + `LoadSlotAsync`, reset on refresh):
- A scatter (user x, community y) with the y=x diagonal, and a ranked "biggest disagreements" list (top 5 each direction). Chart palette from the existing categorical tokens. Plain inline `Border.card`, `pbTextTitle` heading, `pbTextCaption` coverage line stating the ComicVine gap.
- Empty state when fewer than 3 comparable series: explains what's missing and offers the fetch action.
- **Fetch action:** "Fetch community ratings for N series" runs the backfill (below). Result lands in the Activity Center (project rule: use Activity Center for notifications).

**Backfill (Q7):** `CommunityRatingBackfillService` — manga: re-run the existing provider refresh for linked series with no `ExternalRating` row; comics: run the scraper for `ScrapeField.CommunityRating` only on rated series' issues. Rate-limited (Metron ≥20 req/min, 5,000/day; MangaDex/AniList per existing clients), cancellable, undoable through `ScrapeUndoStore` for the comic path.

**Also:** `MangaProviderApply.ApplyRating` fills `ExternalRating.VoteCount` for any provider that exposes it.

**Scrape default:** add `ScrapeField.CommunityRating` to the default field set; update `ScrapeStateTests` accordingly. Existing users' saved profiles keep their own selection.

## Part 3 — Metadata Model Phase 7

**Approach:** derive the minimum Volume/Chapter schema alignment actually needs, then decide what of the source doc's `Volume`/`Chapter`/`Story`/`Collection`/`CollectionContent` set to include. The source doc (`PAPERBUNKR_METADATA_MODEL.md`) is **not in the repo**, so the full original schema can't be checked; its shape is only known from references. Before building Part 3 we will ask you for that file or confirm we proceed from alignment's needs alone.

**Candidate entities (to be confirmed after Part 1 ships):**
- `SeriesVolume` / `SeriesChapter` (external, per provider): persisted result of `/aggregate`, replacing a snapshot blob if querying proves necessary.
- Local linkage: `Issue` ↔ chapter/volume mapping, enabling "this file is chapters 41–45" (collected-edition use case the source doc gates Phase 7 on).
- `Collection`/`CollectionContent` only if the collected-edition mapping can't be expressed with the above.

Gate: Part 3 gets its own short design addendum before any migration, because it adds schema. YAGNI applies — nothing beyond what Part 1's report and the collected-edition mapping need.

## Testing

- `ChapterAlignmentResolver`: matched/missing/extra/volume-conflict cases; decimal chapters; `"none"` volume bucket; empty library; empty MangaDex.
- `CommunityComparisonResolver`: scale normalization per source, weighting, single-provider equals CE's `AverageCommunityRating`, exclusions, coverage numbers.
- Scrape default change; backfill rate-limit and cancel; `VoteCount` filled.
- App: card load via `LazySlot`; lint suite (`XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`); render in dark/daylight/matrix; on-screen check by you (fast set only: `--filter "Speed!=Slow"` / targeted classes, one dotnet process at a time).

## Interpretation notes (please confirm)

- **Q2 "yes"** was read as "build Phase 7", sequenced last.
- **Q10 "yes"** was read as "turn `CommunityRating` on by default" (the question as asked), rather than the recommended "leave off + fetch action". The fetch action is kept regardless.
- Q8, Q9, Q11, Q12 follow the recommendations as stated.

## As built (2026-10-26)

Where the build differs from the text above:

- **Comics backfill:** not built. An issue does not store its Metron issue id, so there is nothing to fetch by; the comic side fills through ordinary scrapes now that `CommunityRating` defaults on.
  The "Fetch community ratings" action covers manga (AniList, MangaBaka, MangaDex) for series you have rated.
- **Rating scales:** your rating and Metron's are both 0-5 (not "your existing scale"), so both scale by 20. MangaBaka's `rating` is 0-100 (verified live) and exposes no vote count; MangaDex's vote count is the sum of its distribution.
- **Aggregate cache:** kept in an `ExternalMetadataSnapshot` (`SchemaVersion "aggregate-1"`); the Phase 7 addendum did not need to replace it.
- **Phase 7:** one entity, `CollectionContent` (issue id, from/to chapter as text and as a sort key, source `Manual`/`MangaDexVolume`). No `Volume`/`Chapter`/`Story` tables. "Map volumes to chapters" fills it from MangaDex's volume list.
  A range entered by hand (no editor yet) is never replaced or cleared by the automatic mapping.
- **Alignment unit:** a series whose files are numbered by volume (no local number matches a chapter, every one matches a MangaDex volume) is compared against volumes until it is mapped.
