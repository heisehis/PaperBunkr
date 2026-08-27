# Books Screen Chrome + Home "Continue Reading — Books" — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-27-books-screen-chrome-and-home-strip-design.md*

**Base branch:** `books/browse-chrome`, off `books/restyle` (PR #17). PR #17 must merge before this
work lands on `master`; develop against `books/restyle` in the meantime.

## Surveyed shapes

- **`Book`** (`Paperbunkr.Data/Entities/Book.cs`): `Id`, `BookSeriesId?`/`BookSeries?`, `Title`,
  `Author?`, `Format` (`BookFormat` — `Epub`/`Pdf`), `FilePath`, `CoverImagePath?`, `Summary?`,
  `PublishedDate?`, `AddedTime`, `LastOpenedTime?`, `LastChapterIndex`, `LastCharacterOffset`,
  `Bookmarks`. No read/finished state.
- **`BookCardSample`** (`Paperbunkr.App/Models`): `BookId`, `Title`, `Author?`, `SeriesName?`,
  `HasSeries`, `Format`, `CoverBrush`, `CoverImage` (`Bitmap?` via `BookCoverImageCache.Get(id)`),
  `FromBook(Book)`.
- **`BooksScreenViewModel`** (post-PR #17): ctor `(Action<int,BookFormat> goReaderForBook, Action
  goLibrarySettings)`; `Books` (`ObservableCollection<BookCardSample>`), `HasBooks`,
  `LoadFromDatabase` (reads `context.Books.Include(b => b.BookSeries).OrderBy(series-then-title)`),
  `SelectBookCommand`, `OpenLibrarySettingsCommand`, `DeleteBookCommand`.
- **`BooksScreen.axaml`** (post-PR #17): `Grid RowDefinitions="Auto,*"` — header `Border`
  (`pbTextHeading` "Books" + `pbTextCaption`), then `ScrollViewer` with an `ItemsControl`/`WrapPanel`
  over `Books` (`IsVisible="{Binding HasBooks}"`) + a centered empty state (`!HasBooks`) with an
  "Open Library settings" `Button`. Local styles: `Button.card`, `Border.bookCover` (+ glow
  selectors).
- **Library chrome precedent** (`Views/LibraryToolbar.axaml`): `Button.toolbarPill` (+ `.open`),
  `Border.dropdown`, `Button.modeOption` (+ `.active`), single-`ActiveDropdown` string mechanism
  with `Toggle*` commands and `Is*Open` computed bools. `SortDirection` enum in `Data.Entities`.
  `LibrarySortField`/`LibraryGroupField` are `Data.Entities` enums, EF-configured
  `.HasConversion<string>().HasMaxLength(32).HasDefaultValue(x).HasSentinel(y)` in
  `PaperbunkrDbContext.cs:578+`.
- **`SeriesCardGroup`** (`Models`): `{ required string Header; required ObservableCollection<SeriesCardSample> Items }`.
  Library's grouped grid = an `ItemsControl` over `Groups`, each item a `StackPanel` with the 4a
  `DockPanel` header (`pbTextHeading` + count `TextBlock` + 1px rule) + a nested `ItemsControl`/
  `WrapPanel` over `Items`.
- **Home** (`HomeScreenViewModel` + `HomeScreen.axaml` + `HomeFeedResolver` +
  `Models/HomeContinueReadingCard`): the comic "Continue Reading" `StackPanel` = a `pbTextHeading`
  header (always shown) + a horizontal `ScrollViewer`/`ItemsControl` of `PosterTile`s over
  `ContinueReading`, gated `IsVisible="{Binding HasContinueReading}"`, with a
  "Nothing in progress yet" `emptyState` line when empty. `HomeFeedResolver.GetContinueReading`
  is `static ... (PaperbunkrDbContext, int limit = 10)`. `HomeScreenViewModel` ctor is
  `(Action<int> goDetailForSeries, Action<int> goReaderForIssue, Action<string> goLibraryWithSearch,
  Action<int,int> goReaderForIssueInReadingList)`; `LoadFromDatabase` maps resolver output → cards;
  `MainViewModel.cs:40` constructs it; `MainViewModel.GoBookReaderForBook(int bookId, BookFormat
  format)` (`:699`) already routes EPUB→text / PDF→page reader — reuse as the book-open callback.
- **`PosterTile`**: `CoverSource` (`IImage?`), `TitleText`, `MetaText`, `BadgeText`, `ShowProgress`
  (`bool`), `ProgressFraction` (`double` 0–1), `Command`, `CommandParameter`.
  `BookCoverImageConverter.Instance` (`Views/CoverImageConverter.cs`): `int bookId → Bitmap?`.
- **`BookReaderScreenViewModel`**: `LoadBook(int bookId)` opens `_source` (`EpubBookSource`/
  `PdfBookSource`), `_book = context.Books.Include(Bookmarks).Single(...)`, builds `TableOfContents`
  from `_source.Chapters`, sets `_book.LastOpenedTime`, `context.SaveChanges()` (`:123`–`:150`).
  `NextPage` terminal `else` (`:271`–`:275`): `_history.Pop(); return;` — the "already at end of
  book" branch, **before** the normal `RecomputeCurrentPage(); PersistPosition();`.
  `PersistPosition()` (`:481`): fresh context, load book by `_bookId`, set position fields + save.
- **Migration test pattern**: `src/Paperbunkr.Data.Tests/*MigrationTests.cs` — own temp `.db`,
  `context.GetService<IMigrator>().Migrate(prior)` → seed raw SQL → `.Migrate()` → assert; latest
  migration is currently `LibraryDetailsColumns` (from PR #17's ancestry — confirm the actual
  latest on the branch when scaffolding).
- **`BooksScreenViewModelTests`** exists (PR #17) — `DatabasePathOverride` + `EnsureCreated`,
  `CreateViewModel(goReader?, goLibrarySettings?)`, `AddBook(title, format, filePath)` helper.
  **No `HomeScreenViewModelTests` book coverage** and `HomeFeedResolverTests` exists for comics.

---

## Step 1: Schema — enums, `Book` + `AppSettings` columns, migration

**Files:** `src/Paperbunkr.Data/Entities/BooksSortField.cs` (new),
`src/Paperbunkr.Data/Entities/BooksGroupField.cs` (new),
`src/Paperbunkr.Data/Entities/Book.cs` (edit), `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit),
`src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit),
`src/Paperbunkr.Data/Migrations/<ts>_AddBooksBrowseState.cs` (+ `.Designer.cs`, snapshot) (new)

**What:**
- `enum BooksSortField { Title, Author, RecentlyAdded, LastOpened }` and
  `enum BooksGroupField { None, Series, Author }` in `Paperbunkr.Data.Entities`.
- `Book`: `public bool Finished { get; set; }`, `public int ChapterCount { get; set; }`.
- `AppSettings`: `BooksSortField BooksSortField { get; set; } = BooksSortField.Title;`
  `SortDirection BooksSortDirection { get; set; } = SortDirection.Ascending;`
  `BooksGroupField BooksGroupField { get; set; } = BooksGroupField.None;` — doc-comment each.
- `PaperbunkrDbContext` `AppSettings` config block: three `.HasConversion<string>().HasMaxLength(32)
  .HasDefaultValue(...).HasSentinel(...)` lines mirroring `LibrarySortField`/`LibrarySortDirection`/
  `LibraryGroupField` (sentinel == the CLR-default value for `BooksSortField.Title` /
  `BooksGroupField.None`; `BooksSortDirection` sentinel `Ascending`, default `Ascending` — keep it
  explicit for consistency). `Book.Finished` → `.HasDefaultValue(false)`; `Book.ChapterCount` →
  `.HasDefaultValue(0)` (the singleton-row backfill rationale doesn't apply to `Book` — no seeded
  rows — but keep defaults for a clean `ALTER TABLE ADD COLUMN`).
- `dotnet ef migrations add AddBooksBrowseState --project src/Paperbunkr.Data`. Expect five clean
  `AddColumn`s (2 on `Books`, 3 on `AppSettings`). Hand-review the `Up`/`Down` + snapshot diff (the
  scaffolder has emitted bad `RenameColumn`s on this repo before).

**Depends on:** none
**Verify:** `dotnet build src/Paperbunkr.Data`; `dotnet ef database update` clean against a scratch
DB; migration test in Step 6.

## Step 2: Book reader — `Finished` + `ChapterCount` wiring

**Files:** `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/BookReaderScreenViewModelTests.cs` (edit)

**What:**
- `LoadBook`: in the block that already has `context` + `_book` open (before the `:150`
  `context.SaveChanges()`), add:
  `_book.ChapterCount = _source.Chapters.Count;`
  `if (_book.Finished) _book.Finished = false;` (re-reading a finished book un-finishes it).
- `PersistPosition`: add a `bool markFinished = false` parameter; when true, `book.Finished = true`
  before `SaveChanges()`.
- `NextPage` terminal `else` (the `_history.Pop(); return;` branch): change to
  `_history.Pop(); PersistPosition(markFinished: true); return;` — no visible reader change, just
  records that the book was read to the end.

**Depends on:** Step 1
**Verify:** new `BookReaderScreenViewModelTests` cases —
- `LoadBook_PopulatesChapterCount` (open a seeded EPUB fixture, assert `context.Books.Find(id).ChapterCount > 0`);
- `LoadBook_ClearsFinished_WhenReopening` (seed `Finished = true`, load, assert cleared);
- `NextPage_AtEndOfBook_SetsFinished` (page to the last page, `NextPage()`, assert `Finished`).
  Reuse the existing EPUB test fixture the reader tests already use (`EpubBookSourceTests` /
  `BookReaderScreenViewModelTests` fixtures).

## Step 3: `BooksScreenViewModel` — chrome state + filtered/grouped projection

**Files:** `src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Models/BookCardSample.cs` (edit),
`src/Paperbunkr.App/Models/BookCardGroup.cs` (new),
`src/Paperbunkr.App.Tests/BooksScreenViewModelTests.cs` (edit)

**What:**
- `BookCardSample`: add `public bool Finished { get; init; }` (set in `FromBook` from `book.Finished`
  — not rendered in Piece A, but the projection carries it for Piece B / a future dim). Add
  `public DateTime AddedTime { get; init; }` and `public DateTime? LastOpenedTime { get; init; }`
  (needed for sort). Keep everything else.
- `BookCardGroup` (`Models`): `{ required string Header; required ObservableCollection<BookCardSample> Items }`
  (mirror `SeriesCardGroup`).
- `BooksScreenViewModel`:
  - `[ObservableProperty] string _searchQuery = ""`, `BooksSortField _sortField`,
    `SortDirection _sortDirection`, `BooksGroupField _groupField`; each `On*Changed` (except search)
    → `SaveBooksSettings(); Rebuild();`. Search `On*Changed` → `Rebuild()` only (no persist).
  - `[ObservableProperty] string? _activeDropdown` + `IsSortOpen`/`IsGroupOpen` computed +
    `OnActiveDropdownChanged` raising both + `[RelayCommand] ToggleSort`/`ToggleGroup` (same
    string-toggle shape as `LibraryScreenViewModel`).
  - `[RelayCommand] SetSortField(BooksSortField)`, `SetGroupField(BooksGroupField)`,
    `ToggleSortDirection()`. Computed labels `SortLabel` / `GroupLabel` (e.g. "Title", "Series") for
    the pill text.
  - `ObservableCollection<BookCardGroup> Groups { get; }`; `bool IsGrouped => GroupField != None`.
  - `LoadFromDatabase`: load `context.Books.Include(b => b.BookSeries)` into an in-memory
    `List<Book>` once; keep a private `List<BookCardSample> _allCards` (all books, unfiltered);
    then call `Rebuild()`.
  - `private void Rebuild()`: filter `_allCards` by `SearchQuery` (case-insensitive `Contains` over
    `Title` / `Author` / `SeriesName`); sort by `SortField` + `SortDirection`
    (`Title`→`Title`, `Author`→`Author ?? ""`, `RecentlyAdded`→`AddedTime`, `LastOpened`→
    `LastOpenedTime ?? DateTime.MinValue`); then if `!IsGrouped` fill `Books`, else partition into
    `Groups` (`Series` → `SeriesName ?? "Standalone"`, `Author` → `Author ?? "Unknown author"`;
    the "Standalone"/"Unknown" bucket sorts last; other groups order alphabetically for Title/Author
    sort, or by the group's newest `AddedTime`/`LastOpenedTime` for the two time sorts).
  - Visibility computeds: `ShowEmptyLibrary => _allCards.Count == 0`;
    `ShowNoMatches => _allCards.Count > 0 && !(Books.Count > 0 || Groups.Count > 0)`. Raise both
    (plus `HasBooks`) at the end of `Rebuild()`.
  - Settings: `LoadBooksSettings()` (direct-field seed in ctor, before the first `LoadFromDatabase`)
    + `SaveBooksSettings()` (`using PaperbunkrDb.CreateContext(); GetOrCreateAppSettings(); set 3
    fields; SaveChanges()`), mirroring `LibraryScreenViewModel.LoadLibrarySettings`/`SaveLibrarySettings`.
  - `[RelayCommand] ClearSearch() => SearchQuery = "";`

**Depends on:** Step 1
**Verify:** `BooksScreenViewModelTests` additions —
- search filters by title / author / series; empty query shows all;
- each `SortField` + both directions order `Books` correctly;
- `GroupField.Series` yields a "Standalone" group for no-series books, sorted last;
  `GroupField.Author` yields "Unknown author";
- `SortField` / `SortDirection` / `GroupField` round-trip through `AppSettings` (new VM instance
  reads them back); `SearchQuery` does **not** persist;
- `ShowNoMatches` true for a non-empty library with a non-matching query; `ShowEmptyLibrary` true
  only with zero books.

## Step 4: `BooksScreen.axaml` — chrome row + grouped grid

**Files:** `src/Paperbunkr.App/Views/BooksScreen.axaml` (edit)

**What:**
- `UserControl.Styles`: copy `Button.toolbarPill` (+ `.open`), `Border.dropdown`,
  `Button.modeOption` (+ `.active`) from `LibraryToolbar.axaml` (same "each UserControl keeps its
  own copy" precedent as `LibraryScreen`).
- New chrome `Border` between the header `Border` and the content `ScrollViewer`: `PbSurface2` bg,
  bottom border, `12,10` padding; a `Grid ColumnDefinitions="*,Auto,Auto" ColumnSpacing="10"`:
  - col 0: a search `Border` (`PbSurface1`, `PbRadius`, 1px border, `MaxWidth="360"`,
    `HorizontalAlignment="Left"`) → `DockPanel` with a leading `Path Classes="pbIcon"`
    `PbIconSearch` + a `TextBox` (`Text="{Binding SearchQuery}"`, `PlaceholderText="Search books…"`,
    transparent), `AutomationProperties.AutomationId="BooksSearchBox"`.
  - col 1: `Button x:Name="GroupButton" Classes="toolbarPill" Classes.open="{Binding IsGroupOpen}"
    Command="{Binding ToggleGroupCommand}"` → `Group {GroupLabel} ▾`, id `BooksGroupButton`.
  - col 2: `Button x:Name="SortButton" …` → `Sort {SortLabel} {↑|↓} ▾`, id `BooksSortButton`.
- Two `Popup`s (`PlacementTarget` `#GroupButton` / `#SortButton`, `IsOpen` bound, `IsLightDismissEnabled`):
  - Group popup: `None` / `Series` / `Author` `modeOption` rows (`SetGroupFieldCommand`, `.active`
    on equality), ids `BooksGroupOption_{None,Series,Author}`.
  - Sort popup: `Title` / `Author` / `Recently added` / `Last opened` rows
    (`SetSortFieldCommand`), then a rule, then `↑ Ascending` / `↓ Descending` rows
    (`ToggleSortDirectionCommand`, `.active` on the current direction), ids
    `BooksSortOption_*` / `BooksSortAscending` / `_Descending`.
- Content `ScrollViewer` inner `Grid`:
  - ungrouped `ItemsControl` over `Books`, `IsVisible="{Binding !IsGrouped}"` — same `WrapPanel` +
    `Button Classes="card"` template as today (extract the card `DataTemplate` to
    `UserControl.Resources` with `x:Key="BookCardTemplate"` so both paths share it).
  - grouped `ItemsControl` over `Groups`, `IsVisible="{Binding IsGrouped}"` — item template a
    `StackPanel`: the 4a `DockPanel` header (`pbTextHeading` `{Binding Header}` + count `{Binding
    Items.Count}` `pbTextCaption` + 1px `PbBorderBrush` rule) + a nested `ItemsControl`/`WrapPanel`
    over `Items` with `ItemTemplate="{StaticResource BookCardTemplate}"`.
  - empty-library state: existing centered `StackPanel`, now `IsVisible="{Binding ShowEmptyLibrary}"`.
  - no-matches state: a new centered `StackPanel` `IsVisible="{Binding ShowNoMatches}"` — `pbTextBody`
    `No books match “…”` (bind to `SearchQuery` via `StringFormat`) + a `Button Classes="secondary"
    Content="Clear search" Command="{Binding ClearSearchCommand}"`.

**Depends on:** Step 3
**Verify:** `dotnet build src/Paperbunkr.App` (no new `.axaml` file → no AVLN2000 guard needed);
**on-screen screenshot** — chrome row both skins, search, each sort/group, grouped Bebas headers,
no-matches state.

## Step 5: Home — "Continue Reading — Books" row

**Files:** `src/Paperbunkr.Data/Metadata/HomeFeedResolver.cs` (edit),
`src/Paperbunkr.App/Models/HomeBookCard.cs` (new),
`src/Paperbunkr.App/ViewModels/HomeScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit),
`src/Paperbunkr.App/Views/HomeScreen.axaml` (edit),
`src/Paperbunkr.App.Tests/HomeScreenViewModelTests.cs` (edit),
`src/Paperbunkr.Data.Tests/HomeFeedResolverTests.cs` (edit)

**What:**
- `HomeFeedResolver`: `public static IReadOnlyList<Book> GetContinueReadingBooks(PaperbunkrDbContext
  context, int limit = 10)` — `context.Books.Include(b => b.BookSeries)
  .Where(b => b.LastOpenedTime != null && !b.Finished && (b.LastChapterIndex > 0 ||
  b.LastCharacterOffset > 0)).OrderByDescending(b => b.LastOpenedTime).Take(limit).ToList()`.
- `HomeBookCard` (`Models`): `{ int BookId; BookFormat Format; string Title; string? Author;
  double ProgressFraction; bool ShowProgress }` — `FromBook(Book)` computes
  `ShowProgress = book.Format == BookFormat.Epub && book.ChapterCount > 1`,
  `ProgressFraction = ShowProgress ? Math.Clamp(book.LastChapterIndex / (double)(book.ChapterCount - 1), 0, 1) : 0`.
- `HomeScreenViewModel`:
  - ctor gains `Action<int, BookFormat> goReaderForBook` (last param); store it.
  - `ObservableCollection<HomeBookCard> ContinueReadingBooks { get; }`;
    `bool HasBooksLibrary` (backing field set in `LoadFromDatabase`),
    `bool HasContinueReadingBooks => ContinueReadingBooks.Count > 0`.
  - `LoadFromDatabase`: after the comic Continue Reading block —
    `HasBooksLibrary = context.Books.Any();` then `ContinueReadingBooks.Clear();
    foreach (var b in HomeFeedResolver.GetContinueReadingBooks(context)) ContinueReadingBooks.Add(HomeBookCard.FromBook(b));`
    Raise `HasBooksLibrary` + `HasContinueReadingBooks` in the trailing `OnPropertyChanged` block.
  - `[RelayCommand] private void OpenContinueReadingBook(HomeBookCard? card) { if (card != null)
    _goReaderForBook(card.BookId, card.Format); }`
- `MainViewModel.cs:40`: pass `GoBookReaderForBook` as the new arg.
- `HomeScreen.axaml`: clone the comic Continue Reading `StackPanel` immediately after it —
  root `IsVisible="{Binding HasBooksLibrary}"`, header `pbTextHeading` "Continue Reading — Books"
  (id `HomeContinueReadingBooksHeader`), a horizontal `ScrollViewer`/`ItemsControl` over
  `ContinueReadingBooks` gated `IsVisible="{Binding HasContinueReadingBooks}"`, `PosterTile`
  `CoverSource="{Binding BookId, Converter={x:Static views:BookCoverImageConverter.Instance}}"`,
  `TitleText="{Binding Title}"`, `MetaText="{Binding Author}"`,
  `ShowProgress="{Binding ShowProgress}"`, `ProgressFraction="{Binding ProgressFraction}"`,
  `Command="{Binding …OpenContinueReadingBookCommand}"`, `CommandParameter="{Binding}"`;
  a "Nothing in progress yet" `emptyState` line when `!HasContinueReadingBooks`.

**Depends on:** Step 1 (`Finished`/`ChapterCount`); benefits from Step 2 (populated `ChapterCount`)
but doesn't require it (books unopened since the migration just show no bar).
**Verify:** `HomeFeedResolverTests` — `GetContinueReadingBooks` includes only started-not-finished
books, newest first, capped, excludes `Finished` and never-opened. `HomeScreenViewModelTests` —
`HasBooksLibrary` false at 0 books / true at ≥1; `ContinueReadingBooks` populated + ordered;
`OpenContinueReadingBookCommand` invokes the callback with `(BookId, Format)`. Build.

## Step 6: Migration test + full build + on-screen

**Files:** `src/Paperbunkr.Data.Tests/AddBooksBrowseStateMigrationTests.cs` (new),
`docs/superpowers/specs/2026-08-27-books-screen-chrome-and-home-strip-design.md` (status)

**What:**
- Migration test (mirror `LibraryDetailsColumnsMigrationTests`): migrate to the prior migration,
  seed a `Books` row + the `AppSettings` singleton via raw SQL, `.Migrate()`, assert
  `Books.Finished == 0` / `ChapterCount == 0` and the three `AppSettings` columns exist with
  defaults; `Down` drops all five, rows survive.
- Solution build; full `dotnet test` (`Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests`); launch the
  app: Books chrome (search / each sort / each group, grouped Bebas headers, no-matches state), both
  skins; Home — the "Continue Reading — Books" row present with an in-progress book, absent on a
  comic-only library, a finished book gone from it. Flip the design doc status to Implemented with
  what was verified vs. left to manual.

**Depends on:** Steps 1–5
