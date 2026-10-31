# Books improvement arc — Slice 4a: Calibre import (design)

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding). Decisions: Slice 4 grilling rounds 1 and 2 (2026-10-27), all recommendations accepted. **Assumed** = not asked.

## 1. Goal

Bring a Calibre library's books and metadata into Paperbunkr without touching the Calibre library.

## 2. Facts checked against a real library

The user's own library (`C:\Users\DeeDee\Calibre Library`, 40 books, EPUB and PDF, no ratings, no custom columns) was inspected through a copy of `metadata.db`. The real layout matches what this design assumes:

- `books(id, title, sort, timestamp, pubdate, series_index, author_sort, path, uuid, has_cover, last_modified)`; the book's folder is `books.path` under the library root and holds `cover.jpg` (when `has_cover`) and `metadata.opf`.
- `data(book, format, name)`: one row per format; the file is `{root}/{path}/{name}.{format lower-case}`.
- Link tables `books_authors_link`, `books_series_link`, `books_tags_link`, `books_publishers_link`, `books_languages_link`, `books_ratings_link`, joined to `authors`, `series`, `tags`, `publishers`, `languages(lang_code)`, `ratings(rating)` (0–10); `comments(book, text)`; `identifiers(book, type, val)`; `custom_columns(id, label, name, datatype)` with per-column tables `custom_column_{id}`; `library_id(uuid)`.
- Calibre 7 also has `annotations` and `last_read_positions`; **out of scope** here.

## 3. What is imported

For each book with at least one supported format, in the order EPUB, FB2, MOBI, PDF (**one Paperbunkr book per Calibre book**, referencing the file in place; nothing is copied or moved):

| Calibre | Paperbunkr |
|---|---|
| title | `Title` |
| authors (link order) | `Author`, joined with ", " |
| series + `series_index` | `BookSeries` via the existing series suggester + `SeriesIndex` |
| tags | `Book.Tags` (additive) |
| publisher, `pubdate`, language | `Publisher`, `PublishedDate`, `LanguageISO` |
| comments | `Summary` (HTML stripped to text) |
| identifiers | `ISBN` when type `isbn`; other identifiers are not stored (**Assumed**: no new table) |
| rating (0–10) | `Rating` (half-stars: 10 → 5.0) |
| `cover.jpg` | the cover, through the existing cover cache |
| one chosen custom column (bool, or text with Read / Finished) | `Status = Finished` (optional) |

Values go through `BookMetadataApplier` with a new `BookFieldSourceKind.Calibre` (stored by name, no schema change): **FillEmpty** by default, so a Manual field is never touched and nothing already filled is overwritten; the dialog's "also refresh existing" switch uses **Override only for fields not marked Manual** (a Manual field wins in both modes).

## 4. Matching and repeat imports

- New columns on `Book`: `CalibreBookId` (int?) and `CalibreLibraryId` (string?, the `library_id.uuid`) — one migration `AddBookCalibreLink`.
- Match order: (1) same `CalibreLibraryId` + `CalibreBookId`; (2) same resolved file path; (3) ISBN; (4) normalized title + author, **only when unambiguous**. Matches by 3 or 4 are *needs review* rather than merged: listed in the result with Link / Skip.
- Repeat imports update by (1) and add new books; they never delete a Paperbunkr book. A Calibre book that disappeared is only counted ("N no longer in Calibre").
- Books whose chosen file is missing on disk are counted as "file not found" and skipped.

## 5. Where it lives

"Import from Calibre…" in the Books screen's overflow menu opens a `DialogCard`: a library-folder picker (validated by the presence of `metadata.db`), the read-status column picker (None / each bool or text custom column), the "also refresh existing" switch, and an Import button. Progress uses the Activity Center job pattern; the result is a summary card (added, updated, skipped, file not found, needs review) with the needs-review list beneath it. **Assumed**: the Calibre folder is *not* added as a scanned Books folder, so Calibre-managed files are never rescanned or rewritten by Paperbunkr's own write-back; books imported this way have metadata write-back disabled (`SkipMetadataMatch` untouched; a new import-time `ExcludeFromWriteBack` is not added — the write-back queue already skips files outside library folders, **verified in the plan step**).

## 6. Safety

`metadata.db` is opened read-only (`Mode=ReadOnly`, no journal writes) from a shared-read handle, with a fallback to a temp copy when Calibre has it locked. Nothing is ever written under the Calibre folder.

## 7. Tests and verification

- A fixture builds a minimal `metadata.db` with the real table layout above plus a fake library folder (a tiny EPUB per book), so tests need no Calibre.
- Unit tests: format choice, author/series/tag/rating mapping, HTML-to-text, the four match tiers, Manual protection in both modes, repeat import idempotence, missing files, a locked-database fallback, custom-column read status.
- **On the real library (read-only):** import the user's 40-book library into a *scratch* database (`DatabasePathOverride`), never the live one, and check counts, the duplicate Martin author folders, and covers.
- On screen later: dialog, progress and summary at 1366×768.

## 8. Out of scope

Writing to Calibre, Calibre annotations and reading positions, importing every custom column, Calibre's virtual libraries and saved searches, converting formats, copying files into the library, import by device or content server.
