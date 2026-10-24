# Guided Event and Continuity Creation

**Date:** 2026-10-16
**Status:** Approved for implementation by user on 2026-10-16
**Project:** Paperbunkr
**Feature area:** Events & Continuity

## Summary

Extend the existing New Event / New Continuity dialog into an optional guided setup flow. Keep the initial create step short, then guide the user through provider lookup, identity review, and membership setup without replacing the existing Event or Continuity pages or the per-continuity Metron catalog browser.

Events gain a structured, multi-select type taxonomy. Event templates set editable starting types and guide setup. Continuity templates guide publisher and Metron universe setup. Provider matches are reviewed before any membership or relationship is applied. Existing local records and manually curated data remain protected.

## Current implementation

- `NewEventOrContinuityViewModel` and `NewEventOrContinuityOverlay` already handle create and edit for both entity types.
- Event creation/editing supports name, description, start/end dates, curated tags, and a manual cover.
- Continuity creation/editing supports name, publisher, and description. `Continuity.Designation` and `Continuity.MetronId` already exist, although the create form does not expose designation or Metron setup.
- `StoryEvent` already stores ComicVine and Metron arc IDs, descriptions, dates, cover provenance, members, tags, and identity conflict information.
- Existing Metron universe discovery and the per-continuity catalog browser already support provider catalog browsing and series review. This feature must preserve those existing paths.
- Event membership roles and role suggestions already exist. Provider arc entries currently do not carry Paperbunkr's membership roles.
- ComicRack CE has Story Arc metadata on individual comics, but no corresponding first-class cross-series Event or Continuity workflow or Event-type taxonomy. This feature is a Paperbunkr-specific extension, not a CE parity port.

## Goals

1. Make a newly created Event or Continuity useful immediately while keeping manual creation fast.
2. Reuse Metron and ComicVine identities and existing provider/catalog infrastructure.
3. Let users review and stage provider-derived details and library memberships before applying them.
4. Persist Event types separately from descriptive tags and issue membership roles.
5. Preserve user-entered data and existing Event/Continuity memberships.
6. Keep the existing continuity-specific Metron catalog browser available and functional.

## Non-goals

- Replacing or redesigning the Events & Continuity screens.
- Removing or replacing the existing per-continuity Metron catalog browser.
- Creating Event or Continuity placeholders for provider entries that are not in the library.
- Linking records based only on similar names.
- Automatically adding provider issues or series without review.
- Automatically inferring relationships between Events.
- Adding a Continuity-type taxonomy.
- Reworking source refresh, ComicVine/Metron authentication, or the global provider clients.

## Approved decisions

### Shared create flow

Keep the existing create/edit dialog. New creation follows three stages:

1. **Basics:** select a workflow preset and enter the entity's manual details.
2. **Provider setup:** optionally search/select a provider identity and preview relevant provider information.
3. **Review:** inspect staged provider details, library matches, memberships, and optional Event relationships before applying setup.

The entity is saved after Basics so that provider setup is optional and can be resumed later. Skip, close, cancellation, or provider failure does not delete the saved entity. Event setup can be restarted from the Event's Manage menu. Continuity setup can be restarted from its detail page, alongside the existing Metron catalog browser. Staged changes are not persisted before Apply, so restarting recomputes candidates against the current library. Editing an existing Event or Continuity keeps the established direct edit behavior; guided setup is for new entities, while edit adds the Event-type controls.

### Presets

Presets are in-memory workflow choices, not persisted template records. A preset may select an Event type and orient the following steps, but it cannot silently add members, assign provider IDs, overwrite data, or create relationships.

| Preset | Kind | Starting behavior |
|---|---|---|
| Blank | Event or Continuity | No preset defaults; proceed with manual setup or skip provider setup. |
| Crossover Event | Event | Preselect the Crossover type; guide provider lookup, issue matching, and optional related-Event review. |
| Limited Series Event | Event | Preselect the Limited Series type; emphasize dates and provider lookup. |
| Publisher Universe | Continuity | Emphasize publisher and optional Metron universe selection. |
| Alternate Universe | Continuity | Expose the optional designation field and emphasize Metron universe selection. |

All defaults remain editable. The selected Event type persists as Event data if retained. The preset name itself is not saved.

### Event types

Event types are structured, multi-select classifications, distinct from freeform `StoryEventTag` values and `EventMembership.Role` values. Initial types:

- Crossover
- Limited Series
- Story Arc
- One-shot/Special
- Other

Multiple values may be selected, including overlapping cases such as an event that is both a crossover and a limited series. Existing Events remain unclassified until a user sets types. Blank leaves types unset. Freeform event tags remain available for additional descriptive terms.

### Provider identity and data safety

- Event lookup supports Metron and ComicVine arc sources; Continuity lookup supports Metron universes.
- Continuity setup searches Metron by the continuity name and universe designation, then lets the user choose a result; users are not expected to know or enter a Metron ID before searching.
- Provider IDs remain namespaced. Equal numeric values from different providers are never considered equal by themselves.
- An exact provider-ID match identifies an existing local Event or Continuity for reuse. A similar name alone does not merge or reuse a record.
- A contradictory provider identity is surfaced as a conflict and blocks silent association.
- Provider details are previewed. Only blank local fields may be filled, and only after confirmation. Existing descriptions, dates, curated tags, manual covers, and memberships are preserved.
- Provider failures, offline state, or missing credentials do not prevent manual creation. The provider step can be skipped and retried later.

### Event members and relationships

- Provider arc issue lists are matched against library issues using verified provider IDs and existing verified cross-provider identity bridges.
- Only verified library matches are offered for adding. Unmatched source issues are reported as unavailable; they do not create placeholders.
- Matched issues retain provider ordering when staged. Users choose which matches become Event members.
- Provider arc entries do not supply Paperbunkr Event roles. Use the existing role suggestion system where evidence supports a suggestion; show suggestions for review and allow changes. Do not invent a role when no evidence exists.
- Adding related Events is an optional action within the same Review stage. The user selects existing Events and a relationship type. Do not infer or create relationships automatically.
- Applying selected membership and relationship changes is a separate operation from saving the initial Event.

### Continuity members and catalog browsing

- Selecting a Metron universe presents series associated with that provider universe.
- Exact Metron series-ID matches to local Series are preselected in the review queue.
- Title-based suggestions are shown separately and remain unchecked until selected by the user.
- Applying selected series uses the existing continuity membership resolver and preserves its idempotent behavior and ordering rules.
- The existing per-continuity **Find series on Metron** catalog browser remains present and working. New guided setup reuses existing provider/catalog/matching services where suitable; it does not replace or remove the current browser.
- No unmatched provider series creates a local placeholder Series.

## Data model and migration

Add a typed, multi-valued Event classification with a relational representation suitable for filtering and queries:

- Add a `StoryEventType` enum with the approved values.
- Add a `StoryEventTypeAssignment` join entity containing `StoryEventId`, `Type`, and its `StoryEvent` navigation.
- Enforce a unique `(StoryEventId, Type)` key/index so a type cannot be assigned twice to the same Event.
- Add the assignment collection to `StoryEvent` and configure delete behavior consistently with existing StoryEvent-owned data.
- Add a backward-compatible EF Core migration. Existing Events receive no assignments and therefore remain unclassified; do not infer types from names, tags, provider IDs, or membership roles.
- Do not overload `StoryEventTag` for types. Do not add a type property or migration to `Continuity`.

Expose the existing `Continuity.Designation` field in the Alternate Universe create setup as an optional user-entered value. Provider-derived designation may fill it only when blank and after preview confirmation.

## UI behavior and surfaces

### Create dialog

- Retain the existing dark theme, dialog conventions, create/edit entry points, and accessibility behavior.
- Add a clear stage indicator and Back, Skip setup, and Continue/Apply actions.
- Keep the initial form compact. Event basics include preset, name, dates, Event types, description, curated tags, and manual cover. Continuity basics include preset, name, publisher, description, and designation when the Alternate Universe preset is selected.
- Provider result cards explain the source and available identity. A selected result opens a preview of details to fill and library members to stage.
- Review shows the changes before applying them, including provider identity, fields to fill, selected members, role suggestions, unavailable issues, and proposed related-Event relationships.
- Closing after the entity is saved leaves it available, with provider setup and memberships unchanged unless the user applied them.

### Event-type display and filtering

- Add type selection to Create/Edit Event.
- Show assigned types as compact chips in the Event hero and Events sidebar rows.
- Add type as secondary context to Event search/picker results where those results distinguish Events.
- Add an Events-area type filter, including an explicit Unclassified choice. Filtering does not alter Event membership or ordering.

### Continuity discovery

- Keep the current per-continuity Metron catalog browser, controls, and per-continuity review flow.
- Guided setup may share its catalog data and matching services, but its own staged choices remain uncommitted until the user applies them.
- Add a guided setup/recheck entry point to the Continuity detail without removing or changing the existing **Find series on Metron** browser.

## Architecture boundaries

- Keep matching, provider identity interpretation, and persistence outside the Avalonia view layer.
- Extend `NewEventOrContinuityViewModel` and `NewEventOrContinuityOverlay` or rename them only if a focused refactor is needed; do not introduce a competing editor framework.
- Reuse existing source adapters, provider clients, rate limiting, Event identity resolution, Event relationship resolution, Continuity resolver, Metron universe discovery, and continuity catalog matching.
- Add a focused setup/reconciliation coordinator only if the existing services do not provide a suitable boundary. The view model coordinates stage state and commands; it does not implement matching algorithms.
- Keep API calls cancellable. A source error or partial response must be represented honestly and must not be reported as a complete catalog result.
- Do not overwrite user decisions when reloading setup or revisiting the entity later.

## Apply and failure behavior

- Initial entity creation is committed independently after Basics.
- Provider searches and previews are read-only.
- Apply validates the selected provider identity, target entity, candidate IDs, and uniqueness constraints before mutating data.
- Apply staged provider identity/details plus selected Event member/relationship or Continuity membership changes within a transaction so they do not partially commit.
- If the target became stale or a provider identity conflict appeared after review, fail safely, retain the created entity, and return an actionable conflict message.
- Retrying setup reloads current library state and recomputes matches; it does not silently replay stale proposals.
- Skip or cancel discards only uncommitted staged changes.

## Verification

### Data and migration tests

- Event types can be assigned singly and in combinations; duplicate assignment is rejected or idempotent.
- Existing Events migrate with no inferred type.
- Event type assignments are deleted with their owning Event and do not affect `StoryEventTag` rows.
- Continuity migration preserves existing `Designation`, `MetronId`, memberships, and publisher values.

### View-model and service tests

- Every preset selects the correct kind, type, and setup focus; changing a suggested Event type persists the user's selection.
- Blank and skip paths create usable entities without provider credentials.
- Provider preview does not write to the database.
- Provider confirmation fills blanks only and preserves all manually populated fields.
- Exact provider IDs reuse the correct local Event/Continuity; name-only collisions do not merge; conflicting IDs block silent association.
- Verified issue identities are staged in source order; unavailable issues create no placeholders.
- Existing role suggestions are presented and user overrides are retained; no unsupported role is invented.
- Related Event selection uses existing relation types and is optional.
- Exact Metron series IDs are preselected; title suggestions remain separate and unchecked; existing catalog browser behavior remains covered.
- Failed/stale Apply rolls back all staged memberships/relationships while leaving the initially created Event/Continuity intact.
- Type filters and type chips handle multiple and unset Event types.

### UI checks

- Stage progression, Back, Skip setup, closing after initial save, provider loading/error/empty states, and review counts are visible and accessible.
- Existing create/edit flows and the per-continuity catalog browser remain reachable.
- Event type chips and filters follow existing theme resources and work across runtime skins.
- Keyboard focus and reduced-motion behavior follow the existing overlay/dialog patterns.

## Acceptance criteria

1. A user can create an Event or Continuity manually without using a provider.
2. Guided setup can be skipped or resumed without losing the newly created entity.
3. Event presets set editable structured Event types; multiple Event types persist independently of freeform tags.
4. Event types appear in the Create/Edit form, Event hero, sidebar, relevant pickers, and Events filter.
5. Metron and ComicVine Event arcs can be searched, previewed, and linked without overwriting populated user data.
6. Provider issue memberships are added only from reviewed, verified local matches, in source order, with existing role suggestions available.
7. Related Event relationships are optional, explicitly chosen, and applied with the user's selected relationship type.
8. Metron Continuity setup preselects exact local series identity matches and keeps title suggestions separate for review.
9. The per-continuity Metron catalog browser remains available and functions as before.
10. Existing provider identities are reused only on exact ID matches; name similarity alone cannot silently merge records.
11. Failed Apply operations do not partially apply reviewed membership or relationship changes.
12. Existing Event/Continuity details, memberships, and reading-list features are preserved.
