# You vs Community, Chapter/Volume Alignment, Phase 7 — Implementation Plan
**Status (2026-10-26): all steps built and tested; on-screen check pending. Deviations are listed under "As built" in the design doc.**
*Implements: docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md*

Order is fixed by the design: **A. Alignment → B. You vs Community → C. Phase 7**. A and B share no code and
could run in parallel; C depends on A. Test conventions: xUnit, no live network in tests (fake `HttpMessageHandler`
as in `MangaDexMetadataProviderTests`), App tests via `--filter "Speed!=Slow"` / targeted classes, **one
`dotnet` process at a time**, kill build-server afterwards. Every UI step: read `paperbunkr-design-language`, then
`avalonia-pro-max/review-checklist` off disk, then ask the user to look on screen.

Facts the survey corrected in the design: your rating (`Issue.Rating`, `Series.Rating`) and Metron's community
rating are **both 0–5** (`Series.cs:126`, `Issue.cs:268-270`), so both normalize ×20. Enabling
`ScrapeField.CommunityRating` by default **deviates from CE** (`update_rating_b` defaults off,
`ScrapeSettings.cs:76-83`) — record it as a deliberate deviation in the doc comment.

## Part A — Chapter/volume alignment

### A0: Verify the MangaDex `/aggregate` shape (blocking A1)
**Files:** none (scratch script in scratchpad)
**What:** one real GET of `/manga/{id}/aggregate` (and the language/group params) for a linked series; save the JSON as a test fixture.
**Depends on:** none
**Verify:** fixture `Paperbunkr.Data.Tests/Fixtures/mangadex-aggregate.json` committed; shape matches the DTO in A1 (note the `"none"` volume bucket, `others[]`).
**Status (2026-10-26): shape verified live** (One Piece, `translatedLanguage[]=en`): `{result, volumes:{<vol|"none">:{volume, count, chapters:{<ch>:{chapter, id, isUnavailable, others[], count}}}}}`. New field vs. the design's guess: `isUnavailable`. Caveat for A1: with the `en` filter only 2 volumes came back (`none` and `1`) for a 1,000+ chapter series, because MangaDex volume numbers come from the uploaded scanlation groups, not the publisher. So "volume conflict" findings will be noisy; treat chapter numbers as the reliable key and volume mismatches as low-confidence/advisory. Fixture still to be saved into `Data.Tests` in A1.

### A1: Aggregate client + report model
**Files:** `Paperbunkr.Data/Metadata/MangaDexAggregateClient.cs` (new, reuses `MangaDexHttpClient.Shared`), `Paperbunkr.Data/Metadata/ChapterAlignmentResolver.cs` (new; `AlignmentReport` record: matched / missingLocally / extraLocally / volumeConflicts), `Paperbunkr.Data.Tests/ChapterAlignmentResolverTests.cs`, `Paperbunkr.Data.Tests/MangaDexAggregateClientTests.cs` (new)
**What:** parse aggregate into `(volume?, chapter)` entries; compare against the series' `Issue.Number`/`Volume` using `NumberSortKey()`/`VolumeSortKey()` so `10.5` and `Ch. 12` variants match. Never mutates issues. Chapter/volume totals fill `ChapterCount`/`VolumeCount` on `ExternalMediaMetadata` (currently null, `MangaDexMetadataProvider.cs:319`).
**Depends on:** A0
**Verify:** tests — matched, missing, extra, volume conflict, decimal chapters, `"none"` bucket, empty library, empty MangaDex, 429/outage → `MetadataProviderUnavailableException`.

### A2: Cache the aggregate result
**Files:** `Paperbunkr.Data/Metadata/MangaDexAggregateClient.cs` (edit), `ExternalMetadataResolver.cs` (edit)
**What:** store the raw aggregate in an `ExternalMetadataSnapshot` (provider `MangaDex`, `SchemaVersion = "aggregate-1"`), refreshed by the normal provider refresh. Note: `ExternalMetadataSnapshot`'s own doc says nothing reads `Payload` as the normal model — this step reads only rows with the `aggregate-1` schema version through one typed accessor; Part C replaces it with real tables if querying demands it. No migration.
**Depends on:** A1
**Verify:** test that a second report within the cache window makes no HTTP call; stale snapshot refetches.

### A3: Library Health "Alignment" group
**Files:** `Paperbunkr.App/Services/LibraryHealthService.cs` (edit), `ViewModels/LibraryHealthSections.cs` (edit), `Models/LibraryHealthTab.cs` (check), `Views/` Library Health axaml (edit; locate via `LibraryHealthSections` anchors), `App.Tests/LibraryHealthFindingsTests.cs` (edit)
**What:** one row per manga series with ≥1 mismatch, opens the series Detail; dismiss uses the existing dismissed-rows mechanism. Computed lazily so a library with no MangaDex links costs nothing.
**Depends on:** A1–A2
**Verify:** `LibraryHealthFindingsTests`/`LibraryHealthServiceTests` extended; lint trio (`XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`); on-screen.

### A4: Manga Detail "Missing chapters"
**Files:** `ViewModels/MangaDetailScreenViewModel.cs` (edit, near `RenderChapters`), `Views/` manga detail axaml (edit), `App.Tests/MangaDetailScreenViewModelTests.cs` (edit)
**What:** collapsible list (`Reveal.IsOpen`) of missing chapters grouped by volume with a `have/total` `countPill`; defer any collection mutation one dispatcher tick (CLAUDE.md routed-event rule).
**Depends on:** A1–A2
**Verify:** VM tests for grouping/empty state; lint trio; render dark/daylight/matrix; on-screen at 1366×768.

## Part B — Insights "You vs Community"

### B1: Verify provider facts (blocking B2)
**Files:** none
**What:** confirm MangaBaka `rating` scale and any vote-count field; check whether Metron's issue *list* endpoint returns `average_rating`.
**Depends on:** none
**Verify:** findings noted at the top of B2's resolver doc comment; fix assumptions in the design doc if wrong.

### B2: `VoteCount` + scrape default
**Files:** `Paperbunkr.Data/Metadata/Manga/MangaProviderApply.cs` (edit `ApplyRating`), `MangaBakaMetadataProvider.cs`/`AniListNormalizer.cs`/`MangaDexMetadataProvider.cs` + `ExternalMediaMetadata` (add `RatingVoteCount?` only where a provider exposes it), `ComicVine/Scraping/ScrapeSettings.cs` (edit line 82-83, update doc comment: deliberate CE deviation), `App.Tests/ScrapeStateTests.cs` + `Data.Tests/MangaFullScrapeTests.cs` (edit)
**What:** fill `ExternalRating.VoteCount`; include `CommunityRating` in default fields. Do **not** bump `FieldSetVersion`, so existing saved profiles keep their choice.
**Depends on:** B1
**Verify:** updated `ScrapeStateTests` default assertion; manga apply test asserts VoteCount; existing saved-profile load test still passes.

### B3: `CommunityComparisonResolver`
**Files:** `Paperbunkr.Data/Metadata/CommunityComparisonResolver.cs` (new; `CommunityComparisonRow`, coverage record), `Paperbunkr.Data.Tests/CommunityComparisonResolverTests.cs` (new)
**What:** per series, user = `Series.Rating × 20`; community = mean of `ExternalRating.Value/Scale×100` (manga) and `Σ(CommunityRating×20×count)/Σcount` (comics, count defaults 1); both present → mean; include only series with both sides; coverage `N of M`.
**Depends on:** B1
**Verify:** normalization per source, weighting, single provider equals CE's `ComicBookSeriesStatistics.AverageCommunityRating` (`Engine/ComicBookSeriesStatistics.cs:164`), exclusions, coverage numbers, zero-rating rows ignored.

### B4: Insights card
**Files:** `App/ViewModels/StatsScreenViewModel.Insights.cs` (edit: `_communitySlot`, `EnsureGroupLoaded`, reset ~refresh), `ViewModels/InsightsScreenViewModel.cs` (edit if properties live there), `Views/InsightsScreen.axaml` (+ `.cs` only if needed), `App.Tests/InsightsScreenViewModelTests.cs` (edit)
**What:** `Border.card` with scatter + y=x line, top-5 disagreements each way, coverage line naming the ComicVine gap, empty state under 3 comparable series. Chart colours from existing categorical tokens; no `StatCard` (blank-render bug, roadmap line 2269).
**Depends on:** B3
**Verify:** VM test (slot loads once, resets on refresh, empty state); lint trio; render all three themes; on-screen.

### B5: Backfill service + card action
**Files:** `Paperbunkr.App/Services/CommunityRatingBackfillService.cs` (new), `StatsScreenViewModel.Insights.cs` (command), Activity Center registration (find an existing job like the tracker/scrape jobs and mirror it), `App.Tests/CommunityRatingBackfillServiceTests.cs` (new)
**What:** manga — refresh linked series lacking an `ExternalRating`; comics — scrape `ScrapeField.CommunityRating` only for rated series' issues (undoable via `ScrapeUndoStore`). Rate-limited (Metron ≥20/min, 5,000/day), cancellable, progress and result in the Activity Center. Command removal/collection reloads deferred one dispatcher tick.
**Depends on:** B2, B4
**Verify:** tests with fake providers: skips already-rated, honours cancel, stops on 429 and reports, undo restores; on-screen run on a small slice (not the whole library).

## Part C — Metadata Model Phase 7 (gated)

### C0: Source doc + addendum (gate)
**Files:** `docs/superpowers/specs/2026-10-26-phase7-volume-chapter-addendum-design.md` (new)
**What:** after A ships, check what alignment actually needed. Ask the user for `PAPERBUNKR_METADATA_MODEL.md` (absent from the repo). Write a short addendum choosing the minimum entities (`SeriesVolume`/`SeriesChapter` external rows, issue↔chapter-range mapping, `Collection`/`CollectionContent` only if unavoidable). **No code or migration until the user approves the addendum.**
**Depends on:** A4 (user-verified)
**Verify:** user review.

### C1+ (outlined, detailed after C0)
Entities + one migration (mirror `20260926100953_AddLibraryHealthTab` and its `…MigrationTests`; remember the stale-Designer-snapshot and up-down-up test pitfalls in memory), replace the A2 snapshot accessor, issue↔chapter mapping + collected-edition UI. Files and steps are finalized in the addendum.

## Cross-cutting checks
- After each part: `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~<new classes>"`, then `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~<touched classes>"`; the full fast set only once at the end (memory: no wide App test runs).
- Update `docs/paperbunkr-todo.md` (and the roadmap's "You vs Community" / "chapter/volume alignment" / Phase 7 entries, now stale) by hand with commit refs and what was verified on screen.
- Verified-on-screen items only once the user has looked; unverified items are reported as such.
