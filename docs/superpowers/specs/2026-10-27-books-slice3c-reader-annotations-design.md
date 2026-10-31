# Books improvement arc — Slice 3c: annotation polish (design)

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding). Pitch item 28, reduced by the Slice 3 grilling round (2026-10-27): no margin-notes rail, no PDF text highlighting, tags on highlights deferred. **Assumed** = not asked.

## 1. Goal

Make a highlight's note visible while reading and in the highlights drawer. Notes already work: `BookHighlight.Note` is stored, the reader's highlight popup edits it (`HighlightPopupNote`), and Book Detail's Notes tab lists it.

## 2. A note marker in the text

A highlight with a non-empty note shows a small marker at its end in the reading pane: a short dot-and-bar glyph drawn by the page's injected CSS (`::after` on the highlight span), tinted to the highlight colour, non-selectable and not part of the text content (so copy, search and block offsets are unaffected). Clicking it opens the same highlight popup the highlight itself opens (the existing click handler; the marker only enlarges the hit target by a few pixels). It is part of the existing `HighlightScript` / `pb-color-*` classes, with a `pb-has-note` class added when `Note` is set.

## 3. Highlights drawer

Each row in the reader's Highlights drawer (`ReaderListDrawer`) shows the note as a second, muted line under the excerpt (two lines, ellipsis), italic, only when present. No new controls; the row already opens the highlight popup for editing.

## 4. Tests and verification

- View-model: a highlight row carries its note (`BookHighlightSummary.Note` already exists) and a `HasNote` flag; saving the popup with text then clearing it removes the marker flag.
- Script generation: the injected highlight payload includes `pb-has-note` exactly for highlights with a note (the script text is built in code, so this is unit-testable without the WebView).
- On screen (WebView): marker visible and clickable in light/dark/sepia/OLED themes, does not shift line breaks, survives font-size changes.

## 5. Out of scope

Margin-notes rail, tags on highlights, PDF text highlighting (the PDF reader is image-based; region capture stays), note search.
