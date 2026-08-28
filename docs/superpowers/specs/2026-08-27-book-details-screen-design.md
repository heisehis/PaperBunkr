# Book Details Screen (Piece B1) — Design

**Status:** Implemented 2026-08-27. All unit tests green (App.Tests 994, Data.Tests 457); app
launches clean with the new View. On-screen manual GUI verification (the checklist below) not yet
done. See `2026-08-27-book-details-screen-plan.md` for the step-by-step.

**Implementation deviations from this design:**
- `BookCardTemplate` was **not** extracted to a shared `ResourceDictionary` — the `$parent` /
  `((vm:BooksScreenViewModel)DataContext)` coupling made sharing awkward, so `BookDetailScreen.axaml`
  keeps its own small copy of the Series-mode card template + `Button.card` / `Border.bookCover`
  styles, matching the `LibraryScreen` / `BooksScreen` "own copy" precedent (the design's documented
  fallback).
- No `Escape` / editor-guard changes: `MainViewModel.Escape` only closes overlays and
  `TryLeaveCurrentEditor` is generic (not keyed by screen name), so `"bookDetail"` as a plain
  drill-down needed no table entry.
- The finished/unread control's generated command is `ToggleFinishedCommand` (not
  `MarkFinishedToggle`).
- Delete confirmation is a `Button.Flyout` with a "Yes, delete this book" button (the grid uses a
  nested `MenuItem`; same two-step intent).

**Piece B1 of the Books-section follow-up.** Piece A (Books screen chrome + Home "Continue
Reading — Books" strip) shipped in PR #18
(`2026-08-27-books-screen-chrome-and-home-strip-design.md`). This spec adds a dedicated Book
Details screen sitting between the Books grid and the reader. **Piece B2 — a Book Properties
editor overlay (the first in-app editor for book metadata)** — is a separate later spec; the
"Edit" button on this screen is present but disabled until B2 lands.

## Background

After Piece A, `BooksScreen` is a header + chrome row (search / `Group ▾` / `Sort ▾`) + a
flat-or-grouped `WrapPanel` of `BookCardSample` cover cards. **Clicking a card opens the reader
directly** (`BooksScreenViewModel.SelectBook` → `_goReaderForBook(bookId, format)`). Grouped-by-
Series section headers are inert `TextBlock`s.

The `Book` entity: `Id`, `BookSeriesId?`/`BookSeries?`, `Title`, `Author?`, `Format`
(`Epub`/`Pdf`), `FilePath`, `CoverImagePath?`, `Summary?`, `PublishedDate?`, `AddedTime`,
`LastOpenedTime?`, `LastChapterIndex`, `LastCharacterOffset`, `Finished`, `ChapterCount`,
`Bookmarks` (`List<BookBookmark>`). `BookSeries`: `Name`, `SortName?`, `Author?`, `Books`.
`BookBookmark`: `Id`, `BookId`, `ChapterIndex`, `CharacterOffset`, `Excerpt`, `CreatedTime`.

Two comic Detail screens already exist and set the pattern:
`DetailScreenViewModel` (series-level with a single-issue focus mode) and
`MangaDetailScreenViewModel` (chapter-list-first, with a Show-more/less synopsis). Both open a
fresh `DbContext` in a `LoadSeries(int)` method and populate `[ObservableProperty]` fields with no
caching — a single-item, low-volume screen.

The Novels reflow reader (`BookReaderScreenViewModel`) parses the book on open via
`_source = new EpubBookSource(path)` / `new PdfBookSource(path)`, exposing `_source.Chapters`
(`IReadOnlyList` of `{ Title, Paragraphs }`). It resumes from `Book.LastChapterIndex` /
`LastCharacterOffset`, sets `LastOpenedTime`, lazily fills `ChapterCount`, clears `Finished` on
reopen, and sets `Finished` when paging past the last chapter. The PDF page reader
(`PdfPageReaderScreenViewModel`) is entirely separate: it **persists no position, no `Finished`,
no bookmarks** — it always opens at page 0.

Reader back-navigation: `BookReader` and `PdfReader` are constructed with `GoBooks` hardcoded as
their `goBack`. (The comic `Reader` already solved the general version of this with a
`_screenBeforeReader` capture + `GoBackFromReader` switch — see that code's doc comment for the
"user testing found back went to the wrong place" history.)

**CE note:** ComicRack CE has no prose-reading concept — nothing to port. Confirmed in the
original Novels design spec's CE-verification note.

## Decisions

| Area | Decision |
|---|---|
| **One screen, two modes** | A single `BookDetailScreenViewModel` with `enum BookDetailMode { Book, Series }` (`Mode` + `IsBookMode` / `IsSeriesMode`). One `CurrentScreen = "bookDetail"`. The two modes are `IsVisible`-switched sections in one `BookDetailScreen.axaml`, mirroring `DetailScreenViewModel`'s series/issue split. `LoadBook(int bookId)` → Book mode; `LoadSeries(int bookSeriesId)` → Series mode. |
| **Navigation in** | Books grid card click → **Book mode** (`_goBookDetail(bookId)`, replacing today's `_goReaderForBook`). Grouped-by-Series section header → **Series mode** (`_goBookSeriesDetail(bookSeriesId)`); the "Standalone" bucket header stays inert (no series to open). Home's "Continue Reading — Books" cards are **unchanged** — they still open the reader directly. |
| **Book mode: cover** | 190-wide, same `BookCardSample.CoverBrush` placeholder gradient + `BookCoverImageCache.Get(id)` real cover the grid card uses; eager decode (single item). |
| **Book mode: header** | Title (26px bold) · Author · format badge (`EPUB` / `PDF`, styled like the comic Detail's `metaPill muted`) · a **"Part of *{Series}* ▸"** `Button` shown when `HasSeries`, which calls `LoadSeries(book.BookSeriesId)` and stays on `bookDetail`. |
| **Book mode: meta rows** | `PublishedDate` (formatted, omitted when null) · `AddedTime` ("Added {date}") · `FilePath` shown as caption text with a **"Reveal in Explorer"** button. |
| **Book mode: actions** | Primary **Continue** button — label `Start reading` (never opened), `Continue — Chapter {LastChapterIndex + 1}` (in progress, EPUB), `Continue reading` (in progress, PDF — no chapter number), `Read again` (`Finished`) — opens the reader at the saved position. **Edit** button — visible but `IsEnabled="False"` with `ToolTip.Tip="Book editing is coming soon"` (B2). **Delete Book** — inline confirm (nested `MenuItem` or a two-step button, matching the grid card's existing "Delete Book → Yes, delete this book"), then `RecycleBinHelper.SendToRecycleBin` + row remove + `BookCoverImageCache.Invalidate` + navigate to `books` (the book no longer exists, so the screen can't stay). |
| **Book mode: reading progress** | **EPUB with `ChapterCount > 0`:** a progress bar bound to `LastChapterIndex / max(1, ChapterCount - 1)`, the text "Chapter {LastChapterIndex + 1} of {ChapterCount}", "Last opened {relative date}", and a `Finished` pill when set. **PDF or never-opened:** "Not started" or "Last opened {date}" only — no bar, no chapter count. **All formats:** a **"Mark as finished" / "Mark as unread"** toggle button — *finished* sets `Finished = true`; *unread* sets `Finished = false`, `LastChapterIndex = 0`, `LastCharacterOffset = 0` (leaves `LastOpenedTime` alone — it's a history fact, not a progress fact). One `context.SaveChanges()`, then re-read the bound fields in place. |
| **Book mode: summary** | `Summary` text, or "No summary available." when null/blank. Show-more / show-less toggle past **280** characters, using the same `IsSynopsisExpanded` + `SynopsisToggleLabel` idiom as `MangaDetailScreenViewModel`. |
| **Book mode: chapters** | **EPUB with `_source.Chapters.Count > 1` only.** A list of `BookChapterSummary` rows (`Index`, `Title`), parsed from a throwaway `EpubBookSource` in `LoadBook` (same parse the reader does) and snapshotted into row models — the source is `Dispose()`d before `LoadBook` returns, no live handle kept. Row click → opens the reader **at that chapter** (`new BookPosition(Index, 0)`). Section hidden for PDF and single-chapter EPUBs. |
| **Book mode: bookmarks** | `_book.Bookmarks` ordered by `CreatedTime` descending, as `BookBookmarkSummary` rows (`Excerpt`, `ChapterTitle` resolved from the parsed source, plus `CreatedTime` for a relative-date caption — see "New models" below). Row click → opens the reader **at that bookmark** (`new BookPosition(ChapterIndex, CharacterOffset)`). Per-row context-menu **"Delete"** removes the `BookBookmark` row and refreshes the list. Section hidden when the book has no bookmarks. |
| **Series mode** | Series `Name` (26px) · series `Author` if set · "{n} books" · a `WrapPanel` of the series' books rendered with the **existing `BookCardTemplate`** (extracted to a shared `ResourceDictionary` — see "New/changed files"). Card click → `LoadBook(bookId)`. No progress / summary / chapters / bookmarks in Series mode. |
| **Back link** | Book mode reached from the Books grid: "← Books" → `GoBooks`. Book mode reached from Series mode: "← *{Series}*" → `LoadSeries(thatSeriesId)`; tracked in a `_cameFromSeriesId` field set by the "Part of series" button and cleared by `LoadBook` when entered any other way. Series mode: always "← Books". |
| **Reader entry generalization** | `BookReaderScreenViewModel.LoadBook` and `MainViewModel.GoBookReaderForBook` gain an optional `BookPosition? startAt = null`. `null` = today's behaviour (resume from `Book.LastChapterIndex` / `LastCharacterOffset`). A non-null value overrides the resume position for this open only (nothing new persisted until the user navigates). The PDF path ignores `startAt` — the page reader has no position model. |
| **Reader back-target** | Add `_screenBeforeBookReader` captured in `GoBookReaderForBook` (guarded against self-overwrite like `RememberScreenBeforeReader`), and route both `BookReader` and `PdfReader` back through a new `GoBackFromBookReader()` → returns to `"bookDetail"` (reloading the current book) or `"books"`. Directly mirrors the existing `_screenBeforeReader` / `GoBackFromReader` fix. |
| **No schema change** | Every field this screen shows already exists. `BookBookmarkSummary` gains a `CreatedTime` property (a model, not an entity — no migration). |

## Components

### 1. `BookDetailScreenViewModel` (new)

Ctor: `(Action goBooks, Action<int, BookFormat, BookPosition?> goReaderForBook, Action<int>? goEditBook = null)`.
No `MainViewModel` callback for series navigation: the "Part of series ▸" link and Series-mode card
clicks both just call this VM's own `LoadSeries` / `LoadBook` — the screen is already current, so
there's nothing for `MainViewModel` to route. (`MainViewModel.GoBookSeriesDetailForSeries` exists
only for the *Books grid* header click, and it calls the same public `LoadSeries`.) `goEditBook` is
unused in B1, wired null; it's the B2 seam.

- `enum BookDetailMode { Book, Series }` → `Models/BookDetailMode.cs`.
- `[ObservableProperty]`: `Mode`, cover `Bitmap?` + `IBrush`, `Title`, `Author`, `FormatBadge`,
  `SeriesLinkLabel` (`"Part of {name} ▸"`) + `HasSeries`, `PublishedLabel` + `HasPublished`,
  `AddedLabel`, `FilePath`, `ContinueLabel`, `ProgressFraction`, `ProgressLabel`
  (`"Chapter X of Y"` / `"Not started"` / `"Last opened …"`), `HasChapterProgress`, `IsFinished`,
  `FinishedToggleLabel`, `Summary`, `IsSynopsisExpanded` + `SynopsisToggleLabel` +
  `IsSynopsisToggleVisible`, `HasChapters`, `HasBookmarks`, `SeriesName`, `SeriesAuthor` +
  `HasSeriesAuthor`, `SeriesBookCountLabel`, `BackLabel`.
- Collections: `ObservableCollection<BookChapterSummary> Chapters`,
  `ObservableCollection<BookBookmarkSummary> Bookmarks`,
  `ObservableCollection<BookCardSample> SeriesBooks`.
- `LoadBook(int bookId)`: fresh context; `Include(b => b.BookSeries)` + `Include(b => b.Bookmarks)`;
  populate all Book-mode fields; if `Format == Epub`, `using var src = new EpubBookSource(FilePath)`
  → fill `Chapters` (only when `src.Chapters.Count > 1`) and resolve each bookmark's `ChapterTitle`;
  set `Mode = Book`. Clears `_cameFromSeriesId` unless the caller is the series-link path.
- `LoadSeries(int bookSeriesId)`: fresh context; load the `BookSeries` + `.Books`; fill
  `SeriesBooks` via `BookCardSample.FromBook`; set `Mode = Series`.
- Commands: `Continue`, `MarkFinishedToggle`, `OpenChapter(BookChapterSummary)`,
  `OpenBookmark(BookBookmarkSummary)`, `DeleteBookmark(BookBookmarkSummary)`,
  `OpenSeriesFromLink`, `OpenBookFromSeries(BookCardSample)`, `RevealInExplorer`, `DeleteBook`,
  `ToggleSynopsis`, `GoBack`, `Edit` (disabled).
- `ReloadCurrentBook()` — re-runs `LoadBook(_bookId)`; used by `GoBackFromBookReader` and after
  `MarkFinishedToggle` / `DeleteBookmark` so the screen reflects writes without a full nav round-trip.

### 2. `BookDetailScreen.axaml` (+ `.axaml.cs`) (new)

- Code-behind is the minimal `partial class BookDetailScreen : UserControl { public BookDetailScreen() => InitializeComponent(); }` — **added in the same commit as the `.axaml`** (CLAUDE.md AVLN2000 gotcha).
- `ScrollViewer` → a Book-mode `StackPanel` (`IsVisible="{Binding IsBookMode}"`) and a Series-mode
  `StackPanel` (`IsVisible="{Binding IsSeriesMode}"`), structured like `DetailScreen.axaml`
  (back link, `Grid ColumnDefinitions="190,*"`, cover left, content right).
- Chapter rows: simple `Button.modeOption`-style list rows. Bookmark rows: excerpt (wrapped,
  2 lines max) + chapter title + relative date caption, with a `ContextMenu` "Delete".
- Reuses the shared `BookCardTemplate` for Series-mode cards.

### 3. Shared `BookCardTemplate`

`BooksScreen.axaml` currently defines `BookCardTemplate` inline in its `UserControl.Resources`.
Extract it to `Views/BookCardTemplate.axaml` (a `ResourceDictionary`) and `MergedDictionaries`-
include it in both `BooksScreen.axaml` and `BookDetailScreen.axaml`. The template's
`Command` binding walks `$parent[UserControl]` — keep that working by having each host expose a
`SelectBookCommand` / `DeleteBookCommand` pair with the same names (both VMs already have, or will
have, `SelectBook`; `BookDetailScreenViewModel` maps `SelectBookCommand` → `OpenBookFromSeries`).
*Alternative if the `$parent` coupling proves fussy:* keep two small inline copies, matching the
`LibraryScreen` / `BooksScreen` "own copy of the style classes" precedent already in the codebase.
Implementer's call at plan time.

### 4. `RevealInExplorerHelper` (changed)

Add `RevealBook(Book book)` → `ResolveBookFilePath(book)` (pure: `FilePath` or null) →
`FileExplorer.OpenFolderAndSelect(path)`, mirroring `RevealIssue` / `ResolveIssueFilePath`.

### 5. `BookReaderScreenViewModel` / `PdfPageReaderScreenViewModel` (changed)

- `BookReaderScreenViewModel.LoadBook(int bookId, BookPosition? startAt = null)`: when `startAt`
  is non-null, use it instead of `new BookPosition(clampedLastChapter, LastCharacterOffset)` for
  the initial `_position` (still clamp `ChapterIndex` to the real chapter count). Everything else
  unchanged — `LastOpenedTime`, `ChapterCount`, `Finished = false` still set on open.
- `PdfPageReaderScreenViewModel.LoadBook(int bookId, BookPosition? startAt = null)`: parameter
  accepted and ignored (documented). Keeps the `MainViewModel` call site uniform.

### 6. `BooksScreenViewModel` / `BookCardGroup` / `BooksScreen.axaml` (changed)

- `BooksScreenViewModel` ctor gains `Action<int> goBookDetail` and `Action<int> goBookSeriesDetail`;
  `SelectBook` calls `goBookDetail(book.BookId)`. The `_goReaderForBook` field is **removed** from
  this VM (the grid no longer opens the reader).
- `BookCardGroup` gains `int? BookSeriesId` (null for the "Standalone" bucket and for Author
  grouping). `GroupCards` sets it from the first book's `BookSeriesId` when grouping by Series.
- `BooksScreen.axaml` grouped-view header: wrap the `DockPanel` header in a `Button` (transparent,
  no chrome) bound to a `OpenSeriesCommand` with `CommandParameter="{Binding BookSeriesId}"`, and
  disable it via a `IsEnabled="{Binding BookSeriesId, Converter=...NotNull}"` so Author groups and
  Standalone are non-interactive.

### 7. `MainViewModel` (changed)

- New: `BookDetail` property + `IsBookDetail`, with `"bookDetail"` added to the
  `OnCurrentScreenChanged` `OnPropertyChanged` fan-out. It is a drill-down like `"detail"`, so it
  does **not** go in the `ActiveScreenContent` switch; `MainWindow.axaml` gets its own
  `IsVisible="{Binding IsBookDetail}"` slot for the `BookDetailScreen` view (same as `DetailScreen`).
- `GoBookDetailForBook(int bookId)` → `BookDetail.LoadBook(bookId); CurrentScreen = "bookDetail";`
- `GoBookSeriesDetailForSeries(int bookSeriesId)` → `BookDetail.LoadSeries(bookSeriesId); CurrentScreen = "bookDetail";`
- `Escape` handling and the editor-guard nav table treat `"bookDetail"` as a plain drill-down that
  returns to `"books"`.
- `GoBookReaderForBook(int bookId, BookFormat format, BookPosition? startAt = null)`: capture
  `_screenBeforeBookReader` first; pass `startAt` through to `BookReader.LoadBook` /
  `PdfReader.LoadBook`.
- `GoBackFromBookReader()`: `"bookDetail"` → `BookDetail.ReloadCurrentBook(); CurrentScreen = "bookDetail";`
  else `GoBooks()`. Wire it as the `goBack` for both `BookReader` and `PdfReader` (replacing the
  bare `GoBooks`).
- Construct `BookDetail` with `(GoBooks, GoBookReaderForBook, GoBookSeriesDetailForSeries)`.

### 8. New models

- `Models/BookDetailMode.cs` — the `enum`.
- `BookBookmarkSummary` gains `public DateTime CreatedTime { get; init; }` (the reader's
  `ToSummary` sets it from `bookmark.CreatedTime`; the Details screen shows it as a relative date).

## Data flow

```
BooksScreen card click ─► MainViewModel.GoBookDetailForBook(id)
                          └─► BookDetail.LoadBook(id)  [opens ctx, parses EPUB TOC, disposes source]
                              CurrentScreen = "bookDetail"

BooksScreen Series header ─► MainViewModel.GoBookSeriesDetailForSeries(seriesId)
                             └─► BookDetail.LoadSeries(seriesId); CurrentScreen = "bookDetail"

BookDetail "Part of series ▸" ─► LoadSeries(book.BookSeriesId)   [in-screen, no MainViewModel]
BookDetail Series-mode card ───► LoadBook(id), _cameFromSeriesId retained for the back link

BookDetail Continue ───────► goReaderForBook(id, format, null)          [resume]
BookDetail chapter row ────► goReaderForBook(id, format, new BookPosition(idx, 0))
BookDetail bookmark row ───► goReaderForBook(id, format, new BookPosition(ch, off))
        │
        └─ MainViewModel captures _screenBeforeBookReader = "bookDetail"

Reader back ─► GoBackFromBookReader() ─► BookDetail.ReloadCurrentBook(); CurrentScreen = "bookDetail"

BookDetail Mark finished/unread ─► write Book row, SaveChanges, re-read bound fields in place
BookDetail Delete bookmark ──────► delete BookBookmark row, refresh Bookmarks collection
BookDetail Delete Book ──────────► RecycleBin + row delete + cache invalidate ─► GoBooks
```

## Error handling

- **Book id not found** in `LoadBook` (deleted out from under a stale nav) → `GoBooks()` immediately.
- **`BookSeries` id not found** in `LoadSeries` → `GoBooks()`.
- **`EpubBookSource` throws** (corrupt / missing file) → catch, leave `Chapters` empty, set a
  `ChaptersUnavailable` flag driving a one-line "Chapter list unavailable — the file may be
  missing or damaged." caption in place of the list. Bookmark rows still render (their `Excerpt`
  is stored); `ChapterTitle` falls back to `"Chapter {n + 1}"`.
- **File missing** for Reveal in Explorer → `ResolveBookFilePath` returns null when `FilePath`
  is empty; `FileExplorer.OpenFolderAndSelect` already no-ops a nonexistent path. No crash, no toast.
- **PDF book**: chapters + bookmarks sections simply hidden; progress shows "Last opened …" /
  "Not started"; the finished toggle still works.

## Testing

**`BookDetailScreenViewModelTests`** (new)
- `LoadBook` populates title / author / series link / added label / format badge from a seeded book.
- Progress: `ChapterCount = 10`, `LastChapterIndex = 3` → `ProgressFraction ≈ 3/9`,
  `ProgressLabel == "Chapter 4 of 10"`.
- `Finished == true` → `ContinueLabel == "Read again"`, `IsFinished == true`.
- `MarkFinishedToggle` from not-finished → `Finished` true in the DB; from finished →
  `Finished` false **and** `LastChapterIndex` / `LastCharacterOffset` reset to 0.
- Never-opened book → `ProgressLabel == "Not started"`, `HasChapterProgress == false`.
- PDF book → `Chapters` empty, `HasChapters == false`, `HasBookmarks == false` (no PDF bookmarks
  exist), `ProgressLabel` is the last-opened / not-started line.
- Summary under 280 chars → `IsSynopsisToggleVisible == false`; over → true; `ToggleSynopsis`
  flips `SynopsisToggleLabel`.
- Bookmarks: seeded `BookBookmark` rows come back newest-first with `ChapterTitle` resolved from
  a fake/real source; `DeleteBookmark` removes the row and the DB record.
- `LoadSeries` lists exactly the `BookSeries.Books`, each as a `BookCardSample`;
  `SeriesBookCountLabel == "3 books"`.
- Back label: after `LoadBook` direct → `"← Books"`; after series-link path → `"← {SeriesName}"`.

**`BooksScreenViewModelTests`** (extend)
- `SelectBook` invokes the `goBookDetail` callback with the book id, **not** any reader callback.
- Grouped by Series: `BookCardGroup.BookSeriesId` is set for real series, null for "Standalone".
- (Header-click command wiring is covered by the VM exposing `OpenSeriesCommand`; a test invokes
  it with a series id and asserts the `goBookSeriesDetail` callback fires.)

**`RevealInExplorerHelperTests`** (extend)
- `ResolveBookFilePath` returns the path for a book with a `FilePath`, null for an empty one.

**`BookReaderScreenViewModelTests`** (extend)
- `LoadBook(id, new BookPosition(2, 50))` → reader starts on chapter 2 at offset 50.
- `LoadBook(id)` (no position) → still resumes from `Book.LastChapterIndex` / `LastCharacterOffset`.
- `LoadBook(id, startAt)` with a `ChapterIndex` past the real chapter count → clamped, no throw.

**`MainViewModelTests`** (extend, if the suite covers navigation)
- `GoBookDetailForBook` sets `CurrentScreen == "bookDetail"`.
- Reader opened from `bookDetail` → reader back returns to `"bookDetail"`, not `"books"`.

**Build**: `BookDetailScreen.axaml` + `.axaml.cs` land in one commit; verify the app launches
(AVLN2000 guard — treat "0 Errors" as insufficient, per CLAUDE.md).

**Manual on-screen**
- Grid card → Details → Continue → reader → back lands on Details (not the grid).
- Grouped by Series → header click → Series mode → card → Book mode → "← {Series}" back to Series
  mode → "← Books" to the grid.
- Chapter row and bookmark row each open the reader at the right place.
- Mark finished / unread round-trips and the Home "Continue Reading — Books" strip reflects it.
- A PDF book shows the reduced view (metadata + summary + Read), no chapters/bookmarks/bar.
- Both skins (light / dark).
- "Part of series" link absent for a standalone book.

## Risks / notes

- **EPUB parse on every Book-mode open.** Identical cost to opening the reader, single book,
  one-time — acceptable. The source handle is disposed inside `LoadBook`; only row snapshots are
  kept. PDF books skip the parse entirely.
- **`BookCardTemplate` extraction vs. two copies.** The shared `ResourceDictionary` is cleaner but
  the template's `$parent[UserControl]` command binding couples it to whatever host exposes
  `SelectBookCommand`. If that proves brittle at implementation time, fall back to two inline
  copies — the codebase already has that precedent (`LibraryScreen` / `BooksScreen` each keep
  their own copy of the toolbar style classes). Decide in the plan.
- **PDF read-state is genuinely absent**, not a bug this screen should hide. The reduced PDF view
  is the honest presentation; the manual finished toggle is the one real read-state control a PDF
  book gets, and it does drive the Home strip.
- **`MarkFinishedToggle` "unread" resets progress.** Deliberate — "mark as unread" that left you
  30% in would be a lie. `LastOpenedTime` is kept (it's history, not progress).
- **YAGNI cuts:** no custom book covers (deferred — `BookCoverThumbnailService` has no custom-
  cover setter today), no metadata editing (B2), no Related/Activity tabs (books have no
  cross-references), no per-chapter read state, no "books in this series" grid embedded in Book
  mode (the "Part of series" link is one click).
