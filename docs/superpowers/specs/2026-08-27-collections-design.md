# Collections — build, curate, and relate

*Date: 2026-08-27. Brainstormed from: "lets add collections, the ability to build collections and
add those collections to the relationship models."*

## Problem

The sidebar already shows a `COLLECTIONS` section wired to the `Category` entity (`Id`, `Name`,
`SortOrder`, M:M → `Series`), and selecting one filters the Library. But **nothing in the app
creates, renames, deletes, or assigns membership** — the code comments say as much ("empty today
since nothing creates them yet; that's Beta-scoped"). This spec makes collections a real,
buildable feature and wires them into the Detail screen's relationship surface.

CE has **no** collection/category concept — `Category` was already a documented Mihon-inspired
deviation (`docs/onboarding.md` §6, §15: "stays flat"). This is deliberate non-parity, already
sanctioned; there is nothing to verify against `_reference/ComicRackCE`.

## Scope

**In scope (this spec):**

1. Rename the `Category` entity to `Collection`.
2. Collection CRUD: create, rename, delete, reorder, plus appearance (description, accent color,
   manual/auto cover).
3. Cross-type manual membership — a collection holds any mix of `Series`, standalone `Issue`, and
   `Book` (novel) members, in a user-defined order.
4. Management UI: sidebar `+`/context menu, an `Add to Collection ▸` context submenu on Library
   and Books tiles, a `CollectionPropertiesOverlay`, and add/remove chips on the Detail "Related"
   tab.
5. A mixed collection view in the Library main content area.
6. Detail "Related" tab: an "Also in this collection" group + removable collection chips, mirroring
   the existing Continuity treatment.

**Deferred — each gets its own follow-on spec:**

- ~~**Smart collections**~~ — done. Shipped 2026-08-30 per
  `docs/superpowers/specs/2026-08-30-smart-collections-design.md`: `SmartList` gained a
  `TargetKind` (Issue/Series/Novel), `Collection` gained three optional rule slots, and
  `CollectionResolver.GetMembers` unions manual + rule-matched membership. Scope grew beyond the
  one-line note below during brainstorming (user chose the larger option each time) — see that
  spec for the final shape, not this note.
- ~~`RecommendationReason.SameCollection` + `RecommendationResolver` wiring.~~ — done (found
  already shipped, 2026-08-30 audit): `RecommendationResolver` has a `CollectionScore`/
  `CollectionWeight` (0.15) signal off `CollectionResolver.GetOtherSeriesSharingCollection`, and
  `DescribeDominantSignal` returns `SameCollection` when it dominates. Surfaces today in Home's
  "Because You Read" and Detail's "More Like This". This bullet had gone stale — it shipped at
  some earlier point without this doc being updated.
- ~~A Home-feed shelf for collections (`HomeFeedResolver`).~~ — done (found already shipped,
  2026-08-30 audit): `HomeFeedResolver.GetHomeCollections` + `HomeScreenViewModel.Collections`/
  `HasCollections` + a full `HomeScreen.axaml` "Collections" shelf + `HomeCollectionCard` are all
  wired end-to-end. Same staleness as above. One small gap noted in passing: the masthead
  cover-wall (`BuildMastheadBackdrop`) doesn't pull from `Collections`, only from
  RecentlyAdded/BecauseYouRead/ContinueReading/SpotlightItems — not itself part of this deferred
  item's original scope, just an observation.
- ~~Typed `MediaRelation` edges involving collections (collections as nodes in the pairwise
  relation graph).~~ — done. Shipped 2026-08-30 per docs/superpowers/specs/2026-08-30-media-
  relation-collection-nodes-design.md: `MediaRelation` gained nullable dual-FK pairs (Series or
  Collection per side, exactly one via a `CHECK`), Collection↔Collection rejected in favor of
  `CollectionRelation`, `MediaRelationResolver` gained mixed-kind `GetRelatedFromSeries`/
  `GetRelatedFromCollection`, `IMetadataGraph` gained 3 additive overloads, the Series Detail
  "Related" rail and a new Collection-editor section both support the mixed edge. This closes out
  every item this doc originally deferred.

## Architecture

### Data model — polymorphic `CollectionItem` join

```
Collection      { Id, Name, SortOrder, Description?, AccentColor?, CoverImagePath?, IsAutoCover }
CollectionItem  { Id, CollectionId, SortOrder, SeriesId?, IssueId?, BookId? }   -- exactly one target set
```

Chosen over three separate M:M joins (`CollectionSeries` / `CollectionIssue` / `CollectionBook`)
because a single manually-ordered *mixed* list is the core requirement: with three tables, the
manual `SortOrder` has to be spread across all three and every read needs a 3-way merge, and every
operation (count, add, remove, filter, "also in this collection") triples. The polymorphic join is
one table, one ordered query, one code path.

**Cross-schema note:** `CollectionItem.BookId` is the first FK from the library-org layer into the
`Book` schema. The "no FK crossing between the two schemas" rule in
`2026-08-09-novels-epub-pdf-support-design.md` was specifically about `Issue` ↔ `Book` (so neither
comic nor novel reading code has to know about the other). `Collection` is an org-layer entity
that deliberately spans both; this crossing is acceptable and is recorded here so a future reader
doesn't treat it as a mistake.

### Entities (`Paperbunkr.Data/Entities/`)

| Entity | Fields | Notes |
|---|---|---|
| `Collection` (rename of `Category`) | `Id`, `Name`, `SortOrder`, `Description?`, `AccentColor?`, `CoverImagePath?`, `IsAutoCover` (default `true`) | `AccentColor` is a nullable hex string (`#RRGGBB`). `CoverImagePath` is honoured only when `IsAutoCover == false`. |
| `CollectionItem` (new) | `Id`, `CollectionId`, `SortOrder`, `SeriesId?`, `IssueId?`, `BookId?` | All three target FKs `ON DELETE CASCADE`; `CollectionId` cascade. |

Navigation changes:

- `Series.Categories` (`List<Category>`) → removed. Series membership is reached via
  `CollectionItem` (`Series.CollectionItems`, `List<CollectionItem>`). A `[NotMapped]` convenience
  `IEnumerable<Collection> Collections => CollectionItems.Select(i => i.Collection)` may be added if
  call sites want it — the implementation plan decides based on actual usage.
- `Issue` gains `List<CollectionItem> CollectionItems`.
- `Book` gains `List<CollectionItem> CollectionItems`.

### `PaperbunkrDbContext`

- `DbSet<Category> Categories` → `DbSet<Collection> Collections`.
- Add `DbSet<CollectionItem> CollectionItems`.
- Model config on `CollectionItem`:
  - `CHECK ((SeriesId IS NOT NULL) + (IssueId IS NOT NULL) + (BookId IS NOT NULL) = 1)` — exactly
    one target.
  - Three filtered unique indexes: `(CollectionId, SeriesId) WHERE SeriesId IS NOT NULL`, and the
    same for `IssueId`, `BookId` — blocks duplicate membership.
  - Filtered non-unique indexes on each target FK for reverse lookups.

### Migration `AddCollections`

1. `RenameTable` `Category` → `Collection`; `AddColumn` `Description`, `AccentColor`,
   `CoverImagePath`, `IsAutoCover` (default `1`).
2. `CreateTable` `CollectionItem`.
3. Copy existing implicit `CategorySeries` skip-nav join rows → `CollectionItem` rows (`SeriesId`
   set, `SortOrder` = ascending by existing row order, grouped per collection), then `DropTable`
   `CategorySeries`. In practice zero rows exist (nothing created categories), but the migration
   is written to handle a non-empty table.
4. `RenameColumn` `AppSettings.LibraryActiveCategoryId` → `LibraryActiveCollectionId`.

### Service layer — `CollectionService`

New service following the existing service pattern (`Paperbunkr.Data`, or an App service — plan
decides based on where sibling CRUD services live). Methods:

- `Create(name)` → `Collection` (appends at end of `SortOrder`).
- `Rename(collectionId, name)`.
- `Delete(collectionId)` — cascade removes its `CollectionItem` rows.
- `Reorder(orderedCollectionIds)`.
- `SetAppearance(collectionId, description, accentColor, coverImagePath, isAutoCover)`.
- `AddItems(collectionId, seriesIds, issueIds, bookIds)` — multi-select, idempotent (respects the
  unique indexes), appends at end of the collection's item `SortOrder`.
- `RemoveItem(collectionItemId)` — or `RemoveTargets(collectionId, seriesIds, issueIds, bookIds)`
  for the toggle-off path from the context submenu.
- `ReorderItems(collectionId, orderedCollectionItemIds)`.

### Resolver — `CollectionResolver` (`Paperbunkr.Data/Metadata/`)

Sibling of `ContinuityResolver`. Pure query helpers, no mutation:

- `GetMembers(context, collectionId)` → ordered `IReadOnlyList` of a small discriminated result
  (`kind` ∈ Series/Issue/Book + the loaded entity), sorted by `CollectionItem.SortOrder`.
- `GetOtherMembersSharingCollection(context, seriesId)` → other **series** that share ≥1 collection
  with the given series, deduped across collections, for the Detail "Related" tab group. Mirrors
  `ContinuityResolver.GetOtherSeriesSharingContinuity`.
- `ResolveCover(context, collection)` → `collection.CoverImagePath` when `!IsAutoCover`, else the
  first member's cover (`Series.CoverIssue`, `Issue` own cover, or `Book.CoverImagePath`).
  Computed on read, never stored.

## UI

### A. Sidebar `COLLECTIONS` section (`MainWindow.axaml` ~line 299)

- `+` button on the heading row → inline "New collection" text entry; on commit calls
  `CollectionService.Create` and selects the new collection.
- Each row: accent-color dot (falls back to the current generic dot when `AccentColor` is null) +
  name + member count. `Classes.active` bound to `_activeCollectionId`.
- Per-row right-click context menu: **Rename** (inline), **Edit…** (opens the overlay),
  **Move Up** / **Move Down** (`Reorder`), **Delete** (two-step confirm, reusing the
  `DeleteConfirmLabel` pattern already in the Library tile context menu).
- `CategorySummary` model → `CollectionSummary` (`Id`, `Name`, `Count`, `AccentColor?`,
  `IsActive`). `Count` is total member count across all three types.
- `Library.HasCollections` / `SelectCollectionCommand` keep their current shape; the "No
  collections yet." placeholder stays.

### B. `Add to Collection ▸` context submenu

- Added to: Library series tiles, Library standalone-issue tiles, and Books tiles.
- Submenu items: every collection (with a checkmark when the clicked target — or, on multi-select,
  *all* selected targets — are already members), a separator, then **New collection…** (creates,
  then adds).
- Toggle semantics: clicking a fully-present collection removes the target(s); otherwise adds.
- New commands on `LibraryScreenViewModel` and `BooksScreenViewModel`, both delegating to
  `CollectionService`. Selection-aware via the existing multi-select controllers.

### C. `CollectionPropertiesOverlay` (new)

Clones `ReadingListPropertiesOverlay` end to end:

- New `CollectionPropertiesOverlay.axaml` + `.axaml.cs` (code-behind added in the same step — see
  CLAUDE.md "adding a new Avalonia View" gotcha).
- Hosted in `MainWindow.axaml` behind `IsCollectionPropertiesOverlayOpen`, with
  open/close/Esc-dismiss plumbed through `MainViewModel` exactly like
  `IsReadingListPropertiesOverlayOpen` (including the dismiss chain at `MainViewModel.cs:749`).
- New `CollectionPropertiesScreenViewModel` (mirrors `ReadingListPropertiesScreenViewModel`).
- Fields: Name, Description, Accent color (swatch picker), Cover (Auto ↔ Choose file… toggle
  using the existing `FilePickerService`).
- Reorderable member list: drag handles, per-row remove, per-row type badge (Series / Issue /
  Book). This is the surface for `ReorderItems` and per-item `RemoveItem`.

### D. Mixed collection view (`LibraryScreen` main content)

- When `_activeCollectionId` is set, the main grid renders one mixed, manually-ordered list:
  series cards, standalone-issue tiles, and book tiles, ordered by `CollectionItem.SortOrder`.
- `LibraryScreenViewModel` gains a collection-items projection path. It already builds
  `Covers`/`Groups` for series; this adds `Issue` and `Book` tile projection into the same
  `ObservableCollection<SeriesCardSample>` (or a shared base tile type — plan decides). **Rendering
  `Book` tiles inside the Library grid is new capability and is the single largest chunk of this
  spec.**
- Sidebar `CONTENT TYPE` counts and `All Series` remain driven by the unfiltered library
  (unchanged). Collection selection stays mutually exclusive with the content-type filter, exactly
  as `_activeCategoryId` is today.
- Sort/group toolbar: in collection view the default sort is "Collection order"; the other sort
  fields remain selectable and simply ignore `CollectionItem.SortOrder`.
- `AppSettings.LibraryActiveCollectionId` persists the selection (renamed column; the existing
  `LibraryActiveCategoryId` persistence tests port over directly).

### E. Detail "Related" tab (`DetailTabsViewModel`)

Additive alongside `Related` / `SameContinuity` / `SameEvent`, mirroring the Continuity code:

- `SameCollection` `ObservableCollection<RelatedGroupSeriesSample>` — from
  `CollectionResolver.GetOtherMembersSharingCollection`.
- `CollectionChips` `ObservableCollection<CollectionChip>` — this series' own collection
  memberships, each removable, plus an "add to collection" affordance reusing the same picker as
  the context submenu.
- `RefreshCollections(context, seriesId)` called from `LoadSeries`, next to `RefreshContinuity`.
- Only **series** membership is editable here (Detail is series-centric). Issue/Book membership is
  managed from their tiles and the overlay.

## Error handling

- All `CollectionService` mutations are single-`SaveChanges` units; add/remove are idempotent so a
  double-click or a re-issued command is harmless.
- The exactly-one-target `CHECK` plus the unique indexes are the backstop; the service also guards
  in code so a violation surfaces as a caught, logged no-op rather than an EF exception bubbling
  to the UI.
- Deleting a `Series` / `Issue` / `Book` cascades its `CollectionItem` rows; collection member
  counts recompute on the next sidebar refresh (same refresh path as today).
- `ResolveCover` tolerates a missing manual cover file (falls back to auto) and an empty
  collection (no cover).

## Testing

- **`CollectionServiceTests`** — create/rename/delete/reorder; `AddItems` multi-target and
  idempotency; toggle-off removal; exactly-one-FK guard; cascade when a target entity is deleted;
  `ReorderItems`.
- **`CollectionResolverTests`** — `GetMembers` returns the mixed set in `SortOrder`;
  `GetOtherMembersSharingCollection` dedupes a series that shares two collections;
  `ResolveCover` manual/auto/empty/missing-file paths.
- **`LibraryScreenViewModelTests`** — selecting a collection filters the main list to its mixed
  members; `LibraryActiveCollectionId` persistence (ported from the `LibraryActiveCategoryId`
  tests at lines ~1066–1267); mutual exclusivity with the content-type filter; a stale/nonexistent
  persisted id is ignored.
- **`BooksScreenViewModelTests`** — `Add to Collection` from a book tile.
- **`DetailTabsViewModelTests`** — `SameCollection` and `CollectionChips` populate on load and
  refresh after add/remove.
- **Migration round-trip test** — `Category`→`Collection` rename, `CategorySeries` row copy into
  `CollectionItem`, and the `AppSettings` column rename all preserve data.

## Roadmap

Update `docs/alpha-todo.md` and `docs/ce-feature-inventory.md` §C (Library browsing) once landed —
collections are a Library-org feature not currently tracked as its own row there. Note in the
inventory that the rule-based/smart-collection layer and the recommendation/home-feed wiring are
tracked as follow-on specs.
