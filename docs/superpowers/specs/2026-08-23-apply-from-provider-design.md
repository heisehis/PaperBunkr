# Apply from Provider — Series Metadata Proposals + Detail UI

**Date:** 2026-08-23
**Status:** Approved, pending implementation plan
**Source:** Confirmed gap from the manga-detail-screen/MangaBaka session (see memory
`project-paperbunkr-manga-detail-and-mangabaka`) — linking a series to AniList or MangaBaka has
never applied the fetched Summary/Status/Genre to editable fields, only `ExternalMediaId` + an
audit-log `ExternalMetadataSnapshot` + `SeriesTitle` rows. Also folds in the UI-relevant subset of
`docs/mangabaka-metadata-ui-research.md` (a research memo, not a design spec) per user direction —
see "MangaBaka research scoping" below for what's in vs. explicitly deferred.

## Context

`ExternalMediaMetadata` (`src/Paperbunkr.Data/Metadata/IMetadataProvider.cs`) already carries
`Description`/`Status` per its own doc comment: "enough fields to eventually feed a
`MetadataProposal`, not a passthrough of the provider's own raw schema." `MetadataLinkResolver.
LinkAsync` fetches this DTO on every link/relink but only ever writes the title/id/snapshot side —
the description and status are fetched and discarded.

Paperbunkr already has a generic "suggested value, needs review, accept/reject, audit trail"
mechanism for exactly this shape of problem: `MetadataProposal` (docs/superpowers/specs/2026-08-17-
metadata-model-phase2a-metadata-proposals-design.md). It's Issue-scoped only today (`Title`,
`Format`, `Volume`, `Number`, `Count`, `Year`, `Series`), and its own `MetadataProposalSource` enum
already reserves an unused `MetadataProvider` member for this exact case ("the rest exist now so
later phases ... external providers ... don't need another migration"). Per explicit user
direction, this feature is built by widening that existing mechanism to cover Series-scoped
fields, not by adding a second, structurally-redundant proposal system.

## MangaBaka research scoping

`docs/mangabaka-metadata-ui-research.md` recommends several Detail-screen UI changes inspired by
MangaBaka's own Info tab. Two of those are hard-blocked on data modeling this design doesn't
cover and are deferred to their own future specs, same staging the memo itself proposes:

- **Weighted/categorized tag panel** (Core/Defining/Recurrent/Incidental styling) — needs the
  categorized-tags data model the memo calls "the biggest single gap," which its own
  Recommendations section says needs its own design pass.
- **Per-source score row** (ratings from up to 7 tracked sites) — no provider Paperbunkr has
  (`ExternalMediaMetadata`, AniList, MangaBaka) returns a rating/score field today.

Everything else recommended for the Detail screen's Info-tab layout is in scope for this design:
a sourced attribution line, an external-links panel, and Meta-badge-row polish (see "Detail screen
UI" below). Also explicitly out of scope, not blocked but simply a different feature area: Works/
Collections tabs, cross-reference ID-prefix search shortcuts, the Similar-Series recommendation
rail, and the MangaBaka tracker-adapter/v1-API-migration items — all named in the memo's own
Recommendations section as separate future work.

## Scope

### Schema changes

| Change | Type | Notes |
|---|---|---|
| `MetadataProposal.IssueId` | `int` → `int?` | Existing rows are unaffected (all currently Issue-scoped, value preserved). |
| `MetadataProposal.SeriesId` | new `int?`, FK to `Series`, cascade delete | Mirrors the existing `IssueId` FK shape. Exactly one of `IssueId`/`SeriesId` is set per row — enforced at creation time in code (`MetadataLinkResolver`/`SeriesReassignmentResolver` call sites), not a DB constraint, consistent with how `Series` (issue-scoped field) is already special-cased rather than modeled as a schema-level union type. |
| `MetadataProposal.ProviderKey` | new `ExternalMetadataProvider?` | Which linked provider produced the value, when `Source == MetadataProvider`. Needed because two providers (AniList, MangaBaka) can both be linked to one series — `MetadataProposalSource.MetadataProvider` alone can't tell them apart for the "via MangaBaka" attribution UI. Null for every other source. |
| `MetadataProposalField` | add `Summary \| Status \| Genre` | Additive to a string-backed column (`HasConversion<string>` in `PaperbunkrDbContext`) — no data migration needed for the enum values themselves. |
| `Series.MetadataProposals` | new nav collection | `List<MetadataProposal>`, mirrors `Issue.MetadataProposals`. |

One new EF migration for the two schema changes (nullable `IssueId`, new `SeriesId`/`ProviderKey`
columns + FK).

### Apply flow — `MetadataLinkResolver.LinkAsync`

After the existing `ExternalMediaId`/`ExternalMetadataSnapshot`/`SeriesTitle` writes, for each of
Summary/Status/Genre where `metadata` supplies a non-empty value:

1. Snapshot `Series.Field`'s *current* value into `MetadataProposal.CurrentValue`.
2. Create the proposal: `SeriesId` set, `IssueId` null, `Field` = the relevant enum member,
   `ProposedValue` = the provider's value, `Source = MetadataProvider`, `ProviderKey` = the
   provider being linked, `Confidence` = 1.0 (a provider fetch is a direct value, not a
   heuristically-parsed one — distinct from the filename parser's fixed 0.6).
3. Mark it `Accepted` immediately, `ResolvedAt = now` (per the auto-accept-and-overwrite decision
   below), and write `ProposedValue` directly into `Series.Field` — unconditionally, even when the
   field already has a non-empty value.

A relink later repeats this: the most recently linked/refreshed provider always wins. This is a
deliberate simplification, not an oversight — see "Auto-accept and overwrite" below for why.

`Status` needs a raw-string → `SeriesStatus` normalizer (new, small, provider-agnostic — lives
alongside `MetadataLinkResolver`). AniList's `Status` passes through GraphQL's own enum strings
(`FINISHED`/`RELEASING`/`NOT_YET_RELEASED`/`CANCELLED`/`HIATUS`, confirmed in
`AniListNormalizer.cs`). MangaBaka's exact `status` string values aren't confirmed yet (its DTO
passes the raw field through untyped) — the implementation pass needs a real API response to
confirm them, not a guess. Any unrecognized value maps to `SeriesStatus.Unknown` rather than
fabricating a mapping.

`Genre` is a single free-text string, same treatment as `Summary` — no attempt to reconcile with
`Issue.Genre` (already the documented source of truth for filtering/display; this only updates
`Series.Genre`'s stored value, unchanged in scope from what it is today).

### Auto-accept and overwrite

Per explicit direction: proposals write immediately (matching the existing `Automatic` policy's
behavior for filename proposals) and overwrite a non-empty existing value rather than only filling
blanks. This is a real, accepted trade-off — a relink can silently replace a manually-edited
Summary/Status/Genre — mitigated by the revert path below, not by blocking the write.

Because Series fields are written directly (unlike Issue fields, which read through an
`Effective*` resolver overlay and are never written by the scanner itself), **Reject needs a real
revert step**, not just a status flip: clicking Reject on an already-`Accepted` Series-scoped
proposal in Needs Review writes `Series.Field` back to the row's snapshotted `CurrentValue`, then
sets `Status = Rejected`. This is the Series-field equivalent of `SeriesReassignmentResolver.
Apply`'s existing precedent (Accept on a `Series`-field proposal already triggers a side effect
beyond a status flip; this is the same shape, on Reject, for a different field group). Accept on
an already-`Accepted` row (the common case, since these arrive pre-accepted) is a no-op — same
semantics `IsAlreadyAccepted` already documents for Issue-level `Automatic` proposals.

No `Effective*` resolver overlay for these three fields — deliberately simpler than the Issue-field
pattern, since there's no filename-parser-vs-embedded-XML race to arbitrate at read time here; the
stored field value is always current.

### Review UI — `NeedsReviewViewModel`

The existing "Metadata Proposals" section already queries generically by `Status`; two small
changes:

- `RefreshMetadataProposalItems`'s label construction branches: Issue-scoped rows keep today's
  `"{Series} #{Number}"` label; Series-scoped rows use just the series name (no issue to name).
- `ResolveProposal`'s Reject path branches on which FK is set: Issue-scoped rejects behave exactly
  as today; Series-scoped rejects call the new revert-to-`CurrentValue` write described above
  (Accept keeps its existing no-op-if-already-accepted behavior for both scopes).

### Detail screen UI

Three additions to `MangaDetailScreen`/`MangaDetailScreenViewModel`, all backed by data this design
already produces or that already exists elsewhere in the codebase:

- **Sourced attribution line.** A small "via MangaBaka" / "via AniList" caption near Summary/
  Status/Genre, sourced from the most recent Series-scoped `MetadataProposal` per field (its
  `ProviderKey`, formatted the same way `ExternalLinkSample.ProviderLabel` already is). Absent
  (not blank) when the field was never provider-sourced.
- **External-links panel, promoted to the header.** `ExternalLinks` already exists as a flat list
  in `DetailTabsViewModel` (Details tab) — this adds a compact chip row of the same data directly
  in the Manga Detail header, next to the tracker-status action button. Deliberately *not*
  MangaBaka's "grouped by purpose" (Publisher/Read Officially/Info/Social) framing — every provider
  in `ExternalMetadataProvider` is a metadata/tracker site in Paperbunkr's model, not a publisher or
  reading-site link, so a purpose grouping would have one populated bucket and add nothing. Flat
  chip row, same data source (`ExternalMetadataResolver.GetExternalIds`), no new query.
- **Meta badge row polish.** The header's existing separate icon-led rows (Status/ContentType/
  Source) fold into one horizontal badge row, plus a chapter/volume count badge — `Chapters.Count`
  and a distinct-`EffectiveVolume()` count are already computed in `LoadSeries`, just not currently
  surfaced as badges.

## Testing

- New EF migration test: existing `MetadataProposal` rows (all Issue-scoped) keep their `IssueId`
  unchanged after the nullable-column migration; a new Series-scoped row round-trips correctly.
- `MetadataLinkResolverTests`: linking creates Accepted Series-scoped proposals with correct
  `CurrentValue` snapshot/`ProposedValue`/`ProviderKey` for Summary/Status/Genre; relinking
  overwrites a manually-set value; a provider response missing a field creates no proposal for
  that field; `Status` normalizer mapping (known values → `SeriesStatus`, unknown → `Unknown`).
- `NeedsReviewViewModelTests`: Series-scoped proposal row label (series name only), Reject reverts
  `Series.Field` to the snapshotted value, Accept on an already-accepted row is a no-op.
- `MangaDetailScreenViewModelTests`: attribution line present/absent per field, external-links chip
  row populated from existing `ExternalMetadataResolver` data, badge row values.
- On-screen verification (per this project's standing UI-testing practice): link a series to
  AniList and separately to MangaBaka, confirm Summary/Status/Genre update and the attribution line
  names the correct provider, confirm Reject in Needs Review restores the prior value, confirm the
  external-links chip row and badge row render correctly on the Manga Detail header.

## Explicitly out of scope

Weighted/categorized tag panel and per-source score row (blocked on data modeling, need their own
design pass). Works/Collections tabs. Cross-reference ID-prefix search shortcuts in the External
Metadata search box. Similar-Series recommendation rail (blocked on `RecommendationResolver`
getting a homepage surface, Phase 6a). MangaBaka `v2`→`v1` API migration and URL-backfill (separate,
independent fixes named in the research memo, unrelated to this feature). A full MangaBaka tracker
adapter UI surface (adapter already exists; wiring it into a UI is separate future work).
