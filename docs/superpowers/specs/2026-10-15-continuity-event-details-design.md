# Continuity event details

**Date:** 2026-10-15
**Status:** Approved and implemented
**Phase:** 3 of the ordered Reading Lists / Continuity improvements

## Goal

Give each event in a Continuity's “Events in order” section enough context to identify and understand it—its own artwork, description, tags, and related events—while preserving the Continuity screen's visual identity and the existing Reading Lists experience.

## Repository and provider findings

- `ContinuityOverviewView` currently lists events in their existing order, showing years, issue/read counts, reading progress, and a relationship cue below connected events.
- `OverviewEventRow` already projects event name, dates, member counts, read counts, and a related-event cue.
- `StoryEvent` already stores `Description`, `StartDate`, `EndDate`, ComicVine/Metron arc IDs, and event relationships. `Description` can be edited through the existing event edit dialog; the dates have no edit controls today.
- Story Events do not currently own a cover or event-tag collection.
- ComicVine and Metron arc-overview adapters already return an arc description and cover URL. CBL Manager also stores source descriptions and cached covers for source-linked reading lists; these details remain separate from ComicRack CE's ordered book-ID list.
- ComicRack CE has no matching Story Event/Continuity surface. This is a deliberate PaperBunkr-specific extension.

## Decisions

1. Keep the existing “Events in order” section and event ordering. Each event row can expand to show its own cover, description, tags, and related events. Existing year/date and reading-progress information remains visible.
2. Reuse `StoryEvent.Description` as the event summary. Provider text may fill an empty description; existing non-empty text is treated as user-owned and is never overwritten by source refresh. Keep description editing in the existing Edit Event workflow.
3. Add event-owned tags. Show manually curated event tags alongside distinct Genre and Tags values from member issues, visibly marking the issue-derived values. Do not infer tag weights.
4. Show existing related-event relationships as clickable entries with their relation labels. Preserve current inter-row chronology cues; do not create new relationships from title similarity.
5. Give a Story Event its own cover. A manually chosen cover is authoritative; otherwise prefer Metron artwork, then ComicVine artwork. Only fall back across providers when the event's existing provider associations do not conflict. A manually chosen cover survives refresh.
6. Keep the Continuity event row compact when collapsed and use an explicit expand/collapse affordance. Do not introduce the Reading List spotlight/header or make Continuity look like a Reading List.

## Design

### Event summary row

The collapsed row retains the existing Continuity styling, chronology, member/read counts, progress bar, and related-event cue. The expanded detail area adds the Story Event cover and full description, event tags, and a compact related-event group with relation types. A source attribution may appear with the event details where provider IDs are known. Related-event links open the existing event page; the current Continuity ordering is not changed.

### Event tags

Persist directly curated event tags as Story Event-owned tag rows. Separately derive a de-duplicated, read-only view of member-issue Genre and Tags values. Distinguish curated tags from tags contributed by comics, preserve categories, and leave weights unset for derived values. Recompute derived values when event membership or a member issue's tags change. Curated tags remain untouched and are never populated by copying the derived projection. Editing curated tags uses the existing Edit Event workflow; issue-derived tags are informational and are not edited from the event row.

### Cover and provider descriptions

Add a persistent event cover URL and provenance (`Manual`, `Metron`, or `ComicVine`), plus a local event-cover cache using the existing Reading List cover cache pattern. Provider lookups are targeted to Story Events with known provider arc IDs and missing provider-derived details, not performed on every Continuity page open. Fill descriptions/covers only when absent. Prefer Metron over ComicVine when both are available; do not mix provider data when stored identity evidence conflicts. Provider failures, offline state, or absent artwork leave the event usable with its text/fallback artwork. A manually selected cover is stored as an override and is not replaced by subsequent provider refreshes.

### Editing

Extend the existing Edit Event workflow for event tags, start/end dates, and a cover selection/clear action. Keep the existing name and description editing. The event row is for reading and navigation; it does not turn into an inline form. Reuse existing cover-picker/cache and dialog conventions where appropriate.

## Data model and migration

- Add event cover URL and typed provenance to `StoryEvent` (or an equivalent small related record if required by the existing cover-cache convention); use the existing local cover-cache mechanism where applicable.
- Add a `StoryEventTag` entity for curated tags, with value, category, and weight. Keep issue-derived tags as a separately computed projection, marked as issue-derived in the view model; do not persist them as curated event tags.
- Keep `StartDate` and `EndDate` nullable; migration must not fabricate dates from issue cover dates. Any inferred display-year fallback remains a projection, not a persisted date.
- Backfill no cover or description from guesses. Existing non-empty descriptions are preserved. Provider-linked empty fields may be filled by a bounded best-effort metadata job after migration.

## Failure and integrity behavior

- Missing provider credentials or offline state does not block opening a Continuity or event.
- Source refresh never overwrites a non-empty user description or manual cover.
- Failed image fetches retain the stored URL for retry and use the existing fallback cover rendering.
- Deleted/merged related events are handled through existing event relationship integrity behavior; no stale relation is rendered as a valid link.
- Expanding rows never changes list order, event membership, reading progress, or Reading List contents.

## Testing

- Continuity event rows preserve sort order, dates, counts, and progress when expanded/collapsed.
- Description is displayed, provider text fills only empty fields, and manual descriptions survive refresh.
- Event tags retain category/weight for curated values; issue-derived tags deduplicate and refresh on membership/tag edits without overwriting curated values.
- Related-event labels and navigation use persisted relation types and handle removed targets.
- Cover resolution prefers manual override, then Metron, then ComicVine when provider identities are compatible; conflicts block cross-provider fallback and failures show fallback artwork.
- Existing Event and Continuity edit flows remain compatible; date edits persist without changing inferred dates on unrelated events.
- Reading Lists views retain their current layout and behavior.

## Out of scope

Redesigning Reading Lists; changing Continuity event ordering or membership; inferring new event relationships; fetching source data every time a view opens; changing Event Map or Timeline layouts; new provider IDs or title-based cross-provider identity assumptions.

