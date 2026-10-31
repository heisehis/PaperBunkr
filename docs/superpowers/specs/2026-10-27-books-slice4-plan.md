# Books Slice 4 (4a Calibre, 4b dictionary, 4c OPDS, 4d read aloud) — Implementation Plan
*Implements the four `2026-10-27-books-slice4{a,b,c,d}-*-design.md` specs.*

Surveyed 2026-10-27. Facts the plan relies on:
- `BookMetadataApplier.Apply(book, BookFieldValues, source, sourceRef, now, resolveSeries, mode, onlyFields)` is the single place that protects Manual edits; **Override ignores Manual**, so a Calibre "refresh" passes `onlyFields` = all fields minus the book's Manual ones. `BookFieldSourceKind` is stored by name, so new kinds (`Calibre`, `Opds`) need no schema change. Tags merge additively.
- `BookFolderScanService.ImportFiles` shows how a `Book` row is built (`BookTextSourceFactory.Create(format, path)`, series find-or-create cached by name). `BookCoverThumbnailService.TrySetCustomCover(bookId, imagePath)` sets a never-swept custom cover.
- `Paperbunkr.Data` already references `Microsoft.EntityFrameworkCore.Sqlite`, so Calibre's `metadata.db` can be read with `Microsoft.Data.Sqlite` there. DPAPI secrets: `Paperbunkr.Data/Credentials`.
- The user's real Calibre library (40 books, EPUB/PDF, no ratings or custom columns) is read-only reference; its layout is the fixture's model.

## Rules
Same as earlier slices: design-language skill for XAML, code-behind with every new `.axaml`, touched test classes only (one `dotnet` at a time), Write-tool patch scripts, **no launch against the real database** (the real Calibre library is imported only into a scratch DB via `DatabasePathOverride`), honest "not verified on screen" notes.

## 4a Calibre import
1. `Paperbunkr.Data/Calibre`: `CalibreLibraryReader` (read-only, copy fallback), `CalibreBook`, `CalibreMapper` (format choice, HTML strip, rating, language code, author `|`→`,`), `CalibreImporter` (match tiers, apply, counts, review list, cover delegate). Migration `AddBookCalibreLink` (`CalibreBookId`, `CalibreLibraryId`); `BookFieldSourceKind.Calibre`.
2. Tests: fixture building a minimal `metadata.db` with the real tables + fake library folder; mapper, tiers, Manual protection both modes, idempotence, missing file, locked-db fallback, read-status column. Then a read-only run of the real library into a scratch DB.
3. UI: `CalibreImportDialogViewModel` + `CalibreImportDialogView` (DialogCard), Books overflow-menu item, `MainViewModel` overlay wiring, Activity Center job, result summary + needs-review list (Link / Skip).

## 4b Dictionary
1. `Paperbunkr.Data/Dictionary` or App services: `DictionaryLemmatizer`, `DictionaryLookup`, `DictionaryEntry`, online response parser; SQLite word-list file reader; download hook (reuse the GCD data-download mechanism, **checked in the step**); extract tool for WordNet if a source is reachable (otherwise the download URL is configuration and the missing data is reported honestly).
2. Settings `BookReaderLookUpOnline` (off), migration `AddBookDictionarySettings`; `InputActionIds.BookLookUp` (`L`).
3. Reader popup: Look up button on single-word selections, definition card, Back; tests for lemmatizer, lookup, parser, selection filter, popup state.

## 4c OPDS
1. `Paperbunkr.App/Services/Opds`: `OpdsFeedParser` (Atom 1.2), `OpdsClient` (auth same-host only, redirects, search templates), `OpdsDownloader` (`.part` → rename, cancel). Entities `OpdsCatalog`, `OpdsDownload`; migration `AddOpdsCatalogs`; `BookFieldSourceKind.Opds`; `AppSettings.OpdsDownloadFolder`.
2. `OpdsCatalogsScreenViewModel` + view (list, breadcrumb, search, grid, download), Books toolbar button, input via `ScreenInput`, Activity Center downloads, post-scan metadata apply.
3. Tests: parser samples (Calibre, Calibre-Web, Komga shapes), fake handler (auth, redirects, download, cancel), credential round trip, CRUD, "In library".

## 4d Read aloud
1. `ISpeechEngine` + `SystemSpeechEngine` (`System.Speech` NuGet), `BookReadAloudPlan`, VM partial `BookReaderScreenViewModel.Speech.cs`, in-page pill + `pb-speaking`, actions `BookSpeakToggle/Faster/Slower` (T, Shift+., Shift+,), sheet card, settings (`BookReaderSpeechRate`, `BookReaderSpeechVoice`), migration `AddBookReaderSpeech`.
2. Tests with a fake engine; key-set pin; migration defaults.

## Wrap-up
Lint trio + every touched class (`MainViewModelTests` once), `docs/paperbunkr-todo.md`, memory note, `dotnet build-server shutdown`, on-screen checklist per sub-slice.

## Risks
Reconstructed vs real Calibre schema (mitigated by the real-library read-only run); WordNet data availability offline (download source); `System.Speech` voice list varies per machine; four migrations plus shared `MainViewModel`/`BookReaderScreen` edits — keep each step green.
