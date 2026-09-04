# Saved Workspaces — Design

*Part of the "Library browsing extras" backlog (docs/Paperbunkr-Roadmap.md). The next sub-project after
**Saved List Layouts** (2026-08-17), which this builds directly on — that shipped the single
auto-persisted view-state; this adds named, multiple, switchable versions of it.*

## CE-parity check (standing rule)

Checked `_reference/ComicRackCE` first.

- **`DisplayWorkspace`** (`ComicRack/Config/DisplayWorkspace.cs`, `WorkspaceType.cs`,
  `IDisplayWorkspace.cs`, `Dialogs/SaveWorkspaceDialog.cs`) is a named, `IComparable` preset that
  bundles up to **four independently-toggleable groups** (`[Flags] WorkspaceType`): `WindowLayout`
  (form bounds / state / fullscreen / minimal-GUI), `ViewsSetup` (the `ComicExplorerViewSettings`
  for the database + file browsers, i.e. selected list, columns, sort, group), `ComicPageLayout`
  (`BookPageLayout` portrait/landscape, RTL, transition effect), `ComicPageDisplay` (background
  colour/mode, paper texture, page margin, realistic-pages). `SaveWorkspaceDialog` is just a name
  field + four checkboxes picking which groups to capture; `OK` is gated on a non-empty name and at
  least one group checked.
- CE ships **zero built-in workspaces** — `InitializeDefaultLists()` seeds default *Smart Lists*
  (`Recently Added`, `Recently Read`, `Reading`, `Never Read`, …), never a `DisplayWorkspace`. The
  workspace list starts empty and the user builds every entry.
- Applying a workspace is **one-shot** (`IDisplayWorkspace.SetWorkspace`) — it pushes settings into
  the live UI and there is no dirty-tracking or auto-write-back; you re-capture by saving again
  over the same name (`StoreWorkspace`).
- Workspaces are **global**, not per-view, in CE — but CE's window shows the grid and the list-tree
  side by side, so one workspace legitimately spans "which list + how it's laid out + how the
  reader looks" at once. Paperbunkr's full-screen one-screen-at-a-time shell doesn't have that
  simultaneity.

**What ports and what deviates** (all confirmed with the user, see Scope):

| CE | Paperbunkr |
| --- | --- |
| One global workspace list, 4 toggleable capture-groups | **Per-screen** lists (Library, Books), no capture-group toggles — a workspace always captures that screen's full persisted state |
| `WindowLayout` / `ComicPageLayout` / `ComicPageDisplay` groups | **Out of scope.** Window state isn't a "workspace" concept in this shell; reader display/layout is already driven by per-series/per-issue overrides + `ContentType` (a better model than a manual workspace toggle) |
| `ViewsSetup` (list + columns + sort + group) | **In scope** — this is exactly the Saved List Layouts field set |
| One-shot apply, re-save to update | **Same** |
| Ships no built-ins | **Ships a few read-only starters** (deliberate deviation — the user wants ready-made shelves; CE users just never got them) |

## Scope

A **Workspace** is a named snapshot of everything one browsing screen already auto-persists to
`AppSettings`. Two screens get the feature, each with its **own independent list**:

- **Library** — captures the full Saved-List-Layouts set: `LibraryGranularity`, `LibrarySortField`
  + `LibrarySortDirection`, `LibraryGroupField`, `LibraryIssueListSortField` +
  `LibraryIssueListSortDirection` + `LibraryIssueListGroupField`, `LibraryViewMode`,
  `LibraryGridDensity`, `LibraryShowTileTitles`, the five overlay/badge toggles
  (`LibraryShowUnreadBadge`, `LibraryShowPublisherBadge`, `LibraryShowLanguageBadge`,
  `LibraryUseLanguageIcon`, `LibraryShowContinueReadingButton`), `LibrarySearchQuery` +
  `LibrarySearchMode`, `LibraryActiveContentType` / `LibraryActiveCollectionId` (the sidebar
  selection), the three filter checkboxes (`LibraryFilterUnreadOnly`, `LibraryFilterMissingIssues`,
  `LibraryFilterTrackedOnly`), and `LibraryDetailsColumns`.
- **Books** — captures `BooksSortField` + `BooksSortDirection` + `BooksGroupField`. (Books
  deliberately never persists its search text — a workspace doesn't capture it either.)

This is the exact field set `LibraryScreenViewModel.LoadLibrarySettings()` /
`SaveLibrarySettings()` already round-trip, and the equivalent three fields on
`BooksScreenViewModel`. **A workspace is those fields and nothing more** — no new state is invented.

**Captured in full, including "what's shown", not just "arrangement"** (user's explicit choice):
applying a Library workspace repopulates the search box, search mode, sidebar selection, and filter
checkboxes exactly as they were when saved. A workspace named "Manga" that was saved with
`LibraryActiveContentType = Manga` re-selects that sidebar entry on apply. This is full parity with
what List Layouts already persists — a workspace is just a named alternative to "the one current
state".

**One-shot apply.** Selecting a workspace writes its stored values into the live view, then you are
editing live state again — which continues to auto-persist through `SaveLibrarySettings()` as
today. There is **no dirty dot, no auto-save back into the workspace, no "revert"**.

**Re-saving (implemented deviation from the first draft of this spec):** there is no separate
`Update "<name>"` row. Instead, **"Save current view as…"** with the name of an existing *user*
workspace overwrites that workspace's captured state in place — exactly what CE's
`SaveWorkspaceDialog` does. A first draft here had an `Update` row gated on "a user workspace is
active", but the active id clears on the very first governed change (see the label rule below), so
`Update` would never be reachable with actual changes to fold in. Overwrite-by-name is both
simpler and pure CE.

**Active-workspace label.** The toolbar dropdown shows the last-applied workspace's name as its
label until any governed field changes, then it falls back to a neutral **"Workspace"** label. This
is not dirty-tracking — it's one nullable id (`AppSettings.LibraryActiveWorkspaceId` /
`BooksActiveWorkspaceId`) set on apply and cleared to `null` by the existing `SaveLibrarySettings()`
write path whenever it runs for a reason other than an apply. The label is cosmetic; nothing
branches on it.

**Restore on launch.** Unchanged from today — the raw last state is restored from `AppSettings`
(the `LibraryActiveWorkspaceId` is restored too, purely so the label is right if the user hasn't
touched anything since). No "reopen last workspace" step, consistent with the one-shot model.

**Not "Saved Searches".** `SearchSuggestionKind.SavedSearch` already exists in the search box and
means "a `Collection`, selected by id" — a different concept (a persistent membership query).
Workspaces don't touch it; a workspace that captured a collection sidebar selection just stores
that `Collection.Id` in `LibraryActiveCollectionId` like any other saved state.

### Explicitly out of scope

- Reader display / page-layout / window-state capture (CE's other three `WorkspaceType` groups).
- Per-capture-group toggles (CE's `SaveWorkspaceDialog` checkboxes) — a workspace is always the
  whole screen state.
- One workspace spanning both screens — the user chose per-screen lists.
- Sharing / import / export of workspaces (`.crpck`-style). Not asked for; each is its own feature.
- Workspaces on any other screen (Smart, Reading, Events).

## Data model

One new entity, `Workspace` (`Paperbunkr.Data/Entities/Workspace.cs`), plus a migration.

```csharp
public class Workspace
{
    public int Id { get; set; }

    /// <summary>Which screen's list this belongs to. A screen only ever loads its own rows.</summary>
    public WorkspaceScreen Screen { get; set; }   // Library | Books

    public string Name { get; set; } = "";

    /// <summary>Manual display order within the screen's list; user-reorderable. Built-ins get 0..n first.</summary>
    public int SortOrder { get; set; }

    /// <summary>Seeded starter (see below). Read-only in the UI: apply or duplicate, never edit/rename/delete.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// System.Text.Json object holding exactly the AppSettings fields this screen persists (see Scope).
    /// A superset/subset on read is tolerated: unknown keys ignored, missing keys fall back to the
    /// app default for that field — same posture as every other nullable JSON blob in AppSettings
    /// (LibraryRecentSearches, LibraryDetailsColumns).
    /// </summary>
    public string StateJson { get; set; } = "{}";
}
```

`WorkspaceScreen` is a new enum in `Paperbunkr.Data.Entities` (`Library = 0`, `Books = 1`),
stored as an int, same convention as every other enum column here.

**Why a JSON blob, not typed columns:** the captured set is already 25+ Library fields and will
grow every time List Layouts grows. Mirroring each as a `Workspace` column doubles the schema
churn — every future "Library now also persists X" spec would need a second migration here. The
blob's shape is defined by two records that live next to the ViewModels that own the fields:

```csharp
// Paperbunkr.App/Models/WorkspaceState.cs
public sealed record LibraryWorkspaceState(
    LibraryContentGranularity Granularity,
    LibrarySortField SortField, SortDirection SortDirection,
    LibraryGroupField GroupField,
    IssueListSortField IssueListSortField, SortDirection IssueListSortDirection,
    IssueListGroupField IssueListGroupField,
    LibraryViewMode ViewMode, double GridDensity, bool ShowTileTitles,
    bool ShowUnreadBadge, bool ShowPublisherBadge, bool ShowLanguageBadge,
    bool UseLanguageIcon, bool ShowContinueReadingButton,
    string? SearchQuery, SearchMode SearchMode,
    ContentType? ActiveContentType, int? ActiveCollectionId,
    bool FilterUnreadOnly, bool FilterMissingIssues, bool FilterTrackedOnly,
    string? DetailsColumns);

public sealed record BooksWorkspaceState(
    BooksSortField SortField, SortDirection SortDirection, BooksGroupField GroupField);
```

Serialization uses the app's existing `System.Text.Json` defaults (enums as strings, matching
`LibraryRecentSearches`). A record with a defaulted constructor parameter for every field means an
old blob missing a newly-added field deserializes cleanly to that field's default.

### Storage layer

New `WorkspaceService` (`Paperbunkr.App/Services/WorkspaceService.cs`), same shape as
`KeyBindingService` — a `Func<PaperbunkrDbContext>` factory ctor with a test seam, opens its own
context per call, no DI:

```csharp
public class WorkspaceService
{
    public IReadOnlyList<Workspace> List(WorkspaceScreen screen);          // ordered by IsBuiltIn desc, then SortOrder, then Id
    public Workspace Create(WorkspaceScreen screen, string name, string stateJson);
    public void UpdateState(int id, string stateJson);                    // no-op if IsBuiltIn
    public void Rename(int id, string name);                              // no-op if IsBuiltIn
    public void Delete(int id);                                           // no-op if IsBuiltIn
    public void Reorder(WorkspaceScreen screen, IReadOnlyList<int> orderedIds);
}
```

`IsBuiltIn` guards live in the service, not just the UI, so a stale command or a test can't mutate
a starter.

### Built-in starters

Seeded once by a `WorkspaceService.EnsureBuiltInsSeeded()` call from `MainViewModel`'s startup path
(same place `BackupService.RunAutoBackupIfDue` and the content-type sweep already hook in), keyed
on a stable `(Screen, Name)` pair so re-running is idempotent and a user deleting nothing/renaming
their own copies is unaffected. If a user finds a built-in useless they ignore it; there's no
hide/disable (YAGNI — revisit if asked).

**Library (3):**

`ViewMode` is `PosterGrid` for every Library built-in — it's the only view mode backed by a
virtualizing panel (`VirtualizingWrapPanel`); List / Details / Tiles / Comic-List all realize
every row and choke a multi-thousand-issue library. A "Recently added" starter was dropped:
PosterGrid already sorts by date-added-descending by default (so it was identical to "All
comics"), and a Details-view variant spiked memory on large libraries. Non-PosterGrid views on
big libraries need virtualization work of their own — tracked separately, out of scope here.

| Name | State (every unlisted field = app default) |
| --- | --- |
| All comics | `ViewMode=PosterGrid`, `Granularity=Issue`, `IssueListSortField=Added` / `Descending`, `IssueListGroupField=None`, no filters, no sidebar selection. (Equals the out-of-box defaults — doubles as "reset to default view".) |
| Currently reading | `ViewMode=PosterGrid`, `Granularity=Issue`, `FilterUnreadOnly=true`, `IssueListSortField=Opened` / `Descending` (`IssueListSortField` has no `LastRead` member; `Opened` = last-opened time is the closest). (CE `DefaultReadingList` precedent.) |
| Manga | `ViewMode=PosterGrid`, `Granularity=Series`, `ActiveContentType=Manga`, `LibrarySortField=Name` / `Ascending`. |

**Books (3):**

| Name | State |
| --- | --- |
| All books | `BooksSortField=Title` / `Ascending`, `BooksGroupField=None`. |
| Recently added | `BooksSortField=RecentlyAdded` / `Descending`, `BooksGroupField=None`. |
| By series | `BooksGroupField=Series`, `BooksSortField=Title` / `Ascending`. |

(Enum members verified against `IssueListSortField.cs` / `LibrarySortField.cs` / `BooksSortField.cs`
as of this spec — `Added`, `Opened`, `Name`, `Title`, `RecentlyAdded`, `Series` all exist.)

## Apply / save mechanics

All three flows are thin methods on the screen ViewModel, reusing the existing settings seam:

- **Apply** (`ApplyWorkspaceCommand(int id)`): deserialize `StateJson` → write each value into the
  backing field the same way `LoadLibrarySettings()` does (direct `_field` writes under
  `#pragma warning disable MVVMTK0034`, to avoid each setter re-triggering
  `LoadFromDatabase`/`SaveLibrarySettings` mid-apply), then raise `OnPropertyChanged` for the lot,
  set `_activeWorkspaceId = id`, then one `LoadFromDatabase()` (the sidebar
  selection/collection-membership may have changed, so a full reload not just `RebuildView()`),
  then `SaveLibrarySettings()` (which now also writes `LibraryActiveWorkspaceId = id`). Stale
  `ActiveCollectionId` gets the **existing** "collection deleted → fall back to All Series"
  treatment in `LoadLibrarySettings` — reused verbatim, not re-implemented.
- **Save current as** (`SaveWorkspaceAsCommand`): opens a small naming overlay (the
  `NewReadingListOverlay` / `NewReadingListViewModel` pattern — a single validated text field,
  Save/Cancel, `MainViewModel` hosts it like the other overlays). On confirm: build the state
  record from the **current live field values** (a `CaptureState()` helper — the read half of the
  same field list), `JsonSerializer.Serialize`, `WorkspaceService.Create`, then set it active.
- **Update "&lt;name&gt;"** (`UpdateActiveWorkspaceCommand`): `CaptureState()` → `Serialize` →
  `WorkspaceService.UpdateState(_activeWorkspaceId, json)`. Only shown when a non-built-in
  workspace is active.
- **Rename / Delete / Reorder**: `WorkspaceService` calls + refresh the dropdown's list. Rename
  reuses the same naming overlay pre-filled. Delete of the active workspace clears
  `_activeWorkspaceId` (label falls back to "Workspace"); the live view is untouched (one-shot —
  deleting the recipe doesn't un-cook the meal).
- **Reset to default view** (`ResetToDefaultViewCommand`): applies the built-in **All comics** /
  **All books** state without going through "a workspace is active" — `_activeWorkspaceId` ends up
  set to that built-in's id, which is correct (you *are* now on "All comics").

`CaptureState()` and the apply-writer share one ordered field list per screen, defined once, so
"List Layouts gained a field" is a one-line change in three places (the record, the capture, the
apply) with a compiler error at each if you miss one.

## UI wiring

### Library toolbar (`LibraryToolbar.axaml`)

A new pill on Row 1, inserted **before** the search box (`Grid.Column` shifts by one; it's the
first thing after the Back/Forward `browseBtn`s):

```xml
<Button Grid.Column="1" x:Name="WorkspaceButton" Classes="toolbarPill" Classes.open="{Binding IsWorkspaceOpen}"
        Command="{Binding ToggleWorkspaceCommand}" VerticalAlignment="Center"
        AutomationProperties.AutomationId="LibraryWorkspaceButton"
        AutomationProperties.Name="{Binding ActiveWorkspaceLabel, StringFormat='Workspace: {0}'}">
    <StackPanel Orientation="Horizontal" Spacing="6">
        <fi:SymbolIcon Symbol="Apps" />
        <TextBlock Text="{Binding ActiveWorkspaceLabel}" />
        <fi:SymbolIcon Symbol="ChevronDown" />
    </StackPanel>
</Button>
```

`ActiveWorkspaceLabel` = the active workspace's `Name`, or `"Workspace"` when
`_activeWorkspaceId is null`.

Its popup is the established `Border.dropdown` + `modeOption` pattern (identical to the
`SearchModeButton` / Filter popups already in this file — light-dismiss, `VerticalOffset="6"`):

```
┌────────────────────────────────┐
│  WORKSPACES                    │   ← TextBlock.dropdownRow header
│  ● All comics                  │   ← modeOption, .active when _activeWorkspaceId matches
│  ● Currently reading           │
│  ● Manga                       │
│  ● My weekly pull    ▲ ▼ ✎ ✕   │   ← non-built-in rows carry inline move/rename/delete controls
│  ──────────────────────────    │
│  Save current view as…         │   ← reusing a user workspace's name overwrites it
│  Reset to default view         │
└────────────────────────────────┘
```

Rows come from an `ItemsControl` over `Workspaces` (an `ObservableCollection<WorkspaceRow>` record
on the ViewModel: `Id`, `Name`, `IsBuiltIn`, `IsActive`). Non-built-in rows carry an inline
`▲ ▼ ✎ ✕` control cluster (move up / move down / rename / delete) rather than a nested `⋯` popup —
`MenuFlyout`/nested-popup reliability in this Avalonia build is poor (see the context-menu-rebuild
work), and the flat cluster is directly FlaUI-testable. Rename opens the shared naming overlay
pre-filled. Reorder is those per-row ▲/▼ buttons (no drag — matches this project's
"no reorder-by-drag" precedent noted in the drag-and-drop spec's out-of-scope list); persists via
`WorkspaceService.Reorder`.

Naming overlay: new `WorkspaceNameOverlay.axaml` + `WorkspaceNameViewModel` modeled on
`NewReadingListOverlay` (validated non-empty, trimmed, Save/Cancel), hosted by `MainViewModel` with
an `IsWorkspaceNameOverlayOpen` flag exactly like `IsReadingListPropertiesOverlayOpen` et al. Used
for both "Save current as" and "Rename".

### Books toolbar (`BooksScreen.axaml`)

Same pill, placed before the existing `GroupButton` / `SortButton` pills (the `ColumnDefinitions`
`*,Auto,Auto` becomes `*,Auto,Auto,Auto`), bound to the Books ViewModel's mirror of the same
commands/collection. Books' `BooksScreenViewModel` grows the same small set of members
(`Workspaces`, `ActiveWorkspaceLabel`, `IsWorkspaceOpen`, the commands) — no shared base class,
just parallel code, consistent with how Library and Books already parallel each other rather than
sharing a base ViewModel.

### Keyboard

No new global hotkey (not asked for). The pill is reachable by Tab like every other toolbar
control. If a shortcut is wanted later it slots into `KeyboardCommandRegistry` as a normal
app-wide command — out of scope here.

## Edge cases

- **Empty non-built-in list** — the dropdown still shows the 4/3 built-ins + "Save current view
  as…" + "Reset to default view". "Update" and "Reorder" rows are hidden.
- **Applying the already-active workspace** — harmless: re-writes the same values, one reload. No
  special-casing.
- **A workspace captured a `SearchMode`/field that no longer exists** (enum member removed in a
  later version) — `System.Text.Json` throws on the unknown enum string; caught per-blob, that
  field falls back to default (same defensive posture as `DeserializeRecentSearches`). A wholly
  unparseable blob → the row is still listed but applying it is a no-op reload with a one-line
  `Diagnostics` log entry, never a crash.
- **Deleting the active workspace** — `_activeWorkspaceId → null`, label → "Workspace", live view
  unchanged.
- **Two workspaces with the same name** — allowed (no uniqueness constraint); the naming overlay
  doesn't block it. `Update "<name>"` / delete operate by id, not name, so it's unambiguous
  internally; only the label is potentially ambiguous, which is the user's own doing.
- **Built-in seeding races a fresh install with an empty DB** — `EnsureBuiltInsSeeded()` runs after
  `GetOrCreateAppSettings()` in the same startup sequence, single-threaded, before either toolbar
  renders. No lock needed.
- **Concurrent sessions** (two app instances, shared per-user DB — see the concurrent-sessions
  memory) — last write wins on a given `Workspace` row; `List()` re-reads on every dropdown open so
  a workspace created in the other instance shows up. No live cross-process notification (not worth
  it for this).

## Testing

- **`WorkspaceServiceTests`** (`Paperbunkr.Data.Tests` or `App.Tests`, no UI): create/list ordering
  (built-ins first, then `SortOrder`), `IsBuiltIn` guards reject `Rename`/`Delete`/`UpdateState`,
  `Reorder` renumbers, `EnsureBuiltInsSeeded` is idempotent and doesn't touch user rows.
- **`WorkspaceMigrationTests`** — the new table/columns exist, `has-pending-model-changes` clean,
  `database update` replays (the pattern every migration spec here already follows; watch the
  `PaperbunkrDbContextModelSnapshot` staleness gotcha noted in the 4b memory).
- **`LibraryWorkspaceTests`** (`App.Tests`, ViewModel-level with the existing context-factory
  seam): `CaptureState()` round-trips every field; `ApplyWorkspace` writes every field and sets
  `LibraryActiveWorkspaceId`; a governed field change afterwards clears it and the label reverts;
  applying a workspace whose `ActiveCollectionId` points at a deleted collection falls back to All
  Series (reuses the existing stale-collection test's setup); "Currently reading" built-in applies
  `FilterUnreadOnly=true` + `LastRead` sort.
- **`BooksWorkspaceTests`** — the three-field equivalent.
- **On-screen (`Paperbunkr.App.UiTests`, FlaUI)** — extends `LibraryToolbarDriver`: the
  `LibraryWorkspaceButton` label reads "Workspace" on a fresh profile; applying "Manga" from the
  dropdown changes the toolbar state and the label; restarting the app keeps the applied state and
  label (the same restart-survives assertion `LibraryListLayoutPersistenceTests` already makes).
  Reorder / rename / the naming overlay are covered at the ViewModel level only — same standing
  "OS-level overlay interaction isn't reliably FlaUI-automatable here" caveat as the other specs.

## Roadmap / docs updates on landing

- `docs/Paperbunkr-Roadmap.md` — mark Saved Workspaces shipped in the "Library browsing extras" section,
  with the deviations (per-screen not global; no reader/window capture; ships starters).
- `docs/ce-feature-inventory.md` §C — flip `Saved "Workspaces"` from "confirmed still not started"
  to shipped, noting the per-screen scope.
