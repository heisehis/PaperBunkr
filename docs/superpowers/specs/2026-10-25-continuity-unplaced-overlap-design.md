# Continuity Unplaced Events and CBRO Overlap Notes

**Date:** 2026-10-25  
**Status:** built, uncommitted (confirmed 2026-10-28 via source: `AddEventOrderOverlapNotes` migration and the overlap-note parsing in `ComicBookReadingOrdersSource`; on-screen check outstanding)  
**Project:** Paperbunkr  
**Feature area:** Events & Continuity

## Summary

Complete the remaining Continuity tooling gaps identified in the 2026-10-12 continuity-sources
work: give events without usable chronology a dedicated terminal region in the continuity map, and
parse Comic Book Reading Orders (CBRO) same-time notes into non-directional overlap connectors.
Keep the Metron universe picker in the guided Continuity setup flow; the approved 2026-10-16
guided-creation work already provides search, selection, and review of universe series, so it is
not a new implementation item here.

## Context and verified facts

- The continuity map currently orders all events through `EventChronology`, then lays their member
  issues into event blocks. An event without date/rank is still placed into the main event sequence.
- The map's existing horizontal lanes represent series, while event blocks form the chronology
  ruler. The new Unplaced region is an event-map block region, not a fictitious date or an extra
  series lane.
- CBRO timeline entries currently retain list key, rank, title, year, and URL. Their parser strips
  markup to derive those fields and discards same-time note text.
- The live CBRO Marvel and DC timelines currently use the phrasings “Takes place during,” “Takes
  place simultaneously with,” and “Occurs during the same time as.” These are source annotations,
  not reading-order claims.
- The existing `EventRelation` and smart chronology model distinguishes directional relations
  from unordered event relations. CBRO overlap evidence must not become a Prequel/Sequel relation
  or change chronology.
- ComicRack CE has no first-class Continuity/Event workflow or equivalent picker, Unplaced region,
  or same-time relationship. This feature extends Paperbunkr's continuity tools rather than
  reproducing CE behavior.

## Approved decisions

### Metron universe selection

- Preserve the existing Metron universe search and selection in guided Continuity creation.
- Do not add a second picker or replace the per-continuity “Find series on Metron” catalog browser.

### Unplaced region

- An event is Unplaced when it has no usable chronology date, no source rank, and no saved
  directional relation that establishes its position. Events with a usable date, provider rank, or
  directional relation remain in the main chronology.
- Render Unplaced events in a distinct terminal region inside the continuity map, after the ordered
  timeline. Give it the “Unplaced” label; its cards have no implied date and sort by event name.
- Keep Unplaced events selectable and subject to the existing event visibility, optional-issue,
  and Events-only filters. The region is quiet: no alert badge and no notification.
- Do not change publication-order maps, continuity membership, reading-list order, or persisted
  event dates/ranks to create this region.

### CBRO overlap annotations

- Parse the three approved equivalent phrases: “Takes place during,” “Takes place simultaneously
  with,” and “Occurs during the same time as.” Retain each original note and its referenced event
  title as CBRO evidence.
- Resolve a note only when its target matches exactly one local event in the same publisher
  timeline, using normalized event title and the year when CBRO provides one. If the reference is
  missing, ambiguous, or conflicts, retain and show the note but draw no connector.
- Draw resolved notes as a non-directional map-ruler bracket labeled “Occurs during.” Keep these
  edges separate from persisted `EventRelation` rows and from the Prequel/Sequel relationship
  editor. They never influence event sorting or smart-connector inference.
- Deduplicate reciprocal notes into one connector while preserving the individual source notes.
- Show all CBRO overlap notes in the event's map details/tooltip, including unmatched and
  ambiguous references. A resolved note also has the map connector.

## Data and refresh behavior

- Extend CBRO timeline parse results with zero or more structured overlap notes (original text and
  parsed target name); keep the existing rank and identity fields unchanged.
- Persist each matched event's CBRO notes as source evidence associated with its existing
  `EventOrderEvidence` row. Refresh replaces that event's previous CBRO overlap-note set together
  with its current CBRO rank evidence, so removed upstream notes do not remain stale.
- Resolve target events against current local event identities when building the continuity map;
  store the CBRO reference, not a permanent local-event assertion. Unmatched notes can therefore
  resolve after the library gains a matching event.
- Build overlap connectors from this evidence as a distinct connector kind. Existing user,
  inferred, Wikidata, and Metron relation data remains untouched.

## Map behavior

- Partition eligible Unplaced events from the normal `EventChronology` sequence before building
  blocks. Append their event blocks to the terminal region in case-insensitive title order.
- Preserve each event's own issue order, role, outside-series rows, and current event selection
  behavior. Existing first-appearance deduplication for issues belonging to more than one event
  still applies.
- Keep overlap edges independent from block order. They connect the resolved event bands even when
  one or both are Unplaced; edge direction is never implied.
- Reuse the event ruler's existing non-directional bracket geometry and hover/click tooltip
  behavior, with a distinct “Occurs during” label and CBRO provenance in the tooltip.

## Non-goals

- No new Continuity taxonomy, automatic event relation, chronology inference, or future import
  rule.
- No changes to ComicVine, Metron, Wikidata, or GCD chronology evidence beyond displaying CBRO
  overlap evidence.
- No event/date editing workflow or change to the continuity map's series lanes.
- No change to ComicRack CE parity claims; CE does not have these cross-event tools.

## Verification plan

- Parser fixtures cover all three note phrasings, HTML entities, multiple notes, and paragraphs
  without notes; existing timeline rank parsing remains unchanged.
- Matching tests cover same-list unique title/year matches, missing targets, ambiguous duplicate
  titles, conflicting years, and references that become resolvable on a later map load.
- Sweep tests verify refresh replaces stale notes and leaves other event-order providers intact.
- Map-builder tests cover all Unplaced criteria, directional-link inclusion, terminal placement,
  name order, filters, hidden events, optional members, and issue deduplication.
- Connector tests verify non-directional rendering/tooltip provenance, reciprocal deduplication,
  and no mutation of `EventRelation` or chronology order.
- Run targeted Continuity and event-order tests, then build `Paperbunkr.App` in the main Debug
  configuration and launch it to verify the Avalonia XAML weave and map behavior.

## Source references

- [Continuity sources design](2026-10-12-continuity-sources-design.md)
- [Guided Event and Continuity creation](2026-10-16-guided-event-continuity-creation-design.md)
- [Continuity map and smart connector](2026-09-27-continuity-map-design.md)
- [CBRO Marvel Event Timeline](https://comicbookreadingorders.com/marvel/event-timeline/)
- [CBRO DC Event Timeline](https://comicbookreadingorders.com/dc/event-timeline/)
