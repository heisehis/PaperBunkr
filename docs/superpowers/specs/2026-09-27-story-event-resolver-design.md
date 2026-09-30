# Story Event resolver (ComicVine/Metron duplicate events) — design

*Status: approved by the user 2026-09-27 after four grilling rounds (Q1–Q17) and a five-section design review; the user asked
for it to be implemented straight away. First of two sub-projects: this resolver, then a continuity-wide map (every issue of
every event in a continuity on one swimlane), which would double-count every duplicated event without it.*

## Goal

The same story arc often exists twice as a Story Event because ComicVine and Metron name it differently — ComicVine
"Hulk: Planet Hulk", Metron "Planet Hulk" — and issues scraped from each provider were grouped under each spelling. The
resolver gives every event both provider arc ids where it can, merges events that turn out to be the same arc (silently
when it is sure, through a review list when it isn't), and stops the duplicate from being created again.

## Facts this rests on (verified in the repo 2026-09-27)

- **Events are created from provider data in exactly two places**, both through `StoryEventResolver.GetOrCreate`: accepting a
  story-arc suggestion (`EventsScreenViewModel.StoryEventSuggestions.AcceptStoryEventCandidateAsync`) and Issue Properties'
  arc look-up (`IssuePropertiesScreenViewModel.LookUpStoryArc`). The New Event dialog and "+ New" create events with no
  duplicate check. Nothing else creates events (CBL import, scrapers, CE migration, plugins and scheduled tasks don't).
- **`GetOrCreate` matches by name only** (exact, then `TitleNormalizer.NamesMatch(ignoreVolume: false)`), never by arc id.
- **At most one provider id per event.** `ArcExternalVerificationService` tries ComicVine first and only falls back to Metron,
  requires an exact (trimmed, case-insensitive) name, and needs ≥ half of the local issue numbers in the external arc. So a
  ComicVine-named and a Metron-named event never share an id today.
- **Names don't match:** `StripDown("Hulk: Planet Hulk")` = `HulkPlanetHulk`, `StripDown("Planet Hulk")` = `PlanetHulk`.
- **`StoryArcGroupingResolver`** groups candidates by `(StripDown(arc), normalized publisher)` and skips issues already in an
  event *with the same stripped name* — after a merge, issues whose `StoryArc` text carries the loser's spelling would be
  re-proposed, re-creating the duplicate.
- **Issues carry no arc id.** Arc names from both providers land joined in `Issue.StoryArc`; ids are used only in memory
  for ordering. Provider *issue* ids are stored (`ComicMetadataExternalId`, kind Issue), and both clients'
  `GetIssueDetailsAsync` return `StoryArcs` as `(ExternalId, Name)`.
- **Metron records carry `cv_id`.** `MetronClient` already reads it for series. Whether Metron's arc endpoint has it must be
  verified against the API before relying on it (see Risks).
- **No event merge exists.** Referencing rows: `EventMembership` (no unique `(StoryEventId, IssueId)` index — uniqueness is
  enforced only in `EventMembershipResolver.AddMember`), `EventRelation` (both FKs cascade, no unique index; `TryCreate`
  rejects self and same-type duplicates either way), `EventSuggestionDismissal` (unique `(StoryEventId, IssueId)`),
  `ReadingList.StoryEventId` (SetNull). Deleting an event cascades memberships, relations and dismissals, so a merge must
  re-point before deleting. `StoryEventCandidateDismissal` and `StoryEventVerificationNegativeCache` are keyed by name, not id.
- **Nothing records who made or named an event.**

## Decisions (from grilling)

| # | Decision |
|---|---|
| Q1 | Resolver first, own spec; the continuity map is a separate, later spec. |
| Q2/Q14 | Core job is **id completion**: fill both arc ids per event; shared id = strongest duplicate evidence. Local signals (name match after prefix strip + ≥ 50 % shared issues) cover unverifiable events. |
| Q3/Q16 | Runs at creation (the new event, immediately, in the background) and as a weekly sweep (plus once after upgrade, plus a manual button). Batched, resumable, existing rate limits; results cached 30 days or until members change; misses to the existing negative cache. |
| Q4/Q9/Q13 | New `Origin` marker: *Provider* (accepted/looked-up) vs *User* (hand-made or renamed). Existing events: Provider if they already have an arc id, else User. A user-set name always wins. |
| Q5 | Merge conflicts: survivor order kept, loser-only issues slotted by cover date, role precedence (see §2), dates widen, longer description, both ids kept. |
| Q6/Q7 | One Activity Center summary per sweep, no toasts; no undo in v1. |
| Q8 | Merged-away names become aliases; create/group/verify match aliases. |
| Q10 | Prefix strip only when the prefix names one of the event's own member series. |
| Q11 | `GetOrCreate` also matches aliases and prefix-stripped keys; the New Event dialog keeps no check. |
| Q12 | Review list in the Story Events sidebar: "Possible duplicates · N", **Merge** / **Not the same**. |
| Q15 | User events get ids filled too, but any merge involving one goes to review. |
| Q17 | Evidence rank: issue arc credits > Metron `cv_id` > name search with issue-list overlap > name alone. Genuine conflicts never merge; flagged "sources disagree". |
| Approach | **A** — hard merge through new services (not extending `StoryEventResolver`/`ArcExternalVerificationService` in place, not a soft "equivalent-to" link that every consumer would have to follow). |

## Design

### 1. Data model (one additive migration)

**`StoryEvent`** gains:
- `Origin` — `StoryEventOrigin { User = 0, Provider = 1 }`, required. The migration backfills `Provider` where
  `ComicVineArcId` or `MetronArcId` is set, `User` elsewhere.
- `IdentityCheckedAt` (`DateTime?`) — last id-completion run.
- `IdentityMemberKey` (`string?`) — fingerprint of the member issue ids at that run; a different fingerprint means "re-check".
- `IdentityConflict` (`string?`) — set when sources disagree ("ComicVine arc 4512 vs 6620"); puts the event in review and
  blocks silent merges.

**`StoryEventAlias`** (new): `Id`, `StoryEventId` (cascade), `Name`, `Key` (the stripped match key), `Source`
(`StoryEventAliasSource { Merge, Provider }`). Unique `(StoryEventId, Key)`; index on `Key`.

**`StoryEventDuplicateDismissal`** (new): `Id`, `LowerEventId`, `HigherEventId` (ordered pair, both cascade), `CreatedAt`.
Unique on the pair.

Not stored: the review list (recomputed locally on sidebar refresh) and search misses (existing
`StoryEventVerificationNegativeCache`).

### 2. Matching and merging (local, no network) — `Paperbunkr.Data/Metadata/`

**`EventNameKeys.For(name, memberSeriesNames, aliases)`** → the set of match keys: `StripDown(name)`; the same with a
trailing `(19xx|20xx)` removed; a prefix-stripped variant when the name is `X: rest` or `X - rest` (also en/em dash) and `X`
matches (`NamesMatch`, volume ignored) one of the event's member series; and every alias key. Keys are lower-cased.

**`StoryEventIdentityResolver.FindPairs(context)`** → `DuplicatePair(EventA, EventB, Evidence, IsSilent, Reason)`:
- Candidates only from shared provider id, shared name key, or shared member issue — never all-against-all.
- Overlap = shared issues ÷ the smaller event's member count.
- Evidence, strongest first: `SharedProviderId`, `NameAndOverlap` (≥ 0.5), `NameOnly`, `OverlapOnly` (≥ 0.5).
- **Silent** iff evidence is `SharedProviderId` or `NameAndOverlap`, both events are `Provider`, neither has
  `IdentityConflict`, the pair isn't dismissed, and the two events don't hold *different* ids for the same provider.
- Different ids for the same provider → never silent; `Reason` "Sources disagree: ComicVine 4512 vs 6620".
- `FindReviewItems` = non-silent pairs (minus dismissed) plus single events with `IdentityConflict`.

**`StoryEventMerger.Merge(context, a, b)`** (one transaction):
- Survivor: the User-origin one if exactly one is; else more members; else the lower id. The result is User if either was.
- Name: a User survivor keeps its name; otherwise the name whose key is the prefix-stripped variant of the other's
  ("Planet Hulk"), else the survivor's. The other name and all loser aliases become survivor aliases (`Merge`), skipping
  keys equal to the survivor's own name key.
- Ids: fill empty ones from the loser. Dates: earliest start, latest end. Description: the longer non-empty one.
  `SpineSeriesId`: survivor's if set, else loser's.
- Members: survivor order kept. Loser-only issues are inserted after the last survivor issue whose cover date (year, month)
  is ≤ theirs; undated ones are appended in the loser's order. Positions renumbered 1…n.
- Issue in both: keep the stronger row — a non-Core role beats Core; between non-Core roles, a user-set role
  (`RoleSource` User or null) beats a detected one; two different user-set roles → the survivor's.
- Re-point relations (drop self-relations and same-type duplicates in either direction), issue-suggestion dismissals (drop
  where the survivor already has that issue), and reading lists. Duplicate dismissals naming the loser are dropped.
- Delete the loser; clear the survivor's `IdentityMemberKey` so its ids are re-checked.

### 3. Id completion (network) — `StoryEventIdCompletion`

Per event, skipped when `IdentityCheckedAt` is < 30 days old **and** `IdentityMemberKey` still matches. Candidates per
provider, strongest first:
1. **Issue arc credits** — up to 5 member issues with that provider's issue id; fetch their details; an arc id carried by a
   majority of the sampled issues whose name matches one of the event's keys is the candidate.
2. **Metron `cv_id`** — with a Metron arc id (stored or from 1), fetch the arc and read `cv_id` → ComicVine candidate.
3. **Name search** on the provider still missing — by name, aliases and prefix-stripped name; accepted only when the result's
   name matches a key **and** its issue list covers ≥ half the local members (provider issue id where known, else series +
   number). Misses go to the negative cache.

Settling, per provider: the strongest candidate fills an empty id. Two different ids from the top source, or a strong result
contradicting a stored id → `IdentityConflict` set, nothing overwritten. The other provider's arc spelling is added as a
`Provider` alias. Stamp `IdentityCheckedAt` and `IdentityMemberKey`.

Budget/failure: existing clients and their throttles; ≤ 5 sampled issues per provider per event; a provider with no
credentials is skipped (noted once in the sweep summary); a network error leaves the event unstamped (retried next run)
and never aborts the sweep.

Order inside a sweep: id completion for every due event, then `FindPairs`, then silent merges.

### 4. Triggers and UI

- **Accept / look-up:** `GetOrCreate` also matches name keys and aliases (so Metron "Planet Hulk" joins an existing
  "Hulk: Planet Hulk"); the event is marked `Provider` and gets completion + matching in the background.
- **Weekly sweep:** new scheduled task "Story event identity", default on, next to story-event autodetect; batched and
  resumable; also runs soon after upgrade.
- **Manual:** **Find duplicate events** in the new sidebar section.
- **Rename** (Edit details) sets `Origin = User`.
- **Grouping and verification** check name keys and aliases, so a merged-away spelling is not re-proposed.
- **Sidebar:** collapsible "Possible duplicates · N" (only when N > 0). Pair rows: both names, reason, which name stays,
  **Merge** / **Not the same**. Conflict rows: **Check again** (clears the note, re-runs completion). Clicks defer one
  dispatcher tick (CLAUDE.md). If the open event is merged away, the screen switches to the survivor.
- **Activity Center:** a sweep is one job with progress, ending "34 ids filled · 4 duplicates merged · 2 to review" with the
  merged pairs in its details. No toasts for background runs; accept-time checks are silent unless they merge.
- Plugins read events only; a plugin holding a merged-away id stops finding it, as after a delete.

## Testing

Data (`Paperbunkr.Data.Tests`, real SQLite, fake provider clients — no live network): `EventNameKeysTests`,
`StoryEventIdentityResolverTests`, `StoryEventMergerTests`, `StoryEventIdCompletionTests`, prevention tests on
`GetOrCreate` and grouping, a forward-only migration test (Origin backfill, new tables). App (headless): the sidebar section
appears only with pairs, Merge / Not the same act only after `TestDispatcher.Drain()`, merging the open event switches to the
survivor, rename marks User, a sweep reports one Activity summary. Manual: real provider behaviour (Metron arc `cv_id`,
actual naming) during planning; the sidebar on screen by the user.

## Risks

- **Metron arc `cv_id` is unverified.** If the arc endpoint lacks it, source 2 is dropped and sources 1 and 3 carry the load.
- **Shared tree:** `EventsScreenViewModel*`, `EventsScreen.axaml`, `StoryEventResolver`, `StoryArcGroupingResolver` and the
  model snapshot may carry other sessions' edits; check `git status` first.
- **Overlap-only false positives** (a prelude event inside its main event) are why overlap alone never merges silently.

## Follow-up

The continuity-wide map (Option 2) — every issue of every event in a continuity on one swimlane — is the next spec.

## Implementation notes (2026-09-27)

Built per `2026-09-27-story-event-resolver-plan.md`. Where the build differs from the text above:

- **Metron `cv_id` is verified.** Metron's `ArcSerializer` fields are `id, name, desc, image, cv_id, gcd_id, resource_url,
  modified` (Metron-Project/metron `api/v1_0/serializers/arc.py`). `MetronSource.GetArcOverviewAsync` now reads it into
  `ArcOverviewInfo.ComicVineId`. The Risks entry no longer applies.
- **Issue-list overlap matches on series + number.** The arc sources' `ArcIssue` carries no provider issue ids, so there's no
  "provider issue id where known" branch.
- **Name keys are symmetric.** A provider arc name matches an event when either name's keys, computed with the event's member
  series, overlap. Metron's "Planet Hulk" therefore matches an event named "Hulk: Planet Hulk", and the reverse.
- **Verification is loosened too.** `ArcExternalVerificationService` accepts a search result whose name keys overlap the
  candidate's, not only an exact name. Its issue-number overlap check still has to pass.
- **`GetOrCreateForIssues`** looks up the incoming issues' series names, so `GetOrCreate` can apply the prefix rule at accept time.
- **"Find duplicate events" toasts** (it's a click). The weekly task and the accept-time check don't, except to announce a merge.
- **The survivor-name rule has one extra case.** A user-origin event always survives with its own name, and the merged event
  becomes User if either was.
- **Batch size is 40 events per run** (`StoryEventIdentitySweep.DefaultBatchSize`). With at most 5 sampled issues per provider
  per event, that stays inside Metron's 14/min background share over a run.
- **Test seams:** `EventsScreenViewModel.IdentityRunner` and `IdentitySources` keep headless tests on the pinned dispatcher
  thread and off the network.
- **Verified:**
  - Data: 60/60 (resolver tests, migration test, existing resolver/grouping/verification suites).
  - App: 237/237.
  - Not verified: live providers, and the sidebar on screen.
