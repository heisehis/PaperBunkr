# "Events & Continuity" Screen Redesign — Design

**Date:** 2026-08-28
**Follows:** the Reading Lists redesign (`2026-08-28-reading-lists-screen-redesign-design.md`).
Second of the two "remaining screens" in UI-rework Phase 7. The 4d–4g metadata work
(`953addc`) is landed and stable, so this is unblocked.

Full layout/UX redesign of the current `EventsScreen` (`Views/EventsScreen.axaml`, 561 lines;
`EventsScreenViewModel` + 3 partials, ~1270 lines). Reuses the Reading Lists v2 patterns
throughout.

## Background

The current screen is one long `ScrollViewer` with a 3-button mode switcher (Events / Continuities
/ Timeline). Events mode alone stacks: header + one stat card, a library search + role + add row,
a dense 6-column member list (`↑`/`↓` text buttons, always-visible role `ComboBox`), then five
collapsible sections — Connected Events, Suggested connections, Event chain, Suggested Issues,
Dismissed. Continuities mode: header, Compare / Reading-list / Add-Series buttons, a comparison
panel, an add-series search, a member-series poster grid. Timeline mode: a scope selector (Series
family / Continuity / Whole library), a character-aware checkbox, a continuity picker, a
review-inferred-ages panel, era-bucketed poster rows.

Same problems the Reading Lists screen had pre-redesign — everything visible at once, cramped
rows, management never recedes — multiplied by three modes.

## Reframe

The screen is really about **how comics connect across the library** — not just events. New
identity: **"Events & Continuity"** (rail label → **"Continuity"**, screen title
**"Events & Continuity"**).

- **Events** and **Continuities** are two co-equal top-level concepts, both shown in the sidebar.
- **Timeline** stops being a top-level mode. It becomes a **second view toggle inside each detail
  pane** — the era-bucketed layout of *this event's* or *this continuity's* issues.
- The old Timeline "Whole library" and free "seed a series" scopes are **dropped** (a
  library-wide timeline is a separate future feature, not this screen's job).

## Architecture

View-layer + light VM re-presentation. The 4d–4g backend (`EventRelationResolver`,
`EventSuggestionResolver`, `ContinuityResolver`, `BookAgeResolver`, `SeriesFamilyResolver`, the
`EventSuggestionDismissal` / `Character` tables) is unchanged. Almost every existing command and
collection survives; they are relocated and re-grouped.

### Navigation model

`EventsScreenViewModel.ScreenMode` (`Events` / `Continuities` / `Timeline`) is replaced by:

- **`SelectedKind`** — `Event` or `Continuity`, set by which sidebar item is picked. (An event
  selection and a continuity selection are mutually exclusive; picking one clears the other's
  active id.)
- **`DetailView`** — `Primary` or `Timeline`, local to the detail pane, reset to `Primary` on
  every sidebar selection. `Primary` means the member list (event) or series grid (continuity).

`IsEventsMode` / `IsContinuitiesMode` / `IsTimelineMode` and the `SetModeCommand` are removed.
New: `IsEventSelected`, `IsContinuitySelected`, `IsPrimaryView`, `IsTimelineView`,
`SetDetailViewCommand`.

### Sidebar (`MainWindow.axaml`, the `IsEvents` block)

Replaces the current three mutually-exclusive per-mode sidebar blocks with one:

- Header: **"EVENTS & CONTINUITY"** + a `＋` icon button → a `MenuFlyout`: **New event** /
  **New continuity**.
- **"Events"** group label, then the `Events` list — lean rows identical to the Reading Lists
  sidebar (one-line truncating name + `ToolTip.Tip`; hover-only delete via
  `TwoStepConfirm`; active row = 3px amber left border + `PbSurface2` tint). **No** progress bar
  (an event has no "read %" concept that matters here) — name only.
- **"Continuities"** group label, then the `Continuities` list — same lean row.
- The old Timeline-mode sidebar (series-seed search) is deleted.

### New event / New continuity dialogs

Small backdrop overlays, same pattern as `NewReadingListOverlay` but far simpler (no build
methods):

- **New event** — one `TextBox` (name). Create → `context.StoryEvents.Add`, open it.
- **New continuity** — name + publisher `TextBox`es. Create → `context` add a `SeriesContinuity`,
  open it.

One shared `NewEventOrContinuityViewModel` (or two tiny VMs) + a `MainViewModel`
`IsNewEventDialogOpen` flag, in the Escape chain. The current inline "+ New Continuity" /
"New Story Event" sidebar buttons and their `CreateContinuityCommand` / `CreateEventCommand` are
rewired to open the dialog instead of spawning un-named.

### Event detail

Top band → **Members | Timeline** toggle → content.

- **Top band:** event name (`pbTextHeading`), a meta line
  `"Event · {TotalMembers} members · in {ContinuityName}"` (the continuity clause only when set),
  2-line description + `more`/`less` (`Description`; an "Add a description" link when empty →
  opens the details editor).
- **Toggle:** a segmented `Members` / `Timeline` control, right-aligned in the top band.
- **Action row:** permanent **`＋ Add issues`** (opens the existing library-search panel over
  `SearchQuery`/`SearchResults`/`AddIssueCommand`; the role `ComboBox` that was next to Search
  moves into that panel) + **`⋯ Manage`** `MenuFlyout`: Edit details, Set continuity, Link to a
  reading list, Delete event.
- **Members list (Primary view):** grouped is not a concept here (no `GroupLabel`) — a flat
  ordered list. Rows = the Reading Lists v2 row: `Position` · 42×63 cover · `"{Name} #{Number}"` ·
  `"{Series} · {Year}"` · role chip (when set) · hover `⋮` `MenuFlyout` (Set role ›, Move up,
  Move down, Remove from event). `EventMemberRowViewModel` gains `Position` / `TitleLine` /
  `SeriesLine` / `HasRole` / `RoleChipLabel` / `SetRoleCommand`, mirroring
  `ReadingListItemRowViewModel`. The always-visible role `ComboBox` and `↑`/`↓` buttons are
  removed.
- **Two recessed panels** below the list, each a collapsed `Border` with a count in its header,
  expand on click:
  - **Related events** — merges today's Connected Events + Suggested connections + Event chain.
    Sub-headers "Connected" (each: name, relation-type chip, hover `⋮` → Unlink / Open) and
    "Suggested" (each: name, reason, `Connect`), then a `＋ Connect an event…` action (the
    existing `ToggleConnectEventCommand` search) and an `Event chain ▸` disclosure
    (`EventFamily` tree) at the bottom. Header count: `"{n} connected · {m} suggested"`.
  - **Issue suggestions** — merges Suggested Issues + Dismissed. The review list
    (`SuggestedIssues`: label, reason, role `▾`, `Add`, `Dismiss`) then a `Dismissed ({n}) ▸`
    disclosure (`DismissedSuggestions`: label, `Restore`). Header count: `"{n} to review"`.
- **Timeline view:** era-section headers (`TimelineSectionViewModel.Label` +
  `CommonlyCitedRange`) + poster rows (`TimelineIssueCard` — 104-wide poster, unread dot,
  reduced-confidence `?` badge, click → open in reader). Nothing else on screen. The scope
  selector, character-aware checkbox, and "Review inferred ages" panel move into **`⋯ Manage`**
  (`⋯ Manage` in Timeline view: "Review inferred ages ({n})", "Include character-only series"
  toggle). Timeline scope is fixed to **the current event's members** — no scope buttons.

### Continuity detail

Top band → **Series | Timeline** toggle → content.

- **Top band:** continuity name (`pbTextHeading`), meta line
  `"Continuity · {Publisher} · {SeriesCount} series"`, 2-line description + `more`/`less`.
- **Action row:** permanent **`＋ Add series`** (existing `ContinuitySeriesSearchQuery` panel) +
  **`⋯ Manage`** `MenuFlyout`: Edit details, **Compare with another continuity** (the existing
  overlap flow — its result renders inline below the action row when active), **Create reading
  list** (`CreateReadingListFromContinuityCommand`, publication order), Delete continuity.
- **Series grid (Primary view):** the existing `ContinuityMembers` poster `WrapPanel` — 88×132
  posters (bumped from 150-tall bespoke), name under, **hover-`✕`** to remove
  (`RemoveSeriesFromActiveContinuityCommand`), click → series detail. Restyled onto tokens /
  `PosterTile`-ish treatment; not the raw `Border` it is now.
- **Timeline view:** same era layout, scoped to this continuity's issues
  (`TimelineScope.Continuity` with `SelectedTimelineContinuity` pinned to the active one — the
  picker `ComboBox` is gone). `⋯ Manage` in Timeline view offers "Review inferred ages".

### Empty states

- No events and no continuities → a single centred prompt: "Create an event or a continuity to
  get started" + the two `＋` actions.
- An event with no members → "No members yet — Add issues to get started" + the Add-issues panel.
- A continuity with no series → "No series yet — Add series".

## ViewModel changes (summary)

- **`EventsScreenViewModel` core:** `ScreenMode`/`SetModeCommand`/`Is*Mode` → `SelectedKind` +
  `DetailView` + the new `Is*` bools + `SetDetailViewCommand`. `OnScreenModeChanged`'s lazy-load
  switch is replaced by load-on-selection via the sidebar selection commands (`SelectEventCommand`,
  and a `SelectContinuityCommand` added if not already present). Add a `MetaLine` computed prop for
  the event top band (mirrors Reading Lists).
- **`EventMemberRowViewModel`:** `Position`, `TitleLine`, `SeriesLine`, `HasRole`,
  `RoleChipLabel`, `SetRoleCommand` (mirror `ReadingListItemRowViewModel`). Drop nothing —
  `MoveUp`/`MoveDown`/`Remove`/`SelectedRoleOption` stay, just re-bound to the `⋮` menu.
- **`EventsScreenViewModel.Timeline.cs`:** delete the scope enum plumbing (`TimelineScope`,
  `SetTimelineScopeCommand`, `Is*Scope`, `TimelineContinuityChoices`,
  `SelectedTimelineContinuity`, the sidebar series-seed search
  `TimelineSeriesSearchQuery`/`TimelineSeriesSearchResults`). `LoadTimeline` takes its issue set
  from the active event's members or the active continuity's series' issues directly. Keep
  `TimelineSections` / `InferredAges` / `TimelineCharacterAware` (now toggled from `⋯ Manage`).
- **`EventsScreenViewModel.EventGraph.cs` / `.Continuities.cs`:** unchanged logic; the Views just
  render their collections in the new places.
- **`MainViewModel`:** `Events` ctor unchanged. New `IsNewEventDialogOpen` flag +
  open/close/`onCreated` wiring + the shared new-item VM, in the Escape chain. Rail button
  `ToolTip.Tip` and `railLabel` text "Story Events" → "Continuity".

## View file structure

`EventsScreen.axaml` (561 lines) is split, mirroring `Views/Preferences/`:

- `Views/Events/EventsContinuityShell.axaml` — top band + toggle + action row host + the
  `ContentControl` that swaps event-detail / continuity-detail / empty-state.
- `Views/Events/EventDetailView.axaml` — members list + the two recessed panels.
- `Views/Events/EventTimelineView.axaml` — era sections (shared control, takes an issue-card
  collection).
- `Views/Events/ContinuityDetailView.axaml` — series grid + compare panel.
- Timeline view reused for both via the shared control.

(If splitting proves heavier than the win, a single restructured `EventsScreen.axaml` with the
sections as `IsVisible`-gated blocks is an acceptable fallback — decided during implementation.)

## Testing

- `EventsScreenViewModelTests` (extend): `SelectedKind` / `DetailView` transitions; selecting an
  event then a continuity clears the event; `DetailView` resets to `Primary` on selection;
  `MetaLine` text; member-row `Position` numbering; role-chip visibility.
- New `NewEventOrContinuityViewModelTests`: name-only event create fires `onCreated`; continuity
  create carries publisher.
- Timeline: `LoadTimeline` for an event uses its members; for a continuity uses its series;
  no-resolvable-age empty state.
- Existing 4d–4g resolver tests (`EventRelationResolverTests`, `EventSuggestionResolverTests`,
  `BookAgeResolverTests`, etc.) are untouched and must stay green.
- Manual on-screen pass: sidebar two groups + `＋` menu; event detail members list + `⋮` menu +
  the two panels expand; Members↔Timeline toggle; continuity detail series grid + hover-`✕`;
  new-event / new-continuity dialogs; rail label reads "Continuity".
- Full suite (App.Tests + Data.Tests) per the `DatabasePathOverride` / `AvaloniaTestCollection`
  isolation lesson.

## Build note

Per CLAUDE.md: any new `Views/Events/*.axaml` ships with its `.axaml.cs` in the same commit;
on an XAML-compile failure after `CoreCompile` succeeds, delete
`obj/Debug/net10.0/Paperbunkr.App.dll` + `.pdb` and rebuild rather than retrying `dotnet build`.

## Out of scope

- A library-wide timeline (old "Whole library" scope) — separate future feature.
- Reworking the 4d–4g resolvers or data model.
- Character-index UI beyond the `⋯ Manage` "include character-only series" toggle.
- `BookAge` autocomplete editor, `SmartListField.Continuity` condition, cross-continuity
  reading-list builder — those shipped in `953addc` and are not touched here.
