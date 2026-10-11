# List Layouts — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-04-list-layouts-design.md*

**Status: all nine steps done 2026-10-04; starter layouts added 2026-10-05 (design section 15).**

Decisions taken while surveying the code (they refine the design, they do not change its scope):

- **No debounce.** `SaveLibrarySettings` already runs on every governed change with an immediate write (the screen's stated
  no-debounce philosophy). The per-list assignment is written from that same hook, only when the captured layout JSON differs from the
  last one applied or saved for the current list. A column drag-resize commits its width once, on release.
- **The default layout is a stored row**, key `*` in `ListLayoutAssignment`, captured from the current state the first time a screen
  view model is constructed after this ships. `AppSettings` keeps mirroring the *current* list's state (so restart, Workspaces and a
  downgrade behave as before), which means it cannot double as the fallback once lists differ. "Default" in the menu deletes the current
  list's row and applies `*`; "Set on all lists" rewrites `*` and every existing row.
- **Books layouts are `BooksWorkspaceState` JSON** (sort, direction, group); Library layouts are `ListLayoutState` JSON. The service stores
  strings and knows neither.
- **Selection keys.** Library: `all`, `content:<ContentType>`, `collection:<id>`. Books has no sidebar selection: `all`.
- **Captions** `null` = today's built-in two lines (series/name + number/sub), so an unconfigured list renders exactly as before.
  `HideCaptions` is the existing `ShowTileTitles` toggle, inverted; no new property.
- **Tile elements** `null` = today's two lines. `TileTextElement`: `Title`, `Series`, `Summary` (series tiles' "Comic · 6 issues" line)
  plus catalog-backed fields rendered through `IssueListFieldCatalog`'s `Display` (series tiles use `RepresentativeRow`).

## Step 1: Data
**Files:** `Paperbunkr.Data/Entities/ListLayout.cs` (new), `ListLayoutAssignment.cs` (new), `PaperbunkrDbContext.cs` (edit), migration `AddListLayouts` (new, `dotnet ef migrations add`).
**What:** two tables; unique (Screen, Name) and (Screen, SelectionKey); `Down` drops only these two tables.
**Verify:** `ListLayoutServiceTests` (EnsureCreated) + a migration up test through the existing migration test fixture if one covers "all migrations apply".

## Step 2: State + service
**Files:** `Paperbunkr.App/Models/ListLayoutState.cs` (new: `ListLayoutColumn`, `ListLayoutState`, `TileTextElement`, `ListLayoutStateJson`), `Paperbunkr.App/Services/ListLayoutService.cs` (new).
**What:** tolerant JSON (reuses `WorkspaceStateJson` options); service CRUD mirroring `WorkspaceService` (ctor seam, own context per call).
**Verify:** `ListLayoutStateTests`, `ListLayoutServiceTests`.

## Step 3: Shared layout controller
**Files:** `Paperbunkr.App/ViewModels/ListLayoutsViewModel.cs` (new: `IListLayoutHost`, rows, commands, assignment tracking).
**What:** one instance per screen. `OnSelectionChanged()` applies the list's assignment or `*`; `TrackChange()` writes the assignment when the
captured JSON changed; `SaveLayoutAs`, `ApplyLayout`, `ResetToDefault`, `Rename`, `Delete`, `Move`, `SetOnCurrentList`, `SetOnAllLists`.
**Depends on:** Step 2. **Verify:** `ListLayoutsViewModelTests` with a fake host.

## Step 4: Library view model
**Files:** `Models/DetailsColumn.cs` (observable `Width`), `ViewModels/LibraryScreenViewModel.ListLayouts.cs` (new partial), `LibraryScreenViewModel.cs` (hooks: ctor, `SaveLibrarySettings`, the three `Select*` commands, browse history, `ApplyLibraryState`, poster title height, deleted collection).
**What:** capture/apply, `MoveDetailsColumn`, `SetDetailsColumnWidth`, caption fields, tile elements, `IListLayoutHost`.
**Depends on:** Step 3. **Verify:** `LibraryListLayoutTests`; the existing Library/Workspace suites stay green.

## Step 5: Books view model
**Files:** `ViewModels/BooksScreenViewModel.cs` (edit). **What:** host implementation over sort/group; hook in `SaveBooksSettings`.
**Verify:** `BooksListLayoutTests`.

## Step 6: Overlays
**Files:** `ViewModels/ListOptionsViewModel.cs`, `ViewModels/EditLayoutsViewModel.cs`, `Views/ListOptionsOverlay.axaml(.cs)`, `Views/EditLayoutsOverlay.axaml(.cs)` (all new, code-behind added in the same step as each `.axaml`), `ViewModels/MainViewModel.cs`, `Views/MainWindow.axaml` (shells, Escape, `IsEditorOverlayOpen`).
**What:** List Options (Library: Details / Thumbnails / Tiles; Books: Sort & group) editing a draft, Apply/OK/Cancel; Edit Layouts (rename, delete, move, set on current, set on all with confirm).
**Governed by:** `avalonia-pro-max/components`, `layout-patterns`, `accessibility`; `review-checklist` before done.
**Verify:** `ListOptionsViewModelTests`, `EditLayoutsViewModelTests`, a headless view test that both overlays load.

## Step 7: Menu, templates, header drag
**Files:** `Views/LibraryToolbar.axaml`, `Views/BooksScreen.axaml` (Workspace popup section), `Views/LibraryScreen.axaml` (caption lines, tile lines, header resize grip), `Views/LibraryScreen.axaml.cs` (resize + reorder drag), `Views/ListLayoutConverters.cs` (new).
**Verify:** headless view tests; drag feel is on-screen only.

## Step 8: Input actions
**Files:** `Services/Input/InputActions.cs` (+ ids), `Views/LibraryScreen.axaml.cs`, `Views/BooksScreen.axaml.cs`.
**What:** `ListOptions` Ctrl+L, `EditListLayouts` Ctrl+Alt+L, `SaveListLayout` unbound; conflict check against the catalog.
**Verify:** existing keymap conflict tests + one keyboard test.

## Step 9: Docs
**Files:** `docs/paperbunkr-todo.md`, `docs/ce-feature-inventory.md`, the design doc (debounce and default-row notes), memory.

## Test strategy
Fast set only (`--filter "Speed!=Slow"`), targeted classes while iterating, one `dotnet` process at a time. On-screen checks (drag feel,
overlay look in both skins, restart keeping a per-list layout) are listed as outstanding, not claimed.
