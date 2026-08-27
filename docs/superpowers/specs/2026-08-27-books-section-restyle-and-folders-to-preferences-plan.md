# Books Section — Restyle + Move Folder Management to Preferences — Implementation Plan

*No design doc — task is clear: apply the Phase-1 design language (as used by Library 4a/4b) to
`BooksScreen`, and move its book-folder management into Preferences → Libraries, mirroring the
comic `WatchedFolders` section already there.*

## Background / current state (surveyed)

- **`BooksScreen.axaml`** (137 lines) — pre-Phase-1: hardcoded `#383D47` border, `PbChromeBrush`,
  `CornerRadius="5"/"6"`, a plain `FontSize="22" FontWeight="Bold"` "Books" heading (not
  `pbTextHeading`). Sections: a header row (`Add Folder` / `Scan Now` `Button.headerAction`), a
  `BOOK FOLDERS` list (`Border.folderRow` + Remove), a scan-status `TextBlock`, a "No books yet"
  line, and a `WrapPanel` of `Button.card` book cards (`Border.bookCover` 200px cover + title +
  author + series).
- **`BooksScreenViewModel.cs`** (196 lines): ctor `(FilePickerService, BookFolderScanService,
  BookCoverThumbnailService, Action<int,BookFormat> goReaderForBook)`. Members: `Books`
  (`BookCardSample`), `Folders` (`BookFolderSummary`), `HasBooks`, `HasFolders`, `ScanStatus`,
  `IsScanning`; commands `SelectBook`, `DeleteBook`, `AddFolder` (picks folder → `context.BookFolders`),
  `RemoveFolder`, `ScanNow` (`_scanService.ScanAllAsync` → if added, `_coverService.GenerateAllAsync`).
  `LoadFromDatabase` reads `context.BookFolders` + `context.Books`.
- **`BookFolder` entity** — just `Id` + `Path` (no `Watch`, unlike `WatchedFolder`).
- **`MainViewModel.cs:42`**: `Books = new BooksScreenViewModel(new FilePickerService(), new
  BookFolderScanService(), new BookCoverThumbnailService(), GoBookReaderForBook);`. `GoBooks()`
  (`:330`) calls `Books.LoadFromDatabase()`. `GoLibraryFoldersPreferences()` (added for Library 4b,
  `:358` area) navigates to Preferences → Libraries tab (`Preferences.ActiveTab = "libraries"`).
- **`PreferencesScreen.axaml` Libraries tab** (`:491–580`): a `Border.groupBox` **headed
  "Book Folders" but bound to `WatchedFolders`** (comic library folders — `LibraryFolderScanner`,
  `Watch` toggle, `Migrate from ComicRack CE` is the next groupBox). The header text is a
  copy-paste misnomer; `AddFolderCommand`'s picker title is literally `"Add Book Folder"`
  (`PreferencesScreenViewModel.cs:856`) — also wrong, it adds a `WatchedFolder`. Row template:
  `Border.skinRow` with Path + "Watch for changes" `CheckBox` + Open + Remove `Button.headerAction ghost`;
  footer: `Add Folder…` (`headerAction primary`) / `Scan Now` / `Generate Covers` / `Sync Metadata`
  + `{Binding ScanStatus}`. `x:Name="WatchedFoldersList"` for the `$parent` command binding.
- **`PreferencesScreenViewModel.cs`**: `// ===== Book Folders =====` region `:835–912` actually
  manages `WatchedFolders` — `RefreshWatchedFolders`, `AddFolder`, `RemoveFolder`, `ToggleWatch`,
  `ScanNow` (`:920` area), `GenerateCovers` (`:979`), `SyncMetadata` (`:1015`). `ScanNow`/`GenerateCovers`
  build `new CoverThumbnailService(_contextFactory)` inline (`:951`, `:997`). Ctor has a public
  overload + `internal … Func<PaperbunkrDbContext> contextFactory` test seam (`:76`).
- **Tests**: `PreferencesScreenViewModelTests` (`DatabasePathOverride = _dbPath` class-wide) has
  `AddFolder_*` / `RemoveFolder_*` / `ScanNow_*` for `WatchedFolders` (`:720–850`). **No
  `BooksScreenViewModelTests` file exists** — that VM is currently untested. `BookFolderScanService`
  has a parameterless ctor; `BookCoverThumbnailService` has both a public and an
  `internal(Func<PaperbunkrDbContext>)` ctor. Parameterless `new BookFolderScanService()` in the
  Preferences VM will honour `DatabasePathOverride` the same way `new CoverThumbnailService()` does.
- **Restyle reference**: Library 4a poster tiles — `Border.cover`/`Border.posterCover` +
  `Border.posterScrim` (Primitives.axaml) + a `Button.card:pointerover Border.posterCover` →
  `PbGlowRing` selector; `pbTextHeading` (Bebas), `pbTextBody`/`pbTextCaption`, `PbSurface1/2/3Brush`,
  `PbRadius`/`PbRadiusSm`. Empty-state pattern from 4b: centered `StackPanel` + `Path Classes="pbIcon"`
  + `pbTextBody` line + `Button Classes="secondary"` bound to a nav callback.
- **UI tests**: none reference `Books*` or `BookFolder*` (grepped).

## Naming decision

The existing (mislabeled) comic section becomes **"Comic Library Folders"**. The new novel-folder
section is **"Book Folders"**. Picker titles corrected to match.

---

## Step 1: Preferences VM — add book-folder management

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit)

**What:**
- `WatchedFolders` region: in `AddFolder` change the picker title `"Add Book Folder"` →
  `"Add Comic Library Folder"` (behaviour unchanged, it still adds a `WatchedFolder`).
- New `// ===== Book Folders (novels) =====` region:
  - `public ObservableCollection<BookFolderSummary> BookFolders { get; }` — init in **both**
    ctors' shared body (next to `WatchedFolders = new(...)` at `:111`).
  - `[ObservableProperty] private string? _bookScanStatus;` + `[ObservableProperty] private bool
    _isScanningBooks;`
  - `private void RefreshBookFolders()` — `using var context = _contextFactory(); BookFolders.Clear();
    foreach (var f in context.BookFolders.OrderBy(f => f.Path)) BookFolders.Add(new BookFolderSummary
    { Id = f.Id, Path = f.Path });`. Call it from wherever `RefreshWatchedFolders()` is already
    called on tab load (`:414` area — `RefreshWatchedFolders()` in the load path).
  - `[RelayCommand] private async Task AddBookFolder()` — `_filePicker.PickFolderAsync("Add Book
    Folder")`; if non-null and not already in `context.BookFolders`, add + save; `RefreshBookFolders()`.
  - `[RelayCommand] private void RemoveBookFolder(BookFolderSummary folder)` — remove by `Id` + save;
    `RefreshBookFolders()`.
  - `[RelayCommand] private void OpenBookFolder(BookFolderSummary folder)` — reuse the same shell
    reveal `OpenFolderCommand` uses for `WatchedFolderSummary` (extract a shared
    `RevealInExplorerHelper`/`Process.Start` call or add a parallel tiny command — check what
    `OpenFolder` does and mirror).
  - `[RelayCommand] private async Task ScanBooksNow()` — mirror the comic `ScanNow`: guard on
    `IsScanningBooks`; `IsScanningBooks = true; BookScanStatus = "Scanning…";` →
    `new BookFolderScanService().ScanAllAsync(new Progress<(int,int)>(p => BookScanStatus = $"Scanning… {p.Done}/{p.Total}"))`;
    if `result.BooksAdded > 0` → `new BookCoverThumbnailService(_contextFactory).GenerateAllAsync(...)`
    with a "Generating covers…" progress; set `BookScanStatus` to the same summary string
    `BooksScreenViewModel.ScanNow` used (`"No new books found."` / `$"Added {n} book(s) across {s}
    series."`); `catch` → `$"Scan failed: {ex.Message}"`; `finally` → `IsScanningBooks = false;
    RefreshBookFolders();`. Fire `_showToast("Book scan complete", …)` on success like the comic one.

**Depends on:** none
**Verify:** Step 5's `PreferencesScreenViewModelTests` cases.

## Step 2: Preferences XAML — rename comic section, add Book Folders section

**Files:** `src/Paperbunkr.App/Views/PreferencesScreen.axaml` (edit)

**What:**
- The existing `<!-- Book Folders -->` groupBox (`:493`): comment → `<!-- Comic Library Folders -->`,
  header `TextBlock Text` `"Book Folders"` → `"Comic Library Folders"`. Nothing else in it changes.
- Add a new `<Border Classes="groupBox">` immediately after it (before the Migration groupBox):
  - `groupHeader` → `"Book Folders"` + a caption line `"EPUB and PDF novels"` (`pbTextCaption` or the
    existing faint style).
  - `ItemsControl x:Name="BookFoldersList" ItemsSource="{Binding BookFolders}"`, item template a
    `Border.skinRow` → `Grid ColumnDefinitions="*,Auto,Auto"` (no Watch column): Path `TextBlock`,
    `Open` `Button.headerAction ghost` → `#BookFoldersList.((vm:PreferencesScreenViewModel)DataContext).OpenBookFolderCommand`,
    `Remove` → `…RemoveBookFolderCommand`, both `CommandParameter="{Binding}"`,
    `AutomationProperties.AutomationId="PreferencesBookFolderOpen"` / `_Remove`.
  - Footer `StackPanel`: `Add Folder…` (`headerAction primary`, `AddBookFolderCommand`,
    id `PreferencesAddBookFolder`) + `Scan Now` (`headerAction ghost`, `ScanBooksNowCommand`,
    `IsEnabled="{Binding !IsScanningBooks}"`, id `PreferencesScanBooksNow`) +
    `TextBlock Text="{Binding BookScanStatus}"`.
  - Use `Path Classes="pbIcon"` (`PbIconFolderOpen` / `PbIconPlus` / `PbIconSearch`) rather than the
    old raster `Border.icon`/`OpacityMask` where easy — but matching the existing comic section's
    raster icons is acceptable if it keeps the diff small (this screen's full icon pass isn't 4b's
    or this task's job). Decide at implementation; consistency with the sibling section wins.

**Depends on:** Step 1
**Verify:** `dotnet build src/Paperbunkr.App` clean; manual — both sections render, Add/Remove/Scan
work.

## Step 3: BooksScreenViewModel — strip folder management, add settings callback

**Files:** `src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)

**What (BooksScreenViewModel):**
- Remove: `Folders`, `HasFolders`, `ScanStatus`, `IsScanning`, `AddFolderCommand`,
  `RemoveFolderCommand`, `ScanNowCommand`; the `_filePicker`, `_scanService`, `_coverService`
  fields and their ctor params.
- Ctor becomes `BooksScreenViewModel(Action<int, BookFormat> goReaderForBook, Action goLibrarySettings)`.
- `public bool HasBooks` stays; add `public string EmptyStateMessage => "No books yet."` (or inline
  in XAML) and a `[RelayCommand] private void OpenLibrarySettings() => _goLibrarySettings();`.
- `LoadFromDatabase`: drop the `context.BookFolders` query + `Folders.Clear()/Add`; keep the
  `context.Books` load + `HasBooks` notify.
- `DeleteBook` / `SelectBook` unchanged (still need `context`, `RecycleBinHelper`, `_goReaderForBook`).
- Update the class doc comment: folder management + scan now live in Preferences → Libraries.

**What (MainViewModel):**
- `:42` → `Books = new BooksScreenViewModel(GoBookReaderForBook, GoLibraryFoldersPreferences);`
  (reuse the 4b callback — it already navigates to Preferences → Libraries).
- `GoBooks()` unchanged (still `Books.LoadFromDatabase()`).

**Depends on:** none (independent of Steps 1–2)
**Verify:** `dotnet build` clean; Step 5's new `BooksScreenViewModelTests`.

## Step 4: BooksScreen.axaml — remove folder UI + full restyle

**Files:** `src/Paperbunkr.App/Views/BooksScreen.axaml` (edit)

**What:**
- **Delete**: the `Add Folder` / `Scan Now` header buttons (the whole `Grid.Column="1"` StackPanel
  at `:60`), the `BOOK FOLDERS` StackPanel (`:67–91`), the `ScanStatus` `TextBlock` (`:93`).
- **Header**: `TextBlock Text="Books" Classes="pbTextHeading"` + subtitle
  `TextBlock Text="Novels — EPUB and PDF" Classes="pbTextCaption"`. Bottom rule `Border` →
  `PbBorderBrush` (unchanged).
- **Styles**: drop the local `Button.headerAction` (no longer used) and `Border.folderRow`. Replace
  `Border.bookCover` with the 4a cover treatment — `CornerRadius="{DynamicResource PbRadiusSm}"`,
  keep the border/shadow but via tokens; add a `Border.posterScrim` child in the cover `Grid` and a
  `Button.card:pointerover Border.bookCover` / `:focus-within` → `BoxShadow="{StaticResource
  PbGlowRing}"` selector (+ a `BoxShadowsTransition`). Keep the local `Button.card` copy.
- **Card**: cover `Border` gets the `posterScrim`; title `pbTextBody`-ish (SemiBold `PbTextBrush`),
  author/series `pbTextCaption`. Keep the `Button.card` root, `SelectBookCommand`, the
  `Delete Book` context menu.
- **Empty state**: replace the plain "No books yet…" `TextBlock` with a centered `StackPanel`
  `IsVisible="{Binding !HasBooks}"` — `Path Classes="pbIcon"` (`PbIconBook`), `pbTextBody`
  "No books yet.", `pbTextCaption` "Add a book folder in Preferences → Libraries, then scan.",
  and a `Button Classes="secondary" Content="Open Library settings"
  Command="{Binding OpenLibrarySettingsCommand}"` (`AutomationProperties.AutomationId="BooksEmptyStateSettings"`).
- Swap remaining `PbChromeBrush` / `#383D47` / literal radii for `PbSurface*` / `PbBorderBrush` /
  `PbRadius*` tokens throughout.

**Depends on:** Step 3 (`OpenLibrarySettingsCommand`, `HasBooks` still present)
**Verify:** `dotnet build src/Paperbunkr.App` (AVLN2000 guard not needed — existing file);
**on-screen screenshot** of Books (empty + with a seeded book) is the real check.

## Step 5: Tests

**Files:** `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit),
`src/Paperbunkr.App.Tests/BooksScreenViewModelTests.cs` (new)

**What:**
- `PreferencesScreenViewModelTests` (mirror the existing `WatchedFolders` cases):
  - `AddBookFolder_UserPicksFolder_PersistsAndRefreshesList` — fake picker returns a path;
    `await vm.AddBookFolderCommand.ExecuteAsync(null)`; `Assert.Single(vm.BookFolders)` +
    `Assert.Single(context.BookFolders)`.
  - `AddBookFolder_UserCancels_DoesNotAdd`.
  - `RemoveBookFolder_DeletesItFromListAndDatabase` — seed a `BookFolder`, load, remove, assert gone
    from list + db.
  - `ScanBooksNow_ReportsResultInBookScanStatus` — empty `_scanRoot` book folder → `BookScanStatus`
    == `"No new books found."`; `IsScanningBooks` false after.
  - If `CreateViewModel` helper needs no new args (it doesn't — no ctor change), leave it.
- New `BooksScreenViewModelTests` (`DatabasePathOverride` + `EnsureCreated`, same shape as
  `LibraryScreenViewModelTests`):
  - `LoadFromDatabase_LoadsBooks_NotFolders` — seed 2 `Book` rows (+ a `BookFolder` that should be
    ignored now); `HasBooks` true, `Books.Count == 2`; VM has no `Folders` member (compile-time).
  - `DeleteBook_RemovesRowAndRecyclesFile` — seed a `Book` with a temp file; `DeleteBookCommand`;
    row gone. (Recycle of a temp path is fine — `RecycleBinHelper` no-ops / best-effort on a missing
    file; match how `LibraryScreenViewModelTests` handles delete.)
  - `SelectBook_InvokesReaderCallback_WithIdAndFormat` — capture the `goReaderForBook` args.
  - `OpenLibrarySettings_InvokesCallback`.

**Depends on:** Steps 1, 3
**Verify:** `dotnet test src/Paperbunkr.App.Tests` green.

## Step 6: Full build + on-screen verification + doc

**Files:** none (or a short note in `docs/superpowers/specs/` if worth it)

**What:** solution build, full `dotnet test` (App.Tests + Data.Tests), launch the app: Books screen
(empty state + with books, both skins), Preferences → Libraries showing both "Comic Library Folders"
and "Book Folders" sections, add/remove/scan a book folder from Preferences, confirm the Books grid
picks up scanned books on next visit, and the Books empty-state button lands on Preferences →
Libraries.

**Depends on:** Steps 1–5
