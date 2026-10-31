# Books improvement arc — Slice 2a: browse and shelves (design)

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding). Builds on Slice 1 (`2026-10-26-books-slice1-data-foundation-design.md`).
Pitch items 3 (smart shelves), 4 (bookshelf view), 5 (Collections), 6 (duplicate formats), 7 (series gaps), 8 (alphabet rail), 10 (Home rows).
Decisions: grilling round 1 for Slice 2 (2026-10-27), all recommendations accepted. Anything marked **Assumed** was not asked and is open to change at review.

## 1. Goal

Turn the Slice 1 data (status, rating, tags, publisher, language, series number) into ways to browse: status tabs, more sort/group axes, one-badge cards, a Shelves menu, a Bookshelf view, series gaps, an alphabet rail, duplicate-format findings and better Home rows. No schema change except persisted view settings.

## 2. Status tabs

A tab strip under the Books header (`Button.tab`, `active`, `Spacing="20"`, the shared family; arrow/bumper navigation comes free from `TabStrip.Step`): **All · Reading · Want to read · Unread · Finished · Abandoned**, each with a count pill. Counts and filtering come from `BooksScreenViewModel.Rebuild()` over `_allCards` (`BookCardSample` gains `Status`, `Rating`, `Progress` (0-1), `Publisher`, `Language`, `PublishedDate`, `SeriesIndex`, `Genres`). The active tab persists (`AppSettings.BooksActiveStatusTab`, stored by name, default All). **Assumed:** tab changes replay the list entrance once, like sort changes do.

## 3. Sort, group, badges

- **Sort** adds Rating, Progress, Publisher, Published, SeriesOrder to `BooksSortField` (stored by name, so no migration beyond the new enum members). Books without a value sort last in both directions.
- **Group** adds Status, Publisher, Language, Genre to `BooksGroupField`. A book with several genres appears under its first genre only (**Assumed**, to keep counts honest). Group header rules from the Books browse chrome (blank → "Unknown …", last) apply.
- **Badges** follow the Library's one-badge rule: progress ring while Reading, check when Finished, nothing otherwise. The rating shows as small stars on hover and in the List/Bookshelf tooltip, not as a permanent badge.

## 4. Shelves ▾

One toolbar dropdown (`Border.dropdown` + `Button.menuRow`, `PopupFit.ToWindow`, one ScrollViewer) with three sections:
1. **Built-in:** Favorites (rating ≥ 4), Unfinished (Reading or progress > 0 and not Finished), Not rated.
2. **Smart shelves:** the user's novel smart lists. `NovelSmartListCatalog` gains fields Rating, Status, Tag, Publisher, Language, SeriesIndex and ProgressPercent (selectors on `Book`, plus `NovelSmartListQueryBuilder` translation where it builds SQL); the existing smart-list editor offers them with no other change.
3. **Collections:** the user's Collections that contain books (`Book.CollectionItems` already exists).

Choosing a shelf filters the grid (tabs and search still apply); the chosen shelf shows as a dismissible chip. Shelf choice is session-only (**Assumed**).

## 5. Bookshelf view

`BooksViewMode { Grid, Bookshelf }` (a segmented switch, `Border.segGroup`), persisted. Bookshelf reuses `VirtualizingVariableWrapPanel` with `UseSpineWidths`: spine width scales with `CharacterCount` (clamped; PDFs and unparsed books get a default width), colour from the cover's dominant colour (existing `CoverBrush` fallback). Click opens Detail; the spine tooltip shows title, author, rating and progress. No List view in this slice.

## 6. Series gaps

In Series mode of Book Detail and in the grouped-by-Series grid, a ghost card "#3 missing" appears only for **holes between owned series numbers** (own 1, 2, 4). Never a tail, never when books lack `SeriesIndex`. Ghost cards are not selectable and offer no actions (**Assumed**: no "add to wanted" for books yet).

## 7. Alphabet rail and pinned headers

Reuse the Library's alphabet rail and pinned group header implementation (`LibraryScreen`), generalised if needed so both screens share one control. Applies when sorted by Title or Author, and to group headers.

## 8. Duplicate formats (Library Health)

A new check in Library Health › Duplicates: books with the same normalised title (`TitleNormalizer.StripDown`) and author in more than one format (or ISBN equal). Rows list the files with format chips and sizes; actions are the existing Reveal in Explorer and Delete (Recycle Bin). Report-only: no hide flag, no "primary" concept.

## 9. Home rows

`HomeFeedResolver` gains `GetUpNextInSeries` (the book with the next `SeriesIndex` after the user's most recent Finished book in a series, if owned and not Finished) and `GetRecentlyAddedBooks` (last 30 days, not opened). Home shows up to three book rows (Continue, Up next, Recently added), each only when non-empty and only when a Books library exists (`HasBooksLibrary`).

## 10. Persistence and motion

New settings: `BooksActiveStatusTab`, `BooksViewMode` (migration `AddBooksBrowseViews`, two columns with defaults). Motion roles per the design language: tab content `Replace`, dropdown `Reveal`, no new bespoke animation. Sizes and tokens only; the 1366×768 minimum: the toolbar sheds width in steps (Shelves label → icon below 1000 px).

## 11. Tests and verification

Data.Tests: Rebuild filtering/sorting/grouping per axis (nulls last), smart-list selectors and SQL translation for the new fields, duplicate-format finder, Home resolvers (up-next ordering, finished-gap cases), gap detection. App.Tests: tab counts and persistence, Shelves composition, bookshelf width math, VM tests with temp SQLite. Lint trio. On-screen check at 1366×768, light/dark/Matrix.

## 12. Out of scope

List view, Editions panel, author page (2b), Detail changes (2b), reader changes, scheduled rules for shelves.
