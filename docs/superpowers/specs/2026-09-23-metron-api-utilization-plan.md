# Metron API Utilization — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md*

## Status (2026-09-23): all 5 phases built, uncommitted

Every phase in this plan was implemented in one session. What shipped vs. what's disclosed as a
real, bounded follow-up (not a TODO left silently unfinished):

- **Phase 1 (foundation)** — shipped as designed. `Team`/`Location`/`Creator`/`Publisher` entities,
  `ComicMetadataExternalId`, resolvers, and (the survey's own correction #1) wired into the scrape
  pipeline for the first time — `Character` included, retrofitting its long-standing gap. Migration
  `AddComicMetadataEntities`. 12 new tests, all passing.
- **Phase 2 (provider scraping)** — shipped, with two corrections found only by reading the real
  code/schema, not assumed: (a) Genre's real home is `IssueTag` (`Series.Genre`'s doc comment pointed
  at a since-removed `Issue.Genre`), simplifying the plan by removing the need for a separate
  series-level apply path for Genre; (b) `Series.Status` is **not** available from the issue-detail
  endpoint at all (confirmed absent from Metron's own `IssueSeries` schema, unlike Genre which
  genuinely is present there) — an earlier draft of this work assumed otherwise and was corrected
  before landing. `RoleMap` gap (`Translator`) fixed. Migration `AddMetronScrapeFields`.
- **Phase 3 (cross-provider merge)** — the precedence engine (`ComicProviderMerge`) is real, fully
  tested, and correct (caught and fixed two of its own bugs during testing: `ComicMetadataExternalIdSync`
  was attaching ids for names a restrictive policy had blocked from ever reaching the issue's own
  text, and `AgeRating`/`Isbn`/`Upc` were missing from the "Metron wins" field list, letting
  ComicVine's gap-fill silently win by default even when Metron had the data). `ComicProviderAdopt`
  does the real fetch-both-and-merge work. **Disclosed gap:** no UI trigger — neither `Issue` nor
  `Series` stores its own external id for both providers anywhere durable today, so there's nothing
  yet for a button to read from. Building that store is the real remaining work, not attempted here.
- **Phase 4 (UI)** — one shared `MetadataEntityDetailScreenViewModel`/View covers all five entity
  kinds (a deliberate, disclosed deviation from "five separate screens" — same data shape, DRY/YAGNI),
  fully wired into `MainViewModel` navigation/history. Forced a clean rebuild to confirm the XAML
  actually weaves, per this project's own AVLN2000 gotcha. **Disclosed gaps:** no bio/image/description
  (no per-entity Metron detail hydration was built — Character/Team/Location/Creator/Publisher only
  ever get `{id, name}` from the issue-detail response, never `/creator/{id}/` etc.); no entry-point
  chips added to existing screens (Detail/Issue Properties still show Characters/Teams as plain text,
  not links to the new screen); not verified on-screen (memory: computer-use permission revoked, and
  launching the exe against the shared dev DB carries a documented corruption risk from an improper
  shutdown - relied on the forced-clean build + full test suite instead).
- **Phase 5 (independent items)** — all four shipped with real schema, real parsing, real tests:
  `foc_date` (Wanted Releases tab, new badge), community ratings (`Issue.CommunityRating` already
  existed and is CE-parity-established elsewhere in the app - Metron's `average_rating` populates it
  directly, `CommunityRatingCount` is new), variant covers (`IssueVariantCover`, wired into the scrape
  pipeline so it's genuinely populated - no external id exists for a variant at all, confirmed against
  Metron's own schema, so identity is name+image-url), associated series (`SeriesAssociation`).
  Bonus finds while here: `Imprint` wiring was missing entirely (Issue.Imprint already existed,
  confirmed via mokkari's real schema, now populated); `reprints` stayed explicitly out of scope per
  the design's own decision. **Disclosed gaps:** variant-cover picker UI, associated-series "related
  series" UI, and both syncs' live scheduling triggers are real, tested building blocks with no
  automatic trigger wired to them yet (same shape as Phase 3's disclosed gap).
- **Regression check:** full `Paperbunkr.Data.Tests`/`Paperbunkr.App.Tests`/`Paperbunkr.Daemon.Tests`
  suites run clean except one pre-existing, unrelated failure (`RecapResolver.cs` remote-isolation
  allowlist gap from other uncommitted work already in this tree before this session started).
- **Not committed** — per this project's own standing feedback, committing wasn't offered/performed;
  left for the user alongside whatever else lands in this already-entangled working tree.

Survey corrected five assumptions the design doc didn't pin down; each is called out inline below
where it changes a step, rather than silently guessed:

1. `CharacterResolver.SyncFromIssue` is **not** wired into the scrape pipeline today — it only
   runs on manual Issue Properties save, or a one-time empty-table backfill. The design doc's "no
   second source of truth" claim only holds if scraping also syncs the derived entity index, so
   Phase 1 adds that wiring (and retrofits `Character` at the same time, since it has the same gap).
2. `ContinuityResolver`/`ContinuityWikidataMatchResolver` are `internal` — `ContinuityMetronMatchResolver`
   must live in `Paperbunkr.Data.Metadata` to call them directly.
3. No small precedent detail screen exists (Series/Manga/Book Detail are 600–750 lines, built around
   readers/tabs/tracker-sync). The five new screens follow ViewLocator/MainViewModel *conventions*
   only, not that structural bulk.
4. `ExternalMediaId.Provider` is typed `ExternalMetadataProvider` (the manga-tracker enum) — the new
   `ComicMetadataExternalId.Provider` must be typed `ComicProvider` instead; different enum, don't reuse.
5. `PullListRelease` has no `FocDate` column — Phase 5's `foc_date` work needs its own migration on
   that entity specifically.

One more judgment call not specified by the design doc: **Publisher's cardinality is 1-per-issue,
not many-per-issue** (unlike Character/Team/Location/Creator, which are lists). So `Publisher` gets
a direct nullable FK on `Series`/`Issue` (`PublisherEntityId`), not an `Appearance`-style join table
— a join table for a single-valued relationship would be pointless indirection.

---

## Phase 1 — Foundation schema

### Step 1: `Team` + `TeamAppearance` entities
**Files:** `src/Paperbunkr.Data/Entities/Team.cs` (new), `src/Paperbunkr.Data/Entities/TeamAppearance.cs` (new)
**What:** Exact mirror of `Character.cs`/`CharacterAppearance.cs` (`src/Paperbunkr.Data/Entities/Character.cs:10-17`,
`CharacterAppearance.cs:11-22`), renamed.
**Depends on:** none
**Verify:** compiles; covered by Step 7's DbContext test.

### Step 2: `Location` + `LocationAppearance` entities
**Files:** `src/Paperbunkr.Data/Entities/Location.cs` (new), `src/Paperbunkr.Data/Entities/LocationAppearance.cs` (new)
**What:** Same mirror, for locations.
**Depends on:** none

### Step 3: `Creator` + `CreatorCredit` entities
**Files:** `src/Paperbunkr.Data/Entities/Creator.cs` (new), `src/Paperbunkr.Data/Entities/CreatorCredit.cs` (new)
**What:** `Creator { Id, Name, List<CreatorCredit> Credits }`. `CreatorCredit { Id, CreatorId, Creator?, IssueId, Issue?, Role (string?) }`
— `Role` exists because one creator can hold multiple roles on one issue (writer *and* artist), so
unlike `CharacterAppearance` the join carries a discriminator column.
**Depends on:** none

### Step 4: `Publisher` entity + FK columns
**Files:** `src/Paperbunkr.Data/Entities/Publisher.cs` (new); edit `src/Paperbunkr.Data/Entities/Series.cs`
(add `PublisherEntityId` (`int?`) + `Publisher?` nav, additive, existing `Publisher` string column untouched);
edit `src/Paperbunkr.Data/Entities/Issue.cs` (same addition)
**What:** `Publisher { Id, Name }`. No appearance table — single-valued per issue/series.
**Depends on:** none

### Step 5: `ComicMetadataEntityKind` enum + `ComicMetadataExternalId` entity
**Files:** `src/Paperbunkr.Data/Entities/ComicMetadataEntityKind.cs` (new), `src/Paperbunkr.Data/Entities/ComicMetadataExternalId.cs` (new)
**What:**
```csharp
public enum ComicMetadataEntityKind { Character, Team, Location, Creator, Publisher }

public class ComicMetadataExternalId
{
    public int Id { get; set; }
    public ComicMetadataEntityKind EntityKind { get; set; }
    public int EntityId { get; set; }
    public ComicProvider Provider { get; set; }   // NOT ExternalMetadataProvider — see note above
    public string ExternalId { get; set; } = string.Empty;
}
```
**Depends on:** none
**Note:** two indexes needed at Step 6 — a unique `(EntityKind, EntityId, Provider)` (one external id
per provider per local entity) and a unique `(EntityKind, Provider, ExternalId)` (reverse lookup: given
a provider + its id, find the existing local entity before falling back to name matching — this is the
lookup resolvers use during scraping in Phase 2).

### Step 6: DbContext registration
**Files:** edit `src/Paperbunkr.Data/PaperbunkrDbContext.cs`
**What:** Add `DbSet<Team>`, `DbSet<TeamAppearance>`, `DbSet<Location>`, `DbSet<LocationAppearance>`,
`DbSet<Creator>`, `DbSet<CreatorCredit>`, `DbSet<Publisher>`, `DbSet<ComicMetadataExternalId>`
(mirroring lines 81-83's `Character`/`CharacterAppearance` declarations). Add fluent config blocks
mirroring `Character`'s exactly (`PaperbunkrDbContext.cs:1086-1108`) for each new entity:
- `Team`/`TeamAppearance`, `Location`/`LocationAppearance`: identical shape to `Character`/`CharacterAppearance`.
- `Creator`/`CreatorCredit`: same shape, but the appearance-join's unique index becomes
  `HasIndex(c => new { c.CreatorId, c.IssueId, c.Role }).IsUnique()` (not just `CreatorId, IssueId`,
  since one creator can have two roles on one issue and both credit rows are legitimate).
- `Publisher`: `HasKey(Id)`, `Property(Name).IsRequired()`, `HasIndex(Name)`. Plus on `Series`/`Issue`
  config blocks, add `HasOne(...).WithMany().HasForeignKey(PublisherEntityId).OnDelete(DeleteBehavior.SetNull)`
  (not Cascade — deleting a Publisher shouldn't cascade-delete every series/issue that references it).
- `ComicMetadataExternalId`: `HasKey(Id)`, the two unique indexes from Step 5's note.
**Depends on:** Steps 1-5
**Verify:** `dotnet build src/Paperbunkr.Data/Paperbunkr.Data.csproj`

### Step 7: Migration
**Files:** new file(s) under `src/Paperbunkr.Data/Migrations/` (generated), `PaperbunkrDbContextModelSnapshot.cs` (regenerated)
**What:** `dotnet ef migrations add AddComicMetadataEntities --project src/Paperbunkr.Data --startup-project src/Paperbunkr.App`
— single migration batching all Step 1-6 model changes (matches this project's convention, confirmed
via the most recent 5 migrations). Never hand-edit the generated files or the snapshot.
**Depends on:** Step 6
**Verify:** new `src/Paperbunkr.Data.Tests/AddComicMetadataEntitiesMigrationTests.cs`, mirroring
`AddReadingGoalsMigrationTests.cs`'s shape exactly (temp SQLite file, `Database.Migrate()`, one
forward-migrate + insert + reload test per new table — no up-down-up test, per this project's own
established anti-pattern finding).

### Step 8: `TeamResolver`, `LocationResolver`
**Files:** `src/Paperbunkr.Data/Metadata/TeamResolver.cs` (new), `LocationResolver.cs` (new)
**What:** Exact mirror of `CharacterResolver.cs` (`ParseNames`, `GetOrCreate`, `SyncFromIssue`,
`RebuildAll`, plus the `GetXForSeries`/`GetSeriesForX` read helpers), parsing `Issue.Teams`/
`Issue.Locations` instead of `Issue.Characters`. Keep `public static class` (matching `CharacterResolver`,
not `internal` — these are read elsewhere the same way Character is, e.g. any future browse-by-team UI).
**Depends on:** Steps 1-2, 6
**Verify:** new `TeamResolverTests.cs`/`LocationResolverTests.cs`, mirroring `CharacterResolverTests.cs`'s
fixture shape (`Path.GetTempPath()` real SQLite file, `Database.EnsureCreated()` not `Migrate()`, same
`SeedIssue`-style helper adapted to `Teams`/`Locations` text).

### Step 9: `CreatorResolver`
**Files:** `src/Paperbunkr.Data/Metadata/CreatorResolver.cs` (new)
**What:** Not a direct mirror — `Character`/`Team`/`Location` each parse one combined free-text
`Issue` field, but creator credits are split across `Issue`'s separate `Writer`/`Penciller`/`Inker`/
`Colorist`/`Letterer`/`CoverArtist`/`Editor` string columns (`Issue.cs:94-108`). `SyncFromIssue` must
iterate all seven fields, parse each as comma/semicolon-separated names (reuse
`CharacterResolver.ParseNames`'s split logic rather than duplicating it — extract that into a shared
helper if `CharacterResolver.ParseNames` isn't already `public`), and for each name write/update a
`CreatorCredit` row with `Role` set to that field's name (`"Writer"`, `"Penciller"`, etc.) Prune stale
credits the same way `CharacterResolver.SyncFromIssue` prunes stale appearances (line 43 area) —
diff against what's currently in the DB for that issue, remove rows no longer present in any of the
seven fields.
**Depends on:** Step 3, 6
**Verify:** new `CreatorResolverTests.cs` — needs cases for one creator appearing under two different
role fields on the same issue (asserts two `CreatorCredit` rows, not a merge/collision), and for a
name removed from a field between two syncs (asserts prune).

### Step 10: `PublisherResolver`
**Files:** `src/Paperbunkr.Data/Metadata/PublisherResolver.cs` (new)
**What:** No appearance/sync-and-prune needed (single-valued). `GetOrCreate(context, name)` mirrors
`CharacterResolver.GetOrCreate`. `SyncIssue(context, issueId)`/`SyncSeries(context, seriesId)` read
`Issue.Publisher`/`Series.Publisher` (falling back Series→Issue per the existing doc-comment precedent
at `Series.cs:69` — Issue's copy is the real source), resolve-or-create a `Publisher` row, and set
`PublisherEntityId` directly (no join row, no pruning — just overwrite the FK).
**Depends on:** Step 4, 6
**Verify:** new `PublisherResolverTests.cs`.

### Step 11: Wire resolvers into the scrape pipeline
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/ScrapeByIdService.cs` (around line 96),
`src/Paperbunkr.Data/ComicVine/ScrapeOrchestrator.cs` (around line 405)
**What:** Immediately after each `IssueDetailsApplier.Apply(...)` call, add:
`CharacterResolver.SyncFromIssue(context, issue.Id)` (**retrofit** — this call doesn't exist today,
per the survey's correction #1), `TeamResolver.SyncFromIssue(...)`, `LocationResolver.SyncFromIssue(...)`,
`CreatorResolver.SyncFromIssue(...)`, `PublisherResolver.SyncIssue(...)`. This is what makes the design
doc's "provider scrapes attach external ids to matched/created rows" actually true — without this step,
entities only materialize on manual issue-properties save, same gap `Character` has today.
**Depends on:** Steps 8-10
**Verify:** extend existing `ScrapeByIdTests.cs`/orchestrator tests to assert `Character`/`Team`/
`Location`/`Creator`/`Publisher` rows exist after a scrape, not just that `Issue` string fields were set.

---

## Phase 2 — Provider scraping (extends the shared record; both clients)

### Step 12: Extend `ComicVineIssueDetails`
**Files:** edit `src/Paperbunkr.Data/ComicVine/ComicVineIssueDetails.cs`
**What:** The record's flat string lists (`StoryArcs`, `Characters`, `Teams`, `Locations`) become
`IReadOnlyList<(int? ExternalId, string Name)>` (breaking change — every constructor call site and
test fixture must update). `ComicVineCredit` gains `CreatorExternalId` (`int?`) and `RoleExternalId`
(`int?`). New scalar fields: `Genre` (`IReadOnlyList<string>`, series-level per the design doc — see
Step 14 note), `AgeRating` (`string?`), `SeriesStatus` (`string?`), `Isbn`/`Upc` (`string?`),
`Universes` (`IReadOnlyList<(int? ExternalId, string Name)>`, new — currently unread by either client).
**Depends on:** none (breaking-change step, do first in Phase 2)
**Verify:** will not compile until Steps 13-14 catch up every call site — expected, fix together.

### Step 13: `MetronClient` — stop discarding ids, add new fields
**Files:** edit `src/Paperbunkr.Data/ComicVine/MetronClient.cs`
**What:** `Names()` helper (line 269-272) becomes a tuple-returning version that also reads `i["id"]`.
Issue detail parsing (lines 214-266) reads `credits[].creator` alongside a creator id if the Metron
schema exposes one on the credit object (confirmed via mokkari research: `credits[{id, creator, role}]`
— `id` here is the *credit row's* id, not the creator's; Metron's `credits[].creator` is a bare string,
not `{id, name}`, so **no creator external id is available from this endpoint** — flag this explicitly
rather than inventing one; creator ids would need a separate `/creator/` search-by-name call, out of
scope for this step). `role[].id` **is** available and real — capture it as `RoleExternalId`. Parse
`arcs`/`characters`/`teams`/`universes` as `(id, name)` tuples. Add `rating`, `isbn`, `upc` (issue-level),
and thread `genres`/`status` from the nested `series` object already present in the issue-detail
response (per mokkari's `Issue.series` sub-schema) — confirm this nested object actually includes
`genres`/`status` during implementation (the design doc's Section 1 facts didn't explicitly confirm the
nested series object's depth on the *issue* endpoint, only the standalone series endpoints).
**Depends on:** Step 12
**Verify:** extend `MetronClientTests.cs` fixtures to include `id` on every arc/character/team object
and assert it round-trips; add a fixture with `universes` populated (currently absent from tests).

### Step 14: `ComicVineClient` — same treatment
**Files:** edit `src/Paperbunkr.Data/ComicVine/ComicVineClient.cs`
**What:** `NamesOf` (lines 278-281) becomes tuple-returning, reading `["id"]` for `story_arc_credits`/
`character_credits`/`team_credits`/`location_credits`. `person_credits` (lines 236-258) starts reading
`person["id"]` (confirmed available on ComicVine's real API shape per the survey, currently unread).
Extend `IssueFields`/`IssueDetailFields`/`VolumeFields` (lines 47-48, 215) to request `genre`,
`age_rating` — **unconfirmed whether ComicVine's API actually returns these**, since Paperbunkr's
client has never requested them; if the live API rejects or ignores the extra field names, leave the
corresponding `ComicVineIssueDetails` properties `null` for this provider without failing the whole
parse. `Universes` stays empty for ComicVine (no such concept there).
**Depends on:** Step 12
**Verify:** extend existing ComicVine client tests the same way as Step 13; a live-field-list smoke
test (or manual check against a real ComicVine response) is the only way to actually confirm
genre/age_rating exist — call this out as a manual-verification item, not something the automated
suite alone can prove.

### Step 15: `IssueDetailsApplier` — attach external ids, add scalar fields
**Files:** edit `src/Paperbunkr.Data/ComicVine/Scraping/IssueDetailsApplier.cs`, `ScrapeField.cs`
**What:** Add `ScrapeField` members: `AgeRating`, `Isbn`, `Upc` (Genre/Status excluded here — they're
series-level, see Step 16). After `Text(...)`-writing `Issue.AgeRating`/new `Issue.Isbn`/`Issue.Upc`
columns (new migration needed — fold into Step 7's migration if this step lands before Step 7 is
finalized, otherwise a follow-up migration), call the Phase 1 resolvers' `GetOrCreate` for each
`(externalId, name)` pair already synced by Step 11's `SyncFromIssue` calls, then write/update a
`ComicMetadataExternalId` row per resolved entity (`EntityKind` inferred from which resolver, `Provider`
from the client that produced this `ComicVineIssueDetails`). Route creator/role external ids the same
way through `CreatorResolver`.
**Depends on:** Steps 11, 13-14
**Verify:** extend `IssueDetailsApplier` tests (or the scrape-service integration tests from Step 11)
to assert a `ComicMetadataExternalId` row exists per entity after a scrape with populated ids.

### Step 16: `Issue.Upc` column + Genre-via-IssueTag + Series-level Status apply path
**Correction found while implementing Phase 1** (reading `Issue.cs`/`Series.cs` directly): `Issue.ISBN`
already exists (`Issue.cs:243`) — no new ISBN column needed, just populate it. `Series.Genre`'s own doc
comment (`Series.cs:72`) says "not the current source of truth — see `Issue.Genre`", but `Issue.Genre`
was already removed in favor of `Issue.Tags`/`IssueTag` (weighted, categorized tags with
`Field ∈ {Genre, Tags}`, per `Issue.cs:130-135`) — that comment is stale. **Genre should populate
`IssueTag` rows (`Field = Genre`), not the dead `Series.Genre` string.** This is actually simpler than
originally planned: since `IssueTag` is per-issue, it fits `IssueDetailsApplier`'s existing per-issue
write pattern directly — no separate series-level apply path needed for Genre at all. `Series.Status`
is a real, current field (`Series.cs:48`, "no CE precedent... real source of truth") with no per-issue
equivalent, so it's the only field that still needs a series-level apply path.
**Files:** edit `src/Paperbunkr.Data/Entities/Issue.cs` (add `Upc`, `string?`, only — not `Isbn`); edit
`src/Paperbunkr.Data/ComicVine/Scraping/IssueDetailsApplier.cs` to write `IssueTag(Field: Genre)` rows
per parsed genre name (dedup against existing tags the same way `Tags` are already written elsewhere —
find the existing `IssueTag`-writing precedent, e.g. wherever the weighted-tags design's own scrape
path already works, and mirror it rather than reinventing tag-writing); locate and edit the series-level
scrape-apply path (per the survey, `IssueDetailsApplier` only ever writes `Issue` — find where
volume-level scrape results currently get applied, likely in `ScrapeOrchestrator.cs` near its
volume/series handling) to add `Series.Status` there.
**Depends on:** Step 12
**Verify:** extend `IssueDetailsApplier` tests to assert `IssueTag(Field: Genre)` rows are written;
extend series-scrape tests to assert `Series.Status` populates.

### Step 17: `RoleMap` gap fix
**Files:** edit `src/Paperbunkr.Data/ComicVine/MetronClient.cs` (`RoleMap`, line 45-54)
**What:** Add `["translator"] = "Translator"` at minimum (confirmed concrete gap — Paperbunkr already
has a `Translator` field per `Paperbunkr.Sharing/Protocol/Dtos.cs`, and the existing test fixture at
`MetronClientTests.cs:112` already models Metron sending this role). Audit for other plausible gaps
against Paperbunkr's own full credit-role vocabulary (grep the codebase for every role name used
elsewhere, e.g. in the Issue Properties editor's role dropdown) and add any Metron is likely to send
that aren't covered — full confirmation against Metron's live role list isn't possible from the repo
alone (not found during research), so treat this as best-effort based on Paperbunkr's own known roles.
**Depends on:** none
**Verify:** update the existing `MetronClientTests.cs` assertion for the `Translator` fixture (line 112) —
check first whether it currently asserts the credit is dropped (`field = null`) and flip that assertion
to expect `field = "Translator"`.

### Step 18: `ContinuityMetronMatchResolver`
**Files:** `src/Paperbunkr.Data/Metadata/ContinuityMetronMatchResolver.cs` (new); edit
`src/Paperbunkr.Data/Entities/Continuity.cs` (add `MetronId` (`string?`) alongside `WikidataId`/`FandomKey`)
**What:** `internal static class`, same visibility/namespace as `ContinuityWikidataMatchResolver` (needed
to call `ContinuityResolver.GetOrCreate`/`AddSeriesToContinuity` directly, both `internal`). Unlike the
Wikidata resolver (a scheduled task making external network calls), this runs **inline during scrape
apply** — Step 15's call site, right after character/team resolution — since `Universes` data is already
in-memory from `ComicVineIssueDetails`, no extra API call needed. For each `(externalId, name)` in
`Universes`, `ContinuityResolver.GetOrCreate(context, name)`, set/overwrite `MetronId` if unset, then
`AddSeriesToContinuity`. Per the design's precedence rule, this can overwrite a continuity assignment
the Wikidata resolver made, but never the reverse (Wikidata resolver already only creates
`ContinuitySuggestion` records requiring user acceptance, per its own doc comment — no live conflict to
adjudicate at write time).
**Depends on:** Steps 12-13, 15 (needs `Universes` data flowing through the pipeline first)
**Verify:** new `ContinuityMetronMatchResolverTests.cs`, mirroring the fixture shape used for
`ContinuityResolver`/`CharacterResolver` tests; one test asserting Metron wins when both a Wikidata
suggestion and a Metron universe tag exist for the same series (though the Wikidata side only writes on
explicit user acceptance, so this may reduce to "Metron sync doesn't get blocked by a pending
suggestion" rather than a true conflict case — confirm exact interaction during implementation).

*(A migration folding in `Issue.Isbn`/`Issue.Upc`/`Continuity.MetronId` follows the Step 7 pattern —
add as `AddMetronScrapeFields` once Steps 13-18 land.)*

---

## Phase 3 — Cross-provider merge

### Step 19: Merge precedence engine
**Files:** `src/Paperbunkr.Data/Metadata/ComicProviderMerge.cs` (new)
**What:** `MergeSeries(PaperbunkrDbContext context, int seriesId, ComicProvider preferredProvider = ComicProvider.Metron)`
and `MergeIssue(...)` implementing the fixed precedence table from the design doc (Metron wins for
credits/characters/teams/arcs/universes; ComicVine wins for cover image and Metron-absent fields;
otherwise keep whichever provider originally scraped it — track "which provider scraped this field
last" via `Issue.MetadataSource`/an equivalent per-series marker, confirm exact field to key off during
implementation). Idempotent — running twice must not duplicate `CreatorCredit`/`TeamAppearance` rows
(the unique indexes from Step 6 make re-running the underlying resolvers naturally idempotent; the
merge engine itself must not, e.g., double-apply `ComicMetadataExternalId` writes).
**Depends on:** Steps 1-18 (needs the full foundation + provider scraping in place to have anything to merge)
**Verify:** new `ComicProviderMergeTests.cs` — one test per precedence rule, one idempotency test
(run merge twice, assert row counts unchanged the second time).

### Step 20: Trigger — the "adopt" action
**Files:** locate the series-matching/`cv_id`-linking UI flow (not yet located — search for where a
series' `ComicProvider`/external id is set today, likely `Preferences/ConnectionsSection` or the
per-series provider-switch UI mentioned in memory as "per-series ComicProvider") and add an explicit
"Adopt data from [other provider]" action once a `cv_id` link is confirmed, calling `ComicProviderMerge`.
**Depends on:** Step 19
**Verify:** on-screen check — this is a user-triggered action, exercise it manually against a real or
recorded series pair.

---

## Phase 4 — UI: five new detail screens

Per survey correction #3, there's no small precedent to mirror structurally — only conventions. Each
screen follows the same shape:

### Step 21: `CharacterDetailScreenViewModel` + `CharacterDetailScreenView`
**Files:** `src/Paperbunkr.App/ViewModels/CharacterDetailScreenViewModel.cs` (new),
`src/Paperbunkr.App/Views/CharacterDetailScreenView.axaml` + `.axaml.cs` (new — **both together**, per
this project's own build gotcha: a new `.axaml` without its matching code-behind in the same step can
produce a silent `AVLN2000` false-negative build)
**What:** Bio/image (from `Creator`/`Publisher`-style Metron data where available — Character has no
bio/image itself per Metron's schema, only via linked `Creator`s/`Teams`), an "appears in" issue list
sourced from `CharacterAppearance`. **Before writing XAML**, load the relevant `avalonia`/
`avalonia-pro-max` subskill per this project's mandatory UI-work rule (read
`~/.claude/skills/avalonia/SKILL.md` first, then the specific subskill's `SKILL.md` directly off disk —
subskills aren't separately invocable, per this project's own documented gotcha).
**Depends on:** Step 1 (data), Step 11 (populated data to display)
**Verify:** on-screen check via the `run` skill; run `avalonia-pro-max/review-checklist` before calling
this screen done.

### Steps 22-25: `CreatorDetailScreen`, `TeamDetailScreen`, `PublisherDetailScreen`, `LocationDetailScreen`
**Files:** same pairing pattern as Step 21, one per entity kind.
**What:** `Creator` and `Publisher` screens show real Metron bio/image/desc data (per the schema
research); `Team` and `Location` screens are lighter (Team has bio/image too per Metron's schema;
Location has none — Metron doesn't expose location data at all, so this screen only ever shows
ComicVine-sourced appearances, no bio/image section).
**Depends on:** Steps 2-4 (data)

### Step 26: `MainViewModel` wiring
**Files:** edit `src/Paperbunkr.App/ViewModels/MainViewModel.cs`
**What:** Per the survey's Section 7 findings — for each of the five new screens: a new VM property
(mirroring line 225's `Detail = new DetailScreenViewModel(...)` pattern), a new `CurrentScreen` string
case added to the `ActiveDrillDownContent`/`ActiveScreenContent` switch (line 804 area), a
`Go<X>ForId(int id)` method mirroring `GoDetailForSeries`'s `RunDrill(...)` shape (lines 1831-1849), and
a new `NavigationEntryKind` enum member + `Build<X>Entry` per screen for back/forward history.
**Depends on:** Steps 21-25

### Step 27: Entry points
**Files:** edit `DetailScreenView.axaml`/`IssuePropertiesScreenView.axaml` (wherever character/team/
creator/publisher/location names are currently displayed as plain text, per the design's Phase 4 goal
of tappable chips) — exact locations not surveyed in this pass, find during implementation.
**What:** Turn existing plain-text character/team/creator/publisher/location displays into tappable
chips/links that call the new `Go<X>ForId` methods.
**Depends on:** Step 26

---

## Phase 5 — Independent items (no dependency on Phases 1-4; can be done any time, including in parallel)

### Step 28: Variant covers
**Files:** edit `ComicVineIssueDetails.cs` (add `Variants: IReadOnlyList<(int? Id, string? ImageUrl)>`),
`MetronClient.cs` (parse `variants`), the cover-picker UI (find the exact component — referenced in
memory as adjacent to `ArcCoverImageCache` but a separate system, locate during implementation).
**Depends on:** none (independent of Phase 1-4, though it does touch the same `ComicVineIssueDetails`
record Step 12 also touches — coordinate to avoid a merge conflict if done concurrently with Phase 2).

### Step 29: `foc_date`
**Files:** new migration adding `PullListRelease.FocDate` (`DateTime?`); edit
`src/Paperbunkr.Data/ComicVine/MetronClient.cs`'s `GetReleasesAsync` (lines 143-173, parse `foc_date`);
locate the exact `new PullListRelease(...)` construction site (not found in this survey — search for it);
edit `src/Paperbunkr.App/ViewModels/WantedScreenViewModel.Releases.cs` (`ReleaseRowViewModel`, `LoadReleases`
around lines 155-273); edit `src/Paperbunkr.App/Views/WantedTabs/ReleasesView.axaml` (new chip/line near
lines 163-171, per the survey — no existing slot to reuse).
**Depends on:** none

### Step 30: `associated` series
**Files:** new entity `SeriesAssociation.cs` (id + SeriesId + AssociatedSeriesId-or-provider-name pair,
per the design's "no requirement the related series exists locally" call); migration; `MetronClient.cs`
parsing; a "related series" list on the Series detail screen.
**Depends on:** none

### Step 31: Community ratings
**Files:** edit `ComicVineIssueDetails.cs` (`AverageRating: double?`, `RatingCount: int?`), `MetronClient.cs`
parsing, `Issue.cs` (new columns + migration), Issue/Series detail screen display.
**Depends on:** none

---

## Suggested execution order for this session

Phase 1 (Steps 1-11) is self-contained, testable, and has no UI dependency — start there. Phase 2
(Steps 12-18) is the next natural continuation since Phase 1's resolvers need real external ids to be
worth having. Phases 3-5 are separable follow-ups; Phase 4 specifically should wait until the
`avalonia`/`avalonia-pro-max` skill can be loaded fresh for that work rather than rushed at the tail of
a long schema-focused session.
