# Metron Continuity Discovery and Needs Attention Bulk Actions

**Status:** Approved for implementation (2026-10-16)

## Problem

The former **Check Wikidata** command scanned eligible library series and proposed universe memberships in Continuity's **Suggestions & checks** area. Replacing it with **Find series on Metron** on an individual Continuity page changed the job: it asked for one universe and browsed that universe's series, rather than scanning the user's library. Separately, the Continuity page's **Needs attention** flyout lists series present in attached events but absent from the current Continuity; it currently only offers one-at-a-time Add actions.

## Decisions

1. Restore the library-wide workflow as **Check Metron** in **Suggestions & checks**.
2. Scan local series that have usable Metron issue identities, or verified ComicVine/UPC references that the existing Metron issue lookup can resolve. Read actual Metron issue `universes` associations. Do not infer universe membership from title similarity.
3. Keep the scan read-only. Put findings in the existing **Shared universes** review queue. Users can select rows, select all/clear, and bulk accept/dismiss; existing per-row Accept/Dismiss remains.
4. Accept creates or reuses the Continuity by Metron universe ID (name only as a fallback where the provider returned no ID), records the provider metadata, and adds the series using `ContinuityResolver`. Existing memberships and user-created Continuities are preserved.
5. In the screenshot's **Needs attention** flyout, add row selection, Select all/Clear, and Add selected. Keep each row's Add button. Selection does not alter the stored series order; applying uses the existing membership resolver.
6. These are separate queues: Metron suggestions group library-wide provider findings; Needs attention remains the current continuity's event-derived outside-series list.

## Existing architecture and constraints

- `ContinuityWikidataMatchResolver` and `ContinuitySuggestion` define the existing review model and dismissal semantics. Metron results should use a distinct provider ID field; never encode a Metron ID as a Wikidata QID or Fandom key.
- `ContinuityMetronSeriesLookup` already resolves up to four issue references per series and reads `ComicVineIssueDetails.Universes`. Separate discovery from persistence so Check Metron can stage results without creating or changing Continuities.
- `ContinuityMetronMatchResolver` remains the existing scraper-time path. The new library-wide scan should reuse its universe identity semantics and `ContinuityResolver`, not add another membership implementation.
- `ContinuityOverviewBuilder` computes the outside-series queue; `ContinuityPageViewModel` owns its row state and membership commands; `ContinuityOverviewView` renders the flyout.
- The per-continuity Metron catalog remains a separate browse-and-add workflow. Metron universe membership remains authoritative, while exact Metron series IDs and Metron's returned ComicVine/GCD series IDs can link catalog rows to local series. These bridges are matched only against the corresponding stored provider identity; titles are never used to assert identity, and contradictory or duplicate IDs remain unresolved for review.
- ComicRack CE has no first-class Continuity membership workflow, so this is a Paperbunkr feature using the project's existing Continuity model.

## Check Metron flow

1. The user runs **Check Metron**. The explicit scan considers the complete library, shows progress in Activity Center, and can be cancelled.
2. Series with existing Continuity membership are skipped as in the former Wikidata scan. For other series, the resolver uses stored, namespaced Metron issue IDs first, then only the existing verified ComicVine/UPC lookup paths. It does not issue a name-only search.
3. The scan produces review rows for every Metron universe found on the first matching issue, preserving the universe's Metron numeric ID and label. No provider identity means no suggestion; missing/unreachable provider data is reported separately from a definitive no-universe result.
4. **Select all**, **Clear**, **Accept selected**, and **Dismiss selected** operate on the current result queue. Per-row actions remain. Accept groups series into the Continuity identified by the Metron universe ID, fills available publisher/designation metadata without overwriting user values, and leaves already-existing memberships unchanged.
5. Dismiss removes the finding from the current review queue and is remembered using a Metron-namespaced dismissal identity; it must not be stored in Wikidata/Fandom fields.

## Needs attention bulk flow

Each outside-series row gets a checkbox. The flyout header exposes Select all, Clear, selected count, and Add selected. Add selected validates that the same continuity is still open, applies all selected memberships in one transaction, and then reloads the overview. Individual Add remains available. A failed operation leaves all selected memberships unapplied. Rows already added or no longer eligible are skipped safely and reported.

## Data and persistence

- Extend the typed suggestion model with an optional `MetronUniverseId` and Metron publisher/designation evidence. Do not fabricate ComicVine, Metron, or GCD IDs.
- Add a dedicated Metron dismissal record/table (or an equivalent clearly namespaced schema) rather than overloading the legacy Wikidata QID column. Use an EF migration and unique `(SeriesId, MetronUniverseId)` key.
- Reuse a Continuity with the same Metron ID before falling back to a case-insensitive name lookup. Never replace a non-empty Metron ID or user-maintained description/publisher/designation with conflicting provider data.
- Bulk acceptance and Needs attention membership updates are transactional. Resolver calls remain idempotent so duplicate memberships are not created.

## UI behavior

- Preserve the per-Continuity **Find series on Metron** panel and hero action as a separate universe-catalog browser. It complements, and does not replace, the library-wide check.
- Add **Check Metron** alongside **Check story events** in Suggestions & checks, with busy state, progress/result summary, and cancellation.
- Add bulk selection controls to the Shared universes review queue. Candidate rows continue to display series, universe, and source evidence.
- Add bulk selection controls to the Needs attention outside-series flyout shown in the provided screenshot.
- Use existing semantic theme resources, Avalonia control patterns, and deferred collection mutation when a row button removes its own visual.

## Verification

Test exact Metron universe identity preservation, no title-only suggestions, series with no resolvable issue IDs, cancellation, incomplete provider failure reporting, Metron dismissal namespace, ID-based Continuity reuse, preserving user metadata, bulk accept/dismiss, Needs attention select/clear/add, duplicate membership handling, and transaction rollback. Run focused Data and App tests and an App build with actual Avalonia XAML compilation. Verify the two bulk controls appear in their distinct intended locations.

## Non-goals

- Keep per-Continuity Metron universe browsing distinct from library-wide suggestions and event-derived Needs attention actions.
- Do not auto-assign Metron matches without user review.
- Do not add title-fuzzy universe matching.
- Do not merge or remove existing Continuity memberships.
- Do not change event-derived Needs attention computation or continuity member ordering.
