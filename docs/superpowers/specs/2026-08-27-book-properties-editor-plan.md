# Book Properties Editor Overlay (Piece B2) — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-27-book-properties-editor-design.md*

**Status: done 2026-08-27.** Steps 1-7 implemented; Step 8 sweep: App.Tests 1015 green, Data.Tests
457 green, app launches clean (after a concurrent-session live-DB breakage was repaired — see the
design doc's Status note). Full on-screen click-through not done this session (no computer-use).

## Step 1: Undo/redo generalisation
**Files:** `src/Paperbunkr.App/Models/MetadataEditTarget.cs` (new),
`src/Paperbunkr.App/Models/MetadataEditHistoryEntry.cs` (edit),
`src/Paperbunkr.App/Models/BookMetadataSnapshot.cs` (new),
`src/Paperbunkr.App/Services/MetadataEditHistoryService.cs` (edit)
**What:** `enum MetadataEditTarget { Issue, Book }`. `MetadataEditHistoryEntry` gains
`Target { get; init; } = MetadataEditTarget.Issue`. `BookMetadataSnapshot.Capture(Book)` /
`Apply(Book, dict)` over Title/Author/Summary/PublishedDate(ISO)/BookSeriesId. Service gains
`RecordBookEdit(desc, bookId, before, after)` (pushes `Target = Book` entry); `Undo`/`Redo` pass
`entry.Target` to `Apply`, which branches Book→`context.Books` / Issue→existing path.
**Depends on:** none
**Verify:** `MetadataEditHistoryServiceTests` (extend), `BookMetadataSnapshotTests` (new).

## Step 2: Cover-service additions
**Files:** `src/Paperbunkr.App/Services/BookCoverImageCache.cs` (edit),
`src/Paperbunkr.App/Services/BookCoverThumbnailService.cs` (edit)
**What:** `BookCoverImageCache.InvalidateMemoryOnly(int)`. `BookCoverThumbnailService.TrySetCustomCover(int bookId, string sourceImagePath)`
(port of `CoverThumbnailService.TrySetCustomCover`) and `ResetCover(int bookId, string? filePath, BookFormat format)`
(`Invalidate` then `TryGenerateThumbnail`).
**Depends on:** none
**Verify:** `BookCoverThumbnailServiceTests` (extend) — custom cover both formats; reset EPUB vs PDF.

## Step 3: BookPropertiesScreenViewModel
**Files:** `src/Paperbunkr.App/ViewModels/BookPropertiesScreenViewModel.cs` (new)
**What:** Per design §Components 1. Ctor `(Action goBack, Action<string,string>? notify = null, MetadataEditHistoryService? history = null)`
+ internal contextFactory seam. `Load`/`Save`/`Cancel`/`HasUnsavedChanges`/`ChangeCoverAsync`/`ResetCoverPreview`.
Buffered snapshots (`_beforeSnapshot` for history, `_loadSnapshot` for dirty check). Series
resolve/create/detach in `Save`; `RecordBookEdit` after `SaveChanges`; cover applied last.
**Depends on:** Steps 1, 2
**Verify:** `BookPropertiesScreenViewModelTests` (new) — Step 7.

## Step 4: BookPropertiesOverlay.axaml (+ code-behind)
**Files:** `src/Paperbunkr.App/Views/BookPropertiesOverlay.axaml` (new),
`src/Paperbunkr.App/Views/BookPropertiesOverlay.axaml.cs` (new — **same commit**, AVLN2000 guard)
**What:** Clone `ReadingListPropertiesOverlay.axaml` structure: `Border.floatingPanel` 560 wide,
DockPanel (header / Cancel+Save / ScrollViewer body). Fields NAME/AUTHOR/PUBLISHED (`DatePicker`),
groupBoxes Summary / Series (name + "Series author" + "Series sort name", latter two
`IsEnabled="{Binding HasSeriesName}"`) / Cover (preview + Change + Reset). Minimal code-behind.
**Depends on:** Step 3
**Verify:** `dotnet build` then **launch the exe**.

## Step 5: MainViewModel + MainWindow wiring
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit),
`src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
**What:** `BookProperties` VM property + `new BookPropertiesScreenViewModel(CloseBookPropertiesOverlay, ShowToast)`.
`_isBookPropertiesOverlayOpen` + `IsBookProperties`. `GoBookPropertiesForBook(int)`.
`[RelayCommand] CloseBookPropertiesOverlay()` (both goBack + corner-X, reloads Details-or-grid).
`Escape` branch. `TryLeaveCurrentEditor` unsaved-changes OR + `LeaveAndNavigate` clears the flag.
Construct `BookDetail` with `GoBookPropertiesForBook` (was null). `BooksScreenViewModel` ctor gains
`Action<int> goEditBook` + `EditBookCommand`. `MainWindow.axaml`: cloned overlay `Border` block.
**Depends on:** Steps 3, 6
**Verify:** `MainViewModelTests` (extend if nav-covered); build + launch.

## Step 6: Context-menu entry points + B1 Edit button
**Files:** `src/Paperbunkr.App/Views/BooksScreen.axaml` (edit),
`src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/BookDetailScreen.axaml` (edit),
`src/Paperbunkr.App/ViewModels/BookDetailScreenViewModel.cs` (edit)
**What:** `BooksScreen` `BookCardTemplate` ctx menu: "Edit…" → `EditBookCommand` (param `BookId`).
`BooksScreenViewModel`: `_goEditBook` field + `[RelayCommand] EditBook(int)`. `BookDetailScreen`
Series-mode card: `ContextMenu` with "Edit…" → new `EditBookInSeriesCommand`. `BookDetailScreenViewModel`:
`[RelayCommand] EditBookInSeries(BookCardSample?)` → `_goEditBook(card.BookId)`. B1's Details "Edit"
button: drop `IsEnabled="False"` + tooltip, bind `Command="{Binding EditCommand}"`.
**Depends on:** none (callbacks wired in Step 5)
**Verify:** `BooksScreenViewModelTests` (extend) — `EditBookCommand` fires callback.

## Step 7: BookPropertiesScreenViewModelTests
**Files:** `src/Paperbunkr.App.Tests/BookPropertiesScreenViewModelTests.cs` (new)
**What:** Per design §Testing. Temp SQLite + `DatabasePathOverride`, `[Collection(nameof(AvaloniaTestCollection))]`.
Field round-trips; blank Title blocks; series create/reuse(case-insensitive)/detach; series
author/sortname reach a sibling; `HasUnsavedChanges` transitions; Cancel writes nothing; exactly
one history entry recorded, `before`/`after` shape; cover pending-path applied/discarded (fake or
temp-dir `BookCoverThumbnailPaths`).
**Depends on:** Step 3
**Verify:** `dotnet test --filter BookPropertiesScreenViewModelTests`.

## Step 8: Full sweep + docs
**Files:** design doc status line, plan status line, `project_paperbunkr_books_browse_chrome` memory
**What:** `dotnet build` (delete `obj/Debug/net10.0/Paperbunkr.App.dll`+`.pdb` if a XAML-compile
failure masks). `dotnet test` App.Tests + Data.Tests. Launch: edit from all 3 entry points, blank
Title, series move + sibling, custom cover EPUB & PDF, Reset, Cancel, Escape, rail-nav prompt,
**rail Undo restores a book edit + repaints Details**, Redo, both skins. Update docs + memory.
**Depends on:** all
**Verify:** green suite + manual checklist.

## Notes / discovered surface
- `ReadingListPropertiesScreenViewModel` is the closest precedent for the VM; `IssuePropertiesScreenViewModel`
  for the `_history` + `HasUnsavedChanges` bits.
- `MainWindow.axaml` overlay blocks live ~lines 644-770; corner close button uses
  `/Assets/Icons/Close_Circle.png` via `Border.OpacityMask`.
- `ShowToast` is `Action<string,string>` (title, body).
- `MetadataEditHistoryEntry` existing call sites: `IssuePropertiesScreenViewModel` ~line 595,
  `BulkIssuePropertiesScreenViewModel` — object-initializer without `Target`, so the default covers them.
- Concurrent worktree sessions under `.claude/worktrees/` — keep edits to `src/Paperbunkr.App*` + docs.
