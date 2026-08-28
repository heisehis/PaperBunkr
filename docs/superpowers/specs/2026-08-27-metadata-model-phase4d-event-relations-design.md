# Metadata Model — Phase 4d: Event Relations

**Date:** 2026-08-27
**Status:** Approved, pending implementation
**Source doc:** Design session with Ehis (2026-08-27), continuing [[phase4b-story-events]]. Not
from the original `PAPERBUNKR_METADATA_MODEL.md` doc — Event Relations are a net-new idea,
developed and grounded against ComicRack CE source and external comic-database precedent
(Metron.cloud's `Universe`/`Arc` resources, Comic Vine's resource catalog) rather than ported from
any single source document.

## Context

Verified before designing (standing project rule): CE has no concept of one publishing event
relating to another — confirmed already for `StoryEvent` itself in [[phase4b-story-events]] (CE
has no cross-series-event concept at all), and by extension no event-to-event relation either.
This is greenfield.

`StoryEvent` (Phase 4b) already models a single event's own membership. What's missing is the same
thing Phase 3's `MediaRelation` gave series: a way to say "this event follows/crosses over
with/shares a universe with that event" — e.g. "Secret Wars (2015)" continuing "Secret Wars
(1984)", or two publishers' annual crossover events sharing characters without one containing the
other.

Rather than invent a second relation-type vocabulary, this phase reuses [[phase3-media-relations]]'s
existing `RelationType` enum and its `RelationTypeCatalog` directionality/inversion logic wholesale
— same reasoning Phase 4c already used to reuse `EventMembershipRole` for `ReadingListItem.Role`
rather than invent a near-identical second enum. The full ~20-value vocabulary stays a single
source of truth; only the *creation-UI picker* is scoped down to the subset that actually describes
a relationship between two events rather than two print runs of the same story (see below) — the
enum itself isn't split or duplicated.

## Scope

### Entities

```csharp
EventRelation
--------------
Id
SourceEventId (FK StoryEvent)
TargetEventId (FK StoryEvent)
RelationType (enum, reuses Entities.RelationType)
Notes (string?)
CreatedAt (DateTime)

EventRelationEvidence
----------------------
Id
EventRelationId (FK EventRelation)
Provider (enum, reuses Entities.RelationEvidenceProvider)
ProviderRelationType (string?)
ProviderSourceId (string?)
Confidence (decimal)
RetrievedAt (DateTime)
```

Same shape as `MediaRelation`/`RelationEvidence`, deliberately — reusing both existing enums
(`RelationType`, `RelationEvidenceProvider`) rather than declaring `EventRelationType`/
`EventRelationEvidenceProvider` duplicates. A user-created relation gets exactly one
`EventRelationEvidence` row: `Provider = User, Confidence = 1.0m`, same as every `MediaRelation`
created today — there's no automatic population path this phase either (no scanner signal, no
external event-linking provider).

**Validation**: `SourceEventId != TargetEventId` (no self-relations); no exact duplicate (same
`SourceEventId`/`TargetEventId`/`RelationType` triple) — enforced at creation time in the resolver,
matching `MediaRelationResolver.TryCreate`'s existing style, not a DB constraint.

**FK behavior**: both `SourceEventId`/`TargetEventId` use `DeleteBehavior.Cascade`. Confirmed
against real code before choosing this (not copied blind from `MediaRelation`'s reasoning, which is
about *Series* deletion specifically): `EventsScreenViewModel.DeleteEvent` already cascades
`EventMembership` on event deletion behind a `TwoStepConfirm` (docs/superpowers/specs/2026-08-22-
delete-functionality-design.md), and `ReadingList.StoryEventId` already uses `SetNull` rather than
block the delete. An `EventRelation` pointing at a deleted event is exactly the same situation
`EventMembership` is already in — `Cascade` just removes a relation that's lost one of its two
endpoints, consistent with how this codebase already treats every other row hanging off a
`StoryEvent`.

### Relation-type picker scope

The creation UI (below) doesn't expose all ~20 `RelationType` values — showing `Adaptation`/
`SourceMaterial`/`Variant`/`DifferentEdition`/`SecondPrinting`/`Compilation`/`Contains`/
`CollectedFrom` for an *event* would be nonsensical (those describe a single work's print history,
not two separate cross-series storylines relating to each other). The picker exposes: `Prequel`,
`Sequel`, `Continuation`, `Crossover`, `SameUniverse`, `SharedUniverse`, `Related`, `Other` — the
subset that actually describes how one publishing event relates to another. `RelationTypeCatalog
.All`'s existing directionality/inversion data (Prequel/Sequel as a named inverse pair, the rest
symmetric) applies unchanged; no new catalog entries needed since these are all already-classified
values from Phase 3.

### `EventRelationResolver`

New file, `src/Paperbunkr.Data/Metadata/EventRelationResolver.cs`, mirroring
`MediaRelationResolver`'s shape exactly:

```csharp
public static class EventRelationResolver
{
    public static IReadOnlyList<(StoryEvent OtherEvent, RelationType DisplayType)> GetRelatedEvents(PaperbunkrDbContext context, int storyEventId);
    public static bool TryCreate(PaperbunkrDbContext context, int sourceEventId, int targetEventId, RelationType relationType);
    public static void Remove(PaperbunkrDbContext context, int eventRelationId);
}
```

Same source/target inversion logic as `MediaRelationResolver.GetRelatedSeries`: a row where the
queried event is the source displays `RelationType` as-stored; a row where it's the target displays
`RelationTypeCatalog.All[RelationType].DisplayFromTargetSide`.

### UI: Connected Events

`EventsScreenViewModel`'s detail pane (currently: name, description, ordered member list) gets a
new "Connected Events" section below the member list:

- Lists each related event as a card (name + relation-type label), reusing `StoryEventSearchResult`
  — already built for Phase 4c's "Link to Event" reading-list picker — for the "+ Connect Event"
  search-and-pick flow, so no new search infrastructure is needed.
- A remove/unlink action per card, same shape as the Related tab's existing unlink action on
  `MediaRelation` cards.
- Clicking a connected event's card switches the screen's active event to it (`LoadEvent`) — a
  natural way to walk an event chain (e.g. Secret Wars '84 -> Secret Wars 2015 -> its own sequels)
  without leaving the screen.

## Testing

- Entity/migration tests: `EventRelation`/`EventRelationEvidence` table shape, cascade delete both
  directions (removing an `EventRelation` removes its `EventRelationEvidence`; deleting a
  `StoryEvent` removes any `EventRelation` referencing it), existing rows untouched (additive-only
  schema).
- `EventRelationResolverTests`: source-side lookup (type as-stored), target-side lookup for a
  named-inverse pair (`Prequel`/`Sequel`) and a symmetric type (`Crossover`), duplicate/self-relation
  attempts rejected with no DB mutation, `Remove` is a no-op on an already-gone id.
- `EventsScreenViewModelTests`: connecting two events makes each appear in the other's Connected
  Events section with correctly inverted labels; removing clears both; clicking a connected event's
  card loads it as the active event.

## Explicitly out of scope

External-provider-sourced event relations (no provider exists yet, same reasoning as Phase 3/4b). A
dedicated event-relationship graph/timeline visualization spanning more than two hops — this phase
is the same flat per-event card list Phase 3 shipped for series, not a graph view; a fuller
visualization is a natural follow-up once real connected-event data exists ([[phase4g-age-
progression]] separately covers the timeline visualization for age progression, a different
concept). Auto-suggesting event relations from Format/date proximity — that's [[phase4e-format-
signal-suggestions]]'s subject, scoped to event *membership* suggestions, not event-to-event
connections; extending suggestion logic to relations between events is a plausible future step, not
this phase.
