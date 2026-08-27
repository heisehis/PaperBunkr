# Books Screen Chrome + Home "Continue Reading — Books" — Design

**Status:** Implemented 2026-08-27 — see the plan
(`2026-08-27-books-screen-chrome-and-home-strip-plan.md`) for what was verified. Book Details is
Piece B, still to spec.
**Piece A of 2** in the Books-section follow-up to the Library UI redesign. Piece B (a Book
Details screen) is a separate later spec. Builds on the Books restyle + folder-move
(`2026-08-27-books-section-restyle-and-folders-to-preferences-plan.md`, PR #17).

## Background

`BooksScreen` after the restyle is a Bebas header + a flat `WrapPanel` grid of `BookCardSample`
cover cards (cover + title + author + italic series line), reader-on-click, empty state pointing to
Preferences. No search, sort, or grouping. `BooksScreenViewModel` is `(goReaderForBook,
goLibrarySettings)` with `Books` / `HasBooks` / `LoadFromDatabase` / `SelectBook` / `DeleteBook` /
`OpenLibrarySettings`.

The `Book` entity: `Id`, `BookSeriesId?`/`BookSeries?`, `Title`, `Author?`, `Format`, `FilePath`,
`CoverImagePath?`, `Summary?`, `PublishedDate?`, `AddedTime`, `LastOpenedTime?`, `LastChapterIndex`,
`LastCharacterOffset`, `Bookmarks`. `BookSeries`: `Name`, `SortName?`, `Author?`. No "read/finished"
state today.

Home's comic "Continue Reading" row: a horizontal `ScrollViewer` of `PosterTile`s over
`HomeScreenViewModel.ContinueReading` (`HomeContinueReadingCard`), fed by
`HomeFeedResolver.GetContinueReading(context)` — series with an in-progress issue, newest-opened
first. Row body hidden (`IsVisible="{Binding HasContinueReading}"`) with a "Nothing in progress yet"
fallback line; the header always renders.

The book reader (`BookReaderScreenViewModel`) persists position in `NextPage()` /
`SaveCurrentPosition()`. `NextPage()` at the last page of the last chapter currently falls through
to a no-op `else` — the natural hook for "finished".

**CE note:** ComicRack CE has no prose-reading concept; nothing to port. Confirmed in the original
Novels design spec.

## Decisions

| Area | Decision |
|---|---|
| **Chrome row** | One row under the header, `PbSurface2` bg, bottom border: a search `Border` (`PbSurface1`, `PbRadius`, leading `PbIconSearch`, `PlaceholderText="Search books…"`) on the left taking the slack (`MaxWidth ~360`), then a `Group ▾` and a `Sort ▾` `Button.toolbarPill` on the right, each opening a small `Popup`. **Deliberately lighter than Library** — no view modes, no tabbed popup, no chips row, no selection mode. "Books don't need the comic chrome." |
| **Group by** | `None` / `Series` / `Author`. `Series` groups by `BookSeries.Name`; books with no series go in a **"Standalone"** group (sorted last). `Author` groups by `Book.Author` ("Unknown author" bucket last). Group headers: the 4a `DockPanel` — Bebas `pbTextHeading` + count + 1px rule (same as the Library grid). |
| **Sort** | `Title` / `Author` / `Recently added` (`AddedTime`) / `Last opened` (`LastOpenedTime`), plus an ascending/descending toggle. Within a group, the same sort applies to the group's books. Groups themselves order alphabetically (Series/Author name), except **Recently added** / **Last opened** sort — then groups order by their newest book's `AddedTime` / `LastOpenedTime`. |
| **Search** | Case-insensitive substring over `Title`, `Author`, and `BookSeries.Name`. Filters the grid live (no debounce — same as Library, small libraries). Applies before grouping. |
| **Persistence** | `AppSettings.BooksSortField` (enum) + `BooksSortDirection` + `BooksGroupField` (enum), same `HasConversion<string>()` + sentinel pattern as the `Library*` settings. Search text is **not** persisted. New `BooksSortField` / `BooksGroupField` enums in `Paperbunkr.Data.Entities`. |
| **Click a book** | Unchanged — opens the reader at saved position. (Details screen is Piece B.) |
| **`Book.Finished`** | New `bool Finished` column (default `false`). Set `true` by the reader when `NextPage()` would page past the end of the final chapter (the current no-op `else` branch). Cleared to `false` whenever a `Finished` book is opened for reading again (in `BookReaderScreenViewModel.Load`). One EF migration. |
| **Home "Continue Reading — Books"** | A **separate** row, its own `pbTextHeading` "Continue Reading — Books", rendered directly below the comic Continue Reading row. **The whole row (header included) only renders when the library contains any book at all** (`context.Books.Any()`) — books stay invisible to comic-only users. Within it, same `PosterTile` shape as comics: cover, title, `Author` as the meta/badge line, thin progress bar. |
| **Books "in progress"** | `LastOpenedTime is not null` AND `!Finished` AND (`LastChapterIndex > 0` OR `LastCharacterOffset > 0`) — i.e. actually started, not merely opened. Ordered by `LastOpenedTime` desc, capped at 10 (same cap as comics). |
| **Book progress fraction** | `LastChapterIndex / max(1, ChapterCount - 1)` — rough (chapters vary in length) but a useful signal and free. New `int ChapterCount` on `Book` (same migration as `Finished`), **populated lazily** the first time each book is opened in the reader (`BookReaderScreenViewModel.Load` already has `_source.Chapters.Count`). A book that's never been opened has `ChapterCount == 0` and no progress bar — fine, it isn't "in progress" anyway. EPUB only; see the PDF note in Risks. |
| **Home book card click** | Opens the book in the reader at saved position — a new `goReaderForBook`-style callback on `HomeScreenViewModel`, mirroring `OpenContinueReadingCommand`. |

## Components

### 1. `BooksScreenViewModel` — chrome state + filtered/grouped projection

- New: `[ObservableProperty] string _searchQuery`, `BooksSortField _sortField`, `SortDirection _sortDirection`,
  `BooksGroupField _groupField`; `ToggleSortDirectionCommand`, `SetSortFieldCommand(BooksSortField)`,
  `SetGroupFieldCommand(BooksGroupField)`; popup-open bools + toggle commands (`IsSortOpen`/`IsGroupOpen`,
  same single-`ActiveDropdown` mechanism the Library toolbar uses).
- `Books` stays the flat `ObservableCollection<BookCardSample>` for the ungrouped view; add
  `Groups` (`ObservableCollection<BookCardGroup>` — `Header` + count + `Items`) for the grouped view,
  and `IsGrouped => GroupField != None`. Both rebuilt in `LoadFromDatabase` from one in-memory list
  (small libraries — same tradeoff as `LibraryScreenViewModel`).
- Load/save the three persisted fields via `AppSettings` in the ctor + each `On*Changed` hook,
  mirroring `LibraryScreenViewModel.LoadLibrarySettings`/`SaveLibrarySettings` (direct-field seeding
  in the ctor to avoid redundant reloads).
- `BookCardSample` gains `Finished` (for a future dim/badge — not rendered in Piece A) and keeps its
  existing fields. `BookCardGroup` is a new model (mirrors `SeriesCardGroup`).

### 2. `BooksScreen.axaml` — chrome row + grouped grid

- Insert the chrome `Border` between the header `Border` and the content `ScrollViewer`.
- Two `Popup`s (Sort / Group) off `Button.toolbarPill`s, `Border.dropdown` + `Button.modeOption`
  rows — reuse the exact style classes already defined in `LibraryToolbar.axaml` (copy the handful
  of style setters into `BooksScreen.axaml`'s `UserControl.Styles`, same as `LibraryScreen` keeps
  its own `modeOption` copy).
- Content area: an ungrouped `ItemsControl` over `Books` (`IsVisible="{Binding !IsGrouped}"`) and a
  grouped `ItemsControl` over `Groups` (each group = the 4a `DockPanel` header + a nested
  `WrapPanel` `ItemsControl` over `Items`), mirroring the Library List/Tiles grouped structure.
- Empty state unchanged. A zero-*result* search (library non-empty, filter matches nothing) shows a
  lighter "No books match "{query}"." line with a "Clear search" button.

### 3. `AppSettings` + `Book` schema

- `AppSettings`: `BooksSortField`, `BooksSortDirection`, `BooksGroupField` (enum-as-string,
  sentinel pattern). Defaults: `Title` / `Ascending` / `None`.
- `Book`: `bool Finished` (default false), `int ChapterCount` (default 0). One migration
  `AddBookReadingState`.

### 4. `BookReaderScreenViewModel` — Finished + ChapterCount wiring

- `Load`: after resolving `_book` — set `_book.ChapterCount = _source.Chapters.Count` (lazy
  populate); if `_book.Finished`, set it back to `false` (re-reading); save once.
- `NextPage`: in the current terminal `else` (past the last page of the last chapter), set
  `_book.Finished = true` and save. No visible behaviour change in the reader itself.

### 5. Home — "Continue Reading — Books" row

- `HomeFeedResolver`: new `GetContinueReadingBooks(PaperbunkrDbContext, int limit = 10)` returning
  `IReadOnlyList<Book>` per the "in progress" rule above; and a cheap `HasAnyBooks(context)` (or the
  VM just checks `context.Books.Any()`).
- `HomeScreenViewModel`: `ObservableCollection<HomeBookCard> ContinueReadingBooks` (`HomeBookCard` =
  cover source + title + author + progress fraction + `BookId` + `Format`), `HasBooksLibrary`
  (drives the whole row's visibility), `HasContinueReadingBooks` (drives the body vs the
  "Nothing in progress yet" line), `OpenContinueReadingBookCommand`. A new ctor callback
  `Action<int, BookFormat> goReaderForBook` (wired in `MainViewModel`, same delegate `BooksScreen`
  already uses).
- `HomeScreen.axaml`: a `StackPanel` cloned from the comic Continue Reading block, its root
  `IsVisible="{Binding HasBooksLibrary}"`, `PosterTile` bound to the book card, placed immediately
  after the comic row.

## Testing

- **`BooksScreenViewModelTests`** (extends the file added in PR #17): search filters by
  title/author/series; each sort field + direction orders correctly; grouping by Series produces a
  "Standalone" bucket, by Author an "Unknown author" bucket; sort/group/direction round-trip through
  `AppSettings` (new VM instance reads them back); search text does not persist.
- **`HomeScreenViewModelTests`**: `ContinueReadingBooks` includes only started-not-finished books,
  newest-opened first, capped; `HasBooksLibrary` false with zero books and true with ≥1;
  `OpenContinueReadingBookCommand` invokes the callback with id + format.
- **`HomeFeedResolverTests`** / **`BookFolderScanServiceTests`**: `GetContinueReadingBooks` rule;
  scan sets `ChapterCount`.
- **Migration test** (`AddBookReadingStateMigrationTests`, mirroring the existing book/library
  migration tests): `Finished`/`ChapterCount` added with defaults, existing rows unaffected,
  reversible.
- **`BookReaderScreenViewModelTests`**: paging past the last chapter sets `Finished`; opening a
  finished book clears it.
- **Build** with the AVLN2000 guard is N/A (no new `.axaml` files — both are edits).
- **Manual on-screen:** the chrome row both skins; search; each sort/group; grouped headers; a
  finished book leaving the Home strip; the whole Books Home row absent on a comic-only library.

## Risks / notes

- **`ChapterCount` for PDF books.** The PDF path is the comic-panel page reader, not a chaptered
  text source — `ChapterCount` stays 0 there. On the Home strip, `Format == Pdf` book cards simply
  omit the progress bar (an in-progress PDF novel still shows in the row, just without the bar).
- **`Book.ChapterCount` and `Book.Finished` are `Paperbunkr.Data` schema, but the value is set from
  `Paperbunkr.App`** (`BookReaderScreenViewModel`) — no new cross-project coupling, the reader VM
  already writes `Book.LastChapterIndex` etc. the same way.
- **YAGNI checks passed:** no view modes, no multi-select, no filter chips, no "recently added"
  time-bucket grouping, no format badge — all explicitly cut per "books don't need the comic stuff."
