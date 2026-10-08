# Smart features — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-06-smart-features-design.md*

Working rules for every step: new `.axaml` and its code-behind are created together; row-removing buttons defer via `Dispatcher.UIThread.Post`; tests run with
`dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~<Class>"` (Data tests: `src/Paperbunkr.Data.Tests`); never two `dotnet` builds at once; the full
`Speed!=Slow` set runs once at the end of each slice. Precedents mirrored: `PublisherGapsViewModel` + `LibrarySection.axaml` Publishers block (Library Health sections),
`ContinuityCompareOverlay.axaml` (`OverlayShell` dialog), `ContentTypeClassifierRunner` + `ScheduledTaskCatalog` (scheduled task).

## S1 — Smart lists and library gaps
### Step 1.1: Series progress fields + issue `HasPendingProposal`
**Files:** `Data/Entities/SmartListField.cs` (append `UnreadCount`, `ReadCount`, `DaysSinceLastRead`, `HasPendingProposal`), `Data/SmartLists/SeriesSmartListCatalog.cs` (+ Number selectors),
`Data/SmartLists/SeriesSmartListQueryBuilder.cs` (Include issues when a progress field is used; Number evaluation; null = no match), `Data/SmartLists/SmartListCatalog.cs` (Toggle `HasPendingProposal`),
`App/Models/SmartListOptionLabels.cs` (labels / operator sets if they are per-field).
**Depends on:** none. **Verify:** new `SeriesProgressFieldTests` (Data.Tests): read/unread counts, placeholder exclusion, never-opened = no match, last-read via OpenedTime/ReadingEvent; `HasPendingProposal` test.

### Step 1.2: Template catalog
**Files:** `Data/SmartLists/SmartListTemplateCatalog.cs` (new; templates + `Instantiate(template) -> SmartList`), `Data.Tests/SmartListTemplateCatalogTests.cs` (guard test: every field exists in its kind's catalog; operators valid for the data type; instantiation round-trips through the evaluator).
**Depends on:** 1.1. **Verify:** the new tests.

### Step 1.3: Gallery dialog
**Files:** `App/ViewModels/SmartTemplateGalleryViewModel.cs` (new), `App/Views/SmartTemplateGallery.axaml(+.cs)` (new, `OverlayShell`), `App/ViewModels/SmartScreenViewModel.cs` (open gallery / create from template; the three `CreateNew*` commands open it on their kind), `App/Views/SmartScreen.axaml(.cs)` (host the overlay; `NewItem` handler opens it).
**Depends on:** 1.2. **Verify:** `SmartTemplateGalleryTests` (VM: kind tabs, create-from-template makes an editable list and loads it; Blank), headless render test, keyboard test (Ctrl+N, Esc, Enter) in the real window.

### Step 1.4: `HealthFindingDismissal` + migration
**Files:** `Data/Entities/HealthFindingDismissal.cs` (new), `Data/PaperbunkrDbContext.cs` (DbSet + unique index on Kind+Key), `Data/Migrations/*AddHealthFindingDismissals*` (generated; fix Designer nullability if stale), `Data/Metadata/HealthDismissals.cs` (new helper: Dismiss/Restore/IsDismissed/ForKind).
**Depends on:** none. **Verify:** migration test (up, down, up without reusing a dropped column name), helper unit tests.

### Step 1.5: Collection gaps (resolver + section)
**Files:** `Data/Metadata/CollectionGapResolver.cs` (new; options on `InsightsResolver.ComputeGaps` — extract shared core if needed), `App/ViewModels/CollectionGapsViewModel.cs` (new), `App/ViewModels/LibraryHealthSections.cs` (+`CollectionGaps` section, Review tab, anchor `library.healthCollectionGaps`), `App/ViewModels/PreferencesScreenViewModel.LibraryHealthTabs.cs` (property), `App/Views/Preferences/LibrarySection.axaml` (section block, compact table, dismissed sub-group), `App/Models/PreferenceIndex.cs` (entry), navigation for "Open series".
**Depends on:** 1.4. **Verify:** `CollectionGapResolverTests` (ranges, fractional/annual/placeholder/remote excluded, missing-file counted as owned, dismissal keyed on gap set resurfaces), `CollectionGapsViewModelTests`, headless render of the section.

### Step 1.6: Metadata consistency scan
**Files:** `Data/Metadata/MetadataConsistencyResolver.cs` (new), `App/ViewModels/MetadataConsistencyViewModel.cs` (new), `LibraryHealthSections.cs` (+`MetadataConsistency`), `LibrarySection.axaml`, `PreferenceIndex.cs`, `PreferencesScreenViewModel.LibraryHealthTabs.cs`.
**Depends on:** 1.4. **Verify:** `MetadataConsistencyResolverTests` (thresholds, year proposal created Pending, publisher/rating report-only, dismissal), VM tests, headless render.

### Step 1.7: S1 gate
`dotnet test … --filter "Speed!=Slow"` for App.Tests + Data.Tests; run `avalonia-pro-max/review-checklist` over the new views; update `docs/paperbunkr-todo.md` and the spec's Implementation notes.

## S2 — Reading behaviour
### Step 2.1: `ReadingEvent.ActiveSeconds` + capture
**Files:** `Data/Entities/ReadingEvent.cs`, migration, `App/Services/IReadingEventRecorder.cs` + `ReadingEventRecorder.cs` (overload), `App/ViewModels/ReaderScreenViewModel.cs` (+Comfort partial: per-issue delta before `EndComfortVisit`), `App/Services/Reader/ReaderActivityTracker.cs` (new), `BookReaderScreenViewModel.cs`, `PdfPageReaderScreenViewModel.cs`.
**Verify:** recorder tests, tracker tests (pure clock), migration test.
### Step 2.2: Pace + time-left
**Files:** `Data/Metadata/ReadingPaceResolver.cs` (new), `Data/Metadata/TimeLeftFormatter.cs` (new), Detail issue panel / Library inspector / Continue Reading cards bindings. **Verify:** resolver + formatter tests; headless render of the lines.
### Step 2.3: Drop-off watch
**Files:** `Data/Metadata/DropOffResolver.cs` (new), `InsightsScreenViewModel.cs`, `Views/InsightsScreen.axaml` (Today card). **Verify:** resolver tests, headless.
### Step 2.4: Up Next
**Files:** `Data/Metadata/UpNextResolver.cs` (new), `App/Models/HomeLayout.cs` (key), `App/ViewModels/Home/HomeSections.cs`, `HomeScreenViewModel.cs`, `Views/Home/UpNextSection.axaml(+.cs)`, `HomeSectionTemplateSelector` registration. **Verify:** resolver ordering/exclusion tests, Home VM tests, keyboard test.
### Step 2.5: Wanted sort
**Files:** `Data/Acquisition/WantedAffinityScorer.cs` (new), `WantedScreenViewModel.Queue.cs`, `Views/WantedScreen.axaml`. **Verify:** scorer tests, VM sort test.

## S3 — Library hygiene
### Step 3.1: Relink suggestions
**Files:** `App/Services/MissingFileMatchResolver.cs` (new), `MissingFileRowViewModel.cs`, `PreferencesScreenViewModel.cs` (relink-to-match + bulk exact), `LibrarySection.axaml` row template. **Verify:** resolver tier tests, relink data-flow test, headless row.
### Step 3.2: Duplicate keeper ranker
**Files:** `Data/Metadata/DuplicateKeeperRanker.cs` (new), `DuplicateCandidateViewModel.cs` / `DuplicateGroupRowViewModel.cs`, `LibrarySection.axaml`. **Verify:** ranker tests, VM badge test.

## S4 — Metadata
### Step 4.1: Confidence gate
**Files:** `Data/Entities/AppSettings.cs` + migration, `App/Services/LibraryFolderScanner.cs` (two sites), `NeedsReviewViewModel.cs` (Accept all at or above X, Creator revert fix), Preferences Library section (policy + slider), `PreferenceIndex.cs`. **Verify:** gating tests at 0/0.6/1, revert test, VM test.
### Step 4.2: Synopsis genre suggestions
**Files:** `Data/Metadata/SynopsisGenreInferrer.cs` (new), `App/Services/SynopsisGenreSuggestRunner.cs` (new), `Scheduling/ScheduledTaskCatalog.cs` (task `synopsis-genre-suggest`). **Verify:** inferrer tests, runner tests (skip-if-proposed, always Pending), catalog test.

## S5 — Continuity and plugins
### Step 5.1: Continuity suggestions
**Files:** `Data/Metadata/ContinuitySuggestionResolver.cs` (new), `SuggestionsChecksViewModel.cs`, `ContinuityPageViewModel.cs`, `DetailTabsViewModel.cs`, views. **Verify:** resolver tests, VM tests.
### Step 5.2: Chronology check
**Files:** `Data/Metadata/EventChronology.cs` (`FindCycles`), `Data/ReadingLists/ReadingListChronologyCheck.cs` (new), `ReadingListPageViewModel.Checks.cs`, view. **Verify:** check + cycle tests, reorder test.
### Step 5.3: Status writer + finalize job
**Files:** `Data/Metadata/SeriesStatusWriter.cs` (new), `Data/Events/LibraryEvents.cs`, `SeriesActivityEventKind` (+`StatusChanged`), callers (`MetadataLinkResolver`, `SeriesBulkFieldDescriptor`, `LibraryScreenViewModel`, `NeedsReviewViewModel`), `App/Services/SeriesFinalizeService.cs` (new), `AppSettings` + migration, Behavior toggle + index. **Verify:** writer tests (unchanged/once/excluded callers), finalize tests (dedupe, unlinked skip, toggle).
### Step 5.4: Completion hook + Plugin API 4.4
**Files:** `Data/Entities/Continuity.cs` + `StoryEvent.cs` (`CompletedNotifiedAt`) + migration, `Data/Metadata/CollectionCompletionService.cs` (new), `Plugins.Abstractions/Hooks/PluginHooks.cs`, `PluginGlobals.cs`, `PluginGlobalsTypeMap.cs`, `PluginApi.cs` (4.4), `App/Plugins/PluginHostService.cs`, `PluginApiCompatibilityTests.cs`, `wiki/Plugins.md`. **Verify:** completion + re-arm sequence tests, hook dispatch tests, compatibility tests.
### Step 5.5: S5 gate
Same as 1.7, plus a final full `Speed!=Slow` run and the roadmap/todo doc update.
