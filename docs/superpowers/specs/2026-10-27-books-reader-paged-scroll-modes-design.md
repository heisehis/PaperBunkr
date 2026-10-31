# Books reader: paged and scroll modes, chapter units, book pages

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source: `BookPageMap` and its tests; approved in a grilling round "all recommended"; on-screen paged/scroll modes user-verified).
Scope: the reflowable Books reader (EPUB, FB2, MOBI) in `BookReaderScreen` / `BookReaderScreenViewModel`. PDF is untouched.
Follows: `2026-09-07-books-reader-pagination-and-position-fix-design.md` (paged mode switched off) and the 2026-10-27 contents fix
(`BookTocBuilder`, `BookChapter.Sections`).

## 1. Problem

- The reader shows one EPUB *file* at a time as one long vertical scroll. Many books pack several chapters into one file (Glass Sword: 26
  chapters in 6 files), so "a page" holds several chapters and scrolls for a long time.
- There is no paged reading. CSS-column paging was tried three times (vw sizing, exact pixel sizing, `transform` instead of `scrollLeft`);
  every attempt showed the next column bleeding in at the right edge, so `UseColumnPaging` was set to `false` on 2026-09-07.
- There are no page numbers. Six of the 35 EPUBs in the user's Calibre library (the Herbert Dune set) carry a print page list; nothing reads it.

## 2. Decisions (grilling round, 2026-10-27)

| # | Decision |
|---|---|
| Q1 | The reading unit is one **contents entry**, not one file. A file is cut at its entries' anchors; a file with no entries is one unit. |
| Q2 | Paged mode does **not** use CSS columns: one vertical column, shown one screenful at a time, each page trimmed to whole lines. Print page lists are read and used as labels. No two-page spread yet. |
| new | In paged mode every unit starts on a **fresh page**; in scroll mode every unit is its own scroll, so it always starts at the top. |
| Q3 | Scroll mode is one unit per scroll. Scrolling on past the end opens the next unit at its top; scrolling up past the top opens the previous unit at its bottom. Units are never stacked in one document. |
| Q4 | A **Paged / Scroll** switch in the font sheet, app-wide, remembered. Default **Paged**. No per-book override. |
| Q5 | No new shortcuts. `BookNextPage` / `BookPreviousPage` (Right, PageDown, Space / Left, PageUp) turn a page in paged mode and scroll one screen in scroll mode. Up/Down nudge in scroll mode only. |
| Q6 | Footer: book page plus the screen page within the unit in paged mode; book page plus percentage in scroll mode. Recomputed on font or window change. |
| Q7 | Auto-scroll only in scroll mode (its switch is disabled in paged mode). The ruler works in both. Read aloud turns pages in paged mode. |
| Q8 | A short page-turn slide on the Replace motion role; Reduced motion turns it off. No separate animation setting. |
| Q10 | **Book pages** are numbered by word count, independent of font and window: "p. 214 of 640". When the file has a print page list, the print numbers are shown instead. |
| Q11 | Book page range default **250-300 words**. A page ends at the first paragraph end inside the range; a paragraph over the maximum is split at a sentence end; a new unit always starts a new book page. Set in Preferences > Reader. |
| Q12 | **Go to page** jumps to the start of a book page (or print page). |
| Q9 | Open: the user mentioned "the redesign" these modes are needed for, without saying which. Nothing here assumes a particular shape; if it is a webtoon-style strip or a stacked continuous document, §5.3 is where it would change. |

Also decided (no question needed): switching modes, resizing and font changes keep the place through the top-of-page block anchor the reader
already stores; the Detail screen's "Continue — Ch. N" and "Chapter N of M" switch to contents numbering.

## 3. Approaches considered

1. **CSS columns, a fourth time.** Rejected: three independent attempts failed with the same bleed, and the cause (how the embedded WebView
   composites multi-column content under `overflow: hidden`) is not something we can fix from the page.
2. **Paginate in C#** (measure text with Avalonia and hand the WebView one page of HTML at a time). Rejected: C# cannot reproduce the
   browser's line breaking, hyphenation, fonts and image sizing, so page edges would drift from what is drawn.
3. **Chosen: line-aware paging inside the page.** The chapter is laid out once as a normal single column; JavaScript reads where the line
   boxes actually fall and builds a list of page tops so no line is cut. A page is shown by translating the content and clipping below the
   page's last whole line. The browser does all layout; nothing scrolls natively (the trigger for the earlier repaint suspicion).

## 4. Reading units

New `BookUnitSlicer` (App/Services, pure C#):

- `IReadOnlyList<BookUnit> Units(BookChapter file, int fileIndex, string injectedHtml)`: one unit per contents entry of the file (the same
  rows `BookTocBuilder` builds, in the same order), each a slice of the file's **injected** HTML (block ids assigned over the whole file first,
  so ids and every stored highlight, bookmark and position stay valid). A slice runs from the `<` of the element carrying the entry's anchor
  to the next entry's element. Content before the first anchor (a file's preamble) joins the first unit. A file with no entries is one unit.
- Cutting at an element can leave a wrapper unclosed or a closing tag without its opener; the browser repairs both. A wrapper's class
  styling is lost for the units after the first (disclosed limit; the "book's own styling" switch already only reaches inline styles).
- `int UnitOf(units, blockId)`: the unit containing a block, by offset. Used by bookmarks, highlights, search results, resume and Go to page,
  which all still store `(file index, block id)`.

View model:

- `_position` keeps `ChapterIndex` = file index and `BlockId`; a new `_unitIndex` (global, into the flattened unit list) is derived from
  them. `CurrentChapterHtml` becomes the current unit's HTML.
- `NextPage` / `PreviousPage` (called when the page reports "end"/"start") move by unit, not by file. Reaching the last unit marks the book
  finished, as reaching the last file does today.
- The contents drawer's active row and Detail's Chapters tab use the unit directly instead of `BookTocBuilder.ActiveIndex`'s offset search.
- `Book.ChapterCount`, `Book.LastChapterIndex` and Home's progress stay file-based (unchanged data). Detail's labels use the unit index
  computed from `LastChapterIndex` + `LastBlockId`.

## 5. Modes

`AppSettings.BookReaderLayout` (`Paged` | `Scroll`, stored by name, default `Paged`), new migration. The font sheet gets a **LAYOUT** card at
the top: a `segGroup` with "Paged" and "Scroll". Changing it re-renders the current unit and returns to the same block.

### 5.1 Page document

`PushCurrentChapterHtml` keeps one DOM shape for both modes (`#pb-viewport` > `#pb-content`) and sets `window.pbLayout = 'paged' | 'scroll'`.
`UseColumnPaging`, `pbApplyPagination`, the column functions and their CSS are deleted; the scroll-snap rule goes too (it fights paging and
was a dressing for the fallback).

### 5.2 Paged

- `#pb-viewport` is `overflow: hidden`; `#pb-content` is laid out normally at the viewport width.
- `pbBuildPages()` runs after load, on resize and after any typography change. It walks the text nodes with `Range.getClientRects()` and
  the replaced elements (images, `hr`, tables) with `getBoundingClientRect()`, collecting line boxes `(top, bottom)` in content coordinates.
  Page *k* starts at the top of the first line not on page *k-1* and ends at the bottom of the last line that fits within the viewport
  height. Result: `window.pbPages = [{ top, bottom }]`.
- Showing page *k*: `#pb-content` gets `transform: translateY(-top)` and `#pb-viewport` gets `clip-path: inset(0 0 (viewportHeight - (bottom - top)) 0)`
  so the part of a line below the page edge never shows. Space left at the bottom of a page stays empty, as in print.
- Images get `max-height` equal to the viewport height (CSS), so an image always fits one page. A single line taller than the page (a huge
  heading at a huge font) gets a page to itself and is clipped; no infinite loop.
- `pbNextPage()` / `pbPreviousPage()` return `'moved:<fraction>'`, `'end'` or `'start'` exactly as today, so the C# side
  (`NextPageAsync`/`PreviousPageAsync` → VM fallback) is unchanged. Going back into the previous unit lands on its **last** page
  (`pbShowLastPage()`), which the C# side requests through the existing pending-scroll hand-off.
- `pbScrollToBlock(id)` shows the page containing the element's top. `pbReportPosition` reports the first block whose top is on the page.
- Mouse wheel in paged mode turns one page per gesture (250 ms debounce, so a touchpad flick is one page). Up/Down do nothing.

### 5.3 Scroll

- `#pb-viewport` scrolls vertically, as today, one unit per document.
- At the bottom, a further wheel turn or a `BookNextPage` press past the end opens the next unit at its top; at the top, scrolling up or
  `BookPreviousPage` opens the previous unit at its bottom. A small 400 ms "settle" guard stops one long fling from skipping several units.
- Auto-scroll, the ruler and read aloud work as today.

### 5.4 Page turn motion

The C# side pushes `window.pbMotionMs = MotionTokens.Get(Replace)` when `MotionPolicy.AllowsMotion(Replace)`, else 0. A page turn fades
the content in from a 6 px horizontal offset in the direction of travel (Replace role, `PbMotionEase` curve). 0 means instant.

### 5.5 Reading aids in paged mode

- Auto-scroll: the font sheet switch and its key are disabled while Paged is on; switching to Paged stops a running auto-scroll.
- Ruler: moves line by line inside the page; moving past the page's last line turns the page and lands on its first line (and the reverse).
- Read aloud: when the spoken block starts on another page, that page is shown (`pbSetSpeaking` calls `pbScrollToBlock` in paged mode
  instead of `scrollIntoView`).

## 6. Book pages

### 6.1 Word-count pages

New `BookPageMap` (App/Services, pure C#), built once per open book off the UI thread and cached in memory by file path + last-write time +
range:

- Input: every unit's blocks in reading order (block id + plain text, from the same leaf-block walk `BookReadAloudPlan` uses, moved into a
  shared `BookBlockReader`).
- A page accumulates blocks. Before adding a block, if the page already holds at least `min` words and adding it would pass `max`, the page
  closes. A block with more than `max` words on its own is split at sentence ends (`.`, `!`, `?`, `…` followed by space) into pieces of at
  most `max` words; a piece that cannot be split (no sentence end) stays whole. Every unit starts a new page.
- Output: `Pages: [(unitIndex, blockId, wordOffsetInBlock)]` and `PageOf(blockId) → int` (the page holding the block's first word).
- The footer's "p. *n*" uses `PageOf(top block)`. For a split paragraph that is the page where the paragraph starts (approximation,
  disclosed).

### 6.2 Print page lists

- `IBookTextSource` gains `IReadOnlyList<BookPrintPage> PrintPages` (label, file index, element id); empty for FB2, MOBI and EPUBs without
  one. `EpubBookSource` reads the EPUB 3 nav `page-list` and falls back to the EPUB 2 NCX `pageList` (both through VersOne's schema; parsed
  from the archive if the schema does not expose it).
- When present, the footer shows the print label of the last page marker at or before the top block ("p. 214"), and the total is the last
  label ("of 890"). Go to page takes print labels (roman numerals included, matched as text).

### 6.3 Footer and progress

`ProgressLabel` becomes:

- Paged: `p. 214 of 640 · Nine · 3 / 12` (book page, unit title, screen page within the unit).
- Scroll: `p. 214 of 640 · Nine · 37%` (percent through the unit).
- While the page map is still building (large book): the unit title and screen page / percent only.

`ProgressFraction` (the thin bar) becomes book page / total pages.

### 6.4 Setting

`AppSettings.BookPageWordsMin` / `BookPageWordsMax` (default 250 / 300; clamped 100-600, max ≥ min + 20), in the same migration as the
layout. Preferences > Reader gets a **Books** card with one `SettingsRow` "Words per book page" holding two numeric fields. Changing it
rebuilds the map the next time a book opens.

### 6.5 Go to page

A "Go to page" field at the top of the Chapters drawer: "Go to page [ 214 ] of 640", Enter jumps (closes the drawer, deferred one dispatcher
tick per the routed-event rule). The top bar is not used: at 1366 px it already holds seven icon buttons, and the drawer is where the
contents already are. An out-of-range or unknown print label shows "No page 999" under the field.

## 7. Detail screen

`BookDetailScreenViewModel` uses the unit index for "Continue — Ch. *n*" and "Chapter *n* of *m*" (*m* = number of units). The Chapters
tab's current row comes from the same unit lookup. No book-page numbers on Detail (they need the page map, which only the reader builds).

## 8. Data and migrations

One migration, `AddBookReaderLayout`: `AppSettings.BookReaderLayout` (text, default `'Paged'`), `BookPageWordsMin` (int, 250),
`BookPageWordsMax` (int, 300). No change to `Book`, bookmarks or highlights.

## 9. Testing

Automated (C#):

- `BookUnitSlicerTests`: slicing at anchors, preamble joining the first unit, file without entries, ids preserved, `UnitOf`.
- `BookPageMapTests`: range boundaries, unit starts a page, long paragraph split at sentences, unsplittable paragraph, `PageOf`.
- `EpubPrintPageTests`: a fixture EPUB with an EPUB 3 page-list and one with only an NCX pageList.
- `BookReaderScreenViewModel` tests: next/previous move by unit across files, last unit finishes the book, a bookmark in a later unit of the
  same file opens that unit, mode setting persists, auto-scroll refuses to start in paged mode, footer label in both modes.
- Migration test for the three columns; `XamlConsistencyLintTests` for the new card and drawer field.

Not testable headless (the WebView does not run in tests), checked on screen at 1366×768 with Glass Sword and Dune:

- Paged: no line cut at the page bottom, at three font sizes; images fit; chapter starts a page; last page of the previous unit on Back.
- Scroll: over-scroll moves units one at a time; reverse lands at the bottom.
- Mode switch, resize and font change keep the place; ruler and read aloud turn pages; Reduced motion removes the slide.
- Footer numbers in both books (Dune shows print pages).

## 10. Out of scope

Two-page spread; PDF; stacking several units in one scroll; per-book layout override; book-page numbers on Detail or Home; a page-turn
animation setting.
