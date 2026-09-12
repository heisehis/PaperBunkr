# Library Sort/Group Axes — Virtual Tags, Needs-Review, OpenCount Grouping, IsFinalIssue Tri-State

*Date: 2026-09-12. Scope: resumes the "pluggable sort/group strategies" item paused mid-scope on
2026-08-16 (`docs/superpowers/specs/2026-08-16-*` sessions, see
`project_paperbunkr_session_2026-08-16_handoff` memory) and its "5 unmappable comparer/grouper
concepts" follow-up. Produced via a `/grilling` pass per this project's `CLAUDE.md` design workflow.*

## 0. What the 2026-08-16 pause actually left behind (ground truth, not assumption)

The pause described the Library sort/group engine as "a small hardcoded switch" in
`LibraryScreenViewModel`, with 5 comparer/grouper concepts having no data to map from
(`AlternateCount`, `BookmarkCount`, `Manga`, `OpenCount`, `Proposed*`/`EnableProposed`, per-issue
`SeriesComplete`). Re-verified against the current codebase (not trusted from memory, per this
project's standing CE-parity/verification rule) before designing anything further:

- **The sort/group engine changed since the pause.** It is no longer a hardcoded switch. Since
  2026-09-03 (`docs/superpowers/specs/2026-08-18-issue-list-pluggable-sort-group-design.md`,
  landed as part of the "single sort/group pool" unification), `LibraryScreenViewModel` delegates
  to `IssueListScreenViewModel`, which is driven entirely by a data-driven catalog:
  `IssueListFieldCatalog` (`src/Paperbunkr.App/Models/IssueListFieldCatalog.cs`) — a
  `Dictionary<IssueListSortField, IssueListSortFieldDescriptor>` /
  `Dictionary<IssueListGroupField, IssueListGroupFieldDescriptor>` pair, operating on
  `IssueListRow` (built once per issue via `IssueListRow.FromIssue`, not recomputed per
  comparison). This is already "pluggable" in the sense that matters — the work here is adding
  new catalog entries, not building an axis system from scratch.
- **`BookmarkCount` and `OpenCount` are already live, working sort fields**, backed by simple
  existing data (`issue.Bookmarks.Count`, `Issue.OpenCount`) — not gaps. `Manga`/`ContentType`
  shipped separately on 2026-08-16 itself and is out of scope here.
- **The CE-ported comparer/grouper engine (`Paperbunkr.Engine`, `ComicBook` adapter, ~138
  comparer+grouper classes) is confirmed unused** by the App layer — `IssueListFieldCatalog`
  reimplements the same logic natively against `IssueListRow`. This design does not touch that
  engine or its `ComicBook` adapter (whose constructor has a real, unfixed static-event-subscription
  leak — `VirtualTagsCollection.TagsRefresh` with no unsubscribe path — but it's inert today since
  nothing instantiates `ComicBook` anywhere in `src/`, and nothing here changes that).
- **Real remaining gaps, scoped for this design**: Virtual Tags as a sort/group axis (currently
  wired only into Smart Lists and the Detail-screen pill row), a Needs-Review axis, an `OpenCount`
  grouper (sort-only today), and `IsFinalIssue`'s bool→tri-state upgrade.
- **Out of scope, split off deliberately**: `AlternateCount`/variant tracking needs a whole
  unscoped `IssueEdition` model and is not part of this pass — it's its own future Beta item.

## 1. Virtual Tags — dynamic per-tag sort/group entries

Paperbunkr's Virtual Tags (`VirtualTagDefinition`: user-defined, variable count, each with a
`CaptionFormat` template evaluated per issue/series via `VirtualTagTemplateEvaluator`) don't map
onto CE's `VirtualTag01`–`VirtualTag20` (20 fixed raw-string slots) as a single static field the
way `IssueListSortField`'s other ~60 enum members do. Verified against CE source
(`_reference/ComicRackCE/ComicRack.Engine/Metadata/VirtualTags/ComicBookVirtualTagComparer.cs`,
`ComicBookVirtualTagGrouper.cs`): CE sorts/groups by *one tag slot's raw string value at a time* —
ascending, case-insensitive, article-ignoring string compare for sort; one group bucket per exact
literal value for group, empty/null → `"Unspecified"`, one row per issue (not per comma-value).

Paperbunkr's version needs the same per-tag-at-a-time shape, but the "which tag" dimension is
dynamic instead of a fixed enum. This mirrors a problem Smart Lists already solved
(`SmartListField.VirtualTag` + `SmartListCondition.VirtualTagId`), so the same pattern applies here
rather than inventing a new one:

- `IssueListSortField`/`IssueListGroupField` gain one new marker value: `VirtualTag`.
- The current sort/group selection is held in two concrete places, both needing a nullable
  companion `VirtualTagId` (`int?`) alongside the existing field enum, meaningful only when
  `Field == VirtualTag` — exactly `SmartListCondition.VirtualTagId`'s existing shape: the
  persisted columns `AppSettings.LibraryIssueListSortField`/`LibraryIssueListGroupField`
  (`src/Paperbunkr.Data/Entities/AppSettings.cs:193,199` — this is where Library's sort/group
  choice already persists across sessions, confirmed there is no separate multi-layout entity;
  "Saved List Layouts" persists onto these same `AppSettings` columns, single active layout) and
  the runtime `IssueListScreenViewModel.SortField`/`GroupField` observable properties
  (`src/Paperbunkr.App/ViewModels/IssueListScreenViewModel.cs:67,73`).
- The sort/group picker UI shows one live entry per **enabled** `VirtualTagDefinition`
  (`IsEnabled == true`, ordered by `SortOrder`), labeled by its `Name` — not a single generic
  "Virtual Tag" entry with a secondary picker. This is more discoverable and matches how the
  Detail-screen pill row already surfaces enabled tags by name.
- `IssueListRow.FromIssue(Issue issue, Series series, ...)` gains an optional param
  `IReadOnlyList<VirtualTagDefinition>? virtualTags = null` (same shape as
  `DetailBandViewModel.LoadSeries`'s existing `virtualTags` param). When supplied, it evaluates
  each enabled definition's `CaptionFormat` once via `VirtualTagTemplateEvaluator.Evaluate` at
  row-build time and stores results in a new `IReadOnlyDictionary<int, string> VirtualTagValues`
  property on the row, keyed by `VirtualTagDefinition.Id`. This is the same cost class as
  `BookmarkCount`/`OpenCount` (computed once when the row list rebuilds, not per sort comparison)
  — no new caching layer needed.
- Sort: ascending, case-insensitive string compare on `row.VirtualTagValues.GetValueOrDefault(id, "")`
  (matches CE's comparer shape). Group: one bucket per exact value, empty/null → `"Unspecified"`
  (matches CE's grouper shape exactly, including the literal fallback caption).

## 2. Needs-Review axis — deliberate CE deviation, documented

CE's actual `EnableProposed` comparer/grouper
(`_reference/ComicRackCE/ComicRack.Engine/Metadata/ComicBook/{Comparer,Group}/*EnableProposed*.cs`)
is a different, inapplicable concept: a boolean meaning "this issue is filling in blank
Series/Title/Format/Volume/Number/Count/Year fields from guessed filename values" — not "has a
pending externally-sourced metadata suggestion." Paperbunkr has no equivalent filename-fallback
mechanism, so building literal `EnableProposed` parity would mean building a feature Paperbunkr
doesn't have a use for. Instead, this axis is grounded in Paperbunkr's real, already-shipped
Needs-Review system:

- `IssueListSortField.NeedsReview` — bool sort (no-proposals before has-proposals) +
  `IssueListGroupField.NeedsReview` — 2-bucket group ("Needs Review" / unlabeled-rest), following
  the existing `Read`/`Status` boolean-field precedent already in the catalog
  (`SortStrategies.Boolean`). Backing value: `issue.MetadataProposals.Any(p => p.Status ==
  MetadataProposalStatus.Pending)`, computed once in `FromIssue` from the already-loaded
  `Issue.MetadataProposals` nav collection (per verification: no new query infrastructure needed,
  same pattern `NeedsReviewViewModel.RefreshMetadataProposalItems` already uses).
- `IssueListSortField.PendingProposalCount` — int sort only, no grouper (matches
  `BookmarkCount`'s existing sort-only shape, and CE's own precedent of not grouping raw counts).
  Backing value: `issue.MetadataProposals.Count(p => p.Status == MetadataProposalStatus.Pending)`.

This is a named, intentional deviation from CE naming/behavior — flagged here per this project's
standing rule to verify against CE rather than silently assume parity where none exists.

## 3. OpenCount grouper — new, CE-parity bucket ranges

`OpenCount` is sort-only today (`IssueListSortField.OpenCount` exists;
`IssueListGroupField.OpenCount` does not). Add the grouper using CE's literal fixed ranges
(`_reference/ComicRackCE/ComicRack.Engine/Metadata/ComicBook/Group/ComicBookGroupOpenCount.cs`,
via the shared `ItemGroupCount` bucket base class, resource key `CountGroups`): `0-20`, `21-50`,
`51-100`, `101-200`, `201-500`, `501-1000`, `>1000`. CE's 8th bucket (`Unspecified`, for negative
values) is dropped — `Issue.OpenCount` can't be negative in Paperbunkr, so it would never populate.

## 4. `IsFinalIssue` → tri-state (`bool?`)

`Issue.IsFinalIssue` (currently `bool`) becomes `bool?`. This was explicitly re-confirmed after
surfacing a real caveat during grilling: `CeLibraryMigrator.cs:472` already collapses CE's
`SeriesComplete` tri-state (`YesNo.Unknown`/`No`/`Yes`) down to a bool at CE-import time
(`issue.IsFinalIssue = book.SeriesComplete == YesNo.Yes`), tested explicitly
(`CeLibraryMigratorTests.cs:180` asserts both `No` and `Unknown` become `False`). For any
CE-migrated library, the `Unknown`/`No` distinction is already permanently lost — this tri-state
upgrade only regains meaningful `Unknown` values for issues added *after* it ships. Decided to
proceed anyway: the forward-looking value (a more honest default than silently asserting "not
final" for issues nobody has actually classified) was judged worth the schema change.

- **Representation**: `bool?`, not a new enum. Reasoning: Avalonia's `CheckBox` has native
  `IsThreeState="True"` support bound directly to `bool?` — no converter glue, no new type needed
  for a single field. (There's no existing Yes/No/Unknown enum precedent elsewhere in
  `Paperbunkr.Data.Entities` to match against either way; this is a fresh, low-ceremony choice for
  a single field, not a reversal of an established pattern.)
- **Migration**: existing `True`/`False` rows carry over unchanged — no attempt to retroactively
  infer `Unknown` (impossible; the source distinction is gone). The column becomes nullable;
  new/never-explicitly-set issues default to `null` going forward, replacing the current implicit
  `false` default. `Issue.IsFinalIssue` (`src/Paperbunkr.Data/Entities/Issue.cs:58`) has no
  explicit property initializer today, so changing its type to `bool?` alone gives every new,
  unset instance `null` for free — no separate default-value change needed at the entity level,
  only the EF migration's column nullability.
- **UI**: `IssuePropertiesScreen.axaml:407-408`'s `ToggleSwitch` (bound `IsChecked="{Binding
  IsFinalIssue}"`) becomes a tri-state `CheckBox` (`IsThreeState="True"`, same binding, same
  "Final issue" label — kept as-is, not reverted to CE's "Series complete" wording, since this
  field was already deliberately renamed from CE's `SeriesComplete` on a prior pass).
- **Write-back**: `PaperbunkrSidecar.IsFinalIssue` (`src/Paperbunkr.Data/CeMigration/
  PaperbunkrSidecar.cs:36`) becomes `bool?` too. Confirmed sidecar-only — `IsFinalIssue` has no
  home in standard ComicInfo.xml (CE's `SeriesComplete` is an internal engine/CBI property, not
  part of the ComicInfo.xml schema), so no XML-schema collision to worry about.
- **Sort/group**: sort order `Unknown < No < Yes` (null sorts first), matching CE's own `YesNo`
  enum ordering (`Unknown = -1`). Group buckets: "Final issue" / "Not final" / "Unknown" — using
  Paperbunkr's own existing terminology (see UI note above), not CE's literal "Series complete" /
  "Series not complete" / "Unknown" strings.

## Testing

- `IssueListFieldCatalog`-level unit tests (wherever the existing catalog fields are tested) for
  each new/changed field: sort ordering (including `IsFinalIssue`'s null-first order), grouping/
  bucketing (including the `OpenCount` range edges and Virtual Tag's `"Unspecified"` fallback),
  and null/empty-row handling.
- A migration test for `IsFinalIssue`'s `bool` → `bool?` column change, following this project's
  existing migration-test pattern: seed pre-migration `True`/`False` rows, verify they survive
  unchanged post-migration, verify a freshly-inserted row without an explicit value reads back as
  `null`.
- `MetadataFileWriteBackServiceTests`/`MetadataFileFieldSnapshotTests` updates for the sidecar's
  now-nullable `IsFinalIssue` (round-trip `true`/`false`/`null`).
- On-screen verification once implemented, per this project's standing no-computer-use caveat —
  to be flagged explicitly when done, not skipped silently.

## Explicitly out of scope

- **`AlternateCount`/variant tracking** — needs a whole unscoped `IssueEdition` (variant-cover)
  model; split off to its own future Beta-backlog item, not blocking this pass.
