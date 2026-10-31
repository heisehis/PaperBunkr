# Series relation connector — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-29-series-relation-connector-design.md*

Working tree note (2026-10-29): `master` has a large uncommitted change set from other work, including 12 new migrations up to
`20261027211329_AddBookLastExcerpt` and edits to `DetailTabs.axaml`, `DetailTabsViewModel.cs`, `ScheduledTaskCatalog.cs`,
`AppSettings.cs`, `LibraryEvents.cs` and `PaperbunkrDbContext.cs`. No source file had been touched in the 3 hours before planning.
Build on top of it, and keep every edit to those shared files additive.

Tests: Data tests run freely (`dotnet test src/Paperbunkr.Data.Tests --filter ...`). App tests run by class only (CLAUDE.md; no wide
runs). Never two `dotnet` builds or test runs at once. Kill build servers after each run (`dotnet build-server shutdown`).

---

## Step 1: Relation types, settings and migration
**Files:**
- `src/Paperbunkr.Data/Entities/RelationType.cs` (edit): append `TieIn`, `MainEventSeries`.
- `src/Paperbunkr.Data/Metadata/RelationTypeCatalog.cs` (edit): a directional pair, each the other's inverse.
- `src/Paperbunkr.App/Models/RelationTypeOption.cs` (edit): labels "Tie-in" / "Main event". `FormatLabel` would give "Tie In" /
  "Main Event Series", so add an explicit override map for these two.
- `src/Paperbunkr.App/Services/EventMap/ContinuityMapLoader.cs` (edit): `RelationLabel` cases for both.
- `src/Paperbunkr.Data/Entities/Series.cs` (edit): `DateTime? MetronAssociationsCheckedUtc`.
- `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit): `bool FindRelatedSeriesAutomatically { get; set; } = true;`.
- `src/Paperbunkr.Data/Entities/SeriesRelationDismissal.cs` (edit): doc comment widened to any automatic relation or dismissed suggestion.
- `src/Paperbunkr.Data/Migrations/<timestamp>_AddSeriesRelationConnector.cs` (+ Designer, snapshot): generated with `dotnet ef migrations add`.
  Columns: `Series.MetronAssociationsCheckedUtc` (nullable), `AppSettings.FindRelatedSeriesAutomatically` (default `true`, so
  existing rows get `1`), and on `SeriesAssociation` `ComicVineSeriesId`, `GcdSeriesId` (`int?`) and `IdsCheckedUtc` (`DateTime?`).
  `RelationType` is stored as text, so the enum needs no schema change.
- `src/Paperbunkr.Data/Entities/SeriesAssociation.cs` (edit): the three new properties, with doc comments (design §4).

**Depends on:** none.
**Verify:** build both projects. Check the generated migration has exactly those two `AddColumn`s and a `defaultValue: true` for the
bool. Run the Data migration tests and `RelationTypeCatalog` coverage (every enum member has an entry): `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~Migration|FullyQualifiedName~RelationType"`.
Don't write an up/down/up test that re-adds the column (memory: the "CharacterCount" antipattern).

## Step 2: Move the spine name-match into Data
**Files:**
- `src/Paperbunkr.Data/Metadata/EventSpineResolver.cs` (new): `public static int? FindNameMatch(string eventName, IReadOnlyList<(int SeriesId, string SeriesName)> rowsInReadingOrder)`
  plus the `ForceRelay = 0` constant, moved from `SpineResolver` with `StripYear` and the regex.
- `src/Paperbunkr.App/Services/EventMap/SpineResolver.cs` (edit): `FindNameMatch` delegates to it. `Resolve`'s public shape is unchanged.

**Depends on:** none.
**Verify:** the existing Event Map spine tests pass unchanged (`--filter "FullyQualifiedName~Spine"` in App.Tests). New
`EventSpineResolverTests` in Data.Tests: name match ignoring year, prefers most rows, then first appearance, never "most issues".

## Step 3: Snapshot and the local inferences
**Files (all new, `src/Paperbunkr.Data/Metadata/SeriesConnector/`):**
- `SeriesConnectorSnapshot.cs`: `Load(PaperbunkrDbContext)`, a no-tracking read of series (id, name, `TitleNormalizer` key,
  publisher key = `PublisherEntityId` or normalized `Publisher`, `ContentType`, first/last issue year), event memberships
  (event id, name, `SpineSeriesId`, start date, `(seriesId, seriesName, issueNumber, position)` in position order), user-backed pairs
  (relations with any `User` evidence) and dismissed pairs.
- `InferredSeriesRelation.cs`: `record (int Source, int Target, RelationType Type, decimal Confidence, string Reason, RelationEvidenceProvider Provider, string? ProviderSourceId)` with `Pair` (low, high).
- `VolumeChainInference.cs` (L1, 0.9, "Later volume").
- `CompanionSuffixInference.cs` (L2, 0.85). The suffix table is a static list. Prefix "Giant-Size" is handled separately.
- `SlashTitleInference.cs` (L3, 0.8, "Team-up title").
- `EventTieInInference.cs` (L4 Crossover 0.8 / L5 TieIn 0.8). Uses `EventSpineResolver`. The reason carries the issue range:
  `#33–34`, `#33, #35`, or `#1–6` from the series' member issue numbers in order. `ProviderSourceId` = event id.
- `SeriesConnectorGuards.cs`: a shared filter for same-publisher (per rule), content type, user pairs and dismissed pairs.

Each inference is `static IReadOnlyList<InferredSeriesRelation> Infer(SeriesConnectorSnapshot s)` with no DB access.

**Depends on:** Step 1 (`TieIn`), Step 2.
**Verify:** `src/Paperbunkr.Data.Tests/SeriesConnector/` — one class per inference, building snapshots directly (no DB), covering
every case listed in design §8 for L1–L5 and the guards. `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~SeriesConnector"`.

## Step 4: The sweep (upsert, prune, dismissals)
**Files:**
- `src/Paperbunkr.Data/Metadata/SeriesConnector/SeriesConnectorSweep.cs` (new):
  `RunLocal(Func<PaperbunkrDbContext>)` and `Run(Func<PaperbunkrDbContext>)` (local + mapped Metron associations), returning
  `SeriesConnectorSummary(Created, Updated, Removed)` with `Text` ("14 relations found · 2 removed" / "No new relations").
  Mirrors `EventConnectorSweep.Upsert` and `GcdBondSync.Run`: an existing relation of the same type in either direction gains an evidence
  row. Only evidence it owns is pruned: `Inferred`, and `Metron` with `ProviderRelationType == "associated"`. A relation left with no
  evidence is deleted. `GCD`/`User`/event-order `Metron` evidence is never touched.
- `src/Paperbunkr.Data/Metadata/SeriesConnector/MetronAssociationMapping.cs` (new): maps `SeriesAssociation` rows
  (`Provider == Metron`) to a local series, in order: (1) `ComicMetadataExternalIds` Series/Metron with `ExternalId == ExternalSeriesId`;
  (2) Series/ComicVine with `ExternalId == ComicVineSeriesId`; (3) `Series.GcdSeriesId == GcdSeriesId`. The first step with exactly one
  local series wins. Two or more at a step means no match (no fallthrough). It yields `Related` @ 0.85 candidates, with the matched-by
  step in the reason ("Metron", "Metron · via ComicVine id", "Metron · via GCD id").
- `src/Paperbunkr.Data/Metadata/MediaRelationResolver.cs` (edit): `Remove` writes a `SeriesRelationDismissal` when the relation has any
  automatic evidence (`Gcd`, `Inferred`, `Metron`), not only GCD. Add `public static void DismissSeriesPair(PaperbunkrDbContext, int a, int b)`,
  shared with the suggestion Dismiss (Step 7). Update the doc comment.

**Depends on:** Step 3.
**Verify:** `SeriesConnectorSweepTests` against the Data.Tests SQLite fixture (same base pattern as `EventConnectorTests : IdentityTestDb`).
Cover: idempotent second run; user and dismissed pairs skipped; stale evidence pruned and empty relations deleted; GCD/User/event-order
evidence kept; two events sharing a main series → one relation, two evidence rows; Metron mapping. Extend the existing
`MediaRelationResolver` tests for the widened dismissal.

## Step 5: Metron associated-series fetch
**Files:**
- `src/Paperbunkr.Data/Metadata/SeriesConnector/MetronAssociationFetch.cs` (new):
  `RunAsync(Func<PaperbunkrDbContext>, IMetronSeriesLinks? metron, DateTime nowUtc, CancellationToken)`, in three passes, each
  stopping cleanly on cancellation:
  1. **Resolve Metron ids.** For series with no Metron series id but a ComicVine series id: `FindSeriesByComicVineIdAsync`. Exactly
     one hit → add a `ComicMetadataExternalId` (Series, Metron). Then GCD-only series through `FindSeriesByGcdIdAsync`, only if the
     check below passed. A miss is remembered for 30 days by stamping `MetronAssociationsCheckedUtc`, so it isn't re-asked every week.
  2. **Fetch associated.** For series with a Metron id where `MetronAssociationsCheckedUtc` is null or older than 30 days:
     `GetAssociatedSeriesAsync` → `SeriesAssociationSync.Sync` → stamp. A failed call leaves the stamp unset (retries next run).
  3. **Fill ids for unmatched associations.** For `SeriesAssociation` rows whose Metron id matches no local series and whose
     `IdsCheckedUtc` is null or older than 30 days: `GetSeriesInfoAsync(id)` → store `cv_id`/`gcd_id` on the row and stamp
     `IdsCheckedUtc`. `SeriesAssociationSync.Sync` must keep these columns on rows it leaves in place. Edit it so an unchanged row
     isn't replaced.
  Returns `(Resolved, Checked, IdsFilled, Failed)`.
- `src/Paperbunkr.Data/ComicVine/MetronClient.cs` (edit): a new interface `IMetronSeriesLinks` with `GetAssociatedSeriesAsync`,
  `FindSeriesByComicVineIdAsync`, `GetSeriesInfoAsync` (all existing methods) and `FindSeriesByGcdIdAsync` (new, `series/?gcd_id=`).
  This follows the existing `IMetronEventLists` / `IMetronUniverses` pattern.
  **Before writing `FindSeriesByGcdIdAsync`, confirm Metron's series list accepts `gcd_id`** (Metron API docs / Mokkari's series filter,
  via web lookup). If it doesn't, leave the method out and skip GCD-only series in pass 1. GCD matching still works in pass 3 and in
  mapping, because those read `gcd_id` from the series record itself.
- `src/Paperbunkr.Data/Metadata/SeriesAssociationSync.cs` (edit): update in place, as described in pass 3.

**Depends on:** Step 1 (columns).
**Verify:** `MetronAssociationFetchTests` with a fake `IMetronSeriesLinks`: a ComicVine-only series gets a Metron id from a single hit
and not from two; only due series are asked; stamps are set on success and not on failure; looked-up ids are stored and survive a
re-sync; the GCD pass runs only when supported; cancellation stops between series. No network. Extend `MetronAssociationMapping`
tests (Step 4) with the ComicVine and GCD fallbacks and the ambiguous case.

## Step 6: Triggers, scheduled task, Activity Center
**Files:**
- `src/Paperbunkr.App/Services/SeriesConnectorRunner.cs` (new): `RunFullAsync(IActivityJobHandle?, CancellationToken)`, which reads
  the setting, runs `RunLocal`, then the fetch (Metron via `ComicProviderFactory.Create(context, ComicProvider.Metron, ComicVineRequestPriority.Low) as IMetronSeriesLinks`,
  skipped when not connected), then `Run`. It returns the summary text or "Turned off in Preferences". Also `RunLocalQuietAsync()`
  for the triggers.
- `src/Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs` (edit): add const `FindRelatedSeries = "find-related-series"` and
  a descriptor: "Find related series", weekly, `DefaultEnabled: true`, `SchedulerResourceClass.Network`, `ActivityJobKind.SyncMetadata`,
  `Priority: 19`. The description is plain-language, like its neighbours.
- `src/Paperbunkr.Data/Events/LibraryEvents.cs` (edit): `public sealed record ScrapeCompletedEvent(IReadOnlyList<int> SeriesIds)`,
  `event Action<ScrapeCompletedEvent>? ScrapeCompleted` and a `Raise` overload.
- `src/Paperbunkr.App/Scraper/ScrapeCoordinator.cs` (edit): raise `ScrapeCompleted` when `ScrapeIssuesAsync` finishes with at least
  one applied issue (series ids of those issues).
- `src/Paperbunkr.App/Services/SeriesConnectorReactions.cs` (new): subscribes `LibraryScanCompleted` and `ScrapeCompleted` (pattern:
  `ScrapeOnImport`, `SmartLibraryReactions`). It runs `RunLocalQuietAsync` on the thread pool, coalescing overlapping triggers into one
  run, and posts an Activity Center note only when `Created + Removed > 0`.
- `src/Paperbunkr.App/App.axaml.cs` (edit): construct it next to `ScrapeOnImport` and dispose it on exit.

**Depends on:** Steps 4, 5.
**Verify:** App tests by class: `ScheduledTaskCatalogTests` (the entry and its settings), new `SeriesConnectorReactionsTests` (off
switch blocks; quiet when nothing changed; overlapping triggers coalesce), and `ScrapeCoordinator` tests for the new event.
`dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~ScheduledTaskCatalog|FullyQualifiedName~SeriesConnectorReactions|FullyQualifiedName~ScrapeCoordinator"`.

## Step 7: Suggestions engine
**Files:**
- `src/Paperbunkr.Data/Metadata/SeriesConnector/SeriesRelationSuggestions.cs` (new):
  `For(PaperbunkrDbContext, int seriesId, int limit = 8)` → `IReadOnlyList<InferredSeriesRelation>`.
  - S1 uses `CharacterAppearance`: share of a series' issues per character, ≥ 50 % in both, confidence 0.6 + 0.1 × min share above 50 %.
  - S2 uses `CreatorCredit` writer roles, ≥ 3 issues each, overlapping years.
  - S3: spin-off by character name in the title.
  - S4: title containment.
  It excludes pairs that already have any relation, user pairs, dismissed pairs and the content-type guard, and returns the top 8
  sorted by confidence.

**Depends on:** Step 3 (guards, record).
**Verify:** `SeriesRelationSuggestionsTests` (SQLite fixture): each rule's threshold, the cap, the exclusions, and returns nothing for
a series with no candidates.

## Step 8: Related tab — rail labels, suggestions, event rows
**Files:**
- `src/Paperbunkr.App/Models/PosterRailItem.cs` (edit): `bool IsSuggestion`.
- `src/Paperbunkr.App/Views/PosterRail.axaml` (+ `.axaml.cs`) (edit): `AcceptCommand` styled property. On a suggestion card, an
  Accept (`Checkmark`) button beside the existing remove button, both `iconButton sm` with `AutomationProperties.Name` ("Accept {0}",
  "Dismiss {0}"). A `.dimmed` state style on suggestion cards (`Classes.dimmed="{Binding IsSuggestion}"`, cover Opacity via token).
  Keep the existing `railRemove` look for non-suggestion cards. Load `paperbunkr-design-language` and avalonia `components` /
  `review-checklist` before editing.
- `src/Paperbunkr.App/Models/EventRelationRow.cs` (new): `EventId`, `Title`, `ContextLabel`, `ObservableCollection<PosterRailItem> Items`.
- `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit):
  - `RefreshRelated`: exclude `TieIn` / `MainEventSeries` endpoints. The SubLabel shows the automatic reason
    (`RelationEvidence.ProviderRelationType` for `Inferred`, "Metron" for associated, "GCD") instead of only the type.
  - Replace `RefreshSameEvent` with `RefreshEventRows`: build `EventRows` (stored `TieIn` relations grouped by evidence event id, main
    series first with "Main event", tie-ins with their issue range; current series left out; newest event first) and `OtherEvents`
    (events with no main series and 4+ series). Add `ShowAllEvents`, `VisibleEventRows` (first 3 unless expanded) and `OpenEventCommand`.
    Remove `SameEvent` / `EventRail` / `HasSameEvent` and update `HasAnyRelatedRail`.
  - Suggestions: `RefreshSuggestions(seriesId)` on the thread pool with the generation guard (same shape as `RefreshMoreLikeThis`). The
    rows are appended to `RelatedRail` with `IsSuggestion = true` and dropped when the setting is off.
  - `AcceptSuggestionCommand`: `MediaRelationResolver.TryCreate` with the suggested type (User evidence).
    `DismissSuggestionCommand`: `MediaRelationResolver.DismissSeriesPair`. Both do their removal and refresh in
    `Dispatcher.UIThread.Post`, with a fresh context inside the lambda (CLAUDE.md routed-event gotcha). Move `RemoveRelation`'s refresh
    into a `Post` too, since it has the same shape.
  - `FindRelationsCommand`: runs `SeriesConnectorRunner.RunFullAsync` as an Activity Center job, then refreshes.
- `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit): remove the Same Event card. Add an `ItemsControl` over `VisibleEventRows`, each a
  `Border.card` > `PosterRail` (`ClickCommand` = `OpenRelatedSeriesCommand`, `ContextLabel` → `OpenEventCommand`). Add a
  `Button.ghost` "Show all {n} events", the "Other events" rail, `AcceptCommand` / `RemoveCommand` wiring on the Related rail for
  suggestions, and a `Button.ghost` "Find relations" beside the existing manage label.

`PosterRail.ContextLabel` is plain text today. Making it clickable for "open event" needs a `ContextCommand` styled property: the label
becomes a `Button.ghost sm` when the command is set.

**Depends on:** Steps 1, 4, 6 (runner), 7.
**Verify:** `DetailTabsViewModelTests` by class, with new cases: event rows replace Same Event; main series first; issue-range labels;
the 3-row cap and Show all; the Other events rail; accept → user relation; dismiss → dismissal; removing an automatic relation writes
a dismissal; suggestions hidden when the setting is off. Run the lint classes
(`XamlConsistencyLintTests|ThemeHardcodedColorLintTests|SharedControlsStyleTests`) and the `avalonia-pro-max/review-checklist`.

## Step 9: Preferences toggle
**Files:**
- `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit): `[ObservableProperty] bool _findRelatedSeriesAutomatically`,
  loaded with the other library settings (beside `AutoRemoveMissingOnScan`, line ~1157) and persisted through `PersistBehaviorSetting`.
- `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit): a `pref:SettingsRow` (Icon `Link`, title "Find related series
  automatically", the design §6.4 description) with a `ToggleSwitch` (`OnContent`/`OffContent` null) and an AutomationId, placed with
  the scan toggles.

**Depends on:** Step 1.
**Verify:** `PreferencesScreenViewModelTests` is tagged Slow, so run only the new test by name: load and persist round-trip. Add the
toggle to the Preferences search index if the section uses one (check `PreferencesSearchIndex` or equivalent when editing).

## Step 10: Docs and on-screen check
**Files:**
- `docs/paperbunkr-todo.md` (edit): a Beta-backlog entry with status and what was verified (CLAUDE.md roadmap rule).
- `wiki/` page for the Related tab, if one exists (check `wiki/`; user wiki memory).
- Memory: a project memory for this feature.

**Depends on:** Steps 1–9.
**Verify:** build into the normal `bin` output (memory: build into bin before handover). Run the scheduled task once from the
Automation tab on the real library. Check on screen at 1366×768 in a dark and a light theme: Superman Unlimited (suggestions), Batman
(several event rows, the Show all button), a Convergence tie-in (main event card), an Annual (Side story · Annual), and dismissing then
re-running the sweep (it stays gone). The user does the on-screen check (no UI automation without asking).

---

Independent groups: Steps 2, 5, 9 only need Step 1. Steps 3 → 4 → 6 and 3 → 7 are chains. Step 8 needs 4, 6 and 7.
