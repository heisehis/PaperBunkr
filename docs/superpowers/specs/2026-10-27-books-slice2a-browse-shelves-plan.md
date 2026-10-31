# Books Slice 2a (browse and shelves) — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md*

Surveyed 2026-10-27 against the tree after Slice 1. Facts the plan relies on:

- `BooksScreenViewModel.Rebuild()` filters by search only, then `Sort()` (4 axes) and `GroupCards()` (Series/Author, fallback buckets "Standalone"/"Unknown author" last), over `_allCards` of `BookCardSample`. Sort/group/direction persist via `LoadBooksSettings/SaveBooksSettings` (`AppSettings.BooksSortField/Direction/GroupField`, enums stored by name). `BookCardSample.FromBook` is the only card builder.
- Novel smart lists: fields `SmartListField.Novel*` (**stored by name**, so appending enum members needs no migration), definitions + in-memory `Func<Book,…>` selectors in `NovelSmartListCatalog`, evaluated by `NovelSmartListQueryBuilder.LoadSnapshot/Evaluate` (loads `Books.Include(BookSeries)` only, so Tags must be added to the include for a Tag field).
- Library already has: a Bookshelf mode (`LibraryViewMode.Bookshelf`, `VirtualizingVariableWrapPanel.UseSpineWidths`, custom `SpineTile`, templates `BookshelfIssueTemplate`/`BookshelfSeriesTemplate`), an alphabet rail (`LibraryScreen.axaml` `AlphabetRail` ItemsControl bound to `AlphabetIndex`, letters from `LibraryScreenViewModel.AlphabetIndexLetters`, click handling in `LibraryScreen.axaml.cs`), and `ProgressArcRing`/`ProgressRing` controls.
- **Home changed since the spec was written:** comics and books share one mixed "Continue Reading" row (`HomeFeedResolver.GetContinueReadingMixed`, cards `HomeResumeCard{Book=HomeBookCard}` in `HomeScreenViewModel` ~line 683). The plan keeps that row and adds the two new book rows beside it.
- Duplicates today are comic-issue clusters (`SmartListQueryBuilder.BuildDuplicateGroups`, `NeedsReviewViewModel`, `DuplicateGroupRowViewModel`) inside Library Health (`LibraryHealthSections`, `PreferencesScreenViewModel.LibraryHealthTabs.cs`). A book check is a new section, not an extension.
- Slice 1 gave `Book` Status/Rating/Publisher/LanguageISO/SeriesIndex/Tags/CharacterCount and `BookCoverImageCache`; novels have `Book.CollectionItems`.

## Ground rules

Same as the Slice 1 plan: standing CE rule, `avalonia` skill + `paperbunkr-design-language` for every UI step (read the subskill `SKILL.md` off disk; run `avalonia-pro-max/review-checklist` before calling a UI step done), new `.axaml` always with its code-behind in the same step, touched-class tests only, one `dotnet` process at a time, no live network, never run the dev exe against the real DB without a backup and the user's say-so. Pure logic goes in `Paperbunkr.Data`/`Services` with its own tests; view models stay thin.

## Step 1 — Enums, settings, migration
**Files:** `Paperbunkr.Data/Entities/BooksSortField.cs` (add `Rating, Progress, Publisher, Published, SeriesOrder`), `BooksGroupField.cs` (add `Status, Publisher, Language, Genre`), `BooksStatusTab.cs` (new: `All, Reading, WantToRead, Unread, Finished, Abandoned`), `BooksViewMode.cs` (new: `Grid, Bookshelf`), `AppSettings.cs` (`BooksActiveStatusTab`, `BooksViewMode`), `PaperbunkrDbContext.cs` (string conversions, max length 32, defaults All/Grid), migration `AddBooksBrowseViews` (+ Designer + snapshot).
**What:** two TEXT columns with defaults. Same care as Slice 1's migration: generate with `dotnet ef migrations add`, confirm the diff contains only these two columns (other sessions' migrations sit untracked in the tree).
**Depends on:** none.
**Verify:** `AddBooksBrowseViewsMigrationTests` (Migrate, defaults, round trip, reversible), then forced `CoreCompile` rebuild.

## Step 2 — Browse query as pure logic
**Files:** `Paperbunkr.Data/Books/BooksBrowseQuery.cs` (new), `Paperbunkr.App/Models/BookCardSample.cs` (edit: `Status, Rating, Progress, Publisher, Language, PublishedDate, SeriesIndex, Genres, IsGhost`), `ViewModels/BooksScreenViewModel.cs` (edit: `Rebuild/Sort/GroupCards` delegate to it).
**What:** one place for filter (status tab, shelf id set, author, search), sort (all 9 axes, nulls last in both directions, stable title tiebreak) and grouping (fallback buckets last; Status groups in fixed order Reading, Want to read, Unread, Finished, Abandoned; Genre = first genre only; Language shown as English name). `Progress` = `ChapterCount > 0 ? (LastChapterIndex + fraction) / ChapterCount : fraction`, clamped 0–1, 1 when Finished. The existing behaviours (search fields, Standalone/Unknown buckets, time-sort group order) must come out identical; the VM keeps selection re-apply and entrance pulse.
**Depends on:** Step 1.
**Verify:** `BooksBrowseQueryTests` (every axis both directions with nulls, every group, tab filtering, combined filters, regression cases lifted from `BooksScreenViewModelTests`), then that existing class unchanged and green.

## Step 3 — Status tabs
**Files:** `ViewModels/BooksScreenViewModel.cs` (`StatusTabs` collection with counts, `ActiveStatusTab`, persisted via Load/SaveBooksSettings), `Views/BooksScreen.axaml` (tab strip under the header: `Button.tab` + `active`, `Spacing="20"`, count `Border.countPill`; `controls:Replace.On` on the grid), `Views/BooksScreen.axaml.cs` if needed.
**What:** counts computed from `_allCards` (all-books counts, not search-filtered, so tabs don't jitter while typing). Empty tab shows the existing "no matches" state with a tab-specific line. `TabStrip.Step` gives bumper/Ctrl+PageUp/Down navigation; no new hotkeys.
**Depends on:** Step 2.
**Verify:** VM tests (counts, persistence round trip, tab + search combine), lint trio, render check.

## Step 4 — Sort and group menus
**Files:** `Views/BooksScreen.axaml` (the existing Sort/Group dropdown rows), `ViewModels/BooksScreenViewModel.cs` (`ActiveSortLabel/ActiveGroupLabel`, `SetSortField/SetGroupField` already generic), `BooksSortField`/`BooksGroupField` docs.
**What:** rows for the new axes in the existing `Border.dropdown` (each dropdown already needs `PopupFit.ToWindow` and one ScrollViewer: check and fix if missing now that the lists are longer). Labels per design language (sentence case).
**Depends on:** Step 2.
**Verify:** VM tests for labels and persistence; lint trio; real-window check that both dropdowns fit at 768 px.

## Step 5 — One-badge cards
**Files:** `Models/BookCardSample.cs` (`BadgeKind`: None/Progress/Finished, `RatingStars`), `Views/BooksScreen.axaml` (card template: reuse `controls:ProgressArcRing` for Reading, `fi:SymbolIcon CheckmarkCircle` for Finished; hover shows small stars through a read-only `StarRating`; tooltip carries rating text for the Bookshelf view too).
**What:** exactly one badge or none. Follow the Library card's badge placement and token colours (read `LibraryScreen.axaml`'s card badge first; copy its position/size, don't restyle). Selection checkbox stays as is.
**Depends on:** Step 2.
**Verify:** badge-state unit tests; render check light/dark/Matrix; `ThemeHardcodedColorLintTests`.

## Step 6 — Smart-list fields for novels
**Files:** `Paperbunkr.Data/Entities/SmartListField.cs` (append `NovelRating, NovelStatus, NovelTag, NovelPublisher, NovelLanguage, NovelSeriesIndex, NovelProgress`), `SmartLists/NovelSmartListCatalog.cs` (definitions + selectors: Rating/SeriesIndex/Progress as Number, Status/Tag/Publisher/Language as Text, Tag selector = comma-joined tag values so "contains" works), `SmartLists/NovelSmartListQueryBuilder.cs` (`LoadSnapshot` adds `.Include(b => b.Tags)`), `ViewModels/SmartListConditionViewModel.cs` (field pickers/labels if they enumerate the catalog explicitly), `SmartListTemplateCatalog.cs` (optional starter templates: "Unrated finished books").
**What:** appending enum members after the newest entry keeps stored names stable. Rating null → treated as 0 for numeric compares (document it in the field label: "My rating (0 = unrated)").
**Depends on:** none (parallel).
**Verify:** `NovelSmartListTests` additions (each new field: match, no match, null handling, nested AND/OR), editor VM test that the new fields appear for a Novel-target list.

## Step 7 — Shelves menu
**Files:** `Services/BookShelfService.cs` (new), `ViewModels/BooksScreenViewModel.cs` (`Shelves`, `ActiveShelf`, `ShelfChip`, `ClearShelf`), `Views/BooksScreen.axaml` (toolbar button + `Border.dropdown` with `Button.menuRow`, `PopupFit.ToWindow`, one ScrollViewer; dismissible chip), `Views/BooksScreen.axaml.cs` if popup glue is needed.
**What:** `BookShelfService.List()` returns built-ins (Favorites = `Rating >= 4`, Unfinished = Reading status or progress > 0 and not Finished, Not rated), novel smart lists (`SmartList` rows with `SmartListTargetKind.Novel`), and collections that contain books. `Resolve(shelf)` returns the id set; smart lists via `NovelSmartListQueryBuilder.Build`. Choosing a shelf sets the filter set consumed by `BooksBrowseQuery`; session-only. Closing the popup from inside a row is deferred one dispatcher tick (CLAUDE.md gotcha).
**Depends on:** Steps 2, 6.
**Verify:** `BookShelfServiceTests` (built-in rules, smart list resolution, collection filtering, empty collections omitted), VM test (shelf + tab + search compose), lint trio, real-window check of the dropdown at 768 px.

## Step 8 — Bookshelf view
**Files:** `Models/BookSpineItem.cs` (new), `Services/BookSpineWidth.cs` (new, pure), `ViewModels/BooksScreenViewModel.cs` (`ViewMode`, `IsBookshelfView`, persisted), `Views/BooksScreen.axaml` (Grid/Bookshelf `Border.segGroup`/`Button.segItem` switch, bookshelf `controls:SmoothScrollViewer` section mirroring Library's, spine DataTemplate), reuse of Library's `SpineTile` (read how `BookshelfIssueTemplate` feeds it first; if it is coupled to `IssueListRow`, add a small adapter rather than copying the control).
**What:** `BookSpineWidth.For(characterCount, format)` clamps to the panel's spine range; PDFs and unparsed books get a default. Colour from `CoverBrush`/dominant cover colour already used by the Library spine. Tooltip: title, author, rating, progress. Grouped mode keeps working (spines per group).
**Depends on:** Steps 1, 2.
**Verify:** `BookSpineWidthTests`, VM tests (mode persistence), render check of both modes in all themes, lint trio.

## Step 9 — Series gaps
**Files:** `Paperbunkr.Data/Books/BookSeriesGaps.cs` (new, pure), `Models/BookCardSample.cs` (`IsGhost`, `GhostLabel`), `ViewModels/BooksScreenViewModel.cs` (insert ghosts inside each series group when grouped by Series), `ViewModels/BookDetailScreenViewModel.cs` (Series mode member list), the matching card templates.
**What:** `Find(indices)` returns missing whole numbers strictly between the lowest and highest owned index, only when every counted book has a `SeriesIndex`; fractional indices (2.5) never create gaps; duplicates ignored; empty/singleton → none. Ghost cards: not selectable, not clickable, no menu, no tab stop (CLAUDE.md: don't make nested controls focusable without need); excluded from counts, selection and bulk actions.
**Depends on:** Step 2.
**Verify:** `BookSeriesGapsTests` (own 1,2,4; 1,3,5; fractional; unnumbered book present; single), VM tests (ghosts appear only grouped by Series, never in `Books`/selection), render check.

## Step 10 — Alphabet rail and pinned headers
**Files:** read `Views/LibraryScreen.axaml` (~line 1942) and `.axaml.cs` (~190) first. Preferred: extract the rail into `Controls/AlphabetRail` used by both screens; fallback if the Library code is entangled with its virtualization: `Views/BooksScreen.axaml` gets its own rail bound to a Books `AlphabetIndex` and shares `LibraryScreenViewModel.AlphabetIndexLetters` and the letter→first-item math through a small static helper.
**What:** rail shows only for Title/Author sorts and grouped headers; clicking a letter scrolls to the first matching card/group; pinned group header reuses Library's approach. Decide extract-vs-copy on evidence and record it in the step's commit/doc note; do not refactor Library behaviour.
**Depends on:** Steps 2, 4.
**Verify:** helper tests (letter → index for titles with articles/digits, per Library's existing rule), `LibraryScreen` rail tests still green, real-window check that Library's rail is unchanged.

## Step 11 — Duplicate formats in Library Health
**Files:** `Paperbunkr.Data/Books/BookDuplicateFinder.cs` (new), `ViewModels/BookDuplicateGroupRowViewModel.cs` (new), `ViewModels/LibraryHealthSections.cs` + `PreferencesScreenViewModel.LibraryHealthTabs.cs` (new section key under Duplicates), the Library Health section XAML (find the existing Duplicates section markup and mirror it), `Services/RevealInExplorerHelper` reuse.
**What:** groups books by ISBN (when equal and non-empty) else by `TitleNormalizer.StripDown(title)` + normalised author, keeping groups with ≥ 2 books of **different formats** (same-format copies are an ordinary duplicate and are listed too, labelled as such). Rows: format chips, size, path; actions Reveal and Delete (Recycle Bin, then prune empty series, same as the Books grid delete). Report-only: no dismiss-forever table in this slice (**Assumed**; a "Not a duplicate" action is a possible follow-up).
**Depends on:** none.
**Verify:** `BookDuplicateFinderTests` (ISBN match, title+author match, different authors don't match, article/punctuation differences, single-format duplicates labelled), VM test (delete removes the row and the book), lint trio.

## Step 12 — Home rows
**Files:** `Paperbunkr.Data/Metadata/HomeFeedResolver.cs` (`GetUpNextInSeries`, `GetRecentlyAddedBooks`), `ViewModels/HomeScreenViewModel.cs` (two collections, `HasUpNextBooks`, `HasRecentBooks`, gated by `HasBooksLibrary`), `Views/HomeScreen.axaml` (two rows reusing the existing book card template and the section-row family), `Models/HomeBookCard.cs` if a subtitle ("Book 3 of Dune Chronicles") is needed.
**What:** Up next = for each series, the owned book with the smallest `SeriesIndex` greater than the highest index among the user's Finished books in that series, not itself Finished, newest-finished series first, max 10. Recently added = `AddedTime` within 30 days, never opened, newest first, max 10. The mixed Continue row is unchanged.
**Depends on:** Step 1.
**Verify:** `HomeFeedResolverTests` additions (finished gap handling, no index, multiple series, 30-day boundary, opened excluded), `HomeScreenViewModelTests`, real-window check.

## Step 13 — Wrap-up
**What:** lint trio plus every touched class (one run at a time; `MainViewModelTests` once, since `MainViewModel` is not expected to change here); update `docs/paperbunkr-todo.md` with what is actually verified; update the memory note; hand over an on-screen checklist at 1366×768 (tabs, both dropdowns, Shelves, Bookshelf in three themes, ghost cards, rail, Library Health section, Home rows). Do not run the dev exe against the real DB without a backup and the user's go-ahead.
**Depends on:** all.

## Order and parallelism

`1 → 2` first. Then `3, 4, 5, 8, 9` can proceed independently on top of 2 (they touch `BooksScreen.axaml`, so run them one at a time; XAML merges are the risk, not logic). `6` is independent and unblocks `7`. `10` follows `4`. `11` and `12` are independent of the grid and can go any time after `1`. Sessions that write code should not overlap on `BooksScreen.axaml`/`BooksScreenViewModel.cs`.

## Risks

- `BooksScreen.axaml` and `BooksScreenViewModel.cs` accumulate five steps of edits: keep each step's diff small and green before the next.
- Extracting the alphabet rail could disturb the Library: guard with the Library's existing tests and an on-screen check, and fall back to a copy if the extraction is not clean.
- Smart-list `Tag` evaluation loads all tags for every book (in-memory evaluation): fine at personal-library scale; revisit if a library passes ~20k books.
- Home is under active change by other sessions (the mixed Continue row); re-read `HomeScreenViewModel`/`HomeScreen.axaml` immediately before Step 12.
- The tree holds other sessions' uncommitted work (migrations, ComicVine dialogs); touch only the files listed and never stage others'.
