# Event Smart fill

**Date:** 2026-11-04
**Status:** Approved; implemented and focused checks passed in the working tree. Not released; schema-2 package prepared locally, not published. See the implementation plan for verification and limits.
**Repository baseline:** `0e43ae0`; unrelated working-tree changes are outside this design.

## Purpose and approved scope

Fill missing Story Event metadata using Metron, ComicVine and optional local GCD data. Support the six fields in Edit Event: name, event types, start/end dates, event tags, cover and description. Fetching evidence does not guarantee a value for every field.

The user approved automatic filling of eligible blanks, rather than a mandatory preview. Preserve every existing value, including values previously filled by this feature. Preserve deliberately cleared fields until the user explicitly enables filling them again. Offer a separate Compare sources action for conflicts, weaker suggestions and replacements. Keep a saved change history with conditional Undo, including batches.

Entry points are explicit actions on one event and on selected events. No scheduled library-wide enrichment, external AI service, membership writes, reading-order changes, new event relationships, Continuity metadata enrichment or issue metadata scraping is included.

In the editor, filling changes the draft; Save commits and Cancel discards it. From an event page or batch action, eligible changes save immediately, with progress, a result summary and Undo. New-event drafts may use an identity already explicitly selected in guided creation; Smart fill does not create an event before the existing Save/Create flow does.

## Evidence and existing behavior

The supporting investigation is [the provider audit](../../audits/2026-11-04-metron-event-metadata.md). It distinguishes published source, indexed website content and actual local dump queries.

- Metron arcs expose name, description and optional artwork, but no matching arc-level type, tag or start/end fields. Arc issue membership is separately paginated and publication-ordered, not a guaranteed reading order.
- ComicVine provides an arc name, short summary (`deck`), HTML description and images; known issue details provide publication dates. No matching structured event-type/tag fields were established. Its exact Timestorm page's description contains the edition lists visible in the user's screenshot.
- GCD's full database has arc names, descriptions, first-publication year with uncertainty, and story-to-arc membership. Its published API does not expose those arc records. Story keywords and genres are not event tags; issue cover URLs are not dedicated arc artwork.
- The user's `Downloads/current.zip` contains the 2026-09-15 SQLite snapshot. Read-only queries found 582 active arcs, 142 with nonblank descriptions, and 478 with membership links. There is no Timestorm arc. Its original limited series, two one-shots and a distinct collected edition exist. These counts describe that snapshot, not current GCD production.
- `StoryEventDetailsCompletion` currently fills descriptions/covers and explicit refresh can replace a non-manual provider cover. `NewEventOrContinuityViewModel` also writes provider details during setup. Neither has the proposed deliberate-empty state.
- `EventChronology` already projects event spans from explicit dates, otherwise member publication dates with GCD support. That projection is not a persisted Smart fill value.
- Event-owned tags are curated separately from member-derived genres/tags. Event types are multi-select, separate from tags and membership roles.
- `StoryEventMerger` currently selects date extrema and a longer description; it does not understand the new metadata state. Its integration needs explicit preservation rules.

ComicRack CE was checked in `_reference/ComicRackCE`: `ComicInfo.StoryArc` is issue-level text; `MetronInfoProvider` maps embedded arc/story data to legacy issue fields. CE has no matching event editor/entity. This feature extends Paperbunkr's existing deliberate deviation; it must not change CE-compatible issue fields or serialization.

Relevant earlier designs are [event details](2026-10-15-continuity-event-details-design.md), [guided creation and event types](2026-10-16-guided-event-continuity-creation-design.md), [event identity](2026-09-27-story-event-resolver-design.md), and [GCD data](2026-09-27-gcd-data-design.md). This spec supersedes their blank-only enrichment application policy only for the explicit Smart fill/Compare sources paths and the shared protection checks described below. Guided membership review remains unchanged.

## Alternatives and selected approach

1. Extend the existing provider-details refresh with more fields. Smallest surface change, but mixes fetching, deciding and saving, and makes draft editing, comparison and undo inconsistent.
2. **Selected: one shared evidence/decision/application workflow.** Reuse provider clients and identity machinery; let editor, page and batch callers use the same decisions with different save boundaries. Comparison and history reuse those decisions.
3. Build a general metadata workspace. More UI and abstraction than this event-only feature needs; deferred.

## Identity and read-only evidence collection

Use namespaced verified arc IDs first. Reuse existing identity resolvers and verified cross-provider bridges; equal numeric IDs across providers never establish identity. GCD gets its own arc identity, distinct from series/issue GCD IDs.

A previously unlinked event can acquire a verified identity only through existing verified-ID/member-evidence rules or the user's explicit candidate selection. A similar title, a publisher match, or one incidental member alone is insufficient. Multiple credible candidates, incompatible IDs or stored identity conflicts block automatic use of the disputed association. Show candidates and reasons in Compare sources. Compatible undisputed evidence may still contribute to other fields.

Resolve GCD membership through indexed stories linked to an arc and then their issues. Do not equate an individual story title with an event name, import reprints as originals, or union all similarly named series. Use explicitly identified original publications, edition/format evidence and reprint relationships. If those facts do not establish an original set, report limited coverage rather than asserting completeness.

Provider membership can be fetched without owning every issue. It is evidence only: never add, remove, reorder, import or create local member issues. Record whether the source membership list completed, its snapshot/fingerprint and which dates are missing. A local subset cannot establish the complete event span.

An evidence candidate carries field, value, provider/object identity, source link, snapshot time, direct/derived status, rule/reason, and any uncertainty or coverage limits. Avoid unexplained percentage confidence. Collection is read-only and does not persist a selected candidate or overwrite fields.

## Eligibility and field protection

| Field shape | Automatically eligible |
| --- | --- |
| Text | Null, empty or whitespace, and not deliberately protected. |
| Cover | No effective manual or provider cover, and not protected. A temporarily failed image download is not a blank cover. |
| Start/end endpoint | No effective stored endpoint, including partial dates, and not protected. Evaluate each endpoint independently. |
| Types or event-owned tags | Entire collection empty and not protected. A nonempty collection is preserved as a whole. |

Persist protection per logical field, including each date endpoint and each collection. Clearing a previously populated field in the editor marks it intentionally empty, whether its prior value was manual or automatic. A never-populated blank remains eligible. For collections, removing their last value counts as clearing. Discarding an edit discards the protection changes too.

Compare sources offers an explicit Fill again action for a protected blank. This changes that field's protection only after the user applies/saves. Adding a manual value makes it user-owned; its later clear is protected again. Rerunning Smart fill is not consent to reset protection.

Existing blank fields are eligible after migration; do not invent historical clearing intent. Existing nonblank values are preserved. Presets and existing type selections count as populated values.

## Candidate selection and bounded inference

Use normalized text/value equivalence to deduplicate candidates, preserving all supporting source links. Selection follows evidence quality for that field. Distinct equally credible alternatives stay unresolved rather than being selected by network completion order. Provider errors are separate from no-data results.

### Name and description

Fill a missing name from a verified arc identity; ordinary named events are unchanged. Retain existing required-name validation.

Keep ComicVine deck and description as separate candidates. Normalize HTML into safe readable text while preserving paragraph/list boundaries. Strip markup once in the source adapter. Ignore empty/markup-only text. Recognize descriptions made solely of collected-edition, non-US-edition or link-list sections as bibliographic material, not a synopsis; retain them for comparison rather than auto-selecting them.

Prefer a useful direct event synopsis/overview over bibliographic-only material. Do not rank a description by character count. Full descriptions that include both genuine overview text and edition sections remain usable after separating the bibliographic sections. A brief deck can beat an edition-only description.

For automatic selection, useful text means nonempty provider overview/deck prose outside identified bibliographic headings/link lists, excluding known placeholder text. This is a structural eligibility rule, not a claim that the synopsis is factually superior. Non-English or unrecognized section structures are left as comparison alternatives when the filter cannot confidently separate prose from bibliography; preserve the original candidate for inspection. Two eligible distinct overviews at the same quality remain a conflict.

Do not generate a new summary, concatenate issue synopses, or promote a single issue synopsis to event description. Different useful overviews without a defensible quality distinction are alternatives for Compare sources. No LLM or semantic truth claim is implied by text cleanup.

### Cover

Use compatible dedicated arc artwork before any issue-cover fallback. For equivalent dedicated-artwork candidates, retain the established Metron-then-ComicVine preference; flag identity contradictions rather than falling back across them. An explicitly selected source remains authoritative.

An issue-cover fallback must belong to a verified original issue and be identified as an issue cover, not arc artwork. Auto-select it only when the original first issue is unambiguous and no dedicated artwork candidate exists. Otherwise show the alternatives. GCD issue covers are candidates accessed through an available verified URL; do not scrape pages or bulk-package cover scans into the data download.

Reuse the event-cover cache. Fetch/decode a candidate before committing a new cover selection; failure leaves that field unchanged and retryable. Manual cover assets stay protected and Undo must not depend on deleting a shared cache file.

### Event types

Treat these as whole-event classifications. Initial bounded rules are:

- A verified provider arc record supports Story Arc when the type collection is eligible.
- Limited Series requires the event's complete verified original membership to correspond to one explicitly classified limited series, with the event identity matching that series. A limited main series plus unrelated/tie-in membership does not, by itself, classify the whole event.
- One-shot / Special requires the entire verified event to match a single explicitly classified one-shot/special publication. One one-shot member is insufficient.
- Crossover requires explicit event-level classification evidence. Merely spanning multiple series, featuring multiple characters, or mentioning another event is insufficient; such cases can be explained suggestions in Compare sources.
- Other is never an automatic fallback for unknown classification.

These rules may yield multiple compatible types but never append to an existing type collection automatically. Preserve the user's ability to select any types manually. If a source supplies no trustworthy event-level classification, keep the unsupported types unset.

### Event tags

Preserve the existing separation between event-owned tags and projected member genres/tags. Do not auto-copy character lists, genres, publisher names or arbitrary issue keywords into event-owned tags. Do not infer weights.

None of the three examined arc schemas supplies an authoritative event-tag list. Therefore this first implementation must not claim guaranteed automatic event tags. It can show provenance-labelled, unselected suggestions in Compare sources when evidence describes the whole event; accepting one is an explicit curated choice. In the absence of such evidence the collection stays blank. GCD's isolated Timestorm keyword `Smithsonian` is a negative regression example.

## Publication dates and completeness

Dates describe original publication, not fictional chronology. Store year/month/day precision, source basis, uncertainty, derivation and coverage separately. `2009-08-00` means August 2009, not an exact calendar date. Do not persist a fabricated first/last day as an asserted event date.

For each verified original issue prefer a reliable on-sale date; otherwise use a publication/key/cover date with its actual precision and basis. An uncertain on-sale date remains uncertain. GCD's arc first-publication year can support a year-only starting candidate when issue evidence does not establish a stronger endpoint; it cannot invent an ending or overrule contradictory stronger evidence.

Compute endpoints as intervals over known date components. The earliest/latest possible relevant issue determines the defensible endpoint precision. A known 26 August date plus another issue known only as August does not prove an exact 26 August endpoint. Do not rank these values by manufactured calendar days. If the available intervals cannot establish order, report the coarser range and uncertainty.

An incomplete membership fetch, missing dates, an ongoing publication or unestablished completion makes the range provisional. Display its end as through [date] and show coverage. Complete membership with usable dates is not by itself proof that an ongoing event has ended. Confirmed ending needs affirmative completion evidence; otherwise remain provisional or let the user explicitly confirm an endpoint in the editor.

Never shrink or extend an already stored endpoint during automatic filling. A new candidate belongs in Compare sources. When only one endpoint is eligible, evaluate it against the protected existing endpoint; do not save an invalid reversed span. Start/end ordering validation operates on precision intervals, allowing an overlapping month/day range without pretending its exact order is known.

### Storage compatibility

Use a small event-endpoint record for each start/end value, containing components, precision, uncertainty, basis and provisional/coverage metadata. This record is authoritative when present. Legacy `StoryEvent.StartDate`/`EndDate` remain compatible exact-date fields: synchronize them for true day-precision values and leave them null for partial values. All event consumers use one effective-date accessor, so null legacy fields do not cause an existing partial endpoint to be refilled.

Existing exact legacy dates remain readable through the accessor as legacy/manual values with their stored precision; do not claim provider verification. Migrations do not infer spans for existing events. Update editor parsing/display, summaries, chronology, maps and merge consumers together. Preserve the existing read-time member-date fallback when there is no explicit stored endpoint, respecting intentional-empty display semantics: protection prevents persistence, not existing read-only chronology projections.

The consumer audit includes `EventChronologyInference`, `EventSuggestionResolver`, `EventRelationSuggestionResolver`, `EventOrderSweep`, `SeriesConnectorSnapshot`, the series connector detail rows and `ContinuitySidebarViewModel`, not only `EventChronology`. Their date comparisons must use effective values/precision rather than interpreting a partial endpoint's null legacy field as absent.

The editor accepts `YYYY`, `YYYY-MM` and `YYYY-MM-DD`, validates real components and exposes uncertainty/provisional status without requiring fake day values. Exact-date entry remains compatible with the current editor.

## Application, concurrency and batches

Separate three responsibilities: adapters collect provider evidence; a deterministic event evaluator produces decisions; a shared apply service validates current state and saves changes/history. Existing view models orchestrate these operations rather than duplicating provider decisions. Use CommunityToolkit.Mvvm and the existing runner/dispatcher conventions.

Capture a field/protection revision and identity/member snapshot when preparing decisions. Before a write, re-read the event and check that revision and relevant source identity/member evidence. A field changed while fetching is skipped and reported as changed since the operation began. Treat date endpoints, precision and flags as one field value; collections and their protection state are atomic units. Unrelated fields may still be applied when safe.

Each event's selected writes, provenance and history are one transaction. Network calls and image downloads happen outside that transaction. A transaction failure rolls back that event only. Process a batch using the existing shared provider clients and rate limits, not a second client pool. Cache identical source reads within the operation; follow pagination and report incomplete results.

Cancellation stops outstanding work and new events. Already committed events remain committed and undoable. Cancellation before an event commits produces no partial writes for that event. A slow, offline, unconfigured or quota-limited source does not block useful evidence from another source indefinitely. Use existing bounded timeouts, cancellation and throttle handling. Report filled, unchanged/protected, needs comparison, unavailable and failed outcomes separately; no-new-data is not a network success claim.

Editor draft requests obey the same stale checks against current draft values. Closing/cancelling the editor cancels its work and prevents a late callback applying to a different event. Saving persists only accepted current draft state and corresponding history; suggestions discarded before Save create no saved change entries.

## Compare sources and history UI

Expose Smart fill and Compare sources from event editing/detail actions, plus Smart fill for selected events. Reuse the existing event action locations. A batch result provides access to conflicts/history without opening a mandatory preflight review.

Compare sources shows the current value, protection state, labelled alternatives, original links, evidence reason and coverage/uncertainty. Replacements and weaker suggestions are unselected until explicitly chosen. An Apply/Save action commits only the chosen fields after stale validation. A selected identity does not implicitly accept all its metadata or memberships.

Present long text and cover candidates in a scrollable shared DialogCard; stack current/candidate values at narrow widths rather than requiring an oversized side-by-side form. Use the current event editor for manual values. An event page/history entry can open Compare sources for its event; batch results identify which events need attention. Show progress with cancellation and a concise completion summary.

Use the Paperbunkr design-language skill's controls, typography and dynamic theme tokens, sized for 1366x768. Source and status information must have text labels, not colour alone. Return focus to the invoking action and support keyboard/controller navigation through `IInputService`; no screen hotkey handlers or new hardcoded key bindings. Respect reduced motion. Defer collection replacement, row removal and overlay close until the routed triggering event has completed.

History persists operation ID, event ID, field, before/after normalized values and protection state, evidence, time and a field revision. Compare-source replacements and saved editor enrichments use the same history. Keep value snapshots sufficient to undo without repeating a provider request. Do not store credentials, full provider response bodies or unrelated library metadata. History is local and retained with the event; no new external telemetry or automatic expiry is introduced.

Undo applies per field only when its current revision still matches the recorded write. Equal text after a later edit is not sufficient evidence that no edit occurred. Restore date/type/tag/cover metadata consistently with the value. Report skipped fields without overwriting them; repeated Undo is idempotent. A user Undo returning an auto-filled field to blank marks it deliberately empty so the next fill does not immediately reapply it. Undo does not remove event identities, members or newly acquired issues.

Deletion cascades event-owned current state/history; a missing event produces a skipped result. For event merges, preserve survivor values and deliberate-empty protection; carry new source evidence as comparison material rather than selecting a longer description or broader date span over protected metadata. Keep histories traceable to their original event/operation, and mark pre-merge undo entries ineligible after the identity boundary changes rather than replaying them onto a different event. Do not broaden this work into a general merge redesign.

## Optional GCD extract

Extend the compact extract in its existing publication tool. Include arc identity/name/language/disambiguation/description/year with uncertainty, story-to-arc-to-issue links, required original-versus-reprint/edition evidence, series publication format and issue date uncertainty. Include only the selected fields needed by these rules; do not bundle the full story/credit database or cover image binaries. Existing series/bonds/date capabilities remain available.

Bump the schema version and make its capabilities explicit. The new reader accepts the existing schema-1 extract for existing functions and limited issue-date evidence; arc enrichment is unavailable there, not a corruption error. The updated artifact must use a versioned manifest/release path so existing binaries continue receiving a supported schema-1 artifact rather than breaking their GCD installation. Download/update validation must keep the prior usable extract until the new file is verified and installed atomically.

Update availability must compare schema/capabilities as well as dump date. The existing installer compares dates only, so a richer extract built from the same 2026-09-15 snapshot must still be offered as an upgrade. Reader and installer validation must agree on supported versions; simply increasing `GcdExtractor.SchemaVersion` while keeping exact equality checks is insufficient.

Missing GCD data does not block Smart fill. The Connections row reports snapshot date and missing arc capability with an optional update action. Use namespaced IDs and read-only indexed queries. Resolve generic keyword content types by model identity, never snapshot-specific numeric content-type IDs. No live GCD API or website scraping is needed for arc enrichment.

Preserve the existing GCD data licence/attribution and source links in the extract and app; the published extract carries the appropriate notice. Cover URLs retain their source attribution and are not presented as artwork owned by GCD. The user's dump is research/build input, not a file copied into the app repository or committed to Git.

## Integration and migration requirements

- Add field state/revisions, endpoint records, current provenance, GCD arc identity and local operation/change records using the repository's EF migration conventions. No backfill performs network calls or asserts missing classifications.
- Route explicit provider-details refresh through Smart fill semantics. It must stop replacing populated provider covers. Compare sources becomes the explicit replacement path.
- Existing best-effort automatic description/cover completion and guided setup must use the same eligibility/protection checks. They retain their existing triggering scope; they do not become background fills of all six fields. Guided setup continues to require its existing review for membership changes.
- Manual editor changes advance revisions and persist protection, ensuring undo cannot clobber later edits. Existing provenance-free nonblank fields remain protected by value.
- Audit direct writes and event merge paths so no legacy writer treats a partial date or deliberately cleared field as an eligible blank.
- Add enum/source support needed to represent GCD and issue-cover provenance without reinterpreting existing stored enum values. Do not introduce GCD as a general issue scraper provider.
- Keep the current shared Metron/ComicVine authentication, throttling and caches. Do not change remapping, plugin input or keymap storage.

## Verification and acceptance

Use focused deterministic Data/App tests and tiny GCD fixtures; the full dump is an optional read-only verification input, not a test dependency or committed fixture. Record source identity and snapshot for factual checks.

Acceptance cases:

1. A single action fills supported blanks with no mandatory preview, preserves existing fields and shows sources/reasons.
2. Editor fill is draft-only until Save, with Cancel and late-response protection; page/batch fill commits per event.
3. A deliberate clear survives every completion/setup/refresh path and a restart; explicit Fill again re-enables just that field.
4. Nonempty types/tags remain unchanged; one-shot members and isolated issue keywords do not classify/tag the whole event.
5. The Timestorm edition-list description loses to a useful synopsis when the local description is blank; an existing edition-list description survives until explicitly replaced.
6. Equal credible candidates and ambiguous identities stay unresolved. Pagination failures, absent credentials and HTTP failures are distinguished from confirmed no-data outcomes.
7. GCD Timestorm evidence excludes series `233267` (collected edition) from original publication calculation. X-Men's `2009-08-00` stays month-precision, even though another issue has exact date `2009-08-26`.
8. Partial/uncertain/provisional endpoints round-trip through editor, database, event summaries and chronology without fabricated days. Conflicting protected endpoints are not silently reversed or cleared.
9. Field revisions and identity/member snapshots block stale writes; atomic event saves prevent partial history/value state.
10. Batch cancellation preserves committed events; Undo is idempotent, survives restart, and skips later edits or merged/deleted identities safely.
11. Existing GCD schema 1 remains usable; schema 2 supports arc evidence; failed updates preserve the old extract and old app manifests remain schema compatible.
12. Existing member-derived tags, memberships, reading order, relations and CE-compatible issue metadata remain unchanged.

UI validation includes the shared XAML/theme lint checks, focused headless editor/comparison/batch/history tests and keyboard checks hosted in the real MainWindow. Render Default, Daylight and Matrix at supported narrow/window sizes, then verify in the running app. Follow `avalonia-pro-max/review-checklist` before calling UI implementation complete.

If new views are added, add their code-behind in the same change. After any failed XAML weave force a genuine compile/weave, then launch or verify target execution; a plain successful retry is insufficient. Never run concurrent builds/tests against the shared project output. Run touched classes first and the documented fast set as appropriate; the slow suite remains release validation.

## Review boundary

This document specifies approved feature behavior and concrete implementation constraints; it is not an implementation plan or a claim of shipped functionality. Self-review must check unsupported inference, stale/clear protection bypasses, date precision and extract compatibility. The user reviews this written spec before the writing-plans hand-off. No application code or production database changes are authorized by the existence of this document alone.
