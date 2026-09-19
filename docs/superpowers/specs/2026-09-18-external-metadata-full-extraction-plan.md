# External Metadata Full Extraction — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md*

Six phases, foundation first, cover pipeline second (priority ask). Each step names its files and
verification; steps within a phase are independent unless a "Depends on" is listed.

## Phase A — Foundation

### Step A1: `ExternalMediaMetadata` schema growth
**Files:** `src/Paperbunkr.Data/Metadata/IMetadataProvider.cs` (edit)
**What:** Add to the record: `CoverImageUrl` (`string?`), `Creator` (`string?`), `PublicationYear`
(`int?`), `PublicationFormat` (`string?`), `Demographic` (`string?`), `CrossReferences`
(`IReadOnlyList<(ExternalMetadataProvider Provider, string ExternalId)>?`), `GenreTags`
(`IReadOnlyList<string>?`), `OtherTags` (`IReadOnlyList<(string Value, string Category)>?`). Keep
existing `Genre` (`string?`) untouched — it stays the legacy carrier for `Series.Genre`'s existing
auto-apply path (§4 of the design doc is explicit this isn't touched); `GenreTags`/`OtherTags` are
new and separate, feeding `IssueTag` import only.
**Depends on:** none
**Verify:** compiles; existing `ExternalMediaMetadata` constructions across the 3 providers still
compile with new optional trailing parameters defaulted to null.

### Step A2: AniList query + DTO additions
**Files:** `src/Paperbunkr.Data/Metadata/AniListMetadataProvider.cs` (edit),
`src/Paperbunkr.Data/Metadata/AniListNormalizer.cs` (edit)
**What:** Add to `GetByIdQuery`: `coverImage{large}`, `staff{edges{role node{name{full}}}}`,
`startDate{year}`, `format`, `tags{name rank isMediaSpoiler}`,
`relations{edges{relationType node{id title{romaji english} siteUrl}}}` (relations used by Step E2,
fetched here since it's the same request). Extend `AniListMediaDto`/`AniListTitleDto` with matching
DTOs (`CoverImage`, `Staff`, `StartDate`, `Format`, `Tags`, `Relations`). In
`AniListNormalizer.ToMediaMetadata`: `CoverImageUrl` from `coverImage.large`; `Creator` = dedup
join of every `staff.edges[].node.name.full` regardless of role text (role is freeform, not
parsed); `PublicationYear` from `startDate.year`; `PublicationFormat` from `format`; `GenreTags`
from existing `genres` list; `OtherTags` from `tags` where `isMediaSpoiler == false`, each with
`Category = "Uncategorized"` (AniList tags carry no grouping).
**Depends on:** A1
**Verify:** unit tests in `src/Paperbunkr.Data.Tests/` (find the existing AniList normalizer test
file, likely alongside `AniListTrackerAdapterTests.cs`'s sibling) — one test per new field, plus a
spoiler-tag-excluded case.

### Step A3: MangaDex query + DTO additions
**Files:** `src/Paperbunkr.Data/Metadata/MangaDexMetadataProvider.cs` (edit)
**What:** Add `?includes[]=cover_art&includes[]=author&includes[]=artist` to the existing
`manga/{id}` `GetAsync` call (search stays unchanged — no relationships needed for a result list).
Add a `Relationships` array to `MangaDexMangaDto` (`type`, `id`, optional `attributes.fileName` for
cover_art, optional `attributes.name` for author/artist). In `MangaDexNormalizer.ToMediaMetadata`:
`CoverImageUrl` = `https://uploads.mangadex.org/covers/{mangaId}/{fileName}` from the `cover_art`
relationship; `Creator` = dedup join of `author`/`artist` relationship names; `PublicationYear`
from `attributes.year`; `Demographic` from `attributes.publicationDemographic`; `CrossReferences`
parsed from `attributes.links` (map MangaDex's `al`/`mal`/`mu`/`kt` keys to
`ExternalMetadataProvider.AniList`/`MyAnimeList`/`MangaUpdates`/`Kitsu`); `GenreTags` from existing
genre-group tag extraction (`ResolveGenre`, now returning a list instead of a joined string —
`ExternalMediaMetadata.Genre` still gets the joined-CSV form for back-compat); `OtherTags` from the
previously-discarded non-genre tag groups (theme/format/content), `Category` = the group name
title-cased.
**Depends on:** A1
**Verify:** unit tests in `src/Paperbunkr.Data.Tests/MangaDexMetadataProviderTests.cs` — one per
new field including the `links`-to-`CrossReferences` provider-key mapping and non-genre tag
categorization.

### Step A4: MangaBaka `v2` → `v1` migration
**Files:** `src/Paperbunkr.Data/Metadata/MangaBakaMetadataProvider.cs` (edit),
`src/Paperbunkr.Data.Tests/MangaBakaTrackerAdapterTests.cs` and any
`MangaBakaMetadataProvider`-specific test file (edit — check for live-shape assumptions)
**What:** Change `BaseUrl` from `.../v2/` to `.../v1/`. Confirm (via the live API — this provider's
own precedent, per its doc comment, is to verify shapes live rather than assume) whether
`series/search`/`series/{id}` response shape differs between `v1`/`v2`; adjust `MangaBakaSeriesDto`
if fields moved/renamed. `Creator` stays null (no staff field in either version, confirmed absent).
**Depends on:** A1
**Verify:** existing `MangaBakaMetadataProvider` tests still pass against `v1` fixtures; if fixture
JSON was captured from `v2`, recapture against `v1` live responses.

## Phase B — Cover pipeline (priority)

### Step B1: `TrySetCustomCoverFromBytes`
**Files:** `src/Paperbunkr.App/Services/CoverThumbnailService.cs` (edit)
**What:** Extract the resize/encode body shared by `TryGenerateThumbnail`/`TrySetCustomCover`
(scale-to-400px-longest-edge, JPEG-85 encode, cache-invalidate) into a private helper taking an
already-constructed `Bitmap`. Add `public bool TrySetCustomCoverFromBytes(int issueId, byte[]
imageBytes)`: constructs `new Bitmap(new MemoryStream(imageBytes))`, calls the shared helper,
same try/catch-false-on-failure contract as `TrySetCustomCover`.
**Depends on:** none
**Verify:** new unit test: valid JPEG bytes → file written + cache invalidated; garbage bytes →
returns false, no partial file left (assert `File.Exists` is false afterward).

### Step B2: `IMultiCoverProvider` + MangaBaka cover-archive call
**Files:** `src/Paperbunkr.Data/Metadata/IMetadataProvider.cs` (edit — add interface + record),
`src/Paperbunkr.Data/Metadata/MangaBakaMetadataProvider.cs` (edit)
**What:** New `public interface IMultiCoverProvider { Task<IReadOnlyList<CoverCandidate>>
GetCoverCandidatesAsync(string externalId, CancellationToken); }`, `public sealed record
CoverCandidate(string Url, string Type, string? Source)`. `MangaBakaMetadataProvider` implements
it via `GET /v1/series/{id}/images` (paginated — fetch first page only for this pass, matching
`v1`'s 180/min non-search rate limit already applied uniformly by this class).
**Depends on:** A4
**Verify:** unit test with a mocked multi-page response, confirms `CoverCandidate` list populates
with `Type`/`Source` from the documented `audiobook`/`banner`/`chapter`/`other`/`season`/`volume`/
`volume_back` enum.

### Step B3: In-memory remote-thumbnail cache for candidates
**Files:** `src/Paperbunkr.App/Services/ProviderCoverCandidateCache.cs` (new)
**What:** Mirrors `ArcCoverImageCache.DownloadAndCacheAsync`'s HTTP-GET-and-decode approach
(`ArcCoverImageCache.cs:60-76`), but keyed per-URL in an `LruCache<string, Bitmap>`, memory-only —
no disk write, since these are transient browse candidates, not a persisted cover. One static
method: `Task<Bitmap?> FetchAsync(string url, CancellationToken)`.
**Depends on:** none
**Verify:** unit test — successful fetch caches and returns a `Bitmap`; failed fetch (bad URL/
network error) returns null, matches `ArcCoverImageCache`'s own error-swallowing contract.

### Step B4: `CoverPickerViewModel` "From External Provider" tab
**Files:** `src/Paperbunkr.App/ViewModels/CoverPickerViewModel.cs` (edit),
`src/Paperbunkr.App/Views/CoverPickerView.axaml` (edit)
**What:** 4th tab, `SelectedTabIndex == 3`, `IsExternalProviderTabActive`. Constructor gains an
optional `(ExternalMetadataProvider Provider, string ExternalId)?` parameter — when non-null and
the resolved provider implements `IMultiCoverProvider`, `Load` calls `GetCoverCandidatesAsync` and
populates a new `ObservableCollection<ProviderCoverCandidate>` (record: `Url`, `Type`, `Source`),
thumbnails fetched via `ProviderCoverCandidateCache.FetchAsync`. New `SelectExternalCandidate`
command: downloads the full-res bytes (same URL, no separate "thumbnail vs full" distinction from
MangaBaka's API) and calls `CoverThumbnailService.TrySetCustomCoverFromBytes(_targetIssueId,
bytes)`, then `_onApplied()`. Tab only shows when the optional parameter is supplied and non-empty
(AniList/MangaDex never populate it — they use the direct Apply-cover action in B5 instead).
**Depends on:** B1, B2, B3
**Verify:** ViewModel test — candidates populate from a fake `IMultiCoverProvider`, selecting one
calls `TrySetCustomCoverFromBytes` exactly once with the fetched bytes.

### Step B5: "Apply cover" action for AniList/MangaDex
**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit),
`src/Paperbunkr.App/Views/DetailTabs.axaml` (edit)
**What:** New `[RelayCommand] ApplyCoverAsync` on the External Metadata tab, enabled when the
currently-fetched/linked provider metadata has a non-null `CoverImageUrl` AND
`series.CoverIssueId` is set (disabled + tooltip "Set a series cover issue first" otherwise, per
the design's explicit no-guessing rule). Downloads the URL via a plain `HttpClient.GetByteArrayAsync`
and calls `CoverThumbnailService.TrySetCustomCoverFromBytes(series.CoverIssueId.Value, bytes)`.
For MangaBaka matches, this button instead opens the `CoverPickerView` flyout pre-loaded into the
new External Provider tab (Step B4) rather than auto-applying.
**Depends on:** B1, B4
**Verify:** on-screen check (this project's UI-automation harness): link a real AniList/MangaDex
series with a `CoverIssueId` set, click Apply cover, confirm the cover updates in the Library grid;
link MangaBaka, confirm the picker opens with real candidate thumbnails.

## Phase C — Creator field

### Step C1: `Series.Creator` schema
**Files:** `src/Paperbunkr.Data/Entities/Series.cs` (edit),
`src/Paperbunkr.Data/Entities/MetadataProposalField.cs` (edit — add `Creator` member),
new EF migration under `src/Paperbunkr.Data/Migrations/` (`dotnet ef migrations add
AddSeriesCreator`, following the existing `AddEmptyRowFlags`-style naming/Designer-pair pattern),
`src/Paperbunkr.Data/Migrations/PaperbunkrDbContextModelSnapshot.cs` (regenerated by the migration
tool, not hand-edited)
**What:** `public string? Creator { get; set; }` on `Series`, no backfill (new field, nothing to
migrate from). Add `Creator` to `MetadataProposalField` enum with a doc comment mirroring
`Genre`'s ("Series-scoped — writes directly to `Series.Creator`").
**Depends on:** none
**Verify:** migration applies cleanly on a copy of a real dev DB; `dotnet ef migrations add` then
`dotnet build` (per this project's own AVLN2000-adjacent gotcha in `CLAUDE.md` — not relevant to a
non-XAML entity change, but confirm `dotnet build` still reports the migration compiling).

### Step C2: Wire `Creator` into `MetadataLinkResolver`
**Files:** `src/Paperbunkr.Data/Metadata/MetadataLinkResolver.cs` (edit)
**What:** Add `ProposeAndApply(context, series, MetadataProposalField.Creator, series.Creator,
metadata.Creator, provider.ProviderKey);` alongside the existing Summary/Status/Genre calls in
`LinkAsync`. Add the matching `case MetadataProposalField.Creator: series.Creator = providedValue;
break;` to the `switch` in `ProposeAndApply`.
**Depends on:** C1, A1 (needs `metadata.Creator`)
**Verify:** unit test in `MetadataLinkResolverTests`-equivalent (find/extend existing
`MetadataLinkResolver` test coverage) — linking a fake provider with `Creator` set writes it to
`series.Creator`; a provider returning null `Creator` (MangaBaka) leaves an existing value
untouched (existing no-op-on-empty behavior, unchanged).

## Phase D — Tag import

### Step D1: Categorized merge helper
**Files:** `src/Paperbunkr.Data/Entities/IssueTagExtensions.cs` (edit)
**What:** New `public static void MergeFromCategorized(this Issue issue, IssueTagField field,
IEnumerable<(string Value, string Category)> incoming)` — same diff-not-replace shape as the
existing `MergeFrom` (remove tags whose value is no longer present, leave surviving tags'
Category/Weight untouched), but newly-added tags take their `Category` from the incoming tuple
instead of a single blanket default. `Weight` stays `IssueTagWeight.Unset` on every new add, same
as `MergeFrom`.
**Depends on:** none
**Verify:** unit test mirroring the existing `MergeFrom` test pattern — new categorized value
added with its given category; a value's category from a second import call does NOT retroactively
change a tag that survived from the first call (category is a create-time-only assignment, same
"never touched again" rule as `MergeFrom`'s existing values).

### Step D2: Per-series tag import orchestration
**Files:** `src/Paperbunkr.Data/Metadata/ExternalTagImportResolver.cs` (new)
**What:** `public static void ApplyToSeries(PaperbunkrDbContext context, Series series,
ExternalMediaMetadata metadata)`: loads every `Issue` for `series.Id` (with `.Tags` included), and
for each issue calls `issue.MergeFromCategorized(IssueTagField.Genre, metadata.GenreTags.Select(g
=> (g, "Genre")))` and `issue.MergeFromCategorized(IssueTagField.Tags, metadata.OtherTags)` when
either list is non-null/non-empty. Caller is responsible for `SaveChanges()`.
**Depends on:** D1, A1
**Verify:** unit test — a 3-issue series, provider metadata with 2 genre tags + 1 categorized
tag, confirms all 3 issues get identical `IssueTag` rows; a pre-existing hand-set `Weight=Core` tag
on one issue survives untouched when the same value is re-sent.

### Step D3: Wire tag import into the Apply flow
**Files:** `src/Paperbunkr.Data/Metadata/MetadataLinkResolver.cs` (edit)
**What:** Call `ExternalTagImportResolver.ApplyToSeries(context, series, metadata)` in `LinkAsync`,
right after the existing `ProposeAndApply` calls, before `context.SaveChanges()`. This covers
AniList/MangaDex (tags already present on the single `GetAsync` response). MangaBaka's richer
`/v1/tags`-tree-derived categorization is out of scope for the immediate-Apply path — its
`GenreTags`/`OtherTags` from the plain `v1` `series/{id}` response (just `is_genre`/`is_spoiler`
flags, no category name) still flow through this same call, just with `Category = "Uncategorized"`
for every non-genre MangaBaka tag until/unless a future pass adds the `/v1/tags` tree lookup.
**Depends on:** D2, C2 (same method, sequenced after)
**Verify:** on-screen check — link a real AniList or MangaDex series, confirm every issue's Tags
pill row shows the imported values with correct Category grouping.

## Phase E — Relations

### Step E1: `ExternalMediaRelation` entity
**Files:** `src/Paperbunkr.Data/Entities/ExternalMediaRelation.cs` (new),
`src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit — add `DbSet<ExternalMediaRelation>
ExternalMediaRelations => Set<ExternalMediaRelation>();` near the existing `ExternalMediaIds`/
`MediaRelations` declarations), new EF migration (`AddExternalMediaRelation`)
**What:** `Id`, `SourceSeriesId` (FK `Series`), `Provider` (`ExternalMetadataProvider`),
`TargetExternalId` (`string`), `TargetTitle` (`string`), `TargetUrl` (`string?`), `RelationType`
(`RelationType`), `CreatedAt` (`DateTime`, default `UtcNow`, matching `MediaRelation`'s own
convention).
**Depends on:** none
**Verify:** migration applies cleanly; entity round-trips through EF in a basic add/query test.

### Step E2: Provider relation extraction (lazy)
**Files:** `src/Paperbunkr.Data/Metadata/IMetadataProvider.cs` (edit — add `IRelationsProvider`
interface), `src/Paperbunkr.Data/Metadata/AniListMetadataProvider.cs` (edit — relations already
fetched by Step A2's query, just needs normalizing), `src/Paperbunkr.Data/Metadata/
MangaBakaMetadataProvider.cs` (edit — new `GET /v1/series/{id}/relationships` call),
`src/Paperbunkr.Data/Metadata/RelationTypeCatalog.cs` (edit — add the two provider-to-`RelationType`
lookup tables)
**What:** `public interface IRelationsProvider { Task<IReadOnlyList<ProviderRelation>>
GetRelationsAsync(string externalId, CancellationToken); }`, `public sealed record
ProviderRelation(string TargetExternalId, string TargetTitle, string? TargetUrl, RelationType
Type)`. AniList's 13-ish `relationType` enum and MangaBaka's 22-value enum each map via a
`Dictionary<string, RelationType>` literal, falling back to `RelationType.Other` for anything
unmapped. MangaDex: no implementation (no confirmed endpoint, per the design doc's explicit
exclusion).
**Depends on:** E1
**Verify:** unit tests per provider — known relation-type strings map correctly; an unrecognized
value falls back to `Other` without throwing.

### Step E3: Persist + auto-upgrade
**Files:** `src/Paperbunkr.Data/Metadata/MetadataLinkResolver.cs` (edit)
**What:** Two additions to `LinkAsync`, after the `ExternalMediaId` upsert block:
1. If `provider` implements `IRelationsProvider`, fetch relations (lazy — only called when the
   Related tab itself triggers a re-link/refresh, not on the default single-field Apply; see
   Step E4's UI wiring for where this actually gets invoked) and upsert `ExternalMediaRelation`
   rows for `(SourceSeriesId, Provider, TargetExternalId)` not already present.
2. Auto-upgrade: query `ExternalMediaRelations` where `(Provider, TargetExternalId) ==
   (provider.ProviderKey, metadata.ExternalId)` — i.e., some other series in the library already
   has a placeholder pointing at *this* series' just-linked external id. For each match, create a
   `MediaRelation(SourceSeriesId: match.SourceSeriesId, TargetSeriesId: seriesId, RelationType:
   match.RelationType)` and remove the `ExternalMediaRelation` row.
**Depends on:** E1, E2
**Verify:** unit test — linking series B (which has an `ExternalMediaRelation` placeholder from
series A pointing at B's external id) creates a real `MediaRelation` A→B and deletes the
placeholder; linking with no matching placeholder is a no-op.

### Step E4: Related-tab UI merge
**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit — locate the existing
Related-tab-backing collection/property), `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit)
**What:** Related tab's item list gains `ExternalMediaRelation` rows alongside real
`MediaRelation` rows, rendered with a "Not in library" badge and dimmed styling, no click-through.
Opening the tab (first time per series per session, or via explicit refresh) triggers the
Step E3-#1 fetch for any linked provider implementing `IRelationsProvider` — this is the "lazy"
trigger point named in the design doc's §7.
**Depends on:** E3
**Verify:** on-screen check — link a real AniList series with known relations where the target
isn't in the library, confirm it shows dimmed/badged in Related; add that target series and
re-link/refresh, confirm the placeholder disappears and a real relation row appears.

## Phase F — Cross-reference auto-linking + remaining lazy wiring

### Step F1: Cross-reference auto-link
**Files:** `src/Paperbunkr.Data/Metadata/MetadataLinkResolver.cs` (edit)
**What:** In `LinkAsync`, after the primary `ExternalMediaId` upsert, loop
`metadata.CrossReferences` — for each `(Provider, ExternalId)`, check
`context.ExternalMediaIds.FirstOrDefault(e => e.SeriesId == seriesId && e.Provider ==
crossRef.Provider)`. If none exists, insert a new row (`LastFetchedAt = null`, since it's asserted,
not yet independently fetched). If one exists with a *different* `ExternalId`, leave it untouched
— do not log/flag beyond what Needs-Review already surfaces for other identity conflicts (no new
UI surface for this pass, per the design doc's explicit scope).
**Depends on:** A1 (needs `CrossReferences`)
**Verify:** unit test — no existing row → inserts; existing matching row → no duplicate; existing
differing row → untouched, verified via before/after equality check on that row's `ExternalId`.

### Step F2: Confirm lazy-fetch wiring is consistent
**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit, if not already covered by
B4/E4's own wiring)
**What:** Audit that the only *eager* work on a default "Link"/Apply action is the single
`GetAsync` call (Steps A2-A4 fields) plus the immediate Series-scoped writes (C2, D3, F1) — all of
which come from that one response, no extra request. Confirm Covers (B4), Relations (E4), and any
MangaBaka tag-tree lookup (noted as future work in D3) are the only per-tab-triggered fetches, and
that opening a tab twice in one session doesn't re-fetch (cache the result for the session, same
lifetime as the existing `MetadataSearchResults` collection).
**Depends on:** B4, E4
**Verify:** manual/on-screen — open External Metadata tab and Apply, confirm exactly one network
call fires (checked via existing rate-limiter instrumentation or a debugger breakpoint); open
Related and Covers tabs, confirm one additional call each, not repeated on a second tab-switch.

## Cross-cutting note

The working tree has another concurrent session active in it (confirmed this session — a
`git commit` earlier this session accidentally bundled unrelated staged files from it). Before any
multi-file `git add`/`git commit` during this implementation, run `git status` and stage only the
exact paths this plan's current step touches — never a broad `git add -A`/`git add .`.
