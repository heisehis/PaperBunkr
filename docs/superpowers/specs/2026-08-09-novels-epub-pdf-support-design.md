# Novels — EPUB/PDF Support — Design Spec

*Date: 2026-08-09. Scope: a new, independent "Books" section for prose novels (EPUB and PDF),
covering library/import, a reflowable text reader, resume position, bookmarks, and in-book search.*

**CE-verification note (per the standing CE-parity rule):** ComicRackCE is a comic reader with no
prose-reading equivalent to check against — its native page-image pipeline (`ComicBook`,
`IComicPageProvider`, `Controls/ComicView`) has nothing analogous to a reflowable text renderer or
an EPUB/PDF-as-prose concept. This feature is new ground, not a CE-parity gap being closed, and no
CE source was or should be consulted for it.

## 1. Scope

Covers:
- A new **Books** nav-rail section, independent of the existing comics Library.
- `.epub`/`.pdf` folder-scan import (mirrors the comic library's `WatchedFolder` scan), plus cover
  and metadata extraction (title/author/series-name).
- A reflowable text reading screen: immersive chrome (hides while reading, tap to reveal a TOC
  drawer and a font/theme sheet), chapter/TOC navigation, adjustable text size/font
  family/line spacing, and Light/Dark/Sepia/Match-app-skin themes.
- Resume position (survives font/theme changes and window resizes, since it's tracked by character
  offset rather than page number) and manual bookmarks.
- In-book full-text search (linear scan over the already-parsed chapter text of the open book).

Explicitly deferred, not part of this pass:
- **Smart Lists / Reading Lists / Categories for Books.** Those subsystems (matchers, condition
  fields, CBL import) are shaped around the comic schema (Penciller, Genre-as-Issue-field, etc.);
  extending them to Books is a separate future design, not a mechanical reuse.
- **Series-conflict resolution UI for `BookSeries`.** The comic library's `SeriesConflict` queue is
  not reused — book-series import matching is a plain exact-name match, no ambiguity workflow.
- **Cross-library / cross-book search.** In-book search only; a persistent full-text index across
  the whole Books library is future scope if it turns out to matter.
- **Fixed-layout fallback view for PDFs that reflow badly.** The architecture doesn't block adding
  one later (a `Book` could gain a `PreferFixedLayout` flag and route to the existing
  `pdfium`-backed fixed-page pipeline already used for comic PDFs), but it isn't being built now —
  reflow-quality problems are accepted as a known limitation of this pass, per the approach decided
  below.
- **Manual single-file "just add one book" flow** was considered and dropped in favor of folder
  scan as the only import path for v1 — simpler to build and test one path than two.

## 2. Data model

New tables, independent of `Series`/`Issue` — no shared columns, no FK crossing between the comic
and book schemas:

```csharp
public class BookSeries
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SortName { get; set; }
    public string? Author { get; set; }   // series-level author, e.g. a trilogy by one writer

    public List<Book> Books { get; set; } = new();
}

public enum BookFormat
{
    Epub,
    Pdf,
}

public class Book
{
    public int Id { get; set; }
    public int? BookSeriesId { get; set; }   // null: standalone novel, no series
    public BookSeries? BookSeries { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public BookFormat Format { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? CoverImagePath { get; set; }
    public string? Summary { get; set; }
    public DateTime? PublishedDate { get; set; }
    public DateTime AddedTime { get; set; }

    // Read-state, named to match Issue's existing convention (OpenedTime / LastPageRead) rather
    // than inventing new naming for the same concept.
    public DateTime? LastOpenedTime { get; set; }
    public int LastChapterIndex { get; set; }
    public int LastCharacterOffset { get; set; }

    public List<BookBookmark> Bookmarks { get; set; } = new();
}

public class BookBookmark
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int ChapterIndex { get; set; }
    public int CharacterOffset { get; set; }
    public string Excerpt { get; set; } = string.Empty;   // snippet around the offset, for display
    public DateTime CreatedTime { get; set; }
}

public class BookFolder
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;   // unique, mirrors WatchedFolder.Path
}
```

`PaperbunkrDbContext` gets four new `DbSet<T>` properties and `OnModelCreating` entries (enum
columns stored as their string name via `HasConversion<string>()`, matching every other enum in the
context) plus a new EF Core migration. No changes to any existing entity.

## 3. Import pipeline

`BookFolderScanService`, structurally parallel to the comic scan service: walks each `BookFolder`
path, filters to `.epub`/`.pdf`, and for each not-already-imported file:
1. Picks `EpubBookSource` or `PdfBookSource` by extension (§4).
2. Reads `Metadata` (title/author/series-name) and cover image bytes from the source.
3. Matches an existing `BookSeries` by exact case-insensitive name, or creates one if the book
   declares a series name and none matches.
4. Inserts the `Book` row; caches the cover to disk in its own cache directory (same on-disk cache
   pattern `CoverThumbnailService` already uses for comics, kept separate rather than shared since
   the two caches key on different entity types).

## 4. Format parsing

Both formats implement a shared contract so the importer/reader don't branch on format beyond
picking which implementation to construct:

```csharp
public interface IBookTextSource
{
    BookMetadata Metadata { get; }         // Title, Author, SeriesName, cover bytes
    IReadOnlyList<BookChapter> Chapters { get; }
}

public class BookChapter
{
    public string Title { get; set; } = string.Empty;
    public IReadOnlyList<BookParagraph> Paragraphs { get; set; } = Array.Empty<BookParagraph>();
}

public class BookParagraph
{
    public string Text { get; set; } = string.Empty;
    // Minimal inline formatting only — bold/italic spans as (start, length) ranges over Text.
    // Everything else a source format might carry (images, tables, multi-column layout, embedded
    // fonts, footnote markers) is dropped. Prose readability is the goal, not layout fidelity.
    public IReadOnlyList<(int Start, int Length, bool Bold, bool Italic)> Spans { get; set; }
        = Array.Empty<(int, int, bool, bool)>();
}
```

**`EpubBookSource`** — via the `VersOne.Epub` NuGet package (MIT, actively maintained,
netstandard2.0-compatible): chapters come pre-segmented from the book's own spine, `Navigation`
gives real chapter titles for the TOC, `Content.Cover` gives the cover image, OPF `Metadata` gives
title/author/series (EPUB3 `belongs-to-collection`/`group-position`, falling back to no series if
absent — most EPUB2 files won't have this).

**`PdfBookSource`** — reuses the `pdfium.dll` native binary already bundled for comic PDF page
rendering (`bblanchon.PDFium.Win32`, already a `Paperbunkr.Engine` dependency), via its
`FPDFText_*` text-extraction API. Chapters come from the PDF's outline/bookmarks if present;
if the file has none, the whole document becomes one synthetic chapter (surfaced in the TOC panel
as "No chapters found" rather than silently mis-splitting). If the currently-referenced
`PDFiumSharpV2` managed wrapper doesn't expose text extraction directly, a small number of raw
`DllImport` declarations get added alongside it — same shape as `HeifAvifImage`'s one raw
`heif_check_filetype` P/Invoke next to its `LibHeifSharp` wrapper, not a new native dependency.

## 5. Reader UI

Confirmed via mockups (visual-companion session, both approved as-is):

- **Immersive layout.** The reading pane is edge-to-edge text; tapping/clicking it reveals a thin
  top bar (☰ for TOC, "Aa" for the font/theme sheet) and a bottom progress indicator, which
  auto-hide again. Chosen over a permanently docked TOC-sidebar layout for being closer to how
  dedicated ebook readers (Kindle, Apple Books) actually work, which matters more here than matching
  the comic screens' rail-and-sidebar chrome, since Novels are a deliberately separate section.
- **Font/theme sheet** (slides up from "Aa"): text size (slider), font family (Serif/Sans/Mono),
  line spacing (Compact/Normal/Relaxed), and theme (Light/Dark/Sepia/Match-app-skin) — all four
  confirmed in scope for v1.
- **Pagination is computed at render time, not stored.** A custom Avalonia control lays out
  `BookParagraph`s with `TextLayout`, measuring against the current viewport size and font settings
  to find page breaks live. This is why position is tracked as `(ChapterIndex, CharacterOffset)`
  rather than a page number — a stored page number would go stale the moment font size or window
  size changes; a character offset doesn't.
- **TOC drawer** lists `Chapters[].Title`, tapping jumps to `(ChapterIndex: n, CharacterOffset: 0)`.

## 6. Resume position & bookmarks

- Resume: `Book.LastChapterIndex`/`LastCharacterOffset`/`LastOpenedTime` written on navigation away
  from the reading screen (chapter change, close), read on open to restore position — same shape as
  `Issue.LastPageRead`/`OpenedTime`.
- Bookmarks: created from the reading screen at the current offset, stored as `BookBookmark` rows
  with a text `Excerpt` (words surrounding the offset) so a bookmarks list is recognizable without
  re-opening the book to check.

## 7. Search

Since a book's `Chapters` are already fully parsed into memory once opened, in-book search is a
linear substring/regex scan over that already-loaded text — no persistent index needed. Matches
jump to `(ChapterIndex, CharacterOffset)` like TOC navigation does.

## 8. Testing

- `EpubBookSource`/`PdfBookSource` unit tests against small fixture files (same pattern as the
  Reader Canvas's fixture comic archives).
- Pagination/character-offset math as pure-function unit tests, independent of live rendering — same
  shape as the existing `ZoomPanMathTests` for the comic reader's zoom/pan.
- `BookFolderScanService` tests mirroring the existing comic-folder scan tests.
- **Manual-only:** actual on-screen pagination, TOC navigation, and font/theme switching need
  eyes-on verification — no unattended desktop GUI automation available, same caveat already
  tracked in `alpha-todo.md` for the Reader's zoom/pan gestures.

## 9. Risks

1. **PDF reflow quality is inherently inconsistent** for footnotes, multi-column layouts, and
   running headers/footers bleeding into the extracted text — an accepted trade-off, not a defect
   to chase perfection on. A future fixed-layout fallback per-book (§1) is the intended escape
   hatch if this proves too rough in practice, not something to build preemptively.
2. **`PDFiumSharpV2` text-extraction API surface is unconfirmed** — resolved during implementation
   per §4, not blocking the design.

## 10. Suggested phasing

For the implementation plan to stage rather than attempt as one drop, matching how the rest of the
alpha roadmap shipped in numbered slices:
- **Phase 1** — data model + migration, `BookFolderScanService`, Books nav section with a
  grid/list view and covers. No reading yet.
- **Phase 2** — `EpubBookSource` + `PdfBookSource`, the text-flow renderer, immersive reading
  screen, TOC drawer, font/theme sheet.
- **Phase 3** — resume position, bookmarks, in-book search.
