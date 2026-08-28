# Book Details Screen (Piece B1) — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-27-book-details-screen-design.md*

**Status: done 2026-08-27.** Steps 1-8 implemented; Step 9 sweep: App.Tests 994 green, Data.Tests
457 green, app launches clean (XAML weave verified via launch). Step 4 (shared `BookCardTemplate`)
was skipped in favour of the documented inline-copy fallback. On-screen manual GUI pass still
outstanding.

## Step 1: Reader-entry position parameter + models
**Files:** `src/Paperbunkr.App/Models/BookDetailMode.cs` (new),
`src/Paperbunkr.App/Models/BookBookmarkSummary.cs` (edit),
`src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/PdfPageReaderScreenViewModel.cs` (edit)
**What:**
- `enum BookDetailMode { Book, Series }`.
- `BookBookmarkSummary` gains `public DateTime CreatedTime { get; init; }`; `BookReaderScreenViewModel.ToSummary` (both overloads) set it.
- `BookReaderScreenViewModel.LoadBook(int bookId, BookPosition? startAt = null)` — when non-null, seed `_position` from `startAt` (still `Math.Clamp` the chapter index to `_source.Chapters.Count - 1`) instead of from `_book.LastChapterIndex/Offset`. No other change.
- `PdfPageReaderScreenViewModel.LoadBook(int bookId, BookPosition? startAt = null)` — param accepted, ignored, XML-doc says why.
**Depends on:** none
**Verify:** `BookReaderScreenViewModelTests` — new facts: `LoadBook(id, new BookPosition(2,50))` starts there; `LoadBook(id)` still resumes; out-of-range chapter clamps.

## Step 2: RevealInExplorerHelper.RevealBook
**Files:** `src/Paperbunkr.App/Services/RevealInExplorerHelper.cs` (edit),
`src/Paperbunkr.App.Tests/RevealInExplorerHelperTests.cs` (edit — file may need creating if absent)
**What:** `RevealBook(Book)` + internal pure `ResolveBookFilePath(Book)` (returns `FilePath` or null when empty), mirroring `RevealIssue`/`ResolveIssueFilePath`.
**Depends on:** none
**Verify:** `ResolveBookFilePath` returns path / null. (If no test file exists, add a minimal one.)

## Step 3: BookDetailScreenViewModel
**Files:** `src/Paperbunkr.App/ViewModels/BookDetailScreenViewModel.cs` (new)
**What:** Full VM per design §Components 1. Ctor `(Action goBooks, Action<int,BookFormat,BookPosition?> goReaderForBook, Action<int>? goEditBook = null)`. `LoadBook`/`LoadSeries`, all `[ObservableProperty]` fields, `Chapters`/`Bookmarks`/`SeriesBooks` collections, commands (`Continue`, `MarkFinishedToggle`, `OpenChapter`, `OpenBookmark`, `DeleteBookmark`, `OpenSeriesFromLink`, `OpenBookFromSeries`, `RevealInExplorer`, `DeleteBook`, `ToggleSynopsis`, `GoBack`, `Edit` no-op), `ReloadCurrentBook()`. EPUB parse via `using var src = new EpubBookSource(FilePath)` wrapped in try/catch → `ChaptersUnavailable` flag; chapter list only when `src.Chapters.Count > 1`; bookmark `ChapterTitle` from `src.Chapters` else `"Chapter {n+1}"`. Synopsis threshold 280. Dates formatted `"MMM d, yyyy"`.
**Depends on:** Step 1 (enum, `BookPosition?` reader signature), Step 2 (RevealBook)
**Verify:** `BookDetailScreenViewModelTests` (new) — Step 7.

## Step 4: BookCardTemplate extraction
**Files:** `src/Paperbunkr.App/Views/BookCardTemplate.axaml` (new `ResourceDictionary`),
`src/Paperbunkr.App/Views/BooksScreen.axaml` (edit — remove inline template, merge the dict)
**What:** Move the `BookCardTemplate` `DataTemplate` + the `Button.card` / `Border.bookCover` style classes it needs into a shared `ResourceDictionary`; `MergedDictionaries` it into `BooksScreen.axaml`. Keep the `$parent[UserControl]` command bindings (`SelectBookCommand` / `DeleteBookCommand`).
**Depends on:** none
**Verify:** build + launch; Books grid still renders and cards still open (now → Details after Step 6). If `$parent` coupling misbehaves, fall back to a second inline copy in `BookDetailScreen.axaml` (design §Risks) and skip the shared dict.

## Step 5: BookDetailScreen.axaml (+ code-behind)
**Files:** `src/Paperbunkr.App/Views/BookDetailScreen.axaml` (new),
`src/Paperbunkr.App/Views/BookDetailScreen.axaml.cs` (new — **same commit**, AVLN2000 guard)
**What:** Two `IsVisible`-switched `StackPanel`s (Book / Series) inside a `ScrollViewer`, styled off `DetailScreen.axaml` (back link, `Grid 190,*`, cover left). Chapter rows + bookmark rows (with `ContextMenu` "Delete"). Series mode reuses `BookCardTemplate`. Minimal code-behind (`InitializeComponent`).
**Depends on:** Step 3 (DataContext), Step 4 (template)
**Verify:** `dotnet build` then **launch the exe** (0 Errors is insufficient — CLAUDE.md).

## Step 6: BooksScreenViewModel + BookCardGroup + grouped-header click
**Files:** `src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Models/BookCardGroup.cs` (edit),
`src/Paperbunkr.App/Views/BooksScreen.axaml` (edit)
**What:**
- `BookCardGroup` gains `int? BookSeriesId`.
- `BooksScreenViewModel` ctor: replace `Action<int,BookFormat> goReaderForBook` with `Action<int> goBookDetail`, add `Action<int> goBookSeriesDetail`. `SelectBook` → `_goBookDetail(book.BookId)`. New `OpenSeriesCommand(int?)` → `_goBookSeriesDetail`. `GroupCards` sets `BookSeriesId` from the first book when `GroupField == Series` (needs `BookSeriesId` on `BookCardSample` — add it, set in `FromBook`).
- `BooksScreen.axaml`: wrap the grouped-view `DockPanel` header in a transparent `Button` bound to `OpenSeriesCommand` w/ `CommandParameter="{Binding BookSeriesId}"`, `IsEnabled` via a not-null converter (Author groups + Standalone inert).
**Depends on:** none (callbacks are `Action<int>`, wired in Step 8)
**Verify:** `BooksScreenViewModelTests` — `SelectBook` fires `goBookDetail` not a reader cb; `BookCardGroup.BookSeriesId` set/null; `OpenSeriesCommand` fires `goBookSeriesDetail`.

## Step 7: BookDetailScreenViewModelTests
**Files:** `src/Paperbunkr.App.Tests/BookDetailScreenViewModelTests.cs` (new)
**What:** Per design §Testing. Use `EpubFixture.Create` for a real source (mirror `BookReaderScreenViewModelTests` setup: temp db + `DatabasePathOverride`, `[Collection(nameof(AvaloniaTestCollection))]`). Cover: field population, progress math, `Finished`→"Read again", mark finished/unread (+ reset), never-opened, PDF reduced view, synopsis threshold, bookmarks newest-first + resolved titles + delete, `LoadSeries` list + count, back label.
**Depends on:** Step 3
**Verify:** `dotnet test --filter BookDetailScreenViewModelTests`.

## Step 8: MainViewModel wiring + MainWindow slot
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit),
`src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:**
- Construct `Books` with `(GoBookDetailForBook, GoBookSeriesDetailForSeries, GoLibraryFoldersPreferences)`; construct `BookDetail = new BookDetailScreenViewModel(GoBooks, GoBookReaderForBook, null)`; `BookReader`/`PdfReader` ctor `goBack` → `GoBackFromBookReader`.
- `BookDetail` property, `IsBookDetail`, `"bookDetail"` in `OnCurrentScreenChanged` fan-out.
- `GoBookDetailForBook(int)` / `GoBookSeriesDetailForSeries(int)`.
- `GoBookReaderForBook(int, BookFormat, BookPosition? startAt = null)` — capture `_screenBeforeBookReader` (guard self-overwrite), pass `startAt` to `BookReader.LoadBook` / `PdfReader.LoadBook`. Update the `HomeScreenViewModel` ctor call site (`GoBookReaderForBook` passed as `Action<int,BookFormat>`) — keep a 2-arg adapter or make Home's delegate 3-arg; simplest: `(id, fmt) => GoBookReaderForBook(id, fmt)` inline, or widen. Check `PaperbunkrOpenBooksManager` / plugin callers too.
- `GoBackFromBookReader()` → `"bookDetail"` reloads current book + stays; else `GoBooks()`.
- `Escape` / editor-guard nav: `"bookDetail"` behaves like `"detail"` (drill-down → `books`).
- `MainWindow.axaml`: add a `ContentControl IsVisible="{Binding IsBookDetail}" Content="{Binding BookDetail}"` with a `BookDetailScreen` template, next to the `IsDetail` one.
**Depends on:** Steps 3, 6
**Verify:** `MainViewModelTests` if it covers nav (`GoBookDetailForBook` → `CurrentScreen == "bookDetail"`; reader-from-bookDetail back → `"bookDetail"`). Build + launch.

## Step 9: Full build, test sweep, manual pass, doc update
**Files:** `docs/superpowers/specs/2026-08-27-book-details-screen-design.md` (edit — status line),
`docs/alpha-todo.md` (edit — if it tracks the Books follow-up), memory
**What:** `dotnet build` (delete `obj/Debug/net10.0/Paperbunkr.App.dll`+`.pdb` first if a XAML-compile failure ever masks — CLAUDE.md). `dotnet test` (App.Tests + Data.Tests). Launch: card→Details→Continue→reader→back→Details; series header→series mode→card→book mode→back chain; chapter/bookmark row jumps; mark finished/unread ↔ Home strip; PDF reduced view; both skins. Update the design doc status; update `project_paperbunkr_books_browse_chrome` memory (Piece B done).
**Depends on:** all
**Verify:** green suite + manual checklist.

## Notes / discovered surface
- `HomeScreenViewModel` is constructed with `GoBookReaderForBook` as its 5th arg — widening that signature ripples there; check `HomeScreenViewModelTests` call sites too (memory notes ~16 sites were patched with `, (_, _) => { }` for a prior signature change).
- `BookPosition` lives in `Paperbunkr.App.Models` (a `readonly record struct`, has `.Start`).
- `EpubBookSource` is `IDisposable`; `Chapters` is `IReadOnlyList<BookChapter>` with `.Title`.
- Concurrent worktree sessions exist under `.claude/worktrees/` — main tree was clean at session start; keep edits to `src/Paperbunkr.App*` + docs.
