# Metadata Model — Phase 2a: Metadata Proposals

**Date:** 2026-08-17
**Status:** Approved, pending implementation plan
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md` (79-section architectural spec),
§37/§38 ("Proposed Metadata", "Proposed Values and Sorting") and §68 ("Phase 2: Metadata
proposals"). Not committed to this repo (external design input).

## Context

The source doc's own §68 rollout splits Phase 2 into `MetadataProposal` +
`MetadataResolutionPolicy` + `EffectiveValueResolver`, migrating CE's `Shadow*`/`Proposed*`
comparer pattern to a real resolver. Scoping this session found that Phase 2 as originally
discussed (see [Phase 1 spec](2026-08-17-metadata-model-phase1-canonical-metadata-design.md)'s
"out of scope" section) had grown to include three separable pieces: this proposal workflow,
Series-reassignment proposals, and the `MetadataDescriptor` sort/group system. Per this project's
established pattern of decomposing oversized bundles (see the "Library browsing extras"
decomposition, `docs/alpha-roadmap.md`), these are now three ordered sub-projects:

- **2a (this spec):** Metadata Proposals core.
- **2b:** Series reassignment proposals — needs its own design pass for FK-migration semantics.
- **2c:** `MetadataDescriptor` + retargeting `LibraryScreenViewModel`'s sort/group to it.

Verified against real CE source and the current schema before writing this (standing project
rule):

- `ComicBook.cs`'s real `Shadow*`/`Proposed*` pattern: 7 fields (`Series`, `Title`, `Format`,
  `Volume`, `Number`, `Count`, `Year`) each have a lazily-parsed `Proposed*` value (from
  `OnParseFilePath()`, CE's filename parser) and a `Shadow*` = real value if `EnableProposed` and
  the real value is non-empty, else the proposed one. `EnableProposed` is a per-book bool, default
  `true`. `WriteProposedValues()` promotes all `Proposed*` into the real fields and flips
  `EnableProposed` off.
- Unlike CE, Paperbunkr's `Issue.SeriesId` is a **mandatory FK** to a real `Series` entity — there
  is no "unresolved series name" scalar state once an `Issue` exists. A `Series` proposal would
  mean "reassign this issue to a different `Series` entity," a materially different and riskier
  operation than the other 6 fields. Deferred to 2b (see above).
- `Issue.Title`/`Number`/`Count`/`Volume`/`Year`/`Format` all already exist as plain nullable
  scalars on `Issue` (`Format` and `Count` from Phase 1 and earlier; `Volume` is `string?` since
  Phase 1). No schema gap to fill for the fields themselves.
- `LibraryFolderScanner`'s current filename-fallback logic (`ComicNameInfo.FromFilePath`) only
  populates **3** of the 6 fields today — `Number`, `Volume`, `Year` — via direct
  `issue.Field ??= nameInfo.Field` writes, with embedded ComicInfo.xml always winning first
  (`docs/superpowers/specs/2026-08-09-embedded-metadata-and-migration-relocation-design.md`).
  `Title`/`Count`/`Format` aren't filename-inferred at all currently. This phase's proposal
  *schema* supports all 6 fields (for future sources — embedded-XML-vs-filename conflicts,
  external providers in later phases), but only `Number`/`Volume`/`Year` will actually have
  proposals created for them by the scanner this phase.
- `NeedsReviewViewModel` (`src/Paperbunkr.App/ViewModels/NeedsReviewViewModel.cs`) is an existing,
  live, persistent review-queue screen with 3 sections (Content Type, Missing Files, Series
  Conflicts), each backed by either a live query or a stored `Pending`-status entity
  (`SeriesConflict` is the closest existing precedent for `MetadataProposal`'s shape), with
  per-row accept/reject `[RelayCommand]`s and a `Refresh()` pattern. This phase's review UI adds a
  4th section to this same screen rather than building a new one.

## Scope

### Schema changes

| Change | Type | Notes |
|---|---|---|
| New `MetadataProposal` entity | `Id`, `IssueId`/`Issue`, `Field` (enum), `CurrentValue`/`ProposedValue` (`string?`), `Source` (enum), `Confidence` (`decimal`), `Status` (enum), `CreatedAt`, `ResolvedAt` (`DateTime?`) | See shape below. |
| New `MetadataProposalField` enum | `Title \| Format \| Volume \| Number \| Count \| Year` | Matches CE's 7 `Shadow*` fields minus `Series` (deferred to 2b). |
| New `MetadataProposalSource` enum | `FilenameParser \| ComicInfoXml \| MetadataProvider \| Manual \| Import \| AI \| Other` | Only `FilenameParser` is produced this phase; the rest exist now so later phases (embedded-XML conflicts, external providers) don't need another migration just to add enum members that are additive to an existing column. |
| New `MetadataProposalStatus` enum | `Pending \| Accepted \| Rejected \| Ignored` | Matches source doc §37.1 exactly. |
| `Issue.MetadataProposals` | new nav collection | `List<MetadataProposal>`, mirrors the `Bookmarks` nav collection added in Phase 1. |
| `AppSettings.MetadataResolutionPolicy` | new enum, default `Automatic` | See policy section below. |

`CurrentValue`/`ProposedValue` are both `string?` even for the two int-backed fields (`Count`,
`Year`) — same "never destroy the display value" rationale `Number`/`Volume` already use
(Phase 1). The resolver parses to the target type per field; `CurrentValue` is a snapshot taken
at proposal-creation time for display/audit purposes, not a live read of the current stored value
(which the resolver reads directly from `Issue` instead).

### `MetadataResolutionPolicy` — scoped down from the source doc's 4 values to 2

The source doc lists `PreferStored | PreferProposed | Prompt | Automatic`. With a single active
proposal source (`FilenameParser`) and no safe way to let a proposal silently beat a stored value
(would violate the source doc's own Rule 5, "preserve user-entered metadata"), `PreferStored` and
`PreferProposed` have no behavior to actually implement yet — there's nothing for them to
prefer *between*. This phase implements only the two that are observably different today:

- **`Automatic`** (default): proposals are created already `Accepted`. `EffectiveValue = stored ??
  accepted proposal`. This matches `LibraryFolderScanner`'s current UX exactly — filename-inferred
  values are still immediately visible after a scan, just now with a full audit trail and a
  reject/undo path via the review queue.
- **`Prompt`**: proposals are created `Pending`. `EffectiveValue = stored only` — a proposed value
  contributes nothing until a human accepts it in the review queue.

Global setting (`AppSettings`, one row for the whole library), not per-issue — CE's
`EnableProposed` is per-book, but Paperbunkr has no per-book UI surface for it and no user request
for one; a single Preferences toggle is the right size for now. `PreferStored`/`PreferProposed`
get added to the enum (and given real behavior) once a second competing source exists where
precedence between two *different* proposals for the same field is a real scenario.

### `EffectiveValueResolver`

New extension methods in `src/Paperbunkr.Data/Metadata/IssueMetadataExtensions.cs` (same file/
style as Phase 1's resolvers — `ReadPercentage`, `NumberSortKey`, etc.), one per field:

```
EffectiveTitle(this Issue)
EffectiveFormat(this Issue)
EffectiveVolume(this Issue)
EffectiveNumber(this Issue)
EffectiveCount(this Issue)
EffectiveYear(this Issue)
```

Each: `issue.Field ?? AcceptedProposalValue(issue, MetadataProposalField.X)`, where
`AcceptedProposalValue` reads from the already-loaded `issue.MetadataProposals` collection (no DB
query inside the resolver — same pattern as every other Phase 1 resolver, callers are responsible
for `.Include(i => i.MetadataProposals)` where needed, same as `.Include(i => i.Bookmarks)`
already is). Parses `Count`/`Year`'s stored string proposal value back to `int?`, returning `null`
on a malformed value rather than throwing (a corrupt/unparseable proposal should never crash a
read path).

### Scanner rewiring

`LibraryFolderScanner`'s 3 lines:

```csharp
issue.Number ??= string.IsNullOrWhiteSpace(nameInfo.Number) ? null : nameInfo.Number;
issue.Volume ??= nameInfo.Volume > 0 ? nameInfo.Volume.ToString() : null;
issue.Year ??= nameInfo.Year > 0 ? nameInfo.Year : null;
```

become: when `Issue.Field` is null and `nameInfo` has a value, create a `MetadataProposal`
(`Source = FilenameParser`, `Confidence = 0.6` — a fixed constant; filename parsing is
deterministic pattern-matching, not a scored signal, so a single reasonable constant below the
1.0 reserved for `Manual`/`ComicInfoXml` is honest here rather than a fabricated per-value score)
with `Status` set per `AppSettings.MetadataResolutionPolicy`
(`Accepted` if `Automatic`, `Pending` if `Prompt`). The direct `issue.Field ??=` write is removed
— the field itself stays `null` on the entity; `EffectiveValue` is what callers read going
forward. Library grid, Detail screen, Issue Properties, Bulk Edit, and Smart Lists all currently
read `Issue.Number`/`Volume`/`Year` directly — this phase updates those read sites to the new
`Effective*` resolvers so filename-inferred values keep showing up exactly as they do today.
Explicitly **not** touched this phase: `Series` resolution (already a separate mechanism, FK is
set at `Issue` creation, not scanner fallback) and embedded-ComicInfo.xml values (already applied
directly and always win — no proposal needed for a value that's already authoritative-by-source).

### Review UI — `NeedsReviewViewModel`'s 4th section

New "Metadata Proposals" section, same architecture as the existing 3:

- `ObservableCollection<MetadataProposalRowViewModel> MetadataProposalItems`, `HasMetadataProposalItems`, folded into `HasPendingItems`.
- `RefreshMetadataProposalItems`: queries `MetadataProposal` rows where `Status` is `Pending` (needs a decision) **or** `Accepted` (already applied, but still user-reviewable/correctable — an `Automatic`-policy proposal isn't "done," it's "applied but auditable"), ordered by `CreatedAt` descending.
- Row shows: series/issue identity, field name, current vs. proposed value, source, status.
- Per-row `Accept`/`Reject` `[RelayCommand]`s: `Accept` sets `Status = Accepted`, `ResolvedAt = now` (a no-op in effect if already `Accepted` via `Automatic`, but available for a `Prompt`-policy `Pending` row). `Reject` sets `Status = Rejected`, `ResolvedAt = now` — since `Issue.Field` itself is never written by the scanner (only the proposal row holds the value; see below), rejecting an already-`Accepted` `Automatic` proposal is enough on its own to make `EffectiveValue` stop surfacing it, no separate field write needed.
- No bulk actions this phase (unlike Series Conflicts' "Keep All Separate"/"Merge All Above 90%") — proposal volume per scan is small (0-3 fields per newly-scanned issue with no embedded metadata) and each is a distinct field/value judgment, not a repeatable yes/no pattern.

## Testing

- `EffectiveValueResolverTests` (or added to `IssueMetadataExtensionsTests`): stored-only,
  proposal-only (`Accepted`), stored-and-proposal (stored wins), `Pending` proposal (contributes
  nothing), `Rejected` proposal (contributes nothing), multiple fields on one issue, malformed
  `Count`/`Year` proposal value (resolves to `null`, doesn't throw).
- `LibraryFolderScannerTests`: updated to assert `MetadataProposal` rows are created instead of
  direct field writes, for both `Automatic` (proposal `Accepted`, `EffectiveValue` populated) and
  `Prompt` (proposal `Pending`, `EffectiveValue` still null) policy, plus the existing
  embedded-metadata-wins case (no proposal created at all when embedded XML supplies the field).
- `NeedsReviewViewModelTests`: new section's `Refresh`, Accept/Reject per row, `HasPendingItems`
  reflecting the new section, `Accepted`-but-still-listed rows appearing correctly.
- New EF migration + migration test (existing `Issue` rows keep their current field values
  unchanged; new `MetadataProposal` table starts empty).
- Read-site updates (Library grid, Detail screen, Issue Properties, Bulk Edit, Smart Lists) get
  regression coverage confirming they now display `EffectiveValue` output, not just `Issue.Field`
  directly, wherever a test already exercises Number/Volume/Year display.

## Explicitly out of scope (later sub-projects)

Series reassignment proposals (2b). `MetadataDescriptor` and sort/group retargeting (2c).
`PreferStored`/`PreferProposed` policy values (need a second real competing source first).
Embedded-ComicInfo.xml as a proposal source (currently applied directly, always wins — revisit if
a future phase wants XML-vs-filename conflicts to be reviewable too). External metadata providers
as a proposal source (Phase 5 in the source doc's own numbering). Per-issue policy override (CE
precedent exists — `EnableProposed` — but no UI surface or user request for it yet). Bulk actions
in the review UI (revisit if proposal volume in practice turns out to warrant it).
