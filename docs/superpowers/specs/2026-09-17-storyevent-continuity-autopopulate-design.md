# Story Event & Continuity Auto-Population Design

**Date:** 2026-09-17
**Status:** Approved by user (2026-09-17), all grilling rounds resolved. Revised after an external
(Gemini) review pass — 4 correctness/robustness fixes adopted (StoryArcNumber positional pairing,
Wikidata `P31` character-class filtering, publisher-alias normalization, per-run call cap +
negative caching), 2 proposed enhancements deferred (cross-publisher crossover merging, CBL
cross-referencing) — see "Explicitly out of scope." Ready for `writing-plans`.

## Summary

Today, `StoryEvent`/`EventMembership` and `Continuity`/`ContinuityMembership` are populated
entirely by hand: a user opens the Story Events screen, types a name, and manually adds
issues/series to it. This feature adds an opt-in, "suggest, don't assert" automation layer on top
of the existing manual flow, in two independently shippable phases:

- **Phase 1 — Story Events**: group the user's own already-tagged `Issue.StoryArc` metadata into
  candidate multi-issue events, optionally verified/enriched against the user's configured
  ComicVine/Metron credentials (reusing the existing `ComicVineSource`/`MetronSource` arc-lookup
  code from the CBL Manager).
- **Phase 2 — Continuity**: group `Series` into shared-universe `Continuity` records by resolving
  their most frequently appearing `Character` entities against Wikidata's "from narrative universe"
  (`P1080`) / "takes place in fictional universe" (`P1434`) properties.

Both phases produce suggestions a user reviews and explicitly accepts or dismisses — nothing is
ever auto-written to the library without a user action. Both run as new entries in the existing
7-task scheduler (weekly, automatic) and are also user-triggerable on demand.

## Background / why this shape

- `StoryEvent` is a greenfield entity with "no CE precedent" (per its own doc comment) — this
  feature is a deliberate deviation, not a CE-parity claim. `Continuity` is similarly greenfield.
- The project already has an established "propose, don't assert" posture used in two places:
  `MetadataProposal` (generic field-level proposal queue, numeric 0-1 confidence) and
  `EventSuggestionResolver`/`EventRelationSuggestionResolver` (candidate-record + `Reason` string +
  optional persisted dismissal table, zero DB writes of their own, three-value
  `FormatSignalStrength` enum — `None`/`Weak`/`Strong` — not numeric). This feature is a closer
  sibling to the latter: it produces brand-new entities from grouped signals rather than proposing a
  single field edit, so it follows `EventSuggestionResolver`'s shape (candidate record, `Weak`/
  `Strong` confidence, composite-key dismissal table), not `MetadataProposal`'s.
- **ComicVine and Metron are not new integrations.** `src/Paperbunkr.Data/ReadingLists/Sources/
  ComicVineSource.cs` and `MetronSource.cs` already implement `IReadingListSource` for the CBL
  Manager's arc-lookup flow: both can search for an arc by name and return its member issues in
  canonical order (`SearchAsync`, `GetArcIssuesInOrderAsync`), and both already read credentials
  (API key / basic auth) from the existing `CredentialStore`. Phase 1 reuses these as-is; it does
  not add a new adapter.
- **Wikidata has no prior integration anywhere in this repo** (confirmed by a repo-wide
  case-insensitive grep returning zero hits, and zero mentions in `docs/ce-feature-inventory.md` or
  `_reference/ComicRackCE`) — Phase 2 is genuinely greenfield network access, unlike Phase 1.
- **Verified before committing to the design** (not assumed): Wikidata's dedicated
  "comic book crossover event" class (`Q115378877`, "comic book storyline") has only ~318-335
  instances total, and matching series directly to a shared universe via `P1080` has an
  effectively 0% hit rate in a 5-series spot-check (Amazing Spider-Man, Fantastic Four, Ultimate
  Spider-Man, Batman, Action Comics — none carry `P1080`; only one carries the fallback `P1434`).
  Individual **characters** carry `P1080` far more reliably (Spider-Man → Earth-616, confirmed).
  This is why Phase 1 does not use Wikidata at all (ComicVine/Metron have real per-issue arc data;
  Wikidata's storyline corpus is too thin to be a useful primary source) and why Phase 2's matching
  path goes through `Character`/`CharacterAppearance` (already materialized by Phase 4d-4g) instead
  of `Series` title lookup.
- Real ComicInfo.xml allows `StoryArc` to hold multiple comma-separated arc names per issue
  (paired positionally with `StoryArcNumber`), per the anansi-project ComicInfo schema. Paperbunkr
  has never split on this comma anywhere — `Issue.StoryArc`/`StoryArcNumber` are stored and read as
  opaque single strings throughout, including in `EventSuggestionResolver`'s existing
  `.Contains(...)` heuristic. Phase 1's new grouping resolver is the first code in this repo to
  split `StoryArc` correctly; this is a scoped correctness fix inside the new resolver, not a
  refactor of the existing (still-working) `EventSuggestionResolver` check.

## Phase 1 — Story Event auto-population

### Algorithm (`StoryArcGroupingResolver`)

1. Scan all `Issue` rows with a non-empty `StoryArc`. Split `StoryArc` on comma and trim each
   token (fixing the multi-arc gap described above). `StoryArcNumber` is split the same way, by
   the same delimiter, and paired positionally by index — token *N* of `StoryArc` takes its order
   value from token *N* of `StoryArcNumber` (empty/missing when the lists are ragged). This gives
   local candidates a real fallback ordering when no external verification is available or
   configured, rather than leaving `Position` unset until a ComicVine/Metron match happens.
2. Group issues by `(normalized arc name, normalized publisher)`. Publisher is part of the
   grouping key, not just a scoring signal, specifically to prevent two unrelated arcs that happen
   to share a generic name (e.g. two different publishers each running a "Rebirth") from being
   merged into one candidate. Publisher normalization is a small static alias lookup local to this
   resolver (e.g. `"Marvel Comics"` / `"Marvel Worldwide"` → `"Marvel"`, `"DC Comics"` → `"DC"`) —
   scoped to the handful of variants actually seen in ComicInfo.xml exports, not a general-purpose
   canonicalization entity. Confirmed via repo-wide grep that no publisher normalization exists
   anywhere else in the codebase (`Issue.Publisher`/`Series.Publisher` are read as raw strings
   everywhere, including in `SmartListCatalog`/`SeriesSmartListCatalog`) — so this is new, minimal,
   and intentionally not shared/promoted to a reusable entity until a second caller needs it.
3. Discard groups with fewer than 2 issues — a single issue carrying a unique arc name is that
   issue's own story title, not a multi-issue event; `EventMembershipRole` values
   (Prologue/Core/TieIn/Epilogue/Optional/Aftermath) only carry meaning across multiple issues.
4. Exclude issues that are already members of an existing `StoryEvent` whose name matches the
   group's arc name (avoid re-proposing something the user already has). Exclude groups matching a
   persisted `StoryEventCandidateDismissal` (see schema below).
5. Emit `StoryEventCandidate(ArcName, Publisher, Members, Strength, Reason)` for each surviving
   group, with `Strength = Weak` initially (local-data-only signal).

### Verification pass (ComicVine/Metron)

If the user has ComicVine and/or Metron credentials configured (existing `CredentialStore`
entries — same ones the CBL Manager already uses), the resolver additionally calls
`ComicVineSource.SearchAsync(ArcName)` / `MetronSource.SearchAsync(ArcName)`. A result counts as a
name match when its arc name normalizes (case-insensitive, trimmed) to the same string as the
local group's `ArcName` — the same normalization used for local grouping in step 2, applied
consistently rather than a separate fuzzy-match threshold. On a match, the resolver calls
`GetArcIssuesInOrderAsync` to fetch the canonical member-issue order:

- If at least half of the local group's issues (by publication identity, e.g. ISBN/issue number
  match) are found in the external ordered list, `Strength` is upgraded to `Strong`, and each
  local issue's proposed `EventMembership.Position` is filled from the external order (issues not
  found externally are appended in publication-date order, after the matched ones).
- If no credentials are configured, or the external search finds nothing, the candidate is still
  surfaced — just at `Weak` confidence (per the user's explicit direction that local-only grouping
  must work standalone, with external verification as a strictly additive enrichment, never a
  hard gate).
- A candidate is never discarded solely because ComicVine/Metron didn't recognize it — local
  library data is trusted as the base signal.
- If a ComicVine call fails with a 429 or times out, the resolver falls back to Metron (if
  configured) for that same candidate before giving up on verification for it, rather than
  aborting the whole run.
- **Per-run call cap and negative caching**: a scheduled run verifies at most 15 candidates
  externally (largest groups first, by member count), regardless of how many unverified candidates
  exist — comfortably inside ComicVine's ~200/hour limit even accounting for a large first-time
  backlog. A candidate whose external search returns no match gets a negative-cache entry (see
  schema below) so it isn't re-queried on every subsequent weekly run; negative cache entries
  expire after 30 days (external data changes slowly enough that a monthly re-check is sufficient,
  and this avoids permanently blacklisting an arc that a source simply hadn't indexed yet).

### Schema changes

- `StoryEvent` gains nullable `ComicVineArcId` (string) and `MetronArcId` (string) columns,
  populated only when a candidate was verified against that source. (Not a Wikidata QID — Phase 1
  does not use Wikidata; see Background.)
- New entity `StoryEventCandidateDismissal { Id, ArcName, Publisher, DismissedAt }`. Keyed on
  `(ArcName, Publisher)` rather than an existing `StoryEventId`, because at dismissal time no
  `StoryEvent` row exists yet — this mirrors the shape of `EventSuggestionDismissal`'s composite
  key (`StoryEventId + IssueId`) but adapted to a pre-creation candidate.
- New entity `StoryEventVerificationNegativeCache { Id, ArcName, Publisher, Source (ComicVine |
  Metron), CheckedAt }`, one row per (arc, source) pair that returned no external match. Consulted
  before spending a call on external verification; a row older than 30 days is treated as expired
  and the lookup is retried.
- New `StoryEventResolver.GetOrCreate(name, publisher?)` (case-insensitive name match before
  insert), mirroring `ContinuityResolver.GetOrCreate`. No equivalent exists today for `StoryEvent`
  — confirmed by reading `EventMembershipResolver.cs`, which only has `AddMember`/`RemoveMember`/
  `Reorder`/lookup methods, no dedup-by-name.

### Triggers and delivery

- A new weekly entry in the existing `ScheduledTaskCatalog` (its own `ScheduledTaskDescriptor`,
  independently enable/disable-able in the Automation Preferences tab, not folded into an existing
  task — matches the scheduler's existing one-task-per-concern granularity).
- Incremental scan: only issues with an unresolved `StoryArc` (no matching `StoryEvent` yet, not
  in a dismissed group) since the task's own last successful run, tracked via the existing
  `ScheduledTaskState` timestamp — no new tracking mechanism needed.
- Manual triggers: the scheduler's existing "Run Now" affordance (bulk), plus a new per-issue
  "Look up story arc" action in the Issue Properties editor (single-item, on-demand).
- Delivery: an Activity Center alert ("N new story-event suggestions found") that deep-links into
  the Story Events screen's existing suggestion UI, reusing both systems rather than building a
  third suggestion inbox.
- If ComicVine/Metron credentials are absent, the verification step is silently skipped per
  source (same posture the CBL Manager already has for these optional configured sources) — no
  new onboarding/setup prompt.

## Phase 2 — Continuity auto-population (Wikidata)

### Algorithm (`ContinuityWikidataMatchResolver`)

Series-title lookup against Wikidata was considered and rejected — see Background for the
verified 0/5 hit rate. Instead:

1. For each `Series`, take its top 5 `Character` rows (already materialized from `Issue.Characters`
   by the existing Phase 4d-4g `CharacterResolver`) ranked by `CharacterAppearance` count.
2. For each character, call Wikidata's `wbsearchentities` API
   (`https://www.wikidata.org/w/api.php`, no API key required) to find matching items. **Filter
   results to those whose `P31` (instance of) includes `Q1114461` ("comics character")** before
   proceeding — verified directly against real data: both Spider-Man (Q79037) and Thor (Q717588,
   the Marvel character) carry `P31=Q1114461`, while a same-name mythological/historical figure
   (e.g. the Norse god Thor, or an unrelated person) does not, so this filter is what actually
   prevents a name collision from polluting the vote. (An earlier draft of this filter cited QIDs
   Q11158/Q2105456 — verified those are "acid" and an unrelated 19th-century politician,
   respectively; not usable. Q1114461 is the confirmed-correct class.) For the first `P31`-matching
   result, fetch the item and read `P1080` ("from narrative universe"), falling back to `P1434`
   ("takes place in fictional universe") if `P1080` is absent. A character with no `P31`-matching
   result is skipped, not treated as a zero-confidence hit.
3. Aggregate resolved universe QIDs across the up-to-5 characters by plurality vote:
   - ≥2 characters agreeing on the same universe QID → `Strength = Strong`.
   - Exactly 1 resolved hit → `Strength = Weak`.
   - No hits → no suggestion for this Series.
4. Emit `ContinuitySuggestion(Series, WikidataQid, UniverseLabel, Strength, Reason)`, excluding
   series already in a `Continuity` with that `WikidataId`, and excluding series matching a
   persisted `ContinuitySuggestionDismissal`.
5. On accept: `ContinuityResolver.GetOrCreate(universeLabel)` (existing case-insensitive dedup),
   `AddSeriesToContinuity`, and set `Continuity.WikidataId`.

Accepting a single `Weak` (1-hit) suggestion is intentional: requiring 2+ agreeing characters
would produce almost no suggestions at all given the sparse coverage confirmed during design
(only Spider-Man was verified to carry `P1080` in the spot-check sample). Surfacing low-confidence
single-hit suggestions, clearly labeled `Weak`, lets the user decide rather than the resolver
silently discarding a plausible match.

### Schema changes

- `Continuity` gains a nullable `WikidataId` (string, QID) column.
- New entity `ContinuitySuggestionDismissal { Id, SeriesId, WikidataQid, DismissedAt }`, mirroring
  `EventSuggestionDismissal`'s composite-key shape (`StoryEventId + IssueId` → here
  `SeriesId + WikidataQid`).
- New entity `ContinuityCharacterLookupNegativeCache { Id, CharacterId, CheckedAt }` — one row per
  `Character` that resolved to no `P31`-filtered Wikidata match, same 30-day expiry rationale as
  Phase 1's negative cache, so a character with no Wikidata presence isn't re-queried by every
  weekly run.

### Triggers and delivery

Identical posture to Phase 1, as its own separate weekly scheduled task (different query target
and error profile — no ComicVine/Metron involvement here — so it gets independent enable/disable
rather than sharing Phase 1's task):

- Weekly automatic scan (incremental, via the task's own `ScheduledTaskState` timestamp) + manual
  "Run Now" (bulk) + a new per-series action next to the existing Continuity chips UI on the
  Related tab (single-item, on-demand).
- Activity Center alert, deep-linking into the Story Events screen's Continuities mode.
- No credentials needed — Wikidata's basic search API is open.

## Cross-cutting concerns

- **Attribution**: suggestion cards show "Source: ComicVine" / "Source: Metron" / "Source:
  Wikidata", matching the existing AniList adapter's attribution convention.
- **Confidence display**: both phases use a `Weak`/`Strong` two-value strength enum (matching
  `FormatSignalStrength`'s shape), not a numeric 0-1 score — deliberately different from
  `MetadataProposal.Confidence`, because this feature's job (propose a *new* grouped entity from
  multiple weak signals) is closer to `EventSuggestionResolver`'s existing shape than to
  `MetadataProposal`'s single-field-edit model.
- **Rate limits / etiquette**: ComicVine (~200 req/hour) and Metron (20 req/min, 5,000/day) both
  already have throttling in their existing `IReadingListSource` implementations, reused as-is.
  Wikidata's `wbsearchentities` has no hard published per-second cap but documented etiquette
  (serial requests, contactable `User-Agent`, exponential backoff on 429) — the new resolver sends
  requests serially with a `User-Agent` identifying the app, honoring `Retry-After`.
- **Error handling**: network failures during a scheduled run are caught per-source/per-item (one
  failed lookup doesn't abort the whole scan) and logged; the scheduled task's summary string
  reports partial completion the same way other scheduled tasks do. No app-level "is online"
  pre-check exists in this codebase today (confirmed) — both new tasks rely on catching
  `HttpRequestException`/timeout the same reactive way `AniListMetadataProvider` already does,
  rather than adding a new proactive connectivity check.

## Explicitly out of scope

- **Gap detection** (surfacing when ComicVine/Metron's canonical arc issue list includes issues
  the user's local library doesn't have) — real value, but overlaps Library Health's domain. The
  user has a specific future use for this (a planned Mylar connector, where a missing-issues list
  is a natural want-list source) — noted for that future work, not built here.
- **Backfilling external IDs onto existing, manually-created `StoryEvent`/`Continuity` rows.**
  This feature only proposes brand-new entities from currently-ungrouped data; it does not
  re-scan or attempt to attach `ComicVineArcId`/`MetronArcId`/`WikidataId` to rows the user already
  created by hand.
- **Full-library Wikidata coverage validation at scale.** Phase 2's hit rate was verified only on
  a small spot-check sample; it is understood and accepted that most Series in most libraries will
  not resolve to any suggestion. This is a best-effort supplementary feature, not a comprehensive
  one.
- Any provider beyond ComicVine, Metron, and Wikidata (e.g. GCD, League of Comic Geeks) — not
  requested, not scoped.
- **Cross-publisher crossover merging** (`IsCrossover`-style handling for events that are
  genuinely multi-publisher by design, e.g. JLA/Avengers). The `(ArcName, Publisher)` grouping key
  exists specifically to *prevent* cross-publisher merges for unrelated same-named arcs; a real
  crossover is the one case where merging across publishers would be correct, but reliably
  telling "real crossover" apart from "name collision" without a human in the loop is a genuinely
  separate design problem, not a small addition to this one. Considered during review, deferred.
- **CBL reading-list cross-referencing** to auto-upgrade a `Weak` candidate to `Strong` when its
  membership matches an already-imported `.cbl` file. Real potential signal, but it's a new data
  dependency (the CBL Manager's imported-list store) not covered by this design's grilling session
  — deferred rather than folded in silently.

## Testing

- `StoryArcGroupingResolverTests`: comma-splitting (including positional `StoryArcNumber`
  pairing, and the ragged-list case where the two comma lists differ in length), publisher-alias
  normalization, `(ArcName, Publisher)` grouping key (including the cross-publisher-collision
  case), the ≥2-issue threshold, dismissal filtering, negative-cache expiry (29 vs 31 days old),
  the 15-candidate-per-run cap prioritizing largest groups, and exclusion of issues already in a
  matching `StoryEvent`.
- `ContinuityWikidataMatchResolverTests`: `P31=Q1114461` filtering (a same-named non-comics
  Wikidata result must not contribute to the vote), plurality-vote aggregation (0/1/2+ hits →
  none/`Weak`/`Strong`), top-5-by-appearance-count character selection, dismissal and
  negative-cache filtering. HTTP calls mocked the same way `AniListMetadataProviderTests` already
  mocks AniList's GraphQL calls.
- Migration tests for the two new nullable-column additions and two new dismissal tables, run
  against a real pre-existing-rows SQLite database per this project's established migration-test
  pattern (not just `EnsureCreated`).
- Scheduled-task integration tests confirming both new tasks are independently enable/disable-able
  and that "Run Now" invokes the same resolver path as the automatic run.
