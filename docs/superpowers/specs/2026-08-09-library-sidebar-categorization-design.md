# Library Sidebar Categorization — Design Spec

*Date: 2026-08-09. Scope: `LibraryScreenViewModel` and the Library sidebar block in
`MainWindow.axaml` only. Not building category *creation* UI — explicitly deferred to Beta
(user's own scoping), same as `docs/alpha-roadmap.md`'s existing Virtual Tags/metadata-driven
auto-categorization notes.*

## 1. Problem

Every row in the Library sidebar (`All Series`, `Collections`, `Comic`, `Manga`) is a plain
`Border` with no `Command`/click handling at all — 100% decorative. Three further findings:

- `LibraryScreenViewModel.ComicCount`/`MangaCount` only cover 2 of the 5 `ContentType` enum values
  (`Comic`/`Manga`/`Manhua`/`Manhwa`/`Unknown`). A library with series in the other 3 buckets has no
  sidebar representation for them at all.
- `"Collections"` shows a hardcoded `"12"`. But `Category` (`Paperbunkr.Data.Entities.Category`) is
  a real, fully-migrated schema entity — flat, many-to-many with `Series`, "Mihon-style" per its own
  doc comment — that nothing in the App layer reads or writes. It's genuinely empty in every real
  install today, not fake-but-hidden data.
- `Button.sideItemButton`/`TextBlock.sideItemLabel` already exist in `MainWindow.axaml`, built
  specifically for "real Smart List selection... styled to match `Border.sideItem`'s look" per their
  own comment — i.e. built to be visually identical to the Library sidebar's current dead rows,
  ready to reuse.

## 2. Fix

**`LibraryScreenViewModel`:**
- Remove `ComicCount`/`MangaCount`. Add `ObservableCollection<ContentTypeSummary> ContentTypes` and
  `ObservableCollection<CategorySummary> Collections`, both rebuilt every `LoadFromDatabase()` call
  (same "clear + rebuild from a fresh query" convention `SmartScreenViewModel.RefreshSidebar()`
  already uses — no incremental-update bookkeeping).
- `LoadFromDatabase()`: query `context.Series.Include(s => s.Issues).Include(s => s.Categories)`,
  group by `ContentType` (only emitting buckets with `Count > 0`, ordered by enum value — `Name` is
  just `ContentType.ToString()`, the enum names are already good labels). Separately query
  `context.Categories.Include(c => c.Series).OrderBy(c => c.SortOrder)` for `Collections` (`Count`
  from `category.Series.Count`).
- Add `private ContentType? _activeContentType` / `private int? _activeCategoryId` (mutually
  exclusive; both `null` means "All Series"). `Covers` is filtered by whichever is set, applied
  after the summaries are built, in the same query scope.
- `public bool IsAllSeriesActive => _activeContentType is null && _activeCategoryId is null;` and
  `public bool HasCollections => Collections.Count > 0;` — both raised manually via
  `OnPropertyChanged` at the end of `LoadFromDatabase()` (same pattern as `SmartScreenViewModel
  .HasResults` — Avalonia's compiled-binding `!` negation needs a real `bool`, and `ObservableCollection`
  doesn't raise change notifications for a derived property).
- Three new `[RelayCommand]`s - `SelectAllSeries()`, `SelectContentType(ContentTypeSummary?)`,
  `SelectCollection(CategorySummary?)` - each set the active-filter field(s) and call
  `LoadFromDatabase()` again. Re-querying on every sidebar click (rather than caching the last
  series list) matches this codebase's existing convention of hitting the DB fresh per user action,
  and is trivially fast at real library sizes.

**New models** (`Models/ContentTypeSummary.cs`, `Models/CategorySummary.cs`), matching
`SmartListSummary`'s existing shape (plain `init`-only properties, no `ObservableObject` - the
sidebar collection is always rebuilt wholesale, never mutated in place):
```csharp
public class ContentTypeSummary
{
    public required ContentType ContentType { get; init; }
    public required string Name { get; init; }
    public int Count { get; init; }
    public bool IsActive { get; init; }
}

public class CategorySummary
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int Count { get; init; }
    public bool IsActive { get; init; }
}
```

**`MainWindow.axaml`:**
- Convert all 4 Library sidebar rows from `Border Classes="sideItem"` to
  `Button Classes="sideItemButton"` (reusing the existing style, not a new one).
- `All Series` → `Command="{Binding Library.SelectAllSeriesCommand}"`,
  `Classes.active="{Binding Library.IsAllSeriesActive}"`.
- `Comic`/`Manga` rows → one `ItemsControl` over `Library.ContentTypes`, each row's `Command` bound
  through the same `#ElementName.((vm:MainViewModel)DataContext)...` indirection the Smart Lists
  sidebar already uses (`x:Name="LibraryContentTypes"`).
- `Collections` row → a `"COLLECTIONS"` section (new `TextBlock Classes="sideHeading"`, above the
  existing `"CONTENT TYPE"` heading) containing an `ItemsControl` over `Library.Collections`
  (`x:Name="LibraryCollections"`), plus a muted `"No collections yet."` `TextBlock` shown when
  `!Library.HasCollections` — same explicit-empty-state pattern as the Plugin screen and Smart
  Lists results.
- Remove the now-unused `Border.sideItem`/`Border.sideItem.active` style block (dead once all 4
  Library rows are Buttons).

## 3. Explicitly not doing

- **Creating/renaming/deleting categories.** No UI to add a category, assign a series to one, or
  manage `Category` rows at all — that's the Beta-scoped "add manually new and personal categories"
  work. This pass only makes the *display and filter* side of an already-real (if currently empty)
  entity honest and functional.
- **The toolbar's own "Filter ▾" dropdown** (Unread only/Missing issues/Tracked series) — separate,
  already-queued sub-project.
- **Auto-populating categories from metadata/Smart-List rules.** Explicitly the Beta-scoped
  "automatic" half of the categories feature per the user's own framing.

## 4. Testing

- `LibraryScreenViewModelTests` (check whether this file exists first; extend or create matching
  the DB-override pattern other ViewModel tests use):
  `LoadFromDatabase_GroupsContentTypes_SkippingEmptyBuckets`,
  `LoadFromDatabase_Collections_ReflectsRealCategoryRows` (create 1-2 `Category` rows with member
  series directly via EF, assert `Collections` reflects them - proves the plumbing, since no UI
  creates categories yet), `SelectContentType_FiltersCovers_AndSetsIsActive`,
  `SelectCollection_FiltersCovers_AndSetsIsActive`, `SelectAllSeries_ClearsFilter_RestoresFullCovers`.
- Manual: open Library, confirm Comic/Manga (and any other real content types present) show
  correct, non-hardcoded counts; click one, confirm the grid filters and the row highlights; click
  "All Series" to clear it; confirm "Collections" shows the real empty state.
