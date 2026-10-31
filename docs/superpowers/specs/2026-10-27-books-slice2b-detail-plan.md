# Books Slice 2b (Book Detail) — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-27-books-slice2b-detail-design.md*

Surveyed 2026-10-27. Relevant facts:

- `BookDetailScreen.axaml` Book mode is a hero + band + a stack of cards: reading progress, "Your shelf" (Slice 1), Chapters, Chapters-unavailable note, Bookmarks. `BookDetailScreenViewModel` already parses the file once in `LoadChaptersAndBookmarks` (chapter titles; `BookChapterSummary{Index,Title,IsActive,PartTitle}`; bookmarks as `BookBookmarkSummary`), has `OpenChapter`, `OpenBookmark`, `DeleteBookmark`, `ExportAnnotations` (Markdown/CSV/JSON through `AnnotationExportService`), `Continue()` = `_goReaderForBook(id, format, null)` and the hero primary action `new DetailHeroAction(ContinueLabel, ContinueCommand …)`.
- `BookHighlight` has `ChapterIndex, BlockId, StartOffset, Length, Color, Note, Excerpt, CreatedTime`; `BookAnnotationImage` has `PageIndex, ImagePath, Note, CreatedTime`; `BookPosition(ChapterIndex, BlockId, Fraction)` is what the reader's `startAt` takes.
- Reading history for a book is `ReadingEvent` rows (`ItemType = Novel`, `ItemId = Book.Id`; `Opened` rows carry `PagesRead` and `ActiveSeconds` after a session ends; pages are estimated at 1800 characters via `ReadingPageMath`).
- The Books grid has `AuthorFilter` (Slice 2a). MainViewModel owns navigation and already wires `Books`/`BookDetail` callbacks (`GoMatchBook` pattern).
- Comics' Detail tabs use the shared `Button.tab` family inside one `StackPanel` (`TabStrip.Step` groups by visual parent).

## Ground rules
Same as 2a: `paperbunkr-design-language` for UI, new `.axaml` with its code-behind, touched-class tests only, patch scripts written with the Write tool using raw strings (a backslash-b in a normal Python string corrupts C# paths), no launching the dev exe against the real DB.

## Step 1 — Reading stats (pure)
**Files:** `Paperbunkr.Data/Books/BookReadingStats.cs` (new: record + `BookReadingStatsResolver.Compute(events, characterCount, progress, finished, nowUtc)`), `Paperbunkr.Data.Tests/BookReadingStatsResolverTests.cs`.
**What:** sessions = Opened events; total active time = sum `ActiveSeconds`; first/last read = min/max timestamp; pace (chars/min) from Opened rows with `ActiveSeconds ≥ 60` and `PagesRead > 0` (pages × 1800 ÷ minutes, summed over qualifying sessions); estimate = remaining characters ÷ pace, only with ≥ 2 qualifying sessions and a known `CharacterCount`, rounded to 5 minutes, hidden when finished or remaining is 0.
**Depends on:** none. **Verify:** unit tests (gating, rounding, untimed/short sessions ignored, finished, empty).

## Step 2 — Detail view model
**Files:** `ViewModels/BookDetailScreenViewModel.cs`, `Models/BookDetailTab.cs`, `Models/BookNoteEntry.cs` (new), `Models/BookChapterSummary.cs` (state + length fields), `Services/BookNotesBuilder.cs` (new, pure ordering/filtering).
**What:**
- Tabs `Overview/Chapters/Notes` (session-only): `ActiveTab`, `SetTab`, `IsOverviewTab/IsChaptersTab/IsNotesTab`, `ShowChaptersTab` (false for PDF), `ShowNotesTab` (any note, or PDF always).
- Read button label: "Start reading" / "Continue — Ch. N · P%" / "Read again" with P from `BookProgress`.
- Chapter rows: `State` (Read: index < LastChapterIndex or Finished; Current; Unread), `LengthFraction` (characters relative to the longest chapter, from the one parse), `CurrentProgress`.
- Notes: `BookNoteEntry{Kind, ChapterIndex, ChapterTitle, Excerpt, Color, Created, Id, BlockId, PageIndex}` from highlights, bookmarks and regions in book order; filters `NoteKindFilter` (All/Highlights/Bookmarks/Regions) and `NoteColorFilter`; `OpenNote`, `DeleteNote` (deferred one tick), existing export command reused.
- Stats tiles from Step 1 (`StatTiles` collection of label/value).
- Author link: `GoBooksForAuthor` callback (set by MainViewModel) + `OpenAuthor` command.
**Depends on:** Step 1. **Verify:** `BookDetailTabsTests` (label cases, chapter state mapping, notes order/filter/delete/open, tab visibility per format, author callback).

## Step 3 — Detail view
**Files:** `Views/BookDetailScreen.axaml`.
**What:** tab strip (`Button.tab`, one StackPanel, `Spacing="20"`) under the band; wrap the existing cards into three panels bound to the tab flags (`controls:Replace.On`): Overview = progress + shelf + stats tiles; Chapters = chapter list with state glyph and length bar; Notes = filter chips + entries (colour dot, excerpt, chapter, date, open/delete) + Export. The old Bookmarks card is replaced by Notes. Author in the hero becomes a link (hero action or band text button).
**Depends on:** Step 2. **Verify:** build with forced weave, lint trio, VM tests, render check.

## Step 4 — Navigation wiring
**Files:** `ViewModels/MainViewModel.cs` (set `BookDetail.GoBooksForAuthor`: show the Books screen, set `Books.AuthorFilter`), `BooksScreenViewModel.cs` (author strip "N books · M finished" with a thin progress bar).
**Depends on:** Step 2. **Verify:** VM test for the strip numbers; `MainViewModelTests` once.

## Step 5 — Wrap-up
Lint trio plus touched classes (one run at a time), update `docs/paperbunkr-todo.md` and the memory note, on-screen checklist (tabs, dashboard, notes open-at-spot, author chip). Do not run the dev exe against the real DB without a backup and the user's go-ahead.

## Risks
`BookDetailScreen.axaml` is edited in three steps: keep each green. Notes "open at spot" depends on the stored `BlockId` still resolving in the re-parsed book (the reader falls back to the chapter start, so a stale one degrades rather than fails). Stats depend on `ActiveSeconds`, which older events lack: the estimate simply stays hidden.
