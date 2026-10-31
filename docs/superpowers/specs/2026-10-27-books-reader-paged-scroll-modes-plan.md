# Books reader paged/scroll modes — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md*

## Step 1: Settings and migration
**Files:** `Paperbunkr.Data/Entities/AppSettings.cs` (edit), `Paperbunkr.Data/Entities/BookReaderLayout.cs` (new enum `Paged`/`Scroll`),
`Paperbunkr.Data/PaperbunkrDbContext.cs` (edit: string conversion + defaults next to `BookReaderAutoScrollSpeed`),
`Paperbunkr.Data/Migrations/*_AddBookReaderLayout.cs` (new, `dotnet ef migrations add`).
**What:** `BookReaderLayout` (default Paged), `BookPageWordsMin` 250, `BookPageWordsMax` 300.
**Depends on:** none. **Verify:** Data migration test (columns exist with defaults on an existing row).

## Step 2: Shared block reader + units
**Files:** `App/Services/Reader/BookBlockReader.cs` (new: leaf `pb-p` blocks with text and offsets, lifted from `BookReadAloudPlan`),
`App/Services/Speech/BookReadAloudPlan.cs` (edit: use it), `App/Services/BookUnitSlicer.cs` (new: units per file, `UnitOf`).
**What:** spec §4. A unit = (file index, toc row index, title, fragment, html slice, start/end offsets in the injected file html).
**Depends on:** none. **Verify:** `BookUnitSlicerTests`, existing `ReadAloudTests`.

## Step 3: Book page map + print pages
**Files:** `App/Services/Reader/BookPageMap.cs` (new), `Engine/.../IBookTextSource.cs` (edit: `BookPrintPage`, `PrintPages`),
`Engine/.../EpubBookSource.cs` (edit: read nav page-list / NCX pageList), FB2/MOBI/PDF sources (edit: empty list).
**What:** spec §6.1–6.2.
**Depends on:** Step 2. **Verify:** `BookPageMapTests`, `EpubPrintPageTests` (fixture EPUB with page-list, with NCX pageList only).

## Step 4: View model
**Files:** `ViewModels/BookReaderScreenViewModel.cs` (+ new partial `BookReaderScreenViewModel.Layout.cs`), `.Aids.cs`, `.Speech.cs` (edit).
**What:** units replace files as the shown document; next/previous by unit; contents active row by unit; `Layout` property persisted;
page map built off the UI thread; `ProgressLabel`/`ProgressFraction` per §6.3; `ScreenPage`/`ScreenPageCount` fed from the page;
Go to page (`GoToPageText`, `GoToPageCommand`, `GoToPageError`); auto-scroll refused in paged mode.
**Depends on:** Steps 1–3. **Verify:** `BookReaderScreenViewModelTests`, `ReadAloudTests`, new layout/unit/footer tests.

## Step 5: Page script
**Files:** `Views/BookReaderScreen.axaml.cs` (edit).
**What:** remove the column code and scroll-snap; `pbLayout`; `pbBuildPages`, page show + clip, `pbNextPage`/`pbPreviousPage`/`pbShowLastPage`,
`pbScrollToBlock` and `pbReportPosition` per mode, wheel handling (paged: one page per gesture; scroll: over-scroll moves unit with a
settle guard), ruler/read-aloud page turns, `pbMotionMs` slide, a `page` message (screen page / count) back to C#.
**Depends on:** Step 4. **Verify:** on screen only (WebView does not run in tests).

## Step 6: UI
**Files:** `Views/ReaderSettingsSheet.axaml(.cs)` (LAYOUT card + `Layout` property, auto-scroll disabled when paged),
`Views/BookReaderScreen.axaml` (bind it; Go to page field atop the Chapters drawer), `Views/Preferences/ReaderSection.axaml` +
`PreferencesScreenViewModel` (Books card, words-per-page row), `ViewModels/BookDetailScreenViewModel.cs` (unit numbering for Ch. labels).
**Depends on:** Step 4. **Verify:** `XamlConsistencyLintTests`, `BookDetailScreenViewModelTests`, `PreferencesScreenViewModelTests` (targeted).

## Step 7: Docs and wrap
**Files:** `docs/paperbunkr-todo.md`, memory note.
**Verify:** targeted test classes + app build; on-screen checklist from spec §9 handed to the user.
