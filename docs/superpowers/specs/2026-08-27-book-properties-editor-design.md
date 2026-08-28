# Book Properties Editor Overlay (Piece B2) — Design

**Status:** Implemented 2026-08-27. App.Tests 1015 green, Data.Tests 457 green; app launches clean
(15:54 run: "Building main window" → "Startup complete", new overlay + view weave verified). Full
on-screen click-through not done this session (no computer-use).

Mid-implementation the shared per-user DB was briefly broken by a **concurrent "Collections"
session** — it applied migration `20260827134456_AddCollections` (not on this branch), whose SQLite
table-rebuild dropped `AppSettings.LibraryActiveCategoryId`, crashing startup in
`SkinService.ApplyPersistedSettings`. Nothing in B1/B2 touches `Paperbunkr.Data`. The DB was then
repaired (history rolled back to `AddBooksBrowseState`, column restored) and the app launches fine.

**Implementation deviations:**
- Corner-X / goBack use one `[RelayCommand] CloseBookPropertiesOverlay()` (Reading List pattern),
  not a separate cancel-invoking command.
- `MetadataEditHistoryEntry.Target` defaults to `Issue`; the existing issue call sites were left
  untouched (confirmed object-initializer syntax).
- `ResolveSeries` matches an existing `BookSeries` case-insensitively by loading the (small) table
  and comparing in memory — same approach as `BookFolderScanService.ScanAll`.

**Piece B2 of the Books-section follow-up.** B1 (the Book Details screen,
`2026-08-27-book-details-screen-design.md`) shipped a disabled "Edit" button as the seam for this.
B2 is the **first in-app editor for book metadata** — every `Book`/`BookSeries` field is
scan-derived and read-only today. A floating overlay, buffered Load/edit/Save/Cancel, composited in
`MainWindow` behind the standard backdrop exactly like `ReadingListPropertiesOverlay`.

## Background

`ReadingListPropertiesOverlay` + `ReadingListPropertiesScreenViewModel` are the pattern: ctor
`(Action goBack)` + an internal `(Action goBack, Func<PaperbunkrDbContext> contextFactory)` test
seam; `Load(int id)` fills buffered `[ObservableProperty]` fields; `Save` writes + calls `goBack`;
`Cancel` just calls `goBack`; a corner "X" button routes to `CancelCommand`. `MainWindow.axaml`
wraps it in `<Border IsVisible="{Binding IsReadingListPropertiesOverlayOpen}" Background="#B0000000">`
with a `Grid` centering the panel and a `Button.rail` close button at the top-right.
`MainViewModel` holds `IsReadingListPropertiesOverlayOpen`, `OpenReadingListPropertiesOverlay(int)`,
`CloseReadingListPropertiesOverlay()` (which also reloads the screen underneath), and an `Escape`
branch.

The issue editors (`IssuePropertiesScreenViewModel`) add: a `_isDirty` flag →
`HasUnsavedChanges()`, consulted by `MainViewModel.TryLeaveCurrentEditor` to raise a discard-confirm
banner before rail-nav; and one `MetadataEditHistoryService.Record(...)` call per Save for
undo/redo.

`MetadataEditHistoryService` (session-only in-memory `Stack` pair, `Shared` app-wide instance):
`Record(description, before, after)` where `before`/`after` are
`Dictionary<int, Dictionary<string,string?>>` (entityId → field→value); `Undo`/`Redo` pop and
`Apply` the snapshot; `CaptureSnapshot(Issue)` builds the per-issue dict from `BulkFieldRegistry`.
`Apply` currently hard-loads `context.Issues`. `MainViewModel` exposes rail `Undo`/`Redo` commands
+ "Undone"/"Redone" toasts + `RefreshAfterHistoryChange()` (repaints Detail/Library).

**Books scan is add-only** (`BookFolderScanService.ScanAll` skips any file already in the library by
path, never re-reads metadata) — so manual edits are permanent and safe; **no "locked field" /
override flag is needed.**

`Book`: `Id`, `BookSeriesId?`/`BookSeries?`, `Title`, `Author?`, `Format`, `FilePath`,
`CoverImagePath?`, `Summary?`, `PublishedDate?`, `AddedTime`, read-state, `Bookmarks`.
`BookSeries`: `Name`, `SortName?`, `Author?`, `Books`.

`BookCoverThumbnailService` today: only `TryGenerateThumbnail(bookId, filePath, format)` (EPUB
embedded cover; PDF has none — PDF books show no cover at all). `BookCoverImageCache`: only
`Invalidate` (memory + file). The comic side's `CoverThumbnailService` has the full pattern to
mirror: `TrySetCustomCover` (scale + write JPEG, `InvalidateMemoryOnly`) and
`ResetCover(id, filePath)` (`Invalidate` then regenerate).

**CE note:** ComicRack CE has no prose-reading concept and no undo/redo for metadata (verified
against `ComicBookDialog.cs`) — nothing to port for either.

## Decisions

| Area | Decision |
|---|---|
| **Overlay, not a screen** | `BookPropertiesOverlay` composited in `MainWindow` behind `#B0000000`, corner "X" close, buffered Save/Cancel — a direct clone of the Reading List properties overlay. Not a `CurrentScreen` value. |
| **Fields — book** | Title (**required**, non-empty — blank blocks Save with a toast, overlay stays open), Author (nullable), Summary (nullable, multi-line), Published date (`PublishedDate`, nullable, an Avalonia `DatePicker` bound via a `DateTimeOffset?`). |
| **Fields — series membership** | One free-text **series name** box. On Save: blank → `book.BookSeriesId = null` (detach to standalone); non-blank matching an existing `BookSeries` by **case-insensitive name** → attach to it; non-blank with no match → create `new BookSeries { Name }` and attach. **The name box never renames** the book's current series — retyping always means "put this book in the series called *that*". |
| **Fields — series row** | `SeriesAuthor` and `SeriesSortName` inputs, labelled **"Series author"** / **"Series sort name"** so it's clear they're not book-level. On Save they're written onto the **post-save resolved** `BookSeries` row (so they affect every book in that series). Both inputs are disabled while the series-name box is blank. |
| **Fields — cover** | A `70×100` preview + **"Change Cover…"** (`FilePickerService.PickImageFileAsync`, buffered — nothing touches disk until Save) + **"Reset"**. Works for **EPUB and PDF** (a PDF book otherwise has no cover at all — real gain). Reset = regenerate the auto cover for EPUB, clear it for PDF. |
| **Entry points** | (1) The B1 Details **"Edit"** button — drop its `IsEnabled="False"`, bind its existing `EditCommand` (already calls `_goEditBook(_bookId)`). (2) A new **"Edit…"** item on the Books grid card context menu (beside "Delete Book"). (3) A new **"Edit…"** item on Book-Details **Series-mode** card context menus. All route through `MainViewModel.GoBookPropertiesForBook(int bookId)`. |
| **Undo/redo** | **In scope.** `MetadataEditHistoryService` is *generalised*, not duplicated (see Components §3). Undo/redo covers the **book row**: Title, Author, Summary, Published date, `BookSeriesId` (membership). It does **not** roll back **series Author/SortName** (a shared-row edit visible to siblings — not "this book's history") or **cover** changes (a file-cache write). This mirrors how `CaptureSnapshot(Issue)` already scopes itself to a field registry, not the whole entity. |
| **Unsaved-changes guard** | `HasUnsavedChanges()` (buffer vs. an on-`Load` snapshot). `MainViewModel.TryLeaveCurrentEditor` gains `|| (IsBookProperties && BookProperties.HasUnsavedChanges())`; `LeaveAndNavigate` clears `IsBookPropertiesOverlayOpen`. `Escape` gains a `IsBookProperties` branch. |
| **No bulk edit** | Single book only. A book equivalent of `BulkIssuePropertiesScreen` is a separate later spec if ever wanted. |
| **Empty series rows** | Detach/reassign can leave a `BookSeries` with zero books. B2 leaves them (nothing prunes comic `Series` either) — a cleanup pass is out of scope. |
| **No schema change** | Every edited field already exists. |

## Components

### 1. `BookPropertiesScreenViewModel` (new)

Ctor `(Action goBack, Action<string,string>? notify = null, MetadataEditHistoryService? history = null)`
+ internal `(Action goBack, Func<PaperbunkrDbContext> contextFactory, Action<string,string>? notify, MetadataEditHistoryService? history)`.
`_history = history ?? MetadataEditHistoryService.Shared`.

- `[ObservableProperty]`: `HeaderLabel`, `Title`, `Author`, `Summary`, `PublishedDate`
  (`DateTimeOffset?`), `SeriesName`, `SeriesAuthor`, `SeriesSortName`, `HasSeriesName`
  (`OnSeriesNameChanged` recomputes it → drives the two series-field `IsEnabled` binds),
  `CoverPreview` (`Bitmap?`).
- Private: `_bookId`, `_pendingCoverImagePath` (buffered pick), `_resetCoverRequested` (bool),
  `_beforeSnapshot` (`Dictionary<string,string?>`), `_loadSnapshot` (for `HasUnsavedChanges`).
- `Load(int bookId)`: fresh context, `Include(b => b.BookSeries)`; fill every buffered field;
  `CoverPreview = BookCoverImageCache.Get(bookId)`; `_beforeSnapshot = BookMetadataSnapshot.Capture(book)`;
  `_loadSnapshot = CurrentBuffer()`; clear `_pendingCoverImagePath` / `_resetCoverRequested`.
- `HasUnsavedChanges()` → `!CurrentBuffer().SequenceEqual(_loadSnapshot)` OR
  `_pendingCoverImagePath is not null` OR `_resetCoverRequested`.
- `ChangeCoverAsync` → `PickImageFileAsync`; on pick set `_pendingCoverImagePath`, `_resetCoverRequested = false`,
  `CoverPreview = new Bitmap(path)` (try/catch). `ResetCoverPreview` → `_pendingCoverImagePath = null`,
  `_resetCoverRequested = true`, `CoverPreview = null` (or a regenerated preview — cheap to just null it).
- `Save`:
  1. `Title` blank → `notify("Can't save", "Title can't be empty.")`, return (overlay stays open).
  2. Fresh context, load book `Include(BookSeries)`.
  3. Resolve series per the table above (create / reuse / detach); apply `SeriesAuthor`/`SeriesSortName`
     to the resolved row when non-null series.
  4. Write `Title`/`Author`/`Summary`/`PublishedDate` (`?.UtcDateTime`).
  5. `SaveChanges()`.
  6. `_history.RecordBookEdit($"Edited \"{Title}\"", _bookId, _beforeSnapshot, BookMetadataSnapshot.Capture(book))`.
  7. Cover: `_pendingCoverImagePath is {} p` → `new BookCoverThumbnailService().TrySetCustomCover(_bookId, p)`;
     else `_resetCoverRequested` → `new BookCoverThumbnailService().ResetCover(_bookId, book.FilePath, book.Format)`.
  8. `goBack()`.
- `Cancel` → `goBack()` (writes nothing; pending pick / reset flag discarded for free).

### 2. `BookPropertiesOverlay.axaml` (+ `.axaml.cs`)

`Border.floatingPanel` `Width="560" MaxHeight="680"`, `DockPanel`:
- Top: `HeaderLabel` (17px bold) + a 1px rule.
- Bottom: `Button.ghost` "Cancel" + `Button.primary` "Save", right-aligned.
- Middle `ScrollViewer`: a `WrapPanel` of `StackPanel.field` (NAME / AUTHOR / PUBLISHED), then
  `Border.groupBox` sections — **Summary** (multi-line `TextBox`), **Series** (name box + "Series
  author" + "Series sort name", the latter two `IsEnabled="{Binding HasSeriesName}"`), **Cover**
  (`70×100` preview + "Change Cover…" + "Reset").
- Code-behind: minimal `InitializeComponent` — **same commit as the `.axaml`** (AVLN2000 guard).

### 3. Undo/redo generalisation

- **`Models/MetadataEditTarget.cs`** (new): `enum MetadataEditTarget { Issue, Book }`.
- **`MetadataEditHistoryEntry`**: add `public MetadataEditTarget Target { get; init; } = MetadataEditTarget.Issue;`
  — the default keeps every existing issue call site compiling and behaving unchanged.
- **`Models/BookMetadataSnapshot.cs`** (new static): `Capture(Book) -> Dictionary<string,string?>`
  with keys `Title`, `Author`, `Summary`, `PublishedDate` (ISO-8601 round-trip or null),
  `BookSeriesId` (`int` as string or null); `Apply(Book, dict)` parses them back onto the entity.
  Mirrors `MetadataEditHistoryService.CaptureSnapshot`.
- **`MetadataEditHistoryService`**:
  - `RecordBookEdit(string description, int bookId, Dictionary<string,string?> before, Dictionary<string,string?> after)`
    → `_undoStack.Push(new() { Description = description, Target = MetadataEditTarget.Book, Before = new() {[bookId]=before}, After = new() {[bookId]=after} }); _redoStack.Clear();`
  - `Undo`/`Redo`: pass `entry.Target` into `Apply`.
  - `Apply(factory, target, snapshots)`: `target == Book` → `context.Books.Where(b => snapshots.Keys.Contains(b.Id))`
    + `BookMetadataSnapshot.Apply` per row; else the existing `context.Issues.Include(...)` path.
    One `SaveChanges()` either way.
- **`MainViewModel.RefreshAfterHistoryChange`**: add `else if (IsBookDetail) BookDetail.ReloadCurrentBook();`
  and `else if (IsBooks) Books.LoadFromDatabase();`. The rail `Undo`/`Redo` commands + toasts need
  no change.

### 4. Cover-service additions

- `BookCoverImageCache.InvalidateMemoryOnly(int bookId)` → `_cache.Remove(bookId)` only (new).
- `BookCoverThumbnailService.TrySetCustomCover(int bookId, string sourceImagePath)` — port of the
  comic method: `Bitmap` → scale to the same longest-edge target → `Save` JPEG to
  `BookCoverThumbnailPaths.GetCachePath(bookId)` → `BookCoverImageCache.InvalidateMemoryOnly(bookId)`;
  `try/catch` → false. (Constants: reuse this class's existing thumbnail-size / JPEG-quality values.)
- `BookCoverThumbnailService.ResetCover(int bookId, string? filePath, BookFormat format)` —
  `BookCoverImageCache.Invalidate(bookId)` (deletes the file) then `TryGenerateThumbnail(bookId, filePath, format)`
  (regenerates for EPUB; no-op false for PDF, leaving it blank — the honest reset).

### 5. `MainViewModel` + `MainWindow.axaml` wiring

- `BookProperties = new BookPropertiesScreenViewModel(CloseBookPropertiesOverlay, ShowToast)` (the
  `_history` default is `Shared`, same as the issue editors).
- `[ObservableProperty] bool _isBookPropertiesOverlayOpen;` + `OnChanged` → `OnPropertyChanged(nameof(IsBookProperties))`;
  `public bool IsBookProperties => IsBookPropertiesOverlayOpen;`
- `GoBookPropertiesForBook(int bookId)` → `BookProperties.Load(bookId); IsBookPropertiesOverlayOpen = true;`
- `[RelayCommand] CloseBookPropertiesOverlay()` → `IsBookPropertiesOverlayOpen = false;` then
  `if (IsBookDetail) BookDetail.ReloadCurrentBook(); else Books.LoadFromDatabase();` — this single
  method is **both** the VM's `goBack` callback (Save and Cancel share it) **and** the corner-X
  button's command, exactly as `CloseReadingListPropertiesOverlay` does. (Closing via X therefore
  behaves like Cancel: `Save` writes before calling back, `Cancel` writes nothing.)
- `Escape`: add `else if (IsBookProperties) BookProperties.CancelCommand.Execute(null);`
- `TryLeaveCurrentEditor`: `hasUnsavedChanges |= IsBookProperties && BookProperties.HasUnsavedChanges();`
  and `LeaveAndNavigate` sets `IsBookPropertiesOverlayOpen = false;`
- Construct `BookDetail` with `GoBookPropertiesForBook` as its `goEditBook` arg (was `null`).
- `BooksScreenViewModel` ctor gains `Action<int> goEditBook`; `[RelayCommand] EditBook(int bookId) => _goEditBook(bookId);`
- `MainWindow.axaml`: one more `<Border IsVisible="{Binding IsBookPropertiesOverlayOpen}" Background="#B0000000">`
  block with `BookPropertiesOverlay` + the standard corner close `Button.rail`, cloned from the
  Reading List block.

### 6. `BooksScreen.axaml` / `BookDetailScreen.axaml` context menus

- `BooksScreen.axaml` `BookCardTemplate` `ContextMenu`: add `<MenuItem Header="Edit…" Command="{Binding $parent[UserControl].((vm:BooksScreenViewModel)DataContext).EditBookCommand}" CommandParameter="{Binding BookId}" />`
  above the existing "Delete Book".
- `BookDetailScreen.axaml` Series-mode card: add a `ContextMenu` with an "Edit…" `MenuItem` bound to
  a new `BookDetailScreenViewModel.EditBookInSeriesCommand(BookCardSample)` → `_goEditBook(card.BookId)`
  (the VM already holds `_goEditBook`).

## Data flow

```
Details "Edit" / grid ctx "Edit…" / series-card ctx "Edit…"
        └─► MainViewModel.GoBookPropertiesForBook(id)
            └─► BookProperties.Load(id)  [buffer fields + _beforeSnapshot; nothing on disk]
                IsBookPropertiesOverlayOpen = true

Save ─► resolve/attach series ─► write book fields ─► SaveChanges
      ─► _history.RecordBookEdit(before, after)          [book-row fields only]
      ─► apply pending cover pick / reset                 [file cache]
      ─► CloseBookPropertiesOverlay ─► reload Details or grid

Cancel / X / Escape ─► CancelCommand ─► goBack (writes nothing)

rail Undo/Redo ─► _history.Undo/Redo ─► Apply(Book, snapshot) ─► SaveChanges
             ─► RefreshAfterHistoryChange ─► BookDetail.ReloadCurrentBook / Books.LoadFromDatabase
```

## Error handling

- **Book id not found** in `Load` or `Save` → close the overlay (`goBack`), no crash.
- **Blank Title on Save** → toast, overlay stays open, nothing written.
- **Bad image file** in `ChangeCoverAsync` → `try/catch`, `_pendingCoverImagePath` stays null,
  preview unchanged.
- **`TrySetCustomCover` fails** (unreadable image) → returns false; the book keeps its old cover;
  no toast (best-effort, same as the comic path).
- **Undo/Redo with an empty stack** → existing "Nothing to undo/redo" toast.
- **Undo of a book whose series was since deleted** — can't happen in B2 (nothing deletes
  `BookSeries`); if `BookSeriesId` points at a now-missing row after some future feature,
  `BookMetadataSnapshot.Apply` sets the FK and EF's next read simply yields a null nav — no crash.

## Testing

**`BookPropertiesScreenViewModelTests`** (new)
- Each field round-trips Load → edit → Save (re-read from a fresh context).
- Blank Title → `Save` writes nothing, `notify` fired.
- Series: blank→detach; new name→row created and attached; existing name (different case)→reused,
  not duplicated.
- `SeriesAuthor`/`SeriesSortName` land on the resolved row and a **sibling** book sees them.
- `HasSeriesName` false when the box is blank.
- `HasUnsavedChanges()` — false right after `Load`, true after any field edit, true after a cover
  pick, false again if edited back to the loaded values.
- `Cancel` after edits → DB unchanged.
- `Save` records **exactly one** history entry; its `before`/`after` contain the five book keys and
  **not** `SeriesAuthor`.
- Cover: `_pendingCoverImagePath` applied on Save (spy/fake service), discarded on Cancel.

**`MetadataEditHistoryServiceTests`** (extend)
- `RecordBookEdit` → `Undo` restores `Title`/`Author`/`Summary`/`PublishedDate`/`BookSeriesId`;
  `Redo` re-applies.
- One shared stack: a book edit then an issue edit undo in LIFO order, each hitting the right table.
- `RecordBookEdit` clears the redo stack.

**`BookMetadataSnapshotTests`** (new) — `Capture` then `Apply` on a fresh entity reproduces every
field, including null Author/Summary/PublishedDate and null `BookSeriesId`.

**`BookCoverThumbnailServiceTests`** (extend) — `TrySetCustomCover` writes the cache file for both
formats; `ResetCover` regenerates for EPUB, leaves nothing for PDF. (Redirect
`BookCoverThumbnailPaths.ThumbnailDirectory` to a temp folder, as those tests already do.)

**`BooksScreenViewModelTests`** (extend) — `EditBookCommand` invokes the callback with the id.

**`MainViewModelTests`** (extend if nav-covered) — `GoBookPropertiesForBook` opens the overlay;
`HasUnsavedChanges` blocks rail nav (discard-confirm banner).

**Build** — `BookPropertiesOverlay.axaml` + `.axaml.cs` in one commit; launch the exe (0 Errors
insufficient, CLAUDE.md).

**Manual** — edit each field from all three entry points; blank-Title block; move a book between
series and confirm a sibling; custom cover on an EPUB and a PDF; Reset; Cancel discards; Escape;
rail-nav discard prompt; **rail Undo after a book edit restores it and repaints Details**; Redo;
both skins.

## Risks / notes

- **Series Author/SortName + cover are outside undo/redo** — deliberate, documented in the UI by
  the "Series …" labels and by cover being a separate control block. A user who changes a series
  author and hits Undo gets the book fields back but not the series author; acceptable and
  consistent with the comic editor's registry-scoped undo.
- **`MetadataEditHistoryEntry.Target` default** must be `Issue` so the ~4 existing issue call sites
  (`IssuePropertiesScreenViewModel`, `BulkIssuePropertiesScreenViewModel`) need no edit. Verified:
  they use object-initializer syntax without `Target`.
- **`BookPropertiesOverlay` is the 3rd new `.axaml` in this Books arc** — the AVLN2000 gotcha bites
  per-file; land code-behind in the same commit and verify by launch.
- **YAGNI cuts:** no bulk edit, no series-rename surface, no empty-series pruning, no
  cover-in-undo, no re-scan-clobber protection (scan is add-only).
