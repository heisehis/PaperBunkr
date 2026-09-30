# Continuity map + smart connector — design

*Update 2026-09-28: the continuity page's toggle is now **Overview | Map | Timeline**, and its "Events in this continuity" list became "Events in order" on the Overview ([continuity screen redesign](2026-09-28-continuity-screen-redesign-design.md)). The map itself is unchanged.*

*Status: approved by the user 2026-09-27 after six grilling rounds (Q1–Q28) and a five-section design review; the user asked for it
to be implemented straight away. Second of three sub-projects: the Story Event resolver
(`2026-09-27-story-event-resolver-design.md`) came first; a bundled GCD extract (series bonds, on-sale dates) is the next spec.*

## Goal

Show a whole continuity on one swimlane map — every issue of every story event that touches it, events in chronological order,
loose issues placed between them by date — and work out that order properly: a **smart connector** that infers which event
comes before which (prequel, sequel, continuation) from the library's own data and Wikidata, instead of leaning on dates alone.

## Facts this rests on (verified 2026-09-27)

- **Relations:** `EventRelation` (source, target, `RelationType`, evidence rows with `Provider`, `ProviderRelationType`,
  `ProviderSourceId`, `Confidence`). `RelationTypeCatalog`: "Source is the Prequel of Target"; Prequel ↔ Sequel inverse;
  **Continuation is its own inverse**, so the data model carries no direction for it. `RelationEvidenceProvider` has only
  `User` and `Other` (stored as text). `EventRelationResolver.TryCreate` rejects self and same-type duplicates either way.
- **Existing event suggestions** (`EventRelationSuggestionResolver`) are untyped and undirected: shared name word, dates within
  ~2 years, shared member series.
- **Continuity** is series-level (`ContinuityMembership`, with `SortOrder`); `EventMapLoader.EventsInContinuity` finds events with
  a member issue from a member series.
- **External sources** (research 2026-09-27): Wikidata has "follows" (P155) / "followed by" (P156) and a Comic Vine ID (P5905,
  `4045-` prefix for story arcs); populated on few storylines (~15 of 318) and noisy (e.g. Planet Hulk → "nothing" Q154242),
  CC0. Fandom event infoboxes have no previous/next fields and Fandom's terms forbid automated access. ComicVine and Metron
  expose no arc-to-arc relations. GCD's API has no relations; its dump has series bonds and (new) story arcs with only
  main/sub-arc and translation relations — used by the next spec, not this one.
- `WikidataClient` (internal) already searches and fetches entities and reads P31/P1080/P1434/P123/P136.

## Decisions (from grilling)

| # | Decision |
|---|---|
| Q1 | Every member of every event touching the continuity; lanes for series outside the continuity are flagged, not dropped. |
| Q2/Q13 | Event order: directional relations (yours + inferred + Wikidata), then start date / earliest cover date, then name; loops broken by date. Continuation = "source continues target" (source later). |
| Q3 | An issue in several events appears once, at its first appearance; "Also in" links. |
| Q4 | Relay layout with event bands on the ruler; clicking a band opens that event's map. |
| Q5/Q18 | Filters All / Hide optional / Events only, plus an Events picker; loose issues shown by default. |
| Q6 | Open reader anchors to the card's event; loose issues open unanchored. |
| Q7 | Continuities get Series \| Map \| Timeline. |
| Q8 | Pending duplicate pairs show as a note linking to Possible duplicates. |
| Q9/9a/9b | No events → publication-date layout (lanes in continuity series order, columns by cover date/series order/number, undated last, lane-only lines, year bands). With events, issues outside every event are placed between event blocks (Q17). |
| Q10–Q16 | Smart connector: strong inferences saved as inferred relations, weak ones as typed suggestions; your relations always win; runs library-wide after the identity step; connectors drawn between bands (solid yours, dashed inferred); dismissals remembered. |
| Q17 | Loose issues go chronologically between event blocks in "Between events · years" bands; leftovers/undated in a final band. |
| Q19 | Wikidata P155/P156 is a strong source (matched by ComicVine arc id, else name keys + type + publisher; placeholders dropped). |
| Q20/Q23–Q28 | GCD becomes bundled in-app data in the **next** spec; this one leaves a slot for it. |
| Q21/Q22 | Comic Book Reading Orders timeline and Fandom are not used automatically. |
| Approach | **A** — new connector + loader feeding the existing Event Map engine (not a copied engine, not a stored order). |

## Design

### 1. Data model (one additive migration)

- `RelationEvidenceProvider` gains `Inferred` and `Wikidata` (text-stored enum, no schema change). An inferred relation's reason
  goes in `ProviderRelationType`; a Wikidata relation stores the item id in `ProviderSourceId`.
- **`EventRelationDismissal`** (new): `Id`, `LowerEventId`, `HigherEventId` (ordered pair, both cascade), `CreatedAt`; unique pair.
- **`StoryEvent`** gains `WikidataQid` (`string?`) and `ChronologyCheckedAt` (`DateTime?`); misses use the existing negative cache,
  whose `ArcVerificationSource` gains `Wikidata`.
- Direction conventions (code, not data): Prequel source first; Sequel source later; Continuation source later; Crossover,
  SameUniverse, SharedUniverse, Related, Other unordered.
- Nothing stored for the map: its order is computed live.

### 2. Smart connector (`Paperbunkr.Data/Metadata/`)

**`EventChronologyInference.Infer(context)`** (local) → `InferredEventRelation(Source, Target, Type, Strong, Confidence, Reason)`.
Event dates: `StartDate`/`EndDate` if set, else earliest/latest member cover date.
- Strong — **direct continuation** (0.9): a shared series where B's issues start at the number right after A's last, dates not
  contradicting → *B Continuation of A*. **Name pattern** (0.85): "Prelude to X", "Road to X", "Countdown to X", "X: Prelude"
  → *Prequel of X*; "X: Aftermath", "Aftermath of X", "X: Fallout", "Fallout of X", "X: Aftershock(s)" → *Sequel of X*; X must
  resolve by name keys to exactly one event. **Shared-series order** (0.75): all shared series have A's issues before B's, no
  shared issues, dates agree → *B Sequel of A* (continuation wins when both apply).
- Weak (suggestions only) — date order + a shared significant name word; same base name ≥ 3 years apart with no shared issues
  (skipped while pending duplicate review); shared issues with overlapping dates → Crossover.

**`WikidataEventLinks`** (network, strong 0.9): match an event to one Wikidata item — by P5905 `4045-{ComicVineArcId}`, else by
name/alias keys on items typed storyline or limited series with a publisher match when known; read P155/P156, drop placeholders,
resolve targets to events in the library (stored `WikidataQid` or name keys); "A followed by B" → *A Prequel of B* (Wikidata
evidence, item id). Batches of 40, `ChronologyCheckedAt`, 30-day recheck.

**`EventConnectorSweep`**: your relations win (a pair you related is skipped); dismissed pairs skipped; direction conflicts keep
the higher confidence; strong inferences create or update inferred/Wikidata relations; an inferred relation whose evidence
vanished is deleted (yours never are). Deleting an inferred relation in the Related events panel records a dismissal. The panel's
"Suggested" list shows weak typed inferences first (Accept creates that exact relation), then the old untyped ones.

**`EventChronology.Order(events, relations)`**: topological by directional relations; among ready events earliest date, then
name; a loop is broken by taking the earliest-dated remaining event.

**Runs** after the identity step in the weekly task, renamed **Check story events** (the sidebar button too).

### 3. Rows and layout

**`ContinuityMapLoader.Load(context, continuityId, options)`** → ordered rows split into blocks, lanes, connectors, counts.
- *Events mode:* events touching the continuity (minus hidden) ordered by `EventChronology`; before each event a
  **between-events block** of not-yet-placed loose issues (member-series issues in no event of the continuity) dated before the
  event starts, in publication order (cover year/month, continuity series order, number); then the **event block** (its
  members in event order, skipping issues already placed, recording "Also in"); a final between-events block for leftovers and
  undated. Hide optional drops Optional members; Events only drops between-events blocks.
- *Publication mode* (no events): all member-series issues in publication order in **year blocks**, undated last.
- Lanes: member series in continuity order, then outside series by first appearance, flagged.
- **`EventMapLayout` block-relay mode**: keeps the given order, one column per card, block column spans recorded; relay chain
  lines inside event blocks, same-lane lines inside other blocks, nothing across a boundary. Everything else in the engine is
  reused.
- Per card: owning event (null = loose), also-in events, outside flag.

### 4. Screen

- Continuities get **Series | Map | Timeline**; `EventMapViewModel` gains a continuity scope; the Events-in-this-continuity list stays.
- Toolbar (continuity scope): no spine picker; **All / Hide optional / Events only** (Events only in events mode); **Events…**
  flyout of checkboxes; density; jump to first unread; status "312 issues in 9 events · 1,480 outside events" or "2,104 issues ·
  publication order"; "2 possible duplicate events" note → opens Possible duplicates.
- Ruler band row: event names over their spans, "Between events · 2004–2006", years; clicking an event band opens that event's
  map; alternate event blocks faintly shaded (skin tokens).
- Connectors drawn in the band row between band labels (Crossover as a bracket); solid for yours, dashed for inferred/Wikidata;
  hover tooltip "Sequel · inferred · Hulk #92–105, then #106–111".
- Inspector: Event link (opens its map), Also in links, "Outside this continuity", Open reader anchored to the card's event.
- Empty: "Add series to this continuity to see its map"; filtered empty: "Nothing to show — change the filters".

## Testing

Data (real SQLite, fakes, no network): `EventChronologyInferenceTests`, `WikidataEventLinksTests`, `EventConnectorSweepTests`,
`EventChronologyTests`, `ContinuityMapLoaderTests`, block-relay layout tests, forward-only migration test. App (headless,
synchronous runner): continuity Map toggle and lazy load, filters/picker relayout, band click opens the event map, reader anchor
per card, Also-in links deferred, duplicate note, Related events panel typed Accept and dismissal-on-delete, one sweep summary,
ruler band/connector hit-testing. Manual: a read-only live Wikidata spot-check (Planet Hulk → World War Hulk, Crisis on Infinite
Earths → Infinite Crisis) during planning; the look on screen by the user.

## Risks

- **Wikidata coverage is thin** — the connector leans on local inference; Wikidata adds precision where it exists.
- **Name-pattern false positives** ("Road to Nowhere") — X must resolve to exactly one existing event.
- **Very large continuities** — virtualization copes; band labels and Compact density keep it navigable.
- **Shared tree** — Events screen files, `EventMapViewModel`/layout (uncommitted Event Map work) and the model snapshot.

## Follow-up

GCD bundled extract (next spec): series bonds as series-continuity connectors, on-sale dates for chronology, GCD arcs as a
weak cross-check, CC BY-SA attribution.

## Implementation notes (2026-09-27)

Built per `2026-09-27-continuity-map-plan.md`. Where the build differs from the text above:

- **Wikidata types are inconsistent** (live check: Planet Hulk is a "written work", World War Hulk a "limited series"). The type
  check is an allow-list (storyline Q115378877, limited series Q3297186, comic book series Q14406742, written work Q47461344,
  crossover Q1047299), plus name keys and a publisher check. The publisher's label is fetched and compared after `NormalizePublisher`.
- **No Wikidata negative cache.** `ChronologyCheckedAt` already skips an event for 30 days after a lookup, found or not, so
  `ArcVerificationSource.Wikidata` exists but nothing writes it.
- **Loader and builder are separate.** `ContinuityMapLoader.LoadData` does the queries; the pure `ContinuityMapBuilder.Build` applies
  the filters. A filter or picker change therefore relayouts without a new query.
- **Different explicit years are never a name match.** "Secret Wars (1984)" / "(2015)" are no longer flagged as possible
  duplicates by the Story Event resolver, so the connector's "same name years apart" suggestion can apply to them. This is a change
  to `StoryEventIdentityResolver`.
- **The pending-duplicate skip applies only to the same-name sequel suggestion,** not to every weak signal. A prelude inside its main
  event can still be suggested as a crossover.
- **Connectors live in the ruler's band row** (`EventMapRuler`, 50 px tall on a continuity map), with hover tooltips and a click on an
  event band. Alternate event blocks get a 5 % accent wash in the edge layer.
- **The inspector's Event / Also in entries** reuse the Connections list. They open the event's map instead of selecting a card.
- **Open reader on a loose issue** (in no event) opens the reader unanchored, so it pages through its series.
- **"Check story events"** (`StoryEventChecks`) is one routine for the weekly task and the sidebar button: identity sweep, then
  connector sweep. The task keeps its id `story-event-identity`.
- **Continuity maps with no events,** when the library has no events at all: the Map view uses the screen's existing
  `!HasNoEvents` visibility rule, like the rest of the detail pane. That limitation pre-dates this work.
- **Verified:** Data 123/123 (connector, chronology, Wikidata links, sweep, resolver, migration, grouping/create-or-reuse/Wikidata
  suites). App targeted run: see the todo entry. Not verified: live Wikidata beyond the two items checked, and the look on screen.
