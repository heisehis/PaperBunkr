# Story Event resolver — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-27-story-event-resolver-design.md*

Pre-checks done while planning (2026-09-27):
- **Metron's `ArcSerializer` includes `cv_id`.** Fields: `id, name, desc, image, cv_id, gcd_id, resource_url, modified`, per
  `api/v1_0/serializers/arc.py` in Metron-Project/metron. `MetronSource.GetArcOverviewAsync` already fetches `/arc/{id}/`.
- **A new interval scheduled task runs on the next scheduler check.** `SchedulerDueLogic`: `LastRunUtc` null → `Run`. So
  "once soon after upgrade" needs no extra code.
- **`ArcIssue` carries no provider issue ids**, so issue-list overlap uses series + number, as verification does. This is
  the spec's documented fallback.

## Step 1: Schema
**Files:**
- `Entities/StoryEvent.cs` (edit)
- `Entities/StoryEventOrigin.cs`, `Entities/StoryEventAlias.cs`, `Entities/StoryEventDuplicateDismissal.cs` (new)
- `PaperbunkrDbContext.cs` (DbSets + config)
- migration `AddStoryEventIdentity` (ef, then add the `Origin` backfill SQL)
- `Paperbunkr.Data.Tests/AddStoryEventIdentityMigrationTests.cs`

**Verify:** the `Up` holds only these changes; forward-only test (backfill; the new tables accept rows).

## Step 2: Local matching and merging (`Paperbunkr.Data/Metadata/`)
**Files:**
- `EventNameKeys.cs`, `StoryEventMerger.cs`, `StoryEventIdentityResolver.cs` (new)
- tests: `EventNameKeysTests`, `StoryEventMergerTests`, `StoryEventIdentityResolverTests`

**Depends on:** Step 1.

## Step 3: Id completion
**Files:**
- `ReadingLists/Sources/ArcModels.cs`: `ArcOverviewInfo` gains an optional `ComicVineId`.
- `MetronSource.cs`: `GetArcOverviewAsync` reads `cv_id`.
- `Metadata/ArcIdentitySource.cs` (new): the `IArcIdentitySource` seam and its provider implementation over `IComicProvider`
  + `IReadingListSource`.
- `Metadata/StoryEventIdCompletion.cs` (new); tests with fake sources.

**Depends on:** Steps 1–2.

## Step 4: Sweep and prevention
**Files:**
- `Metadata/StoryEventIdentitySweep.cs` (new): due events → completion → pairs → silent merges → summary.
- `StoryEventResolver.cs`: match keys and aliases; a new event is `Provider`.
- `StoryArcGroupingResolver.cs`: the skip set uses name keys and aliases.
- `ArcExternalVerificationService.cs`: a result also matches through keys and aliases.
- Tests for each.

**Depends on:** Steps 2–3.

## Step 5: App
**Files:**
- `ScheduledTaskCatalog.cs`: task `story-event-identity`, weekly, default on.
- `EventsScreenViewModel.Identity.cs` (new partial) and `StoryEventDuplicateRowViewModel.cs` (new): the "Possible duplicates"
  list; Merge / Not the same / Check again, deferred; Find duplicate events; a background check after accept; switch to the
  survivor.
- `StoryEventSuggestions.cs` and `IssuePropertiesScreenViewModel.cs`: trigger the background check.
- `NewEventOrContinuityViewModel.cs`: a rename sets `Origin = User`.
- `MainWindow.axaml`: the sidebar section.
- App tests.

**Depends on:** Step 4.

## Step 6: Docs and verification
`docs/paperbunkr-todo.md`, the wiki (Story Events), design implementation notes. Full Data suite and targeted App tests.
Not automated: live provider calls and the sidebar on screen.
