# Saved List Layouts

**Date:** 2026-08-17
**Status:** Approved, pending implementation plan
**Backlog ref:** `docs/Paperbunkr-Roadmap.md`'s "Library browsing extras" bundle — third sub-project
(after Reveal-in-Explorer/fileless entries and Manga/ContentType classification). Sort/group
strategies is paused (see prior session's handoff); this sub-project doesn't depend on it. Saved
Workspaces (the *next* sub-project) depends on this one.

## Context

CE's own equivalent isn't a single feature — it's two distinct concepts, confirmed from source:

- **`DisplayListConfig`** (`ComicRack.Engine\Database\DisplayListConfig.cs`) — a single,
  transparently auto-persisted config per list view: columns, sort/group, thumbnail/tile captions,
  quick-search text, and show/hide filter state (`ShowOptionType`/`ShowComicType`). Edited via
  `ListLayoutDialog`, but the persistence itself has no "Save" step — it's just always-current
  state, one instance per browser view (library view, files view, etc.).
- **`DisplayWorkspace`** (`ComicRack\Config\DisplayWorkspace.cs`) — a *named*, user-managed,
  multiple/switchable bundle (window layout + list layout + page layout + page display settings),
  edited via `SaveWorkspaceDialog`. This is CE's actual "save this as a named preset I can switch
  back to" feature.

`docs/Paperbunkr-Roadmap.md` already names this split correctly: "saved List Layouts... this is
persistence, not new UI" as one item, "saved Workspaces (depends on List Layouts)" as the next.
This spec covers only the first half — the CE `DisplayListConfig` equivalent. Named/multiple
presets (CE `DisplayWorkspace`) are explicitly out of scope here.

`LibraryScreenViewModel` already has a fully-functional session-only sort/group/display/filter UI
(7 view modes, 7 sort fields, 4 group fields, a grid-density slider, 5 overlay badge toggles, free
search, 3 filter checkboxes, sidebar content-type/category selection) — all of it resets to
defaults on every app restart today. This spec makes it persist, matching how `AppSettings`
already persists Reader defaults (`DefaultPageFitMode`, `DefaultAutoRotate`, etc.).

## Scope

Persisted, one `AppSettings` row (the existing app-wide singleton, `Id` always 1):

- **Sort**: `SortField` (`LibrarySortField`), `SortDirection`
- **Group**: `GroupField` (`LibraryGroupField`)
- **Display**: `ViewMode` (`LibraryViewMode`), `GridDensity`, and the 5 overlay toggles
  (`ShowUnreadBadge`, `ShowPublisherBadge`, `ShowLanguageBadge`, `UseLanguageIcon`,
  `ShowContinueReadingButton`) — already flagged in `LibraryScreenViewModel`'s own doc comment as
  "Persisting these is Beta-scoped (Saved Workspaces/List Layouts)"
- **Filter**: `SearchQuery`, the active sidebar selection (`ContentType` or `CategoryId`,
  mutually exclusive), and the 3 filter checkboxes (`FilterUnreadOnly`, `FilterMissingIssues`,
  `FilterTrackedOnly`) — full CE parity, matching `DisplayListConfig`'s own inclusion of
  `QuickSearch`/`ShowOptionType`/`ShowComicType`

Explicitly **not** persisted: the "+ Add" flyout's transient fields (`NewIssueSeriesName`,
`NewIssueNumber`, `NewIssueContentType`, `NewIssueReadingMode`) — form state for a single in-flight
action, not view state, and already reset on every open regardless of this spec.

## Architecture

### Enum relocation

`AppSettings` lives in `Paperbunkr.Data`; the 4 enums driving sort/group/display (`SortDirection`,
`LibrarySortField`, `LibraryGroupField`, `LibraryViewMode`) currently live in
`Paperbunkr.App.Models` — the wrong direction for `AppSettings` to reference (`Data` cannot depend
on `App`). Rather than a second parallel set with mapping code at the boundary, this follows the
precedent already set for `PageLayoutMode`/`PageTransitionStyle`/`ImageFitMode`/
`ImageBackgroundMode`: those live solely in `Paperbunkr.Data.Entities` today (no `App.Models`
counterpart), and `ReaderScreenViewModel` references that same type directly for its own
in-memory state, not just for persistence.

Move (not duplicate) into `Paperbunkr.Data.Entities`:
- `SortDirection` (`Ascending`/`Descending`)
- `LibrarySortField` (`Name`/`DateAdded`/`LastRead`/`Size`/`IssueCount`/`UnreadCount`/`Publisher`)
- `LibraryGroupField` (`None`/`ContentType`/`Publisher`/`Alphabetical`)
- `LibraryViewMode` (7 modes: `CompactGrid`/`ComfortableGrid`/`CoverOnlyGrid`/`PanoramaGrid`/
  `List`/`Details`/`Tiles`)

Doc comments and default values on each move unchanged. `Paperbunkr.App.Models/SortDirection.cs`,
`LibrarySortField.cs`, `LibraryGroupField.cs`, `LibraryViewMode.cs` are deleted; every reference
(`LibraryScreenViewModel.cs`, `LibraryScreenViewModelTests.cs`, `LibraryScreen.axaml`,
`LibraryScreen.axaml.cs`, `Models/SeriesCardGroup.cs`) updates its `using` from
`Paperbunkr.App.Models` to `Paperbunkr.Data.Entities` for these 4 types (both namespaces are
already imported in `LibraryScreenViewModel.cs`, so this is a pure relocation, not a new
dependency). `ContentType` (for the active-filter column) already lives in `Data.Entities` — no
move needed.

### New `AppSettings` columns (one EF migration)

| Column | Type | Default |
|---|---|---|
| `LibrarySortField` | `LibrarySortField` | `DateAdded` |
| `LibrarySortDirection` | `SortDirection` | `Descending` |
| `LibraryGroupField` | `LibraryGroupField` | `None` |
| `LibraryViewMode` | `LibraryViewMode` | `ComfortableGrid` |
| `LibraryGridDensity` | `double` | `1.0` |
| `LibraryShowUnreadBadge` | `bool` | `true` |
| `LibraryShowPublisherBadge` | `bool` | `false` |
| `LibraryShowLanguageBadge` | `bool` | `false` |
| `LibraryUseLanguageIcon` | `bool` | `false` |
| `LibraryShowContinueReadingButton` | `bool` | `false` |
| `LibrarySearchQuery` | `string?` | `null` |
| `LibraryActiveContentType` | `ContentType?` | `null` |
| `LibraryActiveCategoryId` | `int?` | `null` |
| `LibraryFilterUnreadOnly` | `bool` | `false` |
| `LibraryFilterMissingIssues` | `bool` | `false` |
| `LibraryFilterTrackedOnly` | `bool` | `false` |

Defaults match each field's current in-code default exactly, so an existing `AppSettings` row
(migrated forward) reproduces today's actual startup behavior bit-for-bit until the user changes
something.

## Data flow

- **Load**: `LibraryScreenViewModel`'s constructor reads `AppSettings` once via
  `context.GetOrCreateAppSettings()` and seeds all fields above (including `_activeContentType`/
  `_activeCategoryId`, currently private fields with no public setter path — construction seeds
  them directly, same as today's `null` initial value) before the first `LoadFromDatabase()` call,
  so the first render already reflects last session's state.
- **Save**: each field's existing `partial void On*Changed` writes straight to `AppSettings` +
  `context.SaveChanges()`, immediately, matching `ReaderScreenViewModel`'s existing
  `PageTransitionStyle` write pattern — no debounce, including `SearchQuery` (this ViewModel
  already re-queries on every keystroke with no debounce, per its own doc comment; the extra write
  is consistent with that existing philosophy, and SQLite writes at this scale are cheap). The 3
  sidebar-selection commands (`SelectAllSeries`, `SelectContentType`, `SelectCollection`), which
  currently have no `On*Changed` hook since `_activeContentType`/`_activeCategoryId` are plain
  fields, gain the same immediate-write behavior inline.

## Edge case: stale category reference

If `LibraryActiveCategoryId` points at a category deleted since last session, load falls back to
"All Series" (both `_activeContentType`/`_activeCategoryId` null) rather than silently rendering an
empty grid with no visible reason why.

## Testing

Extend `LibraryScreenViewModelTests`:
- Constructing the ViewModel against an `AppSettings` row with non-default values for every field
  above reflects those values immediately (sort/group/view-mode/density/badges/search/filter
  checkboxes/active content-type/active category).
- Changing each field writes it back to `AppSettings` (assert via a fresh context read).
- Stale-category fallback: seed `LibraryActiveCategoryId` pointing at a non-existent id,
  construct, assert both active-filter fields are null and "All Series" is effectively active.

No new UI to manually verify beyond confirming a restart actually preserves what was left set —
noted as a manual-verification item, consistent with this project's standing on-screen-testing gap
(no unattended desktop GUI automation available).

## Explicitly out of scope

Named/multiple saved presets (CE's `DisplayWorkspace` — the next sub-project, "Saved Workspaces").
Per-column visibility/order for the Details/List view modes (CE's own `ListLayoutDialog` UI) —
Paperbunkr's List/Details view modes don't currently expose configurable columns at all; adding
that is a separate, larger UI change, not persistence of something that already exists. Window
layout, page layout, and page display settings (also part of CE's `DisplayWorkspace`, not
`DisplayListConfig`) — those are Reader-screen concerns already covered by existing `AppSettings`
Reader-tab fields, unrelated to this spec's Library-screen scope.
