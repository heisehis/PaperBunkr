# Story Event & Continuity Auto-Population — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-17-storyevent-continuity-autopopulate-design.md*

**Status:** Steps 1-13 implemented 2026-09-17. `Paperbunkr.Data.Tests`/`Paperbunkr.App.Tests` green
for every new/edited file (Data.Tests: 41/41 new tests; App.Tests: 187/191 in the touched classes,
the 4 failures confirmed pre-existing via `git stash` against pristine HEAD — unrelated
`Dispatcher.UIThread.Post` timing flake in `EventsScreenViewModelTests`/`DetailTabsViewModelTests`
delete-flow tests, not caused by this work). Step 14's on-screen verification still outstanding.

## Step 1: Phase 1 schema — entities + migration
**Files:**
- `src/Paperbunkr.Data/Entities/StoryEvent.cs` (edit — add `string? ComicVineArcId`, `string? MetronArcId`)
- `src/Paperbunkr.Data/Entities/StoryEventCandidateDismissal.cs` (new — `Id, ArcName, Publisher, DismissedAt`)
- `src/Paperbunkr.Data/Entities/StoryEventVerificationNegativeCache.cs` (new — `Id, ArcName, Publisher, Source (enum ComicVine|Metron), CheckedAt`)
- `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit — add `DbSet<StoryEventCandidateDismissal>`, `DbSet<StoryEventVerificationNegativeCache>`; `OnModelCreating` config mirroring `EventSuggestionDismissal`'s shape at lines 735-744 — composite unique index on `(ArcName, Publisher)` for the dismissal table, `(ArcName, Publisher, Source)` for the negative-cache table)
**What:** New nullable columns on `StoryEvent`, two new tables. No FK to `StoryEvent`/`Issue` on either new table (dismissal/cache key on the pre-creation `(ArcName, Publisher)` string pair, not an entity id — matches design doc's rationale).
**Depends on:** none
**Verify:** `dotnet ef migrations add AddStoryEventAutoPopulationSchema --project src/Paperbunkr.Data`; new `src/Paperbunkr.Data.Tests/Migrations/AddStoryEventAutoPopulationSchemaMigrationTests.cs` mirroring `AddActivityRunsMigrationTests.cs`'s real-SQLite pattern (`Database.Migrate()` from fresh db, insert+round-trip both new tables, assert unique index via `pragma_index_list`, reversibility check).

## Step 2: Phase 2 schema — entities + migration
**Files:**
- `src/Paperbunkr.Data/Entities/Continuity.cs` (edit — add `string? WikidataId`)
- `src/Paperbunkr.Data/Entities/ContinuitySuggestionDismissal.cs` (new — `Id, SeriesId, Series?, WikidataQid, DismissedAt`, FK `SeriesId` Cascade)
- `src/Paperbunkr.Data/Entities/ContinuityCharacterLookupNegativeCache.cs` (new — `Id, CharacterId, Character?, CheckedAt`, FK `CharacterId` Cascade)
- `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit — add both `DbSet`s + `OnModelCreating` config: unique index `(SeriesId, WikidataQid)` on the dismissal table, unique index on `CharacterId` for the cache table)
**What:** Mirrors Step 1's shape for the Continuity side.
**Depends on:** none (independent of Step 1)
**Verify:** `dotnet ef migrations add AddContinuityWikidataSchema --project src/Paperbunkr.Data`; new `AddContinuityWikidataSchemaMigrationTests.cs`, same pattern as Step 1.

## Step 3: `StoryEventResolver.GetOrCreate`
**Files:** `src/Paperbunkr.Data/Metadata/StoryEventResolver.cs` (new)
**What:** `internal static class StoryEventResolver { static StoryEvent GetOrCreate(PaperbunkrDbContext context, string name) }` — case-insensitive trim+match before insert, mirroring `ContinuityResolver.GetOrCreate` (`ContinuityResolver.cs:128-142`) exactly, including `CreatedAt`/`UpdatedAt` stamping. This is the dedup path confirmed missing from `EventMembershipResolver`.
**Depends on:** none
**Verify:** `src/Paperbunkr.Data.Tests/StoryEventResolverTests.cs` (new) — duplicate-name-different-case returns existing row, no duplicate insert.

## Step 4: `StoryArcGroupingResolver` (local grouping)
**Files:** `src/Paperbunkr.Data/Metadata/StoryArcGroupingResolver.cs` (new)
**What:**
- `internal static class PublisherNormalizer { static string Normalize(string? publisher) }` (private nested or same-file static class) — small hardcoded alias dictionary (`"Marvel Comics"`, `"Marvel Worldwide"` → `"Marvel"`; `"DC Comics"` → `"DC"`; case-insensitive trim fallback for anything else), scoped to this resolver only per design doc.
- `record StoryEventCandidate(string ArcName, string Publisher, IReadOnlyList<(Issue Issue, int? Position)> Members, FormatSignalStrength Strength, string Reason)`.
- `GetCandidates(PaperbunkrDbContext context)`: scan `Issue` where `StoryArc` non-empty; split `StoryArc` and `StoryArcNumber` both on `,`, trim, pair positionally by index (ragged lists → missing `Position` for extra tokens); group by `(NormalizedArcName, PublisherNormalizer.Normalize(Publisher))`; drop groups <2 issues; exclude issues already in a matching-name `StoryEvent`'s `EventMembership`; exclude groups present in `StoryEventCandidateDismissal`. All candidates start `Strength = Weak`.
- `Dismiss(context, arcName, publisher)` / `Restore(...)` / `GetDismissed(...)`, mirroring `EventSuggestionResolver`'s dismissal round-trip shape.
**Depends on:** Step 1 (dismissal table)
**Verify:** `src/Paperbunkr.Data.Tests/StoryArcGroupingResolverTests.cs` (new) — comma-splitting incl. ragged `StoryArc`/`StoryArcNumber` lengths, publisher-alias collapsing, `(ArcName, Publisher)` cross-publisher-collision case, ≥2-issue threshold, dismissal filtering, exclusion of issues already event-linked.

## Step 5: ComicVine/Metron verification pass
**Files:** `src/Paperbunkr.Data/Metadata/ArcExternalVerificationService.cs` (new)
**What:** `VerifyAsync(PaperbunkrDbContext context, IReadOnlyList<StoryEventCandidate> candidates, CancellationToken)`:
- Select at most 15 candidates per call, largest-`Members`-count first.
- Per candidate: check `StoryEventVerificationNegativeCache` for a non-expired (< 30 days old) row per source first; skip that source if present.
- Resolve `ComicVineSource`/`MetronSource` via existing `ReadingListSourceRegistry.Get(context, sourceKey)` (returns `null` if credentials absent — skip source silently, per design doc Q10).
- Call `SearchAsync(candidate.ArcName)`; treat a result a match when its name normalizes (case-insensitive, trimmed) equal to `candidate.ArcName`. On match, call `GetArcIssuesInOrderAsync`; if ≥half of local `Members` (by issue-number match) appear in the external ordered list, upgrade `Strength` to `Strong` and fill `Position` from external order (unmatched issues appended by publication date). Set `ComicVineArcId`/`MetronArcId` on eventual `StoryEvent` creation (returned alongside the upgraded candidate, not written yet — see Step 8).
- On `ReadingListSourceException`/timeout from ComicVine, retry the same candidate against Metron (if configured) before giving up.
- No match from a source → insert a `StoryEventVerificationNegativeCache` row for `(ArcName, Publisher, Source)`.
**Depends on:** Step 1, Step 4
**Verify:** extend `StoryArcGroupingResolverTests.cs` or a new `ArcExternalVerificationServiceTests.cs` — mock `IReadingListSource` (both sources already testable via the existing `IReadingListSource` interface, same mocking approach as `ComicVineSource`'s own tests if any exist, else hand-rolled fakes); cover CV-fails-falls-back-to-Metron, negative-cache write + 30-day expiry (29 vs 31 days), 15-candidate cap prioritization.

## Step 6: Phase 1 scheduled task
**Files:** `src/Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs` (edit)
**What:** New `ScheduledTaskDescriptor` entry (id `"story-event-autodetect"`), `ActivityJobKind.SyncMetadata`, `SchedulerResourceClass.Network`, `DefaultInterval = TimeSpan.FromDays(7)`, `DefaultEnabled = true`, `ScheduleMode.Interval`. `RunAsync` calls `StoryArcGroupingResolver.GetCandidates` (incremental: only issues not already covered by an unexpired dismissal/existing event since `ScheduledTaskState.LastRunUtc`, read via the handle's context), then `ArcExternalVerificationService.VerifyAsync`, then raises an `ActivityAlert` via `IActivityJobHandle`/`IActivityService.RaiseAlert` summarizing "N new story-event suggestions found" with a deep-link into the Story Events screen. `Description` field text notes ComicVine/Metron are optional and configured in Preferences (satisfies design doc's "Automation Prefs subtext" requirement without a separate UI edit — `AutomationSection.axaml`'s `ItemsControl` already renders new catalog entries automatically, confirmed, no XAML change needed).
**Depends on:** Step 4, Step 5
**Verify:** existing scheduler test coverage extended (locate current `ScheduledTaskCatalog`/`SchedulerService` tests at implementation time; add a new test file if none covers catalog entries) — assert task registered, enable/disable via `ScheduledTaskState`, "Run Now" invokes the same resolver path as automatic run.

## Step 7: `Character` ranking query
**Files:** `src/Paperbunkr.Data/Metadata/CharacterResolver.cs` (edit)
**What:** New method `GetTopCharactersForSeries(PaperbunkrDbContext context, int seriesId, int take = 5)` — joins `Character`→`CharacterAppearance`→`Issue` filtered to the series, groups by `Character`, orders by appearance count descending, returns top `take`. No such ranking exists today (confirmed).
**Depends on:** none
**Verify:** `src/Paperbunkr.Data.Tests/CharacterResolverTests.cs` (edit or new) — ranking order correct, `take` respected, ties broken deterministically (by `Character.Id`).

## Step 8: `WikidataClient`
**Files:** `src/Paperbunkr.Data/Metadata/WikidataClient.cs` (new)
**What:** Thin HTTP wrapper mirroring `AniListMetadataProvider`'s `SendAsync`/error-handling shape (catch `HttpRequestException`/non-cancellation `TaskCanceledException`, return `null`, no proactive online-check per design doc). Two calls: `SearchEntitiesAsync(string name)` → `wbsearchentities` (`https://www.wikidata.org/w/api.php?action=wbsearchentities&search={name}&language=en&type=item&limit=10`), `GetEntityAsync(string qid)` → fetch item, expose `P31` (instance-of QIDs), `P1080`, `P1434` values. Sets a contactable `User-Agent` header per Wikidata etiquette. Serial requests only (no `Task.WhenAll` fan-out), honors `Retry-After` on 429 with exponential backoff.
**Depends on:** none
**Verify:** `src/Paperbunkr.Data.Tests/WikidataClientTests.cs` (new) — mocked `HttpMessageHandler`, same pattern as any existing `AniListMetadataProvider` HTTP tests; 429+`Retry-After` backoff behavior, malformed-response → `null` not throw.

## Step 9: `ContinuityWikidataMatchResolver`
**Files:** `src/Paperbunkr.Data/Metadata/ContinuityWikidataMatchResolver.cs` (new)
**What:** `record ContinuitySuggestion(Series Series, string WikidataQid, string UniverseLabel, FormatSignalStrength Strength, string Reason)`. `GetSuggestionsAsync(context, wikidataClient, CancellationToken)`:
- Per `Series` not already in a `Continuity` and not in `ContinuitySuggestionDismissal`: `CharacterResolver.GetTopCharactersForSeries(context, series.Id, 5)`.
- Per character (skip if a non-expired `ContinuityCharacterLookupNegativeCache` row exists): `wikidataClient.SearchEntitiesAsync(character.Name)`, filter results to those whose `P31` includes `Q1114461` ("comics character" — verified QID, see design doc), take first match, `GetEntityAsync`, read `P1080` falling back to `P1434`. No `P31`-matching result → insert negative-cache row, skip (not a zero-confidence vote).
- Plurality vote across resolved QIDs: ≥2 agreeing → `Strong`; exactly 1 → `Weak`; 0 → no suggestion.
- `Dismiss`/`Restore`/`GetDismissed` mirroring `EventSuggestionResolver`'s shape, keyed `(SeriesId, WikidataQid)`.
**Depends on:** Step 2, Step 7, Step 8
**Verify:** `src/Paperbunkr.Data.Tests/ContinuityWikidataMatchResolverTests.cs` (new) — `P31` filter excludes a same-named non-comics mock result, vote aggregation (0/1/2+ → none/Weak/Strong), top-5 selection uses Step 7's method, dismissal/negative-cache filtering.

## Step 10: Phase 2 scheduled task
**Files:** `src/Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs` (edit, same file as Step 6)
**What:** New `ScheduledTaskDescriptor` entry (id `"continuity-wikidata-autodetect"`), `ActivityJobKind.SyncMetadata`, `SchedulerResourceClass.Network`, weekly interval, independently enabled/disabled from Step 6's task per design doc. `RunAsync` calls `ContinuityWikidataMatchResolver.GetSuggestionsAsync`, raises Activity Center alert deep-linking into the Story Events screen's Continuities mode.
**Depends on:** Step 9
**Verify:** same approach as Step 6.

## Step 11: Story Events screen — Phase 1 suggestion UI
**Files:**
- `src/Paperbunkr.App/ViewModels/EventsScreenViewModel.StoryEventSuggestions.cs` (new partial)
- `src/Paperbunkr.App/Views/EventsScreen.axaml` (edit — add a suggestions list section, following the existing suggestion-card pattern already used for `EventSuggestionResolver`'s results on this same screen)
**What:** `ObservableCollection<StoryEventCandidate>` populated from `StoryArcGroupingResolver.GetCandidates` (+ verification) on load; `AcceptSuggestionCommand` → `StoryEventResolver.GetOrCreate` + `EventMembershipResolver.AddMember` per issue (with `Position`/`Role` from the candidate) + sets `ComicVineArcId`/`MetronArcId` if present; `DismissSuggestionCommand` → `StoryArcGroupingResolver.Dismiss`. Confidence (`Weak`/`Strong`) and `Reason` shown per card, "Source: ComicVine"/"Source: Metron" attribution shown when verified.
**Depends on:** Step 4, Step 5, Step 3
**Verify:** `src/Paperbunkr.App.Tests/EventsScreenViewModelTests.cs` (edit) — accept creates real `StoryEvent`+`EventMembership` rows, dismiss persists and filters future loads.

## Step 12: Continuities mode — Phase 2 suggestion UI
**Files:**
- `src/Paperbunkr.App/ViewModels/EventsScreenViewModel.Continuities.cs` (edit)
- `src/Paperbunkr.App/Views/EventsScreen.axaml` (edit, Continuities-mode section)
**What:** Same shape as Step 11 for `ContinuitySuggestion`: `AcceptSuggestionCommand` → `ContinuityResolver.GetOrCreate(UniverseLabel)` + `AddSeriesToContinuity` + set `WikidataId`; `DismissSuggestionCommand` → `ContinuityWikidataMatchResolver.Dismiss`.
**Depends on:** Step 9
**Verify:** extend existing `EventsScreenViewModelTests.cs` Continuities-mode coverage.

## Step 13: Manual per-item triggers
**Files:**
- `src/Paperbunkr.App/ViewModels/IssuePropertiesScreenViewModel.cs` (edit — new `[RelayCommand] LookUpStoryArc()` following the existing command pattern at lines 123-129/395-438)
- `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit — new `[RelayCommand] LookUpContinuity()` near `ContinuityChips`/`RemoveContinuity` at line 742)
- `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit — button near the `ContinuityChips` `ItemsControl` at lines 398-412)
**What:** Single-issue and single-series on-demand triggers, calling the same resolver methods as Steps 4/5/9 for just the one issue/series, surfacing the result via the existing `notify` callback (toast) or navigating to the Story Events screen if a suggestion is produced.
**Depends on:** Step 4, Step 5, Step 9
**Verify:** extend `IssuePropertiesScreenViewModelTests.cs` and `DetailTabsViewModel`'s test file (locate exact name at implementation time) with a one-issue/one-series case each.

## Step 14: Full-suite verification
**Files:** none (verification only)
**What:** Run `Paperbunkr.Data.Tests`, `Paperbunkr.App.Tests` in full; confirm no `AvaloniaTestCollection`/`DatabasePathOverride` isolation gaps on the two new test classes touching `DatabasePathOverride` (per this project's established gotcha — join the collection regardless of whether the class touches Avalonia).
**Depends on:** all prior steps
**Verify:** `dotnet test` full solution; on-screen check of both new suggestion UIs (Story Events screen suggestions list, Continuities mode suggestions, Issue Properties "Look up story arc" button, Detail-tab "Look up continuity" button) — this project's own convention requires on-screen verification for new UI before calling it done, not just green tests.
