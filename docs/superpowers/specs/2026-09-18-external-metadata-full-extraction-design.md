# External Metadata Full Extraction: Covers, Creator, Tags, Relations

**Date:** 2026-09-18
**Status:** Approved, pending implementation plan
**Source:** User-requested expansion of the three external-metadata connectors (AniList, MangaBaka,
MangaDex) beyond today's thin `ExternalMediaMetadata` (Title/Description/Status/ChapterCount/
VolumeCount/Genre only), with cover images called out as the priority. Reached via a `/grilling`
pass per `CLAUDE.md`'s brainstorming-replacement rule; the design tree surfaced two premises from
`docs/mangabaka-metadata-ui-research.md` that had gone stale since it was written — the
categorized/weighted tag model it called future work already shipped
(`docs/superpowers/specs/2026-08-23-weighted-categorized-tags-design.md`), and `MediaRelation`
cannot hold an unresolved external reference — both corrected mid-session before settling the
tree below.

## Context

Today's three providers (`src/Paperbunkr.Data/Metadata/AniListMetadataProvider.cs`,
`MangaBakaMetadataProvider.cs`, `MangaDexMetadataProvider.cs`) each implement `IMetadataProvider`
and return a thin `ExternalMediaMetadata` record (`IMetadataProvider.cs:60-71`): title variants,
description, status, chapter/volume counts, and a flat genre string. `MetadataLinkResolver.LinkAsync`
(`src/Paperbunkr.Data/Metadata/MetadataLinkResolver.cs:59-114`) auto-accepts and writes Series-scoped
fields (`Summary`, `Status`, `Genre`) immediately; Issue-scoped changes go through the separate
review-queue (`MetadataProposal`) path instead, per `2026-08-23-apply-from-provider-design.md`'s
"auto-accept-and-overwrite for Series, review-queue for Issue" split.

Real gaps against what each provider actually exposes (full inventory gathered this session):
AniList's GraphQL schema has `coverImage`, `staff`, ranked/spoiler-flagged `tags`, `format`,
`startDate`, and more that the current query never asks for. MangaBaka's provider calls the beta
`v2` API family, which lacks covers, relations, and the richer tag taxonomy entirely — `v1`
(stable) has all of it. MangaDex's provider never requests the `relationships` include, so cover
art and author/artist data sit unread even though a single query parameter would surface them.

Three pieces of existing infrastructure turn out to be directly relevant once fetched:
- `Issue.Tags` is `List<IssueTag>` (`Field`/`Category`/`Weight`), not a flat CSV — shipped
  2026-08-23, coincidentally already using MangaBaka's own weight vocabulary
  (`Incidental`/`Recurrent`/`Defining`/`Core`).
- `CoverPickerViewModel`/`CoverPickerView` (from the 2026-09-17/18 Reader Save-Page-As work) is a
  tab/grid/`SelectCandidate` picker over local on-disk covers — the right UI shape to extend for
  MangaBaka's multi-cover archive, not something to duplicate.
- `ArcCoverImageCache` (`src/Paperbunkr.App/Services/ArcCoverImageCache.cs`) already proves the
  URL-download-and-cache pattern this needs, for reading-list covers — a single-slot sibling to
  mirror, not reuse directly (it's keyed one-per-`ReadingListId`, not multi-candidate).

## Scope

### 1. `ExternalMediaMetadata` schema growth

New fields on the record (`IMetadataProvider.cs`):

| Field | Type | Populated by |
|---|---|---|
| `CoverImageUrl` | `string?` | AniList (`coverImage.large`), MangaDex (cover_art relationship → `uploads.mangadex.org/covers/{mangaId}/{filename}`). Null for MangaBaka (handled separately, see §2). |
| `Creator` | `string?` | See §3. |
| `PublicationYear` | `int?` | AniList (`startDate.year`), MangaDex (`attributes.year`), MangaBaka (no confirmed field — left null). |
| `PublicationFormat` | `string?` | AniList (`format`: MANGA/NOVEL/ONE_SHOT/...), MangaDex (none confirmed). Named distinctly from `Issue.Format` (ComicInfo.xml physical/digital edition format, `Issue.cs:114`) — different concept, same word would collide. |
| `Demographic` | `string?` | MangaDex (`attributes.publicationDemographic`). AniList/MangaBaka have no equivalent. |
| `CrossReferences` | `IReadOnlyList<(ExternalMetadataProvider Provider, string ExternalId)>?` | MangaDex `attributes.links` (al/mal/mu/kt keys), MangaBaka `/v1/source/*` results when queried. AniList has no outbound cross-reference field. |

`Genre` stays as-is on the record for now (provider genre lists still land there as the interim
carrier) but downstream handling changes — see §4.

### 2. Cover images

**AniList / MangaDex (single cover):** `GetAsync` already returns `CoverImageUrl` per the table
above — no extra request needed for AniList (`coverImage` is on the existing get-by-id query) or
MangaDex (needs `?includes[]=cover_art` added to the existing `manga/{id}` call). A new **"Apply
cover"** action on the External Metadata tab downloads the URL and calls a new
`CoverThumbnailService.TrySetCustomCoverFromBytes(int issueId, byte[] imageBytes)` overload
(mirrors `TrySetCustomCover`'s existing decode/resize-to-400px/JPEG-85/cache-invalidate pipeline,
`CoverThumbnailService.cs:132-162`, just sourced from a byte array instead of a local file path).
Target issue is the series' `CoverIssueId` (`Series.cs:67`); if unset, the action is disabled with
an explanatory tooltip rather than guessing an issue. This bypasses `MetadataProposal`/review-queue
entirely — a cover pick is a binary choice the click itself confirms, not an ambiguous field
needing arbitration.

**MangaBaka (multi-cover archive, `/v1/series/{id}/images`):** a new optional interface,
`IMultiCoverProvider` (`GetCoverCandidatesAsync(string externalId, CancellationToken) : Task<IReadOnlyList<CoverCandidate>>`,
`CoverCandidate` = `Url`/`Type`/`Source`), implemented only by `MangaBakaMetadataProvider` and
checked via an `as` cast at the call site — same additive-interface pattern `ITrackerSearchProvider`
already uses for AniList/MangaBaka dual conformance. `CoverPickerViewModel` gains a new "From
External Provider" tab: thumbnails are fetched to memory only (mirroring `ArcCoverImageCache`'s
`DownloadAndCacheAsync` HTTP-GET-and-decode approach, `ArcCoverImageCache.cs:60-76`, but keyed
per-candidate, not per-list, and never written to disk until picked). Selecting a candidate
downloads the full image and calls the same `TrySetCustomCoverFromBytes` overload as above.

### 3. Creator field

New `Series.Creator` (`string?`, nullable, no migration data to backfill). Single combined string,
comma-separated unique names, no role labels — matches this codebase's existing Genre/Teams/
Locations CSV convention (`ExternalMediaMetadata`'s own prior doc comment).

- **MangaDex:** clean `author`/`artist` relationships (needs `?includes[]=author&includes[]=artist`
  added to the existing query) — join their names, deduped.
- **AniList:** `staff.edges.role` is freeform community-edited text ("Story & Art", "Story",
  "Art", "Original Creator", ...), not an enum — extract every staff `node.name.full` regardless of
  the exact role string, dedupe. No attempt to split into separate story/art fields; the freeform
  role text can't support that split reliably (confirmed: no clean AniList role enum exists).
- **MangaBaka:** left null — no staff/author field confirmed in either `v1` or `v2` API (checked
  against the OpenAPI-derived research memo and the provider design doc; the "Staff/Publisher chip
  row" seen live is website-only presentation with no documented API backing).

`Creator` is Series-scoped, so it auto-applies immediately through the existing
`MetadataLinkResolver` pattern, same as `Summary`/`Status`/`Genre` today.

### 4. Tags (import into the existing `IssueTag` model)

Provider tag data is Series-scoped; `IssueTag` is Issue-scoped
(`src/Paperbunkr.Data/Entities/IssueTag.cs:36-52`). Resolution: **write the same tag set to every
current Issue in the series, overwriting existing `IssueTag` rows for values the provider re-sends**
(explicit user instruction — bulk, not diff-preserving, reuses the existing Bulk Issue Editing
write path rather than a new mechanism).

- **`Field`:** provider genre-taxonomy entries → `IssueTagField.Genre`; everything else →
  `IssueTagField.Tags`.
- **`Category`:** provider's own group name when it has one (MangaDex tag `attributes.group`:
  Theme/Format/Content), else `"Uncategorized"` — matches the existing migration-seed convention
  (`IssueTag.cs:48`).
- **`Weight`:** always `IssueTagWeight.Unset` on import. This is not a new rule — `Unset`'s own doc
  comment already states "never inferred on migration or import" (`IssueTag.cs:16-18`). Confirmed
  during research that MangaBaka's weight tiers aren't even documented as a per-series-instance API
  value anyway (only ever observed as a static taxonomy-wide filter facet) — there'd be nothing
  live to import even if the rule allowed it.
- **Spoiler handling:** AniList tags carrying `isMediaSpoiler: true` are silently skipped on
  import — no `IsSpoiler` column exists on `IssueTag` and none is added here (no schema slot to
  reuse; adding one is its own future increment if ever needed, not bundled into a provider-import
  pass).
- MangaDex genre-group tags (`attributes.group == "genre"`) already extracted today
  (`MangaDexNormalizer.ResolveGenre`, `MangaDexMetadataProvider.cs:262-271`) — non-genre groups
  (theme/format/content), currently filtered out entirely, now flow into `IssueTagField.Tags` with
  their group name as `Category`.

### 5. Relations

`MediaRelation` (`MediaRelation.cs:20-47`) can only link two rows already in the library — no slot
for an unresolved external target. New entity `ExternalMediaRelation`: `Id`, `SourceSeriesId` (FK
`Series`), `Provider` (`ExternalMetadataProvider`), `TargetExternalId` (`string`), `TargetTitle`
(`string`, cached display text), `TargetUrl` (`string?`), `RelationType` (reuses the existing
`RelationType` enum, `RelationType.cs:9-47`).

- **AniList** (`relations.edges{relationType, node{id, title, siteUrl}}`) and **MangaBaka**
  (`/v1/series/{id}/relationships`) both populate this table. MangaDex has no confirmed relations
  endpoint — excluded this pass; addable later if one exists.
- Provider relation-type values map to the existing `RelationType` enum via a best-effort lookup
  table (spelled out at implementation time, not enumerated here); anything with no clean match
  falls back to `RelationType.Other`.
- **Auto-upgrade:** whenever `MetadataLinkResolver` upserts an `ExternalMediaId` row for a Series
  (i.e., any series just got linked to a provider), check for `ExternalMediaRelation` rows whose
  `(Provider, TargetExternalId)` matches that same `(Provider, ExternalId)` pair. On a match,
  create the real `MediaRelation` (`SourceSeriesId`→`TargetSeriesId`, mapped `RelationType`) and
  delete the placeholder. This reuses the exact point where a series' provider link already gets
  written — no new pipeline needed.
- **UI:** merged into the existing Related tab, not a separate section — rendered dimmed/badged
  "Not in library," no click-through (matches one relation concept staying one list, not two).

### 6. MangaBaka provider migration (`v2` → `v1`)

Prerequisite for §2/§4/§5 — `v2` has no covers, relations, or rich tag taxonomy at all. Endpoints
move from `series/search`/`series/{id}` under `api.mangabaka.org/v2/` to the `v1` equivalents plus
the new `/v1/series/{id}/images`, `/v1/series/{id}/relationships`, `/v1/tags` calls needed above.
Rate limiting stays governed by the same documented 30/min search-tier limit (`v1`'s non-search
endpoints are more generous at 180/min, but `MangaBakaMetadataProvider` already paces uniformly to
the stricter figure — no change to that policy).

### 7. Fetch strategy

Default "Apply from Provider" stays cheap: one `GetAsync` call, same as today, now returning the
richer-but-still-single-request fields (`CoverImageUrl`, `Creator`, `PublicationYear`,
`PublicationFormat`, `Demographic`, `CrossReferences`). The expensive multi-endpoint data — MangaBaka's
cover archive, either provider's relations, MangaBaka's full tag taxonomy — is fetched **lazily**,
only when the corresponding UI tab (Covers / Related / Tags) is actually opened. Matches MangaBaka's
own tabbed site UX and keeps default Apply usage well under the 30/min limit.

### 8. Cross-reference auto-linking

When `CrossReferences` comes back non-empty (MangaDex's `links`, or a future MangaBaka
`/v1/source/*` lookup), upsert an `ExternalMediaId` row for each referenced provider — **only when
no row already exists for that `(SeriesId, Provider)` pair**. If a row already exists with a
*different* `ExternalId` than the one just discovered, leave it untouched rather than overwriting —
that's a genuine identity conflict, not a case to silently resolve, and gets surfaced through the
existing Needs-Review/Library-Health surfaces rather than a new one.

## Explicitly out of scope

- **MangaDex relations** — no confirmed API endpoint found this session; not guessed at.
- **A dedicated `IsSpoiler` column on `IssueTag`** — spoiler-flagged tags are dropped, not stored
  unflagged. Revisit only if spoiler-aware display becomes a real ask on its own.
- **Per-issue staff/credit writes from provider data** — `Issue.Writer`/`Penciller`/etc. stay
  untouched by this work; `Series.Creator` is a new, separate field, not a fallback source for the
  per-issue ComicInfo credit fields.
- **MangaBaka Works/Collections/News tabs, per-source score row, full hierarchical tag tree** —
  named in the original research memo, no change in status; still not warranted for a personal
  single-user library manager.
- **Splitting AniList `Creator` into separate story/art fields** — the role text is freeform,
  community-edited, and not reliably parseable into two buckets.

## Implementation phasing

Six independently-verifiable phases, foundation first:

- **Phase A** — `ExternalMediaMetadata` schema growth (§1), AniList/MangaDex additive query fields,
  MangaBaka `v2`→`v1` migration (§6). Nothing downstream compiles without this.
- **Phase B** — Cover pipeline (§2): `TrySetCustomCoverFromBytes`, the "Apply cover" action for
  AniList/MangaDex, `IMultiCoverProvider` + `CoverPickerViewModel`'s new tab for MangaBaka. Ships
  first after the foundation — this is the priority ask.
- **Phase C** — `Series.Creator` (§3): migration + per-provider extraction + auto-apply wiring.
- **Phase D** — Tag import (§4): bulk per-series `IssueTag` write path.
- **Phase E** — Relations (§5): `ExternalMediaRelation` entity, provider extraction, auto-upgrade
  hook, Related-tab UI merge.
- **Phase F** — Cross-reference auto-linking (§8) and lazy per-tab fetch wiring (§7) — thin glue
  once B/D/E exist, done last.

## Testing

- DTO/normalizer unit tests per provider for every new field (`CoverImageUrl`, `Creator`,
  `PublicationYear`, `PublicationFormat`, `Demographic`, `CrossReferences`), including the
  MangaBaka `v1` response shape change.
- `CoverThumbnailService.TrySetCustomCoverFromBytes` unit tests: happy path, non-image content,
  network failure — confirm no partial file is left on disk on any failure.
- `IMultiCoverProvider`/`CoverPickerViewModel` tests: candidate list populates, selecting one
  triggers exactly one full-image download and one `TrySetCustomCoverFromBytes` call.
- Tag-import unit tests: bulk-overwrite across every Issue in a series, `Category` defaulting,
  `Weight` always `Unset`, spoiler-flagged AniList tags excluded.
- `ExternalMediaRelation` unit tests: creation, and the auto-upgrade-to-`MediaRelation` transition
  (including placeholder deletion) triggered by a new `ExternalMediaId` link.
- Cross-reference auto-link unit tests: inserts when absent, never overwrites an existing
  differing `ExternalId`.
- On-screen verification via this project's UI-automation harness: apply a real AniList and a real
  MangaDex match and confirm cover/creator/tags/pub-year appear correctly; apply a real MangaBaka
  match and confirm the cover picker grid populates and a pick persists; confirm a relation
  placeholder upgrades to a real `MediaRelation` once its target is added to the library.
