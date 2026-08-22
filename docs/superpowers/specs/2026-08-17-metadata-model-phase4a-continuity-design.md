# Metadata Model — Phase 4a: Continuity

**Date:** 2026-08-17
**Status:** Approved, pending implementation
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md` (79-section architectural spec, now
read in full this session — not accessible in earlier Phase 1-3 sessions, which worked from partial
context), §23.4 ("Continuity") and §65 (Detail-page "Same Continuity" consumer). Phase 1-3 are all
shipped.

## Context

§68's own numbered Phase 4 list ("Events and reading lists") only names `StoryEvent`,
`EventMembership`, `ReadingList`, `ReadingListItem` — `Continuity` isn't explicitly phase-tagged
anywhere in §68. It's grouped with the event entities in §74's "Recommended Initial Entity Set"
(right after `EventMembership`, before `ReadingList`), and both the Phase 1 and Phase 3 specs
already recorded it as bundled into "Phase 4" in their own out-of-scope notes. Splitting this out
as its own lettered slice (4a, ahead of 4b/4c) rather than folding it into the events work: it's
small (one entity + one join, no membership ordering/role concepts to design), fully independent of
`StoryEvent`, and follows the size-management precedent Phase 2 set (2a/2b/2c) for a source-doc
phase that bundles more than one real concept.

Confirmed via real code before designing (standing project rule): no `Continuity` concept exists
anywhere in this codebase or in `_reference/ComicRackCE` (grepped `Continuity` — zero hits outside
the Phase 1/3 design-doc out-of-scope notes). This is greenfield, not a port.

Distinct from Phase 3's `MediaRelation.SameContinuity`/`SharedUniverse` relation types (still
correct and unchanged): those are pairwise, user-asserted relations between two specific series.
`Continuity` is a **named, first-class grouping** ("Earth-616", "DC Prime Earth") that many series
can join at once, letting "what else is in this continuity" be a single query instead of an
all-pairs relation web. The source doc's own §49 ("Similarity vs Relationship") distinction and
§25.1's explicit rejection of ad-hoc pairwise-only modeling both support keeping these as separate
mechanisms rather than expressing continuity membership as N `MediaRelation` rows.

## Scope

### Entities

```csharp
Continuity
----------
Id
Name (string)
Description (string?)
Publisher (string?)
CreatedAt (DateTime)
UpdatedAt (DateTime)

SeriesContinuity
----------------
SeriesId (FK Series, part of composite PK)
ContinuityId (FK Continuity, part of composite PK)
```

`SeriesContinuity` is a pure join with no attributes of its own (unlike `MediaRelation`, which
needed `RelationType` and so needed its own `Id` + evidence collection) — composite PK
`(SeriesId, ContinuityId)`, no separate `Id` column. Both FKs use `DeleteBehavior.Cascade`, matching
Phase 3's `MediaRelation.SourceSeriesId`/`TargetSeriesId` precedent and for the same reason: every
existing Series-deletion path (`SeriesReassignmentResolver.Apply`, `NeedsReviewViewModel
.MergeSeriesInto`) is automatic empty-series cleanup with no interactive moment to check for
continuity membership first. Deleting a `Continuity` itself cascades to its `SeriesContinuity` rows
(a continuity with no series left in it is just an empty grouping, not a data-loss concern the way
losing a series' own metadata would be).

A series can belong to more than one continuity (source doc: "An issue/series can belong to one or
more continuities if necessary") — e.g. a crossover-only series claimed by two publishers'
continuities. This phase scopes continuity membership to **Series**, not Issue, matching Phase 3's
`MediaRelation` granularity and the source doc's own examples (`DC Prime Earth`, `Marvel
Earth-616`) — these are whole-series-line concepts, not something that varies issue-to-issue within
one series.

### `ContinuityResolver`

New file, `src/Paperbunkr.Data/Metadata/ContinuityResolver.cs`:

```csharp
public static class ContinuityResolver
{
    public static IReadOnlyList<Continuity> GetContinuities(PaperbunkrDbContext context, int seriesId);
    public static IReadOnlyList<Series> GetSeriesInContinuity(PaperbunkrDbContext context, int continuityId);
    public static IReadOnlyList<Series> GetOtherSeriesSharingContinuity(PaperbunkrDbContext context, int seriesId);

    public static Continuity GetOrCreate(PaperbunkrDbContext context, string name);
    public static bool AddSeriesToContinuity(PaperbunkrDbContext context, int seriesId, int continuityId);
    public static void RemoveSeriesFromContinuity(PaperbunkrDbContext context, int seriesId, int continuityId);
}
```

`GetOrCreate` does a case-insensitive name match before inserting — the assignment UI below is a
combo-box-with-create (type a name, pick an existing match or create new), so duplicate
near-identical continuities ("Earth-616" vs "earth-616") from careless typing need the same
guardrail Phase 2c's field-descriptor combo boxes already use elsewhere in this codebase.
`AddSeriesToContinuity` is a no-op returning `false` if the pairing already exists (same idempotent
shape as `MediaRelationResolver.TryCreate`'s duplicate check, though here duplicates are cheap to
detect via the composite PK rather than needing an explicit query).

### Assignment UI

No dedicated "Continuities" management screen this phase — there's no existing precedent in this
codebase for a flat-entity list screen with nothing else attached to it (Reading Lists' screen
exists because reading lists have real ordered content to browse; a bare continuity has none). The
Related tab is the natural single home, same reasoning Phase 3 used for `MediaRelation`: it's
already the place series-to-series/series-to-grouping relationships are surfaced and edited, and a
second scattered entry point would just fragment where a user looks for this.

`DetailTabsViewModel`'s Related tab gets:

- A "Continuities" section (separate from the existing relation carousel) listing the current
  series' continuities as removable chips, plus a "+ Add to Continuity" action opening a
  combo-box: type to search existing continuities (case-insensitive `Contains` on `Name`, same
  in-memory search shape as the relation-target picker) or type a new name and confirm to create
  it via `GetOrCreate`.
- Removing a chip calls `RemoveSeriesFromContinuity`.

### Related-tab "Same Continuity" wiring

Phase 3 shipped the Related tab as a flat, ungrouped carousel and explicitly deferred "the source
doc's §65 grouping" to whenever real data existed to make sections meaningful. This phase adds the
first real section split without fully building out §65's entire suggested taxonomy (Same Series /
Crossovers / Prequels-Sequels / etc. — those still come from `MediaRelation` types, already
populated, already correctly labeled, not worth re-grouping speculatively before Phase 4b's "Same
Event" gives a second real reason to):

- `Related` (the existing `MediaRelation`-backed carousel) stays exactly as-is.
- A new, separate `SameContinuity` collection on `DetailTabsViewModel`, populated by
  `ContinuityResolver.GetOtherSeriesSharingContinuity`, rendered as its own labeled section below
  Related when non-empty. This is additive (new property, new AXAML block) — no change to the
  existing `Related` carousel or its tests.

## Testing

- Entity/migration tests: `Continuity`/`SeriesContinuity` table shape, cascade delete both
  directions (deleting a `Continuity` clears its `SeriesContinuity` rows; deleting a `Series` clears
  its `SeriesContinuity` rows without touching the `Continuity` row itself or other members),
  existing rows untouched (purely additive schema).
- `ContinuityResolverTests`: `GetOrCreate` returns the existing row on a case-insensitive name
  match rather than duplicating; `AddSeriesToContinuity` is idempotent (second call on the same
  pair is a no-op, returns `false`); `GetOtherSeriesSharingContinuity` excludes the queried series
  itself and returns series from every continuity it's a member of, deduplicated if two series
  share more than one continuity in common.
- `DetailTabsViewModelTests`: adding a series to a continuity (including via typing a brand-new
  name) makes it appear as a chip and makes sibling series appear under Same Continuity; removing
  it clears both.

## Explicitly out of scope

A dedicated Continuities management/browse screen (no existing precedent for a bare-entity list
screen in this codebase; revisit if continuity data grows enough to need one). Full §65 Related-tab
categorization (Same Series/Crossovers/Prequels-Sequels sections) — only the new Same Continuity
split is added this phase. Publisher-scoped continuity suggestions or auto-detection (e.g.
suggesting "DC Prime Earth" based on `Series.Publisher`) — every continuity assignment this phase is
manual, matching Phase 3's "no automatic population path yet" reasoning. `StoryEvent`/
`EventMembership` (Phase 4b) and the `ReadingList` overhaul (Phase 4c) — separate specs.
