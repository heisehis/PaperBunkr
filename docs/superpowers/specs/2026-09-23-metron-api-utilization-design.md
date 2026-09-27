# Metron API utilization — design

**Status:** ~~design, not yet planned/implemented~~ **all 5 phases built, uncommitted** (see the
matching `-plan.md`'s own status line, confirmed 2026-09-26). On-screen check by the user still
outstanding.
**Motivation:** after browsing metron.cloud directly, it became clear Paperbunkr's Metron
integration barely scratches the API's surface. Research (see "Facts" below) confirmed this
isn't a Metron-specific gap — `ComicVineIssueDetails`, the single neutral record both
`ComicVineClient` and `MetronClient` fill, flattens characters/teams/locations/arcs to bare name
strings and drops every id both providers actually return. Neither provider populates Genre, Age
Rating, or Publisher detail either. This design treats both providers' underuse as one problem.

## Facts this design is grounded on

- `docs/superpowers/specs/2026-09-20-metron-as-comicvine-alternative-design.md` — the doc that
  introduced Metron. It deliberately mapped Metron onto ComicVine's existing flat-string shape
  ("every consumer works with either provider") and explicitly ruled out automatic cross-provider
  merging and using `cv_id` to cross-link — this design revisits both of those calls.
- Metron's real field set was verified against `Metron-Project/mokkari` (the project's own
  actively maintained Python client) on GitHub, since no cached API reference exists in this repo.
  Confirmed fields beyond what the 2026-09-20 doc documented: `variants` (real alternate-cover
  list, distinct from `reprints`), `imprint`, `price`/`price_currency`/`sku`/`isbn`/`upc`, `rating`
  (age rating), `average_rating`/`rating_count` (community ratings), `foc_date`, `status`,
  `associated` (related series), and richer Creator/Character/Team/Publisher/Arc records
  (`birth`/`death`/`image`/`desc`/`alias`/`founded`/`country`). Genre lives on **Series**, not
  Issue. Metron has **no** Location resource at all (no `location.py` in mokkari's schema
  directory) — Location enrichment can only ever come from ComicVine's `location_credits`.
- Today's storage: only `Character` exists as a first-class row (auto-materialized from
  `Issue.Characters` free text by `CharacterResolver`); Team/Location/Creator/Publisher/StoryArc
  are all flat strings on `Issue`/`Series`. `ComicProvider {ComicVine, Metron}` already exists as
  the enum distinguishing the two providers elsewhere (`WatchedSeries`, `CatalogIssue`,
  `Issue.MetadataSource`). `Continuity` already exists and is explicitly documented as "a named
  fictional universe/timeline ('Earth-616', 'DC Prime Earth')" with an existing Wikidata
  auto-populate path (`ContinuityWikidataMatchResolver`) — this is the same concept as Metron's
  `universes` field, confirmed via code search, not assumed.
- `Series.Status`, `Issue.AgeRating`, and `Series.Genre` already exist as columns but are
  unpopulated by either scraper today (ScrapeField/ComicVineIssueDetails never carried them).

## Scope

Both ComicVine and Metron, since they share one neutral record. Out of scope, decided during
design review:

- **Reprints** (`reprints` field — issue republished elsewhere) — overlaps confusingly with the
  app's existing CBL reading-list Arc / Story Events "collection" concepts; deserves its own
  future scoping pass rather than a rushed decision here.
- **A per-issue StoryArc detail screen** — would be a third, conflicting meaning of "arc" in an
  app that already has CBL Manager arcs and Story Events.
- **Imprint as a first-class entity** — nothing else in scope browses/filters by imprint; a plain
  display string is enough.
- **Price/SKU** — collector trivia with no functional use anywhere in this app today.

## Phasing

Layered, not vertical-slice-per-entity — matches this project's own established pattern for large
multi-part efforts (Metadata Model Phases 1–7, UI Rework's 7 phases). A vertical slice per entity
kind (do Character fully, then Team, then...) was considered and rejected: it repeats
schema/scrape/UI overhead five times and delays every other entity's availability for no benefit.

1. **Foundation** — new entities, migrations, external-id table, resolver extension.
2. **Provider scraping** — both clients populate the richer records.
3. **Cross-provider merge** — the `cv_id`-driven adopt flow.
4. **UI** — the five new detail screens.
5. **Independent wins** — the smaller items that don't depend on 1–4 (variant covers, ISBN/UPC,
   `foc_date`, community ratings, associated series, Continuity/Metron wiring).

Phase 5 items can ship independently of 1–4 and don't need to wait — they're listed last only
because they're smaller, not because they're blocked.

## Phase 1 — Foundation schema

New entities, mirroring the existing `Character`/`CharacterAppearance` shape (concrete per-kind
tables, not a generic polymorphic one — every other cross-reference in this codebase is
concretely typed):

- `Team` + `TeamAppearance`
- `Location` + `LocationAppearance` (populated by ComicVine's `location_credits` only — Metron has
  no location data; the table exists anyway since the marginal cost is near zero once the other
  four exist, and it gives `Issue.Locations` the same enrichment path as everything else)
- `Creator` + `CreatorCredit` (new — no creator entity exists today; per-issue credit strings
  `Writer`/`Penciller`/etc. stay as-is for file round-trip)
- `Publisher` (new; `Series.Publisher`/`Issue.Publisher` strings stay as-is)

A single generic external-id table covers all five kinds:

```
ComicMetadataExternalId {
  EntityKind   // Character | Team | Location | Creator | Publisher
  EntityId
  Provider     // ComicProvider: ComicVine | Metron
  ExternalId
}
```

**Directionality:** flat string fields stay authoritative for ComicInfo.xml/paperbunkr.json
round-trip. A resolver (generalizing `CharacterResolver`) auto-materializes entity rows from those
strings; provider scrapes attach external ids to matched/created rows. No second source of truth,
no migration risk — this is exactly what `Character` already does today, just extended to four
more kinds.

**Quota-aware hydration (new consideration, not previously discussed with the user, follows
directly from the existing `MetronQuota` 20/min–5,000/day limits):** issue-detail scraping already
returns `{id, name}` for arcs/characters/teams/universes/credits at no extra API cost — that's
enough to materialize entity rows and external ids. Rich fields (`Creator.bio`/`image`,
`Publisher.founded`/`country`/`image`, etc.) require a *separate* per-entity API call
(`/creator/{id}/`, `/publisher/{id}/`, ...). Fetching those eagerly during a routine issue scrape
would multiply Metron API calls by the number of distinct characters/creators/teams/publishers
touched, and could blow the daily quota on any library-wide re-scrape. **These detail calls are
lazy:** fetched on first visit to that entity's detail screen (Phase 4), then cached; a background
scheduled task (following the existing 7-task scheduler pattern) can opportunistically refresh
stale ones using the existing background-priority quota reservation, the same way other background
Metron work already respects `MetronQuota.BackgroundShouldWait`.

## Phase 2 — Provider scraping

Extend the shared `ComicVineIssueDetails` record and `ScrapeField` enum to carry (with both
clients populating what their provider actually offers):

- `Genre` → `Series.Genre` (Metron: series-level `genres`; ComicVine: not confirmed available,
  request it in the field allow-list and see what comes back)
- `AgeRating` → `Issue.AgeRating` (Metron: `rating`; ComicVine: request in field allow-list —
  currently not requested at all, so unconfirmed whether it exists there)
- `Status` → `Series.Status` (Metron: `status`)
- `ISBN`/`UPC` → new `Issue` columns (Metron only — used as series/issue match-key candidates
  alongside existing matching logic, not just display)
- Character/Team/Arc/credit **ids** from Metron (currently discarded by the `Names()` helper and
  credit parsing in `MetronClient.cs`) and Location/Character/Team/StoryArc ids from ComicVine
  (its `*_credits` fields are `{id, name}` objects too, per ComicVine's API shape, and are
  presumably discarded the same way — confirm during implementation) — stop discarding both sides,
  attach via `ComicMetadataExternalId`
- Fix the credit-role mapping gap: `RoleMap` drops any role name it doesn't recognize (confirmed
  concrete case — `Translator`, which Paperbunkr already has a destination field for, per
  `Paperbunkr.Sharing/Protocol/Dtos.cs`). Audit against Metron's live role vocabulary during
  implementation and extend `RoleMap`.

## Phase 3 — Cross-provider merge

Triggered once, when a `cv_id` link between a ComicVine-sourced and Metron-sourced series is
confirmed (not automatic/continuous — re-running it is an explicit "re-sync from other provider"
action). Field-level precedence, fixed rather than newest-wins (a stale re-scrape shouldn't
silently clobber good data) or user-adjudicated (real UI work for a rare case):

- **Metron wins:** credits, characters, teams, arcs, universes (richer, carries ids)
- **ComicVine wins:** cover image (Metron series have none), anything Metron doesn't expose
- **Otherwise:** whichever provider originally scraped the field keeps it

Applies at both series and issue level — issue-level is where most of the value is (this session
started because of issue/character/credit richness), so series-only merge would miss the point.

## Universes → Continuity (no new entity)

Metron's `universes` (on issues/characters/teams) is the same concept as the existing `Continuity`
entity, confirmed by its own doc comment. Rather than one replacing the other:

- Add `MetronId` alongside the existing `WikidataId`/`FandomKey` columns on `Continuity` — side by
  side, not a replacement.
- New `ContinuityMetronMatchResolver` (mirrors `ContinuityWikidataMatchResolver`) runs directly off
  `universes` data already present in the issue/character/team scrape response — no extra API call,
  unlike Wikidata's QID lookup.
- Where both sources tag the same series/character, Metron wins (per the Phase 3 precedence table).
  Wikidata matching keeps running and fills in continuities Metron hasn't tagged (older/obscure
  series, or entities outside Metron's coverage) — a fallback/supplement, not replaced.

## Phase 4 — UI

Character, Creator, Team, Publisher, and Location each get a real detail screen, matching the
existing Series/Issue/Manga Detail Screen pattern (bio/image/desc where the provider has it,
"appears in" issue list via the appearance join tables). Per the `avalonia`/`avalonia-pro-max`
skill mandate, route through the relevant subskill (layout-patterns, components) before building,
and run the `review-checklist` subskill before calling this phase done.

## Phase 5 — Independent wins

- **Variant covers** (`variants`) — fetched per issue, surfaced through the existing cover-picker
  (`ArcCoverImageCache`-adjacent, not the same system).
- **`foc_date`** — surfaced in the Wanted screen's Releases/calendar view alongside store date.
- **`associated` series** — lightweight Series↔Series relation (id + provider-supplied name, no
  requirement that the related series exist in the user's library), surfaced as a "related series"
  list, e.g. on the series detail screen.
- **Community ratings** (`average_rating`/`rating_count`) — displayed as a secondary "community
  says" data point alongside the app's own rating.

## Testing

- New resolvers (`TeamResolver`, `LocationResolver`, `CreatorResolver`, generalized from
  `CharacterResolver`) get the same test coverage shape as `CharacterResolverTests`.
- `MetronClientTests.cs`/`ComicVineClientTests` extend to cover every newly-parsed field, including
  the `Translator`-style role-map-miss case already present as a fixture.
- Merge precedence logic gets dedicated unit tests: correctness of the fixed per-field table, and
  idempotency of re-running "adopt" (running it twice shouldn't duplicate appearances/credits).
- `ContinuityMetronMatchResolver` gets the same test shape as the existing
  `ContinuityWikidataMatchResolver` tests, plus a precedence test for the "both sources tag it"
  case.
- New migrations follow this project's established up-down-up round-trip testing convention (see
  `project_paperbunkr_migration_updown_up_test_antipattern` history) given past migration bugs in
  this codebase.
- Phase 4 screens get on-screen verification before being called done, per this project's UI
  gotchas (deferred popup/collection mutation bug, `TextLayout` maxHeight bug) — both are realistic
  risks for new list-heavy detail screens with removable rows.

## Explicitly deferred (not this design)

- Fallback continuity for `cv_id` links (auto-suggesting the linked equivalent-provider series if a
  match breaks) — natural next step once linking exists, not built now.
- Reprints, per-issue StoryArc screens, Imprint as an entity, Price/SKU fields.
