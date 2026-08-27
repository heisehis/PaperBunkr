# Collections — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-27-collections-design.md*

Ordered so the schema lands first, then the service/resolver layer, then each UI surface
independently. Steps 6–10 depend only on 1–5 and can be done in any order relative to each other.

---

## Step 1: Rename `Category` → `Collection`, add fields, add `CollectionItem`
**Files:**
- `src/Paperbunkr.Data/Entities/Category.cs` → rename file/class to `Collection.cs` / `Collection` (edit)
- `src/Paperbunkr.Data/Entities/CollectionItem.cs` (new)
- `src/Paperbunkr.Data/Entities/Series.cs` (edit)
- `src/Paperbunkr.Data/Entities/Issue.cs` (edit)
- `src/Paperbunkr.Data/Entities/Book.cs` (edit)

**What:**
- `Collection`: keep `Id`, `Name`, `SortOrder`; add `string? Description`, `string? AccentColor`
  (hex `#RRGGBB`), `string? CoverImagePath`, `bool IsAutoCover` (default `true`). Drop the
  `List<Series> Series` navigation (replaced by `CollectionItem`).
- `CollectionItem` (new): `int Id`, `int CollectionId`, `Collection? Collection`, `int SortOrder`,
  `int? SeriesId`, `Series? Series`, `int? IssueId`, `Issue? Issue`, `int? BookId`, `Book? Book`.
  XML-doc the "exactly one of SeriesId/IssueId/BookId is non-null" invariant and the cross-schema
  `BookId` note from the design doc.
- `Series.Categories` (`List<Category>`) → remove; add `List<CollectionItem> CollectionItems = new()`.
- `Issue`: add `List<CollectionItem> CollectionItems = new()`.
- `Book`: add `List<CollectionItem> CollectionItems = new()`.

**Depends on:** none
**Verify:** compiles after Step 2; no standalone test.

---

## Step 2: DbContext config + migration
**Files:**
- `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit — `DbSet`, lines ~17, ~164–165, ~245–249)
- `src/Paperbunkr.Data/Migrations/<timestamp>_AddCollections.cs` + `.Designer.cs` (new, scaffolded)
- `src/Paperbunkr.Data/Migrations/PaperbunkrDbContextModelSnapshot.cs` (regenerated)

**What:**
- `DbSet<Category> Categories` → `DbSet<Collection> Collections`; add `DbSet<CollectionItem> CollectionItems`.
- Remove the `HasMany(s => s.Categories).WithMany(c => c.Series)` line (~164).
- Replace the `Entity<Category>` block with `Entity<Collection>` (keep `Name` required) and add an
  `Entity<CollectionItem>` block:
  - `HasKey(ci => ci.Id)`
  - three optional FKs, each `.OnDelete(DeleteBehavior.Cascade)`; `CollectionId` cascade
  - `ToTable(t => t.HasCheckConstraint("CK_CollectionItem_OneTarget",
    "((\"SeriesId\" IS NOT NULL) + (\"IssueId\" IS NOT NULL) + (\"BookId\" IS NOT NULL)) = 1"))`
  - three filtered unique indexes: `(CollectionId, SeriesId)` / `(CollectionId, IssueId)` /
    `(CollectionId, BookId)`, each `.HasFilter("\"<col>\" IS NOT NULL")`
- Scaffold the migration, then **hand-edit `Up`/`Down`** (per the EF-scaffolder-mistake gotcha in
  memory — do not trust the generated body blindly):
  1. `RenameTable` `Categories` → `Collections`; `AddColumn` `Description`, `AccentColor`,
     `CoverImagePath`, `IsAutoCover` (`defaultValue: true`).
  2. `CreateTable` `CollectionItems` with the check constraint + indexes.
  3. Data copy: `INSERT INTO CollectionItems (CollectionId, SeriesId, SortOrder) SELECT
     "CategoriesId", "SeriesId", (row_number over ...) FROM "CategorySeries"` (raw SQL — the join
     table has columns `CategoriesId`, `SeriesId` per the model snapshot). Then `DropTable
     CategorySeries`.
  4. `RenameColumn` `AppSettings.LibraryActiveCategoryId` → `LibraryActiveCollectionId`.
  `Down` reverses in order.

**Depends on:** Step 1
**Verify:** `dotnet build` clean; new `MigrationTests` case (Step 11) applies the migration on a DB
with a seeded `CategorySeries` row and asserts a `CollectionItem` row results + the settings column
is renamed.

---

## Step 3: `CollectionService`
**Files:**
- `src/Paperbunkr.Data/Collections/CollectionService.cs` (new) — or `Metadata/` if that's where
  sibling mutation services sit; match `ContinuityResolver`'s folder if no better home
- `src/Paperbunkr.App.Tests/CollectionServiceTests.cs` (new — see Step 11)

**What:** static class, `PaperbunkrDbContext`-in methods mirroring `ContinuityResolver`'s style
(each does its own `SaveChanges`):
- `Collection Create(context, string name)` — trims, appends `SortOrder = max + 1`.
- `void Rename(context, int collectionId, string name)`
- `void Delete(context, int collectionId)` — relies on cascade for items.
- `void Reorder(context, IReadOnlyList<int> orderedCollectionIds)`
- `void SetAppearance(context, int collectionId, string? description, string? accentColor,
  string? coverImagePath, bool isAutoCover)`
- `void AddItems(context, int collectionId, IEnumerable<int> seriesIds, IEnumerable<int> issueIds,
  IEnumerable<int> bookIds)` — idempotent (skip targets already present), append at end of the
  collection's item `SortOrder`.
- `void RemoveTargets(context, int collectionId, IEnumerable<int> seriesIds, issueIds, bookIds)`
  — the context-menu toggle-off path.
- `void RemoveItem(context, int collectionItemId)` — the overlay per-row remove.
- `void ReorderItems(context, int collectionId, IReadOnlyList<int> orderedCollectionItemIds)`
- Guard exactly-one-target in `AddItems` before insert so a bad call is a logged no-op, not an
  EF/`CHECK` exception.

**Depends on:** Steps 1–2
**Verify:** `CollectionServiceTests` (Step 11).

---

## Step 4: `CollectionResolver`
**Files:**
- `src/Paperbunkr.Data/Metadata/CollectionResolver.cs` (new, sibling of `ContinuityResolver.cs`)
- `src/Paperbunkr.App.Tests/CollectionResolverTests.cs` (new — see Step 11)

**What:** static, read-only:
- `IReadOnlyList<Collection> GetCollections(context, int seriesId)` — collections a series belongs
  to (via `CollectionItems`), ordered by `SortOrder`.
- `IReadOnlyList<CollectionMember> GetMembers(context, int collectionId)` — ordered mixed list;
  `CollectionMember` is a small new record (`CollectionItemId`, `Kind` enum Series/Issue/Book,
  `int TargetId`, `string DisplayTitle`, plus the loaded entity refs the UI needs).
- `IReadOnlyList<Series> GetOtherSeriesSharingCollection(context, int seriesId)` — mirrors
  `ContinuityResolver.GetOtherSeriesSharingContinuity` exactly (collect this series' collection
  ids, return other series with a `CollectionItem` in any of them, `Distinct().OrderBy(Name)`).
- `string? ResolveCover(context, Collection c)` — `c.CoverImagePath` when `!c.IsAutoCover` and the
  file exists; else first member's cover (`Series.CoverIssue` cover / `Issue` cover / `Book.CoverImagePath`).

**Depends on:** Steps 1–2
**Verify:** `CollectionResolverTests` (Step 11).

---

## Step 5: Rename the Library-side `Category` plumbing
**Files:**
- `src/Paperbunkr.App/Models/CategorySummary.cs` → `CollectionSummary.cs` / `CollectionSummary` (edit)
- `src/Paperbunkr.App/Models/LibraryBrowseState.cs` (edit — `ActiveCategoryId` → `ActiveCollectionId`)
- `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit — `_activeCategoryId` →
  `_activeCollectionId`; lines ~59, 95, 150, 182, 270–273, 315, 365–371, 478, 526–546, 718, 733,
  740–752)
- `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit — `LibraryActiveCategoryId` →
  `LibraryActiveCollectionId`, ~line 247; update the XML-doc referencing `Category`)
- `src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit — `CreateCategoryWithSeries`
  helper ~107–118 now builds a `Collection` + `CollectionItem`; rename the `LibraryActiveCategoryId`
  assertions ~1085–1296; `SelectCollection_PersistsActiveCategory...` test name/body)

**What:** pure rename + adjust the two membership queries:
- Sidebar summary build (~526): `context.Collections.Include(c => c.CollectionItems)` ordered by
  `SortOrder`; `Count` = total `CollectionItems.Count` (all three kinds); add `AccentColor` to the
  summary.
- Filter (~544): `filtered.Where(s => context.CollectionItems.Any(ci => ci.CollectionId ==
  collectionId && ci.SeriesId == s.Id))` — still series-only here; the mixed view is Step 9.
- Keep `LoadFromDatabase`'s `.Include(s => s.Categories)` → drop it (no longer a nav); membership
  now checked via `CollectionItems` query above.
- `CategorySummary`→`CollectionSummary` add `string? AccentColor`.

**Depends on:** Steps 1–2. Everything still series-only after this step; behaviour is unchanged
except the entity name.
**Verify:** existing `LibraryScreenViewModelTests` (renamed) stay green.

---

## Step 6: Sidebar COLLECTIONS section — create / rename / recolor / reorder / delete
**Files:**
- `src/Paperbunkr.App/Views/MainWindow.axaml` (edit — the `COLLECTIONS` block ~299–315)
- `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit — new commands)
- `src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit — new cases)

**What:**
- `+` button on the heading → `BeginCreateCollection` / inline `TextBox` bound to a
  `NewCollectionName` prop, Enter commits via `CollectionService.Create`, then `LoadFromDatabase`
  and auto-select.
- Per-row `ContextMenu`: **Rename** (inline edit → `RenameCollectionCommand`), **Edit…**
  (`OpenCollectionPropertiesCommand`, wired in Step 8), **Move Up**/**Move Down**
  (`MoveCollectionUp/DownCommand` → `CollectionService.Reorder`), **Delete** (two-step, reuse the
  `DeleteConfirmLabel` pattern already in this VM).
- Accent dot: `Border` background from `AccentColor` with a converter falling back to
  `PbAccentTextBrush` when null.
- `CollectionSummary` gains nothing more; commands take the summary as `CommandParameter`.

**Depends on:** Steps 3, 5
**Verify:** `LibraryScreenViewModelTests` — create adds a row; rename/reorder/delete reflected
after reload; delete removes membership rows (cascade).

---

## Step 7: `Add to Collection ▸` context submenu
**Files:**
- `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — series-tile menu ~144–205 and
  issue-tile menu ~255+)
- `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit)
- `src/Paperbunkr.App/Views/BooksScreen.axaml` (edit — add a tile `ContextMenu` if none exists)
- `src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs` (edit)
- `src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs`, `BooksScreenViewModelTests.cs` (edit)

**What:**
- Expose an `ObservableCollection<CollectionMenuOption>` (`Id`, `Name`, `bool IsChecked`) on each
  VM, rebuilt on load. `IsChecked` = all currently-targeted entities are members.
- `ToggleCollectionMembershipCommand` param `(collectionId)` — resolves the current selection
  (multi-select aware via the existing `TileSelectionController`), calls
  `CollectionService.AddItems` or `RemoveTargets` based on current membership, reloads.
- `New collection…` item → prompt (reuse the inline create) then add the selection.
- Library series tiles pass `SeriesId`; issue tiles pass `IssueId`; Books tiles pass `BookId`.

**Depends on:** Step 3
**Verify:** VM tests — toggling adds then removes; multi-select adds to all; `New collection…`
creates + adds.

---

## Step 8: `CollectionPropertiesOverlay`
**Files:**
- `src/Paperbunkr.App/Views/CollectionPropertiesOverlay.axaml` + `.axaml.cs` (new — **both in the
  same step**, per CLAUDE.md's new-View gotcha)
- `src/Paperbunkr.App/ViewModels/CollectionPropertiesScreenViewModel.cs` (new — clone
  `ReadingListPropertiesScreenViewModel`: two-ctor test seam, `Func<PaperbunkrDbContext>`,
  buffered Load/edit/Save/Cancel, `_pendingCoverImagePath`)
- `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — `IsCollectionPropertiesOverlayOpen`,
  `Open/CloseCollectionPropertiesOverlay`, add to the Esc-dismiss chain ~line 749, construct the
  VM ~line 57, pass the opener into `LibraryScreenViewModel`)
- `src/Paperbunkr.App/Views/MainWindow.axaml` (edit — add the backdrop `Border` + close button
  next to the Reading List overlay ~671)
- `src/Paperbunkr.App.Tests/CollectionPropertiesScreenViewModelTests.cs` (new)

**What:** fields Name, Description, Accent color (swatch buttons writing a hex string), Cover
(Auto ↔ Choose file via `FilePickerService`, buffered like the reading-list one). Reorderable
member list: `ObservableCollection<CollectionMemberRow>` with drag handles (reuse whatever
drag-reorder the reading-list editor uses, or Move Up/Down buttons if it has none), per-row
Remove, per-row type badge. Save → `CollectionService.SetAppearance` + `ReorderItems` +
per-removed-row `RemoveItem`. Close callback reloads Library.

**Depends on:** Steps 3, 4
**Verify:** VM test — Load populates from a seeded collection; Save persists appearance + new
order; Cancel discards.

---

## Step 9: Mixed collection view in the Library main area
**Files:**
- `src/Paperbunkr.App/Models/LibraryTile.cs` (new — or extend `SeriesCardSample`) — a unified tile
  the grid can render for Series / Issue / Book
- `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit — `LoadFromDatabase`
  ~539–590, `SortCards`/`GroupCards`, `HasAnyResults` ~465)
- `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — grid `DataTemplate`(s) for the new tile
  kinds; book/issue tiles route their click to `SelectCollectionMember`)
- `src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit)

**What:**
- When `_activeCollectionId` is set, build `Covers` from `CollectionResolver.GetMembers`
  projected to `LibraryTile` in `CollectionItem.SortOrder`, instead of the series-card path.
- Default sort in this mode = "Collection order" (a new `LibrarySortField` value or a VM-local
  flag — prefer the flag, it's display-only); other sorts still selectable and ignore the manual
  order.
- Series tile click → existing Detail nav; Issue tile → reader/Detail as issue tiles do elsewhere;
  Book tile → the Books screen's book-open path (extract a shared navigation callback via
  `MainViewModel`, same way `GoDetailForSeries` is passed in).
- Content-type sidebar counts + `All Series` stay on the unfiltered `series` list (unchanged).

**Depends on:** Steps 4, 5. This is the largest step — if scope needs trimming, this is the one to
cut (design doc §UI-D notes the fallback: collection just filters the existing series grid).
**Verify:** VM test — a mixed collection yields tiles of all three kinds in manual order; switching
sort re-sorts; leaving the collection restores the normal grid.

---

## Step 10: Detail "Related" tab — Same Collection group + chips
**Files:**
- `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit — mirror the Continuity block
  ~285–393; `LoadSeries` ~155–162 gains `RefreshCollections`)
- `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit — add the group + chip row alongside the
  continuity ones)
- `src/Paperbunkr.App/Models/` — `CollectionChip`, `CollectionSearchResult` (new, tiny; clone
  `ContinuityChip` / `ContinuitySearchResult`)
- `src/Paperbunkr.App.Tests/DetailTabsViewModelTests.cs` (edit)

**What:**
- `ObservableCollection<RelatedGroupSeriesSample> SameCollection` + `HasSameCollection`, populated
  from `CollectionResolver.GetOtherSeriesSharingCollection` with `Note = "Same collection"`.
- `ObservableCollection<CollectionChip> CollectionChips` from `CollectionResolver.GetCollections`.
- `ToggleAddCollection` / `CollectionSearchQuery` / `CollectionSearchResults` /
  `AddCollection` / `RemoveCollection` — byte-for-byte the Continuity picker shape, but
  `AddCollection` on a new name calls `CollectionService.Create` then `AddItems(…, series)`, and
  `RemoveCollection` calls `CollectionService.RemoveTargets(…, series)`.
- `RefreshCollections(context, seriesId)` called in `LoadSeries` next to `RefreshContinuity`.

**Depends on:** Steps 3, 4
**Verify:** `DetailTabsViewModelTests` — chips + group populate on load; add/remove refresh.

---

## Step 11: Tests
**Files (new):** `CollectionServiceTests.cs`, `CollectionResolverTests.cs`,
`CollectionPropertiesScreenViewModelTests.cs`; **(edit):** `LibraryScreenViewModelTests.cs`,
`BooksScreenViewModelTests.cs`, `DetailTabsViewModelTests.cs`, and the migration test file
(`Grep` for the existing migration-round-trip test; if none exists, add
`CollectionsMigrationTests.cs` following whatever DB-fixture pattern the `*ViewModelTests` use —
in-memory SQLite via `PaperbunkrDb` test seam).

**Coverage** (from design doc §Testing):
- Service: CRUD, reorder, `AddItems` multi-target + idempotency, toggle-off, exactly-one-FK guard,
  cascade on target-entity delete, `ReorderItems`.
- Resolver: `GetMembers` ordering; `GetOtherSeriesSharingCollection` dedup across two shared
  collections; `ResolveCover` manual / auto / empty / missing-file.
- Library VM: collection selection → mixed list; `LibraryActiveCollectionId` persistence (ported);
  mutual exclusivity with content-type; stale id falls back to All Series.
- Books VM: add-to-collection from a book tile.
- Detail VM: `SameCollection` + `CollectionChips` populate + refresh.
- Migration: `Category`→`Collection` rename + `CategorySeries` row copy into `CollectionItem` +
  `AppSettings` column rename all preserve data.

**Depends on:** the step each suite covers.
**Verify:** `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj` all green.
UiTests are flaky in this environment (per memory) — not relied on here.

---

## Step 12: Docs
**Files:** `docs/alpha-todo.md`, `docs/ce-feature-inventory.md` (§C), `CLAUDE.md` (only if a new
gotcha surfaces), memory (`project_paperbunkr_*` — new file + `MEMORY.md` pointer).

**What:** record Collections as shipped, note the deferred follow-on specs (smart collections,
recommendation reason, MediaRelation edges, home feed), and any build/EF gotcha hit during Step 2.

**Depends on:** everything landed + verified on-screen.
