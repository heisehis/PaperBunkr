# Books improvement arc — Slice 1: data foundation (design)

Date: 2026-10-26. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding).
Part of the 30-idea Books/Detail/Reader pitch. Slices: **1 data foundation (this doc)** → 2 browse + Detail → 3 reader → 4 extras
(Calibre import, OPDS, dictionary look-up, TTS). Slice 1 covers pitch items 1, 2, 11, 12, 13, 21.

Decisions come from grilling rounds 1 and 2 (2026-10-26); all recommendations were accepted.

## 1. Goal

Give `Book` the metadata and personal state that comics already have (rating, tags, status, publisher, language, ISBN, series index), read
it from the files, let the user edit it, fetch it from public sources, track where each value came from, and write it back to EPUBs.
Slice 1 ships **data you can edit and see on Detail**. Browsing by it (lens tabs, filter, sort, group, badges) is Slice 2.

CE parity (standing rule): CE `ComicBook.Rating` is a float 0–5 rendered as stars, Favorites = rating > 3. Books follow the same scale;
there is no separate favorite flag. Verified in `_reference/ComicRackCE/ComicRack.Engine/Controls/RatingControl.cs` (`MaximumRating = 5`,
float `Rating`) and in `Issue.Rating`'s doc comment.

## 2. Approaches considered

| | Approach | Verdict |
|---|---|---|
| A | Add columns/tables to the Book schema, mirror the comic shapes (`IssueTag`, `Rating`), separate scraper flow | **Chosen.** Books already live in a fully separate schema; mirroring keeps editors/tag chips reusable without coupling the two schemas. |
| B | Unify Book and Issue behind a shared "LibraryItem" abstraction | Rejected. Large refactor across 5,000+ tests for no Slice 1 benefit. |
| C | Store everything in a JSON blob column | Rejected. Not queryable, so Slice 2's smart shelves and sorting would need a second migration. |

## 3. Data model (Paperbunkr.Data)

### 3.1 `Book` new columns
`Subtitle` (string?), `Publisher` (string?), `LanguageISO` (string?), `ISBN` (string?), `SeriesIndex` (float?, so 2.5 works), `Rating`
(float?, 0–5 in 0.5 steps, validated on write), `CommunityRating` (float?), `CommunityRatingCount` (int?), `Status` (`ReadingStatus`,
default Unread).

`Finished` becomes `[NotMapped] bool Finished => Status == ReadingStatus.Finished` with a setter that sets Status. **The `Finished` column
stays in the table, unused; no drop migration** (a past `DropColumn` caused the rollback-chain bug, see memory
`migration_rollback_orphan_column_bug`). Existing writers of `Finished` (reader's `PersistPosition(markFinished)`, `LoadBook` clear) keep
working through the setter.

### 3.2 `ReadingStatus`
`Unread = 0, WantToRead, Reading, Finished, Abandoned`. Transitions:
- first reader open of an Unread/WantToRead book → Reading;
- reader reaches the end → Finished (existing logic); reopen of a Finished book keeps today's behaviour (clears Finished → Reading);
- WantToRead and Abandoned are manual only and are never changed by the reader except Abandoned → Reading on open (reading it again is
  an answer).

Migration data step: `Finished = true` → Finished; else any position (`LastBlockId`, `LastProgressionFraction`, `LastChapterIndex > 0`) →
Reading; else Unread.

### 3.3 `BookTag`
`Id, BookId, Field (BookTagField: Genre | Tags), Value, Category (string?)`. Mirrors `IssueTag` **without `Weight`** (weights are a comics
idea). Unique on (BookId, Field, Value) case-insensitive. Cascade-delete with the book.

### 3.4 `BookFieldSource`
`Id, BookId, Field (string), Source (BookFieldSourceKind: File | OpenLibrary | GoogleBooks | Manual), FetchedUtc, SourceRef (string?, the
provider id/URL)`. One row per (BookId, Field). Written only when the scanner, a scraper or a user edit sets that field. Drives the
"from file / scraped / manual" chips and the Re-fetch action on Detail. A field with no row displays as "unknown".

### 3.5 Skip-forever
`Book.SkipMetadataMatch` (bool, default false). Mirrors CE `skip_forever` as already applied to issues in the scraper.

### 3.6 Migration
One migration `AddBookMetadataFoundation` (columns, two tables, index, data step above). It must be written to survive the repo's
migration up/down/up tests (no orphan columns on rollback; do not assert on a constant that a later migration changes). Add model-snapshot
check against the stale-Designer nullability bug.

## 4. Reading metadata from files (item 12, 13)

`BookFolderScanService` already extracts Title/Author/SeriesName from `source.Metadata`. Extend that metadata type and each format reader:

| Format | Fields |
|---|---|
| EPUB 2/3 OPF | `dc:publisher`, `dc:language`, `dc:identifier` (ISBN via `opf:scheme="ISBN"` or `urn:isbn:`), `dc:subject` → Genre tags, `calibre:series` + `calibre:series_index`, EPUB3 `belongs-to-collection` + `group-position`, `dc:description`, `calibre:rating` (0–10 → /2) |
| FB2 `title-info` | genre → Genre tags, lang, `sequence name/number`, publisher from `publish-info`, isbn |
| MOBI/AZW3 EXTH | publisher, ISBN, language, subjects, series where present |
| PDF | Info dict `Subject`/`Keywords` → Tags only (best effort) |

Series rule (item 13): **auto-assign a series only when the file declares one** (above). Title-only guesses ("Book 3 of …", sort names via
`TitleNormalizer`) never assign; they produce a **Suggested series** chip in Book Properties with Accept/Dismiss. Existing user-set series
are never overwritten.

Each value read from a file records `BookFieldSource(File)`.

### 4.1 Backfill
After the migration, a one-time **Refresh from files** job runs through Activity Center. It fills **only empty fields**, never overwrites
a user edit (a `Manual` source row always wins), and is re-runnable from the Books menu ("Refresh metadata from files"). Throttled so a
5k-book library does not saturate the disk.

## 5. Scraper (item 11)

New `BookMetadataProvider` abstraction with two implementations in Slice 1:
- **Open Library** (`/search.json`, `/isbn/{isbn}.json`, `/works`, covers API) — no account.
- **Google Books** (`/volumes?q=isbn:` / `intitle:+inauthor:`) — keyless, low quota; back off on 429.

Matching: ISBN first when present; otherwise title + author normalised with `TitleNormalizer`; candidates ranked by title similarity, author
match, language. Hardcover (needs a token) is out of Slice 1; the provider interface accepts it later as a Connections tile.

Flow (lighter than the comic scraper, no `MetadataProposal` rows because that table is keyed to `IssueId`/`SeriesId`):
1. Entry: Book Detail **Match metadata…** and Books grid bulk **Match metadata…** (selection bar + context menu).
2. A `DialogCard` (Large) lists candidates with cover, title, author, year, publisher; picking one shows a **per-field diff**
   (current vs proposed, checkboxes, default checked only for empty fields; changed non-empty fields default unchecked).
3. Accept writes the checked fields, a `BookFieldSource` row per field, and the cover only if the user ticks it.
4. **Skip forever** sets `SkipMetadataMatch`, excluding the book from every bulk run.
5. Bulk runs queue as an Activity Center job with a summary (matched / needs review / no match / skipped). Ambiguous books wait in the
   job's review list; they never block the run.

No automatic scraping on import in v1. A Scheduled Task hook can be added later without design changes (provider and job are reusable).

Credentials/ToS: Open Library and Google Books calls send a descriptive `User-Agent`; the Preferences > Connections page gets a
"Book metadata" tile (enable/disable, Google Books optional API key field) following the existing tile-gallery pattern.

## 6. Editing UI (item 2, status)

All new controls come from the shared families; no local restyling (see `paperbunkr-design-language`).

- **Book Properties overlay** (`BookPropertiesScreenViewModel`): new fields Subtitle, Publisher, Language (SuggestBox of ISO codes),
  ISBN, Series index, Status (SuggestBox), Rating (star control, 0.5 steps, clear button), Genre and Tags chip rows (`Border.pbChip`,
  type-ahead from existing book tags), Suggested-series chip. Field provenance chip beside each scraped/file value with **Re-fetch**.
  Undo/redo: `BookMetadataSnapshot` gains the new scalar fields; tags and status are included. Series-level fields remain out of
  history as today.
- **Bulk overlay**: stage Status, Rating, add/remove tags, Publisher, Language (empty = leave unchanged, like the existing six fields).
- **Quick actions**: Books grid context menu and Detail get **Rate ▸** (clear, ½ … 5) and **Set status ▸**. These write immediately and
  record undo history.
- **Detail** (display only in Slice 1): rating stars, status chip, tag chips, Publisher/Language/ISBN lines, source chips. The cinematic
  hero and dashboard are Slice 2.
- The star control: reuse the comic rating control if one exists in the app (grep found none under that name; plan step 1 confirms
  how Issue Properties renders `Issue.Rating`). If it is not reusable, add one `StarRating` control used by both Slice 1 editors, in
  `Styles/Controls.axaml` terms (tokens only, `PbAccent` fill).
- Keyboard: no new hotkeys in Slice 1 beyond what the overlays already get. (Rate-with-number-keys on Detail is a Slice 2 candidate
  through `InputActions`.)

## 7. Write-back (item 12)

Reuses `AppSettings.WriteMetadataToFiles` / `WriteMetadataAutomatically`. **EPUB only**; PDF/FB2/MOBI keep a sidecar like comics.
Writes `dc:publisher`, `dc:language`, `dc:identifier` (ISBN), `dc:subject` (tags/genres), `calibre:series` + `series_index` (and EPUB3
collection meta), `calibre:rating` (rating ×2), `dc:description`. Default off.

Safety: write to a temp file next to the EPUB, validate it reopens, then `File.Replace`. `File.Replace` fires the file watcher and the
library once flagged such books as missing (memory `writeback_watch_missing_bug`), so the write-back path must use the same suppression
the comic write-back already uses. Preserve unrelated OPF content (do not re-serialise the whole package; patch the metadata nodes).

## 8. Insights/home touch points

`ReadingEvent` already supports `ReadingItemType.Novel`; no schema change. Slice 1 only makes `PrimaryGenre` a real value for books (first
Genre tag) when events are written. Home "Continue Reading — Books" is unchanged until Slice 2.

## 9. Testing

- Data.Tests: migration up/down/up using `Database.Migrate()` plus ORM seeding (not bare INSERT), status data step, unique tag index,
  cascade deletes, rating validation.
- App.Tests: OPF/FB2/MOBI parsing fixtures (calibre series, EPUB3 collection, ISBN schemes), series-assignment rules, backfill only fills
  empties and respects Manual, provider parsing with recorded JSON fixtures (no live network), diff-selection logic, status transitions,
  undo/redo round-trip, OPF patch preserves unrelated nodes, bulk-overlay empty-means-unchanged.
- Lint: `XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`.
- Run the touched classes only (CLAUDE.md "Running the App tests"); never two `dotnet` runs at once.
- Real-window check of the Properties overlay and Detail on the user's 1366×768 screen after build.

## 10. Out of scope (Slice 1)

Lens tabs, filters, sort/group and grid badges by status/rating/tags; smart shelves; series-gap view; cinematic Detail hero; reading
dashboard; Hardcover; scheduled auto-scrape; Calibre import; OPDS; reader changes.

## 11. Risks

- Migration touches the shared dev DB (worktrees share it; do not build in a worktree against the live DB).
- OPF patching on real-world EPUBs with odd namespaces: mitigate with temp-write + reopen validation and a fixture set of messy OPFs.
- Provider rate limits (Google Books keyless): serial requests with backoff; the bulk job reports "rate limited" distinctly.
- `Finished` shim: any code that queries `Finished` in LINQ (not the setter) breaks because it is `[NotMapped]`; the plan lists a grep
  and converts those queries to `Status == Finished`.

## 12. Open items for the implementation plan (not design questions)

Exact location of the EPUB/FB2/MOBI metadata types; whether a reusable star-rating control exists; the precise suppression hook the comic
write-back uses; names of the migration's test classes.
