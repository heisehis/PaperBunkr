# Books improvement arc — Slice 2b: Book Detail (design)

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding). Builds on Slice 1 and 2a.
Pitch items 16 (reading dashboard), 17 (notes hub), 18 (chapters with read state), 20 (author view); 22 reduced to a better Read button; 19 (Editions) deferred.
Decisions: grilling round 1 for Slice 2, all accepted. **Assumed** = not asked.

## 1. Goal

Make Book Detail answer "where am I, how fast am I reading, what did I mark, and what else do I own by this author" without becoming one long scroll.

## 2. Tabs

Book mode of `BookDetailScreen` gets the shared tab family (`Button.tab`, sliding underline automatic, `Replace.On` fade for the panel): **Overview · Chapters · Notes**. The hero and band stay above the tabs. Overview holds today's cards (progress, "Your shelf") plus the dashboard; Chapters holds the chapter list with read state; Notes holds the hub. PDFs hide Chapters (they have none) and show the captured-regions part of Notes. The active tab is session-only (**Assumed**). Series mode is unchanged.

## 3. Read button (replaces item 22)

The hero's primary action shows context: "Start reading", "Continue — Ch. 12 · 64%", or "Read again". The percentage is `LastProgressionFraction` for the open chapter combined with chapter position (`(LastChapterIndex + fraction) / ChapterCount`); PDFs show the page. The hero/backdrop itself is unchanged (already `DetailHero`/`DetailBand`).

## 4. Reading dashboard (Overview)

Computed by a new `BookReadingStatsResolver` (Data) from `ReadingEvent` rows with `ItemType = Novel` and `ItemId = Book.Id`:
- sessions (count of Opened events), total active time (sum of `ActiveSeconds`), first and last read dates;
- pace in characters per minute from sessions that have both `PagesRead` (estimated pages × 1800) and `ActiveSeconds` ≥ 60;
- "about 3h 10m left" = remaining characters (`CharacterCount × (1 − progress)`) ÷ pace; shown only with ≥ 2 timed sessions and a known `CharacterCount`; rounded to 5 minutes, hidden when Finished.
Sessions with null `ActiveSeconds` are ignored for pace but counted as sessions. Rendered as stat tiles in the existing Insights tile style, not charts (**Assumed**).

## 5. Chapters tab

`BookDetailScreenViewModel` already parses the EPUB once for the TOC. Rows gain a state: Read (index < `LastChapterIndex`, or all when Finished), Current (a small progress bar from `LastProgressionFraction`), Unread; and a length bar from each chapter's character count relative to the longest. Part titles group rows as today. Clicking a row opens the reader at that chapter (existing `startAt`). FB2/MOBI without a hierarchy show a flat list. PDF: tab hidden.

## 6. Notes tab

One list of highlights, bookmarks and (PDF) captured regions in book order (chapter index, then block position), each row with a colour dot (highlights), excerpt, chapter title, and date. A colour filter row (`Button.filterChip`) and a kind filter (All/Highlights/Bookmarks/Regions). Clicking a row opens the reader at that location (`BookPosition` from the stored `BlockId`); each row has Delete (deferred one dispatcher tick, per CLAUDE.md) and the tab has Export (existing `AnnotationExportService`: Markdown/CSV/JSON). Read and navigate only: writing notes on highlights is Slice 3.

## 7. Author view (filter, not a screen)

The author name on the hero becomes a link: it opens the Books grid with an **Author: X** chip active (a new filter dimension next to the Shelves chip), the 2a status tabs showing counts within that author, and a header strip "N books · M finished" with a thin progress bar. Clearing the chip returns to the full grid. Authors are free text today (case-insensitive exact match; no author entity or alias merging in this slice, **Assumed**).

## 8. Data and settings

No schema change. `BookReadingStatsResolver` and the chapter state are read-only computations. Keyboard: the tab strip uses Ctrl+PageUp/PageDown through `TabStrip.Step`; no new hotkeys (**Assumed**).

## 9. Tests and verification

Data.Tests: stats resolver (pace ignoring short or untimed sessions, estimate gating, rounding, finished books, deleted books). App.Tests: Read button label cases, chapter state mapping, notes ordering/filtering/export, author filter chip and counts, tab visibility per format. Lint trio, render check (light/dark/Matrix), then on screen at 1366×768.

## 10. Out of scope

Editions panel (needs a "same work" concept; revisit after the 2a duplicate check), editing notes, author entity/aliases, charts on the dashboard, Detail hero changes.
