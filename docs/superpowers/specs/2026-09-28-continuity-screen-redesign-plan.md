# Continuity screen redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md*

Avalonia subskills governing the UI steps (read off disk under `~/.claude/skills/avalonia/`): `avalonia-mvvm` (view models,
commands), `avalonia-xaml` + `avalonia-data-templates` (views), `avalonia-styling` + `avalonia-pro-max/design-system` (tokens, era
colours), `avalonia-input-interaction` (poster drag), `avalonia-testing` (headless tests), `avalonia-pro-max/review-checklist`
(before calling it done).

## Survey notes that shape the plan

- The old view model loads synchronously and every test drives it synchronously. The new pages keep the **editable lists** (members,
  series, suggestions) loading as today and move the **heavy overview** (stats, runs, events in order, attention, Continue target,
  collage) into a background runner seam (`Func<Func<T>, Task<T>>`, synchronous in tests). The spec's "Loading" section is updated to
  say exactly this.
- **Automatic / Custom** is a display choice for the Series section, remembered per continuity for the session (no new column).
  `ContinuityMembership.SortOrder` keeps driving map lanes and the reading-list export, as today; dragging in Custom writes it.
- **Timeline scopes:** the series-family, whole-library and character-aware scopes (`TimelineScope`, `LoadTimeline`,
  `LoadLibraryTimeline`) haven't been reachable from any view since the 2026-08-28 redesign (grep: only the old view model and its
  tests reference them). The new timeline view model keeps the continuity and event scopes only; the five tests of the dead scopes are
  listed below as dropped with that reason.
- Era colours: skins aren't split into light/dark resource dictionaries; `ThemeService` writes `Pb…Color` from the active theme and
  its `mode`. So the era defaults are a dark and a light set in `ThemeService`, chosen by `theme.Mode`, overridden by optional
  `theme.json` keys; `App.axaml` holds the dark set as the pre-first-apply fallback.
- `SeriesCardSample` already implements `ITileProgressSource`, so posters get the progress ring from `TileCosmeticsOverlay` for free.

## Phase 1 — shell, sidebar, Suggestions & checks, builders

### Step 1: Remembered sidebar tab
**Files:** `Paperbunkr.Data/Entities/AppSettings.cs` (edit), migration `AddContinuitySidebarTab` (new, `dotnet ef migrations add` -
builds only, never touches the dev DB), `Paperbunkr.Data.Tests/AddContinuitySidebarTabMigrationTests.cs` (new, forward-only, modelled
on `AddLibraryHealthTabMigrationTests`).
**Verify:** the new migration test; the model snapshot diff has only the new column.

### Step 2: Overview builders
**Files (new):** `Paperbunkr.App/Services/ContinuityScreen/ContinuityOverview.cs` (records + `ContinuityOverviewBuilder`, pure),
`ContinuityOverviewLoader.cs` (DB + optional `GcdDataStore` → snapshot, aggregate projections only), `EventOverview.cs` (records +
`EventOverviewBuilder`), `EventOverviewLoader.cs`.
- Runs: member series + Continuation relations among them + GCD bonds to series not in the library (placeholders); components of
  two or more; order older → newer (Kahn, ties by start year, cycles broken by start year); label "Name · years · N series";
  the rest Standalone by start year.
- Stats, events in order (`EventChronology.Order` over `ContinuityMapLoader.LoadData`), link lines between neighbours, attention
  (pending duplicates, weak inferred connections inside the continuity, outside series), Continue target (first unread, owned row in
  `ContinuityMapBuilder` order).
- Event: neighbours from directional relations (`EventChronology.Direction`), stats (core/tie-in counts, series, % read, years via
  `EventChronology.LoadSpans` with GCD), source label from arc ids / `Origin`, continuities containing its series, attention counts,
  Continue target, collage keys (up to 8 core covers).
**Tests (new):** `Paperbunkr.App.Tests/ContinuityScreen/ContinuityOverviewBuilderTests.cs`, `EventOverviewBuilderTests.cs`.

### Step 3: Sidebar view model
**Files:** `ViewModels/ContinuitySidebarViewModel.cs` (new); `Models/ContinuitySummary.cs`, `Models/StoryEventSummary.cs` (add issue
count, start year, faint label); `Models/ContinuitySidebarTab.cs` (new enum).
- One grouped query per list; Continuities | Events tab saved to `AppSettings.ContinuitySidebarTab`; Suggestions & checks badge
  count.
**Tests:** `ContinuityScreen/ContinuitySidebarViewModelTests.cs` (tab saved and restored, first-time Continuities, counts, delete
confirm on rows).

### Step 4: Suggestions & checks view model
**Files:** `ViewModels/SuggestionsChecksViewModel.cs` (new) - ports `EventsScreenViewModel.StoryEventSuggestions.cs`, `.Identity.cs` and
the Wikidata half of `.Continuities.cs` unchanged in behaviour, with callbacks to the shell for "open what I created" and "an event
was merged away".
**Tests:** `ContinuityScreen/SuggestionsChecksViewModelTests.cs`.

### Step 5: Shell
**Files:** `ViewModels/ContinuityScreenViewModel.cs` (+ `.Map.cs`) (new): owns `Sidebar`, `Suggestions`, `ContinuityPage`,
`EventPage`, `Timeline`, `Map`, the Overview | Map | Timeline switch, what's open (nothing / continuity / event / suggestions), and
the members `MainViewModel` calls (`LoadEvent`, `LoadContinuity`, `EnsureEventLoaded`, `ActiveEventId`, `ActiveContinuityId`,
`RefreshSidebar`, `RefreshContinuitiesSidebar`, `RefreshStoryEventCandidates`, `RefreshPossibleDuplicates`, `SelectEventCommand`,
`SelectContinuityCommand`, `GoToReaderInEvent`, `ReaderIssueId`).
**Tests:** `ContinuityScreen/ContinuityScreenViewModelTests.cs` (navigation, map follows selection, open-map deferral, reader
reselect).

## Phase 2 — continuity page

### Step 6: Continuity page view model
**Files:** `ViewModels/ContinuityPageViewModel.cs` (new): ports `.Continuities.cs` (members, add/remove, notes, bulk select, delete,
merge, compare, reading list) and the continuity half of `.Map.cs`, plus Overview state from Step 2, Automatic/Custom, drag move,
the attention flyouts (connections Accept/Dismiss, outside series Add), the compare overlay's three columns, description more/less.
**Tests:** `ContinuityScreen/ContinuityPageViewModelTests.cs`.

### Step 7: Views
**Files (new):** `Controls/HeroBand.cs` + `Styles/HeroBand.axaml` (templated control: collage, fade, title slot, stats, chips,
description, buttons, tab switch), `Views/ContinuityScreen.axaml(.cs)`, `Views/ContinuityPageView.axaml(.cs)`,
`Views/SuggestionsChecksView.axaml(.cs)`, `Views/ContinuitySidebarView.axaml(.cs)`, `Views/ContinuityCompareOverlay.axaml(.cs)`.
Code-behind added in the same step as each `.axaml` (CLAUDE.md AVLN2000 gotcha). Drag-and-drop for Custom order in
`ContinuityPageView.axaml.cs`, with Ctrl+←/→ as the keyboard equivalent.
**Verify:** App build; headless tests in Step 12.

### Step 8: Publisher suggestion box
**Files:** `ViewModels/NewEventOrContinuityViewModel.cs` (publisher suggestions from the library), its dialog XAML (`SuggestBox` +
`BrandMark` for the current value).
**Tests:** `NewEventOrContinuityViewModelTests` gains a publisher-suggestions test.

## Phase 3 — event page and the switch

### Step 9: Event page view model and view
**Files (new):** `ViewModels/EventPageViewModel.cs` (ports the event half of `EventsScreenViewModel.cs`, `.EventGraph.cs`, the event
half of `.Map.cs`; adds filters, neighbours, attention, Continue, source/continuity chips), `Views/EventPageView.axaml(.cs)`.
**Tests:** `ContinuityScreen/EventPageViewModelTests.cs`; `RoleDetectionUiTests` gains event-page twins of its event tests.

### Step 10: Switch the rail to the new screen
**Files:** `ViewModels/MainViewModel.cs` (`Events` becomes `ContinuityScreenViewModel`), `Views/MainWindow.axaml` (sidebar block →
`ContinuitySidebarView`; content template → `ContinuityScreen`), `ViewModels/EventsCardContextMenuBuilder.cs`,
`EventsCardContextMenuBuilderTests.cs`, any view binding `Events.*`.
**Verify:** full App test suite; the user checks it on screen (end of phase 3).

## Phase 4 — Timeline polish

### Step 11: Timeline view model, view and era tokens
**Files:** `ViewModels/ContinuityTimelineViewModel.cs` (new: continuity/event scopes, histogram, era progress, folding),
`Views/ContinuityTimelineView.axaml(.cs)` (new), `ViewModels/TimelineSectionViewModel.cs` (era, counts, fold state),
`App.axaml` (era tokens), `Models/ThemeDefinition.cs` (optional `eraPlatinum`…`eraModern`), `Services/ThemeService.cs`
(apply with light/dark defaults).
**Tests:** `ContinuityScreen/ContinuityTimelineViewModelTests.cs`, `ThemeServiceTests` (era defaults per mode, skin override).

### Step 12: Headless view tests and review
**Files:** `ContinuityScreen/ContinuityScreenViewTests.cs` (hero with and without covers, sidebar switch, histogram click scrolls).
Run `avalonia-pro-max/review-checklist` over the new views; fix what it finds. Update `docs/paperbunkr-todo.md` and memory.

## Phase 5 — after the user's OK (not in this pass)

Delete `Views/EventsScreen.axaml(.cs)`, `Views/EventTimelineView.axaml(.cs)`, the seven `EventsScreenViewModel*.cs`,
`Models/EventsScreenMode.cs`, `Models/TimelineScope.cs`, and the old tests (`EventsScreenViewModelTests`, `EventsScreenIdentityTests`,
`EventMap/EventsScreenMapTests`, the old-screen tests in `ContinuityMapViewModelTests` and `RoleDetectionUiTests`). Update the wiki
page, the roadmap and the older specs' notes.

## Test parity list

| Old test | Lands in |
|---|---|
| EventsScreenViewModelTests: NoEvents_HasNoEventsTrue, SwitchToContinuitiesMode_…, ContinuitySidebarRow_HasDeleteConfirm, DeleteConfirm_Trigger_…, Delete_OfTheLastRemainingEvent_… | ContinuitySidebarViewModelTests / ContinuityScreenViewModelTests |
| CreateNew_…, Search_…, AddIssue_…, MoveDown_…, RemoveMember_…, ChangingRoleOnRow_…, Connect/RemoveConnected/OpenConnected/SearchEvents, AddSuggestion/DismissSuggestion×2/SuggestedRole, EventChain_…, ConnectionSuggestion_…, DeleteActiveEvent_…, SearchQuery_LiveSearches, ToggleAddIssues_…, AddSelectedMembers_…, RemoveSelectedMembers_…, MemberRow_Position_Is1Based, RoleAndRelationTextProjections_… | EventPageViewModelTests |
| SelectContinuity_…, AddAndRemoveSeries_…, CreateNewContinuityFromSidebar_…, OpenContinuitySeries_…, CompareContinuities_…, CreateReadingListFromContinuity_…, SetContinuitySeriesNote_…, MoveContinuitySeriesLater_… (now drag/keyboard move), DeleteActiveContinuity_…, AddSelectedSeries_…, RemoveSelectedSeries_… | ContinuityPageViewModelTests |
| SelectEvent_ThenSelectContinuity_…, SetDetailView_Timeline_ForEvent_… | ContinuityScreenViewModelTests |
| TimelineContinuityScope_…, TimelineIssue_InDisputedWindow_…, ClickingTimelineIssue_…, TimelineInferredAges_… | ContinuityTimelineViewModelTests (re-seeded through continuity/event scope) |
| SelectTimelineSeries_…, TimelineLibraryScope_…, TimelineCharacterAware_… | **Dropped**: series-family / library / character-aware scopes are unreachable since 2026-08-28 (see survey notes); their era bucketing is covered by the continuity-scope tests |
| RefreshStoryEventCandidates_…, AcceptStoryEventCandidate_…, DismissStoryEventCandidate_… | SuggestionsChecksViewModelTests |
| EventsScreenIdentityTests (all five) | SuggestionsChecksViewModelTests (Renaming_MarksTheEventYours stays with NewEventOrContinuity - it's the dialog) |
| EventsScreenMapTests (all five) | ContinuityScreenViewModelTests |
| ContinuityMapViewModelTests: Screen_MapToggle_…, Screen_TheSweep_…, Screen_TypedSuggestion_… | ContinuityScreenViewModelTests / EventPageViewModelTests |
| RoleDetectionUiTests: event-side tests | re-pointed at EventPageViewModel |
| EventsCardContextMenuBuilderTests | kept, re-pointed through MainViewModel's new `Events` |
