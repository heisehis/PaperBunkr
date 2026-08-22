# Metadata Model — Phase 4c: Reading List Overhaul

**Date:** 2026-08-17
**Status:** Approved, pending implementation
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md`, §24 ("Reading Lists"), §51 ("Reading
List vs Event"), §68 ("Phase 4: Events and reading lists"). Continues [[phase4a-continuity]] and
[[phase4b-story-events]] (this spec's optional `StoryEvent` link depends on 4b's entity existing;
otherwise independent). Phase 1-3 shipped.

## Context

Unlike 4a/4b, this is a genuine **overhaul of an existing, shipped feature**
(docs/superpowers/specs/2026-08-06-reading-lists-design.md), not greenfield. Confirmed by reading
the current entities directly rather than assuming the source doc's target shape needs building
from scratch:

```csharp
// current, as shipped 2026-08-06
ReadingList: Id, Name, SortOrder, Source (string?, dormant), ArcId (string?, dormant),
             ArcName (string?, dormant), Description, CoverImageUrl, Items
ReadingListItem: Id, ReadingListId, IssueId, GroupLabel, SortOrder
```

against the source doc's §24 target:

```
ReadingList: Id, Name, Description, Type, CreatedBy, CreatedAt, UpdatedAt
ReadingListItem: Id, ReadingListId, MediaId, Position, Role?, Notes?
```

**Gap analysis, field by field:**

- `Type` — missing. Current `Source` is a free-text, entirely dormant stub for a *different*,
  still-deferred feature (external story-arc lookup), not a typed classification of the list
  itself. Adding it.
- `CreatedBy` — **not applicable, scoping it out rather than adding a dead column.** Confirmed by
  grep: no user/account model exists anywhere in this codebase (Paperbunkr is a single-user local
  desktop app). A `CreatedBy` column with no second user to ever differ from would be exactly the
  kind of unused field Rule 2 of the source doc's own §73 warns against ("do not create schema
  fields merely to satisfy" an external spec's shape). Skipping it; revisit only if this project
  ever grows multi-user/cloud-sync support.
- `CreatedAt`/`UpdatedAt` — missing entirely. Adding both.
- `Position` — already present as `SortOrder`, functionally identical (an int used for ordering, no
  linked-list/fractional scheme in either). Not renaming — Rule 1 of §73 ("prefer extending an
  existing concept over creating a duplicate") applies directly to a same-purpose rename, and
  `SortOrder` is this codebase's established name for the same concept on `ReadingList` itself.
- `Role?`, `Notes?` — missing on `ReadingListItem`. Adding both. `Role` reuses Phase 4b's
  `EventMembershipRole` enum rather than inventing a second, near-identical vocabulary — the source
  doc doesn't specify distinct role values for reading-list items, and the same
  Prologue/Core/TieIn/Epilogue/Optional/Aftermath set applies equally well here, especially for
  `Type = Event` lists that mirror an event's own membership roles.
- `MediaId` (generic) — current `IssueId` stays as-is, not generalized. Reading-list items have
  always been real resolved `Issue` references in this codebase (confirmed by the entity's own doc
  comment: "never a dangling text reference... matches ComicRackCE's actual post-import reading-list
  model"), matching CBL's own per-issue match-key format. Generalizing to a polymorphic `MediaId`
  would be speculative — nothing in this project needs a Series-level reading-list entry today.

**Explicitly not touched this phase**, per the user's own direction mid-session: `CblReadingListIO`/
`CsvReadingListIO`, the `.cbl`/CSV wire formats themselves, and the dormant `Source`/`ArcId`/
`ArcName` fields. The user has separate, dedicated plans for a CBL manager overhaul tied to their
own ComicRack CE plugin — extending or restructuring the CBL import/export path here would risk
conflicting with that later work rather than complementing it. This phase only adds new columns and
UI for them; the existing import/export round-trip is untouched and continues to leave the new
columns at their defaults on import (no CBL-format support for `Type`/`Role`/`Notes` — CE's format
has no equivalent fields to read per the original 2026-08-06 spec's own CE verification).

## Scope

### Schema changes

```csharp
ReadingList (add):
    Type: ReadingListType (enum, default User)
    CreatedAt: DateTime
    UpdatedAt: DateTime
    StoryEventId: int? (FK StoryEvent, SetNull on delete)

ReadingListItem (add):
    Role: EventMembershipRole? (reuses Phase 4b's enum)
    Notes: string?
```

`ReadingListType`, straight from §24:

```
Official
Community
User
Event
Chronological
PublicationOrder
Recommended
Custom
```

Migration default: every existing `ReadingList` row gets `Type = User` (the correct default for
lists that were all user-created before this concept existed) and `CreatedAt`/`UpdatedAt` backfilled
to the migration's own run time (no better source of truth exists for pre-this-phase rows — same
"can't recover what was never recorded" situation Phase 1's `OpenCount` backfill was in).
`UpdatedAt` is bumped on every future edit (name change, item add/remove/reorder) — enforced in the
same view-model methods that already call `SaveChanges` today, not a DB trigger.

**`StoryEventId`, the one genuinely new connective piece this phase adds**: §51's own comparison
table says "An event can have an official reading order" and "A reading list can exist without an
event" — i.e. the link is optional and one-directional in meaning (an event doesn't require a
reading list; a reading list may represent one event's official order). Nullable FK,
`DeleteBehavior.SetNull` (deleting the `StoryEvent` shouldn't delete the reading list built from
it, just detach it back to a plain list — losing curated reading-order work because its source event
got removed would be a real regression, the same class of concern that drove `MediaRelation`'s
`Cascade` choice in the opposite direction for a *lesser*-value row). Setting `Type = Event`
without a `StoryEventId` is allowed (a user might classify a hand-built list as event-shaped without
linking it to a tracked `StoryEvent` row) — the type and the link are independent, not enforced
together.

### UI changes (`ReadingScreen`)

- Header gets a `Type` picker (defaults to `User` for `CreateNew`), shown next to the existing
  Name/Description fields.
- When `Type = Event`, an additional "Link to Event" picker appears (searches `StoryEvent.Name`,
  same in-memory `Contains` shape used everywhere else in this codebase for this kind of picker) —
  setting it stores `StoryEventId`; clearing it sets `StoryEventId = null` without changing `Type`.
- Each `ReadingListItemRowViewModel` row gets a `Role` badge/picker (optional, blank by default) and
  a `Notes` field (shown on expand/hover, not inline in the compact row — matching how `GroupLabel`
  already only shows as a section header rather than per-row clutter).
- `CreatedAt` surfaces as a small read-only timestamp in the header (`UpdatedAt` is US-internal
  bookkeeping, not shown — no existing screen in this codebase surfaces a raw "last modified"
  timestamp to the user, and there's no ask for one here beyond what the schema itself needs).

## Testing

- Migration test: existing `ReadingList`/`ReadingListItem` rows preserved with `Type = User`
  backfilled, `CreatedAt`/`UpdatedAt` set to a non-null migration-time value, new nullable columns
  (`StoryEventId`, `Role`, `Notes`) all null on pre-existing rows.
- `ReadingScreenViewModelTests`: creating a list defaults to `Type = User`; changing `Type` to
  `Event` reveals the event-link picker; linking/unlinking a `StoryEvent` updates `StoryEventId`
  without touching `Type`; setting a `Role`/`Notes` on an item persists and reloads correctly;
  editing any of the above bumps `UpdatedAt`.
- FK behavior test: deleting a linked `StoryEvent` sets the reading list's `StoryEventId` to null
  and leaves the list and its items otherwise intact (not deleted, not orphaned).
- Regression: existing `ReadingListMatcher`/`CblReadingListIO`/`CsvReadingListIO` tests continue to
  pass unmodified — confirms this phase is additive-only to the import/export path.

## Explicitly out of scope

`CreatedBy` (no multi-user model exists; see Context). Any change to `.cbl`/CSV wire formats or the
`CblReadingListIO`/`CsvReadingListIO` classes — reserved for the user's separate CBL manager work.
The dormant `Source`/`ArcId`/`ArcName` fields (still reserved for the original, still-deferred
external story-arc lookup pass — untouched, not repurposed for `StoryEventId` even though they're
adjacent in spirit, since `ArcId`/`ArcName` are specifically about *external* arc identifiers per
their own doc comment, not this project's own `StoryEvent` rows). A reverse "generate a reading list
from this event's membership" one-click action — the manual link covers the data model; an
auto-populate convenience command is a natural but separate follow-up once both screens exist and
real usage shows it's wanted.
