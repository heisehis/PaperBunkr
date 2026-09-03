# Saved Workspaces — Implementation Plan

*Implements: docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md*

**Status (2026-09-03): Steps 1–9 implemented, uncommitted.** Data.Tests 712 green (incl. 2 new
migration tests); App.Tests green (incl. workspace + IssueList + Books + Library clusters re-run
after the perf pass) — one unrelated parallel-load flake
(`LiveFolderWatchServiceTests.Created_FileLockedThenReleased…`, passes in isolation, area untouched).
App smoke-launches clean (~116 MB). UiTests written + compile but **cannot execute in this
environment** — `AppFixture` launch hits a UIA-timeout that the pre-existing
`LibraryListLayoutPersistenceTests` also hits here, i.e. no interactive desktop.

Committed to branch `claude/library-saved-workspaces` (`65e2a22` feature, `37281e4` docs),
surgically split from the uncommitted Books-reader WebView work sharing this tree; verified in a
clean detached worktree. Step 10 done: `alpha-roadmap.md` + `ce-feature-inventory.md` §C updated
(Workspaces shipped, filesystem folder browsing dropped, Quick Open design-done).

**Deviations from the design (both reflected back into the design doc):**
1. No separate "Update &lt;name&gt;" row — "Save current view as…" overwrites when you reuse a user
   workspace's name (CE's own model); also makes restart-label-restore correct with no extra state.
2. **"Recently added" Library starter dropped**, and every Library starter is now `PosterGrid`.
   PosterGrid is the only view mode with a virtualizing panel; the original Details-view "Recently
   added" spiked memory to multiple GB on a 2000+ issue library (reported by the user). Root-cause
   fix — virtualizing the other view modes — is a separate spawned task, not this feature.
   `ApplyLibraryState` also now renders the issue list **once** (was up to 4×: new
   `IssueListScreenViewModel.ConfigureSortGroup` sets all three fields with no render; the
   DetailsColumns rebuild is skipped when unchanged; `OnPropertyChanged(string.Empty)` replaced by
   a targeted `RaiseAllToolbarBindings()` that never touches the row bindings).

Surveyed the real surface area first. Key facts the steps below rely on:

- `LibraryScreenViewModel` (`ViewModels/LibraryScreenViewModel.cs`, ~2780 lines) persists via
  `LoadLibrarySettings()` (direct `_field` writes under `#pragma warning disable MVVMTK0034`, then
  it lets `LoadFromDatabase()` build the view) and `SaveLibrarySettings()` (immediate write-back,
  swallows a transient SQLite lock). The stale-collection fallback lives in `LoadLibrarySettings`.
  Toolbar popups use a single-active-dropdown string (`ActiveDropdown` / `IsViewSortOpen` etc.);
  `[RelayCommand] ToggleViewSort()` is the pattern.
- `BooksScreenViewModel` mirrors this with `LoadBooksSettings()` / `SaveBooksSettings()` and the
  same `ActiveDropdown` mechanism (`"sort"` / `"group"`), persisting just
  `BooksSortField`/`BooksSortDirection`/`BooksGroupField`.
- Both VMs `new`-up `PaperbunkrDb.CreateContext()` inline; tests point that at a temp DB via the
  existing `DatabasePathOverride` / `PAPERBUNKR_DB_PATH` seam. `KeyBindingService` is the
  precedent for a small service with a `Func<PaperbunkrDbContext>` test seam.
- `MainViewModel` ctor builds `Home`, then `Library`, then `Books`, … (lines 69–71). Overlays are
  ~15 identical `[ObservableProperty] bool _isXOverlayOpen` + open/close `[RelayCommand]` pairs +
  a `<Border IsVisible="{Binding IsXOverlayOpen}" Background="#B0000000">` block in
  `MainWindow.axaml`, child styled `Border.floatingPanel`.
- Migrations: plain `migrationBuilder.AddColumn`/`CreateTable`; `PaperbunkrDb.EnsureCreated()` runs
  `Database.Migrate()` at startup. Watch the `PaperbunkrDbContextModelSnapshot` staleness gotcha
  (4b memory) — regenerate via `dotnet ef migrations add`, don't hand-edit.
- New-View gotcha (CLAUDE.md): any new `.axaml` ships with its `.axaml.cs` in the **same step**.

---

## Step 1: `Workspace` entity, enum, DbSet, `AppSettings` columns, migration

**Files:**
- `Paperbunkr.Data/Entities/Workspace.cs` (new)
- `Paperbunkr.Data/Entities/WorkspaceScreen.cs` (new — `enum { Library = 0, Books = 1 }`)
- `Paperbunkr.Data/Entities/AppSettings.cs` (edit — add `int? LibraryActiveWorkspaceId`,
  `int? BooksActiveWorkspaceId`, both nullable, XML-doc'd as cosmetic-label-only per the spec)
- `Paperbunkr.Data/PaperbunkrDbContext.cs` (edit — `public DbSet<Workspace> Workspaces => Set<Workspace>();`
  and an `OnModelCreating` `modelBuilder.Entity<Workspace>` block: `Name` required, an index on
  `(Screen, SortOrder)` for the ordered `List()` read)
- `Paperbunkr.Data/Migrations/*_AddWorkspaces.cs` (+`.Designer.cs`, + snapshot) — **generated** via
  `dotnet ef migrations add AddWorkspaces -p src/Paperbunkr.Data -s src/Paperbunkr.App`

`Workspace` fields exactly as the design's data-model section: `Id`, `Screen` (int-backed enum),
`Name`, `SortOrder`, `IsBuiltIn`, `StateJson` (default `"{}"`).

**Depends on:** none
**Verify:** `Paperbunkr.Data.Tests/AddWorkspacesMigrationTests.cs` (new) — table + the two
`AppSettings` columns exist after `database update`, `Down` drops cleanly; `dotnet ef
migrations has-pending-model-changes` is clean. Full `Paperbunkr.Data.Tests` run green.

---

## Step 2: `WorkspaceState` records + JSON helper

**Files:**
- `Paperbunkr.App/Models/WorkspaceState.cs` (new) — `LibraryWorkspaceState` and
  `BooksWorkspaceState` records (every ctor param defaulted so an old blob missing a field
  deserializes), plus a static `WorkspaceStateJson` with `Serialize<T>(T)` / `TryDeserialize<T>(string, out T)`
  using the same `JsonSerializerOptions` the codebase already uses for `LibraryRecentSearches`
  (enums as strings). `TryDeserialize` catches `JsonException` → returns the all-defaults record
  and logs one `DiagnosticsService` line (matches `DeserializeRecentSearches`' posture).

Field lists come verbatim from the design's "Data model" section — `LibraryWorkspaceState` is the
22-ish `LoadLibrarySettings` fields, `BooksWorkspaceState` is the 3 Books fields.

**Depends on:** none (records reference only `Paperbunkr.Data.Entities` enums, already referenced)
**Verify:** `Paperbunkr.App.Tests/WorkspaceStateTests.cs` (new) — round-trips every field; a blob
with an unknown extra key deserializes; a blob missing a key yields that field's default; garbage
string → all-defaults, no throw.

---

## Step 3: `WorkspaceService`

**Files:**
- `Paperbunkr.App/Services/WorkspaceService.cs` (new)

Shape mirrors `KeyBindingService`: default ctor → `PaperbunkrDb.CreateContext`, `internal`
test-seam ctor taking `Func<PaperbunkrDbContext>`. API from the design:
`List(screen)` (ordered `IsBuiltIn desc, SortOrder, Id`), `Create`, `UpdateState`, `Rename`,
`Delete`, `Reorder`, and `EnsureBuiltInsSeeded()`.

`EnsureBuiltInsSeeded()` — for each of the 7 starters (design's tables), key on `(Screen, Name)`;
insert only if absent, building `StateJson` from a hand-written `LibraryWorkspaceState` /
`BooksWorkspaceState` via `WorkspaceStateJson.Serialize`. `IsBuiltIn = true`, `SortOrder` = its
index in the starter list. Idempotent: re-running touches nothing.

`UpdateState` / `Rename` / `Delete` early-return (no-op) when the target row's `IsBuiltIn`.

**Depends on:** Steps 1, 2
**Verify:** `Paperbunkr.App.Tests/WorkspaceServiceTests.cs` (new) — `List` ordering; `Create` then
`List` shows it after built-ins; `IsBuiltIn` guards reject mutate calls; `Reorder` renumbers only
non-built-ins as given; `EnsureBuiltInsSeeded` twice = same rows, and doesn't disturb a
user-created row or a user's identically-named row (guarded — seeding checks `IsBuiltIn == false`
existing rows don't block, but a built-in with that name already existing does).

---

## Step 4: Library VM — workspace state, label, commands

**Files:**
- `Paperbunkr.App/Models/WorkspaceRow.cs` (new) — small `record`/observable
  (`Id`, `Name`, `IsBuiltIn`, `IsActive`) for the popup `ItemsControl`
- `Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit)

VM changes:
- Ctor: two new optional params — `Action<string?, Action<string>>? promptForName = null`
  (opens the naming overlay with initial text, calls back with the entered name) and
  `WorkspaceService? workspaceService = null` (defaults to `new()`).
- `ObservableCollection<WorkspaceRow> Workspaces`; `RefreshWorkspaces()` re-populates from
  `_workspaceService.List(WorkspaceScreen.Library)`, marking `IsActive` by `_activeWorkspaceId`.
- `int? _activeWorkspaceId` seeded in `LoadLibrarySettings()` from
  `settings.LibraryActiveWorkspaceId`; `string ActiveWorkspaceLabel =>` the active row's `Name`
  or `"Workspace"`.
- `bool IsWorkspaceOpen => ActiveDropdown == "workspace"`; add to `OnActiveDropdownChanged`'s
  raise-list; `[RelayCommand] ToggleWorkspace()`.
- `LibraryWorkspaceState CaptureLibraryState()` — reads the current property values.
- `void ApplyLibraryState(LibraryWorkspaceState s, int workspaceId)` — sets `_isApplyingWorkspace = true`;
  writes every backing field the `LoadLibrarySettings` way (direct, `#pragma` off), reusing the
  **existing** stale-collection check for `ActiveCollectionId`; raises `OnPropertyChanged` for the
  touched properties; `_activeWorkspaceId = workspaceId`; `LoadFromDatabase()`; `SaveLibrarySettings()`;
  `_isApplyingWorkspace = false`; `RefreshWorkspaces()`.
- `SaveLibrarySettings()` — also writes `settings.LibraryActiveWorkspaceId = _activeWorkspaceId`;
  **and**, when `!_isApplyingWorkspace`, first sets `_activeWorkspaceId = null` (any governed
  change drops the label back to "Workspace"). Then `RefreshWorkspaces()` if the id changed.
- Commands: `ApplyWorkspace(int id)`, `SaveWorkspaceAs()` (→ `promptForName(null, name => { var w =
  _workspaceService.Create(Library, name, Serialize(CaptureLibraryState())); ApplyWorkspace(w.Id); })`),
  `UpdateActiveWorkspace()` (guarded to a non-built-in active id), `RenameWorkspace(int id)`
  (→ `promptForName(currentName, …)`), `DeleteWorkspace(int id)` (if it was active →
  `_activeWorkspaceId = null`, `SaveLibrarySettings()`), `ReorderWorkspaces(IReadOnlyList<int>)`,
  `ResetToDefaultView()` (→ apply the "All comics" built-in by id).
- `RefreshWorkspaces()` called once at the end of the ctor (after `LoadFromDatabase()`).

**Depends on:** Step 3
**Verify:** `Paperbunkr.App.Tests/LibraryWorkspaceTests.cs` (new) — `CaptureLibraryState` →
serialize → `ApplyLibraryState` round-trips every field; apply sets `LibraryActiveWorkspaceId` in
the DB and the label; a subsequent `SortField` change clears both; apply of a workspace whose
`ActiveCollectionId` points at a since-deleted collection falls back to All Series (reuse the
existing stale-collection test fixture); the "Currently reading" built-in applies
`FilterUnreadOnly` + `Opened`/`Descending`. Full `Paperbunkr.App.Tests` green (existing
`LibraryScreenViewModelTests` constructor calls still compile — new params are optional).

---

## Step 5: Library toolbar XAML — workspace pill + popup

**Files:**
- `Paperbunkr.App/Views/LibraryToolbar.axaml` (edit)

Per `avalonia-pro-max/components` (popup + light-dismiss, `AutomationProperties.Name`/`AutomationId`
on every interactive control — also required by the FlaUI driver in Step 9):
- Row 1 `Grid` `ColumnDefinitions` gains one leading `Auto`; existing columns shift +1. New
  `Button x:Name="WorkspaceButton" Classes="toolbarPill" Classes.open="{Binding IsWorkspaceOpen}"`
  before the search box, content = `SymbolIcon` + `{Binding ActiveWorkspaceLabel}` + chevron.
- New `<Popup PlacementTarget="{Binding #WorkspaceButton}" … IsOpen="{Binding IsWorkspaceOpen}"
  IsLightDismissEnabled="True">` with a `Border.dropdown`: a `dropdownRow` "Workspaces" header, an
  `ItemsControl` over `Workspaces` (row = `modeOption` `Classes.active="{Binding IsActive}"`,
  `Command` → `ApplyWorkspaceCommand`, `CommandParameter="{Binding Id}"`; non-built-in rows carry a
  trailing `⋯` `Button` opening a nested mini-popup with Rename/Delete — same nesting as the
  existing `AddToListButton` popup), a separator, then `modeOption` buttons: "Save current view
  as…", "Update &lt;name&gt;" (`IsVisible` when a non-built-in is active), "Reorder…" (`IsVisible`
  when >1 non-built-in), "Reset to default view".
- Reorder UI: a minimal modal list with ▲/▼ per row (no drag — matches project precedent). Can be
  its own tiny `Popup` off the same button, or fold into Step 8's overlay if simpler during
  implementation.

**Depends on:** Step 4
**Verify:** `dotnet build` clean (heed the CLAUDE.md XAML-weave gotcha — if `CompileAvaloniaXaml`
fails after `CoreCompile`, delete `obj/Debug/net10.0/Paperbunkr.App.dll` + `.pdb` and rebuild).
Visual pass deferred to Step 9.

---

## Step 6: Books VM — workspace state, label, commands

**Files:**
- `Paperbunkr.App/ViewModels/BooksScreenViewModel.cs` (edit)

Parallel to Step 4 but 3 fields: ctor gains `promptForName` + `workspaceService`; `Workspaces`,
`_activeWorkspaceId` (from `settings.BooksActiveWorkspaceId`), `ActiveWorkspaceLabel`,
`IsWorkspaceOpen` (`ActiveDropdown == "workspace"`), `ToggleWorkspace`, `CaptureBooksState()` /
`ApplyBooksState()`, and the same command set. `SaveBooksSettings()` writes
`BooksActiveWorkspaceId` and clears `_activeWorkspaceId` on a non-apply change. No stale-reference
concern (Books state has no entity ids). No shared base class — parallel code, per the design.

**Depends on:** Step 3
**Verify:** `Paperbunkr.App.Tests/BooksWorkspaceTests.cs` (new) — 3-field round-trip; apply sets +
a later sort change clears `BooksActiveWorkspaceId`; the "By series" built-in applies
`GroupField=Series`.

---

## Step 7: Books toolbar XAML — workspace pill + popup

**Files:**
- `Paperbunkr.App/Views/BooksScreen.axaml` (edit)

Same pill + popup as Step 5, placed before the existing `GroupButton`/`SortButton` pills; the
`ColumnDefinitions="*,Auto,Auto"` becomes `"*,Auto,Auto,Auto"`. Bound to the Books VM's mirror
members.

**Depends on:** Step 6
**Verify:** `dotnet build` clean; visual pass in Step 9.

---

## Step 8: Naming overlay + `MainViewModel` wiring

**Files:**
- `Paperbunkr.App/ViewModels/WorkspaceNameViewModel.cs` (new) — `Name` `[ObservableProperty]`,
  `CanSave => !string.IsNullOrWhiteSpace(Name)`, `[RelayCommand] Save()` → `_onConfirm(Name.Trim())`,
  `[RelayCommand] Cancel()` → `_onCancel()`, plus `Begin(string? initial, Action<string> onConfirm)`.
- `Paperbunkr.App/Views/WorkspaceNameOverlay.axaml` **+ `WorkspaceNameOverlay.axaml.cs`** (new,
  same step — AVLN2000 gotcha) — `Border.floatingPanel`, a title, one `TextBox` (`Text="{Binding
  Name}"`, autofocus), Cancel / Save buttons; `AutomationId`s on the box + Save.
- `Paperbunkr.App/ViewModels/MainViewModel.cs` (edit):
  - First line of ctor: `new WorkspaceService().EnsureBuiltInsSeeded();` (before `Library`/`Books`
    are constructed so their first `List()` sees the starters).
  - `WorkspaceName = new WorkspaceNameViewModel(CloseWorkspaceNameOverlay)`.
  - `[ObservableProperty] bool _isWorkspaceNameOverlayOpen;` + `[RelayCommand] CloseWorkspaceNameOverlay()`.
  - `PromptWorkspaceName(string? initial, Action<string> onName)` → `WorkspaceName.Begin(initial, n
    => { onName(n); IsWorkspaceNameOverlayOpen = false; }); IsWorkspaceNameOverlayOpen = true;`
  - Pass `PromptWorkspaceName` as the `promptForName` arg into both `new LibraryScreenViewModel(…)`
    and `new BooksScreenViewModel(…)`.
- `Paperbunkr.App/Views/MainWindow.axaml` (edit) — one more
  `<Border IsVisible="{Binding IsWorkspaceNameOverlayOpen}" Background="#B0000000">` block hosting
  `<views:WorkspaceNameOverlay DataContext="{Binding WorkspaceName}" />` + the standard close
  button, alongside the existing overlay blocks.

**Depends on:** Steps 4, 6
**Verify:** `Paperbunkr.App.Tests/WorkspaceNameViewModelTests.cs` (new) — `CanSave` gating,
trim-on-save, callback fires once. App launches (run the exe, per CLAUDE.md — "0 Errors" alone
isn't proof the XAML weave ran). `Paperbunkr.App.Tests` full run green.

---

## Step 9: UiTests — FlaUI apply + restart-survives

**Files:**
- `Paperbunkr.App.UiTests/LibraryToolbarDriver.cs` (edit) — accessors for `LibraryWorkspaceButton`,
  the popup rows, "Save current view as…"
- `Paperbunkr.App.UiTests/LibraryWorkspaceTests.cs` (new)

Cases: fresh profile → `LibraryWorkspaceButton` label reads "Workspace"; open the dropdown, the 4
Library built-ins are listed; click "Manga" → toolbar granularity/label change; **restart the app
(the isolated-DB `PAPERBUNKR_DB_PATH` mechanism `LibraryListLayoutPersistenceTests` already uses)**
→ state + label persist; change the sort → label reverts to "Workspace".

**Depends on:** Steps 5, 8
**Verify:** `dotnet test Paperbunkr.App.UiTests`. Per the live-folder-watch and 4b memories, FlaUI
runs can be flaky / need an interactive desktop in some environments — if the existing
`HomeScreenTests` baseline fails the same way, note it and fall back to the ViewModel-level
coverage from Steps 4/6 as the real gate, exactly as prior specs did.

---

## Step 10: Docs

**Files:**
- `docs/alpha-roadmap.md` (edit) — "Library browsing extras" section: mark Saved Workspaces
  shipped, note the deviations (per-screen not global; no reader/window capture; ships 7 starters).
  Also record that **filesystem folder browsing was dropped** from the bundle by decision.
- `docs/ce-feature-inventory.md` §C (edit) — flip `Saved "Workspaces"` to shipped (per-screen
  scope); flip `Filesystem folder browsing mode` to won't-do.

**Depends on:** Steps 1–9 landed
**Verify:** n/a (docs). Follow CLAUDE.md's "update `docs/alpha-todo.md` by hand" rule — state what
was verified, not just what the commits claim.

---

## Test strategy summary

- **Unit / ViewModel** (`Paperbunkr.App.Tests`, xUnit): Steps 2, 3, 4, 6, 8 — the real gate.
  Isolated DB via `DatabasePathOverride` / `PAPERBUNKR_DB_PATH`, same as every existing VM test.
- **Migration** (`Paperbunkr.Data.Tests`): Step 1 — table/columns exist, `Down` reverses,
  `has-pending-model-changes` clean.
- **On-screen** (`Paperbunkr.App.UiTests`, FlaUI/UIA3): Step 9 — restart-survives + apply. Treated
  as confirmatory; flakiness in this environment is a known, documented condition, not a blocker.
- No new test framework, fixture style, or live-network dependency introduced.
- Full-solution run (`dotnet test`) green before calling it done; current baselines ≈
  `Paperbunkr.App.Tests` 949+, `Paperbunkr.Data.Tests` 454+ (4b memory).
