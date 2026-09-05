# Weighted/Categorized Tags + Click-to-Search Chips

**Date:** 2026-08-23
**Status:** Approved, pending implementation plan
**Source:** Follow-up from the MangaBaka research memo
(`docs/mangabaka-metadata-ui-research.md:201-213`), which named this "the biggest single gap"
between Paperbunkr's flat `Genre`/`Tags` CSV strings and MangaBaka's categorized+weighted
taxonomy, and which `docs/superpowers/specs/2026-08-23-apply-from-provider-design.md:35-38` had
already deferred as needing "its own design pass." Also closes a separate, smaller gap found in
the same session: today's `DetailPills` chips (Teams/Locations/Genre/Virtual Tags) render as
plain `Border`+`TextBlock` with no click handling at all — clicking one does nothing, even though
`LibraryScreenViewModel`'s `Descriptive`/`All` search modes already search `Genre` and `Tags`.

## Context

`Issue.Genre` and `Issue.Tags` (`src/Paperbunkr.Data/Entities/Issue.cs:102,120`) are flat nullable
`string` CSV fields, matching ComicInfo.xml's on-disk format. `DetailPillsViewModel`
(`src/Paperbunkr.App/ViewModels/DetailPillsViewModel.cs`) reads `Genre` via `BulkFieldRegistry`
and renders it as chips in `DetailPills.axaml` — Genre is the only one of the two shown as chips
today; `Tags` isn't rendered as chips anywhere. Neither carries any notion of category or
relevance strength. `_reference/ComicRackCE` has no precedent for either concept — CE's own
Genre/Tags are flat CSV too, so this is a deliberate deviation, not a CE-parity catch-up.

## Scope

### Data model

New entity `IssueTag`, replacing `Issue.Genre` and `Issue.Tags` outright (both columns dropped —
no dual-write/back-compat shim):

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` (PK) | |
| `IssueId` | FK → `Issue`, cascade delete | |
| `Field` | `enum { Genre, Tags }` | Preserves the CE-field distinction as a discriminator on one table, rather than two physically separate tables, so Smart Lists/search can query both uniformly while the UI still renders them as two distinct chip rows. |
| `Value` | `string` | The tag text, e.g. "Time Skip". |
| `Category` | `string?` | Free text, extensible (not a fixed enum). Null renders as "Uncategorized". Migration seeds `"Genre"` for migrated `Field=Genre` rows, `"Uncategorized"` for migrated `Field=Tags` rows. |
| `Weight` | `enum { Unset, Incidental, Recurrent, Defining, Core }` | Ascending significance. `Unset` is the honest default for anything not yet rated by the user — never inferred. |

**Migration:** one EF migration adds `IssueTags`; a one-time data-migration step splits every
existing `Issue.Genre`/`Issue.Tags` value via the same comma-split convention
`CsvFieldAggregator` already uses, producing `Weight = Unset` rows with the category defaults
above. The old columns are dropped after migrating — this is a clean cut per this project's usual
style (no `[Obsolete]`, no parallel old/new field).

**Series-level aggregation** (`DetailPillsViewModel.LoadSeries`, which already merges Genre
across a series' issues): when the same `Value` appears on multiple issues with different
weights, display the *highest* weight found. No new concept here beyond "max wins."

### File interchange (ComicInfo.xml) stays flat

The on-disk format is an external contract, not ours to restructure — files Paperbunkr writes
must stay readable by CE and other tools.

- **Export:** join a `Field`'s `Value`s back into a comma string, same shape as today.
- **Import/rescan:** diff, don't replace. A `Value` that already has an `IssueTag` row (Category/
  Weight already set by the user) is left untouched. Only values newly present in the file get
  added (`Weight = Unset`); values no longer present get removed. Without this, a routine library
  rescan would silently wipe out weighting work, since the file format itself has no concept of
  weight or category.

### Consuming systems

These currently read/write `Issue.Genre`/`Issue.Tags` as flat strings and get rewired to query
`IssueTag` grouped by `Field`, but keep the same external contract (a comma string) so nothing
downstream of them changes behavior:

- **`BulkFieldRegistry`** — `Genre`/`Tags` descriptors' `Get` returns the same joined string as
  today; `Set` (used by the bulk issue editor) diffs the submitted CSV against existing rows,
  preserving Category/Weight for values that survive the diff and defaulting new values to
  `Weight = Unset`, same rule as file import.
- **Smart Lists** field resolvers — `Genre`/`Tags` conditions query `IssueTag.Value` where
  `Field` matches, instead of `string.Contains` on the old columns. No change to Smart List UX or
  condition syntax.
- **Library search modes** (`LibraryScreenViewModel.cs:624-646`) — `Descriptive`/`All` modes query
  joined `IssueTag` values instead of the old string columns. No change to search UX.

### Detail screen UI

- `DetailPills.axaml`/`DetailPillsViewModel` — Genre chips gain weight-based styling (Core
  rendered bold/filled, tapering to Incidental rendered light, per the research memo's
  suggestion at `docs/mangabaka-metadata-ui-research.md:209-213`) and group by Category. A new
  **Tags** pill row is added — it renders as chips for the first time.
- **Left-click a chip:** runs the existing Library search for that value (reuses the search modes
  above — no new search logic, just prefills the search box and executes).
- **Right-click a chip:** opens a small popover to re-rate that tag's Weight in place, without
  opening the full Issue Properties Editor. Category editing is not available here — only Weight
  (see Issue Properties Editor below for Category).

### Issue Properties Editor

The existing Genre/Tags field editors gain a per-value Category dropdown (free text, with
previously-used categories offered as suggestions) and a 4-tier Weight picker next to each tag
entry. This is the only place Category is set; the Detail-screen popover is Weight-only.

## Explicitly out of scope

- **Reading Lists don't carry tags at all** — noticed in passing during this design (Smart Lists
  reminded the user of it), explicitly deferred until after this feature ships. Not part of this
  spec; flagging here so it isn't lost.
- **Full MangaBaka taxonomy** (Character Archetype, Objects, hierarchical tag paths, spoiler
  flags) — the research memo itself recommends against this ("no community to maintain a taxonomy
  that rich"). This spec builds the lighter categorized model the memo suggested instead.
- **Per-source score row** — separate gap named in the same memo, unrelated to tags, not part of
  this spec (see `apply-from-provider-design.md:38`).
- **A dedicated tag-dictionary/vocabulary management screen** — Category stays a per-`IssueTag`
  free-text field, not a globally-enforced controlled vocabulary. The same tag value could end up
  with inconsistent categories across different issues; that's an acceptable tradeoff for a
  personal library manager, correctable via the bulk editor if it ever matters.

## Implementation phasing

Nine surfaces are touched in total; implementation ships in four independently-verifiable phases
rather than one large change:

- **Phase A** — `IssueTag` entity + migration, ComicInfo.xml import/export diff-not-replace,
  `BulkFieldRegistry` rewire. The foundation everything else sits on.
- **Phase B** — Smart Lists + Library search-mode rewire. Mechanical once Phase A exists.
- **Phase C** — Detail-screen chips: weight styling, Category grouping, the new Tags row,
  left-click-to-search, right-click reweight popover.
- **Phase D** — Issue Properties Editor's Category dropdown + Weight picker.

## Testing

- Unit tests for the migration step (CSV → `IssueTag` rows, correct `Weight=Unset`/`Category`
  defaults) against real Genre/Tags fixtures already in the test suite.
- Unit tests for diff-not-replace on both the ComicInfo.xml import path and the bulk-editor `Set`
  path — the case that matters most is "an existing weighted tag survives a re-import untouched."
- Unit tests for Smart Lists/search-mode queries against the `Field`-discriminated table, reusing
  existing Smart List test patterns.
- ViewModel tests for `DetailPillsViewModel`'s max-weight-across-series aggregation and the two
  new chip interactions (click → search, right-click → reweight popover).
- On-screen verification via this project's UI-automation harness: tag a comic's Genre/Tags with
  categories and weights, confirm chip styling reflects weight, confirm series-level aggregation
  picks the max, confirm click-to-search actually filters the Library, confirm a rescan doesn't
  clobber existing weights.
