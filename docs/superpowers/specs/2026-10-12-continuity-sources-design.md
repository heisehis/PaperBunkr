# Continuity sources: Metron-first universes, consensus event order, character identity

Status: draft for review, 2026-10-12. Follows the grilling round of the same date (all ten recommendations accepted).
Builds on [continuity-map](2026-09-27-continuity-map-design.md), [story-event-resolver](2026-09-27-story-event-resolver-design.md) and
[gcd-data](2026-09-27-gcd-data-design.md).

## 1. Goal

Stop asking the user to review things the sources can settle. Use Metron as the only universe authority, work event order out from
CBRO, GCD, Metron and ComicVine together, drop the character-overlap suggestions, and give characters and teams a real identity.

Non-goals: per-universe character splitting, anything that *uses* the new identity columns, bundling CBRO data, a review queue of any kind.

## 2. Verified facts

| Fact | Source |
|---|---|
| Metron has `/universe/` (id, name, publisher, **designation** e.g. "Earth-616", gcd_id, desc, modified). Docs site is behind a bot wall, so verified from `api/v1_0/serializers/universe.py` on GitHub. | Metron source |
| Metron characters and teams carry `cv_id`, `gcd_id` and `universes`. | `character.py`, `team.py` |
| Metron arcs are `id, name, desc, image, cv_id, gcd_id, modified`: **no relations, no ordering.** Same for ComicVine arcs. So event order must be derived, not read. | `arc.py` |
| The scrape already receives characters, teams and universes as `ComicVineIdName` (with `ExternalId`) at no extra API cost. | `MetronClient.cs:435-439` |
| CBRO has no export. The existing adapter parses HTML and covers **Marvel and DC events only** (via `/dc/event-timeline/` and `/marvel/event-timeline/`). CBRO is therefore as narrow as Wikidata was, for the same two publishers. | `ComicBookReadingOrdersSource.cs` |
| CBRO `robots.txt`: `crawl-delay: 5`, nothing disallowed. The adapter throttles at 500 ms, **below the stated delay**. No terms-of-use page found. | fetched 2026-10-12 |
| Metron also exposes `/reading_list/` (filters: `name`, `list_type` = CREATOR/EVENT/STORY/CHARACTERS/TEAMS/MASTER, `attribution_source` = CBRO/CMRO/CBH/CBT/MG/HTLC/LOCG/OTHER, `publisher`, `modified_gt`). A list's detail carries **`previous` and `next`** ("the reading list that comes before/after this one in a reading order", `{id, name}`), plus `attribution_url` and an `items_url` (ordered, 50 per page, each with `issue_type` Prologue/Core/Tie-In/Epilogue). Public lists are visible to any authenticated user. | `reading_list.py`, `reading_lists/models.py`, `api/views.py` |
| CBRO's event timeline is **reading/story order, not publication order**: "Second Clone Saga (1994)" sits after "Age of Apocalypse (1995)", "Marvel 2099 (1992)" among 1993 entries. It has era markers ("Marvel NOW! starts here"), many older entries without links, and notes such as "Occurs during the same time as Civil War". Checked live 2026-10-12. | `comicbookreadingorders.com/marvel/event-timeline/` |
| Issues store only a cover year/month (`Year`, `Month`) and a GCD id: there is **no Metron store date** in the library, so date authority is GCD on-sale date, then cover date. | `Issue.cs` |

## 3. Slice 1: demote Wikidata, scrap shared characters (mostly deletion)

- Remove the `continuity-wikidata-autodetect` scheduled task and the Wikidata step of `EventConnectorSweep`. Keep `WikidataId`/`FandomKey`
  columns, `WikidataClient` and every accepted value (migrations are append-only; nothing reads them destructively).
- On the first run of the new sweep, delete relations whose evidence is **only** `Wikidata`. They are derived, and a pair is re-created
  if a remaining source supports it. User relations and `EventRelationDismissal` rows are never touched.
- Delete `ContinuityCharacterSuggestionResolver.GetSuggestions` and its two entries in `SuggestionsChecksViewModel`, plus their tests.
  Keep `OtherContinuityLines` (membership-based, not character-based). The `ContinuitySuggestion` dismissal rows stay in the table.
- Continuity membership stays exactly as `ContinuityMetronMatchResolver` does it today (tag a series from Metron's `universes` on scrape).
- Add `Continuity.Designation` (nullable string) filled from Metron's universe `designation`; shown next to the name. A cached copy of
  Metron's universe catalogue feeds the "new continuity" picker so every universe with its publisher is browsable. It never creates
  empty continuities by itself.
- When an issue scrape supplies a Metron universe ID, refresh the continuity's **Name**, **Designation**, **Publisher**, and **Description** from the exact `/universe/{id}` record (`name`, `designation`, `publisher`, `desc`). Run this once per distinct universe in a scrape batch; retain the universe tag's name and membership if the detail lookup fails, and let `ContinuityUniverseSweep` retry incomplete metadata. These four fields are provider-refreshed values and are replaced by Metron on a successful refresh.

## 4. Slice 2: event order by source consensus

Two separate outputs, so we stop conflating them:

1. **Placement**: where an event sits on a continuity's timeline. Every event with dates gets a place, no relation needed.
2. **Links**: "this directly leads into that" (Prequel/Sequel/Continuation relations, as today).

### 4.1 Evidence

| Evidence | Gives | Authority |
|---|---|---|
| CBRO event-timeline position (Marvel/DC) | **placement rank** only | highest for placement: a human's story order |
| Metron EVENT reading-list `previous`/`next` chain (any publisher) | **link** (Prequel) | explicit, human-set; evidence provider `Metron` |
| GCD on-sale dates (already wired, `DateKeysFor`) | placement date | objective date |
| Cover year/month (what ComicVine and Metron scrapes store) | placement date | fallback |
| Member issues of an event | Metron arc issues ∪ ComicVine arc issues | union (existing identity resolver) |
| Existing local rules (shared-series continuation, "Prelude to X" name patterns) | links | kept as last-resort tiebreaker |

CBRO adjacency is **not** a link: neighbours in a reading order are not necessarily sequels (Kree-Skrull War sits next to "The Night Gwen
Stacy Died"). It only ranks events. CBRO's "occurs during the same time as X" notes are not parsed in this spec.

### 4.2 Decision rules

- **Placement key:** an event's own date span, except that events ranked by CBRO are held to the curated order: walking a CBRO list in
  order, each ranked event's *sort key* is `max(its own start, previous ranked event's sort key)`. Real dates are kept for display; only the
  sort key moves. This keeps the key a plain number (no non-transitive comparisons), places "Second Clone Saga (1994)" after "Age of
  Apocalypse (1995)" as CBRO does, and still interleaves unranked events by date. Ties break by rank, then name.
- **Links:** only from the Metron list chain, the existing strong local rules, and manual relations. **Date order alone never creates a
  link.** (The earlier draft's "link from dates" rule is dropped.)
- **Conflict:** higher authority wins silently. An event with no dates and no rank is **Unplaced**: a quiet lane, no badge, no notification.
- **Dropped:** the weak inferences in `EventChronologyInference` (shared word + date order, same base name years apart, overlapping-date
  Crossover). `FromNamePattern` and `FromSharedSeries` stay.
- **Metron list matching:** search `list_type=EVENT&name=<event name>`, accept only when exactly one result's name key equals the event's;
  then read `previous`/`next` names and match each to a library event the same way. At most 20 lookups per run.

### 4.3 Placing an event in a continuity

Derived, not stored: event E is in continuity C when at least half of its member issues' series are members of C. An event
meeting that bar in several continuities appears in each as a crossover and is not forced into one.

### 4.4 CBRO acquisition

- A scheduled task fetches the two timeline pages only (member issues come from Metron/ComicVine), and stores for each matched event
  its rank in the list. The result lives in the `EventOrderEvidence` table with a fetched-at; refreshed at most weekly.
- Throttle raised to **5 s** to honour `crawl-delay` (this also corrects the existing reading-list import, which breaks it today).
- An event is linked to a CBRO page only when its name key matches exactly one page; otherwise skipped without a prompt.
- Not bundled with the app and not redistributed (no terms found, so we only fetch what the user's own library needs).

### 4.5 Schema

- `RelationEvidenceProvider` (append-only enum): add `Metron`. (`Gcd` already exists.)
- New table `EventOrderEvidence` (event, provider `Cbro`/`MetronList`, source id/url, CBRO `Rank`, Metron previous/next list names,
  `CheckedAt`), unique on (event, provider). No change to `StoryEvent`.

## 5. Slice 3: character and team identity (corrected while building)

**The identity field already exists.** I proposed new `MetronId`/`ComicVineId` columns on `Character` and `Team` without first checking the code. Since
the Metron work of 2026-09-23 every scrape stores each character's and team's provider id in `ComicMetadataExternalId` (kind, local row, provider,
external id; unique per kind + provider + id), from the `ExternalId` the scrape already receives. New columns would have duplicated it, so none were added.

What was actually wrong, and is fixed:

- **A pruned character kept its id.** When a character lost its last appearance the row was deleted but its external-id row stayed, owned by a number
  that no longer exists. Because an id can belong to only one local row, the same character re-created later could never get its id back (the attach
  step silently declined). `CharacterResolver`/`TeamResolver` now delete a pruned row's external ids, and the attach step takes over an id whose owner
  row no longer exists (this also heals rows already left behind by older versions). A live owner still keeps its id.
- Not done, by decision: GCD ids (`ComicProvider` has no GCD member and nothing would read it), Metron's per-character `universes`, merging two name
  rows that share an id. Nothing reads the ids yet.

## 8. As built (2026-10-12)

- **Slice 1:** the `continuity-wikidata-autodetect` task is removed (a stored schedule row for it is ignored); `EventConnectorSweep` no longer calls
  Wikidata and removes Wikidata-only relations; `WikidataEventLinks` is deleted (nothing referenced it; `WikidataClient`, the manual "Check Wikidata"
  buttons and the accepted `WikidataId`/`FandomKey` values stay). `ContinuityCharacterSuggestionResolver`, its Suggestions entries and row view-model
  are deleted; `OtherContinuityLines` moved to `ContinuityMembershipLines`. `Continuity.Designation` (+ the hero chip) is filled by
  `ContinuityUniverseSweep` (one `/universe/{id}/` request per Metron-tagged continuity, 20 a run, inside "Check story events"); an empty
  designation means "asked, none". **Not built:** the universe-catalogue picker in the new-continuity dialog (deferred above).
- **Slice 2:** `EventOrderEvidence` + `EventOrderProvider`, `EventOrderSweep` (CBRO ranks; Metron lists, 20 a run, 30-day recheck),
  `ComicBookReadingOrdersSource.ParseTimeline`/`GetEntriesAsync` (5 s throttle), `MetronClient.FindEventListAsync`/`GetUniverseAsync`,
  `EventChronology.ApplyReadingOrder` (median pool-adjacent-violators, §4.2) feeding `EventSpan.SortKey`/`Rank`, `EventConnectorSweep` reading the
  Metron chain (provider `Metron`, 0.85), the weak inferences removed, `EventsInContinuity` using the half rule (§4.3), "Check story events" now
  = identity → order sweep → connector → universes. The Event page's typed "Looks like the sequel of…" suggestions and the overview's "connections worth
  confirming" were the weak guesses' only consumers and are gone; the untyped "these look connected" suggestions (shared word, close dates, shared
  series) remain and connect as *Related*.
- **Unplaced lane:** not built as a lane. An event with no dates and no rank already sorts last on the map and nothing asks about it; that is the
  "quiet" behaviour the design wanted. A dedicated lane remains an option.
- **CBRO checked live:** both timeline pages parse (Marvel 165 entries, DC 127, linked and plain, era markers skipped).
- **Never run against the real Metron** (the endpoints came from its source), so every Metron response field is read defensively.
- Migration `AddEventOrderEvidence` (table + `Continuities.Designation`), tested up, down and up.

## 6. Risks and open points

- **CBRO is Marvel/DC only.** For Image and everyone else, order comes from dates and the local rules. This is the same coverage shape
  that made Wikidata thin, so expect many non-Marvel/DC events to sit in Unplaced until Metron/GCD dates are present.
- **CBRO order is story order, not strict dates** (settled by checking the live page and by the user): handled by the sort-key rule in §4.2.
- **Metron reading lists are included** (user decision 2026-10-12). Their `previous`/`next` chain is the only explicit event-to-event order
  any source gives us, and it covers publishers CBRO does not. Coverage depends on what the Metron community has curated; I have not
  measured it. A Metron list whose attribution is CBRO is a copy of CBRO's order and gets no extra weight (it is a link source, CBRO is a rank source).
- **Not verified against the real Metron:** the reading-list and universe endpoints were read from Metron's source, not called (the
  project has never run its Metron code against the live service). The code must tolerate missing fields and treat any failure as "no evidence".
- **Universe picker UI** (browse the cached catalogue when creating a continuity) is deferred; this spec delivers the catalogue data, `Designation` and the label.

## 7. Testing

- Slice 1: sweep no longer calls Wikidata; Wikidata-only relations deleted, mixed/user ones kept; no `GetSuggestions` references.
- Slice 2: pure-function tests for each rule with fixtures (CBRO HTML already has a fixture pattern); conflict cases; Unplaced result;
  the 5 s throttle (fake clock); the half-of-issues continuity rule.
- Slice 3: id adoption, id-beats-name, filtered unique index migration up/down/up (see the migration antipattern note).
- UI: only the Unplaced lane and the Designation label change; check them on screen via the Continuity screen.
