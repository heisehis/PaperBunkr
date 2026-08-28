# "Events & Continuity" Screen Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-28-events-continuity-screen-redesign-design.md*

Ordered so the app compiles and the screen works after every phase.

## Phase 1: VM navigation model

**Files:** `src/Paperbunkr.App/ViewModels/EventsScreenViewModel.cs`,
`EventsScreenViewModel.Timeline.cs`, `Models/EventsScreenMode.cs` (delete or repurpose)
**What:**
- Replace `ScreenMode` (`EventsScreenMode`) + `SetModeCommand` + `IsEventsMode`/`IsContinuitiesMode`/
  `IsTimelineMode` with:
  - `[ObservableProperty] SelectedEventOrContinuity _selectedKind` (new enum `{ Event, Continuity }`;
    or reuse a 2-value `EventsScreenMode`). Default `Event`.
  - `[ObservableProperty] EventsDetailView _detailView` (new enum `{ Primary, Timeline }`), default
    `Primary`.
  - `Is`-bools: `IsEventSelected`, `IsContinuitySelected`, `IsPrimaryView`, `IsTimelineView`.
  - `[RelayCommand] SetDetailView(EventsDetailView)`.
- `SelectEvent` sets `SelectedKind = Event`, `DetailView = Primary`, clears `_activeContinuityId`,
  `LoadEvent`. `SelectContinuity` mirrors it for continuity.
- `OnDetailViewChanged`: when → `Timeline`, call `LoadTimelineForCurrent()` (Phase 4).
- `MetaLine` computed prop on the event side:
  `"Event · {members} members" (+ " · in {ContinuityName}" when the event has a continuity)`.
  Needs the event's continuity name — `StoryEvent` has no continuity FK today; if none exists,
  drop the continuity clause and the "Set continuity" manage item (note it, don't invent schema).
- Delete from `.Timeline.cs`: `TimelineScope` enum usage, `SetTimelineScopeCommand`,
  `Is*Scope`, `TimelineContinuityChoices`, `SelectedTimelineContinuity`,
  `TimelineSeriesSearchQuery`, `TimelineSeriesSearchResults`, the seed-a-series flow. Keep
  `TimelineSections`, `InferredAges`, `TimelineCharacterAware`, `LoadTimeline`/`LoadContinuityTimeline`
  (repurposed in Phase 4).
**Verify:** build; `EventsScreenViewModelTests` updated (Phase 6).

## Phase 2: `EventMemberRowViewModel` → Reading-Lists row

**Files:** `src/Paperbunkr.App/ViewModels/EventMemberRowViewModel.cs`
**What:** add `[ObservableProperty] int _position`; `TitleLine` (`"{Name} #{Number}"` / `Name`);
`SeriesLine` (`"{Series} · {Year}"`, or with the role appended when set — match the current
"Series · Role" display); `bool HasRole`; `string RoleChipLabel`;
`[RelayCommand] SetRole(EventMembershipRoleOption?)`. Keep `MoveUp`/`MoveDown`/`Remove`/
`SelectedRoleOption`. Parent sets `Position` while filling `Members` in `LoadEvent`.
**Verify:** build.

## Phase 3: new-event / new-continuity dialog

**Files:** `src/Paperbunkr.App/ViewModels/NewEventOrContinuityViewModel.cs` (new),
`src/Paperbunkr.App/Views/NewEventOrContinuityOverlay.axaml` (+ `.axaml.cs`, new),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs`, `src/Paperbunkr.App/Views/MainWindow.axaml`
**What:** VM with `enum Kind { Event, Continuity }`, `Name`, `Publisher` (continuity only),
`Reset(Kind)`, `CanCreate` (name non-empty), `CreateCommand` → `context` add `StoryEvent` /
`SeriesContinuity`, invoke `_onCreated(kind, id)`; `CancelCommand` → `_onCancel`.
`MainViewModel`: `NewEventOrContinuity` instance, `IsNewEventDialogOpen` flag,
`OpenNewEventDialog(string kind)` / `CloseNewEventDialog` commands, `OnEventOrContinuityCreated`
→ close + `Events.SelectEvent`/`SelectContinuity` equivalent + `CurrentScreen = "events"`. Add to
the Escape chain. `MainWindow.axaml`: backdrop overlay block (mirror `NewReadingListOverlay`).
**Verify:** build; `NewEventOrContinuityViewModelTests` (Phase 6).

## Phase 4: Timeline scoped to current item

**Files:** `src/Paperbunkr.App/ViewModels/EventsScreenViewModel.Timeline.cs`
**What:** `LoadTimelineForCurrent()`: if `IsEventSelected` → build `TimelineSections` from the
active event's member issue ids; if `IsContinuitySelected` → from the active continuity's series'
issues (reuse `LoadContinuityTimeline`'s core, minus the picker). `TimelineCharacterAware` stays,
toggled from `⋯ Manage`. `InferredAges` review stays, opened from `⋯ Manage`.
**Verify:** build; timeline tests (Phase 6).

## Phase 5: Views

**Files (new, `Views/Events/`, each with `.axaml.cs`):**
`EventsContinuityShell.axaml`, `EventDetailView.axaml`, `ContinuityDetailView.axaml`,
`EventTimelineView.axaml` (shared era-section control).
**Files (edit):** `src/Paperbunkr.App/Views/EventsScreen.axaml` (becomes the thin shell host or is
replaced by `EventsContinuityShell` — mounted by MainWindow's `DataTemplate`), its `.axaml.cs`,
`src/Paperbunkr.App/Views/MainWindow.axaml` (sidebar block + rail label).
**What:**
- **Shell:** `Grid RowDefinitions="Auto,Auto,*"` — top band (name, `MetaLine` / continuity meta,
  description + `more`/`less`, right-aligned `Primary`/`Timeline` segmented toggle) · action row
  (`＋ Add issues`/`＋ Add series` + `⋯ Manage` MenuFlyout, contents per kind) · a `Panel` that
  shows `EventDetailView` / `ContinuityDetailView` / `EventTimelineView` / empty-state by the
  `Is*` bools.
- **EventDetailView:** flat `ItemsControl` over `Members` — the Reading Lists v2 row
  (`Border.itemRow`, `Position`, 42×63 cover via `CoverImageConverter`, `TitleLine`, `SeriesLine`,
  role chip, hover `⋮` `MenuFlyout` = Set role › (submenu or the compact combo pattern), Move up,
  Move down, Remove). Then the **two recessed panels** (`Border` + click-to-expand, count in
  header): *Related events* (Connected list w/ relation chip + `⋮` Unlink/Open; Suggested list w/
  Connect; `＋ Connect an event…` = `ToggleConnectEventCommand` panel; `Event chain ▸` =
  `EventFamily` tree) and *Issue suggestions* (`SuggestedIssues` rows: label, reason, role `▾`,
  Add, Dismiss; `Dismissed ({n}) ▸` = `DismissedSuggestions` rows w/ Restore). Reuse
  `ScreenChrome.axaml` `issueRow`; drop the local `statCard`/`headerAction` styles.
- **ContinuityDetailView:** `ContinuityMembers` `WrapPanel` of 88×132 posters (name under,
  hover-`✕` = `RemoveSeriesFromActiveContinuityCommand`, click = `OpenContinuitySeriesCommand`);
  the compare panel (`IsComparingContinuities` + `OverlappingContinuities` +
  `SharedContinuitySeries`) renders inline under the action row when active.
- **EventTimelineView:** `ItemsControl` over `TimelineSections` → era header + `WrapPanel` of
  `TimelineIssueCard` posters (unchanged card, restyled).
- **Sidebar (MainWindow):** one `StackPanel IsVisible="{Binding IsEvents}"`: header
  "EVENTS & CONTINUITY" + `＋` `MenuFlyout` (New event → `OpenNewEventDialogCommand` "Event",
  New continuity → "Continuity"); "Events" group label + `Events` list (lean rows: name +
  `ToolTip`, hover `TwoStepConfirm` delete, active = amber left border);
  "Continuities" group label + `Continuities` list (same). Delete the three per-mode blocks and
  the `+ New Continuity` / `New Story Event` inline buttons.
- **Rail:** line ~228/231 `ToolTip.Tip` + `railLabel` "Story Events" → "Continuity".
**Verify:** build (`AVLN` watch — every new `.axaml` ships `.axaml.cs`); Phase 7 manual.

## Phase 6: tests

**Files:** `src/Paperbunkr.App.Tests/EventsScreenViewModelTests.cs` (extend),
`src/Paperbunkr.App.Tests/NewEventOrContinuityViewModelTests.cs` (new)
**What:** `SelectEvent`/`SelectContinuity` set the right `Is*` bools and clear the other's active
id; `DetailView` resets to `Primary` on select; `SetDetailView(Timeline)` populates
`TimelineSections`; `MetaLine` text; `EventMemberRowViewModel.Position` 1-based; role-chip
visibility. New-item VM: event create (name only) fires `onCreated(Event, id)`; continuity create
carries publisher. Existing 4d–4g resolver tests must stay green (run them).
**Verify:** `dotnet test --filter "EventsScreenViewModelTests|NewEventOrContinuityViewModelTests"`.

## Phase 7: build, full suite, manual pass

`dotnet build` clean (delete `obj/Debug/net10.0/Paperbunkr.App.dll`+`.pdb` on a post-CoreCompile
XAML failure). Full `dotnet test` App.Tests + Data.Tests, unfiltered. Launch: sidebar two groups +
`＋` menu; event detail members list calm at rest, `⋮` menu works, the two panels expand;
Members↔Timeline toggle; continuity detail poster grid + hover-`✕`; new-event / new-continuity
dialogs; rail label reads "Continuity"; no startup crash.
