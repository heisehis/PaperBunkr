# Series Detail — Run Separator (Volume grouping)

**Date:** 2026-08-30
**Status:** Approved, pending implementation
**Source:** Design session with Ehis (2026-08-30), following up on Metadata Model Phase 7
(`docs/superpowers/specs/... Phase 7`, deferred 2026-08-18 for lack of a concrete use case).
Ehis's own library has same-title series that restart numbering across eras — e.g. "Venom
(2018)", "Venom (2022)", "Venom (2025)" — a real, current case. Two CE `ComicBookDialog`
screenshots (Venom V2025 #259, Annual Venom V2021 #1) confirmed CE's own `Volume` field is
exactly this year-keyed run identifier, verifying the field choice below against CE precedent
per the CLAUDE.md standing rule.

## Problem

Paperbunkr resolves `Series` identity purely by name (`LibraryFolderScanner.cs:128`,
case-insensitive `Name` dictionary lookup) — no Volume/year disambiguation. Confirmed today: two
runs sharing a title collapse into one `Series` row, and `Issue` numbering restarts mid-collection
(Venom 2018 #1–5, then Venom 2022 #1–10). `OrderByNumber()` (`IssueOrdering.cs:19`) sorts by
`Number` alone, so today the Issues tab literally interleaves "#1, #1, #2, #2…" from both runs —
a real, visible bug whenever this occurs, not a hypothetical.

## Explored and rejected: full Series split

Considered splitting each run into its own `Series` row (linked via the existing `MediaRelation`
mechanism from Phase 3) so every Series-scoped feature — `OrderByNumber`, gap-detection
SmartList matchers, `SeriesComplete`, cover resolution, reading-status — keeps working per-run
without modification. Rejected by Ehis in favor of keeping one `Series` row per title, with
Volume-based sub-grouping surfaced only as a visual separator in the Detail screen. Numbering,
gap-detection, and completion stats stay series-wide (current behavior) — this feature is display
scope only, not a data-model or stats change.

## Scope

Series Detail screen's Issues tab (Poster/List/Card view modes) only. No schema change, no
migration, no change to sort/gap/completion logic anywhere else (SmartLists, Library, series
stats are untouched). **This closes the "run splitting" half of the two items Ehis flagged as
possibly needing Phase 7 — it turns out to need none of it.** Phase 7 (Volume/Chapter/Story
schema, `CollectionContent`) stays deferred; nothing here revives it.

The other flagged item, the Specials Tab design (`docs/superpowers/specs/
2026-08-28-series-detail-specials-tab-design.md`), was re-examined in the same session: it's
pure `Issue.Format` classification with no schema change either, and remains independent of this
feature — Volume and Format are separate axes on the same Issue (confirmed by the Annual Venom
V2021 screenshot: Volume=2021 *and* Format=Annual simultaneously).

## Data / ordering

No new field. Reuses `Issue.Volume` (string, Phase 1) and the existing `VolumeSortKey()`
(`IssueMetadataExtensions.cs:94`, numeric-prefix parse, same treatment as `NumberSortKey`).

`DetailTabsViewModel.LoadSeries` changes issue ordering from `OrderByNumber()` alone to:

1. Primary: `VolumeSortKey()` ascending, with issues that have no parseable Volume sorted first
   (an implicit "no volume set" bucket, matching today's most-common state).
2. Secondary: `NumberSortKey()` ascending within each Volume group.

Issues are then partitioned into ordered run groups by `EffectiveVolume()`. **Collapse rule:** if
the series has one or zero distinct `EffectiveVolume()` values total (the overwhelming majority
of series), grouping is skipped entirely — a single unlabeled group, rendering exactly as today.
Bars only appear when there's genuinely more than one run to separate.

New `IssueRunGroup` (Header: `string?` — `null` for the no-volume bucket, `"Volume {value}"`
otherwise; Items: `ObservableCollection<IssueCardSample>`), and a new
`ObservableCollection<IssueRunGroup> IssueGroups` on `DetailTabsViewModel`, built alongside the
existing flat `Issues` collection (kept as-is for anything that doesn't care about grouping).

## UI

Poster and Card views use a `WrapPanel` (`DetailTabs.axaml:165`, `:216`) — multiple tiles per
row — so a separator "bar" must be a full-width row breaking the wrap flow, not a per-tile
leading strip. All three `ItemsControl`s (Poster/List/Card) restructure from a single flat
`ItemsSource="{Binding Issues}"` into a grouped shape:

- Outer `ItemsControl` bound to `IssueGroups`.
- Each group's `DataTemplate` renders an optional header bar (`IsVisible` bound to
  `Header is not null`) reading `"Volume {value}"`, then an inner `ItemsControl` bound to
  `Items` — `WrapPanel` for Poster/Card (unchanged from today's per-view panel), plain vertical
  stacking for List.
- Same shape as the Library's existing grouped-list pattern (`IssueListRowGroup`,
  `IssueListScreenViewModel.cs:208`) extended to wrap-panel layouts, rather than a new idiom.

Selection, 2D-arrow keyboard navigation, and context-menu wiring (`OnIssueTilePointerPressed`
etc.) currently key off whichever tile/`ItemsControl` raised the event, not a hardcoded reference
to the old flat `Issues`-bound control — verify this still resolves correctly once nested at
implementation time; if it turns out to assume the old flat structure, that's a small
generalization, not a redesign (same caveat the Specials-tab spec already flagged for this file).

## Testing

`DetailTabsViewModelTests` (extend):
- Single-volume series (or no Volume set anywhere) → one group, `Header == null`, renders
  identically to pre-change behavior.
- Multi-volume series → groups ordered by `VolumeSortKey`, each group internally ordered by
  `NumberSortKey`; issues with no Volume bucket first with `Header == null`; every other group's
  `Header == "Volume {value}"`.
- Sum of `Items.Count` across all `IssueGroups` equals `series.Issues.Count` (no issue dropped or
  duplicated).
- Existing Issues-tab tests (selection, view-mode persistence, `OrderByNumber` regression via a
  single-volume series) still pass.
- Regression: full `Paperbunkr.App.Tests` green; plain `dotnet build` (not just incremental) per
  the CLAUDE.md AVLN2000 gotcha reminder, since `DetailTabs.axaml` templates change shape (no new
  `x:Class` view is created, so the gotcha itself doesn't strictly apply, but the same "0 Errors
  isn't proof" caution from the Specials-tab spec holds).

## Risks

- **Concurrent shared-working-tree edits.** `DetailTabsViewModel.cs`/`DetailTabs.axaml` are the
  same files the streaming redesign and the (still-unimplemented) Specials-tab spec both touch
  most heavily. Checked clean at design time (`git status` on both files, 2026-08-30) — re-check
  immediately before implementation; park any WIP to scratchpad, never `git stash` (shared tree,
  see `project_paperbunkr_concurrent_sessions` memory).
- **Existing merged-run libraries.** A user whose library already has a merged multi-run series
  but hasn't tagged `Volume` on any issue gets no separator (correctly falls into the single
  no-volume bucket) — this feature only helps once Volume is populated (via filename/embedded
  parsing already in place, or manual edit). No backfill/detection pass is in scope here.

## Explicitly out of scope

Full `Series` splitting into separate rows per run (considered above, rejected). Run-scoped
numbering/gap-detection/completion stats (stays series-wide). Any schema change or migration.
Cross-run linking via `MediaRelation` (only relevant if the rejected full-split approach is
revisited later). A manual per-issue "run" override independent of `Volume` — Volume is treated
as the single source of truth for run identity, same as CE's own precedent, no second field.
