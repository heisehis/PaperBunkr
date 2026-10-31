# Books Slice 1 (data foundation) — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md*

Surveyed 2026-10-26. Facts the plan relies on (all read from the current tree):

- `BookMetadata` (`src/Paperbunkr.Engine/IO/Provider/Books/IBookTextSource.cs`) has only `Title`, `Author`, `SeriesName`, `CoverImageBytes`.
  `EpubBookSource` builds it from `VersOne.Epub` (`_book.Title/Author`, `FindCalibreSeriesName`); `Fb2BookSource.BuildMetadata`, `MobiBookSource`,
  `PdfBookSource` build their own.
- `BookFolderScanService.ImportFiles` (line ~148) is the single import body for folder scans and open-on-launch. It creates `Book` + find-or-create `BookSeries`
  by name. There is no `SeriesIndex` anywhere.
- `Book.Finished` is read/written in: `BookReaderScreenViewModel` (lines 461, 1167), `BookDetailScreenViewModel` (286, 304, 320, 334, 643-661),
  `BookCardSample` (49, 68), `NovelSmartListCatalog` (22, 42; in-memory lambda), `HomeFeedResolver` (234; **a LINQ query**),
  `PaperbunkrDbContext` (2367). Tests touch it in `BookReaderScreenViewModelTests`, `BookDetailScreenViewModelTests`, `HomeFeedResolverTests`.
- Comics render rating as five whole-star `Button.star` buttons with `SetMyRatingNCommand`s (`IssuePropertiesScreen.axaml` ~line 216). **No reusable
  half-star control exists** → Step 7 builds one.
- Comic tags in the editor are a multi-value `pbc:SuggestBox IsMultiValue="True"` bound to a delimited string + `TagsVocab`; read-only chips use `Border.pbChip`.
  Books reuse that pattern.
- `MetadataFileWriteBackService` (comics) explicitly skips EPUB (`SkippedUnsupportedFormat`). The watcher guard is
  `FileWriteBackCoordinator.Suppress(path, window)` / `BeginWrite(path)` / `IsSuppressed`, checked by `LiveFolderWatchService` (lines 140, 165) and
  `~RF*.TMP` backups are already filtered. The book write-back reuses this guard.
- Provider precedent: `MangaDexMetadataProvider` (`Paperbunkr.Data/Metadata`): injected `HttpClient`, `Shared` static, min request interval, throws
  `MetadataProviderUnavailableException`. Jobs: `IActivityService.StartJob(ActivityJobKind, title, ...)` returning `IActivityJobHandle`.
- Migration folder: `src/Paperbunkr.Data/Migrations/`; book-related precedent `20260827123120_AddBooksBrowseState`. Migration tests use
  `context.Database.Migrate()` + ORM seeding (never bare `INSERT INTO AppSettings`).

## Ground rules for every step

- Standing rule: check `_reference/ComicRackCE` before inventing behaviour (rating scale already verified in the design).
- UI steps: load the `avalonia` skill and **read** the matching subskill `SKILL.md` off disk; use `paperbunkr-design-language`; run
  `avalonia-pro-max/review-checklist` before calling a UI step done. A new `.axaml` always lands with its code-behind `.cs` in the same step.
- Tests: run only touched classes (`dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~X"`), one `dotnet` process at a time, never the
  full or `Speed!=Slow` suite. Kill build-server/test processes afterwards.
- Never build or run against the live DB from a worktree; no live network in tests (recorded JSON fixtures).
- Rating is `float?`, 0–5, validated to 0.5 steps in one place (`BookRating.Normalize`).

## Step 1 — Entities, enums, migration
**Files:** `Paperbunkr.Data/Entities/Book.cs` (edit), `ReadingStatus.cs` (new), `BookTag.cs` (new, with `BookTagField`), `BookFieldSource.cs` (new, with
`BookFieldSourceKind`), `AppSettings.cs` (edit: `BookMetadataBackfillDone`, `BookMetadataOpenLibraryEnabled` default true,
`BookMetadataGoogleBooksEnabled` default true), `PaperbunkrDbContext.cs` (edit Book config near line 2361, new entity configs), new migration
`AddBookMetadataFoundation` + Designer + snapshot, `Metadata/HomeFeedResolver.cs` (edit line 234 → `b.Status != ReadingStatus.Finished`).
**What:**
- `Book`: `Subtitle, Publisher, LanguageISO, ISBN, SeriesIndex, Rating, CommunityRating, CommunityRatingCount, Status (default Unread),
  SkipMetadataMatch`; `Tags`/`FieldSources` navs; `[NotMapped] Finished` shim over `Status`.
- Config: `Status` stored as string (`HasConversion<string>().HasMaxLength(32)`, like `Format`); unique index `(BookId, Field, Value)` on `BookTag`
  using `NOCASE` collation; unique `(BookId, Field)` on `BookFieldSource`; cascade delete both.
- **Keep the legacy `Finished` column.** EF will want to emit `DropColumn` once the property is unmapped: hand-remove that `DropColumn` (and its
  `AddColumn` in `Down`) from the generated migration and keep the snapshot consistent. If that proves fragile, the fallback is to leave `Finished`
  mapped and have `Status`'s setter keep it in sync; decide on evidence in this step, record which in the migration's doc comment.
- Data step in `Up` (`migrationBuilder.Sql`): `Status = 'Finished'` where `Finished = 1`; else `'Reading'` where any of `LastBlockId`,
  `LastProgressionFraction`, or `LastChapterIndex > 0`; else `'Unread'`.
- Convert LINQ uses of `Finished` (grep `\.Finished` on `Book` queries; the survey found only `HomeFeedResolver`); in-memory uses stay.
**Depends on:** none.
**Verify:** new `Paperbunkr.Data.Tests/AddBookMetadataFoundationMigrationTests` (up/down/up via `Migrate()`; status data step for the three cases; unique-tag
index; cascade deletes; rating normalisation helper), `HomeFeedResolverTests`, `ReadingEventBackfillTests` unaffected check; then forced
`CoreCompile` rebuild per CLAUDE.md (delete `obj/.../Paperbunkr.App.dll/pdb`) because the model snapshot changed.

## Step 2 — Read richer metadata from files
**Files:** `Paperbunkr.Engine/IO/Provider/Books/IBookTextSource.cs` (extend `BookMetadata`: `Subtitle, Publisher, LanguageISO, ISBN, SeriesIndex, Description,
Rating, Genres (IReadOnlyList<string>), Tags`), `EpubBookSource.cs`, `Fb2BookSource.cs`, `MobiBookSource.cs` (+ `Mobi/MobiHeaderReader.cs` if EXTH
records 101/104/106/524 aren't parsed yet), `PdfBookSource.cs` (Keywords/Subject → tags only).
**What:** EPUB from the VersOne OPF metadata (publisher, language, identifiers with ISBN scheme or `urn:isbn:`, subjects → genres, `calibre:series_index`,
`calibre:rating` /2, EPUB3 `belongs-to-collection` + `group-position`, description); FB2 `title-info`/`publish-info`; MOBI EXTH. Missing values stay null.
**Depends on:** none (parallel with Step 1).
**Verify:** extend `EpubBookSourceTests`, `Fb2BookSourceTests`, `MobiBookSourceTests`, `PdfBookSourceTests` using the existing `EpubFixture`/`Fb2Fixture`/
`MobiFixture` builders (add overloads for the new fields, including a calibre-style OPF and an EPUB3 collection OPF).

## Step 3 — `BookMetadataApplier` and import wiring
**Files:** `Paperbunkr.App/Services/BookMetadataApplier.cs` (new), `Services/BookFolderScanService.cs` (edit `ImportFiles`), `Paperbunkr.Data/Books/` for
`BookTagParser` (new, splits/joins the multi-value text, dedupes case-insensitively).
**What:** one place that takes a `BookMetadata` (or a provider result, Step 11) and a `Book` and **fills empty fields only**, never overrides a field whose
`BookFieldSource` row is `Manual`, writes a `BookFieldSource` row per field it sets, merges genres/tags, and returns what changed. Series rule: assign
`BookSeries` + `SeriesIndex` only when the file declares a series, never when the book already has one. `ImportFiles` calls it for new books.
**Depends on:** Steps 1, 2.
**Verify:** `BookMetadataApplierTests` (fills empties, respects Manual, series only when declared, tag merge, sources written); extend
`BookFolderScannerTests` for an import carrying new fields.

## Step 4 — Suggested series (title-only guesses)
**Files:** `Paperbunkr.App/Services/BookSeriesSuggester.cs` (new), `BookMetadataApplier` unaffected.
**What:** pure function from title (+ author) to a suggested `(SeriesName, Index)` using `TitleNormalizer` for "Book 3 of …", "… #2", "Name, Vol. 4" patterns
and the existing series list for matching; never writes. Returns null when unsure.
**Depends on:** none.
**Verify:** `BookSeriesSuggesterTests` with a table of real-world-shaped titles (incl. "The Hobbit" sort-name case that must NOT suggest).

## Step 5 — Refresh-from-files backfill job
**Files:** `Paperbunkr.App/Services/BookMetadataBackfillService.cs` (new), `Models/ActivityJob.cs` (edit: add `ActivityJobKind.BookMetadata`), startup hook
in `App.axaml.cs`/`MainViewModel` (follow how `ThumbnailPrefetch`/other one-time jobs start; **do not** run before the startup pipeline's migration read),
Books menu entry in `BooksScreenViewModel` + `BooksScreen.axaml` (action in the existing menu/toolbar family).
**What:** iterates books in batches (parses with `BookTextSourceFactory`, calls `BookMetadataApplier`), throttled, cancellable via the job handle, sets
`AppSettings.BookMetadataBackfillDone` when it completes the first time; re-runnable from the Books menu ("Refresh metadata from files").
**Depends on:** Steps 1–3.
**Verify:** `BookMetadataBackfillServiceTests` (only empties filled, Manual preserved, cancellation leaves consistent state, flag set once);
real-app check on a copy of the dev DB per memory `reference_bash_sandbox_stale_appdata`.

## Step 6 — `StarRating` control (half steps)
**Files:** `Paperbunkr.App/Controls/StarRating.cs` (new), style in `Styles/Controls.axaml` (a new shared family; tokens only, `PbAccent` fill,
`PbTextFaintBrush` for empty stars as icon), lint: `FoundationStyleTests`/`SharedControlsStyleTests` additions if the family needs registering.
**What:** `Value` (`float?`), `MaxStars=5`, half-star hit regions, clear gesture, `AutomationProperties.Name` + `IInputSuppressor`-free (keyboard: Left/Right
adjust by 0.5, Delete clears, handled as the control's own item key per CLAUDE.md "Input handling"), read-only mode for Detail.
**Depends on:** none.
**Verify:** `StarRatingTests` (headless: pointer on left/right half, keyboard, clear, read-only); `XamlConsistencyLintTests|ThemeHardcodedColorLintTests|
SharedControlsStyleTests`; render check via `FoundationStyleTests.WithFoundation` in default, daylight, matrix.

## Step 7 — Book Properties overlay
**Files:** `ViewModels/BookPropertiesScreenViewModel.cs`, `Views/BookPropertiesOverlay.axaml(.cs)`, `Models/BookMetadataSnapshot.cs`,
`Services/MetadataEditHistoryService.cs` (only if the snapshot shape needs it).
**What:** add Subtitle, Publisher, Language (`SuggestBox` of ISO codes), ISBN, Series index, Status (`SuggestBox`), Rating (`StarRating`), Genre and Tags
(multi-value `SuggestBox` + vocab from existing `BookTag`s), Suggested-series chip (Accept/Dismiss, Step 4), per-field provenance chip with Re-fetch
(Re-fetch is wired in Step 12; chip is read-only until then). Saving writes `BookFieldSource(Manual)` for each changed field. `HasUnsavedChanges`
signature, `_beforeSnapshot`, undo/redo include the new scalar fields, tags and status.
**Depends on:** Steps 1, 3, 4, 6.
**Verify:** extend `BookPropertiesScreenViewModelTests` (load/save round trip, manual source rows, tag edits, undo/redo of tags+status+rating, suggestion
accept); lint trio; real-window check at 1366×768 (overlay height/scroll, `DialogCard`/overlay conventions of the existing overlay).

## Step 8 — Bulk overlay
**Files:** `ViewModels/BulkBookPropertiesScreenViewModel.cs`, `Views/BulkBookPropertiesOverlay.axaml(.cs)`, `MetadataEditHistoryService.RecordBookEdits` call.
**What:** stage Status, Rating (with "leave unchanged"), add tags / remove tags, Publisher, Language; empty = unchanged (existing convention).
**Depends on:** Steps 1, 3, 6, 7 (shares tag parsing and rating normalisation).
**Verify:** extend the bulk VM tests; lint trio.

## Step 9 — Quick actions and status transitions
**Files:** `ViewModels/BooksContextMenuBuilder.cs`, `BookDetailContextMenuBuilder.cs`, `BookDetailScreenViewModel.cs` (Rate ▸ / Set status ▸, display of
rating stars, status chip, tag chips, Publisher/Language/ISBN lines, source chips; replace `IsFinished`/`ToggleFinished` with Status-based logic),
`Views/BookDetailScreen.axaml`, `BookReaderScreenViewModel.cs` (line ~461: opening sets Reading; Abandoned → Reading; lines ~1167: finish sets Finished),
`Models/BookCardSample.cs` (carry `Status`, `Rating`).
**What:** writes are immediate and recorded in `MetadataEditHistoryService`. Reader transitions per design §3.2. Reading Event writes keep working
(`PrimaryGenre` for books: first Genre tag; in `ReadingEventRecorder`).
**Depends on:** Steps 1, 6, 7.
**Verify:** extend `BookDetailScreenViewModelTests`, `BookReaderScreenViewModelTests` (open → Reading, end → Finished, reopen Finished, Abandoned → Reading,
WantToRead → Reading), `ReadingEventRecorderTests` (PrimaryGenre for novels), `HomeScreenViewModelTests` for the continue-reading row; lint trio.

## Step 10 — Book metadata providers (no UI)
**Files:** `Paperbunkr.Data/Metadata/Books/IBookMetadataProvider.cs`, `OpenLibraryBookProvider.cs`, `GoogleBooksProvider.cs`, `BookMatchRanker.cs`,
`BookMetadataHttpClient.cs` (shared client with descriptive `User-Agent`), mirrors `MangaDexMetadataProvider` (injected `HttpClient`, `Shared`, min interval,
`MetadataProviderUnavailableException`, 429 backoff with a distinct "rate limited" signal).
**What:** search by ISBN, search by title+author, fetch details → `BookProviderRecord` (title, subtitle, authors, publisher, year, language, ISBN,
subjects, description, series if present, community rating where offered, cover URL). Ranker uses `TitleNormalizer` similarity, author match, language.
**Depends on:** none (parallel).
**Verify:** `OpenLibraryBookProviderTests`, `GoogleBooksProviderTests`, `BookMatchRankerTests` with recorded JSON fixtures; no live calls.

## Step 11 — Match dialog, bulk job, skip-forever, Connections tile
**Files:** `Scraper/BookMatchDialogView.axaml(.cs)` + `BookMatchDialogViewModel.cs` (new; `DialogCard` Large), `Services/BookMetadataMatchService.cs` (new:
one book and bulk job via `IActivityService`), entry points in `BookDetailScreenViewModel` (+ view), `BooksScreenViewModel` selection bar and context
menu, `PreferencesScreenViewModel.Connections.cs` + its view (tile "Book metadata": two enable toggles, Google Books optional key via the same
`CredentialStore` the other connections use).
**What:** candidates → per-field diff (checked by default only for empty fields; ticking a non-empty field overrides), cover opt-in via
`BookCoverThumbnailService.TrySetCustomCover`, accept applies through `BookMetadataApplier` with `BookFieldSource(provider)`; **Skip forever** sets
`SkipMetadataMatch`; bulk run reports matched / needs review / no match / skipped / rate-limited and never blocks on ambiguity.
**Depends on:** Steps 3, 7, 10.
**Verify:** `BookMatchDialogViewModelTests`, `BookMetadataMatchServiceTests` (fake provider; skip-forever excluded; ambiguous queued; cancellation),
`PreferencesScreenViewModelTests` is Slow-tagged: run only the Connections-related class that exists for this tile; lint trio; real-window check.

## Step 12 — Re-fetch from provenance chips
**Files:** `BookPropertiesScreenViewModel.cs`, `BookDetailScreenViewModel.cs`.
**What:** Re-fetch on a scraped/file field re-reads from its source (`File` → re-parse; provider → fetch by stored `SourceRef`) and shows the Step 11 diff for
that one field. Manual fields offer "re-fetch" only after a confirm because it replaces a user edit.
**Depends on:** Steps 7, 11.
**Verify:** VM tests with fakes.

## Step 13 — EPUB OPF write-back
**Files:** `Paperbunkr.App/Services/BookFileWriteBackService.cs` (new), `Services/OpfMetadataPatcher.cs` (new), hook in the save paths of Steps 7/8/9/11
gated on `AppSettings.WriteMetadataToFiles` + `WriteMetadataAutomatically` (same gating as comics).
**What:** patch only the OPF metadata nodes (publisher, language, ISBN identifier, subjects, `calibre:series`/`series_index` + EPUB3 collection,
`calibre:rating` = rating×2, description), leave everything else byte-preserved; write to a sibling temp zip, reopen with `EpubBookSource` to validate, then
`File.Replace`, all inside `FileWriteBackCoordinator.BeginWrite(path)` + `Suppress(path, window)` so the watcher does not mark the book missing.
Non-EPUB: skip visibly (outcome type mirrors `MetadataWriteBackOutcome`).
**Depends on:** Steps 1, 2.
**Verify:** `OpfMetadataPatcherTests` (messy OPFs: default/prefixed namespaces, missing `<metadata>` children, existing calibre meta; unrelated nodes
unchanged), `BookFileWriteBackServiceTests` (temp copy replaced, failure leaves original intact, read-only skip, watcher suppression set), and the
existing `LiveFolderWatchService` tests pass untouched.

## Step 14 — Wrap-up
**What:** run the lint trio plus every class touched above (one run at a time); update `docs/paperbunkr-todo.md` with what is actually verified and the
commit refs once committed (project rule); add a memory note for the slice; ask the user to verify on screen at 1366×768: Properties overlay, bulk overlay,
Detail chips, match dialog, backfill job in Activity Center, an EPUB write-back round trip on a copy of a real book.
**Depends on:** all.

## Order and parallelism

`1 ‖ 2 ‖ 4 ‖ 6 ‖ 10` can start together (independent files); then `3` (needs 1, 2) → `5`, `7` (needs 3, 4, 6) → `8`, `9` → `11` (needs 3, 7, 10) → `12`
→ `13` (needs 1, 2; can run any time after) → `14`. Sessions that write code should not overlap on the same files (Steps 7, 8, 9 all touch Book overlay /
Detail files, so they run sequentially).

## Risks to watch while building

- Model-snapshot churn from the unmapped `Finished` column (Step 1 decides the fallback on evidence).
- `BookDetailScreenViewModel` and `BookReaderScreenViewModel` are large; keep edits surgical and cover them with the existing tests first.
- Provider rate limits and ToS: sequential requests, descriptive `User-Agent`, no automatic scraping on import.
- A dropdown/overlay close from inside a row (tag chip removal, Re-fetch) must be deferred one dispatcher tick (CLAUDE.md "don't remove a control from inside
  a routed event it's still raising").
- Pending work in the tree: `git status` shows many modified files from other sessions; touch only the files above and never stage others'.
