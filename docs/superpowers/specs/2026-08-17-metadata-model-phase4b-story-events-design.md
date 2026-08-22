# Metadata Model — Phase 4b: Story Events

**Date:** 2026-08-17
**Status:** Approved, pending implementation
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md`, §23.3 ("Event"), §50 ("Event
Membership"), §51 ("Reading List vs Event"), §68 ("Phase 4: Events and reading lists"), and §65
(Detail-page "Same Event" consumer). Continues [[phase4a-continuity]] (independent, already
specced). Phase 1-3 shipped.

## Context

Confirmed via real code and CE source before designing (standing project rule): `StoryEvent`/
`EventMembership` don't exist anywhere in this codebase, and CE has no equivalent concept at all —
`_reference/ComicRackCE` was grepped for any domain notion of a cross-series publishing event; the
only hits are C#'s `event EventHandler` keyword, pure noise. This is greenfield, not a port, same
situation Phase 3 was in for `MediaRelation`.

**Event vs Reading List vs StoryArc — three concepts this project now has, each answering a
different question, per the source doc's own §51 table:**

- `Issue.StoryArc` (existing, plain string, CE-ported): "what narrative arc is this one issue part
  of" — unstructured, single-series-scoped in practice, stays exactly as-is. Source doc §23.2 marks
  promoting this to a first-class `StoryArc` entity as a *future* step, not this phase.
- `ReadingList` (existing, Phase 4c overhaul): "read these in this order" — order is essential,
  Core/Tie-in roles optional, can exist with no event behind it at all.
- `StoryEvent` (this phase, new): "these publications belong to this named cross-series
  publishing/story event" — order is useful but optional, Core/Tie-in-style roles are the point of
  the feature (§50's own worked example is entirely role-driven: Prologue → Core → Core → TieIn).

The source doc is explicit that these must not collapse into each other (§51: "Do not conflate
them"). This phase does not merge `StoryEvent` into `ReadingList` or vice versa — they stay two
entities. Phase 4c does add one optional link between them (a `ReadingList` can *represent* an
event's official reading order) once both exist; see that spec.

## Scope

### Entities

```csharp
StoryEvent
----------
Id
Name (string)
Description (string?)
StartDate (DateTime?)
EndDate (DateTime?)
CreatedAt (DateTime)
UpdatedAt (DateTime)

EventMembership
----------------
Id
StoryEventId (FK StoryEvent)
IssueId (FK Issue)
Position (int)
Role (enum)
```

**Granularity: Issue, not Series.** §50's worked example names specific issues ("Green Lantern
Annual #1", "Green Lantern #13") with different roles within the *same* series in the *same*
event — a per-series membership couldn't express that. This is a deliberate difference from Phase
3's `MediaRelation` (Series-scoped) and Phase 4a's `SeriesContinuity` (Series-scoped): those
describe whole publication lines relating to each other or to a universe, while event membership is
inherently about specific single issues' role in one story, matching `ReadingListItem`'s existing
Issue-level granularity.

`IssueId` uses `DeleteBehavior.Restrict`, matching `ReadingListItem.Issue`'s existing precedent (not
`MediaRelation`/`SeriesContinuity`'s `Cascade`) — deleting an Issue that's a tracked event member
should be a conscious action, not silent membership loss, the same reasoning that already governs
reading-list membership. `StoryEventId` uses `Cascade` (deleting the event itself should clear its
membership rows, nothing left to protect).

**No provenance/evidence table this phase**, unlike `MediaRelation`/`RelationEvidence` — the source
doc's §27 ("Relation Provenance") is scoped explicitly to media relations; there's no equivalent
"Event Provenance" section anywhere in the doc, and every event this phase is manually curated (no
scanner signal, no external provider until Phase 5). Revisit if/when Phase 5 adds a provider that
can supply event data (e.g. GCD's own event indexing, noted in §34).

### `Role` (`EventMembershipRole`)

Straight from §50's worked example and §23.3's suggested list, no extension needed this time (this
vocabulary is small and already complete in the source doc, unlike Phase 3's relation types which
needed the project's own classification work for the untagged two-thirds):

```
Prologue
Core
TieIn
Epilogue
Optional
Aftermath
```

### `EventMembershipResolver`

New file, `src/Paperbunkr.Data/Metadata/EventMembershipResolver.cs`:

```csharp
public static class EventMembershipResolver
{
    public static IReadOnlyList<EventMembership> GetOrderedMembers(PaperbunkrDbContext context, int storyEventId);
    public static IReadOnlyList<StoryEvent> GetEventsForSeries(PaperbunkrDbContext context, int seriesId);
    public static IReadOnlyList<Series> GetOtherSeriesInSharedEvents(PaperbunkrDbContext context, int seriesId);

    public static bool AddMember(PaperbunkrDbContext context, int storyEventId, int issueId, EventMembershipRole role);
    public static void RemoveMember(PaperbunkrDbContext context, int eventMembershipId);
    public static void Reorder(PaperbunkrDbContext context, int eventMembershipId, int offset);
}
```

`GetEventsForSeries`/`GetOtherSeriesInSharedEvents` join through `EventMembership.Issue.SeriesId` —
an event's membership is per-issue, but "does this series participate in this event" and "what
other series share an event with this one" (the Related-tab query below) both need to answer at the
series level, same as how `ReadingScreenViewModel` already aggregates issue-level items up for
display. `AddMember` rejects an exact duplicate (same `StoryEventId`/`IssueId` pair) — an issue
holds exactly one role in a given event, re-adding it should edit the role via remove-then-add
rather than create a second row. `Reorder` mirrors `ReadingScreenViewModel.Reorder`'s existing
swap-adjacent-`Position`-values approach exactly (same codebase idiom, no new pattern needed).

### Events screen

Reading Lists already proves out the right shape for "named collection you browse via a sidebar,
with an ordered, addable/removable, reorderable list of issues" — Events is structurally the same
shape with roles added, so this phase adds a new screen built the same way rather than inventing a
different UI pattern:

- `EventsScreenViewModel` (new, mirrors `ReadingScreenViewModel`): sidebar of `StoryEvent`s (own
  `StoryEventSummary` model — `Id`/`Name`/`MemberCount`/`IsActive`, same shape as
  `ReadingListSummary`), detail pane showing the active event's `Name`/`Description`/date range and
  its members ordered by `Position`, each row showing the issue (`Series #Number`, reusing
  `ReadingScreenViewModel.Search`'s exact issue-search shape for the "add issue" flow) and a `Role`
  picker/badge. Reorder via the same up/down affordance as `ReadingListItemRowViewModel`'s existing
  rows. No CBL/CSV import-export — that's a Reading-List-specific format tied to CE/CBLManager
  compatibility (per this project's own reading-list history and the user's separate, still-pending
  CBL manager work) with no equivalent for a concept CE never had.
- `EventMemberRowViewModel` (new, parallels `ReadingListItemRowViewModel`): wraps one
  `EventMembership`, exposes the issue display label, `Role`, and up/down/remove commands.
- New nav entry, wired in `MainViewModel` exactly like every other screen there (`Events` property,
  `IsEvents` computed flag added to the existing `CurrentScreen` switch, `ShowContextualSidebar`
  extended to include it — matching how `Reading` already participates in that same set of
  properties).

### Related-tab "Same Event" wiring

Same additive pattern as Phase 4a's "Same Continuity" section — a second new, separate collection
on `DetailTabsViewModel`:

- `SameEvent` collection, populated by `EventMembershipResolver.GetOtherSeriesInSharedEvents`,
  rendered as its own labeled section alongside (not replacing) the existing `Related` carousel and
  Phase 4a's `SameContinuity` section. No cross-linking into the new Events screen from this card
  this phase (e.g. "view full event") — clicking through to browse the actual event membership list
  is a natural follow-up, not required for the section to be useful as a signal.

## Testing

- Entity/migration tests: `StoryEvent`/`EventMembership` table shape, `Restrict` on `IssueId`
  (deleting an Issue that's an event member throws/is blocked, matching `ReadingListItem`'s existing
  tested behavior), `Cascade` on `StoryEventId` (deleting an event clears its membership), existing
  rows untouched.
- `EventMembershipResolverTests`: `GetOrderedMembers` returns members sorted by `Position`;
  `AddMember` rejects an exact duplicate issue-in-event pair; `Reorder` swaps adjacent positions
  the same way `ReadingScreenViewModelTests` already verifies for reading-list items;
  `GetOtherSeriesInSharedEvents` excludes the queried series, dedupes a series appearing in more
  than one shared event.
- `EventsScreenViewModelTests`: create event, add issue with role, appears in ordered member list;
  remove clears it; reorder swaps positions; sidebar summary counts update.
- `DetailTabsViewModelTests`: a series with an issue in a shared event shows the other
  participating series under Same Event; a series with no event membership shows nothing (no
  regression to the existing empty-state handling).

## Explicitly out of scope

Promoting `Issue.StoryArc` to a first-class entity (source doc's own §23.2, explicitly future).
Event provenance/evidence tracking (no source-doc section calls for it this phase; revisit at
Phase 5 if an external provider can supply event data). Linking a `ReadingList` to a `StoryEvent`
as its official reading order — that's Phase 4c's concern once `StoryEvent` exists to link to.
Homepage "Complete This Event" module (§66, Phase 6 territory — `Recommendation`, built on top of
this data, not part of it). CBL/CSV import-export for events (no CE precedent, no format to import
from).
